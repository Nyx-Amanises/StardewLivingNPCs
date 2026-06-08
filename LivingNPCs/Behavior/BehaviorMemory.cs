using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemory
{
    private static readonly HashSet<string> AllowedHelpRequestItemIds = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "(O)16",
        "(O)18",
        "(O)20",
        "(O)22",
        "(O)66",
        "(O)80",
        "(O)216",
        "(O)223",
        "(O)395",
        "(O)396",
        "(O)398",
        "(O)402",
        "(O)404",
        "(O)406",
        "(O)408",
        "(O)410",
        "(O)412",
        "(O)414",
        "(O)416",
        "(O)418"
    };

    private readonly Dictionary<string, List<BehaviorMemoryEntry>> entriesByNpc = new();
    private readonly Dictionary<string, int> dailyCountsByNpc = new();
    private readonly Dictionary<string, LivingNpcState> statesByNpc = new();
    private int lastStateDecayTotalDays = -1;

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
            if (this.StoreHelpRequest(
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
            if (this.ApplyHelpRequestUpdate(state, candidate, out NpcHelpRequestFact? fulfilledRequest))
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
        this.ApplyWorldStateInfluence(state, world);
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
        this.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer offered a gift";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public IReadOnlyList<NpcHelpRequestFact> TryCompleteItemHelpRequests(NPC npc, GiftMemoryDetails gift, int maxEntriesPerNpc)
    {
        var state = this.GetOrCreateState(npc);
        var fulfilled = state.HelpRequests
            .Where(request => request.Status == "Pending"
                && request.Type == "item_request"
                && string.Equals(request.RequestedItemId, gift.ItemId, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var request in fulfilled)
        {
            this.CompleteHelpRequestCurrentStep(
                state,
                request,
                $"The farmer brought {gift.ItemName}.",
                out bool fullyFulfilled
            );

            var entry = this.CreateEntry(
                npc,
                "HelpRequest",
                fullyFulfilled ? "Fulfilled" : "Advanced",
                request.Resolution
            );
            this.AddEntry(entry, maxEntriesPerNpc);
        }

        return fulfilled;
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
        this.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer interacted during an event";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateStateForExpiredHelpRequest(LivingNpcState state, NpcHelpRequestFact request)
    {
        bool acceptedThenMissed = request.AcceptedTotalDays >= 0;
        request.FailureReaction = this.BuildHelpRequestFailureReaction(state, request, acceptedThenMissed);
        state.Mood = acceptedThenMissed ? "Disappointed" : "Polite";
        state.CurrentInclination = acceptedThenMissed ? "Reserved" : "Measured";
        state.Openness = LivingNpcState.ClampScore(state.Openness - (acceptedThenMissed ? 8 : 3));
        if (acceptedThenMissed)
        {
            this.ApplyRelationshipTrustDelta(state, -5);
        }

        this.ApplyEmotion(
            state,
            "Disappointed",
            acceptedThenMissed ? 14 : 6,
            $"{request.FailureReaction}: {request.Summary}"
        );
        state.LastInteraction = acceptedThenMissed
            ? $"the farmer accepted but did not finish a personal help request: {request.Summary}"
            : $"a personal help request went unanswered: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    private void ApplyHelpRequestDeclineEffects(LivingNpcState state, NpcHelpRequestFact request)
    {
        bool wasAccepted = request.AcceptedTotalDays >= 0;
        state.Mood = wasAccepted ? "Disappointed" : "Polite";
        state.CurrentInclination = wasAccepted ? "Reserved" : "Measured";
        state.Openness = LivingNpcState.ClampScore(state.Openness - (wasAccepted ? 6 : 2));
        this.ApplyRelationshipTrustDelta(state, wasAccepted ? -4 : -1);
        this.ApplyEmotion(
            state,
            wasAccepted ? "Disappointed" : "Calm",
            wasAccepted ? 10 : 2,
            $"the farmer declined a personal help request: {request.Summary}"
        );
        state.LastInteraction = $"the farmer declined a personal help request: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private string BuildHelpRequestFailureReaction(LivingNpcState state, NpcHelpRequestFact request, bool acceptedThenMissed)
    {
        string name = state.NpcName.ToLowerInvariant();
        string baseline = acceptedThenMissed
            ? "they feel let down because the farmer had accepted the request"
            : "they let the unanswered request fade with mild disappointment";

        if (ContainsAny(name, "shane", "george", "haley", "zayne", "maive", "morris"))
        {
            return acceptedThenMissed
                ? "they respond bluntly and become more guarded"
                : "they brush it off, but a little sharply";
        }

        if (ContainsAny(name, "penny", "sophia", "flor", "shiro", "harvey", "sebastian", "claire"))
        {
            return acceptedThenMissed
                ? "they take it quietly and become more hesitant to ask again"
                : "they assume the farmer was busy and withdraw the request softly";
        }

        if (ContainsAny(name, "gus", "emily", "evelyn", "marnie", "robin", "lenny", "keahi", "pika"))
        {
            return acceptedThenMissed
                ? "they stay kind, but the missed help clearly matters"
                : "they remain warm and do not press the matter";
        }

        if (request.Type == "question_request")
        {
            return acceptedThenMissed
                ? "they are disappointed that the conversation never followed through"
                : "they move on from the unanswered question";
        }

        return baseline;
    }

    public LivingNpcState UpdateStateForConversationStart(NPC npc)
    {
        var state = this.GetOrCreateState(npc);
        var world = WorldContext.For(npc);
        this.UpdateConversationRhythm(state, world.FriendshipHearts);
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

        this.ApplyConversationRhythmInfluence(state);
        this.ApplyObservedConcern(state);
        this.ApplyWorldStateInfluence(state, world);
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

            this.RefreshConversationDay(state);
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

    private void RefreshConversationDay(LivingNpcState state)
    {
        int today = Game1.Date.TotalDays;
        if (state.LastConversationTotalDays == today)
        {
            return;
        }

        state.ConversationsToday = 0;
        if (state.LastConversationTotalDays >= 0)
        {
            state.LastConversationGapDays = System.Math.Max(1, today - state.LastConversationTotalDays);
            state.InteractionRhythm = state.LastConversationGapDays >= 7
                ? "LongQuietGap"
                : "NoConversationToday";
        }
        else
        {
            state.LastConversationGapDays = -1;
            state.InteractionRhythm = "New";
        }
    }

    private void UpdateConversationRhythm(LivingNpcState state, int friendshipHearts)
    {
        int today = Game1.Date.TotalDays;
        int previousConversationDay = state.LastConversationTotalDays;
        state.LastFriendshipHearts = friendshipHearts;

        if (previousConversationDay == today)
        {
            state.ConversationsToday = System.Math.Max(0, state.ConversationsToday) + 1;
            state.LastConversationGapDays = 0;
            if (state.ConsecutiveConversationDays <= 0)
            {
                state.ConsecutiveConversationDays = 1;
            }
        }
        else
        {
            state.LastConversationGapDays = previousConversationDay >= 0
                ? System.Math.Max(1, today - previousConversationDay)
                : -1;
            state.ConversationsToday = 1;
            state.ConsecutiveConversationDays = previousConversationDay == today - 1
                ? System.Math.Max(1, state.ConsecutiveConversationDays + 1)
                : 1;
        }

        state.LastConversationTotalDays = today;
        state.LastConversationTimeOfDay = Game1.timeOfDay;
        state.InteractionComfortTier = this.DetermineInteractionComfortTier(state);
        state.DailyConversationComfortLimit = this.DetermineDailyConversationComfortLimit(state.InteractionComfortTier);
        state.RepeatedConversationPressure = this.CalculateRepeatedConversationPressure(state);
        state.InteractionRhythm = this.DetermineInteractionRhythm(state);
    }

    private string DetermineInteractionRhythm(LivingNpcState state)
    {
        if (state.ConversationsToday > state.DailyConversationComfortLimit + 1)
        {
            return "CrowdedToday";
        }

        if (state.ConversationsToday > state.DailyConversationComfortLimit)
        {
            return "AtComfortLimit";
        }

        if (state.ConversationsToday >= 2)
        {
            return state.InteractionComfortTier switch
            {
                "Distant" => "PoliteRepeat",
                "Trusted" or "Intimate" => "ComfortableRepeat",
                _ => "CheckedInAgain"
            };
        }

        if (state.LastConversationGapDays >= 7)
        {
            return "AfterLongGap";
        }

        if (state.ConsecutiveConversationDays >= 5)
        {
            return "DailyRoutine";
        }

        if (state.ConsecutiveConversationDays >= 3)
        {
            return "BuildingRoutine";
        }

        return state.LastConversationGapDays < 0 ? "FirstConversation" : "FreshToday";
    }

    private string DetermineInteractionComfortTier(LivingNpcState state)
    {
        if (state.LastFriendshipHearts >= 10 || state.Familiarity >= 85)
        {
            return "Intimate";
        }

        if (state.LastFriendshipHearts >= 8 || state.Familiarity >= 70)
        {
            return "Trusted";
        }

        if (state.LastFriendshipHearts >= 4 || state.Familiarity >= 45)
        {
            return "Friendly";
        }

        if (state.LastFriendshipHearts >= 2 || state.Familiarity >= 18)
        {
            return "Familiar";
        }

        return "Distant";
    }

    private int DetermineDailyConversationComfortLimit(string comfortTier)
    {
        return comfortTier switch
        {
            "Intimate" => 6,
            "Trusted" => 5,
            "Friendly" => 4,
            "Familiar" => 3,
            _ => 2
        };
    }

    private int CalculateRepeatedConversationPressure(LivingNpcState state)
    {
        int overLimit = System.Math.Max(0, state.ConversationsToday - state.DailyConversationComfortLimit);
        int tierWeight = state.InteractionComfortTier switch
        {
            "Intimate" => 8,
            "Trusted" => 12,
            "Friendly" => 18,
            "Familiar" => 24,
            _ => 32
        };

        return System.Math.Clamp(overLimit * tierWeight, 0, 100);
    }

    private void ApplyConversationRhythmInfluence(LivingNpcState state)
    {
        switch (state.InteractionRhythm)
        {
            case "CrowdedToday":
                this.ApplyCrowdedConversationInfluence(state);
                break;

            case "AtComfortLimit":
                this.ApplyComfortLimitInfluence(state);
                break;

            case "PoliteRepeat":
                state.Mood = "Polite";
                state.CurrentInclination = "Measured";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 1);
                state.Openness = LivingNpcState.ClampScore(state.Openness - 5);
                break;

            case "ComfortableRepeat":
                state.Mood = "Comfortable";
                state.CurrentInclination = "OpenToTalk";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 4);
                state.Openness = LivingNpcState.ClampScore(state.Openness + 3);
                break;

            case "CheckedInAgain":
                state.Mood = state.Openness >= 65 || state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate"
                    ? "Familiar"
                    : "Aware";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 2);
                state.Openness = LivingNpcState.ClampScore(state.Openness + (state.InteractionComfortTier == "Friendly" ? 1 : -2));
                break;

            case "AfterLongGap":
                state.Mood = "Surprised";
                state.CurrentInclination = "Reconnecting";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 6);
                state.Openness = LivingNpcState.ClampScore(state.Openness - 2);
                break;

            case "DailyRoutine":
                state.Mood = "Comfortable";
                state.CurrentInclination = "OpenToTalk";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 4);
                state.Openness = LivingNpcState.ClampScore(state.Openness + 5);
                break;

            case "BuildingRoutine":
                state.Mood = "Familiar";
                state.Attention = LivingNpcState.ClampScore(state.Attention + 2);
                state.Openness = LivingNpcState.ClampScore(state.Openness + 2);
                break;
        }
    }

    private void ApplyObservedConcern(LivingNpcState state)
    {
        if (state.HasUnresolvedConflict && state.HighestUnresolvedConflictSeverity >= 45)
        {
            return;
        }

        bool canCareOpenly = state.RelationshipTrust >= 45 || state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate";
        if (canCareOpenly
            && state.LastConversationGapDays >= 7
            && !(state.CurrentEmotion == "Worried" && state.LastEmotionUpdatedTotalDays == Game1.Date.TotalDays))
        {
            this.ApplyEmotion(
                state,
                "Worried",
                System.Math.Min(20, 8 + state.LastConversationGapDays),
                $"the farmer had not appeared for {state.LastConversationGapDays} days"
            );
        }

        if (canCareOpenly
            && Game1.player.maxHealth > 0
            && Game1.player.health <= System.Math.Max(1, Game1.player.maxHealth / 3)
            && !(state.CurrentEmotion == "Worried" && state.LastEmotionUpdatedTotalDays == Game1.Date.TotalDays))
        {
            this.ApplyEmotion(
                state,
                "Worried",
                18,
                "the farmer looked badly hurt"
            );
        }
    }

    private void ApplyCrowdedConversationInfluence(LivingNpcState state)
    {
        int opennessPenalty = state.InteractionComfortTier switch
        {
            "Intimate" => 3,
            "Trusted" => 5,
            "Friendly" => 7,
            "Familiar" => 10,
            _ => 14
        };

        state.Mood = state.InteractionComfortTier is "Intimate" or "Trusted"
            ? "CrowdedButWarm"
            : "Overloaded";
        state.CurrentInclination = state.InteractionComfortTier is "Intimate" or "Trusted"
            ? "GentleBoundary"
            : "NeedsSpace";
        state.Attention = LivingNpcState.ClampScore(state.Attention + (state.InteractionComfortTier == "Distant" ? -3 : 1));
        state.Openness = LivingNpcState.ClampScore(state.Openness - opennessPenalty);
    }

    private void ApplyComfortLimitInfluence(LivingNpcState state)
    {
        if (state.InteractionComfortTier is "Intimate" or "Trusted")
        {
            state.Mood = "Comfortable";
            state.CurrentInclination = "OpenToTalk";
            state.Attention = LivingNpcState.ClampScore(state.Attention + 2);
            state.Openness = LivingNpcState.ClampScore(state.Openness + 1);
            return;
        }

        state.Mood = state.InteractionComfortTier == "Friendly" ? "Familiar" : "Polite";
        state.CurrentInclination = state.InteractionComfortTier == "Distant" ? "Measured" : "Acknowledging";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 1);
        state.Openness = LivingNpcState.ClampScore(state.Openness - (state.InteractionComfortTier == "Distant" ? 6 : 3));
    }

    private void ApplyWorldStateInfluence(LivingNpcState state, WorldContextSnapshot world)
    {
        state.LastSceneContext = world.DebugLabel;
        state.LastSceneInfluence = string.IsNullOrWhiteSpace(world.StateInfluence.DebugLabel)
            ? "none"
            : world.StateInfluence.DebugLabel;
        state.LastSceneInfluenceReason = string.IsNullOrWhiteSpace(world.StateInfluence.Reason)
            ? "none"
            : world.StateInfluence.Reason;

        if (!world.StateInfluence.HasMood)
        {
            return;
        }

        state.Attention = LivingNpcState.ClampScore(state.Attention + world.StateInfluence.AttentionDelta);
        state.Openness = LivingNpcState.ClampScore(state.Openness + world.StateInfluence.OpennessDelta);

        if (ShouldUseContextMood(state.Mood, world.StateInfluence.Priority))
        {
            state.Mood = world.StateInfluence.Mood;
        }

        if (ShouldUseContextInclination(state.CurrentInclination, world.StateInfluence.Priority))
        {
            state.CurrentInclination = world.StateInfluence.Inclination;
        }
    }

    private static bool ShouldUseContextMood(string currentMood, int contextPriority)
    {
        return contextPriority >= 70
            || currentMood is "Neutral" or "Aware" or "Attentive" or "Calm" or "Fresh" or "Quiet";
    }

    private static bool ShouldUseContextInclination(string currentInclination, int contextPriority)
    {
        return contextPriority >= 70
            || currentInclination is "Neutral" or "Aware" or "Acknowledging";
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
        if (!impact.HasEffect)
        {
            return false;
        }

        string reason = string.IsNullOrWhiteSpace(impact.Reason)
            ? "the latest conversation changed how they felt"
            : impact.Reason;
        if (impact.Emotion == "none")
        {
            if (impact.IntensityDelta < 0)
            {
                state.EmotionIntensity = LivingNpcState.ClampScore(state.EmotionIntensity + impact.IntensityDelta);
                if (state.EmotionIntensity == 0)
                {
                    state.CurrentEmotion = "Calm";
                }

                state.LastEmotionReason = reason;
                state.LastEmotionUpdatedTotalDays = Game1.Date.TotalDays;
                state.LastEmotionUpdatedTimeOfDay = Game1.timeOfDay;
                return true;
            }

            return false;
        }

        this.ApplyEmotion(state, impact.Emotion, impact.IntensityDelta, reason);
        return true;
    }

    private void ApplyEmotion(LivingNpcState state, string emotion, int intensityDelta, string reason)
    {
        string normalizedEmotion = NormalizeEmotion(emotion);
        if (normalizedEmotion == "none")
        {
            return;
        }

        int baseIntensity = state.CurrentEmotion == normalizedEmotion
            ? state.EmotionIntensity
            : normalizedEmotion == "Calm"
                ? 0
                : System.Math.Max(10, state.EmotionIntensity / 2);
        state.CurrentEmotion = normalizedEmotion == "none" ? "Calm" : normalizedEmotion;
        state.EmotionIntensity = LivingNpcState.ClampScore(baseIntensity + intensityDelta);
        if (state.EmotionIntensity == 0)
        {
            state.CurrentEmotion = "Calm";
        }

        state.LastEmotionReason = string.IsNullOrWhiteSpace(reason)
            ? "the latest interaction changed how they felt"
            : reason.Trim();
        state.LastEmotionUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastEmotionUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private void ApplyRelationshipTrustDelta(LivingNpcState state, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        state.RelationshipTrust = LivingNpcState.ClampScore(state.RelationshipTrust + delta);
        state.LastRelationshipTrustUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastRelationshipTrustUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private bool StoreConflict(LivingNpcState state, ValleyTalkConflictCandidate candidate)
    {
        string normalizedSummary = NormalizeMemorySummary(candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedSummary) || candidate.Severity <= 0)
        {
            return false;
        }

        var existing = state.Conflicts.FirstOrDefault(conflict =>
            conflict.Status != "Resolved"
            && NormalizeMemorySummary(conflict.Summary) == normalizedSummary);
        if (existing != null)
        {
            existing.CauseKind = candidate.CauseKind;
            existing.Severity = LivingNpcState.ClampScore(System.Math.Max(existing.Severity, candidate.Severity) + 8);
            existing.PeakSeverity = System.Math.Max(existing.PeakSeverity, existing.Severity);
            if (existing.PeakSeverity >= 60)
            {
                existing.RequiresComplexRepair = true;
                existing.MinimumRepairTotalDays = System.Math.Max(
                    existing.MinimumRepairTotalDays,
                    Game1.Date.TotalDays + ConflictRepairService.GetComplexRepairDelayDays(state, existing.PeakSeverity)
                );
                existing.SpecificRepairTalkReceived = false;
                if (candidate.Severity >= 30)
                {
                    existing.MeaningfulGiftReceived = false;
                }
            }

            ConflictRepairService.RefreshStage(existing, Game1.Date.TotalDays);
            existing.Status = ConflictRepairService.GetConflictStatus(existing.Severity);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            this.ApplyRelationshipTrustDelta(state, -System.Math.Max(2, ConflictRepairService.GetConflictTrustLoss(candidate.Severity) / 2));
            this.ApplyEmotionForConflict(state, existing);
            return true;
        }

        var conflict = new NpcConflictFact
        {
            CauseKind = candidate.CauseKind,
            Summary = candidate.Summary.Trim(),
            Severity = LivingNpcState.ClampScore(candidate.Severity),
            PeakSeverity = LivingNpcState.ClampScore(candidate.Severity),
            Status = ConflictRepairService.GetConflictStatus(candidate.Severity),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            RequiresComplexRepair = candidate.Severity >= 60,
            MinimumRepairTotalDays = candidate.Severity >= 60
                ? Game1.Date.TotalDays + ConflictRepairService.GetComplexRepairDelayDays(state, candidate.Severity)
                : -1,
            TimesReinforced = 1
        };
        ConflictRepairService.RefreshStage(conflict, Game1.Date.TotalDays);
        state.Conflicts.Add(conflict);
        state.Conflicts = state.Conflicts
            .OrderBy(conflictEntry => ConflictStatusOrder(conflictEntry.Status))
            .ThenByDescending(conflictEntry => conflictEntry.Severity)
            .ThenByDescending(conflictEntry => conflictEntry.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        this.ApplyRelationshipTrustDelta(state, -ConflictRepairService.GetConflictTrustLoss(conflict.Severity));
        this.ApplyEmotionForConflict(state, conflict);
        return true;
    }

    private int ApplyConflictRepair(LivingNpcState state, int repairDelta, bool apology, bool specificRepairTalk)
    {
        return this.ApplyConflictRepair(
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
        return this.ApplyConflictRepair(
            state,
            repairDelta,
            apology,
            specificRepairTalk,
            currentTotalDays,
            currentTimeOfDay
        );
    }

    private int ApplyConflictRepair(
        LivingNpcState state,
        int repairDelta,
        bool apology,
        bool specificRepairTalk,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        var update = ConflictRepairService.ApplyRepair(
            state,
            repairDelta,
            apology,
            specificRepairTalk,
            emotionalStyle,
            currentTotalDays,
            currentTimeOfDay
        );
        this.ApplyConflictRepairUpdate(state, update);

        if (update.ResolvedCount > 0)
        {
            this.ApplyEmotion(state, "Calm", -state.EmotionIntensity, "an earlier conflict has been repaired");
        }

        return update.ResolvedCount;
    }

    private void MarkRepairGiftReceived(LivingNpcState state, string giftName)
    {
        this.MarkRepairGiftReceived(state, giftName, Game1.Date.TotalDays, Game1.timeOfDay);
    }

    internal void MarkRepairGiftReceivedForTesting(
        LivingNpcState state,
        string giftName,
        int currentTotalDays,
        int currentTimeOfDay = 1200)
    {
        this.MarkRepairGiftReceived(state, giftName, currentTotalDays, currentTimeOfDay);
    }

    private void MarkRepairGiftReceived(
        LivingNpcState state,
        string giftName,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        ConflictRepairService.MarkRepairGiftReceived(state, giftName, currentTotalDays, currentTimeOfDay);
    }

    private void DecayEmotionAndConflicts(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        EmotionalExpressionCue emotionalStyle)
    {
        this.DecayEmotionAndConflicts(
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
        this.DecayEmotionAndConflicts(
            state,
            emotionDailyDecay,
            conflictDailyDecay,
            emotionalStyle,
            currentTotalDays,
            currentTimeOfDay
        );
    }

    private void DecayEmotionAndConflicts(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        EmotionalExpressionCue emotionalStyle,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        var update = ConflictRepairService.DecayEmotionAndConflicts(
            state,
            emotionDailyDecay,
            conflictDailyDecay,
            emotionalStyle,
            currentTotalDays,
            currentTimeOfDay
        );
        this.ApplyConflictRepairUpdate(state, update);
    }

    private void ApplyConflictRepairUpdate(LivingNpcState state, ConflictRepairUpdate update)
    {
        if (update.ComplexRepairGrowthAwards <= 0)
        {
            return;
        }

        this.ApplyRelationshipTrustDelta(state, update.ComplexRepairGrowthAwards * 8);
        state.Familiarity = LivingNpcState.ClampScore(state.Familiarity + (update.ComplexRepairGrowthAwards * 2));
    }

    private void ApplyEmotionForConflict(LivingNpcState state, NpcConflictFact conflict)
    {
        string emotion = conflict.CauseKind == "promise"
            ? "Disappointed"
            : conflict.Severity switch
        {
            >= 70 => "Angry",
            >= 35 => "Upset",
            _ => "Uneasy"
        };
        this.ApplyEmotion(state, emotion, conflict.Severity / 2, conflict.Summary);
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

    private bool StoreHelpRequest(
        NPC npc,
        LivingNpcState state,
        ValleyTalkHelpRequestCandidate candidate,
        string playerText,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests)
    {
        if (!HelpRequestMemoryRules.CanOpen(
                npc,
                state,
                maxPendingHelpRequestsPerNpc,
                helpRequestCooldownDays,
                minRelationshipTrustForHelpRequests,
                out _))
        {
            return false;
        }

        string normalizedSummary = NormalizeMemorySummary(candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedSummary))
        {
            return false;
        }

        var steps = this.BuildHelpRequestSteps(npc, candidate);
        if (steps.Count == 0)
        {
            return false;
        }

        var firstStep = steps[0];
        string normalizedType = firstStep.Type;
        var existing = state.HelpRequests.FirstOrDefault(request =>
            request.Status is "Offered" or "Pending"
            && NormalizeMemorySummary(request.Summary) == normalizedSummary);
        if (existing != null)
        {
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        bool requiresAcceptance = candidate.RequiresAcceptance && !IsFarmerExplicitlyOfferingHelp(playerText);

        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            NpcDisplayName = npc.displayName,
            QuestLogId = System.Guid.NewGuid().ToString("N"),
            Type = normalizedType,
            Summary = candidate.Summary.Trim(),
            Steps = steps,
            CurrentStepIndex = 0,
            RequestedItemId = firstStep.RequestedItemId,
            RequestedItemLabel = firstStep.RequestedItemLabel,
            QuestionTopic = firstStep.QuestionTopic,
            DueTotalDays = Game1.Date.TotalDays + candidate.DueInDays,
            Reason = candidate.Reason.Trim(),
            Status = requiresAcceptance ? "Offered" : "Pending",
            FollowUpPotential = NormalizeHelpRequestFollowUpPotential(candidate.FollowUpPotential),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            AcceptedTotalDays = requiresAcceptance ? -1 : Game1.Date.TotalDays,
            AcceptedTimeOfDay = requiresAcceptance ? 0 : Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            RewardFriendship = HelpRequestMemoryRules.DetermineFriendshipReward(
                npc,
                candidate,
                normalizedType
            ),
            RewardMoney = HelpRequestMemoryRules.DetermineMoneyReward(steps),
            TimesReinforced = 1
        });
        state.LastHelpRequestTotalDays = Game1.Date.TotalDays;
        state.LastHelpRequestTimeOfDay = Game1.timeOfDay;
        state.HelpRequests = state.HelpRequests
            .OrderBy(request => BehaviorMemory.HelpRequestStatusOrder(request.Status))
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        return true;
    }

    private static bool IsFarmerExplicitlyOfferingHelp(string playerText)
    {
        if (string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        string text = playerText.ToLowerInvariant();
        if (ContainsAny(text, "不需要帮", "不用帮", "不必帮", "don't need help", "do not need help"))
        {
            return false;
        }

        return ContainsAny(
            text,
            "需要帮忙",
            "需要帮",
            "有什么要帮",
            "有什么需要",
            "我能帮",
            "需要我",
            "帮得上",
            "可以帮",
            "要不要我帮",
            "anything i can help",
            "need help",
            "can i help",
            "help you",
            "what can i do"
        );
    }

    private List<NpcHelpRequestStepFact> BuildHelpRequestSteps(NPC npc, ValleyTalkHelpRequestCandidate candidate)
    {
        candidate.Steps ??= new List<ValleyTalkHelpRequestStepCandidate>();
        var rawSteps = candidate.Steps.Count > 0
            ? candidate.Steps
            : new List<ValleyTalkHelpRequestStepCandidate>
            {
                new()
                {
                    Type = candidate.Type,
                    Summary = candidate.Summary,
                    RequestedItemId = candidate.RequestedItemId,
                    RequestedItemLabel = candidate.RequestedItemLabel,
                    QuestionTopic = candidate.QuestionTopic
                }
            };

        var steps = new List<NpcHelpRequestStepFact>();
        foreach (var rawStep in rawSteps.Take(HelpRequestMemoryRules.GetMaxStepsForCurrentWorldStage()))
        {
            string type = NormalizeHelpRequestType(rawStep.Type);
            if (type == "item_request")
            {
                if (!AllowedHelpRequestItemIds.Contains(rawStep.RequestedItemId)
                    || !HelpRequestAdvisor.IsCurrentlyRequestableItem(rawStep.RequestedItemId, npc))
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            steps.Add(new NpcHelpRequestStepFact
            {
                Type = type,
                Summary = string.IsNullOrWhiteSpace(rawStep.Summary)
                    ? candidate.Summary.Trim()
                    : rawStep.Summary.Trim(),
                RequestedItemId = rawStep.RequestedItemId.Trim(),
                RequestedItemLabel = rawStep.RequestedItemLabel.Trim(),
                QuestionTopic = rawStep.QuestionTopic.Trim(),
                Status = "Pending"
            });
        }

        return steps;
    }

    private bool ApplyHelpRequestUpdate(LivingNpcState state, ValleyTalkHelpRequestUpdateCandidate candidate, out NpcHelpRequestFact? fulfilledRequest)
    {
        fulfilledRequest = null;
        string normalizedSummary = NormalizeMemorySummary(candidate.Summary);
        var existing = state.HelpRequests
            .Where(request => request.Status is "Offered" or "Pending")
            .OrderBy(request => request.DueTotalDays)
            .FirstOrDefault(request => NormalizeMemorySummary(request.Summary) == normalizedSummary);
        if (existing == null)
        {
            return false;
        }

        switch (candidate.Status)
        {
            case "accepted":
                this.AcceptHelpRequest(state, existing, candidate.Resolution);
                break;

            case "fulfilled":
            case "advanced":
                if (existing.Status == "Offered")
                {
                    this.AcceptHelpRequest(state, existing, "The farmer accepted and helped right away.");
                }

                if (this.CompleteHelpRequestCurrentStep(state, existing, candidate.Resolution, out bool fullyFulfilled)
                    && fullyFulfilled)
                {
                    fulfilledRequest = existing;
                }

                break;

            case "declined":
                this.DeclineHelpRequest(state, existing, candidate.Resolution);
                break;
        }

        return true;
    }

    private void AcceptHelpRequest(LivingNpcState state, NpcHelpRequestFact request, string resolution)
    {
        if (request.Status == "Pending")
        {
            return;
        }

        request.Status = "Pending";
        request.AcceptedTotalDays = Game1.Date.TotalDays;
        request.AcceptedTimeOfDay = Game1.timeOfDay;
        request.Resolution = string.IsNullOrWhiteSpace(resolution)
            ? "The farmer accepted the request."
            : resolution.Trim();
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
        state.Openness = LivingNpcState.ClampScore(state.Openness + 2);
        this.ApplyRelationshipTrustDelta(state, 2);
        state.LastInteraction = $"the farmer accepted a personal help request: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private void DeclineHelpRequest(LivingNpcState state, NpcHelpRequestFact request, string resolution)
    {
        request.Status = "Declined";
        request.Resolution = string.IsNullOrWhiteSpace(resolution)
            ? "The farmer declined the request."
            : resolution.Trim();
        request.DeclinedTotalDays = Game1.Date.TotalDays;
        request.DeclinedTimeOfDay = Game1.timeOfDay;
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
        request.FailureReaction = this.BuildHelpRequestFailureReaction(state, request, acceptedThenMissed: false);
        this.ApplyHelpRequestDeclineEffects(state, request);
    }

    private bool CompleteHelpRequestCurrentStep(
        LivingNpcState state,
        NpcHelpRequestFact request,
        string resolution,
        out bool fullyFulfilled)
    {
        fullyFulfilled = false;
        if (request.Status != "Pending")
        {
            return false;
        }

        var step = this.GetCurrentHelpRequestStep(request);
        if (step != null)
        {
            step.Status = "Fulfilled";
            step.Resolution = string.IsNullOrWhiteSpace(resolution)
                ? "The farmer completed this part of the request."
                : resolution.Trim();
            step.CompletedTotalDays = Game1.Date.TotalDays;
            step.CompletedTimeOfDay = Game1.timeOfDay;
        }

        if (request.CurrentStepIndex + 1 < request.Steps.Count)
        {
            request.CurrentStepIndex += 1;
            this.ApplyCurrentHelpRequestStep(request);
            request.Resolution = string.IsNullOrWhiteSpace(resolution)
                ? $"The farmer completed step {request.CurrentStepIndex}."
                : resolution.Trim();
            request.LastUpdatedTotalDays = Game1.Date.TotalDays;
            request.LastUpdatedTimeOfDay = Game1.timeOfDay;
            request.TimesReinforced += 1;
            state.Mood = "Encouraged";
            state.CurrentInclination = "OpenToTalk";
            state.Attention = LivingNpcState.ClampScore(state.Attention + 4);
            state.Openness = LivingNpcState.ClampScore(state.Openness + 3);
            this.ApplyRelationshipTrustDelta(state, 2);
            state.LastInteraction = $"the farmer completed part of a personal help request: {request.Summary}";
            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
            return true;
        }

        request.Status = "Fulfilled";
        request.Resolution = string.IsNullOrWhiteSpace(resolution)
            ? "The farmer completed the request."
            : resolution.Trim();
        request.FulfilledTotalDays = Game1.Date.TotalDays;
        request.FulfilledTimeOfDay = Game1.timeOfDay;
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
        this.ApplyHelpRequestFulfillmentEffects(state, request);
        fullyFulfilled = true;
        return true;
    }

    private NpcHelpRequestStepFact? GetCurrentHelpRequestStep(NpcHelpRequestFact request)
    {
        if (request.Steps.Count == 0)
        {
            return null;
        }

        return request.Steps[System.Math.Clamp(request.CurrentStepIndex, 0, request.Steps.Count - 1)];
    }

    private void ApplyCurrentHelpRequestStep(NpcHelpRequestFact request)
    {
        var step = this.GetCurrentHelpRequestStep(request);
        if (step == null)
        {
            return;
        }

        request.Type = step.Type;
        request.RequestedItemId = step.RequestedItemId;
        request.RequestedItemLabel = step.RequestedItemLabel;
        request.QuestionTopic = step.QuestionTopic;
    }

    private void ApplyHelpRequestFulfillmentEffects(LivingNpcState state, NpcHelpRequestFact request)
    {
        state.Mood = "Pleased";
        state.CurrentInclination = "OpenToTalk";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 8);
        state.Openness = LivingNpcState.ClampScore(state.Openness + 6);
        this.AddFamiliarity(state, amount: 2, dailyCap: 8);
        this.ApplyRelationshipTrustDelta(state, 6);
        this.ApplyEmotion(
            state,
            "Grateful",
            16,
            $"the farmer helped with a personal request: {request.Summary}"
        );
        request.SpecialFollowUpPlanned = HelpRequestMemoryRules.ShouldPlanFollowUp(state, request);
        request.FollowUpEligibleTotalDays = request.SpecialFollowUpPlanned
            ? Game1.Date.TotalDays + 1
            : -1;
        this.StoreHelpRequestSharedExperience(state, request);
        state.LastInteraction = $"the farmer helped with a personal request: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private void StoreHelpRequestSharedExperience(LivingNpcState state, NpcHelpRequestFact request)
    {
        string summary = $"the farmer helped with a personal request: {request.Summary}";
        if (request.FollowUpPotential != "none")
        {
            summary += $"; this could naturally grow into {request.FollowUpPotential.Replace('_', ' ')} if the next conversation supports it";
        }

        string key = $"help_request:{NormalizeMemorySummary(summary)}";
        var existing = state.SharedExperiences.FirstOrDefault(experience => experience.Key == key);
        if (existing != null)
        {
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            existing.Importance = System.Math.Min(100, existing.Importance + 5);
            return;
        }

        state.SharedExperiences.Add(new SharedExperienceFact
        {
            Key = key,
            Type = "help_request",
            Summary = summary,
            LocationName = string.Empty,
            LocationLabel = "a personal favor",
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            Importance = request.FollowUpPotential == "none" ? 70 : 82,
            TimesReinforced = 1,
            FollowUpEligibleTotalDays = Game1.Date.TotalDays + 2
        });

        state.SharedExperiences = state.SharedExperiences
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(12)
            .ToList();
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
