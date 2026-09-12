using System;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

/// <summary>Identifies a local farm walk whose invited NPC may cross NPCBarrier tiles.</summary>
internal sealed class FarmOutingPathController : PathFindController
{
    [ThreadStatic]
    private static NPC? pathfindingNpc;

    [ThreadStatic]
    private static GameLocation? pathfindingFarm;

    private readonly NPC owner;

    private FarmOutingPathController(NPC npc, GameLocation farm, Point targetTile, int facingDirection)
        : base(npc, farm, targetTile, facingDirection)
    {
        this.owner = npc;
    }

    public static FarmOutingPathController Create(NPC npc, GameLocation farm, Point targetTile, int facingDirection)
    {
        NPC? previousNpc = pathfindingNpc;
        GameLocation? previousFarm = pathfindingFarm;
        try
        {
            // The base constructor finds the path before npc.controller can identify this walk.
            pathfindingNpc = npc;
            pathfindingFarm = farm;
            return new FarmOutingPathController(npc, farm, targetTile, facingDirection);
        }
        finally
        {
            pathfindingNpc = previousNpc;
            pathfindingFarm = previousFarm;
        }
    }

    internal static bool CanCrossNpcBarrier(Character character, GameLocation location)
    {
        if (character is not NPC npc || !IsMainFarm(location) || npc.currentLocation != location)
        {
            return false;
        }

        return (ReferenceEquals(pathfindingNpc, npc) && ReferenceEquals(pathfindingFarm, location))
            || (npc.controller is FarmOutingPathController controller
                && ReferenceEquals(controller.owner, npc)
                && ReferenceEquals(controller.location, location));
    }

    internal static bool IsMainFarm(GameLocation location)
    {
        return location.IsFarm && string.Equals(location.Name, "Farm", StringComparison.OrdinalIgnoreCase);
    }
}
