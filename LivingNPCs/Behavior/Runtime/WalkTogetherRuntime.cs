using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal sealed class WalkTogetherRuntime
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly CanUseWorldActionHandler canUseWorldAction;
    private readonly Action<NPC, bool> stopTravelActionsForNpc;
    private readonly Func<string, NPC?> findNpc;
    private readonly Func<NPC, Point?> findApproachTile;
    private readonly Func<NPC, bool> facePlayer;
    private readonly Func<Point, int> getDirectionTowardPlayerFromTile;
    private readonly Action<NPC> returnNpcToSchedule;
    private readonly List<PendingWalkTogether> pendingWalks = new();

    public WalkTogetherRuntime(
        ModConfig config,
        BehaviorMemory memory,
        BehaviorFeedbackService feedback,
        CanUseWorldActionHandler canUseWorldAction,
        Action<NPC, bool> stopTravelActionsForNpc,
        Func<string, NPC?> findNpc,
        Func<NPC, Point?> findApproachTile,
        Func<NPC, bool> facePlayer,
        Func<Point, int> getDirectionTowardPlayerFromTile,
        Action<NPC> returnNpcToSchedule)
    {
        this.config = config;
        this.memory = memory;
        this.feedback = feedback;
        this.canUseWorldAction = canUseWorldAction;
        this.stopTravelActionsForNpc = stopTravelActionsForNpc;
        this.findNpc = findNpc;
        this.findApproachTile = findApproachTile;
        this.facePlayer = facePlayer;
        this.getDirectionTowardPlayerFromTile = getDirectionTowardPlayerFromTile;
        this.returnNpcToSchedule = returnNpcToSchedule;
    }

    public void Clear()
    {
        this.pendingWalks.Clear();
    }

    public bool TryStart(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.canUseWorldAction(npc, "walk_together", requireFriendly: false, out reason, allowDuringEvents: false, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiWalkTogether || state == null || state.LastAiWalkTogetherTotalDays == Game1.Date.TotalDays)
        {
            reason = "walk together is disabled or already used today";
            return false;
        }

        if (npc.controller != null)
        {
            reason = "the NPC is already moving";
            return false;
        }

        int maxWalkMinutes = Math.Max(8, this.config.MaxAiWalkTogetherMinutes);
        int durationMinutes = Math.Clamp(
            action.DurationMinutes <= 0 ? 12 : action.DurationMinutes,
            8,
            maxWalkMinutes
        );
        this.stopTravelActionsForNpc(npc, true);
        bool originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        bool originalFollowSchedule = npc.followSchedule;
        NpcTravelRuntime.SuppressSchedule(npc);
        this.pendingWalks.Add(new PendingWalkTogether(
            npc.Name,
            Game1.Date.TotalDays,
            npc.currentLocation?.Name ?? string.Empty,
            BehaviorTimeMath.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
            Game1.player.TilePoint,
            null,
            originalIgnoreScheduleToday,
            originalFollowSchedule
        ));
        state.LastAiWalkTogetherTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "WalkedTogether",
            BuildWorldActionReason(action.Reason, $"they agreed to walk with the farmer for about {durationMinutes} minutes"),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they agreed to walk with the farmer");
        this.feedback.Show($"LivingNPCs：{npc.displayName} 会陪你走一会儿。");
        return true;
    }

    public void TryUpdatePending()
    {
        if (this.pendingWalks.Count == 0)
        {
            return;
        }

        if (Game1.eventUp)
        {
            foreach (var walk in this.pendingWalks.ToList())
            {
                this.Stop(walk, this.findNpc(walk.NpcName), returnToSchedule: true);
            }

            return;
        }

        foreach (var walk in this.pendingWalks.ToList())
        {
            NPC? npc = this.findNpc(walk.NpcName);
            if (walk.TotalDays != Game1.Date.TotalDays
                || Game1.timeOfDay >= walk.EndTimeOfDay
                || npc == null
                || npc.currentLocation?.Name != walk.LocationName)
            {
                this.Stop(walk, npc, returnToSchedule: true);
                continue;
            }

            if (Game1.activeClickableMenu != null)
            {
                continue;
            }

            NpcTravelRuntime.SuppressSchedule(npc);

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 4)
            {
                this.Stop(walk, npc, returnToSchedule: true);
                continue;
            }

            if (npc.controller != null && walk.LastAssignedController != null && npc.controller != walk.LastAssignedController)
            {
                this.Stop(walk, npc, returnToSchedule: true);
                continue;
            }

            if (npc.controller != null && walk.LastAssignedController == null)
            {
                this.Stop(walk, npc, returnToSchedule: true);
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) <= 1.5f)
            {
                this.facePlayer(npc);
                continue;
            }

            if (walk.LastPlayerTile == Game1.player.TilePoint && npc.controller != null)
            {
                continue;
            }

            Point? targetTile = this.findApproachTile(npc);
            if (targetTile == null)
            {
                continue;
            }

            npc.controller = new PathFindController(
                npc,
                Game1.currentLocation,
                targetTile.Value,
                this.getDirectionTowardPlayerFromTile(targetTile.Value)
            );
            walk.LastPlayerTile = Game1.player.TilePoint;
            walk.LastAssignedController = npc.controller;
        }
    }

    public void StopForNpc(NPC npc, bool returnToSchedule)
    {
        foreach (var walk in this.pendingWalks.Where(walk => walk.NpcName == npc.Name).ToList())
        {
            this.Stop(walk, npc, returnToSchedule);
        }
    }

    private void Stop(PendingWalkTogether walk, NPC? npc, bool returnToSchedule)
    {
        if (npc != null && npc.controller == walk.LastAssignedController)
        {
            npc.controller = null;
            npc.Halt();
        }

        if (npc != null)
        {
            NpcTravelRuntime.RestoreSchedule(npc, walk.OriginalIgnoreScheduleToday, walk.OriginalFollowSchedule);
            if (returnToSchedule)
            {
                this.returnNpcToSchedule(npc);
            }
        }

        this.pendingWalks.Remove(walk);
    }

    private static string BuildWorldActionReason(string requestedReason, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedReason)
            ? fallback
            : $"{requestedReason.Trim()}; {fallback}";
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
