using System;
using System.Linq;
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

    public static void PlaceInLocation(NPC npc, GameLocation location, Point targetTile, int facingDirection)
    {
        npc.DirectionsToNewLocation = null;
        RemoveFromKnownLocations(npc);
        if (!location.characters.Contains(npc))
        {
            location.characters.Add(npc);
        }

        npc.currentLocation = location;
        npc.Position = new Vector2(targetTile.X * Game1.tileSize, targetTile.Y * Game1.tileSize);
        if (facingDirection is >= 0 and <= 3)
        {
            npc.faceDirection(facingDirection);
        }
    }

    private static void RemoveFromKnownLocations(NPC npc)
    {
        RemoveFromLocation(npc.currentLocation, npc);
        RemoveFromLocation(Game1.currentLocation, npc);
        foreach (var location in Game1.locations)
        {
            RemoveFromLocation(location, npc);
        }
    }

    private static void RemoveFromLocation(GameLocation? location, NPC npc)
    {
        if (location == null)
        {
            return;
        }

        foreach (var candidate in location.characters
            .Where(candidate => ReferenceEquals(candidate, npc)
                || string.Equals(candidate.Name, npc.Name, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            location.characters.Remove(candidate);
        }
    }
}
