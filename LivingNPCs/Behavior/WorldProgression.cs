using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class WorldProgression
{
    private static readonly IReadOnlyDictionary<string, string> FacilityPromptLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bus"] = "bus",
            ["greenhouse"] = "greenhouse",
            ["minecarts"] = "minecarts",
            ["ginger_island"] = "Ginger Island access",
            ["movie_theater"] = "movie theater"
        };

    private static readonly IReadOnlyDictionary<string, string> FacilityDebugLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bus"] = "巴士",
            ["greenhouse"] = "温室",
            ["minecarts"] = "矿车",
            ["ginger_island"] = "姜岛",
            ["movie_theater"] = "电影院"
        };

    public static WorldProgressSnapshot Current()
    {
        Farmer farmer = Game1.getPlayerOrEventFarmer();
        var previousActivities = farmer.previousActiveDialogueEvents.FirstOrDefault();

        bool communityCenterRestored = HasActivity(previousActivities, "cc_Complete");
        bool jojaMember = HasMail(farmer, "JojaMember");
        bool busRepaired = HasActivity(previousActivities, "cc_Bus");
        bool greenhouseRepaired = HasActivity(previousActivities, "cc_Greenhouse");
        bool minecartsRepaired = HasActivity(previousActivities, "cc_Minecart");
        bool movieTheaterOpen = HasActivity(previousActivities, "movieTheater");
        bool gingerIslandUnlocked = HasMail(farmer, "willyBoat")
            || farmer.locationsVisited.Contains("IslandSouth");

        string route = DetermineRoute(communityCenterRestored, jojaMember);
        string residentStage = DetermineResidentStage();
        int cropCount = CountGrowingCrops();
        int buildingCount = CountCompletedBuildings();
        int animalCount = Game1.getFarm().getAllFarmAnimals().Count();
        string farmScale = DetermineFarmScale(cropCount, buildingCount, animalCount);
        var professionFocuses = DetermineProfessionFocuses(farmer);
        var spouseNames = GetSpouseDisplayNames(farmer);
        int childrenCount = farmer.getChildren().Count;

        var unlockedFacilities = new List<string>();
        if (busRepaired)
        {
            unlockedFacilities.Add("bus");
        }

        if (greenhouseRepaired)
        {
            unlockedFacilities.Add("greenhouse");
        }

        if (minecartsRepaired)
        {
            unlockedFacilities.Add("minecarts");
        }

        if (gingerIslandUnlocked)
        {
            unlockedFacilities.Add("ginger_island");
        }

        if (movieTheaterOpen)
        {
            unlockedFacilities.Add("movie_theater");
        }

        return new WorldProgressSnapshot(
            Game1.year,
            route,
            residentStage,
            busRepaired,
            greenhouseRepaired,
            minecartsRepaired,
            gingerIslandUnlocked,
            movieTheaterOpen,
            professionFocuses,
            farmScale,
            cropCount,
            buildingCount,
            animalCount,
            spouseNames,
            childrenCount,
            BuildPromptLabel(
                route,
                residentStage,
                unlockedFacilities,
                professionFocuses,
                farmScale,
                cropCount,
                buildingCount,
                animalCount,
                spouseNames,
                childrenCount
            ),
            BuildDebugLabel(
                route,
                residentStage,
                unlockedFacilities,
                professionFocuses,
                farmScale,
                cropCount,
                buildingCount,
                animalCount,
                spouseNames,
                childrenCount
            ),
            BuildReplyGuidance(route, residentStage, unlockedFacilities, spouseNames, childrenCount)
        );
    }

    private static bool HasActivity(IDictionary<string, int>? activities, string key)
    {
        return activities?.ContainsKey(key) == true;
    }

    private static bool HasMail(Farmer farmer, string key)
    {
        return farmer.mailReceived.Contains(key);
    }

    private static string DetermineRoute(bool communityCenterRestored, bool jojaMember)
    {
        if (communityCenterRestored)
        {
            return "community_center";
        }

        if (jojaMember)
        {
            return "joja";
        }

        return "unresolved";
    }

    private static string DetermineResidentStage()
    {
        if (Game1.year <= 1 && Game1.currentSeason == "spring")
        {
            return "first_spring_newcomer";
        }

        if (Game1.year <= 1)
        {
            return "first_year_settling_in";
        }

        if (Game1.year == 2)
        {
            return "second_year_established";
        }

        return "veteran_resident";
    }

    private static int CountGrowingCrops()
    {
        return Game1.getFarm().terrainFeatures.Values
            .OfType<StardewValley.TerrainFeatures.HoeDirt>()
            .Count(dirt => dirt.crop != null);
    }

    private static int CountCompletedBuildings()
    {
        var excludedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Shipping Bin",
            "Pet Bowl",
            "Farmhouse"
        };

        return Game1.getFarm().buildings.Count(building =>
            building.daysOfConstructionLeft.Value <= 0
            && !excludedTypes.Contains(building.buildingType.Value)
        );
    }

    private static string DetermineFarmScale(int cropCount, int buildingCount, int animalCount)
    {
        if (cropCount >= 100 || buildingCount >= 6 || animalCount >= 12)
        {
            return "large_operation";
        }

        if (cropCount >= 40 || buildingCount >= 3 || animalCount >= 4)
        {
            return "established_farm";
        }

        if (cropCount >= 10 || buildingCount >= 1 || animalCount >= 1)
        {
            return "small_working_farm";
        }

        return "starting_farm";
    }

    private static IReadOnlyList<string> DetermineProfessionFocuses(Farmer farmer)
    {
        var focuses = new List<string>();
        if (farmer.professions.Any(profession => profession is >= 0 and <= 5))
        {
            focuses.Add("farming");
        }

        if (farmer.professions.Any(profession => profession is >= 6 and <= 11))
        {
            focuses.Add("fishing");
        }

        if (farmer.professions.Any(profession => profession is >= 12 and <= 17))
        {
            focuses.Add("foraging");
        }

        if (farmer.professions.Any(profession => profession is >= 18 and <= 23))
        {
            focuses.Add("mining");
        }

        if (farmer.professions.Any(profession => profession is >= 24 and <= 29))
        {
            focuses.Add("combat");
        }

        return focuses;
    }

    private static IReadOnlyList<string> GetSpouseDisplayNames(Farmer farmer)
    {
        return farmer.friendshipData.FieldDict
            .Where(pair => pair.Value.Value.IsMarried() && !pair.Value.Value.IsRoommate())
            .Select(pair => Game1.characterData.TryGetValue(pair.Key, out var data) && !string.IsNullOrWhiteSpace(data.DisplayName)
                ? data.DisplayName
                : pair.Key)
            .ToList();
    }

    private static string BuildPromptLabel(
        string route,
        string residentStage,
        IReadOnlyList<string> unlockedFacilities,
        IReadOnlyList<string> professionFocuses,
        string farmScale,
        int cropCount,
        int buildingCount,
        int animalCount,
        IReadOnlyList<string> spouseNames,
        int childrenCount)
    {
        string facilities = unlockedFacilities.Count == 0
            ? "no major late-game facilities are confirmed unlocked yet"
            : $"confirmed unlocks: {string.Join(", ", unlockedFacilities.Select(key => FacilityPromptLabels[key]))}";
        string professions = professionFocuses.Count == 0
            ? "no clear profession focus yet"
            : $"profession focus: {string.Join(", ", professionFocuses)}";
        string household = spouseNames.Count == 0
            ? childrenCount == 0
                ? "household: not married, no children"
                : $"household: no spouse recorded, {FormatChildrenPrompt(childrenCount)}"
            : $"household: married to {string.Join(", ", spouseNames)}, {FormatChildrenPrompt(childrenCount)}";

        return $"route: {FormatRoutePrompt(route)}; resident stage: {FormatResidentStagePrompt(residentStage)}; {facilities}; {professions}; farm scale: {FormatFarmScalePrompt(farmScale)} ({cropCount} growing crops, {buildingCount} completed farm buildings, {animalCount} animals); {household}";
    }

    private static string BuildDebugLabel(
        string route,
        string residentStage,
        IReadOnlyList<string> unlockedFacilities,
        IReadOnlyList<string> professionFocuses,
        string farmScale,
        int cropCount,
        int buildingCount,
        int animalCount,
        IReadOnlyList<string> spouseNames,
        int childrenCount)
    {
        string facilities = unlockedFacilities.Count == 0
            ? "暂无已确认的大型解锁"
            : string.Join("、", unlockedFacilities.Select(key => FacilityDebugLabels[key]));
        string professions = professionFocuses.Count == 0
            ? "暂无明确职业倾向"
            : string.Join("、", professionFocuses.Select(FormatProfessionDebug));
        string household = spouseNames.Count == 0
            ? $"未婚，{FormatChildrenDebug(childrenCount)}"
            : $"已婚：{string.Join("、", spouseNames)}，{FormatChildrenDebug(childrenCount)}";

        return $"路线：{FormatRouteDebug(route)}；阶段：{FormatResidentStageDebug(residentStage)}；解锁：{facilities}；职业：{professions}；农场：{FormatFarmScaleDebug(farmScale)}（作物 {cropCount}，建筑 {buildingCount}，动物 {animalCount}）；家庭：{household}";
    }

    private static string BuildReplyGuidance(
        string route,
        string residentStage,
        IReadOnlyList<string> unlockedFacilities,
        IReadOnlyList<string> spouseNames,
        int childrenCount)
    {
        string stageGuidance = residentStage switch
        {
            "first_spring_newcomer" =>
                "the farmer is still a newcomer in the first spring; do not assume long-established routines, deep town history, or late-game access",
            "first_year_settling_in" =>
                "the farmer is still in the first year but no longer at the very beginning; some routines may be forming, yet NPCs should not speak as if years have passed",
            "second_year_established" =>
                "the farmer is an established second-year resident; NPCs may naturally speak as if the farmer belongs here now",
            _ =>
                $"the farmer is a veteran resident in year {Game1.year}; do not speak as if she just arrived, and references to established routines are natural when relevant"
        };

        string routeGuidance = route switch
        {
            "community_center" =>
                "the restored community center should be treated as an existing fact, not a future hope",
            "joja" =>
                "the town followed the Joja route; do not speak as if community-center restoration is still the expected future",
            _ =>
                "the valley route is still unresolved; avoid assuming either community-center restoration or Joja completion"
        };

        string unlockGuidance = unlockedFacilities.Count == 0
            ? "avoid casual references to unrepaired travel or late-game facilities"
            : $"already-unlocked facilities such as {string.Join(", ", unlockedFacilities.Select(key => FacilityPromptLabels[key]))} may be treated as normal parts of life";
        string householdGuidance = spouseNames.Count == 0 && childrenCount == 0
            ? "do not imply a spouse or children"
            : "the farmer's household has changed, so spouse or child references are allowed when the topic naturally reaches family life";

        return $"{stageGuidance}; {routeGuidance}; {unlockGuidance}; {householdGuidance}.";
    }

    private static string FormatRoutePrompt(string route)
    {
        return route switch
        {
            "community_center" => "community center restored",
            "joja" => "Joja membership route",
            _ => "community route unresolved"
        };
    }

    private static string FormatRouteDebug(string route)
    {
        return route switch
        {
            "community_center" => "社区中心已修复",
            "joja" => "Joja 路线",
            _ => "路线未定"
        };
    }

    private static string FormatResidentStagePrompt(string stage)
    {
        return stage switch
        {
            "first_spring_newcomer" => "new arrival in the first spring",
            "first_year_settling_in" => "first-year farmer still settling in",
            "second_year_established" => "established second-year resident",
            _ => $"veteran resident in year {Game1.year}"
        };
    }

    private static string FormatResidentStageDebug(string stage)
    {
        return stage switch
        {
            "first_spring_newcomer" => "第一年春，新来者",
            "first_year_settling_in" => "第一年，逐渐安顿",
            "second_year_established" => "第二年，已融入",
            _ => $"第 {Game1.year} 年，老住户"
        };
    }

    private static string FormatFarmScalePrompt(string scale)
    {
        return scale switch
        {
            "large_operation" => "large operation",
            "established_farm" => "established farm",
            "small_working_farm" => "small working farm",
            _ => "starting farm"
        };
    }

    private static string FormatFarmScaleDebug(string scale)
    {
        return scale switch
        {
            "large_operation" => "大型经营",
            "established_farm" => "成熟农场",
            "small_working_farm" => "小型运转中",
            _ => "起步阶段"
        };
    }

    private static string FormatProfessionDebug(string profession)
    {
        return profession switch
        {
            "farming" => "耕种",
            "fishing" => "钓鱼",
            "foraging" => "采集",
            "mining" => "采矿",
            "combat" => "战斗",
            _ => profession
        };
    }

    private static string FormatChildrenPrompt(int count)
    {
        return count switch
        {
            <= 0 => "no children",
            1 => "one child",
            _ => $"{count} children"
        };
    }

    private static string FormatChildrenDebug(int count)
    {
        return count switch
        {
            <= 0 => "无孩子",
            1 => "1 个孩子",
            _ => $"{count} 个孩子"
        };
    }
}

internal sealed record WorldProgressSnapshot(
    int Year,
    string Route,
    string ResidentStage,
    bool BusRepaired,
    bool GreenhouseRepaired,
    bool MinecartsRepaired,
    bool GingerIslandUnlocked,
    bool MovieTheaterOpen,
    IReadOnlyList<string> ProfessionFocuses,
    string FarmScale,
    int GrowingCropCount,
    int CompletedBuildingCount,
    int AnimalCount,
    IReadOnlyList<string> SpouseNames,
    int ChildrenCount,
    string PromptLabel,
    string DebugLabel,
    string ReplyGuidance
);
