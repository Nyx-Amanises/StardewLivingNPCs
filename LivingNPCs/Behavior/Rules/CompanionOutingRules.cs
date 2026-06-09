using System;

namespace LivingNPCs.Behavior;

internal static class CompanionOutingRules
{
    public const int MinimumSharedMinutesForMemory = 30;
    public const int EstimatedTravelMinutes = 120;
    public const int LatestPlannedStayEndTime = 2500;

    public static string DetermineActivityStyle(string targetLocation, string reason)
    {
        string text = reason ?? string.Empty;
        if (ContainsAny(text, "风景", "景色", "看看海", "散心", "看风景", "scenery", "view", "sightseeing"))
        {
            return "scenic";
        }

        if (ContainsAny(text, "逛", "看看商品", "买东西", "商店", "browse", "shop", "shopping")
            || targetLocation is "SeedShop" or "ArchaeologyHouse" or "Blacksmith" or "FishShop")
        {
            return "browse";
        }

        if (ContainsAny(text, "安静", "坐一会", "待一会", "聊聊", "quiet", "sit", "talk"))
        {
            return "quiet";
        }

        if (targetLocation is "Saloon" or "Town")
        {
            return "social";
        }

        return targetLocation is "Beach" or "Mountain" or "Forest" or "Farm"
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
            _ => "一起拜访"
        };
    }

    public static bool CanFitMinimumStay(int currentTimeOfDay, int minimumStayMinutes)
    {
        int projectedEnd = BehaviorTimeMath.AddMinutesToTime(
            currentTimeOfDay,
            Math.Max(300, minimumStayMinutes) + EstimatedTravelMinutes
        );
        return projectedEnd <= LatestPlannedStayEndTime;
    }

    public static int SelectStableTopCandidateIndex(string npcName, string targetLocation, int totalDays, int candidateCount)
    {
        if (candidateCount <= 1)
        {
            return 0;
        }

        unchecked
        {
            uint hash = 2166136261;
            string value = $"{npcName}|{targetLocation}|{totalDays}";
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash % (uint)Math.Min(3, candidateCount));
        }
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
}
