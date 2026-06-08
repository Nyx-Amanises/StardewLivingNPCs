using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal static class BehaviorActionExecutor
{
    public static bool TryFacePlayer(NPC npc, bool allowFacePlayer)
    {
        if (!allowFacePlayer)
        {
            return false;
        }

        npc.faceDirection(GetDirectionTowardPlayer(npc));
        return true;
    }

    public static bool TryEmote(NPC npc, int emoteId, bool allowEmotes, bool allowFacePlayer)
    {
        if (!allowEmotes)
        {
            return false;
        }

        TryFacePlayer(npc, allowFacePlayer);
        npc.doEmote(emoteId);
        return true;
    }

    public static bool TryPause(NPC npc, bool allowFacePlayer)
    {
        if (!allowFacePlayer)
        {
            return false;
        }

        npc.controller = null;
        npc.Halt();
        TryFacePlayer(npc, allowFacePlayer);
        return true;
    }

    public static bool TryLookAround(NPC npc, bool allowFacePlayer, Random random)
    {
        if (!allowFacePlayer)
        {
            return false;
        }

        npc.faceDirection(random.Next(4));
        return true;
    }

    public static bool TryApproachPlayer(NPC npc, bool allowApproachPlayer, bool allowFacePlayer)
    {
        if (!allowApproachPlayer || Game1.currentLocation == null)
        {
            return false;
        }

        if (!TryFindApproachTile(npc, out Point targetTile))
        {
            TryFacePlayer(npc, allowFacePlayer);
            return false;
        }

        npc.controller = new PathFindController(
            npc,
            Game1.currentLocation,
            targetTile,
            GetDirectionTowardPlayerFromTile(targetTile)
        );

        return npc.controller != null;
    }

    public static bool TryStepAway(NPC npc, bool allowApproachPlayer, bool allowFacePlayer)
    {
        if (!allowApproachPlayer || Game1.currentLocation == null)
        {
            return false;
        }

        if (!TryFindStepAwayTile(npc, out Point targetTile))
        {
            TryFacePlayer(npc, allowFacePlayer);
            return false;
        }

        npc.controller = new PathFindController(
            npc,
            Game1.currentLocation,
            targetTile,
            GetDirectionTowardPlayerFromTile(targetTile)
        );

        return npc.controller != null;
    }

    public static int GetDirectionTowardPlayer(NPC npc)
    {
        Vector2 delta = Game1.player.Tile - npc.Tile;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            return delta.X > 0 ? 1 : 3;
        }

        return delta.Y > 0 ? 2 : 0;
    }

    public static int GetDirectionTowardPlayerFromTile(Point tile)
    {
        Vector2 delta = Game1.player.Tile - new Vector2(tile.X, tile.Y);
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            return delta.X > 0 ? 1 : 3;
        }

        return delta.Y > 0 ? 2 : 0;
    }

    public static Point GetPlayerFacingTile()
    {
        var tile = Game1.player.TilePoint;
        return Game1.player.FacingDirection switch
        {
            0 => new Point(tile.X, tile.Y - 1),
            1 => new Point(tile.X + 1, tile.Y),
            2 => new Point(tile.X, tile.Y + 1),
            3 => new Point(tile.X - 1, tile.Y),
            _ => tile
        };
    }

    public static bool TryFindApproachTile(NPC npc, out Point targetTile)
    {
        targetTile = Point.Zero;
        var playerTile = Game1.player.TilePoint;
        var candidates = new[]
        {
            new Point(playerTile.X, playerTile.Y + 1),
            new Point(playerTile.X + 1, playerTile.Y),
            new Point(playerTile.X - 1, playerTile.Y),
            new Point(playerTile.X, playerTile.Y - 1)
        };

        var location = Game1.currentLocation;
        if (location == null)
        {
            return false;
        }

        foreach (var candidate in candidates.OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), npc.Tile)))
        {
            if (IsSafeDestinationTile(location, candidate))
            {
                targetTile = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool TryFindStepAwayTile(NPC npc, out Point targetTile)
    {
        targetTile = Point.Zero;
        var location = Game1.currentLocation;
        if (location == null)
        {
            return false;
        }

        var npcTile = npc.TilePoint;
        Vector2 away = npc.Tile - Game1.player.Tile;
        int awayX = Math.Abs(away.X) >= Math.Abs(away.Y) ? Math.Sign(away.X) : 0;
        int awayY = Math.Abs(away.Y) > Math.Abs(away.X) ? Math.Sign(away.Y) : 0;
        if (awayX == 0 && awayY == 0)
        {
            awayY = 1;
        }

        var candidates = new[]
        {
            new Point(npcTile.X + awayX, npcTile.Y + awayY),
            new Point(npcTile.X + awayY, npcTile.Y + awayX),
            new Point(npcTile.X - awayY, npcTile.Y - awayX),
            new Point(npcTile.X + awayX + awayY, npcTile.Y + awayY + awayX),
            new Point(npcTile.X + awayX - awayY, npcTile.Y + awayY - awayX)
        };

        foreach (var candidate in candidates
            .Distinct()
            .OrderByDescending(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), Game1.player.Tile))
            .ThenBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), npc.Tile)))
        {
            if (IsSafeDestinationTile(location, candidate))
            {
                targetTile = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool TryFindOpenTileNear(GameLocation location, Point center, NPC ignoredNpc, out Point targetTile)
    {
        foreach (var candidate in GetTilesAround(center, 5))
        {
            if (IsSafeDestinationTile(location, candidate, ignoredNpc))
            {
                targetTile = candidate;
                return true;
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    public static bool IsSafeDestinationTile(GameLocation location, Point tile, NPC? ignoredNpc = null)
    {
        if (tile.X < 0 || tile.Y < 0 || tile.X >= location.Map.Layers[0].LayerWidth || tile.Y >= location.Map.Layers[0].LayerHeight)
        {
            return false;
        }

        var tileVector = new Vector2(tile.X, tile.Y);
        if (!location.isTileLocationOpen(tileVector))
        {
            return false;
        }

        if (!location.isTilePassable(tileVector))
        {
            return false;
        }

        return !location.characters.Any(npc => npc != ignoredNpc && npc.TilePoint == tile);
    }

    private static IEnumerable<Point> GetTilesAround(Point center, int maxRadius)
    {
        yield return center;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    yield return new Point(center.X + dx, center.Y + dy);
                }
            }
        }
    }
}
