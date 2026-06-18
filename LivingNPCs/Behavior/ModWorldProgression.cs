using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class ModWorldProgression
{
    public static ModWorldProgressSnapshot Current(Farmer farmer)
    {
        var sve = ModCompatibility.EnableSve ? BuildSveProgress(farmer) : SveWorldProgressSnapshot.NotInstalled;
        return new ModWorldProgressSnapshot(
            sve,
            BuildPromptLabel(sve),
            BuildDebugLabel(sve),
            BuildReplyGuidance(sve)
        );
    }

    public static ModWorldProgressKnowledgeSnapshot ForNpc(
        NPC npc,
        NpcDispositionProfile disposition,
        ModWorldProgressSnapshot progress,
        IReadOnlyCollection<string> attitudeTraits)
    {
        bool isSveNpc = ModCompatibility.EnableSve && ModCompatibility.IsSveSource(disposition.SourceLabel);
        var promptParts = new List<string>();
        var debugParts = new List<string>();
        var guidanceParts = new List<string>();

        if (isSveNpc && progress.Sve.Installed)
        {
            promptParts.Add($"SVE personal context: {BuildSveNpcPrompt(progress.Sve, npc, attitudeTraits)}");
            debugParts.Add($"SVE：{BuildSveNpcDebug(progress.Sve, npc, attitudeTraits)}");
            guidanceParts.Add("For SVE story/location references, this NPC may treat confirmed SVE milestones as part of their own world; do not mention unconfirmed SVE milestones as completed.");
        }

        if (promptParts.Count == 0)
        {
            promptParts.Add(progress.HasAnyInstalled
                ? "expansion-specific progress exists only as distant background for this NPC unless the conversation, location, or relationship makes it relevant"
                : "no supported expansion-specific progress is active");
            debugParts.Add(progress.HasAnyInstalled ? "扩展进度仅作远景背景" : "无扩展进度");
            guidanceParts.Add("Avoid bringing up expansion-specific story milestones unless this NPC belongs to that world or the current scene directly supports it.");
        }

        return new ModWorldProgressKnowledgeSnapshot(
            string.Join("; ", promptParts),
            string.Join("；", debugParts),
            string.Join(" ", guidanceParts)
        );
    }

    private static SveWorldProgressSnapshot BuildSveProgress(Farmer farmer)
    {
        bool installed = HasKnownCharacter("Claire")
            || HasKnownCharacter("Lance")
            || LocationExists("Custom_AuroraVineyard")
            || LocationExists("Custom_CrimsonBadlands");

        if (!installed)
        {
            return SveWorldProgressSnapshot.NotInstalled;
        }

        bool grandpaShedRestored = HasSeenEvent(farmer, "2554906")
            || HasVisited(farmer, "Custom_GrandpasShed", "Custom_GrandpasShedGreenhouse");
        bool applesKnown = HasSeenEvent(farmer, "7775927")
            || HasFriendshipRecord(farmer, "Apples")
            || HasVisited(farmer, "Custom_ApplesRoom", "Custom_Apples_WarpRoom");
        bool enchantedGroveKnown = HasSeenEvent(farmer, "103042015")
            || HasVisited(farmer, "Custom_EnchantedGrove", "Custom_Apples_WarpRoom");
        bool auroraVineyardRestored = HasVisited(farmer, "Custom_AuroraVineyardRefurbished", "Custom_AuroraVineyardCellarRefurbished");
        bool crimsonBadlandsVisited = HasVisited(farmer, "Custom_CrimsonBadlands", "Custom_CrimsonBadlandsMap")
            || HasMail(farmer, "SVE_ChallengingBadlands");
        bool castleOutpostVisited = HasVisited(farmer, "Custom_CastleVillageOutpost", "Custom_CastleVillage_DayEnd_WarpRoom");
        bool susanMet = HasFriendshipRecord(farmer, "Susan")
            || HasVisited(farmer, "Custom_SusanHouse");
        bool jojaEmporiumVisited = HasVisited(farmer, "Custom_JojaEmporium");

        return new SveWorldProgressSnapshot(
            true,
            grandpaShedRestored,
            applesKnown,
            enchantedGroveKnown,
            auroraVineyardRestored,
            crimsonBadlandsVisited,
            castleOutpostVisited,
            susanMet,
            jojaEmporiumVisited
        );
    }

    private static string BuildPromptLabel(SveWorldProgressSnapshot sve)
    {
        var parts = new List<string>();
        if (sve.Installed)
        {
            parts.Add($"SVE: {sve.PromptLabel}");
        }

        return parts.Count == 0
            ? "no supported expansion-specific progress detected"
            : string.Join("; ", parts);
    }

    private static string BuildDebugLabel(SveWorldProgressSnapshot sve)
    {
        var parts = new List<string>();
        if (sve.Installed)
        {
            parts.Add($"SVE：{sve.DebugLabel}");
        }

        return parts.Count == 0
            ? "无已识别扩展进度"
            : string.Join("；", parts);
    }

    private static string BuildReplyGuidance(SveWorldProgressSnapshot sve)
    {
        var parts = new List<string>();
        if (sve.Installed)
        {
            parts.Add("Only treat confirmed SVE milestones as completed; if an SVE milestone is unconfirmed, avoid claiming the farmer has already done it.");
        }

        return parts.Count == 0
            ? "No expansion-specific continuity constraints are active."
            : string.Join(" ", parts);
    }

    private static string BuildSveNpcPrompt(
        SveWorldProgressSnapshot sve,
        NPC npc,
        IReadOnlyCollection<string> attitudeTraits)
    {
        var parts = new List<string> { sve.PromptLabel };
        if (IsAny(npc.Name, "Apples", "Morgan", "Magnus", "Camilla") && sve.EnchantedGroveKnown)
        {
            parts.Add("magical SVE characters may treat enchanted grove access as meaningful and familiar");
        }

        if (IsAny(npc.Name, "Lance", "Alesia", "Isaac", "MarlonFay", "Gil") && (sve.CrimsonBadlandsVisited || sve.CastleVillageOutpostVisited))
        {
            parts.Add("adventurer-linked SVE characters may recognize the farmer's expanded danger map without overexplaining it");
        }

        if ((IsAny(npc.Name, "Andy", "Susan", "Sophia") || attitudeTraits.Contains("farming", StringComparer.OrdinalIgnoreCase))
            && (sve.GrandpasShedRestored || sve.AuroraVineyardRestored))
        {
            parts.Add("farm/vineyard-linked SVE characters may notice restoration and land stewardship as personally relevant");
        }

        if (IsAny(npc.Name, "Claire", "Martin", "Morris") && sve.JojaEmporiumVisited)
        {
            parts.Add("Joja-linked SVE characters may treat the Emporium as part of their lived work world, not a random shop");
        }

        return string.Join("; ", parts);
    }

    private static string BuildSveNpcDebug(
        SveWorldProgressSnapshot sve,
        NPC npc,
        IReadOnlyCollection<string> attitudeTraits)
    {
        var parts = new List<string> { sve.DebugLabel };
        if (IsAny(npc.Name, "Apples", "Morgan", "Magnus", "Camilla") && sve.EnchantedGroveKnown)
        {
            parts.Add("魔法线角色可感知魔法林地");
        }

        if (IsAny(npc.Name, "Lance", "Alesia", "Isaac", "MarlonFay", "Gil") && (sve.CrimsonBadlandsVisited || sve.CastleVillageOutpostVisited))
        {
            parts.Add("冒险线角色可感知高危区域进展");
        }

        if ((IsAny(npc.Name, "Andy", "Susan", "Sophia") || attitudeTraits.Contains("farming", StringComparer.OrdinalIgnoreCase))
            && (sve.GrandpasShedRestored || sve.AuroraVineyardRestored))
        {
            parts.Add("农场/葡萄园角色可感知修复进展");
        }

        if (IsAny(npc.Name, "Claire", "Martin", "Morris") && sve.JojaEmporiumVisited)
        {
            parts.Add("Joja 相关角色可感知 Emporium");
        }

        return string.Join("；", parts);
    }

    private static bool HasKnownCharacter(string name)
    {
        return Game1.characterData?.ContainsKey(name) == true;
    }

    private static bool LocationExists(string name)
    {
        try
        {
            return Game1.getLocationFromName(name) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasVisited(Farmer farmer, params string[] names)
    {
        return names.Any(name => farmer.locationsVisited.Contains(name));
    }

    private static bool HasSeenEvent(Farmer farmer, string eventId)
    {
        return farmer.eventsSeen.Contains(eventId);
    }

    private static bool HasMail(Farmer farmer, string mailKey)
    {
        return farmer.mailReceived.Contains(mailKey);
    }

    private static bool HasFriendshipRecord(Farmer farmer, string npcName)
    {
        return farmer.friendshipData.FieldDict.ContainsKey(npcName);
    }

    private static bool IsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record ModWorldProgressSnapshot(
    SveWorldProgressSnapshot Sve,
    string PromptLabel,
    string DebugLabel,
    string ReplyGuidance
)
{
    public bool HasAnyInstalled => this.Sve.Installed;
}

internal sealed record ModWorldProgressKnowledgeSnapshot(
    string PromptLabel,
    string DebugLabel,
    string ReplyGuidance
);

internal sealed record SveWorldProgressSnapshot(
    bool Installed,
    bool GrandpasShedRestored,
    bool ApplesKnown,
    bool EnchantedGroveKnown,
    bool AuroraVineyardRestored,
    bool CrimsonBadlandsVisited,
    bool CastleVillageOutpostVisited,
    bool SusanMet,
    bool JojaEmporiumVisited
)
{
    public static SveWorldProgressSnapshot NotInstalled { get; } = new(
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false,
        false
    );

    public string PromptLabel => string.Join(", ", this.BuildPromptParts());

    public string DebugLabel => string.Join("，", this.BuildDebugParts());

    private IEnumerable<string> BuildPromptParts()
    {
        yield return this.GrandpasShedRestored ? "Grandpa's Shed restored" : "Grandpa's Shed restoration not confirmed";
        yield return this.ApplesKnown ? "Apples known" : "Apples not confirmed known";
        yield return this.EnchantedGroveKnown ? "enchanted grove access known" : "enchanted grove access not confirmed";
        yield return this.AuroraVineyardRestored ? "Aurora Vineyard restored" : "Aurora Vineyard restoration not confirmed";
        yield return this.CrimsonBadlandsVisited ? "Crimson Badlands visited" : "Crimson Badlands visit not confirmed";
        yield return this.CastleVillageOutpostVisited ? "Castle Village outpost visited" : "Castle Village outpost visit not confirmed";
        yield return this.SusanMet ? "Susan met or her home visited" : "Susan access not confirmed";
        yield return this.JojaEmporiumVisited ? "Joja Emporium visited" : "Joja Emporium visit not confirmed";
    }

    private IEnumerable<string> BuildDebugParts()
    {
        yield return this.GrandpasShedRestored ? "爷爷的棚屋已修复" : "爷爷的棚屋未确认修复";
        yield return this.ApplesKnown ? "已认识 Apples" : "未确认认识 Apples";
        yield return this.EnchantedGroveKnown ? "已确认魔法林地" : "未确认魔法林地";
        yield return this.AuroraVineyardRestored ? "Aurora 葡萄园已修复" : "Aurora 葡萄园未确认修复";
        yield return this.CrimsonBadlandsVisited ? "已去过 Crimson Badlands" : "未确认 Crimson Badlands";
        yield return this.CastleVillageOutpostVisited ? "已去过 Castle Village 前哨" : "未确认 Castle Village 前哨";
        yield return this.SusanMet ? "已接触 Susan" : "未确认 Susan";
        yield return this.JojaEmporiumVisited ? "已去过 Joja Emporium" : "未确认 Joja Emporium";
    }
}
