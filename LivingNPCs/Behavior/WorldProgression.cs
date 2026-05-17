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

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> ExplicitObservationDomains =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Robin"] = ["farming"],
            ["Marnie"] = ["farming"],
            ["Pierre"] = ["farming"],
            ["Andy"] = ["farming"],
            ["Susan"] = ["farming"],
            ["Sophia"] = ["farming"],
            ["Willy"] = ["fishing"],
            ["Carmen"] = ["fishing"],
            ["Clint"] = ["mining"],
            ["Dwarf"] = ["mining"],
            ["MarlonFay"] = ["mining", "combat"],
            ["Gil"] = ["mining", "combat"],
            ["Abigail"] = ["mining", "combat"],
            ["Lance"] = ["mining", "combat"],
            ["Alesia"] = ["mining", "combat"],
            ["Isaac"] = ["mining", "combat"],
            ["Linus"] = ["foraging"],
            ["Leah"] = ["foraging"],
            ["Bear"] = ["foraging"]
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

    public static WorldProgressKnowledgeSnapshot ForNpc(
        NPC npc,
        int friendshipHearts,
        string locationName,
        WorldProgressSnapshot progression)
    {
        var observationDomains = DetermineObservationDomains(npc);
        bool onFarm = locationName.Contains("farm", StringComparison.OrdinalIgnoreCase);
        bool trustedRelationship = friendshipHearts >= 6;
        bool farmKnowledgeAvailable = onFarm
            || trustedRelationship
            || HasExplicitObservationDomain(npc.Name, "farming")
            || (friendshipHearts >= 2 && observationDomains.Contains("farming", StringComparer.OrdinalIgnoreCase));
        var knownProfessionFocuses = progression.ProfessionFocuses
            .Where(focus =>
                trustedRelationship
                || HasExplicitObservationDomain(npc.Name, focus)
                || (friendshipHearts >= 2 && observationDomains.Contains(focus, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        string personalKnowledgePrompt = BuildPersonalKnowledgePromptLabel(
            progression,
            farmKnowledgeAvailable,
            knownProfessionFocuses,
            onFarm,
            trustedRelationship
        );
        string personalKnowledgeDebug = BuildPersonalKnowledgeDebugLabel(
            progression,
            farmKnowledgeAvailable,
            knownProfessionFocuses,
            onFarm,
            trustedRelationship
        );

        return new WorldProgressKnowledgeSnapshot(
            observationDomains,
            knownProfessionFocuses,
            farmKnowledgeAvailable,
            trustedRelationship,
            BuildNpcPromptLabel(progression, personalKnowledgePrompt),
            BuildNpcDebugLabel(progression, personalKnowledgeDebug),
            BuildNpcReplyGuidance(progression, farmKnowledgeAvailable, knownProfessionFocuses)
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

    private static IReadOnlyCollection<string> DetermineObservationDomains(NPC npc)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ExplicitObservationDomains.TryGetValue(npc.Name, out var explicitDomains))
        {
            foreach (string domain in explicitDomains)
            {
                domains.Add(domain);
            }
        }

        var disposition = NpcDisposition.For(npc);
        string profileText = $"{disposition.PromptLabel} {disposition.BackgroundPrompt} {disposition.DialoguePrompt}".ToLowerInvariant();
        if (ContainsAny(profileText, "farm", "ranch", "vineyard", "crop", "carpenter"))
        {
            domains.Add("farming");
        }

        if (ContainsAny(profileText, "fish", "sea", "water life"))
        {
            domains.Add("fishing");
        }

        if (ContainsAny(profileText, "mine", "smith", "ore", "adventurer", "guild"))
        {
            domains.Add("mining");
        }

        if (ContainsAny(profileText, "forag", "woodland", "forest", "nature"))
        {
            domains.Add("foraging");
        }

        if (ContainsAny(profileText, "combat", "fighter", "battle", "monster", "danger"))
        {
            domains.Add("combat");
        }

        return domains;
    }

    private static bool HasExplicitObservationDomain(string npcName, string domain)
    {
        return ExplicitObservationDomains.TryGetValue(npcName, out var domains)
            && domains.Contains(domain, StringComparer.OrdinalIgnoreCase);
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

    private static string BuildNpcPromptLabel(WorldProgressSnapshot progression, string personalKnowledgePrompt)
    {
        return $"public facts: {BuildPublicPromptLabel(progression)}; personal knowledge available to this NPC: {personalKnowledgePrompt}";
    }

    private static string BuildPublicPromptLabel(WorldProgressSnapshot progression)
    {
        string facilities = BuildFacilityPromptLabel(progression);
        string household = progression.SpouseNames.Count == 0
            ? progression.ChildrenCount == 0
                ? "household publicly known as not married, no children"
                : $"household publicly known as no spouse recorded, {FormatChildrenPrompt(progression.ChildrenCount)}"
            : $"household publicly known as married to {string.Join(", ", progression.SpouseNames)}, {FormatChildrenPrompt(progression.ChildrenCount)}";

        return $"route: {FormatRoutePrompt(progression.Route)}; resident stage: {FormatResidentStagePrompt(progression.ResidentStage)}; {facilities}; {household}";
    }

    private static string BuildPersonalKnowledgePromptLabel(
        WorldProgressSnapshot progression,
        bool farmKnowledgeAvailable,
        IReadOnlyCollection<string> knownProfessionFocuses,
        bool onFarm,
        bool trustedRelationship)
    {
        var details = new List<string>();
        if (farmKnowledgeAvailable)
        {
            string reason = onFarm
                ? "visible from the farm visit"
                : trustedRelationship
                    ? "known through a close relationship"
                    : "plausible from this NPC's work or interests";
            details.Add($"farm scale may be understood approximately as {FormatFarmScalePrompt(progression.FarmScale)} ({reason}); avoid exact crop, building, or animal counts");
        }
        else
        {
            details.Add("farm scale is not clearly known; do not assume exact crop, building, or animal details");
        }

        if (knownProfessionFocuses.Count > 0)
        {
            details.Add($"farmer focus plausibly known as {string.Join(", ", knownProfessionFocuses)}");
        }
        else if (progression.ProfessionFocuses.Count > 0)
        {
            details.Add("farmer profession focus is not clearly known to this NPC");
        }
        else
        {
            details.Add("no clear profession focus is known yet");
        }

        return string.Join("; ", details);
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

    private static string BuildNpcDebugLabel(WorldProgressSnapshot progression, string personalKnowledgeDebug)
    {
        return $"公开事实：{BuildPublicDebugLabel(progression)}；NPC 可知私人信息：{personalKnowledgeDebug}";
    }

    private static string BuildPublicDebugLabel(WorldProgressSnapshot progression)
    {
        string facilities = BuildFacilityDebugLabel(progression);
        string household = progression.SpouseNames.Count == 0
            ? $"未婚，{FormatChildrenDebug(progression.ChildrenCount)}"
            : $"已婚：{string.Join("、", progression.SpouseNames)}，{FormatChildrenDebug(progression.ChildrenCount)}";

        return $"路线 {FormatRouteDebug(progression.Route)}，阶段 {FormatResidentStageDebug(progression.ResidentStage)}，解锁 {facilities}，家庭 {household}";
    }

    private static string BuildPersonalKnowledgeDebugLabel(
        WorldProgressSnapshot progression,
        bool farmKnowledgeAvailable,
        IReadOnlyCollection<string> knownProfessionFocuses,
        bool onFarm,
        bool trustedRelationship)
    {
        var details = new List<string>();
        if (farmKnowledgeAvailable)
        {
            string reason = onFarm
                ? "当前就在农场"
                : trustedRelationship
                    ? "关系够近"
                    : "职业/兴趣相关";
            details.Add($"大致知道农场规模：{FormatFarmScaleDebug(progression.FarmScale)}（{reason}）");
        }
        else
        {
            details.Add("不清楚农场规模");
        }

        if (knownProfessionFocuses.Count > 0)
        {
            details.Add($"可能知道职业倾向：{string.Join("、", knownProfessionFocuses.Select(FormatProfessionDebug))}");
        }
        else
        {
            details.Add("不清楚职业倾向");
        }

        return string.Join("；", details);
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

    private static string BuildNpcReplyGuidance(
        WorldProgressSnapshot progression,
        bool farmKnowledgeAvailable,
        IReadOnlyCollection<string> knownProfessionFocuses)
    {
        string privateKnowledgeGuidance = farmKnowledgeAvailable
            ? "the NPC may refer to the farm's general scale, but should not cite exact crops, buildings, or animal counts unless the farmer just said so"
            : "do not let this NPC speak as if they know the farm's detailed scale";
        string professionGuidance = knownProfessionFocuses.Count > 0
            ? $"this NPC may plausibly recognize {string.Join(", ", knownProfessionFocuses)} as part of the farmer's life"
            : progression.ProfessionFocuses.Count > 0
                ? "do not let this NPC casually name the farmer's profession focus"
                : "no profession focus needs to be assumed yet";

        return $"{progression.ReplyGuidance} {privateKnowledgeGuidance}; {professionGuidance}.";
    }

    private static string BuildFacilityPromptLabel(WorldProgressSnapshot progression)
    {
        var unlockedFacilities = GetUnlockedFacilities(progression);
        return unlockedFacilities.Count == 0
            ? "no major late-game facilities are confirmed unlocked yet"
            : $"confirmed public unlocks: {string.Join(", ", unlockedFacilities.Select(key => FacilityPromptLabels[key]))}";
    }

    private static string BuildFacilityDebugLabel(WorldProgressSnapshot progression)
    {
        var unlockedFacilities = GetUnlockedFacilities(progression);
        return unlockedFacilities.Count == 0
            ? "暂无已确认的大型解锁"
            : string.Join("、", unlockedFacilities.Select(key => FacilityDebugLabels[key]));
    }

    private static IReadOnlyList<string> GetUnlockedFacilities(WorldProgressSnapshot progression)
    {
        var unlockedFacilities = new List<string>();
        if (progression.BusRepaired)
        {
            unlockedFacilities.Add("bus");
        }

        if (progression.GreenhouseRepaired)
        {
            unlockedFacilities.Add("greenhouse");
        }

        if (progression.MinecartsRepaired)
        {
            unlockedFacilities.Add("minecarts");
        }

        if (progression.GingerIslandUnlocked)
        {
            unlockedFacilities.Add("ginger_island");
        }

        if (progression.MovieTheaterOpen)
        {
            unlockedFacilities.Add("movie_theater");
        }

        return unlockedFacilities;
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

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
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

internal sealed record WorldProgressKnowledgeSnapshot(
    IReadOnlyCollection<string> ObservationDomains,
    IReadOnlyList<string> KnownProfessionFocuses,
    bool KnowsFarmScale,
    bool TrustedRelationship,
    string PromptLabel,
    string DebugLabel,
    string ReplyGuidance
);
