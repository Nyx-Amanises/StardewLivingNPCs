using System;

namespace LivingNPCs.Behavior;

internal static class CompanionOutingRules
{
    public const int MinimumStayMinutes = 120;
    public const int MinimumSharedMinutesForMemory = 30;
    public const int EstimatedTravelMinutes = 120;
    public const int LatestPlannedStayEndTime = 2500;

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
        int projectedEnd = BehaviorTimeMath.AddMinutesToTime(
            currentTimeOfDay,
            Math.Max(MinimumStayMinutes, minimumStayMinutes) + EstimatedTravelMinutes
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

    private static bool IsBrowseTarget(string targetLocation)
    {
        return targetLocation is "SeedShop" or "ArchaeologyHouse" or "Blacksmith" or "FishShop"
            or "Custom_Ridgeside_LogCabinHotelLobby" or "Custom_Ridgeside_PurpleMansion"
            or "Custom_Ridgeside_RSVGreenhouse1";
    }

    private static bool IsScenicTarget(string targetLocation)
    {
        return targetLocation is "Beach" or "Mountain" or "Forest" or "Farm"
            or "Custom_GrampletonCoast" or "Custom_BlueMoonVineyard" or "Custom_AuroraVineyard"
            or "Custom_ForestWest" or "Custom_SVESummit" or "Custom_GrandpasShedOutside"
            or "Custom_JunimoWoods" or "Custom_EnchantedGrove"
            or "Custom_Ridgeside_Ridge" or "Custom_Ridgeside_RidgeFalls"
            or "Custom_Ridgeside_RidgeForest" or "Custom_Ridgeside_RSVCableCar"
            or "Custom_Ridgeside_RSVCliff" or "Custom_Ridgeside_RSVTheHike"
            or "Custom_Ridgeside_RSVSpiritRealm";
    }

    private static bool IsSocialTarget(string targetLocation)
    {
        return targetLocation is "Saloon" or "Town" or "Custom_Ridgeside_RidgesideVillage";
    }
}
