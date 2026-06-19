using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class DelayedTravelActionRuntime
{
    public delegate bool TryStartTravelActionHandler(NPC npc, ValleyTalkWorldActionRequest action, out string reason);

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly Func<string, NPC?> findNpcInCurrentLocation;
    private readonly TryStartTravelActionHandler tryStartCompanionOuting;
    private readonly List<PendingDelayedTravelAction> pendingActions = new();

    public DelayedTravelActionRuntime(
        ModConfig config,
        IMonitor monitor,
        BehaviorMemory memory,
        Func<string, NPC?> findNpcInCurrentLocation,
        TryStartTravelActionHandler tryStartCompanionOuting)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.findNpcInCurrentLocation = findNpcInCurrentLocation;
        this.tryStartCompanionOuting = tryStartCompanionOuting;
    }

    public void Clear()
    {
        this.pendingActions.Clear();
    }

    public void Queue(NPC npc, ValleyTalkWorldActionRequest action)
    {
        int delayMinutes = Math.Clamp(action.DelayMinutes, 1, 20);
        this.pendingActions.RemoveAll(pending => pending.NpcName == npc.Name);
        this.pendingActions.Add(new PendingDelayedTravelAction(
            npc.Name,
            Game1.Date.TotalDays,
            npc.currentLocation?.Name ?? string.Empty,
            BehaviorTimeMath.AddMinutesToTime(Game1.timeOfDay, delayMinutes),
            action.Type,
            action.TargetLocation,
            action.DurationMinutes,
            action.Reason
        ));

        var state = this.memory.GetState(npc);
        if (state != null)
        {
            MarkStateAfterWorldAction(state, "they needed a brief moment before the shared outing");
        }

        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Queued delayed travel action {action.Type} for {npc.Name} in {delayMinutes} minutes toward {action.TargetLocation}.",
                LogLevel.Debug
            );
        }
    }

    public void TryStartPending()
    {
        if (this.pendingActions.Count == 0
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var pending in this.pendingActions.ToList())
        {
            if (pending.TotalDays != Game1.Date.TotalDays)
            {
                this.pendingActions.Remove(pending);
                continue;
            }

            if (Game1.timeOfDay < pending.NotBeforeTimeOfDay)
            {
                continue;
            }

            NPC? npc = this.findNpcInCurrentLocation(pending.NpcName);
            if (npc == null
                || npc.currentLocation?.Name != pending.LocationName
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 2)
            {
                this.pendingActions.Remove(pending);
                continue;
            }

            var action = new ValleyTalkWorldActionRequest
            {
                Type = pending.Type,
                TargetLocation = pending.TargetLocation,
                DurationMinutes = pending.DurationMinutes,
                Reason = pending.Reason
            };
            if (!this.tryStartCompanionOuting(npc, action, out string reason) && this.config.Debug)
            {
                this.monitor.Log(
                    $"Skipped delayed travel action {action.Type} for {npc.Name}: {(string.IsNullOrWhiteSpace(reason) ? "travel action request rejected" : reason)}.",
                    LogLevel.Debug
                );
            }
            this.pendingActions.Remove(pending);
        }
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
