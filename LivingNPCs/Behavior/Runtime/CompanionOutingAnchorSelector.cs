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
                new(new Point(32, 34), 2, "near the open shoreline", ["scenic", "quiet", "visit"]),
                new(new Point(44, 24), 2, "on a quiet stretch of sand", ["scenic", "quiet", "visit"]),
                new(new Point(60, 13), 1, "near the pier with a view of the water", ["scenic", "quiet", "visit"])
            ],
            ["Mountain"] =
            [
                new(new Point(31, 20), 0, "near the mountain lake", ["scenic", "quiet", "visit"]),
                new(new Point(42, 13), 2, "at an open mountain overlook", ["scenic", "quiet", "visit"]),
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
                new(new Point(20, 17), 0, "beside one of the saloon's side tables", ["social", "quiet", "visit"]),
                new(new Point(26, 18), 3, "along the quieter side of the saloon", ["social", "quiet", "visit"]),
                new(new Point(12, 18), 1, "near the public seating area", ["social", "visit"])
            ],
            ["ArchaeologyHouse"] =
            [
                new(new Point(11, 9), 0, "beside a public museum display", ["browse", "quiet", "visit"]),
                new(new Point(17, 9), 0, "along the museum's public exhibit aisle", ["browse", "visit"]),
                new(new Point(8, 13), 1, "near the library shelves", ["browse", "quiet", "visit"])
            ],
            ["Hospital"] =
            [
                new(new Point(9, 17), 0, "in the clinic's public waiting area", ["quiet", "visit"]),
                new(new Point(12, 16), 3, "along the side of the clinic waiting room", ["quiet", "visit"])
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
