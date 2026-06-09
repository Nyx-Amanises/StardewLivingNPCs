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
        var rsv = ModCompatibility.EnableRsv ? BuildRsvProgress(farmer) : RsvWorldProgressSnapshot.NotInstalled;
        return new ModWorldProgressSnapshot(
            sve,
            rsv,
            BuildPromptLabel(sve, rsv),
            BuildDebugLabel(sve, rsv),
            BuildReplyGuidance(sve, rsv)
        );
    }

    public static ModWorldProgressKnowledgeSnapshot ForNpc(
        NPC npc,
        NpcDispositionProfile disposition,
        ModWorldProgressSnapshot progress,
        IReadOnlyCollection<string> attitudeTraits)
    {
        bool isSveNpc = ModCompatibility.EnableSve && ModCompatibility.IsSveSource(disposition.SourceLabel);
        bool isRsvNpc = ModCompatibility.EnableRsv && ModCompatibility.IsRsvSource(disposition.SourceLabel);
        var promptParts = new List<string>();
        var debugParts = new List<string>();
        var guidanceParts = new List<string>();

        if (isSveNpc && progress.Sve.Installed)
        {
            promptParts.Add($"SVE personal context: {BuildSveNpcPrompt(progress.Sve, npc, attitudeTraits)}");
            debugParts.Add($"SVE：{BuildSveNpcDebug(progress.Sve, npc, attitudeTraits)}");
            guidanceParts.Add("For SVE story/location references, this NPC may treat confirmed SVE milestones as part of their own world; do not mention unconfirmed SVE milestones as completed.");
        }

        if (isRsvNpc && progress.Rsv.Installed)
        {
            promptParts.Add($"Ridgeside personal context: {BuildRsvNpcPrompt(progress.Rsv, npc, attitudeTraits)}");
            debugParts.Add($"RSV：{BuildRsvNpcDebug(progress.Rsv, npc, attitudeTraits)}");
            guidanceParts.Add("For Ridgeside references, this NPC may treat confirmed RSV milestones as part of their own village life; do not mention unconfirmed RSV milestones as completed.");
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

    private static RsvWorldProgressSnapshot BuildRsvProgress(Farmer farmer)
    {
        bool installed = HasKnownCharacter("Lenny")
            || HasKnownCharacter("Flor")
            || LocationExists("Custom_Ridgeside_RidgesideVillage")
            || LocationExists("Custom_Ridgeside_RSVCableCar");

        if (!installed)
        {
            return RsvWorldProgressSnapshot.NotInstalled;
        }

        bool villageVisited = HasVisited(farmer, "Custom_Ridgeside_RidgesideVillage", "Custom_Ridgeside_RSVCliff", "Custom_Ridgeside_Ridge");
        bool cableCarUsed = HasVisited(farmer, "Custom_Ridgeside_RSVCableCar", "Custom_Ridgeside_RSVTheRide_static")
            || villageVisited;
        bool gatheringSeen = HasSeenEvent(farmer, "75160120")
            || HasVisited(farmer, "Custom_Ridgeside_RSVGathering");
        bool greenhouseRestored = HasSeenEvent(farmer, "75160174")
            || HasVisited(farmer, "Custom_Ridgeside_RSVGreenhouse1", "Custom_Ridgeside_RSVGreenhouse2");
        bool ridgeForestVisited = HasVisited(farmer, "Custom_Ridgeside_RidgeForest");
        bool spiritRealmKnown = HasVisited(farmer, "Custom_Ridgeside_RSVSpiritRealm");
        bool ninjaHouseKnown = HasVisited(farmer, "Custom_Ridgeside_RSVNinjaHouse", "Custom_Ridgeside_RSVHiddenWarp");
        bool undreyaKnown = HasSeenEvent(farmer, "75160182")
            || HasSeenEvent(farmer, "75160385")
            || HasFriendshipRecord(farmer, "Undreya")
            || HasVisited(farmer, "Custom_Ridgeside_RSVAbandonedHouse");
        bool daiaKnown = HasSeenEvent(farmer, "75160201")
            || HasFriendshipRecord(farmer, "Daia");

        return new RsvWorldProgressSnapshot(
            true,
            villageVisited,
            cableCarUsed,
            gatheringSeen,
            greenhouseRestored,
            ridgeForestVisited,
            spiritRealmKnown,
            ninjaHouseKnown,
            undreyaKnown,
            daiaKnown
        );
    }

    private static string BuildPromptLabel(SveWorldProgressSnapshot sve, RsvWorldProgressSnapshot rsv)
    {
        var parts = new List<string>();
        if (sve.Installed)
        {
            parts.Add($"SVE: {sve.PromptLabel}");
        }

        if (rsv.Installed)
        {
            parts.Add($"Ridgeside Village: {rsv.PromptLabel}");
        }

        return parts.Count == 0
            ? "no supported expansion-specific progress detected"
            : string.Join("; ", parts);
    }

    private static string BuildDebugLabel(SveWorldProgressSnapshot sve, RsvWorldProgressSnapshot rsv)
    {
        var parts = new List<string>();
        if (sve.Installed)
        {
            parts.Add($"SVE：{sve.DebugLabel}");
        }

        if (rsv.Installed)
        {
            parts.Add($"RSV：{rsv.DebugLabel}");
        }

        return parts.Count == 0
            ? "无已识别扩展进度"
            : string.Join("；", parts);
    }

    private static string BuildReplyGuidance(SveWorldProgressSnapshot sve, RsvWorldProgressSnapshot rsv)
    {
        var parts = new List<string>();
        if (sve.Installed)
        {
            parts.Add("Only treat confirmed SVE milestones as completed; if an SVE milestone is unconfirmed, avoid claiming the farmer has already done it.");
        }

        if (rsv.Installed)
        {
            parts.Add("Only treat confirmed Ridgeside milestones as completed; if an RSV milestone is unconfirmed, avoid claiming the farmer has already done it.");
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

    private static string BuildRsvNpcPrompt(
        RsvWorldProgressSnapshot rsv,
        NPC npc,
        IReadOnlyCollection<string> attitudeTraits)
    {
        var parts = new List<string> { rsv.PromptLabel };
        if ((IsAny(npc.Name, "Lenny", "Keahi", "Pika", "Richard", "Ysabelle") || attitudeTraits.Contains("community", StringComparer.OrdinalIgnoreCase))
            && (rsv.VillageVisited || rsv.GatheringSeen))
        {
            parts.Add("community-linked RSV characters may treat village visits and gatherings as shared social context");
        }

        if ((IsAny(npc.Name, "Kenneth", "Bryle", "June") || attitudeTraits.Contains("technical", StringComparer.OrdinalIgnoreCase))
            && rsv.CableCarUsed)
        {
            parts.Add("cable-car-linked RSV characters may treat the cable car as everyday infrastructure");
        }

        if ((IsAny(npc.Name, "Jio", "Corine", "Bryle", "Lola", "Freddie") || attitudeTraits.Contains("adventurous", StringComparer.OrdinalIgnoreCase))
            && (rsv.RidgeForestVisited || rsv.NinjaHouseKnown || rsv.SpiritRealmKnown))
        {
            parts.Add("adventure-linked RSV characters may recognize the mountain's hidden dangers when confirmed");
        }

        if (IsAny(npc.Name, "Daia", "Raeriyala", "Undreya") && (rsv.DaiaKnown || rsv.UndreyaKnown || rsv.SpiritRealmKnown))
        {
            parts.Add("mystical RSV characters may treat confirmed magical or hidden-village events as personally relevant");
        }

        return string.Join("; ", parts);
    }

    private static string BuildRsvNpcDebug(
        RsvWorldProgressSnapshot rsv,
        NPC npc,
        IReadOnlyCollection<string> attitudeTraits)
    {
        var parts = new List<string> { rsv.DebugLabel };
        if ((IsAny(npc.Name, "Lenny", "Keahi", "Pika", "Richard", "Ysabelle") || attitudeTraits.Contains("community", StringComparer.OrdinalIgnoreCase))
            && (rsv.VillageVisited || rsv.GatheringSeen))
        {
            parts.Add("社区型 RSV 角色可感知村庄来往");
        }

        if ((IsAny(npc.Name, "Kenneth", "Bryle", "June") || attitudeTraits.Contains("technical", StringComparer.OrdinalIgnoreCase))
            && rsv.CableCarUsed)
        {
            parts.Add("缆车相关角色可感知缆车日常化");
        }

        if ((IsAny(npc.Name, "Jio", "Corine", "Bryle", "Lola", "Freddie") || attitudeTraits.Contains("adventurous", StringComparer.OrdinalIgnoreCase))
            && (rsv.RidgeForestVisited || rsv.NinjaHouseKnown || rsv.SpiritRealmKnown))
        {
            parts.Add("冒险线 RSV 角色可感知山地危险进展");
        }

        if (IsAny(npc.Name, "Daia", "Raeriyala", "Undreya") && (rsv.DaiaKnown || rsv.UndreyaKnown || rsv.SpiritRealmKnown))
        {
            parts.Add("神秘线 RSV 角色可感知隐藏/魔法进展");
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
    RsvWorldProgressSnapshot Rsv,
    string PromptLabel,
    string DebugLabel,
    string ReplyGuidance
)
{
    public bool HasAnyInstalled => this.Sve.Installed || this.Rsv.Installed;
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

internal sealed record RsvWorldProgressSnapshot(
    bool Installed,
    bool VillageVisited,
    bool CableCarUsed,
    bool GatheringSeen,
    bool GreenhouseRestored,
    bool RidgeForestVisited,
    bool SpiritRealmKnown,
    bool NinjaHouseKnown,
    bool UndreyaKnown,
    bool DaiaKnown
)
{
    public static RsvWorldProgressSnapshot NotInstalled { get; } = new(
        false,
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
        yield return this.VillageVisited ? "Ridgeside village visited" : "Ridgeside village visit not confirmed";
        yield return this.CableCarUsed ? "cable car use confirmed" : "cable car use not confirmed";
        yield return this.GatheringSeen ? "Ridgeside gathering seen" : "Ridgeside gathering not confirmed";
        yield return this.GreenhouseRestored ? "Ridgeside greenhouse restored" : "Ridgeside greenhouse restoration not confirmed";
        yield return this.RidgeForestVisited ? "ridge forest visited" : "ridge forest visit not confirmed";
        yield return this.SpiritRealmKnown ? "spirit realm known" : "spirit realm not confirmed";
        yield return this.NinjaHouseKnown ? "ninja house known" : "ninja house not confirmed";
        yield return this.UndreyaKnown ? "Undreya known" : "Undreya not confirmed known";
        yield return this.DaiaKnown ? "Daia known" : "Daia not confirmed known";
    }

    private IEnumerable<string> BuildDebugParts()
    {
        yield return this.VillageVisited ? "已到过里奇赛德村" : "未确认到过里奇赛德村";
        yield return this.CableCarUsed ? "已确认缆车" : "未确认缆车";
        yield return this.GatheringSeen ? "已见过里奇赛德聚会" : "未确认里奇赛德聚会";
        yield return this.GreenhouseRestored ? "里奇赛德温室已修复" : "里奇赛德温室未确认修复";
        yield return this.RidgeForestVisited ? "已去过 Ridge Forest" : "未确认 Ridge Forest";
        yield return this.SpiritRealmKnown ? "已确认 Spirit Realm" : "未确认 Spirit Realm";
        yield return this.NinjaHouseKnown ? "已确认 Ninja House" : "未确认 Ninja House";
        yield return this.UndreyaKnown ? "已认识 Undreya" : "未确认 Undreya";
        yield return this.DaiaKnown ? "已认识 Daia" : "未确认 Daia";
    }
}
