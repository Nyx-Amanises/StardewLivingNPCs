using System;

namespace LivingNPCs.Behavior;

/// <summary>What the outing state machine should do with one pending outing on this tick.</summary>
internal enum CompanionOutingTickPlan
{
    /// <summary>The outing is from another day or its NPC vanished; drop it (restoring the schedule if possible).</summary>
    DropStaleOuting,

    /// <summary>An event scene is active; preserve the outing and resume its current leg afterward.</summary>
    WaitForEventScene,

    /// <summary>Too late in the day to still be walking there; stop so the NPC can head home.</summary>
    StopForLateTravel,

    /// <summary>A menu is open; leave the outing untouched this tick.</summary>
    WaitForMenu,

    AdvanceTravel,
    AdvanceStay,
    AdvanceReturn
}

internal static class CompanionOutingRules
{
    public const int MinimumStayMinutes = 60;
    public const int MinimumShortVisitMinutes = 10;
    public const int MaximumShortVisitMinutes = 30;
    public const int DefaultShortVisitMinutes = 20;
    public const int MinimumSharedMinutesForMemory = 30;
    public const int EstimatedTravelMinutes = 120;
    public const int EstimatedShortVisitTravelMinutes = 60;
    public const int LatestPlannedStayEndTime = 2500;
    public const int SettledEmoteDelayMinutes = 20;
    public const int ExclamationEmoteId = 16;
    public const int HeartEmoteId = 20;

    /// <summary>
    /// The per-tick decision core of the outing state machine, pulled out of
    /// CompanionOutingRuntime so it can be tested with a fake <see cref="IOutingWorldView"/>.
    /// Order matters and mirrors the runtime's historical checks: staleness first, then event
    /// pause, then the too-late-to-travel abort, then menu pause, then phase dispatch.
    /// </summary>
    public static CompanionOutingTickPlan PlanTick(
        IOutingWorldView world,
        int outingTotalDays,
        bool npcExists,
        CompanionOutingPhase phase)
    {
        if (!npcExists || outingTotalDays != world.TotalDays)
        {
            return CompanionOutingTickPlan.DropStaleOuting;
        }

        if (world.IsEventActive)
        {
            return CompanionOutingTickPlan.WaitForEventScene;
        }

        if (world.TimeOfDay >= LatestPlannedStayEndTime && IsTravelingPhase(phase))
        {
            return CompanionOutingTickPlan.StopForLateTravel;
        }

        if (world.IsMenuOpen)
        {
            return CompanionOutingTickPlan.WaitForMenu;
        }

        return phase switch
        {
            CompanionOutingPhase.Traveling
                or CompanionOutingPhase.TravelingToFarmBoundary
                or CompanionOutingPhase.TravelingFromFarmBoundary
                or CompanionOutingPhase.TravelingToVehicleGateway => CompanionOutingTickPlan.AdvanceTravel,
            CompanionOutingPhase.AtDestination => CompanionOutingTickPlan.AdvanceStay,
            _ => CompanionOutingTickPlan.AdvanceReturn
        };
    }

    public static bool IsTravelingPhase(CompanionOutingPhase phase)
    {
        return phase is CompanionOutingPhase.Traveling
            or CompanionOutingPhase.TravelingToFarmBoundary
            or CompanionOutingPhase.TravelingFromFarmBoundary
            or CompanionOutingPhase.TravelingToVehicleGateway;
    }

    public static string DetermineActivityStyle(string targetLocation, string reason)
    {
        string text = reason ?? string.Empty;
        if (ContainsAny(text, "花舞", "花田", "跳舞", "dance meadow", "flower dance", "festival")
            || targetLocation == "FlowerDance")
        {
            return "festival";
        }

        if (ContainsAny(text, "吧台", "酒保", "喝一杯", "喝点", "小酌", "bar counter", "bartop", "drink")
            || IsSocialTarget(targetLocation))
        {
            return "social";
        }

        if (ContainsAny(text, "电影", "看片", "影院", "movie", "film", "cinema")
            || targetLocation == "MovieTheater")
        {
            return "browse";
        }

        if (ContainsAny(text, "温泉", "泡汤", "泡澡", "泡一泡", "spa", "hot spring", "bath house", "bathhouse", "soak")
            || targetLocation == "BathHouse_Entry")
        {
            return "quiet";
        }

        if (ContainsAny(text, "风景", "景色", "看看海", "看海", "看浪", "海浪", "浪花", "散心", "看风景", "瀑布", "山顶", "scenery", "view", "sightseeing", "shore", "waves", "waterfall"))
        {
            return "scenic";
        }

        if (ContainsAny(text, "逛", "翻书", "看书", "书架", "展品", "看看商品", "买东西", "商店", "browse", "book", "read", "shelves", "exhibit", "shop", "shopping")
            || IsBrowseTarget(targetLocation))
        {
            return "browse";
        }

        if (ContainsAny(text, "安静", "坐一会", "待一会", "聊聊", "聊天", "说说话", "湖边聊", "quiet", "sit", "talk", "chat"))
        {
            return "quiet";
        }

        return IsScenicTarget(targetLocation)
            ? "scenic"
            : "visit";
    }

    public static string GetActivityPromptLabel(string activityStyle)
    {
        return activityStyle switch
        {
            "scenic" => "taking in the surroundings together",
            "browse" => "looking around the place together",
            "quiet" => "sharing some quiet time",
            "social" => "spending relaxed public time together",
            "festival" => "walking the festival edge together",
            _ => "visiting the place together"
        };
    }

    public static string GetActivityDebugLabel(string activityStyle)
    {
        return activityStyle switch
        {
            "scenic" => "看风景",
            "browse" => "逛一逛",
            "quiet" => "安静相处",
            "social" => "公共场所相处",
            "festival" => "节日散步",
            _ => "一起拜访"
        };
    }

    public static bool CanFitMinimumStay(int currentTimeOfDay, int minimumStayMinutes)
    {
        int stayMinutes = NormalizeRequestedStayMinutes(minimumStayMinutes);
        int projectedEnd = BehaviorTimeMath.AddMinutesToTime(
            currentTimeOfDay,
            stayMinutes + GetEstimatedTravelMinutes(stayMinutes)
        );
        return projectedEnd <= LatestPlannedStayEndTime;
    }

    public static int NormalizeRequestedStayMinutes(int durationMinutes)
    {
        if (durationMinutes <= 0)
        {
            return MinimumStayMinutes;
        }

        if (durationMinutes < MinimumStayMinutes)
        {
            return Math.Clamp(durationMinutes, MinimumShortVisitMinutes, MaximumShortVisitMinutes);
        }

        return Math.Clamp(durationMinutes, MinimumStayMinutes, 600);
    }

    public static bool IsShortVisit(int durationMinutes)
    {
        return NormalizeRequestedStayMinutes(durationMinutes) < MinimumStayMinutes;
    }

    /// <summary>
    /// The stay length used for every full (non-short-visit) outing. This is a fixed, predictable
    /// duration from config by design: the AI-requested durationMinutes only distinguishes a brief
    /// escort from a real outing, and the action-decision instructions tell the model the same so
    /// the spoken plans stay consistent with the actual behavior.
    /// </summary>
    public static int GetFixedFullOutingStayMinutes(int configuredStayMinutes)
    {
        return Math.Clamp(configuredStayMinutes, MinimumStayMinutes, 600);
    }

    private static int GetEstimatedTravelMinutes(int stayMinutes)
    {
        return stayMinutes < MinimumStayMinutes
            ? EstimatedShortVisitTravelMinutes
            : EstimatedTravelMinutes;
    }

    public static int SelectStableTopCandidateIndex(string npcName, string targetLocation, int totalDays, int candidateCount)
    {
        if (candidateCount <= 1)
        {
            return 0;
        }

        uint hash = ComputeStableHash($"{npcName}|{targetLocation}|{totalDays}");
        return (int)(hash % (uint)Math.Min(3, candidateCount));
    }

    public static int SelectSettledEmoteId(
        string npcName,
        string targetLocation,
        string activityStyle,
        int totalDays,
        bool warmRelationship,
        bool emotionallyComfortable)
    {
        if (!emotionallyComfortable)
        {
            return 0;
        }

        int roll = (int)(ComputeStableHash(
            $"{npcName}|{targetLocation}|{activityStyle}|{totalDays}|settled-emote"
        ) % 100);
        if (warmRelationship
            && activityStyle is "scenic" or "quiet" or "festival"
            && roll < 28)
        {
            return HeartEmoteId;
        }

        if (activityStyle is "browse" or "social" or "festival" && roll < 22)
        {
            return ExclamationEmoteId;
        }

        return !warmRelationship && activityStyle == "scenic" && roll < 12
            ? ExclamationEmoteId
            : 0;
    }

    private static bool ContainsAny(string text, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static bool IsBrowseTarget(string targetLocation)
    {
        return targetLocation is "SeedShop" or "ArchaeologyHouse" or "Blacksmith" or "FishShop" or "MovieTheater";
    }

    private static bool IsScenicTarget(string targetLocation)
    {
        return targetLocation is "Beach" or "Mountain" or "Forest" or "Farm"
            or "Desert" or "IslandSouth"
            or "Custom_GrampletonCoast" or "Custom_BlueMoonVineyard" or "Custom_AuroraVineyard"
            or "Custom_ForestWest" or "Custom_SVESummit" or "Custom_GrandpasShedOutside"
            or "Custom_JunimoWoods" or "Custom_EnchantedGrove";
    }

    private static bool IsSocialTarget(string targetLocation)
    {
        return targetLocation is "Saloon" or "Town";
    }
}
