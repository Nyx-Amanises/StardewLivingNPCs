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

internal sealed class CompanionOutingAnchorSelector
{
    private sealed record AuthoredAnchor(Point Tile, int FacingDirection, string Label, string[] Styles);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<AuthoredAnchor>> AuthoredAnchors =
        new Dictionary<string, IReadOnlyList<AuthoredAnchor>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SeedShop"] =
            [
                new(new Point(10, 17), 0, "beside the public shop shelves", ["browse", "visit"]),
                new(new Point(12, 16), 3, "along a public aisle in the general store", ["browse", "visit"]),
                new(new Point(8, 15), 1, "near the public display shelves", ["browse", "visit"])
            ],
            ["Beach"] =
            [
                new(new Point(32, 34), 2, "where the tide rolls in along the open shoreline", ["scenic", "quiet", "visit"]),
                new(new Point(44, 24), 2, "on a quiet stretch of sand with room to watch the waves", ["scenic", "quiet", "visit"]),
                new(new Point(60, 13), 1, "near the pier with a view across the water", ["scenic", "quiet", "visit"]),
                new(new Point(38, 31), 2, "near the foam line where the waves keep breaking", ["scenic", "quiet"]),
                new(new Point(56, 9), 1, "beside the dock approach with the sea beside them", ["scenic", "visit"])
            ],
            ["Mountain"] =
            [
                new(new Point(31, 20), 0, "near the mountain lake where a quiet conversation fits", ["scenic", "quiet", "visit"]),
                new(new Point(39, 9), 1, "along the north shore of the mountain lake", ["scenic", "quiet", "visit"]),
                new(new Point(54, 32), 0, "by the lower mountain lake path", ["scenic", "quiet", "visit"]),
                new(new Point(42, 13), 2, "at an open mountain overlook above the lake", ["scenic", "quiet", "visit"]),
                new(new Point(15, 10), 1, "beside the mountain path", ["scenic", "visit"])
            ],
            ["Forest"] =
            [
                new(new Point(58, 27), 2, "near the forest river", ["scenic", "quiet", "visit"]),
                new(new Point(87, 36), 0, "in a quiet forest clearing", ["scenic", "quiet", "visit"]),
                new(new Point(34, 34), 1, "beside a wooded path", ["scenic", "visit"])
            ],
            ["Saloon"] =
            [
                new(new Point(11, 20), 0, "leaning near the public side of Gus's bar counter", ["social", "visit"]),
                new(new Point(13, 19), 0, "close to the bar where conversation blends into the room", ["social", "visit"]),
                new(new Point(20, 17), 0, "beside one of the saloon's side tables", ["social", "quiet", "visit"]),
                new(new Point(26, 18), 3, "along the quieter side of the saloon", ["social", "quiet", "visit"]),
                new(new Point(12, 18), 1, "near the public seating area", ["social", "visit"])
            ],
            ["ArchaeologyHouse"] =
            [
                new(new Point(11, 9), 0, "beside a public museum display", ["browse", "quiet", "visit"]),
                new(new Point(17, 9), 0, "along the museum's public exhibit aisle", ["browse", "visit"]),
                new(new Point(8, 13), 1, "near the library shelves where they can quietly flip through books", ["browse", "quiet", "visit"]),
                new(new Point(18, 14), 2, "at the library tables with the shelves just behind them", ["browse", "quiet", "visit"]),
                new(new Point(13, 14), 0, "between the reading tables and the public book stacks", ["browse", "quiet"])
            ],
            ["Hospital"] =
            [
                new(new Point(9, 17), 0, "in the clinic's public waiting area", ["quiet", "visit"]),
                new(new Point(12, 16), 3, "along the side of the clinic waiting room", ["quiet", "visit"])
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
            ["Custom_Ridgeside_RidgesideVillage"] =
            [
                new(new Point(89, 48), 2, "in the village square where neighbors naturally pass by", ["social", "visit"]),
                new(new Point(69, 52), 0, "beside the central Ridgeside walkway", ["social", "quiet", "visit"]),
                new(new Point(118, 41), 2, "on the upper village overlook path", ["social", "scenic", "quiet", "visit"]),
                new(new Point(41, 113), 2, "near the lower village green", ["social", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_Ridge"] =
            [
                new(new Point(22, 38), 3, "on the ridge path with the valley falling away nearby", ["scenic", "quiet", "visit"]),
                new(new Point(22, 29), 2, "beside the ridge fishing spot", ["scenic", "quiet", "visit"]),
                new(new Point(15, 26), 3, "along the mountain ridge trail", ["scenic", "quiet", "visit"]),
                new(new Point(44, 46), 0, "near the high ridge overlook", ["scenic", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_RidgeFalls"] =
            [
                new(new Point(18, 18), 2, "near Ridge Falls where the water noise fills the pause", ["scenic", "quiet", "visit"]),
                new(new Point(33, 26), 1, "on the falls-side path with mist in the air", ["scenic", "quiet", "visit"]),
                new(new Point(52, 33), 3, "beside the lower waterfall walk", ["scenic", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_RidgeForest"] =
            [
                new(new Point(60, 69), 1, "inside Ridge Forest where the path disappears under trees", ["scenic", "quiet", "visit"]),
                new(new Point(96, 94), 2, "at a quiet Ridge Forest clearing", ["scenic", "quiet", "visit"]),
                new(new Point(163, 40), 1, "near the deeper forest edge", ["scenic", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_RSVCableCar"] =
            [
                new(new Point(10, 10), 2, "near the cable car platform with the mountain air around them", ["scenic", "quiet", "visit"]),
                new(new Point(17, 12), 3, "beside the Ridgeside cable car railing", ["scenic", "quiet", "visit"]),
                new(new Point(24, 15), 3, "on the cable car approach path", ["scenic", "visit"])
            ],
            ["Custom_Ridgeside_RSVCliff"] =
            [
                new(new Point(29, 10), 1, "at the Ridgeside cliff lookout", ["scenic", "quiet", "visit"]),
                new(new Point(50, 21), 0, "along the cliff path where the view drops away", ["scenic", "quiet", "visit"]),
                new(new Point(82, 21), 1, "near the far cliff overlook", ["scenic", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_RSVTheHike"] =
            [
                new(new Point(45, 61), 2, "on the Ridgeside hiking trail where the path slows", ["scenic", "quiet", "visit"]),
                new(new Point(58, 44), 2, "beside the high trail bend", ["scenic", "quiet", "visit"]),
                new(new Point(30, 55), 1, "near a quiet hiking-trail turnout", ["scenic", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_RSVSpiritRealm"] =
            [
                new(new Point(16, 20), 2, "at the edge of the Spirit Realm path", ["scenic", "quiet", "visit"]),
                new(new Point(28, 27), 3, "where the Spirit Realm feels still enough to speak softly", ["scenic", "quiet", "visit"]),
                new(new Point(40, 18), 1, "beside the strange light of the Spirit Realm", ["scenic", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_LogCabinHotelLobby"] =
            [
                new(new Point(13, 14), 0, "near the lobby piano where conversation can stay public", ["browse", "social", "quiet", "visit"]),
                new(new Point(18, 11), 2, "beside the Log Cabin Hotel seating area", ["social", "quiet", "visit"]),
                new(new Point(3, 15), 2, "along the quieter side of the hotel lobby", ["quiet", "visit"])
            ],
            ["Custom_Ridgeside_PurpleMansion"] =
            [
                new(new Point(30, 29), 2, "in the Purple Mansion's public hall", ["browse", "quiet", "visit"]),
                new(new Point(47, 32), 0, "beside the mansion gallery path", ["browse", "quiet", "visit"]),
                new(new Point(23, 10), 1, "near the mansion's front sitting area", ["social", "quiet", "visit"])
            ],
            ["Custom_Ridgeside_PaulaClinic"] =
            [
                new(new Point(20, 13), 3, "in Paula's public waiting area", ["quiet", "visit"]),
                new(new Point(16, 13), 2, "near the Ridgeside clinic counter", ["quiet", "visit"]),
                new(new Point(3, 12), 1, "along the side of Paula's clinic waiting room", ["quiet", "visit"])
            ],
            ["Custom_Ridgeside_RSVGreenhouse1"] =
            [
                new(new Point(20, 21), 2, "among the restored greenhouse beds", ["browse", "quiet", "visit"]),
                new(new Point(27, 23), 3, "beside the greenhouse path where plants frame the room", ["browse", "quiet", "visit"]),
                new(new Point(35, 14), 0, "near the upper greenhouse work area", ["browse", "quiet", "visit"])
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
        int totalDays,
        IReadOnlySet<Point> reservedTiles,
        out CompanionOutingAnchor? anchor)
    {
        var candidates = new List<CompanionOutingAnchor>();
        var entryTiles = GetLikelyEntryTiles(location, sourceLocation).ToList();

        if (AuthoredAnchors.TryGetValue(targetLocation, out var authored))
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
                    200 + ScoreTile(location, candidate.Tile, activityStyle, entryTiles)
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
