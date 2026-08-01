using System;
using System.Collections.Generic;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 游戏/SMAPI 内容管线的可注入缝（WP15）。运行时实现为 <see cref="GameContentPipeline"/>；
/// 单元测试提供假实现即可测完整个加载/查找/合并/回退链。
/// </summary>
internal interface IContentPipeline
{
    /// <summary>完整 locale（如 zh-CN），不是仅语言码（§4.2 缓存键要求）。</summary>
    string CurrentLocale { get; }

    /// <summary>玩家性别缓存键（Game1.getPlayerOrEventFarmer()?.Gender 的字符串形态）。</summary>
    string PlayerGenderKey { get; }

    /// <summary>是否检测到 SVE（ModRegistry.IsLoaded，01 §4）。</summary>
    bool IsSveLoaded { get; }

    /// <summary>经内容管线加载 Prompts 资产（可被 CP EditData 覆盖后的最终字典）。异常上抛（§4.2 由调用方处理）。</summary>
    Dictionary<string, string> LoadPromptDictionary();

    /// <summary>游戏文本预处理（按玩家性别展开分支，Game1.content 预处理管线）。</summary>
    string PreprocessString(string text);

    /// <summary>经内容管线加载世界摘要资产（完整/精简）。异常时返回 null 并自行记日志。</summary>
    WorldSummary? LoadWorldSummaryAsset(bool optimized);

    /// <summary>经内容管线加载单个传记资产（LoadLocalized 挂载点）。异常时返回 null。</summary>
    NpcBio? LoadBioAsset(string normalizedName);

    /// <summary>
    /// 游戏 Data/Characters 条目；SVE 规范名会按原始内部名优先、规范名兜底查询，没有该 NPC 返回 null。
    /// </summary>
    CharacterData? GetCharacterData(string npcName);

    /// <summary>
    /// 解析 Game1.NPCGiftTastes 第 1、7 段的礼物显示名；SVE 规范名会按原始内部名优先、规范名兜底查询。
    /// 段数不足返回 null；没有个人条目可返回空集合。
    /// </summary>
    (IReadOnlyList<string> Loved, IReadOnlyList<string> Hated)? GetGiftTasteNames(string npcName);

    /// <summary>从磁盘读 SVE 世界增量文件；缺失/坏损返回 null。</summary>
    WorldSummary? LoadSveWorldDelta(bool optimized);

    /// <summary>从磁盘读 SVE 关系增补（locale 感知）；缺失/坏损返回 null。</summary>
    Dictionary<string, Dictionary<string, BioListEntry>>? LoadSveRelationshipPatches();
}

/// <summary>引擎的运行时关闭闸（§4.2：提示词表加载失败时置位；不写回 config.json）。</summary>
internal static class DialogueEngineGate
{
    /// <summary>true = 引擎在本次会话内关闭（WP10/WP12 生成入口须检查）。</summary>
    public static bool RuntimeDisabled { get; set; }
}

/// <summary>
/// 提示词骨架表（旧 PromptCache 的对应物，WP15 §4.2）。
/// 缓存按（完整 locale × 玩家性别）失效重建；值先滤 "(no translation" 再过游戏预处理。
/// </summary>
internal sealed class PromptTable
{
    private const string NoTranslationMarker = "(no translation";

    /// <summary>本包代码按名字直接消费的提示词键（§3.6 最小集合）；PromptKeyAudit 测试据此对表。</summary>
    internal static readonly string[] KeysReferencedByContentCode =
    {
        "generalAnd", "seasonCrops", "seasonForage", "gameSummaryTranslations",
        "generalMale", "generalFemale",
        "generalHe", "generalShe", "generalHim", "generalHer", "generalHis", "generalHers"
    };

    private readonly IContentPipeline pipeline;
    private readonly object gate = new();
    private readonly HashSet<string> warnedKeys = new(StringComparer.Ordinal);

    private Dictionary<string, string>? cache;
    private string cacheLocale = string.Empty;
    private string cacheGender = string.Empty;

    public PromptTable(IContentPipeline pipeline)
    {
        this.pipeline = pipeline;
    }

    /// <summary>资产失效/语言切换时调用；下次访问重建（§4.1）。</summary>
    public void Invalidate()
    {
        lock (this.gate)
        {
            this.cache = null;
        }
    }

    /// <summary>
    /// 三层查找（§4.2）：① 传记 PromptOverrides ② NPC 性别变体 &lt;key&gt;.MaleNpc/.FemaleNpc ③ 基础键。
    /// 命中后做 {{Token}} 替换。查不到：returnNull 时返回 null，否则每键只警告一次并返回 null。
    /// </summary>
    public string? Lookup(
        string key,
        PromptGender gender = PromptGender.None,
        IReadOnlyDictionary<string, string>? promptOverrides = null,
        object? tokens = null,
        bool returnNull = false)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return returnNull ? null : string.Empty;
        }

        if (promptOverrides != null
            && promptOverrides.TryGetValue(key, out string? overridden)
            && !string.IsNullOrWhiteSpace(overridden))
        {
            return ReplaceTokens(overridden, tokens);
        }

        Dictionary<string, string> table = this.EnsureCache();

        if (gender != PromptGender.None)
        {
            string variantKey = gender == PromptGender.Male ? key + ".MaleNpc" : key + ".FemaleNpc";
            if (table.TryGetValue(variantKey, out string? variantValue))
            {
                return ReplaceTokens(variantValue, tokens);
            }
        }

        if (table.TryGetValue(key, out string? value))
        {
            return ReplaceTokens(value, tokens);
        }

        if (!returnNull)
        {
            this.WarnOnce(key);
        }

        return null;
    }

    /// <summary>裸取键值（不带变体/警告），供 Util 的回退路径与审计用。</summary>
    public bool TryGetRaw(string key, out string value)
    {
        Dictionary<string, string> table = this.EnsureCache();
        return table.TryGetValue(key, out value!);
    }

    private Dictionary<string, string> EnsureCache()
    {
        string locale = this.pipeline.CurrentLocale;
        string genderKey = this.pipeline.PlayerGenderKey;
        lock (this.gate)
        {
            if (this.cache != null
                && string.Equals(this.cacheLocale, locale, StringComparison.Ordinal)
                && string.Equals(this.cacheGender, genderKey, StringComparison.Ordinal))
            {
                return this.cache;
            }

            this.cache = this.BuildCache();
            this.cacheLocale = locale;
            this.cacheGender = genderKey;
            return this.cache;
        }
    }

    private Dictionary<string, string> BuildCache()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string> raw;
        try
        {
            raw = this.pipeline.LoadPromptDictionary();
        }
        catch (Exception ex)
        {
            // §4.2：记两条错误日志并运行时关闭对话引擎（不写回 config.json），返回空缓存。
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.assetLoadFailed",
                    new { asset = ContentAssetNames.Prompts, error = ex.Message },
                    $"Failed to load content asset '{ContentAssetNames.Prompts}': {ex.Message}"),
                StardewModdingAPI.LogLevel.Error);
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.promptTableOff",
                    null,
                    "The AI dialogue engine is disabled for this session because its prompt table is unavailable."),
                StardewModdingAPI.LogLevel.Error);
            DialogueEngineGate.RuntimeDisabled = true;
            return result;
        }

        foreach (var pair in raw)
        {
            if (pair.Value == null || pair.Value.StartsWith(NoTranslationMarker, StringComparison.Ordinal))
            {
                continue;
            }

            string processed;
            try
            {
                processed = this.pipeline.PreprocessString(pair.Value);
            }
            catch
            {
                processed = pair.Value;
            }

            if (string.IsNullOrWhiteSpace(processed) || processed.StartsWith(NoTranslationMarker, StringComparison.Ordinal))
            {
                continue;
            }

            result[pair.Key] = processed;
        }

        return result;
    }

    private void WarnOnce(string key)
    {
        lock (this.gate)
        {
            if (!this.warnedKeys.Add(key))
            {
                return;
            }
        }

        // 键名打错会静默吞掉整块上下文，这条防线必须保留（§4.2）。
        DialogueServices.Monitor?.Log(
            Util.GetConsoleString(
                "dialogue.log.promptKeyMissing",
                new { key },
                $"Prompt skeleton key '{key}' was not found; that prompt section will be empty."),
            StardewModdingAPI.LogLevel.Warn);
    }

    /// <summary>{{Token}} 替换：token 名忽略大小写匹配；tokens 可为匿名对象或字符串字典
    /// （装配器合并"标准 token + 调用点 token"时用字典）。未知 token 原样保留。</summary>
    internal static string ReplaceTokens(string text, object? tokens)
    {
        if (tokens == null || string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tokens is IReadOnlyDictionary<string, string> map)
        {
            foreach (var pair in map)
            {
                values[pair.Key] = pair.Value;
            }
        }
        else
        {
            foreach (var property in tokens.GetType().GetProperties())
            {
                values[property.Name] = property.GetValue(tokens)?.ToString() ?? string.Empty;
            }
        }

        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\{\{\s*([\w.\-]+)\s*\}\}",
            match => values.TryGetValue(match.Groups[1].Value, out string? replacement) ? replacement : match.Value);
    }
}
