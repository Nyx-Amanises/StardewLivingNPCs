using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// SVE 相关的静态规则与合并逻辑（WP15 §4.3.1/§4.4）。名字规整、别名表、兼容名单为兼容契约。
/// </summary>
internal static class SveContentRules
{
    public const string SveUniqueId = "FlashShifter.StardewValleyExpandedCP";

    /// <summary>SVE 内部 ID → 传记注册名别名表（§4.3.1）。</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GuntherSilvian"] = "Gunther",
        ["MarlonFay"] = "Marlon",
        ["MorrisTod"] = "Morris",
        ["HankSVE"] = "Hank"
    };

    /// <summary>
    /// 规范传记名 → 游戏数据查询候选。SVE 原始内部名优先于可能同名的原版条目；
    /// 规范名只作数据兜底，虚拟 Bio 资产本身仍始终使用规范名。
    /// </summary>
    private static readonly Dictionary<string, string[]> GameDataLookupNames = Aliases
        .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(
            group => group.Key,
            group => group
                .Select(pair => pair.Key)
                .Append(group.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>SVE 兼容名单（§4.4）：命中且 EnableSveCompatibility 关 → 扩展兼容判 false。</summary>
    private static readonly HashSet<string> SveCompatibilityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alesia", "Andy", "Apples", "Bear", "Camilla", "Charlie", "CharlieChicken", "Claire", "Dusty",
        "Gunther", "GuntherSilvian", "Isaac", "Jadu", "Jolyne", "Lance", "Magnus", "Martin", "Morgan",
        "Morris", "Olivia", "Scarlett", "Sophia", "Susan", "Victor", "Gil", "MarlonFay", "MorrisTod",
        "Marlon", "Hank", "HankSVE", "MrQi", "Qi"
    };

    /// <summary>
    /// 名字规整（§4.3.1）：去掉尾部 ·/•/-（多配偶类 mod 的克隆后缀），再查 SVE 别名表。
    /// </summary>
    public static string NormalizeName(string npcName)
    {
        string trimmed = (npcName ?? string.Empty).Trim().TrimEnd('·', '•', '-');
        return Aliases.TryGetValue(trimmed, out string? canonical) ? canonical : trimmed;
    }

    public static bool IsSveCharacter(string normalizedName)
    {
        return SveCompatibilityNames.Contains(normalizedName);
    }

    /// <summary>
    /// 返回游戏数据查询候选：已知别名用 SVE 原始内部名优先、规范名兜底；普通 NPC 只用规范名。
    /// 该顺序只服务游戏数据旁路，不改变公开虚拟资产键使用规范名的契约。
    /// </summary>
    public static IReadOnlyList<string> GetGameDataLookupNames(string npcName)
    {
        string normalized = NormalizeName(npcName);
        return GameDataLookupNames.TryGetValue(normalized, out string[]? candidates)
            ? candidates
            : new[] { normalized };
    }

    /// <summary>
    /// 每角色扩展兼容判定（旧名 IsExpansionCompatibilityEnabledForCharacter，§4.4）。
    /// 判 false 的后果：强制轻量回退传记 + 对话样本走净版加载。
    /// </summary>
    public static bool IsExpansionCompatibilityEnabled(string npcName, bool sveCompatibilityEnabled)
    {
        string normalized = NormalizeName(npcName);
        if (SveCompatibilityNames.Contains(normalized) && !sveCompatibilityEnabled)
        {
            return false;
        }

        return !RsvAiPolicy.IsBlockedNpcName(normalized);
    }

    /// <summary>
    /// 把 SVE 增量（world-sve 文件，形如只含 Locations/Villagers 的 WorldSummary）并入默认摘要，
    /// 键冲突以 SVE 侧为准（§4.4）。返回新对象，不改入参。
    /// </summary>
    public static WorldSummary MergeWorldDelta(WorldSummary baseSummary, WorldSummary? delta)
    {
        var merged = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool>(baseSummary.SectionOrder),
            Intro = CloneSection(baseSummary.Intro),
            FarmerBackground = CloneSection(baseSummary.FarmerBackground),
            Seasons = CloneSection(baseSummary.Seasons),
            Locations = CloneSection(baseSummary.Locations),
            Festivals = CloneSection(baseSummary.Festivals),
            Villagers = CloneSection(baseSummary.Villagers),
            Outro = CloneSection(baseSummary.Outro)
        };

        if (delta == null)
        {
            return merged;
        }

        MergeSection(merged.Locations, delta.Locations);
        MergeSection(merged.Villagers, delta.Villagers);
        MergeSection(merged.Seasons, delta.Seasons);
        MergeSection(merged.Festivals, delta.Festivals);
        return merged;
    }

    /// <summary>
    /// 把 SVE 关系增补（sve-relationship-patches 文件）并入传记的 Relationships。
    /// 已有键来自最终传记资产（含社区文件与 Content Patcher 编辑），优先于内置 SVE 增补。
    /// </summary>
    public static void ApplyRelationshipPatch(NpcBio bio, Dictionary<string, BioListEntry>? additions)
    {
        if (additions == null)
        {
            return;
        }

        foreach (var pair in additions)
        {
            bio.Relationships.TryAdd(pair.Key, pair.Value);
        }
    }

    private static void MergeSection(WorldSummarySection? target, WorldSummarySection? delta)
    {
        if (target == null || delta == null)
        {
            return;
        }

        foreach (var pair in delta.Entries)
        {
            target.Entries[pair.Key] = pair.Value;
        }
    }

    private static WorldSummarySection? CloneSection(WorldSummarySection? section)
    {
        if (section == null)
        {
            return null;
        }

        return new WorldSummarySection
        {
            Text = section.Text,
            Entries = new Dictionary<string, WorldSummaryEntry>(section.Entries)
        };
    }
}
