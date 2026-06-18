using System;

namespace LivingNPCs.Behavior;

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
        return targetLocation is "SeedShop" or "ArchaeologyHouse" or "Blacksmith" or "FishShop";
    }

    private static bool IsScenicTarget(string targetLocation)
    {
        return targetLocation is "Beach" or "Mountain" or "Forest" or "Farm"
            or "Custom_GrampletonCoast" or "Custom_BlueMoonVineyard" or "Custom_AuroraVineyard"
            or "Custom_ForestWest" or "Custom_SVESummit" or "Custom_GrandpasShedOutside"
            or "Custom_JunimoWoods" or "Custom_EnchantedGrove";
    }

    private static bool IsSocialTarget(string targetLocation)
    {
        return targetLocation is "Saloon" or "Town";
    }
}
