using System.Collections.Generic;

namespace LivingNPCs.Behavior;

internal static class TravelLocationRules
{
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
        ["医院"] = "Hospital"
    };

    public static string Normalize(string value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (Aliases.TryGetValue(candidate, out string? mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(candidate) ? "Town" : candidate;
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
            _ => locationName
        };
    }
}
