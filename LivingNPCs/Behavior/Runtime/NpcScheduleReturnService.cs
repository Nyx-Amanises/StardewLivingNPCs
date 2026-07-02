using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

/// <summary>
/// Returns an NPC to whatever their vanilla schedule says they should be doing right now.
/// Used after companion outings: same-location targets are pathfound normally; cross-location
/// recovery may teleport (opt-out via <c>allowCrossLocationTeleport</c> for escort-style exits).
/// </summary>
internal sealed class NpcScheduleReturnService
{
    private const string LogContext = "LivingNPCs companion outing";

    private readonly IMonitor monitor;
    private readonly ModConfig config;

    public NpcScheduleReturnService(IMonitor monitor, ModConfig config)
    {
        this.monitor = monitor;
        this.config = config;
    }

    public bool TryReturnToCurrentSchedule(NPC npc, bool allowCrossLocationTeleport = true)
    {
        if (!this.TryResolveCurrentScheduleTarget(npc, out GameLocation? location, out Point targetTile, out int facingDirection)
            || location == null)
        {
            return false;
        }

        try
        {
            npc.controller = null;
            npc.Halt();

            bool sameLocation = string.Equals(npc.currentLocation?.Name, location.Name, StringComparison.OrdinalIgnoreCase);
            if (sameLocation)
            {
                if (!location.characters.Contains(npc))
                {
                    npc.currentLocation?.characters.Remove(npc);
                    location.characters.Add(npc);
                    npc.currentLocation = location;
                }

                if (Vector2.Distance(npc.Tile, new Vector2(targetTile.X, targetTile.Y)) > 0.75f)
                {
                    npc.controller = new PathFindController(
                        npc,
                        location,
                        targetTile,
                        facingDirection is >= 0 and <= 3 ? facingDirection : 2
                    );
                }
                else if (facingDirection is >= 0 and <= 3)
                {
                    npc.faceDirection(facingDirection);
                }

                if (this.config.Debug)
                {
                    this.monitor.Log(I18n.Get("log.schedule.resumedCurrent", new { npc = npc.Name, context = LogContext }), LogLevel.Debug);
                }

                return true;
            }

            if (!allowCrossLocationTeleport)
            {
                if (this.config.Debug)
                {
                    this.monitor.Log(
                        I18n.Get("log.schedule.leftAtEscort", new { npc = npc.Name, context = LogContext, location = location.Name }),
                        LogLevel.Debug
                    );
                }

                return false;
            }

            npc.currentLocation?.characters.Remove(npc);
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

            if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.schedule.returned", new { npc = npc.Name, context = LogContext }), LogLevel.Debug);
            }

            return true;
        }
        catch (Exception ex)
        {
            this.monitor.Log(I18n.Get("log.schedule.resumeFailed", new { npc = npc.Name, context = LogContext, error = ex.Message }), LogLevel.Warn);
            return false;
        }
    }

    public bool TryResolveCurrentScheduleTarget(NPC npc, out GameLocation? location, out Point targetTile, out int facingDirection)
    {
        location = null;
        targetTile = Point.Zero;
        facingDirection = -1;
        if (npc.Schedule == null || npc.Schedule.Count == 0)
        {
            return false;
        }

        object? scheduleEntry = npc.Schedule
            .Where(entry => entry.Key <= Game1.timeOfDay)
            .OrderByDescending(entry => entry.Key)
            .Select(entry => entry.Value)
            .FirstOrDefault();
        if (scheduleEntry == null
            || !ScheduleReflectionReader.TryReadScheduleDestination(scheduleEntry, out string locationName, out Point scheduledTile, out int scheduledFacingDirection))
        {
            return false;
        }

        targetTile = scheduledTile;
        facingDirection = scheduledFacingDirection;

        try
        {
            location = Game1.getLocationFromName(locationName);
        }
        catch
        {
            location = null;
        }

        if (location == null)
        {
            return false;
        }

        if (!BehaviorActionExecutor.IsSafeDestinationTile(location, targetTile, npc)
            && !BehaviorActionExecutor.TryFindOpenTileNear(location, targetTile, npc, out targetTile))
        {
            return false;
        }

        return true;
    }
}
