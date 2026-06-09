using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;

namespace LivingNPCs.Behavior;

internal sealed class DirectWorldActionRuntime
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly CanUseWorldActionHandler canUseWorldAction;
    private readonly CompanionOutingAnchorSelector festivalAnchorSelector;

    public DirectWorldActionRuntime(
        ModConfig config,
        BehaviorMemory memory,
        BehaviorFeedbackService feedback,
        CanUseWorldActionHandler canUseWorldAction)
    {
        this.config = config;
        this.memory = memory;
        this.feedback = feedback;
        this.canUseWorldAction = canUseWorldAction;
        this.festivalAnchorSelector = new CompanionOutingAnchorSelector(BehaviorActionExecutor.IsSafeDestinationTile);
    }

    public bool TryWaterNearbyCrops(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.canUseWorldAction(npc, "farm_help", requireFriendly: true, out reason, allowDuringEvents: false, allowDistantWhenExplicit: false))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiFarmHelp || state == null || state.LastAiFarmHelpTotalDays == Game1.Date.TotalDays)
        {
            reason = "farm help is disabled or already used today";
            return false;
        }

        if (Game1.currentLocation is not Farm farm)
        {
            reason = "the player is not on the farm";
            return false;
        }

        int requestedTiles = action.TileCount <= 0 ? 6 : action.TileCount;
        int maxTiles = Math.Clamp(requestedTiles, 1, this.config.MaxAiWateredTilesPerAction);
        var nearbyTiles = farm.terrainFeatures.Pairs
            .Where(pair => pair.Value is HoeDirt dirt && dirt.crop != null && dirt.state.Value != 1)
            .OrderBy(pair => Vector2.Distance(pair.Key, Game1.player.Tile))
            .Take(maxTiles)
            .ToList();

        foreach (var pair in nearbyTiles)
        {
            if (pair.Value is HoeDirt dirt)
            {
                dirt.state.Value = 1;
                dirt.updateNeighbors();
            }
        }

        if (nearbyTiles.Count == 0)
        {
            reason = "there are no nearby unwatered crops";
            return false;
        }

        state.LastAiFarmHelpTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "WateredNearbyCrops",
            BuildWorldActionReason(action.Reason, $"they watered {nearbyTiles.Count} nearby crop tiles for the farmer"),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they helped the farmer with watering");
        this.feedback.Show($"LivingNPCs：{npc.displayName} 帮你浇了 {nearbyTiles.Count} 格作物。");
        return true;
    }

    public bool TryFestivalInteraction(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiFestivalInteractions)
        {
            reason = "festival interactions are disabled";
            return false;
        }

        if (!this.canUseWorldAction(npc, "festival_interaction", requireFriendly: false, out reason, allowDuringEvents: true, allowDistantWhenExplicit: false))
        {
            return false;
        }

        if (!Game1.eventUp)
        {
            reason = "there is no active event";
            return false;
        }

        var state = this.memory.GetState(npc);
        bool movedToAnchor = this.TryMoveToFestivalAnchor(npc, action, out string anchorLabel);
        npc.doEmote(20);
        if (state != null)
        {
            string anchorCue = movedToAnchor && !string.IsNullOrWhiteSpace(anchorLabel)
                ? $"they shared a light special interaction during an event scene while settling {anchorLabel}"
                : "they shared a light special interaction during an event scene";
            this.memory.RecordNpcWorldAction(
                npc,
                "FestivalInteraction",
                BuildWorldActionReason(action.Reason, anchorCue),
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(
                state,
                movedToAnchor && !string.IsNullOrWhiteSpace(anchorLabel)
                    ? $"they shared a small festival interaction {anchorLabel}"
                    : "they shared a small festival interaction"
            );
        }

        return true;
    }

    public bool TryAssistQuest(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiQuestAssists)
        {
            reason = "quest assists are disabled";
            return false;
        }

        if (!this.canUseWorldAction(npc, "assist_quest", requireFriendly: true, out reason, allowDuringEvents: false, allowDistantWhenExplicit: false))
        {
            return false;
        }

        if (Game1.player?.questLog == null || Game1.player.questLog.Count == 0)
        {
            reason = "the farmer has no active quest";
            return false;
        }

        var state = this.memory.GetState(npc);
        npc.doEmote(16);
        if (state != null)
        {
            string questCue = string.IsNullOrWhiteSpace(action.QuestHint)
                ? "an active task"
                : action.QuestHint.Trim();
            this.memory.RecordNpcWorldAction(
                npc,
                "AssistedQuest",
                BuildWorldActionReason(action.Reason, $"they offered light non-completing help around {questCue}"),
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(state, "they offered light task help");
        }

        return true;
    }

    private static string BuildWorldActionReason(string requestedReason, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedReason)
            ? fallback
            : $"{requestedReason.Trim()}; {fallback}";
    }

    private bool TryMoveToFestivalAnchor(NPC npc, ValleyTalkWorldActionRequest action, out string anchorLabel)
    {
        anchorLabel = string.Empty;
        GameLocation? location = Game1.currentLocation;
        if (location == null || !this.config.AllowApproachPlayer)
        {
            return false;
        }

        string targetLocation = ResolveFestivalAnchorTarget(action);
        string sourceLocation = BehaviorMemory.NormalizeTravelLocation(location.Name, location.Name);
        string activityStyle = CompanionOutingRules.DetermineActivityStyle(targetLocation, action.Reason);
        if (!this.festivalAnchorSelector.TrySelect(
                npc,
                location,
                targetLocation,
                sourceLocation,
                activityStyle,
                Game1.Date.TotalDays,
                new HashSet<Point>(),
                out CompanionOutingAnchor? anchor)
            || anchor == null)
        {
            return false;
        }

        anchorLabel = anchor.SemanticLabel;
        if (Vector2.Distance(npc.Tile, new Vector2(anchor.Tile.X, anchor.Tile.Y)) <= 0.75f)
        {
            npc.controller = null;
            npc.Halt();
            npc.faceDirection(anchor.FacingDirection);
            return true;
        }

        npc.controller = new PathFindController(npc, location, anchor.Tile, anchor.FacingDirection);
        return npc.controller != null;
    }

    private static string ResolveFestivalAnchorTarget(ValleyTalkWorldActionRequest action)
    {
        string requested = BehaviorMemory.NormalizeTravelLocation(action.TargetLocation, string.Empty);
        if (requested == "FlowerDance" || LooksLikeFlowerDanceMoment(action.Reason))
        {
            return "FlowerDance";
        }

        if (TravelLocationRules.IsKnownPublicOutingTarget(requested))
        {
            return requested;
        }

        string current = BehaviorMemory.NormalizeTravelLocation(Game1.currentLocation?.Name ?? string.Empty, string.Empty);
        return string.IsNullOrWhiteSpace(current)
            ? "Town"
            : current;
    }

    private static bool LooksLikeFlowerDanceMoment(string reason)
    {
        return (Game1.currentLocation?.Name == "Forest" && Game1.currentSeason == "spring" && Game1.dayOfMonth == 24)
            || ConversationActionCueRules.ContainsAny(
                reason,
                "花舞",
                "花田",
                "跳舞",
                "flower dance",
                "flower festival",
                "dance meadow"
            );
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
