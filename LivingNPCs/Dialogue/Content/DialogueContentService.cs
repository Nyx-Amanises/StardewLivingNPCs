using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue.Engine;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 对 WP10 的唯一内容门面（WP15 §5）：内聚传记加载链（§4.3）、世界摘要选择与合并（§4.4）、
/// 提示词表（§4.2）与全部缓存/失效。游戏耦合经 <see cref="IContentPipeline"/> 注入，可整链单测。
/// </summary>
internal sealed class DialogueContentService : IDialogueContent
{
    private static readonly string[] BasePortraits = { "h", "s", "l", "a" };

    private readonly IContentPipeline pipeline;
    private readonly PromptTable promptTable;
    private readonly WorldSummaryRenderer renderer;
    private readonly object gate = new();

    private readonly Dictionary<string, CachedBio> bioCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly WorldSummary?[] mergedSummaries = new WorldSummary?[4];
    private readonly string?[] renderedSummaries = new string?[4];

    /// <summary>游戏侧全局实例；由 DialogueContentSetup 装配。测试各自构造。</summary>
    public static DialogueContentService? Instance { get; internal set; }

    public DialogueContentService(IContentPipeline pipeline)
    {
        this.pipeline = pipeline;
        this.promptTable = new PromptTable(pipeline);
        this.renderer = new WorldSummaryRenderer(
            key => this.promptTable.Lookup(key, returnNull: true) ?? string.Empty,
            DialogueServices.Monitor);
        Util.PromptFallback = (key, tokens) => this.promptTable.Lookup(key, tokens: tokens, returnNull: true);
    }

    internal PromptTable Prompts => this.promptTable;

    private sealed class CachedBio
    {
        public NpcBio Bio { get; init; } = null!;

        /// <summary>加载时的 SVE 兼容开关值（§4.3.5：开关翻转即重载）。</summary>
        public bool SveCompatibilityAtLoad { get; init; }
    }

    // ---- IDialogueContent ----

    public NpcBio GetBio(string npcName)
    {
        string normalized = SveContentRules.NormalizeName(npcName);
        bool sveSwitch = DialogueServices.Config?.EnableSveCompatibility ?? true;

        lock (this.gate)
        {
            if (this.bioCache.TryGetValue(normalized, out CachedBio? cached)
                && cached.SveCompatibilityAtLoad == sveSwitch
                && (!string.IsNullOrWhiteSpace(cached.Bio.Biography) || cached.Bio.Missing))
            {
                return cached.Bio;
            }
        }

        NpcBio bio = this.LoadBio(normalized, sveSwitch);
        lock (this.gate)
        {
            this.bioCache[normalized] = new CachedBio { Bio = bio, SveCompatibilityAtLoad = sveSwitch };
        }

        return bio;
    }

    public WorldSummary GetWorldSummary()
    {
        bool optimized = DialogueServices.Config?.UseOptimizedPrompts ?? false;
        return this.GetMergedSummary(optimized);
    }

    public string GetPromptSkeleton(string key, PromptVariant variant = default)
    {
        return this.LookupPrompt(key, npc: null, variant, tokens: null, returnNull: false) ?? string.Empty;
    }

    // ---- 内部富接口（WP10 消费） ----

    /// <summary>带传记覆盖与 token 的完整查找（§4.2 三层规则 + Optimized 变体）。</summary>
    public string? LookupPrompt(string key, NpcBio? npc, PromptVariant variant, object? tokens, bool returnNull)
    {
        if (variant.Optimized)
        {
            // 变体命名遵循交付的提示词表："<key>Optimized"（无点号），与 .MaleNpc/.FemaleNpc 的点号风格不同。
            string? optimizedValue = this.promptTable.Lookup(
                key + "Optimized", variant.NpcGender, npc?.PromptOverrides, tokens, returnNull: true);
            if (optimizedValue != null)
            {
                return optimizedValue;
            }
        }

        return this.promptTable.Lookup(key, variant.NpcGender, npc?.PromptOverrides, tokens, returnNull);
    }

    /// <summary>渲染成字符串的世界摘要，四个变体（完整/精简 × 含 SVE/纯净）各自缓存（§4.4）。</summary>
    public string GetWorldSummaryText(bool optimized)
    {
        int slot = this.SummarySlot(optimized);
        lock (this.gate)
        {
            if (this.renderedSummaries[slot] != null)
            {
                return this.renderedSummaries[slot]!;
            }
        }

        string text = this.renderer.Render(this.GetMergedSummary(optimized));
        lock (this.gate)
        {
            this.renderedSummaries[slot] = text;
        }

        return text;
    }

    /// <summary>性别名词（generalMale/generalFemale 的本地化值）；None 返回空串。</summary>
    public string GetGenderNoun(PromptGender gender)
    {
        return gender switch
        {
            PromptGender.Male => this.promptTable.Lookup("generalMale", returnNull: true) ?? string.Empty,
            PromptGender.Female => this.promptTable.Lookup("generalFemale", returnNull: true) ?? string.Empty,
            _ => string.Empty
        };
    }

    /// <summary>性别代词组（generalHe/She/Him/Her/His/Hers，裁决 4：改从提示词表取）。</summary>
    public (string Subject, string Object, string Possessive) GetPronouns(PromptGender gender)
    {
        string Get(string maleKey, string femaleKey)
        {
            return gender switch
            {
                PromptGender.Male => this.promptTable.Lookup(maleKey, returnNull: true) ?? string.Empty,
                PromptGender.Female => this.promptTable.Lookup(femaleKey, returnNull: true) ?? string.Empty,
                _ => string.Empty
            };
        }

        return (Get("generalHe", "generalShe"), Get("generalHim", "generalHer"), Get("generalHis", "generalHers"));
    }

    // ---- 失效（§4.1） ----

    /// <summary>Prompts 资产失效（含语言切换、patch reload）。</summary>
    public void InvalidatePrompts()
    {
        this.promptTable.Invalidate();
        this.InvalidateWorldSummaries();
    }

    /// <summary>世界摘要资产失效：四缓存全部作废。</summary>
    public void InvalidateWorldSummaries()
    {
        lock (this.gate)
        {
            Array.Clear(this.mergedSummaries);
            Array.Clear(this.renderedSummaries);
        }
    }

    /// <summary>单个传记资产失效。</summary>
    public void InvalidateBio(string npcName)
    {
        lock (this.gate)
        {
            this.bioCache.Remove(SveContentRules.NormalizeName(npcName));
        }
    }

    /// <summary>语言切换等全量失效。</summary>
    public void InvalidateAll()
    {
        this.InvalidatePrompts();
        lock (this.gate)
        {
            this.bioCache.Clear();
        }
    }

    // ---- 加载链 ----

    private bool SveActive => this.pipeline.IsSveLoaded && (DialogueServices.Config?.EnableSveCompatibility ?? true);

    private int SummarySlot(bool optimized)
    {
        return (optimized ? 1 : 0) | (this.SveActive ? 2 : 0);
    }

    private WorldSummary GetMergedSummary(bool optimized)
    {
        int slot = this.SummarySlot(optimized);
        lock (this.gate)
        {
            if (this.mergedSummaries[slot] != null)
            {
                return this.mergedSummaries[slot]!;
            }
        }

        WorldSummary baseSummary = this.pipeline.LoadWorldSummaryAsset(optimized) ?? new WorldSummary();
        WorldSummary merged = this.SveActive
            ? SveContentRules.MergeWorldDelta(baseSummary, this.pipeline.LoadSveWorldDelta(optimized))
            : baseSummary;

        lock (this.gate)
        {
            this.mergedSummaries[slot] = merged;
        }

        return merged;
    }

    private NpcBio LoadBio(string normalized, bool sveSwitch)
    {
        // §4.6 挂载点 ①：被 RSV 屏蔽的 NPC 不加载资产也不生成回退传记。
        if (RsvAiPolicy.IsBlockedNpcName(normalized))
        {
            return this.MissingBio(normalized, "blocked by the Ridgeside AI policy");
        }

        bool expansionCompatible = SveContentRules.IsExpansionCompatibilityEnabled(normalized, sveSwitch);
        NpcBio? bio = null;
        if (expansionCompatible)
        {
            bio = this.pipeline.LoadBioAsset(normalized);
        }
        else
        {
            // 判 false：即使传记资产存在也强制使用 Data/Characters 轻量回退传记（§4.4）。
            DialogueServices.Monitor?.Log($"[LivingNPCs] Expansion compatibility is off for '{normalized}'; using the lightweight fallback biography.", LogLevel.Debug);
        }

        if (bio == null || string.IsNullOrWhiteSpace(bio.Biography))
        {
            var characterData = this.pipeline.GetCharacterData(normalized);
            bio = characterData != null
                ? BioFallbackBuilder.Build(normalized, characterData)
                : this.MissingBio(normalized, "no biography asset and no Data/Characters entry");
        }
        else if (this.SveActive)
        {
            var patches = this.pipeline.LoadSveRelationshipPatches();
            if (patches != null && patches.TryGetValue(normalized, out var additions))
            {
                SveContentRules.ApplyRelationshipPatch(bio, additions);
            }
        }

        this.FinalizeBio(normalized, bio);
        return bio;
    }

    private NpcBio MissingBio(string normalized, string reason)
    {
        DialogueServices.Monitor?.Log($"[LivingNPCs] No biography for '{normalized}' ({reason}).", LogLevel.Warn);
        var bio = new NpcBio { Missing = true };
        this.FinalizeBio(normalized, bio);
        return bio;
    }

    /// <summary>加载后派生（§4.3 步骤 4）：肖像集、话题池、性别。</summary>
    private void FinalizeBio(string normalized, NpcBio bio)
    {
        if (!string.IsNullOrWhiteSpace(bio.Unique))
        {
            bio.ExtraPortraits["u"] = bio.Unique;
        }

        bio.ValidPortraits = new HashSet<string>(BasePortraits, StringComparer.OrdinalIgnoreCase);
        bio.ValidPortraits.UnionWith(bio.ExtraPortraits.Keys);

        bio.TopicPool = new List<string>(bio.Preoccupations);
        var giftNames = this.pipeline.GetGiftTasteNames(normalized);
        if (giftNames == null)
        {
            DialogueServices.Monitor?.Log($"[LivingNPCs] Gift taste data for '{normalized}' is missing or malformed; topic pool has no gift names.", LogLevel.Debug);
        }
        else
        {
            bio.TopicPool.AddRange(giftNames.Value.Loved);
            bio.TopicPool.AddRange(giftNames.Value.Hated);
        }

        bio.TopicPool = bio.TopicPool
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bio.ResolvedGender = this.ResolveGender(normalized, bio);
    }

    /// <summary>性别：游戏数据推导为主；Gender 资产字段仅当匹配 generalMale/generalFemale 本地化值时覆盖（裁决 4）。</summary>
    private PromptGender ResolveGender(string normalized, NpcBio bio)
    {
        PromptGender fromGame = this.pipeline.GetCharacterData(normalized)?.Gender switch
        {
            StardewValley.Gender.Male => PromptGender.Male,
            StardewValley.Gender.Female => PromptGender.Female,
            _ => PromptGender.None
        };

        if (!string.IsNullOrWhiteSpace(bio.Gender))
        {
            string male = this.promptTable.Lookup("generalMale", returnNull: true) ?? string.Empty;
            string female = this.promptTable.Lookup("generalFemale", returnNull: true) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(male) && string.Equals(bio.Gender.Trim(), male.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return PromptGender.Male;
            }

            if (!string.IsNullOrWhiteSpace(female) && string.Equals(bio.Gender.Trim(), female.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return PromptGender.Female;
            }
        }

        return fromGame;
    }
}
