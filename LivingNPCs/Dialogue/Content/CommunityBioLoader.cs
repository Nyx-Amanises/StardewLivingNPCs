using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// <c>npc_bios/</c> 完整社区传记的纯逻辑加载器。它只负责候选路径、本地化回退和内容校验；
/// 实际磁盘读取由调用方注入，因此无需游戏即可单测。
/// </summary>
internal static class CommunityBioLoader
{
    public const long MaxFileBytes = 64 * 1024;

    private const int MaxBiographyLength = 16_384;
    private const int MaxLongTextLength = 4_096;
    private const int MaxDescriptionLength = 2_048;
    private const int MaxShortTextLength = 512;
    private const int MaxIdentifierLength = 128;
    private const int MaxCollectionEntries = 128;
    private const string PortableInvalidFileNameCharacters = "<>:\"/\\|?*";

    private static readonly string[] TopLevelFields =
    {
        "Biography",
        "Relationships",
        "Traits",
        "BiographyEnd",
        "Gender",
        "Unique",
        "ExtraPortraits",
        "Preoccupations",
        "Dialogue",
        "HomeLocationBed",
        "UsePatchedDialogue",
        "PromptOverrides",
    };

    private static readonly string[] EntryFields = { "id", "Heading", "Description" };

    private static readonly HashSet<string> ReservedJsonMetadataProperties = new(
        new[] { "$id", "$ref", "$type", "$values" },
        StringComparer.Ordinal);

    private static readonly HashSet<string> WindowsReservedDeviceNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(index => $"COM{index}"))
            .Concat(Enumerable.Range(1, 9).Select(index => $"LPT{index}")),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>候选文件的磁盘/解析结果；不把“不存在”与“存在但无效”混为同一个 null。</summary>
    internal enum BioFileReadStatus
    {
        Missing,
        Invalid,
        Valid,
    }

    /// <summary>严格读取结果。只有 <see cref="BioFileReadStatus.Valid"/> 携带 <see cref="NpcBio"/>。</summary>
    internal sealed record BioFileReadResult(
        BioFileReadStatus Status,
        NpcBio? Bio,
        string? Problem)
    {
        public static BioFileReadResult Missing() => new(BioFileReadStatus.Missing, null, null);

        public static BioFileReadResult Invalid(string problem) =>
            new(
                BioFileReadStatus.Invalid,
                null,
                string.IsNullOrWhiteSpace(problem) ? "the file is invalid" : problem);

        public static BioFileReadResult Valid(NpcBio bio) =>
            new(BioFileReadStatus.Valid, bio ?? throw new ArgumentNullException(nameof(bio)), null);
    }

    /// <summary>一次加载中被语义校验拒绝的候选文件。</summary>
    internal sealed record ValidationFailure(string RelativePath, string Problem);

    /// <summary>命中的传记、来源路径，以及命中前被跳过的无效候选。</summary>
    internal sealed record LoadResult(
        NpcBio? Bio,
        string? RelativePath,
        IReadOnlyList<ValidationFailure> InvalidCandidates);

    /// <summary>按来源 Mod 命名空间加载后的唯一命中或冲突结果。</summary>
    internal sealed record ScopedLoadResult(
        NpcBio? Bio,
        string? RelativePath,
        IReadOnlyList<ValidationFailure> InvalidCandidates,
        IReadOnlyList<string> ConflictingPaths);

    /// <summary>
    /// 按 exact locale → 父语言 → 默认文件读取。Missing 直接继续；Invalid 记录失败后继续；
    /// Valid 还要通过语义校验才会命中。
    /// </summary>
    public static LoadResult Load(
        string rootDirectory,
        string npcName,
        string locale,
        Func<string, BioFileReadResult> readBio)
    {
        ArgumentNullException.ThrowIfNull(readBio);

        return LoadCandidates(CandidatePaths(rootDirectory, npcName, locale), readBio);
    }

    /// <summary>
    /// 加载仅在目标 Mod 已激活时由调用方传入的命名空间目录。只有成功解析且通过语义校验的来源才计入冲突；
    /// 无效来源会被记录但不会阻断另一份唯一有效的来源。多个有效激活来源命中时 fail closed。
    /// </summary>
    public static ScopedLoadResult LoadScopedSources(
        IEnumerable<string> activeSourceRoots,
        string npcName,
        string locale,
        Func<string, BioFileReadResult> readBio)
    {
        ArgumentNullException.ThrowIfNull(activeSourceRoots);
        ArgumentNullException.ThrowIfNull(readBio);

        var invalid = new List<ValidationFailure>();
        var matches = new List<LoadResult>();
        foreach (string sourceRoot in activeSourceRoots
                     .Where(root => !string.IsNullOrWhiteSpace(root))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(root => root, StringComparer.OrdinalIgnoreCase))
        {
            LoadResult result = LoadCandidates(ScopedCandidatePaths(sourceRoot, npcName, locale), readBio);
            invalid.AddRange(result.InvalidCandidates);
            if (result.Bio != null)
            {
                matches.Add(result);
            }
        }

        if (matches.Count == 1)
        {
            return new ScopedLoadResult(matches[0].Bio, matches[0].RelativePath, invalid, Array.Empty<string>());
        }

        return new ScopedLoadResult(
            null,
            null,
            invalid,
            matches.Count > 1
                ? matches.Select(match => match.RelativePath!).ToArray()
                : Array.Empty<string>());
    }

    private static LoadResult LoadCandidates(
        IEnumerable<string> candidatePaths,
        Func<string, BioFileReadResult> readBio)
    {
        var invalid = new List<ValidationFailure>();
        foreach (string relativePath in candidatePaths)
        {
            BioFileReadResult? readResult = readBio(relativePath);
            if (readResult == null)
            {
                invalid.Add(new ValidationFailure(relativePath, "the biography reader returned no result"));
                continue;
            }

            if (readResult.Status == BioFileReadStatus.Missing)
            {
                continue;
            }

            if (readResult.Status == BioFileReadStatus.Invalid)
            {
                invalid.Add(new ValidationFailure(
                    relativePath,
                    string.IsNullOrWhiteSpace(readResult.Problem) ? "the file is invalid" : readResult.Problem));
                continue;
            }

            NpcBio? bio = readResult.Bio;
            if (readResult.Status != BioFileReadStatus.Valid || bio == null)
            {
                invalid.Add(new ValidationFailure(relativePath, "the biography reader returned an inconsistent result"));
                continue;
            }

            IReadOnlyList<string> problems = Validate(bio);
            if (problems.Count > 0)
            {
                invalid.Add(new ValidationFailure(relativePath, string.Join("; ", problems)));
                continue;
            }

            return new LoadResult(bio, relativePath, invalid);
        }

        return new LoadResult(null, null, invalid);
    }

    /// <summary>
    /// 解析社区 Bio 的严格 JSON 子集。禁止 Json.NET 兼容语法和强制转型，并且仅接受精确大小写的契约字段。
    /// </summary>
    internal static BioFileReadResult ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return BioFileReadResult.Invalid("the file is empty or whitespace");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });

            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return BioFileReadResult.Invalid("top-level JSON value must be an object");
            }

            string? structuralProblem = FindStructuralProblem(root, "$");
            if (structuralProblem != null)
            {
                return BioFileReadResult.Invalid(structuralProblem);
            }

            if (!TryBuildBio(root, out NpcBio? bio, out string? problem))
            {
                return BioFileReadResult.Invalid(problem!);
            }

            return BioFileReadResult.Valid(bio!);
        }
        catch (JsonException ex)
        {
            return BioFileReadResult.Invalid($"cannot parse strict JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            return BioFileReadResult.Invalid($"cannot parse biography fields: {ex.Message}");
        }
    }

    private static string? FindStructuralProblem(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    return $"duplicate JSON property '{property.Name}' at {path}";
                }

                if (ReservedJsonMetadataProperties.Contains(property.Name))
                {
                    return $"reserved JSON metadata property '{property.Name}' is not allowed at {path}";
                }

                string? childProblem = FindStructuralProblem(property.Value, $"{path}.{property.Name}");
                if (childProblem != null)
                {
                    return childProblem;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? childProblem = FindStructuralProblem(item, $"{path}[{index}]");
                if (childProblem != null)
                {
                    return childProblem;
                }

                index++;
            }
        }

        return null;
    }

    private static bool TryBuildBio(JsonElement root, out NpcBio? bio, out string? problem)
    {
        bio = null;
        problem = null;
        var result = new NpcBio();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonProperty property in root.EnumerateObject())
        {
            seen.Add(property.Name);
            switch (property.Name)
            {
                case "Biography":
                    if (!TryReadString(property.Value, property.Name, out string? biography, out problem))
                    {
                        return false;
                    }

                    result.Biography = biography!;
                    break;

                case "Relationships":
                    if (!TryReadEntryMap(property.Value, property.Name, out Dictionary<string, BioListEntry>? relationships, out problem))
                    {
                        return false;
                    }

                    result.Relationships = relationships!;
                    break;

                case "Traits":
                    if (!TryReadEntryMap(property.Value, property.Name, out Dictionary<string, BioListEntry>? traits, out problem))
                    {
                        return false;
                    }

                    result.Traits = traits!;
                    break;

                case "BiographyEnd":
                    if (!TryReadString(property.Value, property.Name, out string? biographyEnd, out problem))
                    {
                        return false;
                    }

                    result.BiographyEnd = biographyEnd!;
                    break;

                case "Gender":
                    if (!TryReadString(property.Value, property.Name, out string? gender, out problem))
                    {
                        return false;
                    }

                    result.Gender = gender!;
                    break;

                case "Unique":
                    if (!TryReadString(property.Value, property.Name, out string? unique, out problem))
                    {
                        return false;
                    }

                    result.Unique = unique!;
                    break;

                case "ExtraPortraits":
                    if (!TryReadStringMap(property.Value, property.Name, out Dictionary<string, string>? portraits, out problem))
                    {
                        return false;
                    }

                    result.ExtraPortraits = portraits!;
                    break;

                case "Preoccupations":
                    if (!TryReadStringList(property.Value, property.Name, out List<string>? preoccupations, out problem))
                    {
                        return false;
                    }

                    result.Preoccupations = preoccupations!;
                    break;

                case "Dialogue":
                    if (!TryReadStringMap(property.Value, property.Name, out Dictionary<string, string>? dialogue, out problem))
                    {
                        return false;
                    }

                    result.Dialogue = dialogue!;
                    break;

                case "HomeLocationBed":
                    if (!TryReadBoolean(property.Value, property.Name, out bool homeLocationBed, out problem))
                    {
                        return false;
                    }

                    result.HomeLocationBed = homeLocationBed;
                    break;

                case "PromptOverrides":
                    if (!TryReadStringMap(property.Value, property.Name, out Dictionary<string, string>? promptOverrides, out problem))
                    {
                        return false;
                    }

                    result.PromptOverrides = promptOverrides!;
                    break;

                case "UsePatchedDialogue":
                    if (!TryReadBoolean(property.Value, property.Name, out bool usePatchedDialogue, out problem))
                    {
                        return false;
                    }

                    result.UsePatchedDialogue = usePatchedDialogue;
                    break;

                default:
                    problem = UnknownFieldProblem("top level", property.Name, TopLevelFields);
                    return false;
            }
        }

        string[] missing = TopLevelFields.Where(field => !seen.Contains(field)).ToArray();
        if (missing.Length > 0)
        {
            problem = $"missing top-level fields: {string.Join(", ", missing)}";
            return false;
        }

        bio = result;
        return true;
    }

    private static bool TryReadString(
        JsonElement element,
        string field,
        out string? value,
        out string? problem)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            value = null;
            problem = $"{field} must be a string, got {JsonKindName(element.ValueKind)}";
            return false;
        }

        value = element.GetString();
        problem = null;
        return true;
    }

    private static bool TryReadBoolean(
        JsonElement element,
        string field,
        out bool value,
        out string? problem)
    {
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            value = false;
            problem = $"{field} must be a boolean, got {JsonKindName(element.ValueKind)}";
            return false;
        }

        value = element.GetBoolean();
        problem = null;
        return true;
    }

    private static bool TryReadEntryMap(
        JsonElement element,
        string field,
        out Dictionary<string, BioListEntry>? entries,
        out string? problem)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            entries = null;
            problem = $"{field} must be an object, got {JsonKindName(element.ValueKind)}";
            return false;
        }

        var result = new Dictionary<string, BioListEntry>();
        foreach (JsonProperty item in element.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.Object)
            {
                entries = null;
                problem = $"{field}['{item.Name}'] must be an object, got {JsonKindName(item.Value.ValueKind)}";
                return false;
            }

            var entry = new BioListEntry();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty member in item.Value.EnumerateObject())
            {
                if (member.Value.ValueKind != JsonValueKind.String)
                {
                    entries = null;
                    problem = $"{field}['{item.Name}'].{member.Name} must be a string, got {JsonKindName(member.Value.ValueKind)}";
                    return false;
                }

                switch (member.Name)
                {
                    case "id":
                        seen.Add(member.Name);
                        entry.Id = member.Value.GetString()!;
                        break;
                    case "Heading":
                        seen.Add(member.Name);
                        entry.Heading = member.Value.GetString()!;
                        break;
                    case "Description":
                        seen.Add(member.Name);
                        entry.Description = member.Value.GetString()!;
                        break;
                    default:
                        entries = null;
                        problem = UnknownFieldProblem($"{field}['{item.Name}']", member.Name, EntryFields);
                        return false;
                }
            }

            string[] missing = EntryFields.Where(member => !seen.Contains(member)).ToArray();
            if (missing.Length > 0)
            {
                entries = null;
                problem = $"{field}['{item.Name}'] is missing: {string.Join(", ", missing)}";
                return false;
            }

            result.Add(item.Name, entry);
        }

        entries = result;
        problem = null;
        return true;
    }

    private static bool TryReadStringMap(
        JsonElement element,
        string field,
        out Dictionary<string, string>? entries,
        out string? problem)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            entries = null;
            problem = $"{field} must be an object, got {JsonKindName(element.ValueKind)}";
            return false;
        }

        var result = new Dictionary<string, string>();
        foreach (JsonProperty item in element.EnumerateObject())
        {
            if (item.Value.ValueKind != JsonValueKind.String)
            {
                entries = null;
                problem = $"{field}['{item.Name}'] must be a string, got {JsonKindName(item.Value.ValueKind)}";
                return false;
            }

            result.Add(item.Name, item.Value.GetString()!);
        }

        entries = result;
        problem = null;
        return true;
    }

    private static bool TryReadStringList(
        JsonElement element,
        string field,
        out List<string>? entries,
        out string? problem)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            entries = null;
            problem = $"{field} must be an array, got {JsonKindName(element.ValueKind)}";
            return false;
        }

        var result = new List<string>();
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                entries = null;
                problem = $"{field}[{index}] must be a string, got {JsonKindName(item.ValueKind)}";
                return false;
            }

            result.Add(item.GetString()!);
            index++;
        }

        entries = result;
        problem = null;
        return true;
    }

    private static string UnknownFieldProblem(string scope, string actual, params string[] expected)
    {
        string? corrected = expected.FirstOrDefault(
            field => string.Equals(field, actual, StringComparison.OrdinalIgnoreCase));
        return corrected != null
            ? $"field '{actual}' in {scope} has incorrect casing; expected '{corrected}'"
            : $"unknown field '{actual}' in {scope}";
    }

    private static string JsonKindName(JsonValueKind kind)
    {
        return kind.ToString().ToLowerInvariant();
    }

    /// <summary>生成稳定、去重且不允许路径穿越的候选相对路径。</summary>
    public static IReadOnlyList<string> CandidatePaths(string rootDirectory, string npcName, string locale)
    {
        string root = NormalizeRoot(rootDirectory);
        if (!TryNormalizeNpcName(npcName, out string name) || string.IsNullOrWhiteSpace(root))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in LocaleDirectories(locale))
        {
            string localized = $"{root}/{directory}/{name}.json";
            if (seen.Add(localized))
            {
                paths.Add(localized);
            }
        }

        string fallback = $"{root}/{name}.json";
        if (seen.Add(fallback))
        {
            paths.Add(fallback);
        }

        return paths;
    }

    /// <summary>
    /// 命名空间布局：<c>&lt;sourceRoot&gt;/locales/&lt;locale&gt;/&lt;Npc&gt;.json</c>，
    /// 再回退到 <c>&lt;sourceRoot&gt;/bios/&lt;Npc&gt;.json</c>。
    /// </summary>
    public static IReadOnlyList<string> ScopedCandidatePaths(string sourceRoot, string npcName, string locale)
    {
        string root = NormalizeRoot(sourceRoot);
        if (!TryNormalizeNpcName(npcName, out string name) || string.IsNullOrWhiteSpace(root))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in LocaleDirectories(locale))
        {
            string localized = $"{root}/locales/{directory}/{name}.json";
            if (seen.Add(localized))
            {
                paths.Add(localized);
            }
        }

        string fallback = $"{root}/bios/{name}.json";
        if (seen.Add(fallback))
        {
            paths.Add(fallback);
        }

        return paths;
    }

    /// <summary>校验完整社区传记。空字符串兼容字段可为空，但任何契约字段都不可为 null。</summary>
    public static IReadOnlyList<string> Validate(NpcBio bio)
    {
        ArgumentNullException.ThrowIfNull(bio);

        var problems = new List<string>();
        RequireText(bio.Biography, "Biography", MaxBiographyLength, allowBlank: false, problems);
        RequireText(bio.BiographyEnd, "BiographyEnd", MaxLongTextLength, allowBlank: true, problems);
        RequireText(bio.Gender, "Gender", MaxIdentifierLength, allowBlank: true, problems);
        RequireText(bio.Unique, "Unique", MaxShortTextLength, allowBlank: true, problems);

        ValidateEntries(bio.Relationships, "Relationships", problems);
        ValidateEntries(bio.Traits, "Traits", problems);
        ValidateStringMap(bio.Dialogue, "Dialogue", MaxLongTextLength, problems);
        ValidateStringMap(bio.PromptOverrides, "PromptOverrides", MaxBiographyLength, problems);
        if (bio.UsePatchedDialogue)
        {
            problems.Add("UsePatchedDialogue must be false in npc_bios; use an explicitly authorized Content Patcher pack for this advanced override");
        }

        if (bio.PromptOverrides?.Count > 0)
        {
            problems.Add("PromptOverrides must be empty in npc_bios because community data may not replace trusted prompt sections");
        }

        if (bio.ExtraPortraits == null)
        {
            problems.Add("ExtraPortraits must be an object");
        }
        else
        {
            if (bio.ExtraPortraits.Count > MaxCollectionEntries)
            {
                problems.Add($"ExtraPortraits has more than {MaxCollectionEntries} entries");
            }

            var normalizedMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string? marker, string? description) in bio.ExtraPortraits)
            {
                string? normalized = PortraitMarkerRules.NormalizeExtraMarker(marker);
                if (normalized == null)
                {
                    problems.Add($"ExtraPortraits key '{marker}' must be 'u' or a decimal frame from 6 to {PortraitMarkerRules.MaxSupportedFrameIndex}");
                }
                else if (!normalizedMarkers.Add(normalized))
                {
                    problems.Add($"ExtraPortraits contains duplicate frame marker '{normalized}'");
                }

                RequireText(description, $"ExtraPortraits['{marker}']", MaxShortTextLength, allowBlank: false, problems);
            }
        }

        if (bio.Preoccupations == null)
        {
            problems.Add("Preoccupations must be an array");
        }
        else
        {
            if (bio.Preoccupations.Count > MaxCollectionEntries)
            {
                problems.Add($"Preoccupations has more than {MaxCollectionEntries} entries");
            }

            for (int i = 0; i < bio.Preoccupations.Count; i++)
            {
                RequireText(
                    bio.Preoccupations[i],
                    $"Preoccupations[{i}]",
                    MaxShortTextLength,
                    allowBlank: false,
                    problems);
            }
        }

        return problems;
    }

    private static void ValidateEntries(
        IReadOnlyDictionary<string, BioListEntry>? entries,
        string field,
        ICollection<string> problems)
    {
        if (entries == null)
        {
            problems.Add($"{field} must be an object");
            return;
        }

        if (entries.Count > MaxCollectionEntries)
        {
            problems.Add($"{field} has more than {MaxCollectionEntries} entries");
        }

        foreach ((string? key, BioListEntry? entry) in entries)
        {
            RequireText(key, $"{field} key", MaxIdentifierLength, allowBlank: false, problems);
            if (entry == null)
            {
                problems.Add($"{field}['{key}'] is null");
                continue;
            }

            RequireText(entry.Id, $"{field}['{key}'].id", MaxIdentifierLength, allowBlank: false, problems);
            RequireText(entry.Heading, $"{field}['{key}'].Heading", MaxShortTextLength, allowBlank: false, problems);
            RequireText(entry.Description, $"{field}['{key}'].Description", MaxDescriptionLength, allowBlank: false, problems);
        }
    }

    private static void ValidateStringMap(
        IReadOnlyDictionary<string, string>? entries,
        string field,
        int maxValueLength,
        ICollection<string> problems)
    {
        if (entries == null)
        {
            problems.Add($"{field} must be an object");
            return;
        }

        if (entries.Count > MaxCollectionEntries)
        {
            problems.Add($"{field} has more than {MaxCollectionEntries} entries");
        }

        foreach ((string? key, string? value) in entries)
        {
            RequireText(key, $"{field} key", MaxIdentifierLength, allowBlank: false, problems);
            RequireText(value, $"{field}['{key}']", maxValueLength, allowBlank: false, problems);
        }
    }

    private static void RequireText(
        string? value,
        string field,
        int maxLength,
        bool allowBlank,
        ICollection<string> problems)
    {
        if (value == null)
        {
            problems.Add($"{field} must be a string");
            return;
        }

        if (!allowBlank && string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{field} must not be blank");
            return;
        }

        if (value.EnumerateRunes().Count() > maxLength)
        {
            problems.Add($"{field} is longer than {maxLength} characters");
        }
    }

    private static string NormalizeRoot(string rootDirectory)
    {
        return (rootDirectory ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
    }

    private static string? NormalizeLocale(string locale)
    {
        string normalized = (locale ?? string.Empty).Trim().Replace('_', '-');
        string[] segments = normalized.Split('-', StringSplitOptions.None);
        if (segments.Length == 0
            || segments.Any(segment =>
                segment.Length is 0 or > 16
                || !segment.All(IsAsciiLetterOrDigit)))
        {
            return null;
        }

        segments[0] = segments[0].ToLowerInvariant();
        for (int index = 1; index < segments.Length; index++)
        {
            string segment = segments[index];
            if (segment.Length == 2 && segment.All(IsAsciiLetter))
            {
                segments[index] = segment.ToUpperInvariant();
            }
            else if (segment.Length == 4 && segment.All(IsAsciiLetter))
            {
                segments[index] = char.ToUpperInvariant(segment[0]) + segment[1..].ToLowerInvariant();
            }
        }

        return string.Join('-', segments);
    }

    private static IReadOnlyList<string> LocaleDirectories(string locale)
    {
        string? normalizedLocale = NormalizeLocale(locale);
        if (normalizedLocale == null)
        {
            return Array.Empty<string>();
        }

        string[] segments = normalizedLocale.Split('-');
        var directories = new List<string>();
        for (int length = segments.Length; length >= 1; length--)
        {
            directories.Add(string.Join('-', segments.Take(length)));
        }

        return directories;
    }

    private static bool TryNormalizeNpcName(string? npcName, out string name)
    {
        string rawName = npcName ?? string.Empty;
        name = rawName.Trim();
        return !string.IsNullOrWhiteSpace(name)
            && string.Equals(rawName, name, StringComparison.Ordinal)
            && IsSafePathSegment(name);
    }

    private static bool IsSafePathSegment(string value)
    {
        return value is not "." and not ".."
            && !value.EndsWith(".", StringComparison.Ordinal)
            && !value.EndsWith(" ", StringComparison.Ordinal)
            && !WindowsReservedDeviceNames.Contains(value.Split('.', 2)[0])
            && !value.Any(character =>
                char.IsControl(character)
                || character is '/' or '\\'
                || PortableInvalidFileNameCharacters.Contains(character)
                || Path.GetInvalidFileNameChars().Contains(character));
    }

    private static bool IsAsciiLetterOrDigit(char character)
    {
        return IsAsciiLetter(character) || character is >= '0' and <= '9';
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
    }
}
