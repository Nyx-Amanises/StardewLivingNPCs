using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal static class NpcTravelRuntime
{
    public static void SuppressSchedule(NPC npc)
    {
        npc.ignoreScheduleToday = true;
        npc.followSchedule = false;
    }

    public static void RestoreSchedule(NPC npc, bool ignoreScheduleToday, bool followSchedule)
    {
        npc.ignoreScheduleToday = ignoreScheduleToday;
        npc.followSchedule = followSchedule;
    }

    public static bool TryAssignVanillaScheduleRoute(
        NPC npc,
        GameLocation destination,
        Point targetTile,
        int facingDirection,
        out PathFindController? controller)
    {
        controller = null;
        GameLocation? source = npc.currentLocation;
        if (source == null)
        {
            return false;
        }

        npc.controller = null;
        npc.Halt();
        if (string.Equals(source.Name, destination.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (Vector2.Distance(npc.Tile, new Vector2(targetTile.X, targetTile.Y)) <= 0.75f)
            {
                if (facingDirection is >= 0 and <= 3)
                {
                    npc.faceDirection(facingDirection);
                }

                return true;
            }

            controller = new PathFindController(
                npc,
                source,
                targetTile,
                facingDirection is >= 0 and <= 3 ? facingDirection : 2
            );
            npc.controller = controller;
            return true;
        }

        try
        {
            SchedulePathDescription description = npc.pathfindToNextScheduleLocation(
                "LivingNPCsCompanionOuting",
                source.Name,
                npc.TilePoint.X,
                npc.TilePoint.Y,
                destination.Name,
                targetTile.X,
                targetTile.Y,
                facingDirection is >= 0 and <= 3 ? facingDirection : 2,
                string.Empty,
                string.Empty
            );
            if (description?.route == null || description.route.Count == 0)
            {
                return false;
            }

            npc.DirectionsToNewLocation = description;
            controller = new PathFindController(
                description.route,
                npc,
                source
            )
            {
                finalFacingDirection = facingDirection is >= 0 and <= 3 ? facingDirection : 2,
                NPCSchedule = true
            };
            npc.controller = controller;
            return true;
        }
        catch
        {
            npc.DirectionsToNewLocation = null;
            return false;
        }
    }

    public static bool TryGetWarpBoundary(
        GameLocation source,
        string targetLocationName,
        NPC npc,
        out Point sourceTile,
        out Point targetTile)
    {
        sourceTile = source.getWarpPointTo(targetLocationName);
        if (sourceTile == Point.Zero
            && !TryFindWarpTileTo(source, targetLocationName, out sourceTile))
        {
            targetTile = Point.Zero;
            return false;
        }

        targetTile = source.getWarpPointTarget(sourceTile, npc);
        if (targetTile == Point.Zero
            && !TryFindWarpTargetTile(source, sourceTile, targetLocationName, out targetTile))
        {
            return false;
        }

        return true;
    }

    public static bool TryWarpAcrossBoundary(
        NPC npc,
        GameLocation source,
        Point sourceTile,
        string expectedTargetLocationName,
        out GameLocation? targetLocation,
        out Point targetTile)
    {
        targetLocation = null;
        targetTile = source.getWarpPointTarget(sourceTile, npc);
        if (targetTile == Point.Zero
            && !TryFindWarpTargetTile(source, sourceTile, expectedTargetLocationName, out targetTile))
        {
            return false;
        }

        string targetName = TryFindWarpTargetName(source, sourceTile, expectedTargetLocationName);
        string normalizedTarget = TravelLocationRules.Normalize(targetName, targetName);
        string normalizedExpected = TravelLocationRules.Normalize(expectedTargetLocationName, expectedTargetLocationName);
        if (!string.Equals(normalizedTarget, normalizedExpected, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        targetLocation = ResolveLocation(normalizedTarget);
        if (targetLocation == null)
        {
            targetLocation = ResolveLocation(targetName);
        }

        if (targetLocation == null)
        {
            return false;
        }

        npc.controller = null;
        npc.DirectionsToNewLocation = null;
        npc.Halt();
        Game1.warpCharacter(npc, targetLocation, new Vector2(targetTile.X, targetTile.Y));
        return true;
    }

    private static bool TryFindWarpTileTo(GameLocation source, string targetLocationName, out Point tile)
    {
        string normalizedTarget = TravelLocationRules.Normalize(targetLocationName, targetLocationName);
        foreach (var warp in source.warps)
        {
            string warpTarget = ScheduleReflectionReader.GetWarpTargetName(warp);
            if (string.Equals(
                    TravelLocationRules.Normalize(warpTarget, warpTarget),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase))
            {
                tile = new Point(warp.X, warp.Y);
                return true;
            }
        }

        tile = Point.Zero;
        return false;
    }

    private static bool TryFindWarpTargetTile(
        GameLocation source,
        Point sourceTile,
        string targetLocationName,
        out Point targetTile)
    {
        string normalizedTarget = TravelLocationRules.Normalize(targetLocationName, targetLocationName);
        foreach (var warp in source.warps)
        {
            if (warp.X != sourceTile.X || warp.Y != sourceTile.Y)
            {
                continue;
            }

            string warpTarget = ScheduleReflectionReader.GetWarpTargetName(warp);
            if (!string.Equals(
                    TravelLocationRules.Normalize(warpTarget, warpTarget),
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return ScheduleReflectionReader.TryReadWarpTargetTile(warp, out targetTile);
        }

        targetTile = Point.Zero;
        return false;
    }

    private static string TryFindWarpTargetName(
        GameLocation source,
        Point sourceTile,
        string fallback)
    {
        foreach (var warp in source.warps)
        {
            if (warp.X == sourceTile.X && warp.Y == sourceTile.Y)
            {
                string targetName = ScheduleReflectionReader.GetWarpTargetName(warp);
                if (!string.IsNullOrWhiteSpace(targetName))
                {
                    return targetName;
                }
            }
        }

        return fallback;
    }

    private static GameLocation? ResolveLocation(string locationName)
    {
        string normalized = TravelLocationRules.Normalize(locationName, locationName);
        if (string.Equals(normalized, "Farm", StringComparison.OrdinalIgnoreCase))
        {
            return Game1.getFarm();
        }

        try
        {
            return Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(normalized);
        }
        catch
        {
            return null;
        }
    }
}
