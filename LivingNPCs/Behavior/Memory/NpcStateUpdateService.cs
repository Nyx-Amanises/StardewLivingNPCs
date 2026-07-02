using System;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Applies interaction-driven mutations to a single NPC's <see cref="LivingNpcState"/>: small
/// behaviors, gifts, event interactions, conversation starts, and observed romantic attention.
/// Cross-cutting effects (familiarity caps, emotions, trust, conflict repair) stay owned by
/// <see cref="BehaviorMemory"/> and are injected as delegates, mirroring how
/// <see cref="HelpRequestMemoryService"/> is wired.
/// </summary>
internal sealed class NpcStateUpdateService
{
    private readonly Action<LivingNpcState, int, int> addFamiliarity;
    private readonly Action<LivingNpcState, string, int, string> applyEmotion;
    private readonly Action<LivingNpcState, int> applyRelationshipTrustDelta;
    private readonly Action<LivingNpcState, string> markRepairGiftReceived;
    private readonly Func<LivingNpcState, int, bool, bool, int> applyConflictRepair;
    private readonly Func<LivingNpcState, ValleyTalkConflictCandidate, bool> storeConflict;

    public NpcStateUpdateService(
        Action<LivingNpcState, int, int> addFamiliarity,
        Action<LivingNpcState, string, int, string> applyEmotion,
        Action<LivingNpcState, int> applyRelationshipTrustDelta,
        Action<LivingNpcState, string> markRepairGiftReceived,
        Func<LivingNpcState, int, bool, bool, int> applyConflictRepair,
        Func<LivingNpcState, ValleyTalkConflictCandidate, bool> storeConflict)
    {
        this.addFamiliarity = addFamiliarity;
        this.applyEmotion = applyEmotion;
        this.applyRelationshipTrustDelta = applyRelationshipTrustDelta;
        this.markRepairGiftReceived = markRepairGiftReceived;
        this.applyConflictRepair = applyConflictRepair;
        this.storeConflict = storeConflict;
    }

    public LivingNpcState UpdateForBehavior(LivingNpcState state, NPC npc, BehaviorIntent intent, string source)
    {
        var world = WorldContext.For(npc);
        int attentionDelta = source == "passive" ? 6 : 12;
        int opennessDelta = source == "passive" ? 2 : 4;
        int familiarityGain = source == "passive" ? 0 : 1;

        switch (intent.Type)
        {
            case BehaviorIntentType.FacePlayer:
                state.Mood = source == "passive" ? "Aware" : "Curious";
                state.CurrentInclination = "Acknowledging";
                break;

            case BehaviorIntentType.Emote:
                state.Mood = "Expressive";
                state.CurrentInclination = "Reacting";
                attentionDelta += 4;
                break;

            case BehaviorIntentType.ApproachPlayer:
                state.Mood = "Engaged";
                state.CurrentInclination = "OpenToTalk";
                attentionDelta += 8;
                opennessDelta += 6;
                familiarityGain += 1;
                break;

            case BehaviorIntentType.Pause:
                state.Mood = "Attentive";
                state.CurrentInclination = "Acknowledging";
                attentionDelta += 2;
                break;

            case BehaviorIntentType.LookAround:
                state.Mood = "Aware";
                state.CurrentInclination = "Aware";
                attentionDelta += 1;
                break;

            case BehaviorIntentType.StepAway:
                state.Mood = source == "passive" ? "Careful" : "Guarded";
                state.CurrentInclination = "GentleBoundary";
                attentionDelta += 2;
                opennessDelta -= 4;
                break;
        }

        this.addFamiliarity(state, familiarityGain, 6);
        state.Attention = LivingNpcState.ClampScore(state.Attention + attentionDelta);
        state.Openness = LivingNpcState.ClampScore(state.Openness + opennessDelta);
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = source == "passive" ? "passive nearby reaction" : "small behavior near the farmer";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateForGift(LivingNpcState state, NPC npc, GiftMemoryDetails gift)
    {
        var world = WorldContext.For(npc);
        if (state.LastGiftTotalDays != Game1.Date.TotalDays)
        {
            state.GiftsToday = 0;
        }

        state.GiftsToday += 1;
        state.LastGiftName = gift.ItemName;
        state.LastGiftTaste = gift.IsBirthdayGift
            ? $"{gift.TasteLabel}，生日礼物"
            : gift.TasteLabel;
        state.LastGiftTotalDays = Game1.Date.TotalDays;
        state.LastGiftTimeOfDay = Game1.timeOfDay;

        int familiarityGain = gift.TasteScore switch
        {
            0 => 4,
            2 => 3,
            8 => 1,
            _ => 0
        };

        int attentionDelta = gift.TasteScore switch
        {
            0 => 14,
            2 => 10,
            4 => 4,
            6 => 6,
            _ => 6
        };

        int opennessDelta = gift.TasteScore switch
        {
            0 => 12,
            2 => 8,
            4 => -5,
            6 => -12,
            _ => 2
        };

        state.Mood = gift.TasteScore switch
        {
            0 => "Delighted",
            2 => "Pleased",
            4 => "Awkward",
            6 => "Upset",
            _ => "GiftAware"
        };
        state.CurrentInclination = gift.TasteScore is 0 or 2 ? "OpenToTalk" : gift.TasteScore == 6 ? "Reserved" : "Acknowledging";

        switch (gift.TasteScore)
        {
            case 0:
                this.applyEmotion(state, "Happy", 18, $"the farmer gave them a loved gift: {gift.ItemName}{(gift.IsBirthdayGift ? " on their birthday" : string.Empty)}");
                this.applyRelationshipTrustDelta(state, 4);
                this.markRepairGiftReceived(state, gift.ItemName);
                this.applyConflictRepair(state, 18, false, false);
                break;

            case 2:
                this.applyEmotion(state, "Happy", 10, $"the farmer gave them a liked gift: {gift.ItemName}{(gift.IsBirthdayGift ? " on their birthday" : string.Empty)}");
                this.applyRelationshipTrustDelta(state, 2);
                this.markRepairGiftReceived(state, gift.ItemName);
                this.applyConflictRepair(state, 10, false, false);
                break;

            case 4:
                this.applyEmotion(state, "Uneasy", 14, $"the farmer gave them a disliked gift: {gift.ItemName}");
                this.storeConflict(state, new ValleyTalkConflictCandidate
                {
                    CauseKind = "gift",
                    Summary = $"The farmer gave them a disliked gift: {gift.ItemName}.",
                    Severity = 15
                });
                break;

            case 6:
                this.applyEmotion(state, "Upset", 28, $"the farmer gave them a hated gift: {gift.ItemName}");
                this.storeConflict(state, new ValleyTalkConflictCandidate
                {
                    CauseKind = "gift",
                    Summary = $"The farmer gave them a hated gift: {gift.ItemName}.",
                    Severity = 35
                });
                break;
        }

        this.addFamiliarity(state, familiarityGain, 8);
        state.Attention = LivingNpcState.ClampScore(state.Attention + attentionDelta);
        state.Openness = LivingNpcState.ClampScore(state.Openness + opennessDelta);
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer offered a gift";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateForEventInteraction(LivingNpcState state, NPC npc, string eventContext)
    {
        var world = WorldContext.For(npc);
        state.LastEventContext = eventContext;
        state.LastEventTotalDays = Game1.Date.TotalDays;
        state.LastEventTimeOfDay = Game1.timeOfDay;
        state.Mood = "EventAware";
        state.CurrentInclination = state.Familiarity >= 35 || world.FriendshipHearts >= 4 ? "OpenToTalk" : "Acknowledging";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 8);
        state.Openness = LivingNpcState.ClampScore(state.Openness + (world.FriendshipHearts >= 4 ? 4 : 1));
        this.addFamiliarity(state, 1, 8);
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer interacted during an event";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateForConversationStart(LivingNpcState state, NPC npc)
    {
        var world = WorldContext.For(npc);
        ConversationStateService.UpdateConversationRhythm(state, world.FriendshipHearts, Game1.Date.TotalDays, Game1.timeOfDay);
        int repeatFamiliarityLimit = Math.Min(3, state.DailyConversationComfortLimit);
        int familiarityGain = state.ConversationsToday == 1 ? 3 : state.ConversationsToday <= repeatFamiliarityLimit ? 1 : 0;
        if (state.ConversationsToday == 1 && state.ConsecutiveConversationDays >= 3)
        {
            familiarityGain += 1;
        }

        this.addFamiliarity(state, familiarityGain, 6);
        state.Mood = state.Openness >= 60 || state.Familiarity >= 40 ? "Warm" : "Attentive";
        state.CurrentInclination = "OpenToTalk";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 18);
        state.Openness = LivingNpcState.ClampScore(state.Openness + (state.Familiarity >= 40 ? 8 : 6));
        if (state.HasUnresolvedConflict)
        {
            int severity = state.HighestUnresolvedConflictSeverity;
            state.Mood = severity >= 60 ? "Guarded" : "Polite";
            state.CurrentInclination = severity >= 60 ? "NeedsSpace" : "Reserved";
            state.Openness = LivingNpcState.ClampScore(state.Openness - Math.Min(18, 4 + (severity / 5)));
        }

        ConversationStateService.ApplyConversationRhythmInfluence(state);
        ConversationStateService.ApplyObservedConcern(
            state,
            Game1.Date.TotalDays,
            Game1.player.health,
            Game1.player.maxHealth,
            (concernedState, emotion, intensityDelta, reason) => this.applyEmotion(concernedState, emotion, intensityDelta, reason)
        );
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer started a conversation";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateForObservedRomanticInteraction(LivingNpcState state, NPC otherNpc)
    {
        this.applyEmotion(
            state,
            "Jealous",
            state.RelationshipTrust >= 70 ? 10 : 6,
            $"they noticed the farmer giving romantic attention to {otherNpc.displayName}"
        );
        state.Mood = "Guarded";
        state.CurrentInclination = "Measured";
        state.Openness = LivingNpcState.ClampScore(state.Openness - 4);
        state.LastInteraction = $"they noticed the farmer being close with {otherNpc.displayName}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }
}
