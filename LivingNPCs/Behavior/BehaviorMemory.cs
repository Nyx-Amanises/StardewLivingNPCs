using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemory
{
    private readonly Dictionary<string, List<BehaviorMemoryEntry>> entriesByNpc = new();
    private readonly Dictionary<string, int> dailyCountsByNpc = new();
    private readonly Dictionary<string, LivingNpcState> statesByNpc = new();
    private HelpRequestMemoryService? helpRequests;
    private int lastStateDecayTotalDays = -1;

    private HelpRequestMemoryService HelpRequests =>
        this.helpRequests ??= new HelpRequestMemoryService(
            this.AddFamiliarity,
            this.ApplyRelationshipTrustDelta,
            this.ApplyEmotion,
            this.CreateEntry,
            this.AddEntry
        );

    public void Load(BehaviorMemorySaveData? saveData, int maxEntriesPerNpc)
    {
        this.entriesByNpc.Clear();
        this.dailyCountsByNpc.Clear();
        this.statesByNpc.Clear();
        this.lastStateDecayTotalDays = saveData?.LastStateDecayTotalDays ?? -1;

        if (saveData == null)
        {
            return;
        }

        foreach (var pair in saveData.EntriesByNpc ?? new Dictionary<string, List<BehaviorMemoryEntry>>())
        {
            var entries = pair.Value
                .Where(entry => !string.IsNullOrWhiteSpace(entry.NpcName))
                .OrderBy(entry => entry.TotalDays)
                .ThenBy(entry => entry.TimeOfDay)
                .TakeLast(maxEntriesPerNpc)
                .ToList();

            if (entries.Count > 0)
            {
                this.entriesByNpc[pair.Key] = entries;
            }
        }

        if (saveData.StatesByNpc != null)
        {
            foreach (var pair in saveData.StatesByNpc)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value.NpcName))
                {
                    continue;
                }

                pair.Value.Clamp();
                this.statesByNpc[pair.Key] = pair.Value;
            }
        }

        this.RebuildDailyCounts();
    }

    public BehaviorMemorySaveData ToSaveData()
    {
        return new BehaviorMemorySaveData
        {
            EntriesByNpc = this.entriesByNpc.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList()
            ),
            StatesByNpc = this.statesByNpc.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone()
            ),
            LastStateDecayTotalDays = this.lastStateDecayTotalDays
        };
    }

    public void ResetDaily()
    {
        this.dailyCountsByNpc.Clear();
    }

    public bool HasDailyBudget(NPC npc, int maxPerDay)
    {
        return !this.dailyCountsByNpc.TryGetValue(npc.Name, out int count) || count < maxPerDay;
    }

    public LivingNpcState? GetState(NPC npc)
    {
        return this.statesByNpc.TryGetValue(npc.Name, out var state)
            ? state
            : null;
    }

    public IEnumerable<LivingNpcState> GetTrackedStates()
    {
        return this.statesByNpc.Values.ToList();
    }

    public IReadOnlyList<CommunityImpressionFact> GetRetellableCommunityImpressions(LivingNpcState state, int maxCount)
    {
        this.RefreshMemoryStores(state);
        return CommunityImpressionStore.GetRetellable(state, maxCount, Game1.Date.TotalDays);
    }

    public void MarkCommunityImpressionShared(CommunityImpressionFact memory)
    {
        CommunityImpressionStore.MarkShared(memory, Game1.Date.TotalDays, Game1.timeOfDay);
    }

    public BehaviorMemoryEntry Record(NPC npc, BehaviorIntent intent, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(npc, "Behavior", intent.Type.ToString(), intent.Reason);
        this.AddEntry(entry, maxEntriesPerNpc);
        this.dailyCountsByNpc[npc.Name] = this.dailyCountsByNpc.TryGetValue(npc.Name, out int count) ? count + 1 : 1;
        return entry;
    }

    public BehaviorMemoryEntry RecordConversationStart(NPC npc, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(
            npc,
            "Conversation",
            "ConversationStarted",
            "the farmer approached and started a conversation"
        );

        this.AddEntry(entry, maxEntriesPerNpc);
        return entry;
    }

    public BehaviorMemoryEntry RecordGiftOffered(NPC npc, GiftMemoryDetails gift, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(
            npc,
            "Gift",
            "GiftOffered",
            $"the farmer offered {gift.ItemName}; gift taste: {gift.TastePromptLabel}"
        );

        this.AddEntry(entry, maxEntriesPerNpc);
        return entry;
    }

    public BehaviorMemoryEntry RecordEventInteraction(NPC npc, string eventContext, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(
            npc,
            "Event",
            "EventInteraction",
            $"the farmer interacted with them during an event scene: {eventContext}"
        );

        this.AddEntry(entry, maxEntriesPerNpc);
        return entry;
    }

    public BehaviorMemoryEntry RecordNpcWorldAction(NPC npc, string action, string reason, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(
            npc,
            "NpcAction",
            action,
            reason
        );

        this.AddEntry(entry, maxEntriesPerNpc);
        return entry;
    }

    public bool RecordCommunityImpression(
        NPC observer,
        NPC subject,
        string kind,
        string summary,
        string source,
        string visibility,
        int transmissionDepth,
        int distortionLevel,
        string heardFromNpcName,
        string circleKey,
        int importance,
        int maxEntriesPerNpc)
    {
        if (string.Equals(observer.Name, subject.Name, System.StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        var state = this.GetOrCreateState(observer);
        bool stored = CommunityImpressionStore.Store(
            state,
            subject.Name,
            subject.displayName,
            kind,
            summary,
            source,
            visibility,
            transmissionDepth,
            distortionLevel,
            heardFromNpcName,
            circleKey,
            importance,
            Game1.Date.TotalDays,
            Game1.timeOfDay,
            out bool created,
            out string normalizedKind
        );
        if (!stored)
        {
            return false;
        }

        if (created)
        {
            var entry = this.CreateEntry(observer, "SocialMemory", normalizedKind, summary);
            this.AddEntry(entry, maxEntriesPerNpc);
        }

        return true;
    }

    internal int StoreLongTermMemoriesFromAnalysisForTesting(
        LivingNpcState state,
        string analysisJson,
        int currentTotalDays,
        int currentTimeOfDay = 1200)
    {
        var analysis = ParseExchangeAnalysis(analysisJson);
        int stored = 0;
        foreach (var candidate in analysis.Memories
                     .Where(memory => !memory.PlayerPreference)
                     .Where(memory => memory.Importance >= 40 && !string.IsNullOrWhiteSpace(memory.Summary))
                     .OrderByDescending(memory => memory.Importance)
                     .Take(4))
        {
            if (this.StoreLongTermMemory(state, candidate, currentTotalDays, currentTimeOfDay))
            {
                stored++;
            }
        }

        return stored;
    }

    public ValleyTalkExchangeResult RecordValleyTalkExchange(
        NPC npc,
        string playerText,
        string npcResponse,
        string analysisJson,
        int maxEntriesPerNpc,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests,
        int maxExtraFriendshipPerDay,
        int maxDialogueBehaviorInfluenceDays)
    {
        var analysis = ParseExchangeAnalysis(analysisJson);
        var state = this.GetOrCreateState(npc);
        int storedMemories = 0;
        int storedPlayerPreferences = 0;
        int storedHelpRequests = 0;
        int updatedHelpRequests = 0;
        int storedConflicts = 0;
        int storedBehaviorInfluences = 0;
        int resolvedConflicts = 0;
        bool emotionChanged = false;
        var fulfilledHelpRequests = new List<NpcHelpRequestFact>();

        foreach (var candidate in analysis.Memories
                     .Where(memory => memory.Importance >= 40 && !string.IsNullOrWhiteSpace(memory.Summary))
                     .OrderByDescending(memory => memory.Importance)
                     .Take(4))
        {
            if (candidate.PlayerPreference && this.StorePlayerPreferenceMemory(state, candidate))
            {
                storedPlayerPreferences++;
                var entry = this.CreateEntry(
                    npc,
                    "PlayerPreferenceMemory",
                    candidate.PlayerPreferenceKind,
                    candidate.Summary
                );
                this.AddEntry(entry, maxEntriesPerNpc);
                continue;
            }

            if (this.StoreLongTermMemory(state, candidate))
            {
                storedMemories++;
                var entry = this.CreateEntry(
                    npc,
                    "LongTermMemory",
                    candidate.Kind,
                    candidate.Summary
                );
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.HelpRequests
                     .Where(request =>
                         maxPendingHelpRequestsPerNpc > 0
                         && !string.IsNullOrWhiteSpace(request.Summary)
                         && NormalizeHelpRequestType(request.Type) == "item_request")
                     .Take(1))
        {
            if (this.HelpRequests.Store(
                    npc,
                    state,
                    candidate,
                    playerText,
                    maxPendingHelpRequestsPerNpc,
                    helpRequestCooldownDays,
                    minRelationshipTrustForHelpRequests))
            {
                storedHelpRequests++;
                var entry = this.CreateEntry(
                    npc,
                    "HelpRequest",
                    candidate.Type,
                    candidate.Summary
                );
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.HelpRequestUpdates
                     .Where(update => !string.IsNullOrWhiteSpace(update.Summary))
                     .Take(2))
        {
            if (this.HelpRequests.ApplyUpdate(state, candidate, out NpcHelpRequestFact? fulfilledRequest))
            {
                updatedHelpRequests++;
                if (fulfilledRequest != null)
                {
                    fulfilledHelpRequests.Add(fulfilledRequest);
                }
                var entry = this.CreateEntry(
                    npc,
                    "HelpRequestUpdate",
                    candidate.Status,
                    string.IsNullOrWhiteSpace(candidate.Resolution) ? candidate.Summary : candidate.Resolution
                );
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.Conflicts
                     .Where(conflict => !string.IsNullOrWhiteSpace(conflict.Summary) && conflict.Severity > 0)
                     .Take(2))
        {
            if (this.StoreConflict(state, candidate))
            {
                storedConflicts++;
                var entry = this.CreateEntry(
                    npc,
                    "Conflict",
                    candidate.CauseKind,
                    candidate.Summary
                );
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.BehaviorInfluences
                     .Where(influence => !string.IsNullOrWhiteSpace(influence.Summary))
                     .Take(2))
        {
            if (this.StoreDialogueBehaviorInfluence(npc, state, candidate, maxDialogueBehaviorInfluenceDays))
            {
                storedBehaviorInfluences++;
                var entry = this.CreateEntry(
                    npc,
                    "DialogueBehavior",
                    candidate.Type,
                    candidate.Summary
                );
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        if (analysis.EmotionImpact.HasEffect)
        {
            emotionChanged = this.ApplyDialogueEmotionImpact(state, analysis.EmotionImpact);
            resolvedConflicts += this.ApplyConflictRepair(
                state,
                analysis.EmotionImpact.RepairDelta,
                analysis.EmotionImpact.Apology,
                specificRepairTalk: analysis.EmotionImpact.RepairDelta > 0
            );
        }

        // Keep one deterministic fallback for nickname requests if the model forgets metadata.
        if (storedMemories == 0
            && storedPlayerPreferences == 0
            && storedHelpRequests == 0
            && updatedHelpRequests == 0
            && storedConflicts == 0
            && storedBehaviorInfluences == 0
            && TryExtractNicknameRequest(playerText, out string nickname))
        {
            string status = DetermineNicknameStatus(nickname, npcResponse);
            var fallbackMemory = new ValleyTalkMemoryCandidate
            {
                Kind = "preference",
                Summary = status switch
                {
                    "Accepted" => $"The farmer prefers to be called {nickname}, and this NPC accepted.",
                    "Rejected" => $"The farmer asked to be called {nickname}, but this NPC did not accept.",
                    _ => $"The farmer asked to be called {nickname}; acceptance is unclear."
                },
                Importance = 85
            };

            if (this.StoreLongTermMemory(state, fallbackMemory))
            {
                storedMemories++;
                var entry = this.CreateEntry(npc, "LongTermMemory", "preference", fallbackMemory.Summary);
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        int appliedFriendship = this.ApplyAiDialogueFriendship(state, analysis.RapportDelta, maxExtraFriendshipPerDay);
        if (storedMemories > 0
            || storedPlayerPreferences > 0
            || storedHelpRequests > 0
            || updatedHelpRequests > 0
            || storedConflicts > 0
            || storedBehaviorInfluences > 0
            || resolvedConflicts > 0
            || emotionChanged
            || appliedFriendship > 0)
        {
            if (storedMemories > 0 || storedPlayerPreferences > 0 || storedHelpRequests > 0)
            {
                state.LastInteraction = "the farmer shared something worth remembering";
            }
            else if (updatedHelpRequests > 0)
            {
                state.LastInteraction = "they updated a personal help request with the farmer";
            }
            else if (storedConflicts > 0)
            {
                state.LastInteraction = "the farmer caused interpersonal friction";
            }
            else if (storedBehaviorInfluences > 0)
            {
                state.LastInteraction = "the latest conversation changed how they may behave around the farmer";
            }
            else if (resolvedConflicts > 0)
            {
                state.LastInteraction = "the farmer helped repair a conflict";
            }
            else
            {
                state.LastInteraction = "the farmer had an AI conversation";
            }

            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        }

        return new ValleyTalkExchangeResult(
            storedMemories,
            storedPlayerPreferences,
            storedHelpRequests,
            updatedHelpRequests,
            storedConflicts,
            storedBehaviorInfluences,
            resolvedConflicts,
            emotionChanged,
            appliedFriendship,
            analysis.RapportDelta,
            analysis.EndConversation,
            analysis.AmbientFollowUp.Text,
            analysis.AmbientFollowUp.DelayMinutes,
            analysis.Actions,
            fulfilledHelpRequests
        );
    }

    public LivingNpcState UpdateStateForBehavior(NPC npc, BehaviorIntent intent, string source)
    {
        var state = this.GetOrCreateState(npc);
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

        this.AddFamiliarity(state, familiarityGain, dailyCap: 6);
        state.Attention = LivingNpcState.ClampScore(state.Attention + attentionDelta);
        state.Openness = LivingNpcState.ClampScore(state.Openness + opennessDelta);
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = source == "passive" ? "passive nearby reaction" : "small behavior near the farmer";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateStateForGift(NPC npc, GiftMemoryDetails gift)
    {
        var state = this.GetOrCreateState(npc);
        var world = WorldContext.For(npc);
        if (state.LastGiftTotalDays != Game1.Date.TotalDays)
        {
            state.GiftsToday = 0;
        }

        state.GiftsToday += 1;
        state.LastGiftName = gift.ItemName;
        state.LastGiftTaste = gift.TasteLabel;
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
                this.ApplyEmotion(state, "Happy", 18, $"the farmer gave them a loved gift: {gift.ItemName}");
                this.ApplyRelationshipTrustDelta(state, 4);
                this.MarkRepairGiftReceived(state, gift.ItemName);
                this.ApplyConflictRepair(state, 18, apology: false, specificRepairTalk: false);
                break;

            case 2:
                this.ApplyEmotion(state, "Happy", 10, $"the farmer gave them a liked gift: {gift.ItemName}");
                this.ApplyRelationshipTrustDelta(state, 2);
                this.MarkRepairGiftReceived(state, gift.ItemName);
                this.ApplyConflictRepair(state, 10, apology: false, specificRepairTalk: false);
                break;

            case 4:
                this.ApplyEmotion(state, "Uneasy", 14, $"the farmer gave them a disliked gift: {gift.ItemName}");
                this.StoreConflict(state, new ValleyTalkConflictCandidate
                {
                    CauseKind = "gift",
                    Summary = $"The farmer gave them a disliked gift: {gift.ItemName}.",
                    Severity = 15
                });
                break;

            case 6:
                this.ApplyEmotion(state, "Upset", 28, $"the farmer gave them a hated gift: {gift.ItemName}");
                this.StoreConflict(state, new ValleyTalkConflictCandidate
                {
                    CauseKind = "gift",
                    Summary = $"The farmer gave them a hated gift: {gift.ItemName}.",
                    Severity = 35
                });
                break;
        }

        this.AddFamiliarity(state, familiarityGain, dailyCap: 8);
        state.Attention = LivingNpcState.ClampScore(state.Attention + attentionDelta);
        state.Openness = LivingNpcState.ClampScore(state.Openness + opennessDelta);
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer offered a gift";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public IReadOnlyList<NpcHelpRequestFact> TryCompleteItemHelpRequests(NPC npc, GiftMemoryDetails gift, int maxEntriesPerNpc)
    {
        var state = this.GetOrCreateState(npc);
        return this.HelpRequests.TryCompleteItemHelpRequests(npc, state, gift, maxEntriesPerNpc);
    }

    public LivingNpcState UpdateStateForEventInteraction(NPC npc, string eventContext)
    {
        var state = this.GetOrCreateState(npc);
        var world = WorldContext.For(npc);
        state.LastEventContext = eventContext;
        state.LastEventTotalDays = Game1.Date.TotalDays;
        state.LastEventTimeOfDay = Game1.timeOfDay;
        state.Mood = "EventAware";
        state.CurrentInclination = state.Familiarity >= 35 || world.FriendshipHearts >= 4 ? "OpenToTalk" : "Acknowledging";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 8);
        state.Openness = LivingNpcState.ClampScore(state.Openness + (world.FriendshipHearts >= 4 ? 4 : 1));
        this.AddFamiliarity(state, amount: 1, dailyCap: 8);
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer interacted during an event";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateStateForExpiredHelpRequest(LivingNpcState state, NpcHelpRequestFact request)
    {
        return this.HelpRequests.UpdateExpiredRequest(state, request);
    }

    public LivingNpcState UpdateStateForConversationStart(NPC npc)
    {
        var state = this.GetOrCreateState(npc);
        var world = WorldContext.For(npc);
        ConversationStateService.UpdateConversationRhythm(state, world.FriendshipHearts, Game1.Date.TotalDays, Game1.timeOfDay);
        int repeatFamiliarityLimit = System.Math.Min(3, state.DailyConversationComfortLimit);
        int familiarityGain = state.ConversationsToday == 1 ? 3 : state.ConversationsToday <= repeatFamiliarityLimit ? 1 : 0;
        if (state.ConversationsToday == 1 && state.ConsecutiveConversationDays >= 3)
        {
            familiarityGain += 1;
        }

        this.AddFamiliarity(state, familiarityGain, dailyCap: 6);
        state.Mood = state.Openness >= 60 || state.Familiarity >= 40 ? "Warm" : "Attentive";
        state.CurrentInclination = "OpenToTalk";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 18);
        state.Openness = LivingNpcState.ClampScore(state.Openness + (state.Familiarity >= 40 ? 8 : 6));
        if (state.HasUnresolvedConflict)
        {
            int severity = state.HighestUnresolvedConflictSeverity;
            state.Mood = severity >= 60 ? "Guarded" : "Polite";
            state.CurrentInclination = severity >= 60 ? "NeedsSpace" : "Reserved";
            state.Openness = LivingNpcState.ClampScore(state.Openness - System.Math.Min(18, 4 + (severity / 5)));
        }

        ConversationStateService.ApplyConversationRhythmInfluence(state);
        ConversationStateService.ApplyObservedConcern(
            state,
            Game1.Date.TotalDays,
            Game1.player.health,
            Game1.player.maxHealth,
            this.ApplyEmotion
        );
        ConversationStateService.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer started a conversation";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateStateForObservedRomanticInteraction(NPC observer, NPC otherNpc)
    {
        var state = this.GetOrCreateState(observer);
        this.ApplyEmotion(
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

    public void DecayStates(int dailyDecay, int emotionDailyDecay, int conflictDailyDecay)
    {
        if (this.lastStateDecayTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        this.lastStateDecayTotalDays = Game1.Date.TotalDays;
        foreach (var state in this.statesByNpc.Values)
        {
            var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
            state.Familiarity = LivingNpcState.ClampScore(state.Familiarity);
            if (state.LastFamiliarityGainTotalDays != Game1.Date.TotalDays)
            {
                state.FamiliarityGainedToday = 0;
            }

            if (state.LastAiFriendshipTotalDays != Game1.Date.TotalDays)
            {
                state.AiFriendshipGainedToday = 0;
            }

            ConversationStateService.RefreshConversationDay(state, Game1.Date.TotalDays);
            if (state.LastGiftTotalDays != Game1.Date.TotalDays)
            {
                state.GiftsToday = 0;
            }

            this.RefreshMemoryStores(state);
            this.FadeCommunityImpressions(state);
            if (dailyDecay <= 0)
            {
                this.DecayEmotionAndConflicts(state, emotionDailyDecay, conflictDailyDecay, emotionalStyle);
                continue;
            }

            state.Attention = LivingNpcState.MoveToward(state.Attention, 35, dailyDecay);
            state.Openness = LivingNpcState.MoveToward(state.Openness, 50, dailyDecay / 2);
            state.CurrentInclination = state.Attention >= 55 ? "Aware" : "Neutral";
            state.Mood = state.Openness >= 58 ? "Calm" : "Neutral";
            state.LastInteraction = "time passed";
            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
            this.DecayEmotionAndConflicts(state, emotionDailyDecay, conflictDailyDecay, emotionalStyle);
        }
    }

    private void FadeCommunityImpressions(LivingNpcState state)
    {
        CommunityImpressionStore.Fade(state, Game1.Date.TotalDays);
    }

    private void AddFamiliarity(LivingNpcState state, int amount, int dailyCap)
    {
        if (amount <= 0 || dailyCap <= 0)
        {
            return;
        }

        if (state.LastFamiliarityGainTotalDays != Game1.Date.TotalDays)
        {
            state.LastFamiliarityGainTotalDays = Game1.Date.TotalDays;
            state.FamiliarityGainedToday = 0;
        }

        int remainingToday = System.Math.Max(0, dailyCap - state.FamiliarityGainedToday);
        int gained = System.Math.Min(amount, remainingToday);
        if (gained <= 0)
        {
            return;
        }

        state.Familiarity = LivingNpcState.ClampScore(state.Familiarity + gained);
        state.FamiliarityGainedToday += gained;
    }

    private BehaviorMemoryEntry CreateEntry(NPC npc, string kind, string action, string reason)
    {
        return new BehaviorMemoryEntry
        {
            NpcName = npc.Name,
            Kind = kind,
            Action = action,
            Reason = reason,
            Year = Game1.year,
            Season = Game1.season.ToString(),
            Day = Game1.dayOfMonth,
            TimeOfDay = Game1.timeOfDay,
            TotalDays = Game1.Date.TotalDays,
            LocationName = npc.currentLocation?.Name ?? string.Empty,
            LocationDisplayName = npc.currentLocation?.DisplayName ?? string.Empty
        };
    }

    private void AddEntry(BehaviorMemoryEntry entry, int maxEntriesPerNpc)
    {
        if (!this.entriesByNpc.TryGetValue(entry.NpcName, out var entries))
        {
            entries = new List<BehaviorMemoryEntry>();
            this.entriesByNpc[entry.NpcName] = entries;
        }

        entries.Add(entry);
        while (entries.Count > maxEntriesPerNpc)
        {
            entries.RemoveAt(0);
        }
    }

    public string BuildPromptContext(
        NPC npc,
        int maxEntries,
        bool includeState,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests)
    {
        this.entriesByNpc.TryGetValue(npc.Name, out var entries);
        var disposition = NpcDisposition.For(npc);
        var emotionalStyle = EmotionalExpressionStyle.For(npc);
        int safeMaxEntries = System.Math.Max(maxEntries, 0);
        var recentEntries = entries is { Count: > 0 }
            ? entries.TakeLast(safeMaxEntries).ToList()
            : new List<BehaviorMemoryEntry>();
        LivingNpcState? state = null;
        if (includeState)
        {
            this.statesByNpc.TryGetValue(npc.Name, out state);
        }
        var world = WorldContext.For(npc, state);

        MemoryRecallPlan recallPlan = state == null
            ? MemoryRecallPlan.Empty
            : this.BuildMemoryRecallPlan(state, world, recentEntries, longTermCount: 3, preferenceCount: 4);
        IReadOnlyList<CommunityImpressionSelection> communityImpressions = state == null
            ? System.Array.Empty<CommunityImpressionSelection>()
            : this.BuildCommunityImpressionRecallPlan(state, maxCount: 2);

        string prompt = BehaviorPromptContextBuilder.BuildPromptContext(
            npc,
            recentEntries,
            state,
            world,
            disposition,
            emotionalStyle,
            recallPlan,
            communityImpressions,
            maxPendingHelpRequestsPerNpc,
            helpRequestCooldownDays,
            minRelationshipTrustForHelpRequests,
            Game1.Date.TotalDays
        );
        this.MarkMemoriesRecalled(recallPlan);
        this.MarkCommunityImpressionsRecalled(communityImpressions);

        return prompt;
    }

    private static int GetMemoryAge(int totalDays)
    {
        return GetMemoryAge(totalDays, Game1.Date.TotalDays);
    }

    internal static int GetMemoryAge(int totalDays, int currentTotalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - totalDays);
    }

    private MemoryRecallPlan BuildMemoryRecallPlan(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        int longTermCount,
        int preferenceCount)
    {
        this.RefreshMemoryStores(state);
        return MemoryRecallService.BuildPlan(
            state,
            world,
            recentEntries,
            longTermCount,
            preferenceCount,
            Game1.Date.TotalDays
        );
    }

    internal MemoryRecallPlan BuildMemoryRecallPlanForTesting(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        int longTermCount,
        int preferenceCount,
        int currentTotalDays)
    {
        return MemoryRecallService.BuildPlan(
            state,
            world,
            recentEntries,
            longTermCount,
            preferenceCount,
            currentTotalDays
        );
    }

    private IReadOnlyList<CommunityImpressionSelection> BuildCommunityImpressionRecallPlan(
        LivingNpcState state,
        int maxCount)
    {
        this.RefreshMemoryStores(state);
        return MemoryRecallService.BuildCommunityImpressionPlan(state, maxCount, Game1.Date.TotalDays);
    }

    private void MarkMemoriesRecalled(MemoryRecallPlan recallPlan)
    {
        MemoryRecallService.MarkRecalled(recallPlan, Game1.Date.TotalDays, Game1.timeOfDay);
    }

    private void MarkCommunityImpressionsRecalled(IReadOnlyList<CommunityImpressionSelection> selections)
    {
        MemoryRecallService.MarkCommunityImpressionsRecalled(selections, Game1.Date.TotalDays, Game1.timeOfDay);
    }

    private void RefreshMemoryStores(LivingNpcState state)
    {
        LongTermMemoryStore.Refresh(state, Game1.Date.TotalDays);
        PlayerPreferenceMemoryStore.Refresh(state, Game1.Date.TotalDays);
        CommunityImpressionStore.Refresh(state, Game1.Date.TotalDays);
    }

    public string BuildDebugSummary(NPC npc, int maxEntries, bool includeState)
    {
        this.entriesByNpc.TryGetValue(npc.Name, out var entries);
        var disposition = NpcDisposition.For(npc);
        var emotionalStyle = EmotionalExpressionStyle.For(npc);
        IReadOnlyList<BehaviorMemoryEntry> allEntries = entries is { Count: > 0 }
            ? entries
            : System.Array.Empty<BehaviorMemoryEntry>();
        LivingNpcState? state = null;
        if (includeState)
        {
            this.statesByNpc.TryGetValue(npc.Name, out state);
        }
        var world = WorldContext.For(npc, state);

        MemoryRecallPlan recallPlan = MemoryRecallPlan.Empty;
        IReadOnlyList<CommunityImpressionSelection> communityImpressions = System.Array.Empty<CommunityImpressionSelection>();
        if (state != null)
        {
            recallPlan = this.BuildMemoryRecallPlan(
                state,
                world,
                allEntries.TakeLast(System.Math.Max(maxEntries, 0)).ToList(),
                longTermCount: 3,
                preferenceCount: 4
            );
            communityImpressions = this.BuildCommunityImpressionRecallPlan(state, maxCount: 2);
        }

        return BehaviorPromptContextBuilder.BuildDebugSummary(
            npc,
            allEntries,
            maxEntries,
            state,
            world,
            disposition,
            emotionalStyle,
            recallPlan,
            communityImpressions
        );
    }

    private LivingNpcState GetOrCreateState(NPC npc)
    {
        if (!this.statesByNpc.TryGetValue(npc.Name, out var state))
        {
            state = new LivingNpcState
            {
                NpcName = npc.Name,
                Mood = "Neutral",
                Attention = 35,
                Openness = 50,
                Familiarity = 0,
                FamiliarityGainedToday = 0,
                LastFamiliarityGainTotalDays = Game1.Date.TotalDays,
                ConversationsToday = 0,
                ConsecutiveConversationDays = 0,
                LastConversationTotalDays = -1,
                LastConversationTimeOfDay = 0,
                LastConversationGapDays = -1,
                InteractionRhythm = "New",
                InteractionComfortTier = "Distant",
                DailyConversationComfortLimit = 2,
                RepeatedConversationPressure = 0,
                LastFriendshipHearts = 0,
                LastGiftName = string.Empty,
                LastGiftTaste = string.Empty,
                LastGiftTotalDays = -1,
                LastGiftTimeOfDay = 0,
                GiftsToday = 0,
                LastEventContext = string.Empty,
                LastEventTotalDays = -1,
                LastEventTimeOfDay = 0,
                LongTermMemories = new List<LongTermMemoryFact>(),
                AiFriendshipGainedToday = 0,
                LastAiFriendshipTotalDays = -1,
                LastSceneContext = "none",
                LastSceneInfluence = "none",
                LastSceneInfluenceReason = "none",
                CurrentInclination = "Neutral",
                LastInteraction = "none yet",
                LastUpdatedTotalDays = Game1.Date.TotalDays,
                LastUpdatedTimeOfDay = Game1.timeOfDay
            };
            this.statesByNpc[npc.Name] = state;
        }

        state.NpcName = npc.Name;
        this.EnsureRelationshipTrustInitialized(state, npc);
        return state;
    }

    private void EnsureRelationshipTrustInitialized(LivingNpcState state, NPC npc)
    {
        if (state.RelationshipTrustInitialized)
        {
            return;
        }

        int hearts = WorldContext.For(npc).FriendshipHearts;
        state.RelationshipTrust = LivingNpcState.ClampScore(20 + (hearts * 6) + (state.Familiarity / 5));
        state.RelationshipTrustInitialized = true;
        state.LastRelationshipTrustUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastRelationshipTrustUpdatedTimeOfDay = Game1.timeOfDay;
    }

    internal static ValleyTalkExchangeAnalysis ParseExchangeAnalysis(string analysisJson)
    {
        return ValleyTalkExchangeParser.Parse(analysisJson);
    }

    private bool ApplyDialogueEmotionImpact(LivingNpcState state, ValleyTalkEmotionImpact impact)
    {
        return ConflictEmotionMemoryService.ApplyDialogueEmotionImpact(
            state,
            impact,
            Game1.Date.TotalDays,
            Game1.timeOfDay
        );
    }

    private void ApplyEmotion(LivingNpcState state, string emotion, int intensityDelta, string reason)
    {
        ConflictEmotionMemoryService.ApplyEmotion(
            state,
            emotion,
            intensityDelta,
            reason,
            Game1.Date.TotalDays,
            Game1.timeOfDay
        );
    }

    private void ApplyRelationshipTrustDelta(LivingNpcState state, int delta)
    {
        ConflictEmotionMemoryService.ApplyRelationshipTrustDelta(
            state,
            delta,
            Game1.Date.TotalDays,
            Game1.timeOfDay
        );
    }

    private bool StoreConflict(LivingNpcState state, ValleyTalkConflictCandidate candidate)
    {
        return ConflictEmotionMemoryService.StoreConflict(
            state,
            candidate,
            Game1.Date.TotalDays,
            Game1.timeOfDay
        );
    }

    private int ApplyConflictRepair(LivingNpcState state, int repairDelta, bool apology, bool specificRepairTalk)
    {
        return ConflictEmotionMemoryService.ApplyConflictRepair(
            state,
            repairDelta,
            apology,
            specificRepairTalk,
            Game1.Date.TotalDays,
            Game1.timeOfDay
        );
    }

    internal int ApplyConflictRepairForTesting(
        LivingNpcState state,
        int repairDelta,
        bool apology,
        bool specificRepairTalk,
        int currentTotalDays,
        int currentTimeOfDay = 1200)
    {
        return ConflictEmotionMemoryService.ApplyConflictRepair(
            state,
            repairDelta,
            apology,
            specificRepairTalk,
            currentTotalDays,
            currentTimeOfDay
        );
    }

    private void MarkRepairGiftReceived(LivingNpcState state, string giftName)
    {
        ConflictEmotionMemoryService.MarkRepairGiftReceived(state, giftName, Game1.Date.TotalDays, Game1.timeOfDay);
    }

    internal void MarkRepairGiftReceivedForTesting(
        LivingNpcState state,
        string giftName,
        int currentTotalDays,
        int currentTimeOfDay = 1200)
    {
        ConflictEmotionMemoryService.MarkRepairGiftReceived(state, giftName, currentTotalDays, currentTimeOfDay);
    }

    private void DecayEmotionAndConflicts(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        EmotionalExpressionCue emotionalStyle)
    {
        ConflictEmotionMemoryService.DecayEmotionAndConflicts(
            state,
            emotionDailyDecay,
            conflictDailyDecay,
            emotionalStyle,
            Game1.Date.TotalDays,
            Game1.timeOfDay
        );
    }

    internal void DecayEmotionAndConflictsForTesting(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        int currentTotalDays,
        int currentTimeOfDay = 1200)
    {
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        ConflictEmotionMemoryService.DecayEmotionAndConflicts(
            state,
            emotionDailyDecay,
            conflictDailyDecay,
            emotionalStyle,
            currentTotalDays,
            currentTimeOfDay
        );
    }

    private bool StorePlayerPreferenceMemory(LivingNpcState state, ValleyTalkMemoryCandidate candidate)
    {
        return PlayerPreferenceMemoryStore.Store(state, candidate, Game1.Date.TotalDays, Game1.timeOfDay);
    }

    private bool StoreDialogueBehaviorInfluence(
        NPC npc,
        LivingNpcState state,
        ValleyTalkBehaviorInfluenceCandidate candidate,
        int maxDialogueBehaviorInfluenceDays)
    {
        string fallbackLocation = npc.currentLocation?.Name ?? Game1.currentLocation?.Name ?? string.Empty;
        if (!DialogueBehaviorInfluenceStore.Store(
                state,
                candidate,
                fallbackLocation,
                maxDialogueBehaviorInfluenceDays,
                Game1.Date.TotalDays,
                Game1.timeOfDay,
                out var storedInfluence))
        {
            return false;
        }

        if (storedInfluence != null)
        {
            this.ApplyDialogueBehaviorStateEffect(state, storedInfluence);
        }

        return true;
    }

    private void ApplyDialogueBehaviorStateEffect(LivingNpcState state, DialogueBehaviorInfluenceFact influence)
    {
        switch (influence.Type)
        {
            case "companion_walk":
            case "stay_near":
                state.Mood = state.Mood == "Guarded" ? "Polite" : "Engaged";
                state.CurrentInclination = "OpenToTalk";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 8);
                state.Openness = LivingNpcState.ClampScore(state.Openness + 6);
                break;

            case "comforted":
                state.Mood = "Comfortable";
                state.CurrentInclination = "OpenToTalk";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 5);
                state.Openness = LivingNpcState.ClampScore(state.Openness + 8);
                this.ApplyRelationshipTrustDelta(state, 2);
                break;

            case "offended":
                state.Mood = "Guarded";
                state.CurrentInclination = "Reserved";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 3);
                state.Openness = LivingNpcState.ClampScore(state.Openness - 10);
                break;

            case "give_space":
                state.Mood = "Careful";
                state.CurrentInclination = "GentleBoundary";
                state.Openness = LivingNpcState.ClampScore(state.Openness - 6);
                break;

            case "visit_location":
                state.Mood = state.Mood == "Guarded" ? "Polite" : "Curious";
                state.CurrentInclination = "Aware";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 3);
                break;

            case "pause_to_talk":
                state.Mood = state.Mood == "Guarded" ? "Polite" : "Attentive";
                state.CurrentInclination = "Acknowledging";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 5);
                break;
        }

        state.LastInteraction = $"the latest conversation created a behavior tendency: {influence.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    internal static HelpRequestReadinessResult EvaluateHelpRequestReadiness(
        LivingNpcState state,
        int friendshipHearts,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests,
        int currentTotalDays)
    {
        return HelpRequestMemoryRules.EvaluateReadiness(
            state,
            friendshipHearts,
            maxPendingHelpRequestsPerNpc,
            helpRequestCooldownDays,
            minRelationshipTrustForHelpRequests,
            currentTotalDays
        );
    }

    private bool StoreLongTermMemory(LivingNpcState state, ValleyTalkMemoryCandidate candidate)
    {
        return this.StoreLongTermMemory(
            state,
            candidate,
            Game1.Date.TotalDays,
            Game1.timeOfDay,
            updateNicknameState: true
        );
    }

    private bool StoreLongTermMemory(
        LivingNpcState state,
        ValleyTalkMemoryCandidate candidate,
        int currentTotalDays,
        int currentTimeOfDay,
        bool updateNicknameState = false)
    {
        bool stored = LongTermMemoryStore.Store(
            state,
            candidate,
            currentTotalDays,
            currentTimeOfDay,
            out var storedMemory
        );
        if (stored && updateNicknameState)
        {
            this.TryUpdateNicknameStateFromMemory(state, storedMemory);
        }

        return stored;
    }

    private int ApplyAiDialogueFriendship(LivingNpcState state, int requestedDelta, int maxExtraFriendshipPerDay)
    {
        if (maxExtraFriendshipPerDay <= 0 || requestedDelta <= 0)
        {
            return 0;
        }

        if (state.LastAiFriendshipTotalDays != Game1.Date.TotalDays)
        {
            state.LastAiFriendshipTotalDays = Game1.Date.TotalDays;
            state.AiFriendshipGainedToday = 0;
        }

        int remaining = System.Math.Max(0, maxExtraFriendshipPerDay - state.AiFriendshipGainedToday);
        int applied = System.Math.Min(requestedDelta, remaining);
        state.AiFriendshipGainedToday += applied;

        if (applied > 0)
        {
            int familiarityGain = applied switch
            {
                >= 25 => 3,
                >= 16 => 2,
                >= 10 => 1,
                _ => 0
            };
            this.AddFamiliarity(state, familiarityGain, dailyCap: 8);
            state.Openness = LivingNpcState.ClampScore(state.Openness + System.Math.Min(6, applied / 5));
            this.ApplyRelationshipTrustDelta(state, applied switch
            {
                >= 25 => 5,
                >= 16 => 3,
                >= 10 => 2,
                _ => 1
            });
        }

        return applied;
    }

    private void TryUpdateNicknameStateFromMemory(LivingNpcState state, LongTermMemoryFact? memory)
    {
        if (memory == null || memory.Kind != "preference")
        {
            return;
        }

        var match = Regex.Match(
            memory.Summary,
            @"(?:called|称呼|叫)(?:\s+as)?\s*[“""']?(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,24})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return;
        }

        string nickname = CleanNickname(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        state.FarmerNickname = nickname;
        state.FarmerNicknameStatus = memory.Summary.Contains("did not accept", System.StringComparison.OrdinalIgnoreCase)
            || memory.Summary.Contains("未接受", System.StringComparison.OrdinalIgnoreCase)
            ? "Rejected"
            : "Accepted";
        state.FarmerNicknameTotalDays = Game1.Date.TotalDays;
        state.FarmerNicknameTimeOfDay = Game1.timeOfDay;
    }

    private static string NormalizeWorldActionType(string type)
    {
        return BehaviorValueNormalizer.NormalizeWorldActionType(type);
    }

    internal static string NormalizeEmotion(string emotion)
    {
        return BehaviorValueNormalizer.NormalizeEmotion(emotion);
    }

    internal static string NormalizeConflictCauseKind(string causeKind)
    {
        return BehaviorValueNormalizer.NormalizeConflictCauseKind(causeKind);
    }

    internal static string NormalizeHelpRequestType(string type)
    {
        return BehaviorValueNormalizer.NormalizeHelpRequestType(type);
    }

    internal static string NormalizeHelpRequestUpdateStatus(string status)
    {
        return BehaviorValueNormalizer.NormalizeHelpRequestUpdateStatus(status);
    }

    internal static string NormalizeHelpRequestFollowUpPotential(string value)
    {
        return BehaviorValueNormalizer.NormalizeHelpRequestFollowUpPotential(value);
    }

    internal static string NormalizeSharedExperienceType(string type)
    {
        return BehaviorValueNormalizer.NormalizeSharedExperienceType(type);
    }

    private static string NormalizeMemorySummary(string summary)
    {
        return BehaviorValueNormalizer.NormalizeMemorySummary(summary);
    }

    internal static string NormalizeTravelLocation(string value, string fallback)
    {
        return TravelLocationRules.Normalize(value, fallback);
    }

    private static string GetTravelLocationLabel(string locationName)
    {
        return TravelLocationRules.GetLabel(locationName);
    }

    internal static int HelpRequestStatusOrder(string status)
    {
        return status switch
        {
            "Offered" => 0,
            "Pending" => 1,
            "Expired" => 2,
            "Declined" => 3,
            "Fulfilled" => 4,
            _ => 5
        };
    }

    internal static int ConflictStatusOrder(string status)
    {
        return status switch
        {
            "Active" => 0,
            "Recovering" => 1,
            "Resolved" => 2,
            _ => 3
        };
    }

    private static bool TryExtractNicknameRequest(string playerText, out string nickname)
    {
        nickname = string.Empty;
        if (string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        var patterns = new[]
        {
            @"(?:以后|以后就|以后你可以|你可以|之后|以后请)?\s*(?:叫|喊|称呼)我(?:为|作|做)?\s*(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,12}?)(?=就|吧|好了|可以了|行了|，|。|,|\.|!|！|\?|？|$)",
            @"(?:call|name)\s+me\s+(?<name>[A-Za-z0-9_·•\-]{1,24})(?=\s|,|\.|!|\?|$)"
        };

        foreach (string pattern in patterns)
        {
            var match = Regex.Match(playerText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            nickname = CleanNickname(match.Groups["name"].Value);
            return !string.IsNullOrWhiteSpace(nickname);
        }

        return false;
    }

    private static string CleanNickname(string nickname)
    {
        return nickname
            .Trim()
            .Trim('“', '”', '"', '\'', '‘', '’', '，', ',', '。', '.', '！', '!', '？', '?', '：', ':');
    }

    private static string DetermineNicknameStatus(string nickname, string npcResponse)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return "Requested";
        }

        string response = npcResponse.ToLowerInvariant();
        bool rejected = ContainsAny(response, "不行", "不能", "不太", "不熟", "暂时", "抱歉", "对不起", "还是算了", "don't", "cannot", "can't", "won't");
        if (rejected)
        {
            return "Rejected";
        }

        bool accepted = response.Contains(nickname.ToLowerInvariant())
            || ContainsAny(response, "可以", "当然", "好啊", "好的", "没问题", "行", "愿意", "sure", "okay", "ok", "of course");

        return accepted ? "Accepted" : "Requested";
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(value.Contains);
    }

    private void RebuildDailyCounts()
    {
        foreach (var pair in this.entriesByNpc)
        {
            int count = pair.Value.Count(entry =>
                entry.TotalDays == Game1.Date.TotalDays
                && string.Equals(entry.Kind, "Behavior", System.StringComparison.OrdinalIgnoreCase)
            );
            if (count > 0)
            {
                this.dailyCountsByNpc[pair.Key] = count;
            }
        }
    }
}
