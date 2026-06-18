using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed record CompanionOutingAnchor(
    Point Tile,
    int FacingDirection,
    string SemanticLabel,
    int Score
);

internal sealed record CompanionOutingAnchorPreview(
    int X,
    int Y,
    int FacingDirection,
    string SemanticLabel,
    int Score
);

internal sealed class CompanionOutingAnchorSelector
{
    private sealed record AuthoredAnchor(
        Point Tile,
        int FacingDirection,
        string Label,
        string[] Styles,
        string[]? Focuses = null,
        int Priority = 0
    );

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AuthoredAnchor>> AuthoredAnchors =
        new Dictionary<string, IReadOnlyList<AuthoredAnchor>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Farm"] =
            [
                new(new Point(64, 15), 2, "near the farmhouse porch without blocking the door", ["quiet", "visit"], ["porch", "farm"], 24),
                new(new Point(72, 16), 2, "beside the farm path where the view opens up", ["scenic", "quiet", "visit"], ["farm", "path"], 20),
                new(new Point(58, 17), 1, "at a calm farmyard spot with room to talk", ["scenic", "quiet", "visit"], ["farm", "yard"], 18)
            ],
            ["Town"] =
            [
                new(new Point(44, 57), 2, "in the town square near the fountain", ["social", "quiet", "visit"], ["town_center", "stroll", "square", "fountain"], 34),
                new(new Point(52, 56), 3, "beside the notice board path without blocking traffic", ["social", "quiet", "visit"], ["town_center", "stroll", "square", "community"], 30),
                new(new Point(58, 66), 3, "along the central town walkway", ["social", "quiet", "visit"], ["town_center", "stroll", "path"], 24),
                new(new Point(34, 72), 0, "on the river path through town", ["scenic", "quiet", "visit"], ["river", "path"], 22)
            ],
            ["BusStop"] =
            [
                new(new Point(11, 11), 2, "beside the bus stop bench", ["quiet", "visit"], ["bench", "bus"], 30),
                new(new Point(16, 10), 3, "near the road sign without blocking the bus", ["visit"], ["bus", "road"], 20),
                new(new Point(8, 8), 1, "on the quiet path by the tunnel road", ["scenic", "quiet", "visit"], ["path", "road"], 16)
            ],
            ["Mine"] =
            [
                new(new Point(8, 9), 2, "near the mine entrance where adventurers gather", ["visit", "quiet"], ["mine", "entrance"], 30),
                new(new Point(12, 13), 3, "beside the mine cart track", ["browse", "visit"], ["mine", "cart"], 20),
                new(new Point(6, 14), 1, "at a safer edge of the mine lobby", ["quiet", "visit"], ["mine"], 16)
            ],
            ["Trailer"] =
            [
                new(new Point(8, 8), 2, "in the trailer's small living area", ["quiet", "visit"], ["home", "living"], 30),
                new(new Point(12, 9), 3, "near the kitchen side of the trailer", ["quiet", "visit"], ["home", "kitchen"], 20),
                new(new Point(6, 11), 1, "beside the trailer walkway with room to talk", ["quiet", "visit"], ["home"], 16)
            ],
            ["JoshHouse"] =
            [
                new(new Point(10, 14), 0, "in the public sitting area of Alex's house", ["quiet", "visit"], ["home", "sitting"], 28),
                new(new Point(14, 16), 3, "beside the entry path inside Alex's house", ["visit"], ["home"], 16)
            ],
            ["HaleyHouse"] =
            [
                new(new Point(13, 17), 0, "near the living room table at Haley and Emily's house", ["quiet", "visit"], ["home", "sitting"], 28),
                new(new Point(8, 16), 1, "beside the kitchen side of the house", ["visit"], ["home", "kitchen"], 18)
            ],
            ["SamHouse"] =
            [
                new(new Point(14, 19), 0, "near the family living room in Sam's house", ["quiet", "visit"], ["home", "sitting"], 28),
                new(new Point(9, 18), 1, "beside the kitchen walkway at Sam's house", ["visit"], ["home", "kitchen"], 18)
            ],
            ["ScienceHouse"] =
            [
                new(new Point(16, 21), 0, "near Robin's shop counter without blocking customers", ["browse", "visit"], ["shop", "counter"], 30),
                new(new Point(9, 16), 1, "beside the family sitting area at Robin's house", ["quiet", "visit"], ["home", "sitting"], 24),
                new(new Point(22, 19), 3, "near the lab side of the mountain house", ["browse", "visit"], ["lab", "study"], 18)
            ],
            ["LeahHouse"] =
            [
                new(new Point(6, 8), 2, "near Leah's cottage work table", ["quiet", "browse", "visit"], ["home", "art"], 30),
                new(new Point(9, 10), 3, "beside the cozy cottage sitting area", ["quiet", "visit"], ["home", "sitting"], 22)
            ],
            ["AnimalShop"] =
            [
                new(new Point(12, 16), 0, "near Marnie's ranch counter", ["browse", "visit"], ["shop", "counter"], 30),
                new(new Point(7, 15), 1, "beside the ranch sitting area", ["quiet", "visit"], ["home", "sitting"], 20)
            ],
            ["ElliottHouse"] =
            [
                new(new Point(5, 7), 2, "beside Elliott's writing desk", ["quiet", "browse", "visit"], ["home", "writing"], 32),
                new(new Point(7, 10), 0, "near the cabin's small sitting space", ["quiet", "visit"], ["home", "sitting"], 20)
            ],
            ["Blacksmith"] =
            [
                new(new Point(7, 14), 0, "near Clint's public counter", ["browse", "visit"], ["shop", "counter"], 32),
                new(new Point(13, 13), 3, "beside the forge area without getting in the way", ["browse", "visit"], ["forge", "work"], 24),
                new(new Point(5, 17), 1, "at the public side of the blacksmith shop", ["quiet", "visit"], ["shop"], 16)
            ],
            ["FishShop"] =
            [
                new(new Point(5, 4), 2, "near Willy's shop counter", ["browse", "visit"], ["shop", "counter"], 32),
                new(new Point(7, 8), 0, "beside the fish shop display barrels", ["browse", "visit"], ["shop", "display"], 24),
                new(new Point(3, 9), 1, "near the quiet side of Willy's shop", ["quiet", "visit"], ["shop"], 16)
            ],
            ["WizardHouse"] =
            [
                new(new Point(5, 12), 0, "near the Wizard's study circle", ["quiet", "browse", "visit"], ["study", "magic"], 32),
                new(new Point(8, 15), 3, "beside the tower's public walkway", ["quiet", "visit"], ["tower"], 18)
            ],
            ["Tent"] =
            [
                new(new Point(4, 5), 2, "near the front of Linus's tent without crowding it", ["quiet", "visit"], ["tent", "camp"], 28),
                new(new Point(6, 7), 3, "beside the small mountain camp clearing", ["scenic", "quiet", "visit"], ["camp", "clearing"], 20)
            ],
            ["SeedShop"] =
            [
                new(new Point(10, 17), 0, "beside the public shop shelves", ["browse", "visit"], ["shop", "shelves"], 28),
                new(new Point(12, 16), 3, "along a public aisle in the general store", ["browse", "visit"], ["shop", "aisle"], 22),
                new(new Point(8, 15), 1, "near the public display shelves", ["browse", "visit"], ["shop", "display"], 20)
            ],
            ["Beach"] =
            [
                new(new Point(32, 34), 2, "where the tide rolls in along the open shoreline", ["scenic", "quiet", "visit"], ["shore", "waves"], 30),
                new(new Point(44, 24), 2, "on a quiet stretch of sand with room to watch the waves", ["scenic", "quiet", "visit"], ["shore", "waves"], 28),
                new(new Point(60, 13), 1, "near the pier with a view across the water", ["scenic", "quiet", "visit"], ["pier", "dock"], 24),
                new(new Point(38, 31), 2, "near the foam line where the waves keep breaking", ["scenic", "quiet"], ["shore", "waves"], 24),
                new(new Point(56, 9), 1, "beside the dock approach with the sea beside them", ["scenic", "visit"], ["pier", "dock"], 18)
            ],
            ["Mountain"] =
            [
                new(new Point(31, 20), 0, "near the mountain lake where a quiet conversation fits", ["scenic", "quiet", "visit"], ["lake"], 32),
                new(new Point(39, 9), 1, "along the north shore of the mountain lake", ["scenic", "quiet", "visit"], ["lake"], 28),
                new(new Point(54, 32), 0, "by the lower mountain lake path", ["scenic", "quiet", "visit"], ["lake", "path"], 22),
                new(new Point(42, 13), 2, "at an open mountain overlook above the lake", ["scenic", "quiet", "visit"], ["overlook", "lake"], 22),
                new(new Point(15, 10), 1, "beside the mountain path", ["scenic", "visit"], ["path"], 16)
            ],
            ["Forest"] =
            [
                new(new Point(58, 27), 2, "near the forest river", ["scenic", "quiet", "visit"], ["river"], 30),
                new(new Point(87, 36), 0, "in a quiet forest clearing", ["scenic", "quiet", "visit"], ["clearing"], 26),
                new(new Point(34, 34), 1, "beside a wooded path", ["scenic", "visit"], ["path"], 18)
            ],
            ["Saloon"] =
            [
                new(new Point(11, 20), 0, "leaning near the public side of Gus's bar counter", ["social", "visit"], ["bar"], 32),
                new(new Point(13, 19), 0, "close to the bar where conversation blends into the room", ["social", "visit"], ["bar"], 28),
                new(new Point(20, 17), 0, "beside one of the saloon's side tables", ["social", "quiet", "visit"], ["table"], 26),
                new(new Point(26, 18), 3, "along the quieter side of the saloon", ["social", "quiet", "visit"], ["table", "quiet"], 22),
                new(new Point(12, 18), 1, "near the public seating area", ["social", "visit"], ["table"], 20)
            ],
            ["ArchaeologyHouse"] =
            [
                new(new Point(18, 14), 2, "at the library reading tables with the shelves just behind them", ["browse", "quiet", "visit"], ["library", "reading"], 36),
                new(new Point(20, 14), 2, "beside the library reading tables where Penny often tutors the children", ["browse", "quiet", "visit"], ["library", "teaching", "reading"], 34),
                new(new Point(19, 16), 0, "near the library table seats without blocking the book aisles", ["browse", "quiet", "visit"], ["library", "reading"], 30),
                new(new Point(13, 14), 0, "between the reading tables and the public book stacks", ["browse", "quiet"], ["library", "reading"], 20),
                new(new Point(11, 9), 0, "beside a public museum display", ["browse", "quiet", "visit"], ["museum", "exhibit"], 18),
                new(new Point(17, 9), 0, "along the museum's public exhibit aisle", ["browse", "visit"], ["museum", "exhibit"], 16)
            ],
            ["Hospital"] =
            [
                new(new Point(9, 17), 0, "in the clinic's public waiting area", ["quiet", "visit"], ["clinic", "waiting"], 32),
                new(new Point(12, 16), 3, "along the side of the clinic waiting room", ["quiet", "visit"], ["clinic", "waiting"], 24)
            ],
            ["FlowerDance"] =
            [
                new(new Point(15, 33), 1, "on the meadow edge just outside the main flower dance circle", ["festival", "quiet", "visit"]),
                new(new Point(20, 35), 3, "beside the flower dance path where a slow walk will not block anyone", ["festival", "quiet", "visit"]),
                new(new Point(32, 37), 3, "near the lower edge of the festival meadow", ["festival", "quiet", "visit"])
            ],
            ["Custom_GrampletonCoast"] =
            [
                new(new Point(38, 18), 2, "on the Grampleton coast path facing the open surf", ["scenic", "quiet", "visit"]),
                new(new Point(54, 29), 2, "near the long shoreline where the waves are easy to watch", ["scenic", "quiet", "visit"]),
                new(new Point(23, 36), 1, "along the coastal walk with the sea beside them", ["scenic", "visit"])
            ],
            ["Custom_BlueMoonVineyard"] =
            [
                new(new Point(25, 55), 2, "at the vineyard overlook where the rows open toward the water", ["scenic", "quiet", "visit"]),
                new(new Point(30, 48), 0, "beside the lower Blue Moon Vineyard path", ["scenic", "quiet", "visit"]),
                new(new Point(21, 32), 0, "near the vineyard work rows without getting in the way", ["scenic", "quiet", "visit"])
            ],
            ["Custom_AuroraVineyard"] =
            [
                new(new Point(13, 17), 2, "among the restored Aurora Vineyard rows", ["scenic", "quiet", "visit"]),
                new(new Point(20, 17), 2, "beside the vineyard path where the old estate feels lived-in again", ["scenic", "quiet", "visit"]),
                new(new Point(11, 8), 2, "near the quiet upper vineyard walk", ["scenic", "quiet", "visit"])
            ],
            ["Custom_ForestWest"] =
            [
                new(new Point(57, 18), 2, "near the western forest pond where the trees thin out", ["scenic", "quiet", "visit"]),
                new(new Point(53, 28), 0, "on the SVE western forest path beside the river bend", ["scenic", "quiet", "visit"]),
                new(new Point(54, 117), 2, "in the deep western forest clearing", ["scenic", "quiet", "visit"])
            ],
            ["Custom_SVESummit"] =
            [
                new(new Point(11, 13), 0, "at the summit edge with the whole valley below", ["scenic", "quiet", "visit"]),
                new(new Point(18, 15), 0, "on the upper summit path where the view opens wide", ["scenic", "quiet", "visit"]),
                new(new Point(24, 20), 3, "beside the summit trail with room to linger", ["scenic", "quiet", "visit"])
            ],
            ["Custom_GrandpasShedOutside"] =
            [
                new(new Point(8, 8), 2, "outside Grandpa's shed where the old path settles into quiet", ["scenic", "quiet", "visit"]),
                new(new Point(12, 12), 3, "beside the shed yard away from the doorway", ["quiet", "visit"]),
                new(new Point(6, 14), 1, "near the overgrown shed-side path", ["scenic", "quiet", "visit"])
            ],
            ["Custom_JunimoWoods"] =
            [
                new(new Point(32, 95), 0, "at the lower Junimo Woods clearing", ["scenic", "quiet", "visit"]),
                new(new Point(36, 99), 2, "among the quiet Junimo Woods paths", ["scenic", "quiet", "visit"]),
                new(new Point(47, 101), 3, "beside the hidden woods trail", ["scenic", "quiet", "visit"])
            ],
            ["Custom_EnchantedGrove"] =
            [
                new(new Point(12, 13), 2, "inside the enchanted grove where the air feels hushed", ["scenic", "quiet", "visit"]),
                new(new Point(17, 15), 3, "near the grove path without blocking the nexus", ["scenic", "quiet", "visit"]),
                new(new Point(9, 19), 1, "along the quieter side of the enchanted grove", ["scenic", "quiet", "visit"])
            ],
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AuthoredAnchor>> SveAuthoredAnchors =
        new Dictionary<string, IReadOnlyList<AuthoredAnchor>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Town"] =
            [
                new(new Point(66, 60), 2, "in the SVE town square beside the fountain", ["social", "quiet", "visit"], ["town_center", "stroll", "square", "fountain"], 44),
                new(new Point(72, 54), 2, "on the SVE central town walkway by the plaza", ["social", "quiet", "visit"], ["town_center", "stroll", "square", "path"], 40),
                new(new Point(76, 54), 3, "near the SVE notice board approach without blocking traffic", ["social", "quiet", "visit"], ["town_center", "stroll", "community", "path"], 34),
                new(new Point(59, 47), 2, "along the upper SVE town square path", ["social", "quiet", "visit"], ["town_center", "stroll", "square", "path"], 30),
                new(new Point(96, 65), 1, "by the SVE Pelican Town park path", ["scenic", "quiet", "visit"], ["park", "path"], 24),
                new(new Point(53, 52), 2, "beside a quieter SVE town center path", ["quiet", "visit"], ["town_center", "stroll", "path"], 20)
            ]
        };

    private readonly Func<GameLocation, Point, NPC?, bool> isSafeDestinationTile;

    public CompanionOutingAnchorSelector(Func<GameLocation, Point, NPC?, bool> isSafeDestinationTile)
    {
        this.isSafeDestinationTile = isSafeDestinationTile;
    }

    public bool TrySelect(
        NPC npc,
        GameLocation location,
        string targetLocation,
        string sourceLocation,
        string activityStyle,
        string reason,
        int totalDays,
        IReadOnlySet<Point> reservedTiles,
        out CompanionOutingAnchor? anchor)
    {
        var candidates = new List<CompanionOutingAnchor>();
        var entryTiles = GetLikelyEntryTiles(location, sourceLocation).ToList();
        string focus = DetermineAnchorFocus(targetLocation, activityStyle, reason, npc.Name);

        if (TryGetAuthoredAnchors(targetLocation, location, out var authored))
        {
            foreach (var candidate in authored)
            {
                if (!candidate.Styles.Contains(activityStyle, StringComparer.OrdinalIgnoreCase)
                    || !this.IsUsable(location, candidate.Tile, npc, reservedTiles))
                {
                    continue;
                }

                candidates.Add(new CompanionOutingAnchor(
                    candidate.Tile,
                    candidate.FacingDirection,
                    candidate.Label,
                    200
                        + candidate.Priority
                        + ScoreAnchorFocus(candidate, focus)
                        + ScoreTile(location, candidate.Tile, activityStyle, entryTiles)
                ));
            }
        }

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                var tile = new Point(x, y);
                if (!this.IsUsable(location, tile, npc, reservedTiles))
                {
                    continue;
                }

                int score = ScoreTile(location, tile, activityStyle, entryTiles);
                if (score < 0)
                {
                    continue;
                }

                int facingDirection = FindInterestingFacingDirection(location, tile, activityStyle);
                candidates.Add(new CompanionOutingAnchor(
                    tile,
                    facingDirection,
                    BuildFallbackLabel(targetLocation, activityStyle),
                    score
                ));
            }
        }

        var best = candidates
            .GroupBy(candidate => candidate.Tile)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Tile.X)
            .ThenBy(candidate => candidate.Tile.Y)
            .Take(3)
            .ToList();
        if (best.Count == 0)
        {
            anchor = null;
            return false;
        }

        int index = CompanionOutingRules.SelectStableTopCandidateIndex(npc.Name, targetLocation, totalDays, best.Count);
        anchor = best[index];
        return true;
    }

    internal static IReadOnlyList<CompanionOutingAnchorPreview> GetAuthoredAnchorPreview(
        string npcName,
        string targetLocation,
        string activityStyle,
        string reason)
    {
        string focus = DetermineAnchorFocus(targetLocation, activityStyle, reason, npcName);
        return TryGetAuthoredAnchorsForPreview(targetLocation, useSveTownAnchors: false, out var authored)
            ? authored
                .Where(candidate => candidate.Styles.Contains(activityStyle, StringComparer.OrdinalIgnoreCase))
                .Select(candidate => new CompanionOutingAnchorPreview(
                    candidate.Tile.X,
                    candidate.Tile.Y,
                    candidate.FacingDirection,
                    candidate.Label,
                    candidate.Priority + ScoreAnchorFocus(candidate, focus)
                ))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.X)
                .ThenBy(candidate => candidate.Y)
                .ToList()
            : [];
    }

    internal static IReadOnlyList<CompanionOutingAnchorPreview> GetAuthoredAnchorPreview(
        string npcName,
        string targetLocation,
        string activityStyle,
        string reason,
        bool useSveTownAnchors)
    {
        string focus = DetermineAnchorFocus(targetLocation, activityStyle, reason, npcName);
        return TryGetAuthoredAnchorsForPreview(targetLocation, useSveTownAnchors, out var authored)
            ? authored
                .Where(candidate => candidate.Styles.Contains(activityStyle, StringComparer.OrdinalIgnoreCase))
                .Select(candidate => new CompanionOutingAnchorPreview(
                    candidate.Tile.X,
                    candidate.Tile.Y,
                    candidate.FacingDirection,
                    candidate.Label,
                    candidate.Priority + ScoreAnchorFocus(candidate, focus)
                ))
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.X)
                .ThenBy(candidate => candidate.Y)
                .ToList()
            : [];
    }

    private static bool TryGetAuthoredAnchors(
        string targetLocation,
        GameLocation location,
        out IReadOnlyList<AuthoredAnchor> authored)
    {
        bool useSveTown = string.Equals(targetLocation, "Town", StringComparison.OrdinalIgnoreCase)
            && ModCompatibility.EnableSve
            && IsSveTownMap(location);
        return TryGetAuthoredAnchorsForPreview(targetLocation, useSveTown, out authored);
    }

    private static bool TryGetAuthoredAnchorsForPreview(
        string targetLocation,
        bool useSveTownAnchors,
        out IReadOnlyList<AuthoredAnchor> authored)
    {
        if (useSveTownAnchors && SveAuthoredAnchors.TryGetValue(targetLocation, out IReadOnlyList<AuthoredAnchor>? sveAuthored))
        {
            authored = sveAuthored;
            return true;
        }

        if (AuthoredAnchors.TryGetValue(targetLocation, out IReadOnlyList<AuthoredAnchor>? defaultAuthored))
        {
            authored = defaultAuthored;
            return true;
        }

        authored = [];
        return false;
    }

    private static bool IsSveTownMap(GameLocation location)
    {
        if (!string.Equals(location.Name, "Town", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        return width >= 130 && height >= 116;
    }

    private bool IsUsable(
        GameLocation location,
        Point tile,
        NPC npc,
        IReadOnlySet<Point> reservedTiles)
    {
        if (reservedTiles.Contains(tile)
            || !this.isSafeDestinationTile(location, tile, npc)
            || IsWarpOrDoorLikeTile(location, tile))
        {
            return false;
        }

        return CountOpenNeighbors(location, tile, npc) >= 2;
    }

    private static int ScoreTile(
        GameLocation location,
        Point tile,
        string activityStyle,
        IReadOnlyList<Point> entryTiles)
    {
        int score = 50;
        int openNeighbors = CountOpenNeighbors(location, tile, null);
        score += openNeighbors switch
        {
            4 => 24,
            3 => 12,
            2 => -8,
            _ => -40
        };

        if (entryTiles.Count > 0)
        {
            int entryDistance = entryTiles.Min(entry => ManhattanDistance(entry, tile));
            if (entryDistance <= 3)
            {
                return -100;
            }

            score += entryDistance switch
            {
                <= 6 => 8,
                <= 14 => 28,
                <= 22 => 12,
                _ => -12
            };
        }

        int nearbyWater = CountNearbyWaterTiles(location, tile, 5);
        int nearbyActions = CountNearbyActionTiles(location, tile, 2);
        score += activityStyle switch
        {
            "scenic" => Math.Min(40, nearbyWater * 5),
            "browse" => Math.Min(36, nearbyActions * 9),
            "quiet" => Math.Min(20, nearbyWater * 3) + (location.characters.Count == 0 ? 10 : 0),
            "social" => Math.Min(18, location.characters.Count * 4),
            _ => Math.Min(12, nearbyWater * 2) + Math.Min(12, nearbyActions * 3)
        };

        return score;
    }

    private static string DetermineAnchorFocus(
        string targetLocation,
        string activityStyle,
        string reason,
        string npcName)
    {
        string text = reason ?? string.Empty;
        if (ContainsAny(text, "吧台", "酒保", "喝一杯", "喝点", "bar counter", "bartop", "drink"))
        {
            return "bar";
        }

        if (ContainsAny(text, "桌", "坐", "座位", "餐桌", "table", "sit", "seat"))
        {
            return "table";
        }

        if (ContainsAny(text, "海浪", "浪", "海边", "沙滩", "shore", "waves", "surf", "sand"))
        {
            return "shore";
        }

        if (ContainsAny(text, "码头", "钓鱼", "pier", "dock", "fish"))
        {
            return "pier";
        }

        if (ContainsAny(text, "湖", "湖边", "lake"))
        {
            return "lake";
        }

        if (ContainsAny(text, "河", "河边", "river"))
        {
            return "river";
        }

        if (ContainsAny(text, "柜台", "买", "商店", "counter", "shop", "store", "buy"))
        {
            return "counter";
        }

        if (ContainsAny(text, "熔炉", "打铁", "铁匠", "forge", "anvil", "blacksmith"))
        {
            return "forge";
        }

        if (ContainsAny(text, "候诊", "诊所", "clinic", "doctor", "waiting"))
        {
            return "waiting";
        }

        if (ContainsAny(text, "家", "屋", "客厅", "home", "house", "living room"))
        {
            return "home";
        }

        if (targetLocation == "ArchaeologyHouse")
        {
            if (ContainsAny(text, "图书馆", "书", "书架", "看书", "读书", "翻书", "学习", "教", "孩子", "library", "book", "bookshelf", "read", "reading", "study", "teach", "tutor"))
            {
                return "library";
            }

            if (ContainsAny(text, "博物馆", "展品", "文物", "古物", "museum", "exhibit", "display", "artifact", "archaeology"))
            {
                return "museum";
            }

            if (string.Equals(npcName, "Penny", StringComparison.OrdinalIgnoreCase))
            {
                return "library";
            }
        }

        if (targetLocation == "Town")
        {
            if (ContainsAny(text, "喷泉", "广场", "镇中心", "市中心", "fountain", "square", "town center", "town centre", "plaza"))
            {
                return "town_center";
            }

            if (ContainsAny(text, "公告栏", "告示板", "notice board", "bulletin", "community board"))
            {
                return "community";
            }

            if (ContainsAny(text, "公园", "长椅", "park", "bench"))
            {
                return "park";
            }

            if (ContainsAny(text, "镇上", "鹈鹕镇", "逛", "散步", "走走", "绕一圈", "town", "around town", "stroll", "walk"))
            {
                return "stroll";
            }

            return "town_center";
        }

        if (ContainsAny(text, "货架", "展示", "商品", "shelf", "shelves", "display"))
        {
            return "display";
        }

        return activityStyle;
    }

    private static int ScoreAnchorFocus(AuthoredAnchor candidate, string focus)
    {
        if (candidate.Focuses == null || candidate.Focuses.Length == 0 || string.IsNullOrWhiteSpace(focus))
        {
            return 0;
        }

        return candidate.Focuses.Contains(focus, StringComparer.OrdinalIgnoreCase)
            ? 80
            : -45;
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

    private static int CountOpenNeighbors(GameLocation location, Point tile, NPC? ignoredNpc)
    {
        int count = 0;
        foreach (Point neighbor in GetCardinalNeighbors(tile))
        {
            if (neighbor.X < 0
                || neighbor.Y < 0
                || neighbor.X >= location.Map.Layers[0].LayerWidth
                || neighbor.Y >= location.Map.Layers[0].LayerHeight)
            {
                continue;
            }

            var vector = new Vector2(neighbor.X, neighbor.Y);
            if (location.isTileLocationOpen(vector)
                && location.isTilePassable(vector)
                && !location.characters.Any(candidate => candidate != ignoredNpc && candidate.TilePoint == neighbor))
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<Point> GetLikelyEntryTiles(GameLocation location, string sourceLocation)
    {
        var matching = location.warps
            .Where(warp => string.Equals(
                TravelLocationRules.Normalize(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
                sourceLocation,
                StringComparison.OrdinalIgnoreCase))
            .Select(warp => new Point(warp.X, warp.Y))
            .ToList();

        return matching.Count > 0
            ? matching
            : location.warps.Select(warp => new Point(warp.X, warp.Y));
    }

    private static bool IsWarpOrDoorLikeTile(GameLocation location, Point tile)
    {
        if (location.warps.Any(warp => ManhattanDistance(new Point(warp.X, warp.Y), tile) <= 3))
        {
            return true;
        }

        return HasTileProperty(location, tile, "Action")
            || HasTileProperty(location, tile, "TouchAction")
            || HasTileProperty(location, tile, "NoPath");
    }

    private static int CountNearbyWaterTiles(GameLocation location, Point center, int radius)
    {
        int count = 0;
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            {
                if (IsInBounds(location, x, y) && location.isWaterTile(x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountNearbyActionTiles(GameLocation location, Point center, int radius)
    {
        int count = 0;
        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
            {
                var tile = new Point(x, y);
                if (HasTileProperty(location, tile, "Action") || HasTileProperty(location, tile, "TouchAction"))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool HasTileProperty(GameLocation location, Point tile, string property)
    {
        if (!IsInBounds(location, tile.X, tile.Y))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(location.doesTileHaveProperty(tile.X, tile.Y, property, "Buildings", false))
            || !string.IsNullOrWhiteSpace(location.doesTileHaveProperty(tile.X, tile.Y, property, "Back", false));
    }

    private static int FindInterestingFacingDirection(GameLocation location, Point tile, string activityStyle)
    {
        foreach ((Point neighbor, int direction) in new[]
        {
            (new Point(tile.X, tile.Y - 1), 0),
            (new Point(tile.X + 1, tile.Y), 1),
            (new Point(tile.X, tile.Y + 1), 2),
            (new Point(tile.X - 1, tile.Y), 3)
        })
        {
            if (activityStyle == "scenic"
                && IsInBounds(location, neighbor.X, neighbor.Y)
                && location.isWaterTile(neighbor.X, neighbor.Y))
            {
                return direction;
            }

            if (activityStyle == "browse"
                && (HasTileProperty(location, neighbor, "Action") || HasTileProperty(location, neighbor, "TouchAction")))
            {
                return direction;
            }
        }

        return 2;
    }

    private static string BuildFallbackLabel(string targetLocation, string activityStyle)
    {
        if (activityStyle == "browse")
        {
            return "beside a public display area";
        }

        if (activityStyle == "scenic")
        {
            return targetLocation switch
            {
                "Beach" => "on a quiet part of the shore",
                "Mountain" => "at an open mountain viewpoint",
                "Forest" => "in a quiet forest clearing",
                "Farm" => "at a calm spot on the farm",
                _ => "at an open spot with a view"
            };
        }

        return activityStyle == "quiet"
            ? "in a quiet public corner"
            : "in an open public area";
    }

    private static IEnumerable<Point> GetCardinalNeighbors(Point tile)
    {
        yield return new Point(tile.X, tile.Y - 1);
        yield return new Point(tile.X + 1, tile.Y);
        yield return new Point(tile.X, tile.Y + 1);
        yield return new Point(tile.X - 1, tile.Y);
    }

    private static int ManhattanDistance(Point first, Point second)
    {
        return Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
    }

    private static bool IsInBounds(GameLocation location, int x, int y)
    {
        return x >= 0
            && y >= 0
            && x < location.Map.Layers[0].LayerWidth
            && y < location.Map.Layers[0].LayerHeight;
    }
}
