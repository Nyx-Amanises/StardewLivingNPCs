using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;

namespace LivingNPCs.Behavior;

internal sealed class DirectWorldActionRuntime
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly CanUseWorldActionHandler canUseWorldAction;

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
        npc.doEmote(20);
        if (state != null)
        {
            this.memory.RecordNpcWorldAction(
                npc,
                "FestivalInteraction",
                BuildWorldActionReason(action.Reason, "they shared a light special interaction during an event scene"),
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(state, "they shared a small festival interaction");
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

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
