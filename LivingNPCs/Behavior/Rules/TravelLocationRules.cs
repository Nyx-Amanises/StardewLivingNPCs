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
        "FlowerDance",
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
        ["魔法林地"] = "Custom_EnchantedGrove"
    };

    public static IReadOnlyCollection<string> KnownPublicOutingTargets { get; } =
        new ReadOnlyCollection<string>([.. PublicOutingTargets]);

    public static string Normalize(string value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (Aliases.TryGetValue(candidate, out string? mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(candidate) ? "Town" : candidate;
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
            _ => locationName
        };
    }
}
