using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class DialogueBehaviorInfluenceRuntime
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly Func<NPC, bool> tryApproachPlayer;
    private readonly Func<NPC, bool> tryStepAway;
    private readonly Func<NPC, bool> tryPause;
    private readonly Func<NPC, bool> tryFacePlayer;
    private readonly Func<NPC, string, bool> tryShowNpcSpeechBubble;
    private readonly Action<NPC, string> pushInteractionContext;

    public DialogueBehaviorInfluenceRuntime(
        ModConfig config,
        BehaviorMemory memory,
        Func<NPC, bool> tryApproachPlayer,
        Func<NPC, bool> tryStepAway,
        Func<NPC, bool> tryPause,
        Func<NPC, bool> tryFacePlayer,
        Func<NPC, string, bool> tryShowNpcSpeechBubble,
        Action<NPC, string> pushInteractionContext)
    {
        this.config = config;
        this.memory = memory;
        this.tryApproachPlayer = tryApproachPlayer;
        this.tryStepAway = tryStepAway;
        this.tryPause = tryPause;
        this.tryFacePlayer = tryFacePlayer;
        this.tryShowNpcSpeechBubble = tryShowNpcSpeechBubble;
        this.pushInteractionContext = pushInteractionContext;
    }

    public void TryApply()
    {
        if (!this.config.EnableDialogueDrivenBehaviors
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null)
            {
                continue;
            }

            foreach (var influence in state.DialogueBehaviorInfluences.Where(influence => influence.Status == "Active").ToList())
            {
                if (influence.ExpiresTotalDays < Game1.Date.TotalDays)
                {
                    influence.Status = "Expired";
                    influence.LastUpdatedTotalDays = Game1.Date.TotalDays;
                    influence.LastUpdatedTimeOfDay = Game1.timeOfDay;
                }
            }

            var activeInfluence = state.ActiveDialogueBehaviorInfluences
                .Where(influence => influence.LastTriggeredTotalDays != Game1.Date.TotalDays)
                .OrderByDescending(influence => influence.Intensity)
                .ThenBy(influence => influence.CreatedTotalDays)
                .FirstOrDefault();
            if (activeInfluence == null || !this.CanTry(npc, activeInfluence))
            {
                continue;
            }

            if (!this.TryApply(npc, state, activeInfluence))
            {
                continue;
            }

            activeInfluence.TriggerCount += 1;
            activeInfluence.LastTriggeredTotalDays = Game1.Date.TotalDays;
            activeInfluence.LastTriggeredTimeOfDay = Game1.timeOfDay;
            activeInfluence.LastUpdatedTotalDays = Game1.Date.TotalDays;
            activeInfluence.LastUpdatedTimeOfDay = Game1.timeOfDay;
            if (activeInfluence.TriggerCount >= activeInfluence.MaxTriggers)
            {
                activeInfluence.Status = "Spent";
            }

            this.memory.RecordNpcWorldAction(
                npc,
                "DialogueDrivenBehavior",
                $"a recent conversation shaped later behavior: {activeInfluence.Type}; {activeInfluence.Summary}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.pushInteractionContext(npc, $"Applied dialogue-driven behavior for {npc.Name}: {activeInfluence.PromptLabel}.");
        }
    }

    private bool CanTry(NPC npc, DialogueBehaviorInfluenceFact influence)
    {
        float distance = Vector2.Distance(npc.Tile, Game1.player.Tile);
        return influence.Type switch
        {
            "visit_location" => this.IsTargetLocationCurrent(influence)
                && distance <= this.config.MaxInteractionDistanceTiles + 2,
            "offended" or "give_space" => distance <= 2.5f && npc.controller == null,
            "stay_near" or "comforted" or "pause_to_talk" => distance <= this.config.MaxInteractionDistanceTiles && npc.controller == null,
            _ => false
        };
    }

    private bool TryApply(NPC npc, LivingNpcState state, DialogueBehaviorInfluenceFact influence)
    {
        bool executed = influence.Type switch
        {
            "visit_location" => this.TryReactAtLocation(npc, influence),
            "comforted" => this.TryApplyComforted(npc, state),
            "stay_near" => this.TryApplyStayNear(npc, state),
            "offended" => this.TryApplyDistance(npc, state, "they kept more distance after the conversation"),
            "give_space" => this.TryApplyDistance(npc, state, "they gave the farmer a little more room after the conversation"),
            "pause_to_talk" => this.TryApplyPause(npc, state),
            _ => false
        };

        if (!executed)
        {
            return false;
        }

        state.LastInteraction = $"a recent conversation shaped their later behavior: {influence.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return true;
    }

    private bool TryApplyComforted(NPC npc, LivingNpcState state)
    {
        bool moved = Vector2.Distance(npc.Tile, Game1.player.Tile) > 2.25f && this.tryApproachPlayer(npc);
        if (!moved)
        {
            this.tryFacePlayer(npc);
            if (this.config.AllowEmotes)
            {
                npc.doEmote(20);
            }
        }

        state.Mood = "Comfortable";
        state.CurrentInclination = "OpenToTalk";
        return moved || this.config.AllowFacePlayer;
    }

    private bool TryApplyStayNear(NPC npc, LivingNpcState state)
    {
        bool moved = Vector2.Distance(npc.Tile, Game1.player.Tile) > 2.25f && this.tryApproachPlayer(npc);
        if (!moved)
        {
            this.tryFacePlayer(npc);
        }

        state.Mood = "Engaged";
        state.CurrentInclination = "OpenToTalk";
        return moved || this.config.AllowFacePlayer;
    }

    private bool TryApplyDistance(NPC npc, LivingNpcState state, string lastInteraction)
    {
        bool moved = this.tryStepAway(npc);
        if (!moved)
        {
            return false;
        }

        state.Mood = "Guarded";
        state.CurrentInclination = "GentleBoundary";
        state.LastInteraction = lastInteraction;
        return true;
    }

    private bool TryApplyPause(NPC npc, LivingNpcState state)
    {
        if (!this.tryPause(npc))
        {
            return false;
        }

        state.Mood = "Attentive";
        state.CurrentInclination = "Acknowledging";
        return true;
    }

    private bool TryReactAtLocation(NPC npc, DialogueBehaviorInfluenceFact influence)
    {
        if (!this.IsTargetLocationCurrent(influence))
        {
            return false;
        }

        this.tryFacePlayer(npc);
        this.tryShowNpcSpeechBubble(npc, BuildLocationRemark(influence));
        return true;
    }

    private bool IsTargetLocationCurrent(DialogueBehaviorInfluenceFact influence)
    {
        string currentLocation = TravelLocationRules.Normalize(Game1.currentLocation?.Name ?? string.Empty, string.Empty);
        return !string.IsNullOrWhiteSpace(influence.TargetLocation)
            && influence.TargetLocation == currentLocation;
    }

    private static string BuildLocationRemark(DialogueBehaviorInfluenceFact influence)
    {
        return influence.TargetLocation switch
        {
            "Beach" => "你提到海边后，我也想来看看。",
            "ArchaeologyHouse" => "你提到这里后，我就想顺路来看看。",
            "Forest" => "刚才聊到森林，我就想来走走。",
            "Mountain" => "你提到山上后，我也有点在意这里。",
            _ => $"你刚才提到{influence.TargetLocationLabel}，我后来还想着这件事。"
        };
    }
}
