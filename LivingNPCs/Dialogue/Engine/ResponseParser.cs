using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>解析产物：首行台词（净文本，含合法 Stardew 记号）+ 选项（净文本）。</summary>
internal sealed class ParsedResponse
{
    public bool Success { get; init; }
    public string DialogueLine { get; init; } = string.Empty;
    public List<string> Options { get; init; } = new();
    /// <summary>失败原因（诊断用）：empty / no-dialogue-line / overlong-segment。</summary>
    public string FailureReason { get; init; } = string.Empty;
}

/// <summary>
/// LLM 响应解析与校验（WP10 §4.10）。纯文本逻辑，可单测。
/// 长度限制：段 200 / 选项 90 / 选项候选 160。超长段句界拆分后仍超长 → 整行作废
/// （开放问题 3 裁决：采纳建议，删除永不可达的宽松校验分支，重试上限维持 2 次）。
/// </summary>
internal static class ResponseParser
{
    public const int MaxSegmentLength = 200;
    public const int MaxOptionLength = 90;
    public const int MaxOptionCandidateLength = 160;

    private static readonly string[] MetadataFieldNames =
    {
        "rapportDelta", "endConversation", "memories", "ambientFollowUp", "emotionImpact",
        "actions", "behaviorInfluences", "helpRequests", "helpRequestUpdates", "conflicts"
    };

    private static readonly Regex MetadataMarkerPattern = new(
        @"!+LIVINGNPCS_META",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JsonPropertyLinePattern = new(
        """^(?:[-*]\s*)?[\{\[]?\s*["“”]?[A-Za-z][A-Za-z0-9_]*["“”]?\s*["']?\s*:\s*""",
        RegexOptions.Compiled);
    private static readonly Regex JsonStructuralLinePattern = new(
        @"^[\s,]*[\}\]]+[\s,.;]*$|^```(?:json)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MidLineOptionPattern = new(@"(?<=\S)[ \t]+(?=%{1,2}[^\s%\d])", RegexOptions.Compiled);
    private static readonly Regex EmotionCommandFixPattern = new(@"#\$(?<token>[A-Za-z0-9]+)", RegexOptions.Compiled);
    private static readonly Regex DollarTokenPattern = new(@"\$(?<token>[A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex BareDollarPattern = new(@"\$(?![A-Za-z0-9_])", RegexOptions.Compiled);
    private static readonly Regex BracketCommandPattern = new(@"\[[^\]\r\n]*\]", RegexOptions.Compiled);
    private static readonly Regex PageMarkerPattern = new(@"(#\$[be]#)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitEnglishStrongJoyPattern = new(
        @"\b(?:(?:really|so|very|extremely|incredibly|truly|genuinely)\s+(?:happy|glad|excited)|overjoyed|thrilled|ecstatic|elated|(?:i(?:'m|\s+am|\s+feel)|we(?:'re|\s+are|\s+feel))\s+(?:delighted|joyful|excited)|(?:this|that|it)(?:'s|\s+is|\s+feels)\s+(?:really|so|very|extremely|incredibly|truly)\s+(?:exciting|wonderful)|i\s+(?:can't|cannot)\s+stop\s+(?:smiling|laughing)|i\s+(?:couldn't|could\s+not)\s+be\s+happier|(?:burst|broke)\s+out\s+laughing|(?:laughing|beaming)\s+with\s+joy|over\s+the\s+moon|on\s+cloud\s+nine|made\s+my\s+day|how\s+exciting)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OtherEnglishJoySubjectPattern = new(
        @"\b(?:(?:are\s+)?you(?:'re|\s+are|\s+seem|\s+look|\s+sound|\s+feel)?|he(?:'s|\s+is|\s+seems|\s+looks|\s+sounds|\s+feels)?|she(?:'s|\s+is|\s+seems|\s+looks|\s+sounds|\s+feels)?|they(?:'re|\s+are|\s+seem|\s+look|\s+sound|\s+feel)?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitChineseStrongJoyPattern = new(
        @"太开心|真的?很开心|真开心|非常开心|特别开心|好开心|开心极了|开心死了|高兴极了|非常高兴|特别高兴|欣喜若狂|喜出望外|乐开了花|兴奋|开怀|忍不住(?:笑|大笑)|笑得停不下来",
        RegexOptions.Compiled);
    private static readonly Regex OtherChineseJoySubjectPattern = new(
        @"(?:你|他|她|他们|她们)(?:看起来|显得|似乎|好像|是不是|一定|应该|居然|怎么)?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex OptionPrefixPattern = new(
        @"^(?:[-*•\d.、)）\s]*)?(?:玩家回应|玩家回复|回应|回复|选项|response|reply|option)\s*\d*\s*[:：.、\-]?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ListBulletPattern = new(@"^[\s*•\d]+[.、)）]?\s*", RegexOptions.Compiled);
    private static readonly string[] PleasantryOpeners = { "Here is", "Here's", "Sure", "以下", "当然" };

    public static ParsedResponse Parse(
        string rawText,
        IReadOnlySet<string> validPortraits,
        string farmerName,
        bool fixPunctuation,
        bool debug = false)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return new ParsedResponse { FailureReason = "empty" };
        }

        // 1. 原始归一化。
        string normalized = ConversationTextPostProcessor.RemoveInvisibleCharacters(rawText).Replace("\r", string.Empty);
        normalized = MetadataMarkerPattern.Replace(normalized, "\n!LIVINGNPCS_META");
        normalized = MidLineOptionPattern.Replace(normalized, "\n");

        // Hidden metadata is always a tail section. Discard the entire section before splitting
        // lines so pretty-printed JSON members like "type", "itemId", and "itemLabel" can never
        // be mistaken for unprefixed response choices. ConversationAnalysis parses the original
        // response separately, so this does not affect action or memory extraction.
        int metadataIndex = normalized.IndexOf(EngineConstants.MetadataMarker, StringComparison.OrdinalIgnoreCase);
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        // 2. 拆行、去空、剔除疑似元数据行。
        var lines = normalized
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Where(line => !IsMetadataLine(line))
            .ToList();

        // 3. 首行定位（找不到时宽容恢复）。
        int dialogueIndex = lines.FindIndex(line => line.StartsWith("-", StringComparison.Ordinal));
        if (dialogueIndex < 0)
        {
            int firstOption = lines.FindIndex(line => line.StartsWith("%", StringComparison.Ordinal));
            var candidates = (firstOption < 0 ? lines : lines.Take(firstOption))
                .Where(line => !PleasantryOpeners.Any(opener => line.StartsWith(opener, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            string? candidate = candidates.LastOrDefault();
            if (candidate == null || candidate.StartsWith("%", StringComparison.Ordinal))
            {
                return new ParsedResponse { FailureReason = "no-dialogue-line" };
            }

            if (debug)
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.lineRecovered",
                        new { line = candidate },
                        $"Dialogue line recovered without '-' prefix: {candidate}"),
                    StardewModdingAPI.LogLevel.Warn);
            }

            dialogueIndex = lines.IndexOf(candidate);
            lines[dialogueIndex] = "- " + candidate;
        }

        // 4. 台词行清洗。
        string dialogue = CleanDialogueLine(lines[dialogueIndex], validPortraits);
        if (dialogue.Length == 0)
        {
            return new ParsedResponse { FailureReason = "no-dialogue-line" };
        }

        // 5. 超长拆分（失败 → 整行作废）。
        string? split = SplitLongSegments(dialogue);
        if (split == null)
        {
            return new ParsedResponse { FailureReason = "overlong-segment" };
        }

        dialogue = split;

        // 6. 标点修补。
        if (fixPunctuation)
        {
            dialogue = FixSegmentPunctuation(dialogue, validPortraits);
        }

        // 7. 选项行。
        var options = new List<string>();
        foreach (string line in lines.Skip(dialogueIndex + 1))
        {
            string? option = TryParseOption(line, farmerName, fixPunctuation);
            if (option != null)
            {
                options.Add(option);
            }
        }

        return new ParsedResponse { Success = true, DialogueLine = dialogue, Options = options };
    }

    internal static bool IsMetadataLine(string line)
    {
        string stripped = line.TrimStart('{', ',', '"', ' ', '\t');
        return MetadataFieldNames.Any(field => stripped.StartsWith(field, StringComparison.OrdinalIgnoreCase))
            || JsonPropertyLinePattern.IsMatch(line)
            || JsonStructuralLinePattern.IsMatch(line.Trim());
    }

    internal static string CleanDialogueLine(string line, IReadOnlySet<string> validPortraits)
    {
        string text = StreamingDialoguePreview.StripHiddenAndResponseTail(line);
        text = text.TrimStart('-', ' ', '"', '“', '%');
        text = text.TrimEnd('"', '”', ' ');
        text = text.Replace("\"", string.Empty).Replace("“", string.Empty).Replace("”", string.Empty);
        text = ConversationTextPostProcessor.NormalizeStardewDialogueCommands(text);
        text = TrimPageMarkers(text);
        text = text.Replace("#$c .5#", string.Empty, StringComparison.Ordinal);
        text = text.Replace("@@", "@", StringComparison.Ordinal);
        text = BracketCommandPattern.Replace(text, string.Empty)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        // #$<肖像> → $<肖像>（$b/$e 已由 Normalize 处理为 #$b#/#$e#，不在此列）。
        text = EmotionCommandFixPattern.Replace(text, match =>
        {
            string token = match.Groups["token"].Value;
            if (token.Equals("b", StringComparison.OrdinalIgnoreCase)
                || token.Equals("e", StringComparison.OrdinalIgnoreCase))
            {
                return "#$" + token.ToLowerInvariant();
            }

            return "$" + token;
        });

        // 扫描全部 $ 记号：规范化后的分页命令与合法肖像键保留，其余整体删除。
        text = DollarTokenPattern.Replace(text, match =>
        {
            string token = match.Groups["token"].Value;
            if (token is "e" or "b")
            {
                return match.Value;
            }

            return TryGetAvailablePortrait(token, validPortraits, out string marker)
                ? "$" + marker
                : ShouldFallBackToNeutral(token) && validPortraits.Contains("0")
                    ? "$0"
                    : string.Empty;
        });

        // Stardew 每个 DialogueLine 只消费一个肖像标记；同一页保留多个不同标记时，
        // 未消费的 `$x` 会被 SpriteText 当成金币字形画出来。每页只保留最后一个标记，
        // 并把它移到页尾，确保游戏能稳定消费。
        text = NormalizePortraitMarkers(text, validPortraits);
        text = BareDollarPattern.Replace(text, string.Empty);

        return text.Trim();
    }

    internal static string NormalizePortraitMarkers(string text, IReadOnlySet<string> validPortraits)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string[] parts = PageMarkerPattern.Split(text);
        for (int i = 0; i < parts.Length; i++)
        {
            if (PageMarkerPattern.IsMatch(parts[i]))
            {
                continue;
            }

            var portraitMatches = DollarTokenPattern.Matches(parts[i])
                .Cast<Match>()
                .Select(match => new
                {
                    Marker = TryGetAvailablePortrait(match.Groups["token"].Value, validPortraits, out string marker)
                        ? marker
                        : null
                })
                .Where(item => item.Marker != null)
                .ToList();
            if (portraitMatches.Count == 0)
            {
                continue;
            }

            string lastPortrait = "$" + portraitMatches[^1].Marker;
            string visibleText = DollarTokenPattern.Replace(parts[i], match =>
                TryGetAvailablePortrait(match.Groups["token"].Value, validPortraits, out _)
                    ? string.Empty
                    : match.Value);
            visibleText = visibleText.TrimEnd();
            parts[i] = visibleText.Length == 0 ? string.Empty : visibleText + lastPortrait;
        }

        return string.Concat(parts);
    }

    private static string TrimPageMarkers(string text)
    {
        string trimmed = text.Trim();
        while (true)
        {
            Match first = PageMarkerPattern.Match(trimmed);
            if (!first.Success || first.Index != 0)
            {
                break;
            }

            trimmed = trimmed[first.Length..].TrimStart();
        }

        while (true)
        {
            MatchCollection matches = PageMarkerPattern.Matches(trimmed);
            if (matches.Count == 0)
            {
                break;
            }

            Match last = matches[^1];
            if (last.Index + last.Length != trimmed.Length)
            {
                break;
            }

            trimmed = trimmed[..last.Index].TrimEnd();
        }

        return trimmed;
    }

    /// <summary>按 #$b#/#$e# 分段；任一段 &gt;200 字符时在句界回溯切分；仍超长返回 null。</summary>
    internal static string? SplitLongSegments(string dialogue)
    {
        string[] parts = PageMarkerPattern.Split(dialogue);
        var rebuilt = new StringBuilder(dialogue.Length);
        string? pendingBoundary = null;
        foreach (string part in parts)
        {
            if (PageMarkerPattern.IsMatch(part))
            {
                pendingBoundary = part.Equals("#$e#", StringComparison.OrdinalIgnoreCase)
                    ? "#$e#"
                    : "#$b#";
                continue;
            }

            string trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            string portraitSuffix = ExtractPortraitSuffix(ref trimmed);
            int visibleLimit = MaxSegmentLength - portraitSuffix.Length;
            IEnumerable<string> pieces = trimmed.Length + portraitSuffix.Length <= MaxSegmentLength
                ? new[] { trimmed }
                : SplitAtSentences(trimmed, visibleLimit);

            bool firstPiece = true;
            foreach (string piece in pieces)
            {
                string rebuiltPiece = piece + portraitSuffix;
                if (rebuiltPiece.Length > MaxSegmentLength)
                {
                    return null;
                }

                if (rebuilt.Length > 0)
                {
                    rebuilt.Append(firstPiece && pendingBoundary != null ? pendingBoundary : "#$b#");
                }

                rebuilt.Append(rebuiltPiece);
                firstPiece = false;
            }

            pendingBoundary = null;
        }

        return rebuilt.ToString();
    }

    private static IEnumerable<string> SplitAtSentences(string segment, int maxLength)
    {
        if (maxLength <= 0)
        {
            return new[] { segment };
        }

        var pieces = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < segment.Length; i++)
        {
            current.Append(segment[i]);
            if (current.Length >= maxLength || !IsSentenceEnd(segment[i]))
            {
                continue;
            }

            // 段尾肖像记号粘附在所属句子后。
            int j = i + 1;
            while (j < segment.Length && segment[j] == '$')
            {
                int k = j + 1;
                while (k < segment.Length && char.IsLetterOrDigit(segment[k]))
                {
                    k++;
                }

                current.Append(segment[j..k]);
                i = k - 1;
                j = k;
            }

            pieces.Add(current.ToString().Trim());
            current.Clear();
        }

        if (current.Length > 0)
        {
            pieces.Add(current.ToString().Trim());
        }

        // 超长且无句界可切时，退回单块（由调用方判作废）。
        return pieces.Where(piece => piece.Length > 0);
    }

    private static string ExtractPortraitSuffix(ref string segment)
    {
        Match match = Regex.Match(segment, @"\$(?<token>[A-Za-z0-9_]+)\s*$");
        string? marker = match.Success
            ? PortraitMarkerRules.NormalizeGameMarker(match.Groups["token"].Value)
            : null;
        if (marker == null)
        {
            return string.Empty;
        }

        segment = segment[..match.Index].TrimEnd();
        return "$" + marker;
    }

    private static bool IsSentenceEnd(char c)
    {
        return c is '.' or '!' or '?' or '。' or '！' or '？';
    }

    /// <summary>每段在肖像记号前若不以句末标点结尾则补句号（§4.10.6）。</summary>
    internal static string FixSegmentPunctuation(string dialogue, IReadOnlySet<string> validPortraits)
    {
        string[] parts = PageMarkerPattern.Split(dialogue);
        for (int i = 0; i < parts.Length; i++)
        {
            if (PageMarkerPattern.IsMatch(parts[i]))
            {
                parts[i] = parts[i].Equals("#$e#", StringComparison.OrdinalIgnoreCase)
                    ? "#$e#"
                    : "#$b#";
                continue;
            }

            string segment = parts[i].TrimEnd();
            string portraitSuffix = string.Empty;
            var match = Regex.Match(segment, @"\$(?<token>[A-Za-z0-9_]+)\s*$");
            if (match.Success
                && TryGetAvailablePortrait(match.Groups["token"].Value, validPortraits, out string marker))
            {
                portraitSuffix = "$" + marker;
                segment = segment[..match.Index].TrimEnd();
            }

            if (segment.Length > 0 && !IsSentenceEnd(segment[^1]) && segment[^1] is not ('…' or '"' or '”'))
            {
                segment += ContainsCjk(segment) ? "。" : ".";
            }

            parts[i] = segment + portraitSuffix;
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// Fail closed when the final portrait texture changed after generation. The formatted line has
    /// already passed the normal parser, so preserve Stardew's page/menu/item commands verbatim and
    /// only replace tokens that can denote portrait frame indexes.
    /// </summary>
    internal static string DowngradePortraitMarkersToNeutral(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return DollarTokenPattern.Replace(text, match =>
        {
            string token = match.Groups["token"].Value;
            return PortraitMarkerRules.TryGetFrameIndex(token, out _)
                ? "$0"
                : match.Value;
        });
    }

    /// <summary>
    /// Corrects the standard angry alias after metadata evidence identifies a shy/flustered
    /// misclassification. Numeric and custom portrait markers are left untouched because their
    /// meanings depend on the final portrait sheet.
    /// </summary>
    internal static string DowngradeCanonicalAngryPortraitToNeutral(
        string text,
        Func<string, bool>? shouldNeutralizePage = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string[] parts = PageMarkerPattern.Split(text);
        for (int i = 0; i < parts.Length; i++)
        {
            if (PageMarkerPattern.IsMatch(parts[i]))
            {
                continue;
            }

            string visiblePage = DollarTokenPattern.Replace(parts[i], string.Empty).Trim();
            if (shouldNeutralizePage != null && !shouldNeutralizePage(visiblePage))
            {
                continue;
            }

            parts[i] = DollarTokenPattern.Replace(parts[i], match =>
                string.Equals(match.Groups["token"].Value, "a", StringComparison.OrdinalIgnoreCase)
                    ? "$0"
                    : match.Value);
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// Promotes a weak positive expression only when one page explicitly voices strong joy and the
    /// final portrait sheet exposes a reviewed, unambiguous stronger expression. Each page is
    /// evaluated independently; no NPC name, conventional marker, or portrait-pack layout is
    /// assumed.
    /// </summary>
    internal static string PromoteWeakPositivePortraitsForExplicitJoy(
        string text,
        IReadOnlyDictionary<string, string> portraitDescriptions)
    {
        if (string.IsNullOrEmpty(text) || portraitDescriptions.Count == 0)
        {
            return text ?? string.Empty;
        }

        var normalizedDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawMarker, string description) in portraitDescriptions)
        {
            string? marker = PortraitMarkerRules.NormalizeGameMarker(rawMarker);
            if (marker != null
                && !string.IsNullOrWhiteSpace(description)
                && !normalizedDescriptions.ContainsKey(marker))
            {
                normalizedDescriptions[marker] = description;
            }
        }

        var strongCandidate = normalizedDescriptions
            .Select(pair => new
            {
                pair.Key,
                Score = GetStrongJoyPortraitScore(pair.Value),
                FrameIndex = PortraitMarkerRules.TryGetFrameIndex(pair.Key, out int frameIndex)
                    ? frameIndex
                    : int.MaxValue
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.FrameIndex)
            .ThenBy(candidate => candidate.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (strongCandidate == null)
        {
            return text;
        }

        string[] parts = PageMarkerPattern.Split(text);
        for (int i = 0; i < parts.Length; i++)
        {
            if (PageMarkerPattern.IsMatch(parts[i]))
            {
                continue;
            }

            Match? currentPortrait = DollarTokenPattern.Matches(parts[i])
                .Cast<Match>()
                .LastOrDefault(match =>
                {
                    string? marker = PortraitMarkerRules.NormalizeGameMarker(match.Groups["token"].Value);
                    return marker != null && normalizedDescriptions.ContainsKey(marker);
                });
            if (currentPortrait == null)
            {
                continue;
            }

            string currentMarker = PortraitMarkerRules.NormalizeGameMarker(
                currentPortrait.Groups["token"].Value)!;
            string visiblePage = DollarTokenPattern.Replace(parts[i], string.Empty).Trim();
            if (!HasExplicitStrongJoyEvidence(visiblePage)
                || !IsWeakPositivePortraitDescription(normalizedDescriptions[currentMarker])
                || string.Equals(currentMarker, strongCandidate.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parts[i] = parts[i][..currentPortrait.Index]
                + "$" + strongCandidate.Key
                + parts[i][(currentPortrait.Index + currentPortrait.Length)..];
        }

        return string.Concat(parts);
    }

    internal static bool HasExplicitStrongJoyEvidence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (Match match in ExplicitEnglishStrongJoyPattern.Matches(text))
        {
            string prefix = text[..match.Index];
            string suffix = text[(match.Index + match.Length)..].TrimStart();
            if (!HasNegatedJoyPrefix(prefix)
                && !OtherEnglishJoySubjectPattern.IsMatch(prefix)
                && !StartsWithQuestionSuffix(suffix))
            {
                return true;
            }
        }

        foreach (Match match in ExplicitChineseStrongJoyPattern.Matches(text))
        {
            string prefix = text[..match.Index];
            string suffix = text[(match.Index + match.Length)..].TrimStart();
            if (!HasNegatedJoyPrefix(prefix)
                && !OtherChineseJoySubjectPattern.IsMatch(prefix)
                && !StartsWithQuestionSuffix(suffix))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsWeakPositivePortraitDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)
            || GetStrongJoyPortraitScore(description) > 0
            || HasMixedOrContextSpecificJoySemantics(description))
        {
            return false;
        }

        string[] fragments =
        {
            "gentle", "grateful", "warm", "content", "soft smile", "slight smile",
            "friendly", "pleasant", "pleased", "relieved", "fond", "affection",
            "amused smile", "reserved smile", "happy smile",
            "温柔", "感激", "温暖", "满足", "柔和", "淡淡", "友好", "愉悦",
            "释然", "亲切", "和善", "微笑"
        };
        return fragments.Any(fragment =>
            description.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsStrongJoyPortraitDescription(string description)
    {
        return GetStrongJoyPortraitScore(description) > 0;
    }

    private static int GetStrongJoyPortraitScore(string description)
    {
        if (string.IsNullOrWhiteSpace(description)
            || HasMixedOrContextSpecificJoySemantics(description))
        {
            return 0;
        }

        string normalized = description.ToLowerInvariant();
        if (normalized.Contains("joyful", StringComparison.Ordinal)
            || normalized.Contains("开心大笑", StringComparison.Ordinal)
            || normalized.Contains("欣喜大笑", StringComparison.Ordinal)
            || normalized.Contains("开怀大笑", StringComparison.Ordinal))
        {
            return 100;
        }

        if (normalized.Contains(" laugh", StringComparison.Ordinal)
            || normalized.StartsWith("laugh", StringComparison.Ordinal)
            || normalized.Contains("大笑", StringComparison.Ordinal))
        {
            return 95;
        }

        if (normalized.Contains("clearly happy", StringComparison.Ordinal)
            || normalized.Contains("delighted", StringComparison.Ordinal)
            || normalized.Contains("exuberant", StringComparison.Ordinal)
            || normalized.Contains("radiant", StringComparison.Ordinal)
            || normalized.Contains("beaming", StringComparison.Ordinal)
            || normalized.Contains("明确高兴", StringComparison.Ordinal)
            || normalized.Contains("欣喜", StringComparison.Ordinal)
            || normalized.Contains("灿烂", StringComparison.Ordinal))
        {
            return 90;
        }

        bool visiblyBrightSmile = (normalized.Contains("bright,", StringComparison.Ordinal)
                || normalized.Contains("bright ", StringComparison.Ordinal))
            && (normalized.Contains("smile", StringComparison.Ordinal)
                || normalized.Contains("laugh", StringComparison.Ordinal)
                || normalized.Contains("grin", StringComparison.Ordinal)
                || normalized.Contains("happy", StringComparison.Ordinal));
        if (normalized.Contains("cheerful", StringComparison.Ordinal)
            || normalized.Contains("excited", StringComparison.Ordinal)
            || visiblyBrightSmile
            || normalized.Contains("开朗", StringComparison.Ordinal)
            || normalized.Contains("兴奋", StringComparison.Ordinal)
            || normalized.Contains("明快", StringComparison.Ordinal))
        {
            return 80;
        }

        return 0;
    }

    private static bool HasMixedOrContextSpecificJoySemantics(string description)
    {
        string[] fragments =
        {
            "flustered", "embarrassed", "shy", "bashful", "anxious", "worried", "tearful",
            "crying", "distressed", "pained", "fear", "sad", "downcast", "somber", "subdued",
            "uneasy", "dejected", "ashamed", "lonely", "angry", "irritated", "displeased",
            "surprised", "startled", "shocked", "alarmed", "uncertain", "costume", "cosplay", "uniform",
            "shirtless", "mud-smeared", "holding", "telephone", "radio", "without glasses",
            "background", "害羞", "尴尬", "慌乱", "焦虑", "担心", "含泪", "哭", "痛苦",
            "害怕", "难过", "低落", "忧伤", "悲伤", "不安", "沮丧", "羞愧", "孤独",
            "生气", "愤怒", "恼怒", "不悦", "惊讶", "吃惊", "震惊", "惊慌", "不确定",
            "装扮", "制服", "赤裸", "泥污", "抱着", "电话", "耳机", "摘下眼镜", "背景"
        };
        return fragments.Any(fragment =>
            description.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasNegatedJoyPrefix(string prefix)
    {
        string trimmed = prefix.TrimEnd();
        return Regex.IsMatch(trimmed, @"\b(?:not|never|hardly)\s*$", RegexOptions.IgnoreCase)
            || trimmed.EndsWith("不", StringComparison.Ordinal)
            || trimmed.EndsWith("没", StringComparison.Ordinal)
            || trimmed.EndsWith("没有", StringComparison.Ordinal)
            || trimmed.EndsWith("别", StringComparison.Ordinal)
            || trimmed.EndsWith("不要", StringComparison.Ordinal);
    }

    private static bool StartsWithQuestionSuffix(string suffix)
    {
        return suffix.StartsWith("?", StringComparison.Ordinal)
            || suffix.StartsWith("？", StringComparison.Ordinal)
            || suffix.StartsWith("吗", StringComparison.Ordinal)
            || suffix.StartsWith("么", StringComparison.Ordinal);
    }

    private static bool TryGetAvailablePortrait(
        string token,
        IReadOnlySet<string> validPortraits,
        out string marker)
    {
        marker = PortraitMarkerRules.NormalizeGameMarker(token) ?? string.Empty;
        return marker.Length > 0 && validPortraits.Contains(marker);
    }

    private static bool ShouldFallBackToNeutral(string token)
    {
        string trimmed = token.Trim();
        if (trimmed.Length == 1
            && char.ToLowerInvariant(trimmed[0]) is 'h' or 's' or 'u' or 'l' or 'a')
        {
            return true;
        }

        if (!PortraitMarkerRules.TryGetFrameIndex(trimmed, out int frameIndex))
        {
            return false;
        }

        // Numeric 1-5 are deliberately treated as ordinary/unsafe dollar tokens. Custom numeric
        // portrait markers start at 6 and fall back to neutral when absent from the runtime sheet.
        return frameIndex == 0 || frameIndex >= 6;
    }

    private static bool ContainsCjk(string text)
    {
        return text.Any(c => c >= '一' && c <= '鿿');
    }

    /// <summary>选项行归一化（§4.10.7）。返回 null = 不是选项。</summary>
    internal static string? TryParseOption(string line, string farmerName, bool fixPunctuation)
    {
        string candidate;
        if (line.StartsWith("%", StringComparison.Ordinal))
        {
            candidate = line.TrimStart('%').Trim();
        }
        else
        {
            if (line.StartsWith("!", StringComparison.Ordinal)
                || line.StartsWith("{", StringComparison.Ordinal)
                || line.StartsWith("}", StringComparison.Ordinal)
                || line.StartsWith("-", StringComparison.Ordinal)
                || line.StartsWith("###", StringComparison.Ordinal)
                || IsMetadataLine(line))
            {
                return null;
            }

            candidate = OptionPrefixPattern.Replace(line, string.Empty);
            candidate = ListBulletPattern.Replace(candidate, string.Empty).Trim();
            if (candidate.Length == 0
                || candidate.Length > MaxOptionCandidateLength
                || candidate.Contains("#$", StringComparison.Ordinal)
                || candidate.Contains(EngineConstants.MetadataMarker, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (candidate.Length == 0)
        {
            return null;
        }

        // 选项清洗：删 #，删所有 $ 命令，@→农夫名，必要时补标点。
        candidate = candidate.Replace("#", string.Empty);
        candidate = DollarTokenPattern.Replace(candidate, string.Empty);
        candidate = candidate.Replace("$", string.Empty, StringComparison.Ordinal);
        candidate = candidate.Replace("@", farmerName ?? string.Empty, StringComparison.Ordinal);
        candidate = candidate.Trim().Trim('"', '“', '”').Trim();
        if (candidate.Length == 0)
        {
            return null;
        }

        if (fixPunctuation && !IsSentenceEnd(candidate[^1]) && candidate[^1] is not ('…' or '"' or '”'))
        {
            candidate += ContainsCjk(candidate) ? "。" : ".";
        }

        return candidate.Length > MaxOptionLength ? null : candidate;
    }
}
