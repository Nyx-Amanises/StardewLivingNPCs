using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LivingNPCs.Behavior;

internal static class TravelLocationRules
{
    private static readonly HashSet<string> PublicOutingTargets = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "Farm",
        "Town",
        "Mountain",
        "Mine",
        "Beach",
        "Forest",
        "BusStop",
        "Saloon",
        "SeedShop",
        "ArchaeologyHouse",
        "Hospital",
        "Trailer",
        "JoshHouse",
        "HaleyHouse",
        "SamHouse",
        "ScienceHouse",
        "LeahHouse",
        "AnimalShop",
        "ElliottHouse",
        "Blacksmith",
        "FishShop",
        "WizardHouse",
        "Tent",
        // Vehicle destinations (bus/boat/door): unreachable by vanilla schedule pathfinding, so
        // CompanionOutingRuntime rides them via OutingVehicleRules gateways instead of walking.
        "Desert",
        "IslandSouth",
        "MovieTheater",
        "BathHouse_Entry",
        // Note: "FlowerDance" is intentionally NOT an outing target. No GameLocation with that
        // name exists (the festival loads on a Temp map), so an outing there can never resolve;
        // it remains a valid festival-anchor target for DirectWorldActionRuntime, which paths
        // within the current festival map and keeps using the aliases/labels below.
        "Custom_GrampletonCoast",
        "Custom_BlueMoonVineyard",
        "Custom_AuroraVineyard",
        "Custom_ForestWest",
        "Custom_SVESummit",
        "Custom_GrandpasShedOutside",
        "Custom_JunimoWoods",
        "Custom_EnchantedGrove"
    };

    private static readonly Dictionary<string, string> Aliases = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Farm"] = "Farm",
        ["农场"] = "Farm",
        ["Town"] = "Town",
        ["Pelican Town"] = "Town",
        ["鹈鹕镇"] = "Town",
        ["Mountain"] = "Mountain",
        ["山地"] = "Mountain",
        ["山上"] = "Mountain",
        ["Mine"] = "Mine",
        ["Mines"] = "Mine",
        ["The Mines"] = "Mine",
        ["矿井"] = "Mine",
        ["矿洞"] = "Mine",
        ["矿山"] = "Mine",
        ["Beach"] = "Beach",
        ["海滩"] = "Beach",
        ["海边"] = "Beach",
        ["Forest"] = "Forest",
        ["Cindersap Forest"] = "Forest",
        ["森林"] = "Forest",
        ["煤矿森林"] = "Forest",
        ["BusStop"] = "BusStop",
        ["Bus Stop"] = "BusStop",
        ["巴士站"] = "BusStop",
        ["Trailer"] = "Trailer",
        ["Trailer_Big"] = "Trailer",
        ["Penny's Trailer"] = "Trailer",
        ["Pam's Trailer"] = "Trailer",
        ["Penny's House"] = "Trailer",
        ["Pam's House"] = "Trailer",
        ["Penny's Home"] = "Trailer",
        ["Pam's Home"] = "Trailer",
        ["潘妮家"] = "Trailer",
        ["潘妮的家"] = "Trailer",
        ["潘妮家里"] = "Trailer",
        ["帕姆家"] = "Trailer",
        ["帕姆的家"] = "Trailer",
        ["拖车"] = "Trailer",
        ["JoshHouse"] = "JoshHouse",
        ["Alex's House"] = "JoshHouse",
        ["亚历克斯家"] = "JoshHouse",
        ["HaleyHouse"] = "HaleyHouse",
        ["Haley's House"] = "HaleyHouse",
        ["Emily's House"] = "HaleyHouse",
        ["海莉家"] = "HaleyHouse",
        ["艾米丽家"] = "HaleyHouse",
        ["SamHouse"] = "SamHouse",
        ["Sam's House"] = "SamHouse",
        ["山姆家"] = "SamHouse",
        ["ScienceHouse"] = "ScienceHouse",
        ["Robin's House"] = "ScienceHouse",
        ["Sebastian's House"] = "ScienceHouse",
        ["Maru's House"] = "ScienceHouse",
        ["罗宾家"] = "ScienceHouse",
        ["塞巴斯蒂安家"] = "ScienceHouse",
        ["玛鲁家"] = "ScienceHouse",
        ["LeahHouse"] = "LeahHouse",
        ["Leah's Cottage"] = "LeahHouse",
        ["莉亚家"] = "LeahHouse",
        ["AnimalShop"] = "AnimalShop",
        ["Marnie's Ranch"] = "AnimalShop",
        ["玛妮牧场"] = "AnimalShop",
        ["玛妮家"] = "AnimalShop",
        ["ElliottHouse"] = "ElliottHouse",
        ["Elliott's Cabin"] = "ElliottHouse",
        ["艾利欧特家"] = "ElliottHouse",
        ["Blacksmith"] = "Blacksmith",
        ["铁匠铺"] = "Blacksmith",
        ["FishShop"] = "FishShop",
        ["鱼店"] = "FishShop",
        ["WizardHouse"] = "WizardHouse",
        ["Wizard's Tower"] = "WizardHouse",
        ["法师塔"] = "WizardHouse",
        ["Tent"] = "Tent",
        ["Linus's Tent"] = "Tent",
        ["莱纳斯帐篷"] = "Tent",
        ["Saloon"] = "Saloon",
        ["Stardrop Saloon"] = "Saloon",
        ["酒吧"] = "Saloon",
        ["星之果实酒吧"] = "Saloon",
        ["SeedShop"] = "SeedShop",
        ["Pierre's"] = "SeedShop",
        ["Pierre's General Store"] = "SeedShop",
        ["杂货店"] = "SeedShop",
        ["皮埃尔的杂货店"] = "SeedShop",
        ["ArchaeologyHouse"] = "ArchaeologyHouse",
        ["Museum"] = "ArchaeologyHouse",
        ["Library"] = "ArchaeologyHouse",
        ["博物馆"] = "ArchaeologyHouse",
        ["图书馆"] = "ArchaeologyHouse",
        ["Hospital"] = "Hospital",
        ["Clinic"] = "Hospital",
        ["诊所"] = "Hospital",
        ["医院"] = "Hospital",
        ["FlowerDance"] = "FlowerDance",
        ["Flower Dance"] = "FlowerDance",
        ["Flower Festival"] = "FlowerDance",
        ["花舞节"] = "FlowerDance",
        ["花舞节会场"] = "FlowerDance",
        ["Custom_GrampletonCoast"] = "Custom_GrampletonCoast",
        ["Grampleton Coast"] = "Custom_GrampletonCoast",
        ["SVE Coast"] = "Custom_GrampletonCoast",
        ["格兰普顿海岸"] = "Custom_GrampletonCoast",
        ["SVE 海岸"] = "Custom_GrampletonCoast",
        ["Custom_BlueMoonVineyard"] = "Custom_BlueMoonVineyard",
        ["Blue Moon Vineyard"] = "Custom_BlueMoonVineyard",
        ["Sophia's Vineyard"] = "Custom_BlueMoonVineyard",
        ["蓝月葡萄园"] = "Custom_BlueMoonVineyard",
        ["索菲亚的葡萄园"] = "Custom_BlueMoonVineyard",
        ["Custom_AuroraVineyard"] = "Custom_AuroraVineyard",
        ["Aurora Vineyard"] = "Custom_AuroraVineyard",
        ["极光葡萄园"] = "Custom_AuroraVineyard",
        ["Custom_ForestWest"] = "Custom_ForestWest",
        ["Forest West"] = "Custom_ForestWest",
        ["West Forest"] = "Custom_ForestWest",
        ["SVE Forest West"] = "Custom_ForestWest",
        ["西部森林"] = "Custom_ForestWest",
        ["Custom_SVESummit"] = "Custom_SVESummit",
        ["SVE Summit"] = "Custom_SVESummit",
        ["Summit"] = "Custom_SVESummit",
        ["山顶"] = "Custom_SVESummit",
        ["SVE 山顶"] = "Custom_SVESummit",
        ["Custom_GrandpasShedOutside"] = "Custom_GrandpasShedOutside",
        ["Grandpa's Shed"] = "Custom_GrandpasShedOutside",
        ["Grandpa's Shed Outside"] = "Custom_GrandpasShedOutside",
        ["爷爷的棚屋"] = "Custom_GrandpasShedOutside",
        ["Custom_JunimoWoods"] = "Custom_JunimoWoods",
        ["Junimo Woods"] = "Custom_JunimoWoods",
        ["祝尼魔森林"] = "Custom_JunimoWoods",
        ["Custom_EnchantedGrove"] = "Custom_EnchantedGrove",
        ["Enchanted Grove"] = "Custom_EnchantedGrove",
        ["魔法林地"] = "Custom_EnchantedGrove",
        ["Desert"] = "Desert",
        ["The Desert"] = "Desert",
        ["Calico Desert"] = "Desert",
        ["沙漠"] = "Desert",
        ["卡利科沙漠"] = "Desert",
        ["卡利可沙漠"] = "Desert",
        ["IslandSouth"] = "IslandSouth",
        ["Ginger Island"] = "IslandSouth",
        ["GingerIsland"] = "IslandSouth",
        ["Island South"] = "IslandSouth",
        ["姜岛"] = "IslandSouth",
        ["生姜岛"] = "IslandSouth",
        ["姜之岛"] = "IslandSouth",
        ["MovieTheater"] = "MovieTheater",
        ["Movie Theater"] = "MovieTheater",
        ["Movie Theatre"] = "MovieTheater",
        ["Cinema"] = "MovieTheater",
        ["电影院"] = "MovieTheater",
        ["影院"] = "MovieTheater",
        ["BathHouse_Entry"] = "BathHouse_Entry",
        ["BathHouse"] = "BathHouse_Entry",
        ["Bath House"] = "BathHouse_Entry",
        ["Bathhouse"] = "BathHouse_Entry",
        ["Spa"] = "BathHouse_Entry",
        ["Hot Spring"] = "BathHouse_Entry",
        ["Hot Springs"] = "BathHouse_Entry",
        ["温泉"] = "BathHouse_Entry",
        ["澡堂"] = "BathHouse_Entry",
        ["浴场"] = "BathHouse_Entry"
    };

    public static IReadOnlyCollection<string> KnownPublicOutingTargets { get; } =
        new ReadOnlyCollection<string>([.. PublicOutingTargets]);

    /// <summary>
    /// Finds every distinct supported outing destination explicitly named in visible text.
    /// ASCII aliases use word boundaries so, for example, "farmer" does not accidentally match
    /// the "farm" alias. Callers which infer a destination should accept exactly one result.
    /// </summary>
    public static IReadOnlyList<string> FindMentionedPublicOutingTargets(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return System.Array.Empty<string>();
        }

        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var matches = new List<string>();
        foreach (KeyValuePair<string, string> alias in Aliases)
        {
            if (!PublicOutingTargets.Contains(alias.Value)
                || !ContainsAlias(text, alias.Key)
                || !seen.Add(alias.Value))
            {
                continue;
            }

            matches.Add(alias.Value);
        }

        return matches;
    }

    private static bool ContainsAlias(string text, string alias)
    {
        bool requireAsciiWordBoundary = IsAsciiWordAlias(alias);
        int searchStart = 0;
        while (searchStart < text.Length)
        {
            int index = text.IndexOf(alias, searchStart, System.StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            int end = index + alias.Length;
            if (!requireAsciiWordBoundary
                || ((!IsAsciiWordCharacterBefore(text, index))
                    && !IsAsciiWordCharacterAt(text, end)))
            {
                return true;
            }

            searchStart = index + 1;
        }

        return false;
    }

    private static bool IsAsciiWordAlias(string alias)
    {
        foreach (char character in alias)
        {
            if (character > 127)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiWordCharacterBefore(string text, int index)
    {
        return index > 0 && IsAsciiWordCharacter(text[index - 1]);
    }

    private static bool IsAsciiWordCharacterAt(string text, int index)
    {
        return index < text.Length && IsAsciiWordCharacter(text[index]);
    }

    private static bool IsAsciiWordCharacter(char character)
    {
        return character <= 127
            && (char.IsLetterOrDigit(character) || character == '_');
    }

    public static string Normalize(string value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            // Deliberately no "Town" last-resort: a blank value with a blank fallback stays
            // blank, so downstream guards and fallbacks can actually fire — the companion-outing
            // "a supported outing destination is required" rejection, the behavior-influence
            // store's current-location fallback, and the festival anchor's current-map fallback
            // all rely on blank staying blank. Call sites that want a "Town" default pass "Town"
            // explicitly as the fallback (e.g. DialogueBehaviorInfluenceStore.NormalizeForStore).
            return string.Empty;
        }

        if (Aliases.TryGetValue(candidate, out string? mapped))
        {
            return mapped;
        }

        string collapsed = CollapseAdjacentDuplicateCharacters(candidate);
        if (!string.Equals(collapsed, candidate, System.StringComparison.Ordinal)
            && Aliases.TryGetValue(collapsed, out mapped))
        {
            return mapped;
        }

        return candidate;
    }

    private static string CollapseAdjacentDuplicateCharacters(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 2)
        {
            return value;
        }

        var result = new System.Text.StringBuilder(value.Length);
        char previous = '\0';
        foreach (char character in value)
        {
            if (character != previous)
            {
                result.Append(character);
                previous = character;
            }
        }

        return result.ToString();
    }

    public static bool IsKnownPublicOutingTarget(string locationName)
    {
        string normalized = Normalize(locationName, string.Empty);
        return PublicOutingTargets.Contains(normalized);
    }

    public static string GetLabel(string locationName)
    {
        return locationName switch
        {
            "Farm" => "the farm",
            "Town" => "Pelican Town",
            "Mountain" => "the mountain",
            "Mine" => "the mines",
            "Beach" => "the beach",
            "Forest" => "Cindersap Forest",
            "BusStop" => "the bus stop",
            "Trailer" => "Penny and Pam's trailer",
            "JoshHouse" => "Alex's house",
            "HaleyHouse" => "Haley and Emily's house",
            "SamHouse" => "Sam's house",
            "ScienceHouse" => "Robin's house",
            "LeahHouse" => "Leah's cottage",
            "AnimalShop" => "Marnie's ranch",
            "ElliottHouse" => "Elliott's cabin",
            "Blacksmith" => "the blacksmith",
            "FishShop" => "the fish shop",
            "WizardHouse" => "the Wizard's tower",
            "Tent" => "Linus's tent",
            "Saloon" => "the Stardrop Saloon",
            "SeedShop" => "Pierre's General Store",
            "ArchaeologyHouse" => "the museum and library",
            "Hospital" => "the clinic",
            "FlowerDance" => "the edge of the Flower Dance meadow",
            "Custom_GrampletonCoast" => "Grampleton Coast",
            "Custom_BlueMoonVineyard" => "Blue Moon Vineyard",
            "Custom_AuroraVineyard" => "Aurora Vineyard",
            "Custom_ForestWest" => "SVE's western forest",
            "Custom_SVESummit" => "the SVE summit",
            "Custom_GrandpasShedOutside" => "Grandpa's shed",
            "Custom_JunimoWoods" => "Junimo Woods",
            "Custom_EnchantedGrove" => "the enchanted grove",
            "Desert" => "the Calico Desert",
            "IslandSouth" => "Ginger Island's beach",
            "MovieTheater" => "the movie theater",
            "BathHouse_Entry" => "the spa by the railroad",
            _ => locationName
        };
    }

    public static string GetLocalizedLabel(string locationName)
    {
        string normalized = Normalize(locationName, locationName);
        string key = normalized switch
        {
            "Farm" => "location.farm",
            "Town" => "location.town",
            "Mountain" => "location.mountain",
            "Mine" => "location.mine",
            "Beach" => "location.beach",
            "Forest" => "location.forest",
            "BusStop" => "location.busStop",
            "Trailer" => "location.trailer",
            "JoshHouse" => "location.joshHouse",
            "HaleyHouse" => "location.haleyHouse",
            "SamHouse" => "location.samHouse",
            "ScienceHouse" => "location.scienceHouse",
            "LeahHouse" => "location.leahHouse",
            "AnimalShop" => "location.animalShop",
            "ElliottHouse" => "location.elliottHouse",
            "Blacksmith" => "location.blacksmith",
            "FishShop" => "location.fishShop",
            "WizardHouse" => "location.wizardHouse",
            "Tent" => "location.tent",
            "Saloon" => "location.saloon",
            "SeedShop" => "location.seedShop",
            "ArchaeologyHouse" => "location.archaeologyHouse",
            "Hospital" => "location.hospital",
            "FlowerDance" => "location.flowerDance",
            "Custom_GrampletonCoast" => "location.grampletonCoast",
            "Custom_BlueMoonVineyard" => "location.blueMoonVineyard",
            "Custom_AuroraVineyard" => "location.auroraVineyard",
            "Custom_ForestWest" => "location.forestWest",
            "Custom_SVESummit" => "location.sveSummit",
            "Custom_GrandpasShedOutside" => "location.grandpasShed",
            "Custom_JunimoWoods" => "location.junimoWoods",
            "Custom_EnchantedGrove" => "location.enchantedGrove",
            "Desert" => "location.desert",
            "IslandSouth" => "location.islandSouth",
            "MovieTheater" => "location.movieTheater",
            "BathHouse_Entry" => "location.bathHouse",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(key) ? GetLabel(normalized) : I18n.Get(key);
    }

    public static string GetChineseLabel(string locationName)
    {
        return locationName switch
        {
            "Farm" => "农场",
            "Town" => "鹈鹕镇",
            "Mountain" => "山上",
            "Mine" => "矿井",
            "Beach" => "海边",
            "Forest" => "煤矿森林",
            "BusStop" => "巴士站",
            "Trailer" => "潘妮和帕姆的家",
            "Saloon" => "星之果实酒吧",
            "SeedShop" => "皮埃尔的杂货店",
            "ArchaeologyHouse" => "博物馆和图书馆",
            "Hospital" => "诊所",
            "JoshHouse" => "亚历克斯家",
            "HaleyHouse" => "海莉和艾米丽家",
            "SamHouse" => "山姆家",
            "ScienceHouse" => "罗宾家",
            "LeahHouse" => "莉亚家",
            "AnimalShop" => "玛妮牧场",
            "ElliottHouse" => "艾利欧特小屋",
            "Blacksmith" => "铁匠铺",
            "FishShop" => "鱼店",
            "WizardHouse" => "法师塔",
            "Tent" => "莱纳斯的帐篷",
            "FlowerDance" => "花舞节边缘",
            "Custom_GrampletonCoast" => "格兰普顿海岸",
            "Custom_BlueMoonVineyard" => "蓝月葡萄园",
            "Custom_AuroraVineyard" => "极光葡萄园",
            "Custom_ForestWest" => "SVE 西部森林",
            "Custom_SVESummit" => "SVE 山顶",
            "Custom_GrandpasShedOutside" => "爷爷的棚屋",
            "Custom_JunimoWoods" => "祝尼魔森林",
            "Custom_EnchantedGrove" => "魔法林地",
            "Desert" => "卡利科沙漠",
            "IslandSouth" => "姜岛海滩",
            "MovieTheater" => "电影院",
            "BathHouse_Entry" => "铁路温泉",
            _ => locationName
        };
    }
}
