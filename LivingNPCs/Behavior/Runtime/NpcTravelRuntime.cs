using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

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

    public static void PlaceInLocation(NPC npc, GameLocation location, Point targetTile, int facingDirection)
    {
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
