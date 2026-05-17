using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemory
{
    internal const int MaxLongTermMemoriesPerNpc = 24;
    internal const int MaxPlayerPreferenceMemoriesPerNpc = 24;
    internal const int MaxDialogueBehaviorInfluencesPerNpc = 12;
    internal const int MaxCommunityImpressionsPerNpc = 16;

    private static readonly HashSet<string> AllowedPlayerPreferenceTags = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "food",
        "drink",
        "flower",
        "mineral",
        "forage",
        "nature",
        "sweet",
        "comfort",
        "practical",
        "scholarly",
        "adventurous",
        "magical",
        "artistic",
        "refined",
        "work",
        "active",
        "fishing",
        "mining",
        "farming",
        "morning",
        "night"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> MemoryKeywordTags =
        new Dictionary<string, IReadOnlyCollection<string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["farm"] = ["farming", "work", "nature"],
            ["农场"] = ["farming", "work", "nature"],
            ["crop"] = ["farming", "work"],
            ["作物"] = ["farming", "work"],
            ["mine"] = ["mining", "adventurous", "mineral"],
            ["mines"] = ["mining", "adventurous", "mineral"],
            ["矿"] = ["mining", "adventurous", "mineral"],
            ["fish"] = ["fishing", "nature"],
            ["fishing"] = ["fishing", "nature"],
            ["钓鱼"] = ["fishing", "nature"],
            ["beach"] = ["fishing", "nature"],
            ["海边"] = ["fishing", "nature"],
            ["海滩"] = ["fishing", "nature"],
            ["flower"] = ["flower", "nature", "artistic"],
            ["花"] = ["flower", "nature", "artistic"],
            ["food"] = ["food", "comfort"],
            ["吃"] = ["food", "comfort"],
            ["料理"] = ["food", "comfort"],
            ["coffee"] = ["drink", "comfort", "work"],
            ["咖啡"] = ["drink", "comfort", "work"],
            ["book"] = ["scholarly"],
            ["library"] = ["scholarly"],
            ["书"] = ["scholarly"],
            ["图书馆"] = ["scholarly"],
            ["magic"] = ["magical"],
            ["魔法"] = ["magical"],
            ["art"] = ["artistic"],
            ["画"] = ["artistic"],
            ["艺术"] = ["artistic"],
            ["morning"] = ["morning"],
            ["早"] = ["morning"],
            ["night"] = ["night"],
            ["晚"] = ["night"]
        };

    private static readonly Dictionary<string, string> CommitmentLocationAliases = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Farm"] = "Farm",
        ["农场"] = "Farm",
        ["Town"] = "Town",
        ["Pelican Town"] = "Town",
        ["鹈鹕镇"] = "Town",
        ["Mountain"] = "Mountain",
        ["山地"] = "Mountain",
        ["山上"] = "Mountain",
        ["Beach"] = "Beach",
        ["海滩"] = "Beach",
        ["Forest"] = "Forest",
        ["Cindersap Forest"] = "Forest",
        ["森林"] = "Forest",
        ["煤矿森林"] = "Forest",
        ["BusStop"] = "BusStop",
        ["Bus Stop"] = "BusStop",
        ["巴士站"] = "BusStop",
        ["Saloon"] = "Saloon",
        ["Stardrop Saloon"] = "Saloon",
        ["酒吧"] = "Saloon",
        ["星之果实酒吧"] = "Saloon",
        ["SeedShop"] = "SeedShop",
        ["Pierre's"] = "SeedShop",
        ["Pierre's General Store"] = "SeedShop",
        ["杂货店"] = "SeedShop",
        ["皮埃尔的杂货店"] = "SeedShop",
        ["ArchaeologyHouse"] = "ArchaeologyHouse",
        ["Museum"] = "ArchaeologyHouse",
        ["Library"] = "ArchaeologyHouse",
        ["博物馆"] = "ArchaeologyHouse",
        ["图书馆"] = "ArchaeologyHouse",
        ["Hospital"] = "Hospital",
        ["Clinic"] = "Hospital",
        ["诊所"] = "Hospital",
        ["医院"] = "Hospital"
    };

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
        return this.statesByNpc.Values;
    }

    public IReadOnlyList<CommunityImpressionFact> GetRetellableCommunityImpressions(LivingNpcState state, int maxCount)
    {
        this.RefreshMemoryStores(state);
        return state.CommunityImpressions
            .Where(memory => memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= Game1.Date.TotalDays)
            .Where(memory => memory.FreshnessStage is "fresh" or "settled")
            .Where(memory => memory.Confidence >= 35)
            .OrderByDescending(GetCommunityImpressionRetentionScore)
            .ThenBy(memory => memory.LastSharedTotalDays < 0 ? -1 : memory.LastSharedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .Take(System.Math.Max(0, maxCount))
            .ToList();
    }

    public void MarkCommunityImpressionShared(CommunityImpressionFact memory)
    {
        memory.LastSharedTotalDays = Game1.Date.TotalDays;
        memory.LastSharedTimeOfDay = Game1.timeOfDay;
        memory.ShareCount += 1;
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
        string normalizedKind = NormalizeCommunityImpressionKind(kind);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string normalizedSource = NormalizeCommunityImpressionSource(source);
        string normalizedVisibility = NormalizeCommunityImpressionVisibility(visibility);
        int confidence = normalizedSource switch
        {
            "Witnessed" => 95,
            "CloseCircle" => 68,
            _ => 42
        };
        int normalizedDepth = System.Math.Clamp(transmissionDepth, 0, 8);
        int normalizedDistortion = System.Math.Clamp(distortionLevel, 0, 100);
        var existing = state.CommunityImpressions.FirstOrDefault(memory =>
            string.Equals(memory.SubjectNpcName, subject.Name, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(memory.Kind, normalizedKind, System.StringComparison.OrdinalIgnoreCase)
            && NormalizeMemorySummary(memory.Summary) == normalizedSummary);

        if (existing != null)
        {
            existing.SubjectDisplayName = subject.displayName;
            existing.Source = normalizedSource switch
            {
                "Witnessed" => "Witnessed",
                "CloseCircle" when existing.Source == "PublicRumor" => "CloseCircle",
                _ => existing.Source
            };
            existing.Visibility = NormalizeCommunityImpressionVisibility(existing.Visibility);
            existing.Visibility = GetMoreRestrictiveCommunityVisibility(existing.Visibility, normalizedVisibility);
            existing.Confidence = System.Math.Max(existing.Confidence, confidence);
            existing.TransmissionDepth = System.Math.Min(existing.TransmissionDepth, normalizedDepth);
            existing.DistortionLevel = System.Math.Min(existing.DistortionLevel, normalizedDistortion);
            existing.HeardFromNpcName = string.IsNullOrWhiteSpace(existing.HeardFromNpcName)
                ? heardFromNpcName?.Trim() ?? string.Empty
                : existing.HeardFromNpcName;
            existing.CircleKey = string.IsNullOrWhiteSpace(existing.CircleKey)
                ? circleKey?.Trim() ?? string.Empty
                : existing.CircleKey;
            existing.Importance = System.Math.Min(100, System.Math.Max(existing.Importance, importance) + 3);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        state.CommunityImpressions.Add(new CommunityImpressionFact
        {
            SubjectNpcName = subject.Name,
            SubjectDisplayName = subject.displayName,
            Kind = normalizedKind,
            Summary = summary.Trim(),
            Source = normalizedSource,
            Visibility = normalizedVisibility,
            Confidence = confidence,
            TransmissionDepth = normalizedDepth,
            DistortionLevel = normalizedDistortion,
            HeardFromNpcName = heardFromNpcName?.Trim() ?? string.Empty,
            CircleKey = circleKey?.Trim() ?? string.Empty,
            Importance = System.Math.Clamp(importance, 0, 100),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            ExpiresTotalDays = DetermineCommunityImpressionExpiry(
                normalizedSource,
                normalizedVisibility,
                normalizedDepth,
                Game1.Date.TotalDays
            ),
            TimesReinforced = 1
        });
        state.CommunityImpressions = state.CommunityImpressions
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxCommunityImpressionsPerNpc)
            .ToList();

        var entry = this.CreateEntry(observer, "SocialMemory", normalizedKind, summary);
        this.AddEntry(entry, maxEntriesPerNpc);
        return true;
    }

    public ValleyTalkExchangeResult RecordValleyTalkExchange(
        NPC npc,
        string playerText,
        string npcResponse,
        string analysisJson,
        int maxEntriesPerNpc,
        int maxPendingCommitmentsPerNpc,
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
        int storedCommitments = 0;
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

        foreach (var candidate in analysis.Commitments
                     .Where(commitment => maxPendingCommitmentsPerNpc > 0 && !string.IsNullOrWhiteSpace(commitment.Summary))
                     .Take(2))
        {
            if (this.StoreCommitment(npc, state, candidate, maxPendingCommitmentsPerNpc))
            {
                storedCommitments++;
                var entry = this.CreateEntry(
                    npc,
                    "Commitment",
                    candidate.Type,
                    candidate.Summary
                );
                this.AddEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.HelpRequests
                     .Where(request => maxPendingHelpRequestsPerNpc > 0 && !string.IsNullOrWhiteSpace(request.Summary))
                     .Take(1))
        {
            if (this.StoreHelpRequest(
                    npc,
                    state,
                    candidate,
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
            && storedCommitments == 0
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
            || storedCommitments > 0
            || storedHelpRequests > 0
            || updatedHelpRequests > 0
            || storedConflicts > 0
            || storedBehaviorInfluences > 0
            || resolvedConflicts > 0
            || emotionChanged
            || appliedFriendship > 0)
        {
            if (storedMemories > 0 || storedPlayerPreferences > 0 || storedCommitments > 0 || storedHelpRequests > 0)
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
            storedCommitments,
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

    public LivingNpcState UpdateStateForFulfilledCommitment(NPC npc, NpcCommitmentFact commitment)
    {
        var state = this.GetOrCreateState(npc);
        var world = WorldContext.For(npc);
        state.Mood = state.InteractionComfortTier is "Trusted" or "Intimate"
            ? "Comfortable"
            : "Pleased";
        state.CurrentInclination = "OpenToTalk";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 10);
        state.Openness = LivingNpcState.ClampScore(state.Openness + 8);
        this.AddFamiliarity(state, amount: 2, dailyCap: 8);
        state.CommitmentTrust = LivingNpcState.ClampScore(state.CommitmentTrust + GetFulfilledCommitmentTrustGain(commitment.Type));
        this.ApplyRelationshipTrustDelta(state, commitment.Type == "help_task" ? 6 : 4);
        state.ConsecutiveMissedCommitments = 0;
        this.StoreSharedExperience(state, commitment);
        if (commitment.Type == "help_task")
        {
            this.ApplyEmotion(state, "Grateful", 18, $"the farmer helped keep a meaningful plan: {commitment.Summary}");
        }
        this.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = $"they fulfilled a plan with the farmer: {commitment.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public LivingNpcState UpdateStateForExpiredCommitment(LivingNpcState state, NpcCommitmentFact commitment)
    {
        int trustLoss = GetExpiredCommitmentTrustLoss(state.ConsecutiveMissedCommitments);
        state.CommitmentTrust = LivingNpcState.ClampScore(state.CommitmentTrust - trustLoss);
        this.ApplyRelationshipTrustDelta(state, -System.Math.Max(4, trustLoss / 2));
        state.MissedCommitments += 1;
        state.ConsecutiveMissedCommitments += 1;
        state.LastMissedCommitmentTotalDays = Game1.Date.TotalDays;
        state.LastMissedCommitmentTimeOfDay = Game1.timeOfDay;
        state.Mood = state.ConsecutiveMissedCommitments >= 2 ? "Guarded" : "Polite";
        state.CurrentInclination = state.ConsecutiveMissedCommitments >= 2 ? "Reserved" : "Measured";
        state.Openness = LivingNpcState.ClampScore(state.Openness - System.Math.Min(16, 4 + trustLoss));
        this.ApplyEmotion(
            state,
            "Disappointed",
            state.ConsecutiveMissedCommitments >= 2 ? 18 : 10,
            $"the farmer missed an agreed plan: {commitment.Summary}"
        );
        this.StoreConflict(state, new ValleyTalkConflictCandidate
        {
            CauseKind = "promise",
            Summary = $"The farmer missed an agreed plan: {commitment.Summary}.",
            Severity = state.ConsecutiveMissedCommitments >= 2 ? 30 : 16
        });
        state.LastInteraction = $"the farmer missed a plan: {commitment.Summary}";
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
            state.CommitmentTrust = LivingNpcState.ClampScore(state.CommitmentTrust - 4);
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
                this.DecayEmotionAndConflicts(state, emotionDailyDecay, conflictDailyDecay);
                continue;
            }

            state.Attention = LivingNpcState.MoveToward(state.Attention, 35, dailyDecay);
            state.Openness = LivingNpcState.MoveToward(state.Openness, 50, dailyDecay / 2);
            state.CurrentInclination = state.Attention >= 55 ? "Aware" : "Neutral";
            state.Mood = state.Openness >= 58 ? "Calm" : "Neutral";
            state.LastInteraction = "time passed";
            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
            this.DecayEmotionAndConflicts(state, emotionDailyDecay, conflictDailyDecay);
        }
    }

    private void FadeCommunityImpressions(LivingNpcState state)
    {
        foreach (var memory in state.CommunityImpressions)
        {
            int age = GetMemoryAge(memory.LastUpdatedTotalDays);
            if (age <= 0)
            {
                continue;
            }

            int sourceDecay = memory.Source switch
            {
                "Witnessed" => 1,
                "CloseCircle" => 3,
                _ => 5
            };
            int distortionDecay = memory.TransmissionDepth + (memory.DistortionLevel / 25);
            int decay = System.Math.Max(1, sourceDecay + distortionDecay);
            memory.Confidence = System.Math.Max(0, memory.Confidence - decay);
            memory.Importance = System.Math.Max(0, memory.Importance - System.Math.Max(1, decay / 2));
        }

        state.CommunityImpressions = state.CommunityImpressions
            .Where(memory => memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= Game1.Date.TotalDays)
            .Where(memory => memory.Confidence >= 18)
            .OrderByDescending(GetCommunityImpressionRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxCommunityImpressionsPerNpc)
            .ToList();
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
        var world = WorldContext.For(npc);
        int safeMaxEntries = System.Math.Max(maxEntries, 0);
        var recentEntries = entries is { Count: > 0 }
            ? entries.TakeLast(safeMaxEntries).ToList()
            : new List<BehaviorMemoryEntry>();
        LivingNpcState? state = null;
        if (includeState)
        {
            this.statesByNpc.TryGetValue(npc.Name, out state);
        }

        MemoryRecallPlan recallPlan = state == null
            ? MemoryRecallPlan.Empty
            : this.BuildMemoryRecallPlan(state, world, recentEntries, longTermCount: 3, preferenceCount: 4);
        IReadOnlyList<CommunityImpressionSelection> communityImpressions = state == null
            ? System.Array.Empty<CommunityImpressionSelection>()
            : this.BuildCommunityImpressionRecallPlan(state, maxCount: 2);

        var prompt = new StringBuilder();
        prompt.AppendLine($"## LivingNPCs Context: {npc.displayName}");
        prompt.AppendLine("Purpose: hidden continuity for ValleyTalk's next in-character reply.");
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Treat this as current body language, relationship memory, and scene pressure, not text to quote.");
        prompt.AppendLine("- Stay in character; do not mention LivingNPCs, mods, prompts, AI, JSON, or context notes.");
        prompt.AppendLine("- Use at most one or two relevant details naturally. If none fit, let this only shape tone and pacing.");
        prompt.AppendLine("- The next reply should be a normal Stardew Valley NPC line, not a status report.");

        prompt.AppendLine();
        prompt.AppendLine("Conversation stance:");
        prompt.AppendLine($"- {this.BuildConversationStance(npc, state, disposition, world)}");

        prompt.AppendLine();
        prompt.AppendLine("Current state:");
        prompt.AppendLine($"- Character profile source: {disposition.SourceLabel}.");
        prompt.AppendLine($"- Disposition: {disposition.PromptLabel}.");
        if (disposition.HasProfileContext)
        {
            if (!string.IsNullOrWhiteSpace(disposition.BackgroundPrompt))
            {
                prompt.AppendLine($"- Character background: {disposition.BackgroundPrompt}");
            }

            if (!string.IsNullOrWhiteSpace(disposition.DialoguePrompt))
            {
                prompt.AppendLine($"- Character dialogue cue: {disposition.DialoguePrompt}");
            }
        }

        prompt.AppendLine($"- Scene: {world.PromptLabel}; location: {world.LocationDisplayName}; date: {world.Season} {world.DayOfMonth}; time: {world.TimeOfDay}.");
        prompt.AppendLine($"- World progression: {world.Progression.PromptLabel}.");
        if (state != null)
        {
            prompt.AppendLine($"- Mood: {state.Mood}; attention to farmer: {state.Attention}/100; response inclination: {state.CurrentInclination}.");
            prompt.AppendLine($"- Interpersonal emotion: {state.EmotionPromptLabel}.");
            prompt.AppendLine($"- Long-term familiarity with the farmer: {state.Familiarity}/100 ({state.FamiliarityPromptLabel}).");
            prompt.AppendLine($"- Relationship trust in the farmer: {state.RelationshipTrustPromptLabel}.");
            prompt.AppendLine($"- Secret-sharing depth: {state.SecretSharingPromptLabel}.");
            prompt.AppendLine($"- Relationship-aware interaction rhythm: {state.InteractionRhythmPromptLabel}; comfort tier: {state.InteractionComfortTierPromptLabel}.");
            prompt.AppendLine($"- Recent gift context: {state.LastGiftPromptLabel}.");
            prompt.AppendLine($"- Recent event context: {state.LastEventPromptLabel}.");
            prompt.AppendLine($"- Durable memory store: {state.LongTermMemories.Count} long-term memories tracked; recall focus for this reply: {this.FormatLongTermMemoryPromptLabel(recallPlan.LongTermMemories)}.");
            prompt.AppendLine($"- Known farmer preferences: {this.FormatPlayerPreferencePromptLabel(recallPlan.PlayerPreferences)}.");
            prompt.AppendLine($"- Conversation-driven behavior tendencies: {state.DialogueBehaviorInfluencePromptLabel}.");
            prompt.AppendLine($"- Commitments with the farmer: {state.CommitmentPromptLabel}.");
            prompt.AppendLine($"- Shared experiences from fulfilled plans: {state.SharedExperiencePromptLabel}.");
            prompt.AppendLine($"- Trust in keeping plans: {state.CommitmentTrustPromptLabel}.");
            prompt.AppendLine($"- Help requests involving the farmer: {state.HelpRequestPromptLabel}.");
            prompt.AppendLine($"- Community impressions about the farmer's ties with other NPCs: {this.FormatCommunityImpressionPromptLabel(npc, communityImpressions)}.");
            prompt.AppendLine($"- Stable community circles this NPC belongs to: {this.FormatSocialCirclePromptLabel(npc)}.");
            prompt.AppendLine("- Help-request lifecycle: Offered means the NPC has asked but the farmer has not accepted; Pending means accepted and active; only Pending requests should be treated like tasks.");
            prompt.AppendLine($"- Help-request readiness: {this.BuildHelpRequestReadinessLabel(npc, state, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, minRelationshipTrustForHelpRequests)}.");
            prompt.AppendLine($"- Help-request fit: {HelpRequestAdvisor.BuildPromptLabel(npc)}");
            prompt.AppendLine($"- Conflict memory: {state.ConflictPromptLabel}.");
            prompt.AppendLine($"- Personal memory context: {state.FarmerNicknamePromptLabel}.");
            prompt.AppendLine($"- Scene influence on mood: {state.LastSceneInfluenceReason}.");
            prompt.AppendLine($"- Last interaction: {state.LastInteraction}.");
        }
        else
        {
            prompt.AppendLine("- No persistent LivingNPCs state exists yet; use disposition and scene context conservatively.");
        }

        var priorityContext = this.BuildPriorityPromptContext(npc, state, world, recentEntries, recallPlan, communityImpressions).ToList();
        if (priorityContext.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("High-priority continuity:");
            foreach (string item in priorityContext)
            {
                prompt.AppendLine($"- {item}");
            }
        }

        if (recentEntries.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Recent tracked moments, oldest to newest:");
            foreach (var entry in recentEntries)
            {
                prompt.AppendLine($"- {this.FormatPromptEntry(entry)}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Next reply guidance:");
        foreach (string guidance in this.BuildReplyGuidance(state, world))
        {
            prompt.AppendLine($"- {guidance}");
        }

        this.MarkMemoriesRecalled(recallPlan);
        this.MarkCommunityImpressionsRecalled(communityImpressions);

        return prompt.ToString();
    }

    private string BuildConversationStance(
        NPC npc,
        LivingNpcState? state,
        NpcDispositionProfile disposition,
        WorldContextSnapshot world)
    {
        if (state == null)
        {
            return $"{npc.displayName} should sound {disposition.PromptLabel}, shaped by {disposition.SourceLabel} profile context and the current scene: {world.PromptLabel}.";
        }

        string tone = this.BuildToneCue(state);
        string rhythm = this.BuildRhythmCue(state);
        string scenePressure = world.StateInfluence.HasMood
            ? $"scene pressure suggests {world.StateInfluence.Mood}/{world.StateInfluence.Inclination}"
            : "scene pressure is mild";

        return $"{npc.displayName} should sound {tone}; temperament is {disposition.PromptLabel}; profile source is {disposition.SourceLabel}; relationship is {state.FamiliarityPromptLabel}; {rhythm}; {scenePressure}.";
    }

    private IEnumerable<string> BuildPriorityPromptContext(
        NPC npc,
        LivingNpcState? state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        MemoryRecallPlan recallPlan,
        IReadOnlyList<CommunityImpressionSelection> communityImpressions)
    {
        if (state != null)
        {
            int giftAge = GetMemoryAge(state.LastGiftTotalDays);
            if (!string.IsNullOrWhiteSpace(state.LastGiftName) && giftAge <= 7)
            {
                string freshness = FormatMemoryAge(state.LastGiftTotalDays);
                yield return $"Gift memory: the farmer offered {state.LastGiftName} {freshness}; taste was {state.LastGiftTaste}; let this affect warmth, surprise, or distance only if relevant.";
            }

            int eventAge = GetMemoryAge(state.LastEventTotalDays);
            if (!string.IsNullOrWhiteSpace(state.LastEventContext) && eventAge <= 3)
            {
                string freshness = FormatMemoryAge(state.LastEventTotalDays);
                yield return $"Event memory: {state.LastEventContext} ({freshness}); acknowledge only if the conversation naturally continues it.";
            }

            if (!string.IsNullOrWhiteSpace(state.FarmerNickname))
            {
                yield return $"Personal name memory: {state.FarmerNicknamePromptLabel}.";
            }

            if (recallPlan.LongTermMemories.Count > 0)
            {
                yield return $"Relevant long-term memories for this reply: {this.FormatLongTermMemoryPromptLabel(recallPlan.LongTermMemories)}; use at most one if it naturally matters now.";
            }

            if (recallPlan.PlayerPreferences.Count > 0)
            {
                yield return $"Relevant farmer preference memories for this reply: {this.FormatPlayerPreferencePromptLabel(recallPlan.PlayerPreferences)}; when a gift or topic naturally matches one, it is okay to acknowledge remembering it briefly.";
            }

            if (communityImpressions.Count > 0)
            {
                yield return $"Community impressions: {this.FormatCommunityImpressionPromptLabel(npc, communityImpressions)}; use at most one, keep indirect reports tentative, and do not reveal knowledge the NPC would not plausibly have.";
            }

            var activeBehaviorInfluence = state.ActiveDialogueBehaviorInfluences.FirstOrDefault();
            if (activeBehaviorInfluence != null)
            {
                yield return $"Conversation-driven behavior tendency: {activeBehaviorInfluence.PromptLabel}; this should shape body language and follow-through, not be quoted as dialogue.";
            }

            var activeHelpRequest = state.HelpRequests.FirstOrDefault(request => request.Status is "Offered" or "Pending");
            if (activeHelpRequest != null)
            {
                string lifecycle = activeHelpRequest.Status == "Offered"
                    ? "the NPC has asked, but the farmer has not clearly accepted or declined yet; do not treat it as an active task until accepted"
                    : "the farmer accepted this ask; it is now an active personal task";
                yield return $"Active help request: {activeHelpRequest.PromptLabel}; {lifecycle}; remember that this is an unfinished ask, not a vague topic.";
            }

            var recentlyFulfilledHelpRequest = state.HelpRequests.FirstOrDefault(request =>
                request.Status == "Fulfilled"
                && request.FulfilledTotalDays >= Game1.Date.TotalDays - 3
                && request.LastMentionedTotalDays < 0
            );
            if (recentlyFulfilledHelpRequest != null)
            {
                yield return $"Recently fulfilled help request: {recentlyFulfilledHelpRequest.FulfilledPromptLabel}; if it fits, the NPC may briefly thank the farmer for following through.";
            }

            var expiredHelpRequest = state.HelpRequests.FirstOrDefault(request =>
                request.Status == "Expired"
                && request.LastMentionedTotalDays < Game1.Date.TotalDays);
            if (expiredHelpRequest != null)
            {
                string reaction = string.IsNullOrWhiteSpace(expiredHelpRequest.FailureReaction)
                    ? "the NPC noticed the request did not work out"
                    : expiredHelpRequest.FailureReaction;
                yield return $"Unfinished help request: {expiredHelpRequest.PromptLabel}; reaction: {reaction}; the next conversation may acknowledge this according to personality, without over-punishing if the farmer never accepted.";
            }

            var urgentCommitment = state.Commitments.FirstOrDefault(commitment =>
                commitment.Status == "Pending"
                && commitment.DueTotalDays <= Game1.Date.TotalDays
            );
            if (urgentCommitment != null)
            {
                yield return $"Pending commitment: {urgentCommitment.PromptLabel}; if this is due now or today, let it matter naturally.";
            }

            var tomorrowCommitment = state.Commitments.FirstOrDefault(commitment =>
                commitment.Status == "Pending"
                && commitment.DueTotalDays == Game1.Date.TotalDays + 1
                && commitment.DayBeforeReminderMentionedTotalDays < Game1.Date.TotalDays
            );
            if (tomorrowCommitment != null)
            {
                yield return $"Upcoming commitment tomorrow: {tomorrowCommitment.PromptLabel}; if conversation allows, gently remind the farmer once rather than springing it on them tomorrow.";
            }

            var expiredCommitment = state.Commitments.FirstOrDefault(commitment =>
                commitment.Status == "Expired"
                && commitment.LastMentionedTotalDays < Game1.Date.TotalDays
            );
            if (expiredCommitment != null)
            {
                yield return $"Unmet commitment: {expiredCommitment.PromptLabel}; the next conversation should briefly acknowledge that it did not happen.";
            }

            var fulfilledCommitment = state.Commitments.FirstOrDefault(commitment =>
                commitment.Status == "Fulfilled"
                && commitment.FulfilledTotalDays >= Game1.Date.TotalDays - 3
                && commitment.FollowUpMentionedTotalDays < 0
            );
            if (fulfilledCommitment != null)
            {
                yield return $"Recently fulfilled commitment: {fulfilledCommitment.FulfilledPromptLabel}; this really happened, so the next conversation may briefly reflect on it as a shared experience.";
            }

            var sharedExperience = state.SharedExperiences.FirstOrDefault(experience =>
                experience.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && experience.FollowUpShownTotalDays < 0
                && experience.CreatedTotalDays >= Game1.Date.TotalDays - 7
            );
            if (sharedExperience != null)
            {
                yield return $"Shared experience milestone: {sharedExperience.PromptLabel}; if it fits, the NPC may warmly mention enjoying that time together and can be a little more open to a similar future plan.";
            }

            if (state.ConsecutiveMissedCommitments > 0)
            {
                yield return $"Plan reliability: the farmer has missed {state.ConsecutiveMissedCommitments} recent commitment(s); trust in keeping plans is {state.CommitmentTrust}/100. Let disappointment be proportionate, and allow sincere apology plus a new concrete plan to begin repair.";
            }

            if (state.RelationshipTrust < 35)
            {
                yield return $"Relationship trust is only {state.RelationshipTrust}/100; keep disclosures surface-level and avoid sudden emotional intimacy.";
            }
            else if (state.RelationshipTrust >= 80)
            {
                yield return $"Relationship trust is {state.RelationshipTrust}/100; deeper private honesty is allowed when the moment genuinely supports it.";
            }

            var activeConflict = state.Conflicts
                .Where(conflict => conflict.Status is "Active" or "Recovering")
                .OrderByDescending(conflict => conflict.Severity)
                .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
                .FirstOrDefault();
            if (activeConflict != null)
            {
                yield return $"Unresolved conflict: {activeConflict.PromptLabel}; while this remains unresolved, warmth should be reduced and the NPC may be brief, cool, or decline closeness.";
                if (activeConflict.RequiresComplexRepair)
                {
                    yield return $"Complex repair chain: stage {activeConflict.RepairStage}; serious hurt may need apology, a meaningful gesture, time, and a specific restorative conversation before it is fully repaired.";
                }
            }

            var recoveredConflict = state.Conflicts.FirstOrDefault(conflict =>
                conflict.Status == "Resolved"
                && conflict.ResolvedTotalDays >= Game1.Date.TotalDays - 3
                && conflict.RecoveryMentionedTotalDays < 0
            );
            if (recoveredConflict != null)
            {
                yield return $"Recently resolved conflict: {recoveredConflict.ResolvedPromptLabel}; if it fits naturally, the NPC may briefly make clear that the earlier issue is past now.";
            }

            if (!string.IsNullOrWhiteSpace(state.InteractionRhythm)
                && state.InteractionRhythm is not "New" and not "NoConversationToday")
            {
                yield return $"Interaction rhythm: {state.InteractionRhythmPromptLabel}; do not make every repeated talk sound equally eager.";
            }

            if (state.RepeatedConversationPressure >= 20)
            {
                yield return $"Boundary cue: repeated conversation pressure is {state.RepeatedConversationPressure}/100; a short or gently bounded reply may fit.";
            }

            if (state.Familiarity >= 18 || state.LastFriendshipHearts > 0)
            {
                yield return $"Relationship cue: familiarity {state.Familiarity}/100 and {state.LastFriendshipHearts} hearts; match warmth to this instead of defaulting to stranger-level politeness.";
            }
        }

        if (world.StateInfluence.HasMood && world.StateInfluence.Priority >= 35)
        {
            yield return $"Scene cue: {world.StateInfluence.Reason}; this can tint mood toward {world.StateInfluence.Mood} and reply style toward {world.StateInfluence.Inclination}.";
        }

        yield return $"World-stage continuity: {world.Progression.ReplyGuidance}";

        if (world.NearbyNpcNames.Count > 0)
        {
            yield return $"Social cue: nearby NPCs include {string.Join(", ", world.NearbyNpcNames)}; keep the reply aware of public company.";
        }

        var latestNonConversation = recentEntries
            .LastOrDefault(entry => !string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase));
        if (latestNonConversation != null)
        {
            yield return $"Most recent non-conversation moment: {latestNonConversation.Action}; {latestNonConversation.Reason}.";
        }
    }

    private IEnumerable<string> BuildReplyGuidance(LivingNpcState? state, WorldContextSnapshot world)
    {
        if (state == null)
        {
            yield return "Let the reply be scene-aware and modest because there is no persistent state yet.";
            yield return "Keep continuity subtle; do not invent strong feelings from weak context.";
            yield break;
        }

        yield return $"Tone target: {this.BuildToneCue(state)}.";
        yield return $"Relationship pacing: {state.InteractionComfortTierPromptLabel}.";
        yield return $"Disclosure pacing: {state.SecretSharingPromptLabel}.";
        yield return $"Invitation policy: {this.BuildTravelInvitationPolicyPromptLabel(state)}.";

        if (!string.IsNullOrWhiteSpace(state.LastGiftName) && state.LastGiftTotalDays == Game1.Date.TotalDays)
        {
            yield return "If the gift is conversationally relevant, a brief natural acknowledgement is allowed; avoid turning the whole reply into gift analysis.";
        }

        if (state.PlayerPreferenceMemories.Count > 0)
        {
            yield return "If choosing to mention a remembered farmer preference, keep it short and human, not like reciting a profile.";
        }

        if (state.Commitments.Any(commitment => commitment.Status == "Pending"))
        {
            yield return "Treat pending commitments as future plans, not as completed history.";
        }

        if (state.Commitments.Any(commitment =>
                commitment.Status == "Pending"
                && commitment.DueTotalDays == Game1.Date.TotalDays + 1
                && commitment.DayBeforeReminderMentionedTotalDays < Game1.Date.TotalDays))
        {
            yield return "If a commitment is due tomorrow, the NPC may mention it once in a natural reminder rather than repeating it insistently.";
        }

        if (state.Commitments.Any(commitment => commitment.Status == "Expired" && commitment.LastMentionedTotalDays < Game1.Date.TotalDays))
        {
            yield return "If there is an unmet commitment, mention it once with appropriate warmth, disappointment, or practicality instead of ignoring it.";
        }

        if (state.Commitments.Any(commitment =>
                commitment.Status == "Fulfilled"
                && commitment.FulfilledTotalDays >= Game1.Date.TotalDays - 3
                && commitment.FollowUpMentionedTotalDays < 0))
        {
            yield return "If a recently fulfilled commitment is relevant, mention it as something the two of you actually did together, not as a future plan.";
        }

        if (state.SharedExperiences.Any(experience =>
                experience.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && experience.FollowUpShownTotalDays < 0))
        {
            yield return "Shared experiences from completed plans can deepen continuity; after a pleasant one, the NPC may naturally be more open to proposing a similar plan later.";
        }

        if (state.ConsecutiveMissedCommitments > 0)
        {
            yield return "Missed commitments should affect trust more than ordinary awkwardness; sincere apology and a concrete rebooking can start repairing that trust.";
        }

        if (state.HasUnresolvedConflict)
        {
            yield return "An unresolved conflict is still shaping the relationship; do not answer with default warmth, and if the conflict is severe it is okay to keep the reply short or refuse a friendly invitation.";
            if (state.Conflicts.Any(conflict => conflict.RequiresComplexRepair && conflict.Status is "Active" or "Recovering"))
            {
                yield return "For a serious conflict, let repair feel gradual: a single pleasant line should not erase hurt before apology, gesture, time, and a real repair conversation have accumulated.";
            }
        }

        if (state.Conflicts.Any(conflict =>
                conflict.Status == "Resolved"
                && conflict.ResolvedTotalDays >= Game1.Date.TotalDays - 3
                && conflict.RecoveryMentionedTotalDays < 0))
        {
            yield return "If a recently resolved conflict is relevant, it is okay to say in a natural in-character way that the earlier matter is behind you now.";
        }

        if (state.RepeatedConversationPressure >= 20)
        {
            yield return "Because the farmer has been checking in repeatedly, it is okay to sound brief, amused, busy, or gently boundary-setting.";
        }

        if (world.StateInfluence.HasMood)
        {
            yield return $"Let the scene nudge tone through {world.StateInfluence.Reason}, without explicitly explaining the scene mechanics.";
        }

        yield return $"Keep references to town progress, the farmer's household, and how long she has lived here consistent with this world stage: {world.Progression.ReplyGuidance}";
    }

    private string BuildTravelInvitationPolicyPromptLabel(LivingNpcState state)
    {
        return state.InteractionComfortTier switch
        {
            "Intimate" or "Trusted" =>
                "shared outings and private visits may be accepted when the scene and schedule allow",
            "Friendly" =>
                "public outings are natural; private invitations such as visiting the farmer's farm should still need a good in-character reason",
            "Familiar" =>
                "brief public company may be acceptable, but private or extended outings should usually be declined or deferred",
            _ =>
                "the relationship is still distant, so private invitations such as visiting the farmer's farm or home should usually be declined politely; at most, brief public company may fit"
        };
    }

    private string BuildToneCue(LivingNpcState state)
    {
        string emotion = state.CurrentEmotion switch
        {
            "Happy" => "genuinely pleased",
            "Jealous" => "a little jealous",
            "Worried" => "worried",
            "Grateful" => "grateful",
            "Disappointed" => "disappointed",
            "Uneasy" => "slightly uneasy",
            "Upset" => "not entirely pleased",
            "Angry" => "angry",
            "Sad" => "sad",
            _ => "calm"
        };

        string attention = state.Attention switch
        {
            >= 75 => "attentive",
            >= 45 => "aware",
            _ => "lightly distracted"
        };

        string openness = state.Openness switch
        {
            >= 75 => "open",
            >= 45 => "measured",
            _ => "reserved"
        };

        return $"{state.Mood.ToLowerInvariant()}, {emotion}, {attention}, and {openness}";
    }

    private string BuildRhythmCue(LivingNpcState state)
    {
        return state.InteractionRhythm switch
        {
            "CrowdedToday" => "the farmer's attention is starting to feel repetitive today",
            "AtComfortLimit" => "the farmer is near today's comfortable conversation limit",
            "ComfortableRepeat" => "repeated conversation still feels natural because the relationship is close",
            "PoliteRepeat" => "repeated conversation should stay polite rather than overly warm",
            "DailyRoutine" or "BuildingRoutine" => "the farmer checking in has become part of a familiar routine",
            "LongQuietGap" or "AfterLongGap" => "there is a noticeable gap since the last recorded conversation",
            "FreshToday" => "this is the first recorded conversation today",
            "FirstConversation" => "this is the first recorded LivingNPCs conversation",
            _ => state.InteractionRhythmPromptLabel
        };
    }

    private static int GetMemoryAge(int totalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, Game1.Date.TotalDays - totalDays);
    }

    private static string FormatMemoryAge(int totalDays)
    {
        int age = GetMemoryAge(totalDays);
        return age switch
        {
            0 => "today",
            1 => "yesterday",
            int.MaxValue => "at an unknown time",
            _ => $"{age} days ago"
        };
    }

    private MemoryRecallPlan BuildMemoryRecallPlan(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        int longTermCount,
        int preferenceCount)
    {
        this.RefreshMemoryStores(state);
        MemoryRecallContext context = this.BuildMemoryRecallContext(state, world, recentEntries);
        var longTermMemories = state.LongTermMemories
            .Select(memory => this.ScoreLongTermMemory(memory, context))
            .Where(selection => selection.Score >= 45)
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Memory.Importance)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, longTermCount))
            .ToList();
        var playerPreferences = state.PlayerPreferenceMemories
            .Select(memory => this.ScorePlayerPreferenceMemory(memory, context))
            .Where(selection => selection.Score >= 45)
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Memory.Importance)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, preferenceCount))
            .ToList();

        return new MemoryRecallPlan(context, longTermMemories, playerPreferences);
    }

    private IReadOnlyList<CommunityImpressionSelection> BuildCommunityImpressionRecallPlan(
        LivingNpcState state,
        int maxCount)
    {
        this.RefreshMemoryStores(state);
        return state.CommunityImpressions
            .Select(this.ScoreCommunityImpression)
            .Where(selection => selection.Score >= 45)
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Memory.Importance)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, maxCount))
            .ToList();
    }

    private CommunityImpressionSelection ScoreCommunityImpression(CommunityImpressionFact memory)
    {
        int age = GetMemoryAge(memory.LastUpdatedTotalDays);
        int freshnessScore = age switch
        {
            0 => 30,
            1 => 24,
            <= 3 => 18,
            <= 7 => 10,
            <= 14 => 4,
            _ => -20
        };
        int sourceScore = memory.Source switch
        {
            "Witnessed" => 12,
            "CloseCircle" => 5,
            _ => 1
        };
        int lifecycleScore = memory.FreshnessStage switch
        {
            "fresh" => 10,
            "settled" => 2,
            "fading" => -8,
            _ => -20
        };
        int recentRecallPenalty = memory.LastRecalledTotalDays == Game1.Date.TotalDays ? 18 : 0;
        int score = memory.Importance
            + (memory.Confidence / 5)
            + freshnessScore
            + sourceScore
            + lifecycleScore
            + (memory.TimesReinforced * 2)
            - recentRecallPenalty
            - (memory.DistortionLevel / 8);
        string reason = memory.Source switch
        {
            "Witnessed" => $"目击，{FormatMemoryAge(memory.LastUpdatedTotalDays)}",
            "CloseCircle" => $"熟人转述，{FormatMemoryAge(memory.LastUpdatedTotalDays)}",
            _ => $"公共场所里听到一点，{FormatMemoryAge(memory.LastUpdatedTotalDays)}"
        };
        return new CommunityImpressionSelection(memory, score, reason);
    }

    private MemoryRecallContext BuildMemoryRecallContext(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries)
    {
        var tags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var tokens = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        this.AddWorldRecallTags(world, tags);
        this.AddRecallSignals(world.LocationName, tags, tokens);
        this.AddRecallSignals(world.LocationDisplayName, tags, tokens);
        this.AddRecallSignals(world.PromptLabel, tags, tokens);
        this.AddRecallSignals(world.Progression.PromptLabel, tags, tokens);
        this.AddRecallSignals(state.LastGiftName, tags, tokens);
        this.AddRecallSignals(state.LastEventContext, tags, tokens);
        this.AddRecallSignals(state.LastInteraction, tags, tokens);
        this.AddRecallSignals(state.LastEmotionReason, tags, tokens);

        foreach (var entry in recentEntries.TakeLast(6))
        {
            this.AddRecallSignals(entry.Action, tags, tokens);
            this.AddRecallSignals(entry.Reason, tags, tokens);
        }

        foreach (var request in state.HelpRequests.Where(request => request.Status == "Pending").Take(2))
        {
            this.AddRecallSignals(request.Summary, tags, tokens);
            this.AddRecallSignals(request.QuestionTopic, tags, tokens);
            this.AddRecallSignals(request.RequestedItemLabel, tags, tokens);
        }

        foreach (var commitment in state.Commitments.Where(commitment => commitment.Status == "Pending").Take(2))
        {
            this.AddRecallSignals(commitment.Summary, tags, tokens);
            this.AddRecallSignals(commitment.LocationLabel, tags, tokens);
        }

        return new MemoryRecallContext(tags, tokens);
    }

    private void AddWorldRecallTags(WorldContextSnapshot world, ISet<string> tags)
    {
        switch (world.LocationName)
        {
            case "Farm":
                tags.Add("farming");
                tags.Add("work");
                tags.Add("nature");
                break;

            case "Beach":
                tags.Add("fishing");
                tags.Add("nature");
                break;

            case "Mountain":
                tags.Add("mining");
                tags.Add("nature");
                break;

            case "ArchaeologyHouse":
                tags.Add("scholarly");
                break;

            case "Saloon":
                tags.Add("food");
                tags.Add("drink");
                tags.Add("comfort");
                break;
        }

        if (world.TimeOfDay < 900)
        {
            tags.Add("morning");
        }
        else if (world.TimeOfDay >= 1800)
        {
            tags.Add("night");
        }

        if (world.Progression.GreenhouseRepaired)
        {
            tags.Add("farming");
            tags.Add("work");
        }

        if (world.Progression.MinecartsRepaired)
        {
            tags.Add("mining");
        }

        if (world.Progression.GingerIslandUnlocked || world.Progression.BusRepaired)
        {
            tags.Add("adventurous");
            tags.Add("nature");
        }

        if (world.Progression.MovieTheaterOpen)
        {
            tags.Add("artistic");
            tags.Add("comfort");
        }
    }

    private void AddRecallSignals(string? text, ISet<string> tags, ISet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (string token in ExtractRecallTokens(text))
        {
            tokens.Add(token);
        }

        foreach (var pair in MemoryKeywordTags)
        {
            if (!text.Contains(pair.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string tag in pair.Value)
            {
                tags.Add(tag);
            }
        }
    }

    private static IEnumerable<string> ExtractRecallTokens(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(token => token.Length >= 2)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Take(24);
    }

    private LongTermMemorySelection ScoreLongTermMemory(LongTermMemoryFact memory, MemoryRecallContext context)
    {
        var reasons = new List<string>();
        int score = memory.Importance;
        int reinforcementBonus = System.Math.Min(18, memory.TimesReinforced * 3);
        score += reinforcementBonus;
        if (reinforcementBonus > 0)
        {
            reasons.Add($"reinforced +{reinforcementBonus}");
        }

        int kindBonus = GetLongTermMemoryKindBonus(memory.Kind);
        score += kindBonus;
        if (kindBonus > 0)
        {
            reasons.Add($"{memory.Kind} +{kindBonus}");
        }

        int freshnessBonus = GetMemoryFreshnessBonus(memory.LastUpdatedTotalDays);
        score += freshnessBonus;
        if (freshnessBonus > 0)
        {
            reasons.Add($"fresh +{freshnessBonus}");
        }

        int tagOverlap = memory.Tags.Count(tag => context.Tags.Contains(tag));
        if (tagOverlap > 0)
        {
            int tagBonus = tagOverlap * 14;
            score += tagBonus;
            reasons.Add($"tags +{tagBonus}");
        }

        int tokenOverlap = CountRecallTokenOverlap(memory.Subject, memory.Summary, context.Tokens);
        if (tokenOverlap > 0)
        {
            int tokenBonus = tokenOverlap * 8;
            score += tokenBonus;
            reasons.Add($"topic +{tokenBonus}");
        }

        int recallPenalty = GetRecentRecallPenalty(memory.LastRecalledTotalDays);
        score -= recallPenalty;
        if (recallPenalty > 0)
        {
            reasons.Add($"recent recall -{recallPenalty}");
        }

        return new LongTermMemorySelection(memory, score, reasons.Count == 0 ? "base salience" : string.Join(", ", reasons));
    }

    private PlayerPreferenceSelection ScorePlayerPreferenceMemory(PlayerPreferenceFact memory, MemoryRecallContext context)
    {
        var reasons = new List<string>();
        int score = memory.Importance;
        int reinforcementBonus = System.Math.Min(18, memory.TimesReinforced * 3);
        score += reinforcementBonus;
        if (reinforcementBonus > 0)
        {
            reasons.Add($"reinforced +{reinforcementBonus}");
        }

        int freshnessBonus = GetMemoryFreshnessBonus(memory.LastUpdatedTotalDays);
        score += freshnessBonus;
        if (freshnessBonus > 0)
        {
            reasons.Add($"fresh +{freshnessBonus}");
        }

        int tagOverlap = memory.Tags.Count(tag => context.Tags.Contains(tag));
        if (tagOverlap > 0)
        {
            int tagBonus = tagOverlap * 16;
            score += tagBonus;
            reasons.Add($"tags +{tagBonus}");
        }

        int tokenOverlap = CountRecallTokenOverlap(memory.Subject, memory.Summary, context.Tokens);
        if (tokenOverlap > 0)
        {
            int tokenBonus = tokenOverlap * 10;
            score += tokenBonus;
            reasons.Add($"topic +{tokenBonus}");
        }

        int recallPenalty = GetRecentRecallPenalty(memory.LastRecalledTotalDays);
        score -= recallPenalty;
        if (recallPenalty > 0)
        {
            reasons.Add($"recent recall -{recallPenalty}");
        }

        return new PlayerPreferenceSelection(memory, score, reasons.Count == 0 ? "base salience" : string.Join(", ", reasons));
    }

    private static int CountRecallTokenOverlap(string subject, string summary, IReadOnlySet<string> contextTokens)
    {
        return ExtractRecallTokens($"{subject} {summary}")
            .Count(contextTokens.Contains);
    }

    private static int GetMemoryFreshnessBonus(int totalDays)
    {
        return GetMemoryAge(totalDays) switch
        {
            0 => 18,
            1 => 14,
            <= 7 => 10,
            <= 28 => 5,
            _ => 0
        };
    }

    private static int GetRecentRecallPenalty(int totalDays)
    {
        return GetMemoryAge(totalDays) switch
        {
            0 => 18,
            1 => 10,
            <= 3 => 4,
            _ => 0
        };
    }

    private static int GetLongTermMemoryKindBonus(string kind)
    {
        return NormalizeLongTermMemoryKind(kind) switch
        {
            "boundary" => 18,
            "promise" => 16,
            "relationship" => 12,
            "preference" => 8,
            _ => 0
        };
    }

    private string FormatLongTermMemoryPromptLabel(IReadOnlyList<LongTermMemorySelection> selections)
    {
        return selections.Count == 0
            ? "no durable personal memory is especially relevant right now"
            : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
    }

    private string FormatPlayerPreferencePromptLabel(IReadOnlyList<PlayerPreferenceSelection> selections)
    {
        return selections.Count == 0
            ? "no durable farmer preference memory is especially relevant right now"
            : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
    }

    private string FormatCommunityImpressionPromptLabel(NPC npc, IReadOnlyList<CommunityImpressionSelection> selections)
    {
        CommunityReactionCue reaction = CommunityReactionStyle.For(npc);
        return selections.Count == 0
            ? "no community impression is especially relevant right now"
            : $"observer tendency: {reaction.PromptLabel}; retelling tendency: {reaction.RetellingPromptLabel}; {string.Join("; ", selections.Select(selection => selection.Memory.PromptLabel))}";
    }

    private string FormatLongTermMemoryDebugLabel(IReadOnlyList<LongTermMemorySelection> selections)
    {
        return selections.Count == 0
            ? "暂无"
            : string.Join("；", selections.Select(selection =>
                $"{selection.Memory.Summary}（分数 {selection.Score}，{selection.Reason}）"));
    }

    private string FormatPlayerPreferenceDebugLabel(IReadOnlyList<PlayerPreferenceSelection> selections)
    {
        return selections.Count == 0
            ? "暂无"
            : string.Join("；", selections.Select(selection =>
                $"{selection.Memory.Summary}（分数 {selection.Score}，{selection.Reason}）"));
    }

    private string FormatCommunityImpressionDebugLabel(IReadOnlyList<CommunityImpressionSelection> selections)
    {
        return selections.Count == 0
            ? "暂无"
            : string.Join("；", selections.Select(selection =>
                $"{selection.Memory.Summary}（{selection.Memory.Source}/{selection.Memory.Visibility}，分数 {selection.Score}，{selection.Reason}）"));
    }

    private void MarkMemoriesRecalled(MemoryRecallPlan recallPlan)
    {
        foreach (var selection in recallPlan.LongTermMemories)
        {
            this.MarkMemoryRecalled(selection.Memory);
        }

        foreach (var selection in recallPlan.PlayerPreferences)
        {
            this.MarkMemoryRecalled(selection.Memory);
        }
    }

    private void MarkCommunityImpressionsRecalled(IReadOnlyList<CommunityImpressionSelection> selections)
    {
        foreach (var selection in selections)
        {
            var memory = selection.Memory;
            if (memory.LastRecalledTotalDays == Game1.Date.TotalDays
                && memory.LastRecalledTimeOfDay == Game1.timeOfDay)
            {
                continue;
            }

            memory.LastRecalledTotalDays = Game1.Date.TotalDays;
            memory.LastRecalledTimeOfDay = Game1.timeOfDay;
            memory.RecallCount += 1;
        }
    }

    private void MarkMemoryRecalled(LongTermMemoryFact memory)
    {
        if (memory.LastRecalledTotalDays == Game1.Date.TotalDays
            && memory.LastRecalledTimeOfDay == Game1.timeOfDay)
        {
            return;
        }

        memory.LastRecalledTotalDays = Game1.Date.TotalDays;
        memory.LastRecalledTimeOfDay = Game1.timeOfDay;
        memory.RecallCount += 1;
    }

    private void MarkMemoryRecalled(PlayerPreferenceFact memory)
    {
        if (memory.LastRecalledTotalDays == Game1.Date.TotalDays
            && memory.LastRecalledTimeOfDay == Game1.timeOfDay)
        {
            return;
        }

        memory.LastRecalledTotalDays = Game1.Date.TotalDays;
        memory.LastRecalledTimeOfDay = Game1.timeOfDay;
        memory.RecallCount += 1;
    }

    private void RefreshMemoryStores(LivingNpcState state)
    {
        state.LongTermMemories ??= new List<LongTermMemoryFact>();
        state.LongTermMemories = state.LongTermMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(NormalizeLongTermMemoryForStore)
            .Where(memory => !string.IsNullOrWhiteSpace(BuildLongTermMemoryKey(memory.Kind, memory.Subject, memory.Summary)))
            .GroupBy(
                memory => BuildLongTermMemoryKey(memory.Kind, memory.Subject, memory.Summary),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(MergeLongTermMemoryGroup)
            .OrderByDescending(GetLongTermMemoryRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxLongTermMemoriesPerNpc)
            .ToList();

        state.PlayerPreferenceMemories ??= new List<PlayerPreferenceFact>();
        state.PlayerPreferenceMemories = state.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(NormalizePlayerPreferenceMemoryForStore)
            .Where(memory => memory.PreferenceKind != "none"
                && !string.IsNullOrWhiteSpace(BuildPlayerPreferenceKey(memory.PreferenceKind, memory.Subject, memory.Summary)))
            .GroupBy(
                memory => BuildPlayerPreferenceKey(memory.PreferenceKind, memory.Subject, memory.Summary),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(MergePlayerPreferenceGroup)
            .OrderByDescending(GetPlayerPreferenceRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxPlayerPreferenceMemoriesPerNpc)
            .ToList();

        state.CommunityImpressions ??= new List<CommunityImpressionFact>();
        state.CommunityImpressions = state.CommunityImpressions
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.SubjectNpcName)
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && (memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= Game1.Date.TotalDays))
            .Select(NormalizeCommunityImpressionForStore)
            .GroupBy(
                memory => BuildCommunityImpressionKey(memory.SubjectNpcName, memory.Kind, memory.Summary),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(MergeCommunityImpressionGroup)
            .OrderByDescending(GetCommunityImpressionRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxCommunityImpressionsPerNpc)
            .ToList();
    }

    internal static LongTermMemoryFact NormalizeLongTermMemoryForStore(LongTermMemoryFact memory)
    {
        memory.Kind = NormalizeLongTermMemoryKind(memory.Kind);
        memory.Subject = memory.Subject?.Trim() ?? string.Empty;
        memory.Summary = memory.Summary.Trim();
        memory.Tags = NormalizeMemoryTags(memory.Tags, memory.Subject, memory.Summary);
        memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
        memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
        memory.RecallCount = System.Math.Max(0, memory.RecallCount);
        if (memory.LastUpdatedTotalDays < 0)
        {
            memory.LastUpdatedTotalDays = memory.CreatedTotalDays;
            memory.LastUpdatedTimeOfDay = memory.CreatedTimeOfDay;
        }

        return memory;
    }

    internal static PlayerPreferenceFact NormalizePlayerPreferenceMemoryForStore(PlayerPreferenceFact memory)
    {
        memory.PreferenceKind = NormalizePlayerPreferenceKind(memory.PreferenceKind);
        memory.Subject = memory.Subject?.Trim() ?? string.Empty;
        memory.Summary = memory.Summary.Trim();
        memory.Tags = NormalizeMemoryTags(memory.Tags, memory.Subject, memory.Summary);
        memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
        memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
        memory.RecallCount = System.Math.Max(0, memory.RecallCount);
        if (memory.LastUpdatedTotalDays < 0)
        {
            memory.LastUpdatedTotalDays = memory.CreatedTotalDays;
            memory.LastUpdatedTimeOfDay = memory.CreatedTimeOfDay;
        }

        return memory;
    }

    internal static CommunityImpressionFact NormalizeCommunityImpressionForStore(CommunityImpressionFact memory)
    {
        memory.SubjectNpcName = memory.SubjectNpcName?.Trim() ?? string.Empty;
        memory.SubjectDisplayName = string.IsNullOrWhiteSpace(memory.SubjectDisplayName)
            ? memory.SubjectNpcName
            : memory.SubjectDisplayName.Trim();
        memory.Kind = NormalizeCommunityImpressionKind(memory.Kind);
        memory.Summary = memory.Summary.Trim();
        memory.Source = NormalizeCommunityImpressionSource(memory.Source);
        memory.Visibility = NormalizeCommunityImpressionVisibility(memory.Visibility);
        memory.Confidence = System.Math.Clamp(memory.Confidence, 0, 100);
        memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
        memory.TransmissionDepth = System.Math.Clamp(memory.TransmissionDepth, 0, 8);
        memory.DistortionLevel = System.Math.Clamp(memory.DistortionLevel, 0, 100);
        memory.HeardFromNpcName = memory.HeardFromNpcName?.Trim() ?? string.Empty;
        memory.CircleKey = memory.CircleKey?.Trim() ?? string.Empty;
        memory.ShareCount = System.Math.Max(0, memory.ShareCount);
        if (memory.ExpiresTotalDays < 0)
        {
            memory.ExpiresTotalDays = DetermineCommunityImpressionExpiry(
                memory.Source,
                memory.Visibility,
                memory.TransmissionDepth,
                memory.LastUpdatedTotalDays >= 0 ? memory.LastUpdatedTotalDays : Game1.Date.TotalDays
            );
        }

        memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
        memory.RecallCount = System.Math.Max(0, memory.RecallCount);
        if (memory.LastUpdatedTotalDays < 0)
        {
            memory.LastUpdatedTotalDays = memory.CreatedTotalDays;
            memory.LastUpdatedTimeOfDay = memory.CreatedTimeOfDay;
        }

        return memory;
    }

    private static LongTermMemoryFact MergeLongTermMemoryGroup(IEnumerable<LongTermMemoryFact> group)
    {
        var memories = group
            .OrderByDescending(GetLongTermMemoryRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .ToList();
        var primary = memories[0];
        foreach (var memory in memories.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(primary.Subject) && !string.IsNullOrWhiteSpace(memory.Subject))
            {
                primary.Subject = memory.Subject;
            }

            if (memory.Importance > primary.Importance || memory.Summary.Length > primary.Summary.Length)
            {
                primary.Summary = memory.Summary;
            }

            primary.Importance = System.Math.Max(primary.Importance, memory.Importance);
            primary.Tags = NormalizeMemoryTags(
                primary.Tags.Concat(memory.Tags),
                primary.Subject,
                primary.Summary,
                memory.Subject,
                memory.Summary);
            primary.TimesReinforced += memory.TimesReinforced;
            primary.RecallCount += memory.RecallCount;
            if (IsOlderCreatedAt(memory.CreatedTotalDays, primary.CreatedTotalDays))
            {
                primary.CreatedTotalDays = memory.CreatedTotalDays;
                primary.CreatedTimeOfDay = memory.CreatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay, primary.LastUpdatedTotalDays, primary.LastUpdatedTimeOfDay))
            {
                primary.LastUpdatedTotalDays = memory.LastUpdatedTotalDays;
                primary.LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastRecalledTotalDays, memory.LastRecalledTimeOfDay, primary.LastRecalledTotalDays, primary.LastRecalledTimeOfDay))
            {
                primary.LastRecalledTotalDays = memory.LastRecalledTotalDays;
                primary.LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay;
            }
        }

        return NormalizeLongTermMemoryForStore(primary);
    }

    private static PlayerPreferenceFact MergePlayerPreferenceGroup(IEnumerable<PlayerPreferenceFact> group)
    {
        var memories = group
            .OrderByDescending(GetPlayerPreferenceRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .ToList();
        var primary = memories[0];
        foreach (var memory in memories.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(primary.Subject) && !string.IsNullOrWhiteSpace(memory.Subject))
            {
                primary.Subject = memory.Subject;
            }

            if (memory.Importance > primary.Importance || memory.Summary.Length > primary.Summary.Length)
            {
                primary.Summary = memory.Summary;
            }

            primary.Importance = System.Math.Max(primary.Importance, memory.Importance);
            primary.Tags = NormalizeMemoryTags(
                primary.Tags.Concat(memory.Tags),
                primary.Subject,
                primary.Summary,
                memory.Subject,
                memory.Summary);
            primary.TimesReinforced += memory.TimesReinforced;
            primary.RecallCount += memory.RecallCount;
            if (IsOlderCreatedAt(memory.CreatedTotalDays, primary.CreatedTotalDays))
            {
                primary.CreatedTotalDays = memory.CreatedTotalDays;
                primary.CreatedTimeOfDay = memory.CreatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay, primary.LastUpdatedTotalDays, primary.LastUpdatedTimeOfDay))
            {
                primary.LastUpdatedTotalDays = memory.LastUpdatedTotalDays;
                primary.LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastRecalledTotalDays, memory.LastRecalledTimeOfDay, primary.LastRecalledTotalDays, primary.LastRecalledTimeOfDay))
            {
                primary.LastRecalledTotalDays = memory.LastRecalledTotalDays;
                primary.LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay;
            }
        }

        return NormalizePlayerPreferenceMemoryForStore(primary);
    }

    private static CommunityImpressionFact MergeCommunityImpressionGroup(IEnumerable<CommunityImpressionFact> group)
    {
        var memories = group
            .OrderByDescending(GetCommunityImpressionRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .ToList();
        var primary = memories[0];
        foreach (var memory in memories.Skip(1))
        {
            if (memory.Source == "Witnessed")
            {
                primary.Source = "Witnessed";
            }
            else if (primary.Source != "Witnessed" && memory.Source == "CloseCircle")
            {
                primary.Source = "CloseCircle";
            }

            if (memory.Importance > primary.Importance || memory.Summary.Length > primary.Summary.Length)
            {
                primary.Summary = memory.Summary;
            }

            if (string.IsNullOrWhiteSpace(primary.SubjectDisplayName) && !string.IsNullOrWhiteSpace(memory.SubjectDisplayName))
            {
                primary.SubjectDisplayName = memory.SubjectDisplayName;
            }

            primary.Confidence = System.Math.Max(primary.Confidence, memory.Confidence);
            primary.Importance = System.Math.Max(primary.Importance, memory.Importance);
            primary.Visibility = GetMoreRestrictiveCommunityVisibility(primary.Visibility, memory.Visibility);
            primary.TransmissionDepth = System.Math.Min(primary.TransmissionDepth, memory.TransmissionDepth);
            primary.DistortionLevel = System.Math.Min(primary.DistortionLevel, memory.DistortionLevel);
            if (string.IsNullOrWhiteSpace(primary.HeardFromNpcName))
            {
                primary.HeardFromNpcName = memory.HeardFromNpcName;
            }

            if (string.IsNullOrWhiteSpace(primary.CircleKey))
            {
                primary.CircleKey = memory.CircleKey;
            }

            primary.ShareCount += memory.ShareCount;
            if (IsNewerAt(memory.LastSharedTotalDays, memory.LastSharedTimeOfDay, primary.LastSharedTotalDays, primary.LastSharedTimeOfDay))
            {
                primary.LastSharedTotalDays = memory.LastSharedTotalDays;
                primary.LastSharedTimeOfDay = memory.LastSharedTimeOfDay;
            }

            primary.ExpiresTotalDays = System.Math.Max(primary.ExpiresTotalDays, memory.ExpiresTotalDays);
            primary.TimesReinforced += memory.TimesReinforced;
            primary.RecallCount += memory.RecallCount;
            if (IsOlderCreatedAt(memory.CreatedTotalDays, primary.CreatedTotalDays))
            {
                primary.CreatedTotalDays = memory.CreatedTotalDays;
                primary.CreatedTimeOfDay = memory.CreatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay, primary.LastUpdatedTotalDays, primary.LastUpdatedTimeOfDay))
            {
                primary.LastUpdatedTotalDays = memory.LastUpdatedTotalDays;
                primary.LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastRecalledTotalDays, memory.LastRecalledTimeOfDay, primary.LastRecalledTotalDays, primary.LastRecalledTimeOfDay))
            {
                primary.LastRecalledTotalDays = memory.LastRecalledTotalDays;
                primary.LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay;
            }
        }

        return NormalizeCommunityImpressionForStore(primary);
    }

    private static bool IsOlderCreatedAt(int candidateTotalDays, int currentTotalDays)
    {
        return candidateTotalDays >= 0 && (currentTotalDays < 0 || candidateTotalDays < currentTotalDays);
    }

    private static bool IsNewerAt(int candidateTotalDays, int candidateTimeOfDay, int currentTotalDays, int currentTimeOfDay)
    {
        return candidateTotalDays > currentTotalDays
            || (candidateTotalDays == currentTotalDays && candidateTimeOfDay > currentTimeOfDay);
    }

    internal static int GetLongTermMemoryRetentionScore(LongTermMemoryFact memory)
    {
        int score = memory.Importance;
        score += System.Math.Min(24, memory.TimesReinforced * 4);
        score += System.Math.Min(12, memory.RecallCount);
        score += GetLongTermMemoryKindBonus(memory.Kind);
        score += GetMemoryAge(memory.LastUpdatedTotalDays) switch
        {
            0 => 12,
            1 => 9,
            <= 7 => 6,
            <= 28 => 3,
            >= 112 => -12,
            >= 56 => -6,
            _ => 0
        };
        return score;
    }

    internal static int GetPlayerPreferenceRetentionScore(PlayerPreferenceFact memory)
    {
        int score = memory.Importance;
        score += System.Math.Min(24, memory.TimesReinforced * 4);
        score += System.Math.Min(12, memory.RecallCount);
        score += memory.PreferenceKind switch
        {
            "goal" => 14,
            "value" => 12,
            "habit" => 10,
            "liked_item_category" => 8,
            "disliked_item" => 8,
            _ => 0
        };
        score += GetMemoryAge(memory.LastUpdatedTotalDays) switch
        {
            0 => 12,
            1 => 9,
            <= 7 => 6,
            <= 28 => 3,
            >= 112 => -12,
            >= 56 => -6,
            _ => 0
        };
        return score;
    }

    internal static int GetCommunityImpressionRetentionScore(CommunityImpressionFact memory)
    {
        int age = GetMemoryAge(memory.LastUpdatedTotalDays);
        int freshness = age switch
        {
            0 => 20,
            1 => 16,
            <= 3 => 12,
            <= 7 => 6,
            <= 14 => 2,
            _ => -12
        };

        return memory.Importance
            + (memory.Confidence / 5)
            + freshness
            + (memory.Source switch
            {
                "Witnessed" => 8,
                "CloseCircle" => 3,
                _ => 0
            })
            - (memory.TransmissionDepth * 3)
            - (memory.DistortionLevel / 10)
            + System.Math.Min(memory.TimesReinforced * 3, 15)
            - System.Math.Min(memory.RecallCount * 2, 12);
    }

    public string BuildDebugSummary(NPC npc, int maxEntries, bool includeState)
    {
        this.entriesByNpc.TryGetValue(npc.Name, out var entries);
        var disposition = NpcDisposition.For(npc);
        var world = WorldContext.For(npc);
        LivingNpcState? state = null;
        if (includeState)
        {
            this.statesByNpc.TryGetValue(npc.Name, out state);
        }

        if ((entries == null || entries.Count == 0) && state == null)
        {
            return $"{npc.displayName} 还没有 LivingNPCs 行为/互动记忆或状态。\n- 行为倾向：{disposition.DebugLabel}\n- 当前场景：{world.DebugLabel}\n- 世界进度：{world.Progression.DebugLabel}";
        }

        var summary = new StringBuilder();
        if (state != null)
        {
            MemoryRecallPlan recallPlan = this.BuildMemoryRecallPlan(
                state,
                world,
                entries?.TakeLast(System.Math.Max(maxEntries, 0)).ToList() ?? new List<BehaviorMemoryEntry>(),
                longTermCount: 3,
                preferenceCount: 4
            );
            IReadOnlyList<CommunityImpressionSelection> communityImpressions = this.BuildCommunityImpressionRecallPlan(state, maxCount: 2);
            summary.AppendLine($"{npc.displayName} 当前 LivingNPCs 状态：");
            summary.AppendLine($"- 心情：{state.MoodLabel}");
            summary.AppendLine($"- 人际情绪：{state.EmotionLabel}");
            summary.AppendLine($"- 对玩家注意度：{state.AttentionLabel} ({state.Attention})");
            summary.AppendLine($"- 对玩家熟悉度：{state.FamiliarityLabel} ({state.Familiarity})");
            summary.AppendLine($"- 关系信任：{state.RelationshipTrustDebugLabel}");
            summary.AppendLine($"- 互动节奏：{state.InteractionRhythmLabel}");
            summary.AppendLine($"- 互动舒适度：{state.InteractionComfortTierLabel}");
            summary.AppendLine($"- 最近礼物：{state.LastGiftLabel}");
            summary.AppendLine($"- 最近事件：{state.LastEventLabel}");
            summary.AppendLine($"- 长期记忆：{state.LongTermMemoryDebugLabel}");
            summary.AppendLine($"- 当前检索长期记忆：{this.FormatLongTermMemoryDebugLabel(recallPlan.LongTermMemories)}");
            summary.AppendLine($"- 玩家偏好记忆：{state.PlayerPreferenceDebugLabel}");
            summary.AppendLine($"- 当前检索玩家偏好：{this.FormatPlayerPreferenceDebugLabel(recallPlan.PlayerPreferences)}");
            summary.AppendLine($"- 社区消息口吻：{CommunityReactionStyle.For(npc).DebugLabel}");
            summary.AppendLine($"- 社区圈层：{this.FormatSocialCircleDebugLabel(npc)}");
            summary.AppendLine($"- 社区印象：{state.CommunityImpressionDebugLabel}");
            summary.AppendLine($"- 当前检索社区印象：{this.FormatCommunityImpressionDebugLabel(communityImpressions)}");
            summary.AppendLine($"- 对话驱动行为：{state.DialogueBehaviorInfluenceDebugLabel}");
            summary.AppendLine($"- 长期约定：{state.CommitmentDebugLabel}");
            summary.AppendLine($"- 共同经历：{state.SharedExperienceDebugLabel}");
            summary.AppendLine($"- 履约信任：{state.CommitmentTrustDebugLabel}");
            summary.AppendLine($"- 主动求助：{state.HelpRequestDebugLabel}");
            summary.AppendLine($"- 冲突记忆：{state.ConflictDebugLabel}");
            summary.AppendLine($"- 长期称呼记忆：{state.FarmerNicknameLabel}");
            summary.AppendLine($"- 今日 AI 对话额外好感：{state.AiFriendshipGainedToday}");
            summary.AppendLine($"- 角色资料：{disposition.SourceDebugLabel}");
            summary.AppendLine($"- 行为倾向：{disposition.DebugLabel}");
            summary.AppendLine($"- 当前场景：{world.DebugLabel}");
            summary.AppendLine($"- 世界进度：{world.Progression.DebugLabel}");
            summary.AppendLine($"- 情境影响：{state.LastSceneInfluenceLabel}");
            summary.AppendLine($"- 回应倾向：{state.InclinationLabel}");
            summary.AppendLine($"- 最近互动：{state.LastInteractionLabel}");
        }
        else
        {
            summary.AppendLine($"{npc.displayName} 当前 LivingNPCs 倾向：");
            summary.AppendLine($"- 角色资料：{disposition.SourceDebugLabel}");
            summary.AppendLine($"- 行为倾向：{disposition.DebugLabel}");
            summary.AppendLine($"- 当前场景：{world.DebugLabel}");
            summary.AppendLine($"- 世界进度：{world.Progression.DebugLabel}");
        }

        if (entries is { Count: > 0 })
        {
            if (summary.Length > 0)
            {
                summary.AppendLine();
            }

            summary.AppendLine($"{npc.displayName} 最近 {System.Math.Min(entries.Count, maxEntries)} 条 LivingNPCs 行为/互动记忆：");
            foreach (var entry in entries.TakeLast(maxEntries))
            {
                string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
                string locationSuffix = string.IsNullOrWhiteSpace(location) ? string.Empty : $" @ {location}";
                summary.AppendLine($"- 第 {entry.TotalDays} 天 {entry.TimeOfDay}{locationSuffix}: {this.FormatDebugKind(entry)} {entry.Action}; {entry.Reason}");
            }
        }

        return summary.ToString().TrimEnd();
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

    private string FormatPromptEntry(BehaviorMemoryEntry entry)
    {
        string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
        string locationSuffix = string.IsNullOrWhiteSpace(location) ? string.Empty : $" at {location}";
        string kind = entry.Kind.ToLowerInvariant() switch
        {
            "conversation" => "conversation",
            "gift" => "gift",
            "event" => "event",
            "longtermmemory" => "long-term memory",
            "conflict" => "conflict",
            "npcaction" => "npc action",
            "socialmemory" => "social memory",
            _ => "behavior"
        };

        return $"{entry.Season} {entry.Day}, {entry.TimeOfDay}{locationSuffix}: {kind} - {entry.Action}; reason: {entry.Reason}";
    }

    private string FormatSocialCircleDebugLabel(NPC npc)
    {
        var labels = NpcSocialGraph.GetStableCircleLabels(npc.Name);
        return labels.Count == 0
            ? "暂无稳定圈层"
            : string.Join("、", labels);
    }

    private string FormatSocialCirclePromptLabel(NPC npc)
    {
        var labels = NpcSocialGraph.GetStableCircleLabels(npc.Name);
        return labels.Count == 0
            ? "no stable small-circle affiliation is currently tracked"
            : string.Join(", ", labels);
    }

    private string FormatDebugKind(BehaviorMemoryEntry entry)
    {
        return entry.Kind.ToLowerInvariant() switch
        {
            "conversation" => "[对话]",
            "gift" => "[礼物]",
            "event" => "[事件]",
            "longtermmemory" => "[长期记忆]",
            "helprequest" => "[求助]",
            "helprequestupdate" => "[求助更新]",
            "conflict" => "[冲突]",
            "npcaction" => "[NPC动作]",
            "socialmemory" => "[社区印象]",
            _ => "[行为]"
        };
    }

    private static ValleyTalkExchangeAnalysis ParseExchangeAnalysis(string analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson))
        {
            return new ValleyTalkExchangeAnalysis();
        }

        try
        {
            var analysis = JsonSerializer.Deserialize<ValleyTalkExchangeAnalysis>(
                analysisJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            ) ?? new ValleyTalkExchangeAnalysis();

            analysis.RapportDelta = System.Math.Clamp(analysis.RapportDelta, 0, 30);
            analysis.AmbientFollowUp ??= new ValleyTalkAmbientFollowUp();
            analysis.AmbientFollowUp.Text = analysis.AmbientFollowUp.Text?.Trim() ?? string.Empty;
            analysis.AmbientFollowUp.DelayMinutes = System.Math.Clamp(analysis.AmbientFollowUp.DelayMinutes, 0, 120);
            analysis.EmotionImpact ??= new ValleyTalkEmotionImpact();
            analysis.EmotionImpact.Emotion = NormalizeEmotion(analysis.EmotionImpact.Emotion);
            analysis.EmotionImpact.IntensityDelta = System.Math.Clamp(analysis.EmotionImpact.IntensityDelta, -100, 100);
            analysis.EmotionImpact.RepairDelta = System.Math.Clamp(analysis.EmotionImpact.RepairDelta, 0, 100);
            analysis.EmotionImpact.Reason = analysis.EmotionImpact.Reason?.Trim() ?? string.Empty;
            analysis.Memories = analysis.Memories
                .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
                .Select(memory =>
                {
                    memory.Kind = NormalizeLongTermMemoryKind(memory.Kind);
                    memory.Summary = memory.Summary.Trim();
                    memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
                    memory.PlayerPreferenceKind = NormalizePlayerPreferenceKind(memory.PlayerPreferenceKind);
                    memory.Subject = memory.Subject?.Trim() ?? string.Empty;
                    memory.Tags = NormalizeMemoryTags(memory.Tags, memory.Subject, memory.Summary);
                    memory.PlayerPreference = memory.PlayerPreference && memory.PlayerPreferenceKind != "none";
                    return memory;
                })
                .Take(4)
                .ToList();
            analysis.Actions = analysis.Actions
                .Where(action => action != null)
                .Select(action =>
                {
                    action.Type = NormalizeWorldActionType(action.Type);
                    action.Reason = action.Reason?.Trim() ?? string.Empty;
                    action.Amount = System.Math.Clamp(action.Amount, 0, 250);
                    action.TileCount = System.Math.Clamp(action.TileCount, 0, 12);
                    action.DurationMinutes = System.Math.Clamp(action.DurationMinutes, 0, 20);
                    action.DelayMinutes = System.Math.Clamp(action.DelayMinutes, 0, 20);
                    action.TargetLocation = action.TargetLocation?.Trim() ?? string.Empty;
                    action.QuestHint = action.QuestHint?.Trim() ?? string.Empty;
                    return action;
                })
                .Where(action => action.Type != "none")
                .Take(1)
                .ToList();
            analysis.BehaviorInfluences = analysis.BehaviorInfluences
                .Where(influence => influence != null && !string.IsNullOrWhiteSpace(influence.Summary))
                .Select(influence =>
                {
                    influence.Type = NormalizeDialogueBehaviorInfluenceType(influence.Type);
                    influence.Summary = influence.Summary.Trim();
                    influence.TargetLocation = NormalizeCommitmentLocation(influence.TargetLocation, string.Empty);
                    influence.TargetLocationLabel = influence.TargetLocationLabel?.Trim() ?? string.Empty;
                    influence.DurationDays = System.Math.Clamp(influence.DurationDays, 0, 7);
                    influence.Intensity = System.Math.Clamp(influence.Intensity, 0, 100);
                    influence.MaxTriggers = System.Math.Clamp(influence.MaxTriggers, 0, 4);
                    return influence;
                })
                .Where(influence => influence.Type != "none")
                .Take(2)
                .ToList();
            analysis.Commitments = analysis.Commitments
                .Where(commitment => commitment != null && !string.IsNullOrWhiteSpace(commitment.Summary))
                .Select(commitment =>
                {
                    commitment.Type = NormalizeCommitmentType(commitment.Type);
                    commitment.Summary = commitment.Summary.Trim();
                    commitment.DueInDays = System.Math.Clamp(commitment.DueInDays, 0, 28);
                    commitment.TimeOfDay = NormalizeTimeOfDay(commitment.TimeOfDay);
                    commitment.Location = commitment.Location?.Trim() ?? string.Empty;
                    commitment.LocationLabel = commitment.LocationLabel?.Trim() ?? string.Empty;
                    return commitment;
                })
                .Where(commitment => commitment.Type != "none")
                .Take(2)
                .ToList();
            analysis.HelpRequests = analysis.HelpRequests
                .Where(request => request != null && !string.IsNullOrWhiteSpace(request.Summary))
                .Select(request =>
                {
                    request.Type = NormalizeHelpRequestType(request.Type);
                    request.Summary = request.Summary.Trim();
                    request.RequestedItemId = request.RequestedItemId?.Trim() ?? string.Empty;
                    request.RequestedItemLabel = request.RequestedItemLabel?.Trim() ?? string.Empty;
                    request.QuestionTopic = request.QuestionTopic?.Trim() ?? string.Empty;
                    request.DueInDays = System.Math.Clamp(request.DueInDays, 1, 7);
                    request.Reason = request.Reason?.Trim() ?? string.Empty;
                    request.FollowUpPotential = NormalizeHelpRequestFollowUpPotential(request.FollowUpPotential);
                    request.Steps = (request.Steps ?? new List<ValleyTalkHelpRequestStepCandidate>())
                        .Where(step => step != null)
                        .Select(step =>
                        {
                            step.Type = NormalizeHelpRequestType(step.Type);
                            step.Summary = step.Summary?.Trim() ?? string.Empty;
                            step.RequestedItemId = step.RequestedItemId?.Trim() ?? string.Empty;
                            step.RequestedItemLabel = step.RequestedItemLabel?.Trim() ?? string.Empty;
                            step.QuestionTopic = step.QuestionTopic?.Trim() ?? string.Empty;
                            return step;
                        })
                        .Where(step => step.Type != "none" && !string.IsNullOrWhiteSpace(step.Summary))
                        .Take(3)
                        .ToList();
                    return request;
                })
                .Where(request => request.Type != "none")
                .Take(1)
                .ToList();
            analysis.HelpRequestUpdates = analysis.HelpRequestUpdates
                .Where(update => update != null && !string.IsNullOrWhiteSpace(update.Summary))
                .Select(update =>
                {
                    update.Summary = update.Summary.Trim();
                    update.Status = NormalizeHelpRequestUpdateStatus(update.Status);
                    update.Resolution = update.Resolution?.Trim() ?? string.Empty;
                    return update;
                })
                .Where(update => update.Status != "none")
                .Take(2)
                .ToList();
            analysis.Conflicts = analysis.Conflicts
                .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary))
                .Select(conflict =>
                {
                    conflict.CauseKind = NormalizeConflictCauseKind(conflict.CauseKind);
                    conflict.Summary = conflict.Summary.Trim();
                    conflict.Severity = System.Math.Clamp(conflict.Severity, 0, 100);
                    return conflict;
                })
                .Where(conflict => conflict.Severity > 0)
                .Take(2)
                .ToList();
            return analysis;
        }
        catch
        {
            return new ValleyTalkExchangeAnalysis();
        }
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
                    Game1.Date.TotalDays + GetComplexRepairDelayDays(existing.PeakSeverity)
                );
                existing.SpecificRepairTalkReceived = false;
                if (candidate.Severity >= 30)
                {
                    existing.MeaningfulGiftReceived = false;
                }
            }

            this.RefreshComplexRepairStage(existing);
            existing.Status = GetConflictStatus(existing.Severity);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            this.ApplyRelationshipTrustDelta(state, -System.Math.Max(2, GetConflictTrustLoss(candidate.Severity) / 2));
            this.ApplyEmotionForConflict(state, existing);
            return true;
        }

        var conflict = new NpcConflictFact
        {
            CauseKind = candidate.CauseKind,
            Summary = candidate.Summary.Trim(),
            Severity = LivingNpcState.ClampScore(candidate.Severity),
            PeakSeverity = LivingNpcState.ClampScore(candidate.Severity),
            Status = GetConflictStatus(candidate.Severity),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            RequiresComplexRepair = candidate.Severity >= 60,
            MinimumRepairTotalDays = candidate.Severity >= 60
                ? Game1.Date.TotalDays + GetComplexRepairDelayDays(candidate.Severity)
                : -1,
            TimesReinforced = 1
        };
        this.RefreshComplexRepairStage(conflict);
        state.Conflicts.Add(conflict);
        state.Conflicts = state.Conflicts
            .OrderBy(conflictEntry => ConflictStatusOrder(conflictEntry.Status))
            .ThenByDescending(conflictEntry => conflictEntry.Severity)
            .ThenByDescending(conflictEntry => conflictEntry.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        this.ApplyRelationshipTrustDelta(state, -GetConflictTrustLoss(conflict.Severity));
        this.ApplyEmotionForConflict(state, conflict);
        return true;
    }

    private int ApplyConflictRepair(LivingNpcState state, int repairDelta, bool apology, bool specificRepairTalk)
    {
        int totalRepair = System.Math.Clamp(repairDelta + (apology ? 12 : 0), 0, 100);
        if (totalRepair <= 0 && !apology && !specificRepairTalk)
        {
            return 0;
        }

        int resolved = 0;
        foreach (var conflict in state.Conflicts.Where(conflict => conflict.Status is "Active" or "Recovering"))
        {
            conflict.RepairScore = LivingNpcState.ClampScore(conflict.RepairScore + totalRepair);
            conflict.LastUpdatedTotalDays = Game1.Date.TotalDays;
            conflict.LastUpdatedTimeOfDay = Game1.timeOfDay;
            if (apology)
            {
                conflict.ApologyCount += 1;
                conflict.ApologyReceived = true;
            }

            if (specificRepairTalk)
            {
                conflict.SpecificRepairTalkReceived = true;
            }

            if (conflict.RequiresComplexRepair)
            {
                this.RefreshComplexRepairStage(conflict);
                int floor = GetComplexRepairSeverityFloor(conflict);
                conflict.Severity = LivingNpcState.ClampScore(System.Math.Max(floor, conflict.Severity - totalRepair));
                this.RefreshComplexRepairStage(conflict);
                if (CanResolveComplexConflict(conflict) && conflict.Severity == 0)
                {
                    this.ResolveConflict(state, conflict);
                    resolved++;
                }
                else
                {
                    conflict.Status = conflict.RepairStage is "NeedsApology" or "NeedsGesture"
                        ? "Active"
                        : "Recovering";
                }

                continue;
            }

            conflict.Severity = LivingNpcState.ClampScore(conflict.Severity - totalRepair);
            if (conflict.Severity == 0)
            {
                this.ResolveConflict(state, conflict);
                resolved++;
            }
            else
            {
                conflict.Status = GetConflictStatus(conflict.Severity);
            }
        }

        if (resolved > 0)
        {
            this.ApplyEmotion(state, "Calm", -state.EmotionIntensity, "an earlier conflict has been repaired");
        }

        return resolved;
    }

    private void MarkRepairGiftReceived(LivingNpcState state, string giftName)
    {
        foreach (var conflict in state.Conflicts.Where(conflict =>
                     conflict.RequiresComplexRepair
                     && conflict.Status is "Active" or "Recovering"))
        {
            conflict.MeaningfulGiftReceived = true;
            conflict.LastRepairGiftName = giftName;
            conflict.LastUpdatedTotalDays = Game1.Date.TotalDays;
            conflict.LastUpdatedTimeOfDay = Game1.timeOfDay;
            this.RefreshComplexRepairStage(conflict);
        }
    }

    private void DecayEmotionAndConflicts(LivingNpcState state, int emotionDailyDecay, int conflictDailyDecay)
    {
        if (emotionDailyDecay > 0 && state.EmotionIntensity > 0)
        {
            state.EmotionIntensity = LivingNpcState.MoveToward(state.EmotionIntensity, 0, emotionDailyDecay);
            if (state.EmotionIntensity == 0)
            {
                state.CurrentEmotion = "Calm";
            }
        }

        if (conflictDailyDecay <= 0)
        {
            return;
        }

        foreach (var conflict in state.Conflicts.Where(conflict => conflict.Status is "Active" or "Recovering"))
        {
            if (conflict.RequiresComplexRepair)
            {
                this.RefreshComplexRepairStage(conflict);
                int floor = GetComplexRepairSeverityFloor(conflict);
                conflict.Severity = System.Math.Max(
                    floor,
                    LivingNpcState.MoveToward(conflict.Severity, 0, conflictDailyDecay)
                );
                conflict.LastUpdatedTotalDays = Game1.Date.TotalDays;
                conflict.LastUpdatedTimeOfDay = Game1.timeOfDay;
                this.RefreshComplexRepairStage(conflict);
                if (CanResolveComplexConflict(conflict) && conflict.Severity == 0)
                {
                    this.ResolveConflict(state, conflict);
                }
                else
                {
                    conflict.Status = conflict.RepairStage is "NeedsApology" or "NeedsGesture"
                        ? "Active"
                        : "Recovering";
                }

                continue;
            }

            conflict.Severity = LivingNpcState.MoveToward(conflict.Severity, 0, conflictDailyDecay);
            conflict.LastUpdatedTotalDays = Game1.Date.TotalDays;
            conflict.LastUpdatedTimeOfDay = Game1.timeOfDay;
            if (conflict.Severity == 0)
            {
                this.ResolveConflict(state, conflict);
            }
            else
            {
                conflict.Status = GetConflictStatus(conflict.Severity);
            }
        }
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

    private void ResolveConflict(LivingNpcState state, NpcConflictFact conflict)
    {
        conflict.Status = "Resolved";
        conflict.ResolvedTotalDays = Game1.Date.TotalDays;
        conflict.ResolvedTimeOfDay = Game1.timeOfDay;
        conflict.RepairStage = "Resolved";
        if (conflict.RequiresComplexRepair && !conflict.RepairGrowthGranted)
        {
            conflict.RepairGrowthGranted = true;
            this.ApplyRelationshipTrustDelta(state, 8);
            state.Familiarity = LivingNpcState.ClampScore(state.Familiarity + 2);
        }
    }

    private void RefreshComplexRepairStage(NpcConflictFact conflict)
    {
        if (!conflict.RequiresComplexRepair)
        {
            conflict.RepairStage = conflict.Status == "Resolved" ? "Resolved" : "Simple";
            return;
        }

        if (conflict.Status == "Resolved")
        {
            conflict.RepairStage = "Resolved";
            return;
        }

        if (!conflict.ApologyReceived)
        {
            conflict.RepairStage = "NeedsApology";
            return;
        }

        if (!conflict.MeaningfulGiftReceived)
        {
            conflict.RepairStage = "NeedsGesture";
            return;
        }

        if (Game1.Date.TotalDays < conflict.MinimumRepairTotalDays)
        {
            conflict.RepairStage = "NeedsTime";
            return;
        }

        if (!conflict.SpecificRepairTalkReceived)
        {
            conflict.RepairStage = "NeedsConversation";
            return;
        }

        conflict.RepairStage = "ReadyToResolve";
    }

    private static bool CanResolveComplexConflict(NpcConflictFact conflict)
    {
        return !conflict.RequiresComplexRepair
            || conflict.RepairStage == "ReadyToResolve";
    }

    private static int GetComplexRepairSeverityFloor(NpcConflictFact conflict)
    {
        if (!conflict.RequiresComplexRepair)
        {
            return 0;
        }

        return conflict.RepairStage switch
        {
            "NeedsApology" => 45,
            "NeedsGesture" => 30,
            "NeedsTime" => 20,
            "NeedsConversation" => 10,
            _ => 0
        };
    }

    internal static int GetComplexRepairDelayDays(int severity)
    {
        return severity >= 80 ? 5 : 3;
    }

    private static int GetConflictTrustLoss(int severity)
    {
        return severity switch
        {
            >= 70 => 18,
            >= 50 => 12,
            >= 30 => 8,
            _ => 4
        };
    }

    private bool StorePlayerPreferenceMemory(LivingNpcState state, ValleyTalkMemoryCandidate candidate)
    {
        string normalizedKey = NormalizePlayerPreferenceKey(candidate);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        var existing = state.PlayerPreferenceMemories.FirstOrDefault(memory =>
            BuildPlayerPreferenceKey(memory.PreferenceKind, memory.Subject, memory.Summary) == normalizedKey);
        if (existing != null)
        {
            existing.PreferenceKind = NormalizePlayerPreferenceKind(candidate.PlayerPreferenceKind);
            existing.Subject = candidate.Subject.Trim();
            existing.Summary = candidate.Summary.Trim();
            existing.Importance = System.Math.Max(existing.Importance, candidate.Importance);
            existing.Tags = NormalizeMemoryTags(existing.Tags.Concat(candidate.Tags), existing.Subject, existing.Summary);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact
        {
            PreferenceKind = NormalizePlayerPreferenceKind(candidate.PlayerPreferenceKind),
            Subject = candidate.Subject.Trim(),
            Summary = candidate.Summary.Trim(),
            Tags = NormalizeMemoryTags(candidate.Tags, candidate.Subject, candidate.Summary),
            Importance = candidate.Importance,
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            TimesReinforced = 1
        });

        state.PlayerPreferenceMemories = state.PlayerPreferenceMemories
            .OrderByDescending(GetPlayerPreferenceRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxPlayerPreferenceMemoriesPerNpc)
            .ToList();
        return true;
    }

    private bool StoreDialogueBehaviorInfluence(
        NPC npc,
        LivingNpcState state,
        ValleyTalkBehaviorInfluenceCandidate candidate,
        int maxDialogueBehaviorInfluenceDays)
    {
        string type = NormalizeDialogueBehaviorInfluenceType(candidate.Type);
        if (type == "none" || string.IsNullOrWhiteSpace(candidate.Summary))
        {
            return false;
        }

        string fallbackLocation = npc.currentLocation?.Name ?? Game1.currentLocation?.Name ?? string.Empty;
        string targetLocation = NormalizeCommitmentLocation(candidate.TargetLocation, fallbackLocation);
        string targetLabel = string.IsNullOrWhiteSpace(candidate.TargetLocationLabel)
            ? GetCommitmentLocationLabel(targetLocation)
            : candidate.TargetLocationLabel.Trim();
        int durationDays = candidate.DurationDays <= 0
            ? GetDefaultDialogueBehaviorDurationDays(type)
            : candidate.DurationDays;
        durationDays = System.Math.Clamp(
            durationDays,
            0,
            System.Math.Clamp(maxDialogueBehaviorInfluenceDays, 1, 7)
        );
        int maxTriggers = candidate.MaxTriggers <= 0
            ? GetDefaultDialogueBehaviorMaxTriggers(type)
            : candidate.MaxTriggers;
        string normalizedKey = BuildDialogueBehaviorInfluenceKey(type, candidate.Summary, targetLocation);

        var existing = state.DialogueBehaviorInfluences.FirstOrDefault(influence =>
            BuildDialogueBehaviorInfluenceKey(influence.Type, influence.Summary, influence.TargetLocation) == normalizedKey
            && influence.Status == "Active");
        if (existing != null)
        {
            existing.Summary = candidate.Summary.Trim();
            existing.TargetLocation = targetLocation;
            existing.TargetLocationLabel = targetLabel;
            existing.Intensity = System.Math.Max(existing.Intensity, candidate.Intensity);
            existing.ExpiresTotalDays = System.Math.Max(existing.ExpiresTotalDays, Game1.Date.TotalDays + durationDays);
            existing.MaxTriggers = System.Math.Max(existing.MaxTriggers, maxTriggers);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            this.ApplyDialogueBehaviorStateEffect(state, existing);
            return true;
        }

        var influence = new DialogueBehaviorInfluenceFact
        {
            Type = type,
            Summary = candidate.Summary.Trim(),
            TargetLocation = targetLocation,
            TargetLocationLabel = targetLabel,
            Intensity = System.Math.Clamp(candidate.Intensity <= 0 ? GetDefaultDialogueBehaviorIntensity(type) : candidate.Intensity, 1, 100),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            ExpiresTotalDays = Game1.Date.TotalDays + durationDays,
            MaxTriggers = System.Math.Clamp(maxTriggers, 1, 4),
            Status = "Active",
            TimesReinforced = 1
        };

        state.DialogueBehaviorInfluences.Add(influence);
        state.DialogueBehaviorInfluences = state.DialogueBehaviorInfluences
            .OrderBy(influence => DialogueBehaviorInfluenceStatusOrder(influence.Status))
            .ThenBy(influence => influence.ExpiresTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTimeOfDay)
            .Take(MaxDialogueBehaviorInfluencesPerNpc)
            .ToList();
        this.ApplyDialogueBehaviorStateEffect(state, influence);
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

    private bool StoreCommitment(NPC npc, LivingNpcState state, ValleyTalkCommitmentCandidate candidate, int maxPendingCommitmentsPerNpc)
    {
        string locationName = NormalizeCommitmentLocation(candidate.Location, npc.currentLocation?.Name ?? Game1.currentLocation?.Name ?? string.Empty);
        string locationLabel = string.IsNullOrWhiteSpace(candidate.LocationLabel)
            ? GetCommitmentLocationLabel(locationName)
            : candidate.LocationLabel.Trim();
        int dueTotalDays = Game1.Date.TotalDays + candidate.DueInDays;
        string normalizedKey = BuildCommitmentKey(candidate.Type, candidate.Summary, dueTotalDays, candidate.TimeOfDay, locationName);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        var existing = state.Commitments.FirstOrDefault(commitment =>
            BuildCommitmentKey(commitment.Type, commitment.Summary, commitment.DueTotalDays, commitment.TimeOfDay, commitment.LocationName) == normalizedKey);
        if (existing != null)
        {
            existing.Summary = candidate.Summary.Trim();
            existing.LocationLabel = locationLabel;
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            if (existing.Status == "Expired")
            {
                existing.Status = "Pending";
                existing.RenewedAfterMiss = true;
                existing.RenewedTotalDays = Game1.Date.TotalDays;
                existing.RenewedTimeOfDay = Game1.timeOfDay;
                state.CommitmentTrust = LivingNpcState.ClampScore(state.CommitmentTrust + 4);
            }

            return true;
        }

        var recentMissedSimilar = state.Commitments
            .Where(commitment => commitment.Status == "Expired"
                && commitment.Type == candidate.Type
                && commitment.LocationName == locationName
                && commitment.LastUpdatedTotalDays >= Game1.Date.TotalDays - 7)
            .OrderByDescending(commitment => commitment.LastUpdatedTotalDays)
            .ThenByDescending(commitment => commitment.LastUpdatedTimeOfDay)
            .FirstOrDefault();

        state.Commitments.Add(new NpcCommitmentFact
        {
            Type = candidate.Type,
            Summary = candidate.Summary.Trim(),
            DueTotalDays = dueTotalDays,
            TimeOfDay = candidate.TimeOfDay,
            LocationName = locationName,
            LocationLabel = locationLabel,
            Status = "Pending",
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            RenewedAfterMiss = recentMissedSimilar != null,
            RenewedTotalDays = recentMissedSimilar != null ? Game1.Date.TotalDays : -1,
            RenewedTimeOfDay = recentMissedSimilar != null ? Game1.timeOfDay : 0,
            TimesReinforced = 1
        });
        if (recentMissedSimilar != null)
        {
            recentMissedSimilar.RenewedAfterMiss = true;
            recentMissedSimilar.RenewedTotalDays = Game1.Date.TotalDays;
            recentMissedSimilar.RenewedTimeOfDay = Game1.timeOfDay;
            state.CommitmentTrust = LivingNpcState.ClampScore(state.CommitmentTrust + 4);
            state.ConsecutiveMissedCommitments = System.Math.Max(0, state.ConsecutiveMissedCommitments - 1);
        }

        while (state.Commitments.Count(commitment => commitment.Status == "Pending") > maxPendingCommitmentsPerNpc)
        {
            var oldestPending = state.Commitments
                .Where(commitment => commitment.Status == "Pending")
                .OrderBy(commitment => commitment.DueTotalDays)
                .ThenBy(commitment => commitment.TimeOfDay)
                .FirstOrDefault();
            if (oldestPending == null)
            {
                break;
            }

            state.Commitments.Remove(oldestPending);
        }

        state.Commitments = state.Commitments
            .OrderBy(commitment => CommitmentStatusOrder(commitment.Status))
            .ThenBy(commitment => commitment.DueTotalDays)
            .ThenBy(commitment => commitment.TimeOfDay)
            .ThenByDescending(commitment => commitment.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        return true;
    }

    private bool StoreHelpRequest(
        NPC npc,
        LivingNpcState state,
        ValleyTalkHelpRequestCandidate candidate,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests)
    {
        if (!this.CanOpenHelpRequest(
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

        var steps = this.BuildHelpRequestSteps(candidate);
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
            Status = candidate.RequiresAcceptance ? "Offered" : "Pending",
            FollowUpPotential = NormalizeHelpRequestFollowUpPotential(candidate.FollowUpPotential),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            AcceptedTotalDays = candidate.RequiresAcceptance ? -1 : Game1.Date.TotalDays,
            AcceptedTimeOfDay = candidate.RequiresAcceptance ? 0 : Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            RewardFriendship = DetermineHelpRequestFriendshipReward(
                npc,
                candidate,
                normalizedType
            ),
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

    private List<NpcHelpRequestStepFact> BuildHelpRequestSteps(ValleyTalkHelpRequestCandidate candidate)
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
        foreach (var rawStep in rawSteps.Take(3))
        {
            string type = NormalizeHelpRequestType(rawStep.Type);
            if (type == "item_request")
            {
                if (!AllowedHelpRequestItemIds.Contains(rawStep.RequestedItemId)
                    || !HelpRequestAdvisor.IsCurrentlyRequestableItem(rawStep.RequestedItemId))
                {
                    continue;
                }
            }
            else if (type == "question_request")
            {
                if (string.IsNullOrWhiteSpace(rawStep.QuestionTopic))
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
        state.CommitmentTrust = LivingNpcState.ClampScore(state.CommitmentTrust + 4);
        state.ConsecutiveMissedCommitments = 0;
        this.ApplyEmotion(
            state,
            "Grateful",
            16,
            $"the farmer helped with a personal request: {request.Summary}"
        );
        request.FollowUpEligibleTotalDays = Game1.Date.TotalDays + 1;
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

    private static int DetermineHelpRequestFriendshipReward(
        NPC npc,
        ValleyTalkHelpRequestCandidate candidate,
        string normalizedType)
    {
        unchecked
        {
            string seed = $"{npc.Name}:{normalizedType}:{candidate.Summary}:{Game1.Date.TotalDays}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return 50 + System.Math.Abs(hash % 51);
        }
    }

    private bool CanOpenHelpRequest(
        NPC npc,
        LivingNpcState state,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests,
        out string reason)
    {
        if (maxPendingHelpRequestsPerNpc <= 0)
        {
            reason = "help requests are disabled";
            return false;
        }

        if (state.HelpRequests.Count(request => request.Status is "Offered" or "Pending") >= maxPendingHelpRequestsPerNpc)
        {
            reason = "an active help request is already pending";
            return false;
        }

        if (state.HighestUnresolvedConflictSeverity >= 30)
        {
            reason = "unresolved conflict makes asking for help feel wrong";
            return false;
        }

        var world = WorldContext.For(npc);
        bool enoughTrust = state.RelationshipTrust >= minRelationshipTrustForHelpRequests;
        bool enoughFamiliarity = state.Familiarity >= 20 || world.FriendshipHearts >= 2;
        if (!enoughTrust || !enoughFamiliarity)
        {
            reason = "the relationship is not close enough yet";
            return false;
        }

        if (state.CurrentEmotion is "Angry" or "Upset")
        {
            reason = "their current emotion is too strained";
            return false;
        }

        if (state.LastHelpRequestTotalDays >= 0
            && Game1.Date.TotalDays - state.LastHelpRequestTotalDays < helpRequestCooldownDays)
        {
            reason = "a recent help request is still too fresh";
            return false;
        }

        reason = "one modest favor would be natural if the conversation genuinely leads there";
        return true;
    }

    private string BuildHelpRequestReadinessLabel(
        NPC npc,
        LivingNpcState state,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests)
    {
        return this.CanOpenHelpRequest(
                npc,
                state,
                maxPendingHelpRequestsPerNpc,
                helpRequestCooldownDays,
                minRelationshipTrustForHelpRequests,
                out string reason)
            ? $"may naturally ask for one modest favor now; {reason}"
            : $"should not open a new help request now; {reason}";
    }

    private bool StoreLongTermMemory(LivingNpcState state, ValleyTalkMemoryCandidate candidate)
    {
        string normalizedKey = BuildLongTermMemoryKey(candidate.Kind, candidate.Subject, candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        var existing = state.LongTermMemories.FirstOrDefault(memory =>
            BuildLongTermMemoryKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey);
        if (existing != null)
        {
            existing.Kind = NormalizeLongTermMemoryKind(candidate.Kind);
            existing.Subject = candidate.Subject.Trim();
            if (candidate.Importance >= existing.Importance || existing.Summary.Length < candidate.Summary.Trim().Length)
            {
                existing.Summary = candidate.Summary.Trim();
            }

            existing.Importance = System.Math.Max(existing.Importance, candidate.Importance);
            existing.Tags = NormalizeMemoryTags(existing.Tags.Concat(candidate.Tags), existing.Subject, existing.Summary);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            this.TryUpdateNicknameStateFromMemory(state, existing);
            return true;
        }

        state.LongTermMemories.Add(new LongTermMemoryFact
        {
            Kind = NormalizeLongTermMemoryKind(candidate.Kind),
            Subject = candidate.Subject.Trim(),
            Summary = candidate.Summary.Trim(),
            Tags = NormalizeMemoryTags(candidate.Tags, candidate.Subject, candidate.Summary),
            Importance = candidate.Importance,
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            TimesReinforced = 1
        });

        state.LongTermMemories = state.LongTermMemories
            .OrderByDescending(GetLongTermMemoryRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxLongTermMemoriesPerNpc)
            .ToList();

        this.TryUpdateNicknameStateFromMemory(state, state.LongTermMemories.LastOrDefault(memory =>
            BuildLongTermMemoryKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey));
        return true;
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

    private static string NormalizeLongTermMemoryKind(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "preference" => "preference",
            "promise" => "promise",
            "boundary" => "boundary",
            "relationship" => "relationship",
            _ => "fact"
        };
    }

    internal static string NormalizePlayerPreferenceKind(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "liked_item_category" => "liked_item_category",
            "disliked_item" => "disliked_item",
            "habit" => "habit",
            "value" => "value",
            "goal" => "goal",
            _ => "none"
        };
    }

    internal static string NormalizeCommunityImpressionKind(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "helped" => "helped",
            "shared_experience" => "shared_experience",
            "relationship_trend" => "relationship_trend",
            "romantic_attention" => "romantic_attention",
            _ => "community_fact"
        };
    }

    internal static string NormalizeCommunityImpressionSource(string source)
    {
        return source?.Trim() switch
        {
            "Witnessed" => "Witnessed",
            "CloseCircle" => "CloseCircle",
            "Heard" => "CloseCircle",
            "PublicRumor" => "PublicRumor",
            _ => "PublicRumor"
        };
    }

    internal static string NormalizeCommunityImpressionVisibility(string visibility)
    {
        return visibility?.Trim() switch
        {
            "Private" => "Private",
            "Personal" => "Personal",
            _ => "Public"
        };
    }

    private static string GetMoreRestrictiveCommunityVisibility(string first, string second)
    {
        int firstRank = GetCommunityVisibilityRank(first);
        int secondRank = GetCommunityVisibilityRank(second);
        return firstRank >= secondRank
            ? NormalizeCommunityImpressionVisibility(first)
            : NormalizeCommunityImpressionVisibility(second);
    }

    private static int GetCommunityVisibilityRank(string visibility)
    {
        return NormalizeCommunityImpressionVisibility(visibility) switch
        {
            "Private" => 2,
            "Personal" => 1,
            _ => 0
        };
    }

    private static int DetermineCommunityImpressionExpiry(string source, string visibility, int transmissionDepth, int baseTotalDays)
    {
        int baseLifetime = NormalizeCommunityImpressionSource(source) switch
        {
            "Witnessed" => 14,
            "CloseCircle" => 10,
            _ => 6
        };
        int visibilityAdjustment = NormalizeCommunityImpressionVisibility(visibility) switch
        {
            "Private" => -2,
            "Personal" => -1,
            _ => 0
        };
        int depthPenalty = System.Math.Min(4, System.Math.Max(0, transmissionDepth));
        return baseTotalDays + System.Math.Max(3, baseLifetime + visibilityAdjustment - depthPenalty);
    }

    private static string NormalizeWorldActionType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "give_small_gift" => "give_small_gift",
            "give_meaningful_gift" => "give_meaningful_gift",
            "give_money" => "give_money",
            "water_nearby_crops" => "water_nearby_crops",
            "walk_together" => "walk_together",
            "escort_to_location" => "escort_to_location",
            "festival_interaction" => "festival_interaction",
            "assist_quest" => "assist_quest",
            _ => "none"
        };
    }

    internal static string NormalizeDialogueBehaviorInfluenceType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "companion_walk" => "companion_walk",
            "walk_together" => "companion_walk",
            "visit_location" => "visit_location",
            "go_to_location" => "visit_location",
            "comforted" => "comforted",
            "reassured" => "comforted",
            "offended" => "offended",
            "hurt" => "offended",
            "give_space" => "give_space",
            "needs_space" => "give_space",
            "stay_near" => "stay_near",
            "approach" => "stay_near",
            "pause_to_talk" => "pause_to_talk",
            "stop_to_talk" => "pause_to_talk",
            _ => "none"
        };
    }

    internal static string NormalizeEmotion(string emotion)
    {
        return emotion?.Trim().ToLowerInvariant() switch
        {
            "happy" => "Happy",
            "calm" => "Calm",
            "jealous" => "Jealous",
            "worried" => "Worried",
            "grateful" => "Grateful",
            "disappointed" => "Disappointed",
            "uneasy" => "Uneasy",
            "upset" => "Upset",
            "angry" => "Angry",
            "sad" => "Sad",
            _ => "none"
        };
    }

    internal static string NormalizeConflictCauseKind(string causeKind)
    {
        return causeKind?.Trim().ToLowerInvariant() switch
        {
            "dialogue" => "dialogue",
            "gift" => "gift",
            "boundary" => "boundary",
            "promise" => "promise",
            _ => "dialogue"
        };
    }

    internal static string NormalizeCommitmentType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "meet_again" => "meet_again",
            "go_together" => "go_together",
            "help_task" => "help_task",
            "celebrate_together" => "celebrate_together",
            "share_activity" => "share_activity",
            "help_request" => "help_request",
            _ => "none"
        };
    }

    internal static string NormalizeHelpRequestType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "item_request" => "item_request",
            "question_request" => "question_request",
            _ => "none"
        };
    }

    internal static string NormalizeHelpRequestUpdateStatus(string status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "accepted" => "accepted",
            "fulfilled" => "fulfilled",
            "advanced" => "advanced",
            "declined" => "declined",
            _ => "none"
        };
    }

    internal static string NormalizeHelpRequestFollowUpPotential(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "new_commitment" => "new_commitment",
            "shared_activity" => "shared_activity",
            "deeper_relationship" => "deeper_relationship",
            _ => "none"
        };
    }

    private static int GetDefaultDialogueBehaviorDurationDays(string type)
    {
        return NormalizeDialogueBehaviorInfluenceType(type) switch
        {
            "companion_walk" => 0,
            "pause_to_talk" => 1,
            "visit_location" => 3,
            "comforted" => 2,
            "offended" => 3,
            "give_space" => 2,
            "stay_near" => 1,
            _ => 1
        };
    }

    private static int GetDefaultDialogueBehaviorMaxTriggers(string type)
    {
        return NormalizeDialogueBehaviorInfluenceType(type) switch
        {
            "companion_walk" => 1,
            "pause_to_talk" => 1,
            "visit_location" => 2,
            "comforted" => 2,
            "offended" => 2,
            "give_space" => 2,
            "stay_near" => 2,
            _ => 1
        };
    }

    private static int GetDefaultDialogueBehaviorIntensity(string type)
    {
        return NormalizeDialogueBehaviorInfluenceType(type) switch
        {
            "companion_walk" => 65,
            "visit_location" => 45,
            "comforted" => 55,
            "offended" => 70,
            "give_space" => 60,
            "stay_near" => 55,
            "pause_to_talk" => 45,
            _ => 40
        };
    }

    internal static int DialogueBehaviorInfluenceStatusOrder(string status)
    {
        return status switch
        {
            "Active" => 0,
            "Spent" => 1,
            "Expired" => 2,
            _ => 3
        };
    }

    private static int GetFulfilledCommitmentTrustGain(string type)
    {
        return NormalizeCommitmentType(type) switch
        {
            "celebrate_together" => 8,
            "share_activity" => 7,
            "go_together" => 6,
            "help_task" => 6,
            _ => 5
        };
    }

    private static int GetExpiredCommitmentTrustLoss(int consecutiveMisses)
    {
        return consecutiveMisses switch
        {
            >= 2 => 14,
            1 => 10,
            _ => 6
        };
    }

    private void StoreSharedExperience(LivingNpcState state, NpcCommitmentFact commitment)
    {
        string key = BuildSharedExperienceKey(commitment.Type, commitment.Summary, commitment.LocationName);
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
            Type = commitment.Type,
            Summary = commitment.Summary,
            LocationName = commitment.LocationName,
            LocationLabel = commitment.LocationLabel,
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            Importance = commitment.Type switch
            {
                "celebrate_together" => 80,
                "share_activity" => 72,
                "go_together" => 68,
                "help_task" => 66,
                _ => 60
            },
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

    private static string BuildSharedExperienceKey(string type, string summary, string locationName)
    {
        return $"{NormalizeCommitmentType(type)}:{NormalizeMemorySummary(summary)}:{NormalizeCommitmentLocation(locationName, string.Empty)}";
    }

    private static string NormalizeMemorySummary(string summary)
    {
        return Regex.Replace(summary ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
    }

    internal static List<string> NormalizePlayerPreferenceTags(IEnumerable<string>? tags)
    {
        return tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => AllowedPlayerPreferenceTags.Contains(tag))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList()
            ?? new List<string>();
    }

    private static List<string> NormalizeMemoryTags(IEnumerable<string>? tags, params string?[] texts)
    {
        var allTags = new List<string>();
        if (tags != null)
        {
            allTags.AddRange(tags);
        }

        foreach (string? text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var pair in MemoryKeywordTags)
            {
                if (!text.Contains(pair.Key, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                allTags.AddRange(pair.Value);
            }
        }

        return NormalizePlayerPreferenceTags(allTags);
    }

    private static string NormalizePlayerPreferenceKey(ValleyTalkMemoryCandidate candidate)
    {
        return BuildPlayerPreferenceKey(candidate.PlayerPreferenceKind, candidate.Subject, candidate.Summary);
    }

    private static string BuildPlayerPreferenceKey(string kind, string subject, string summary)
    {
        string normalizedKind = NormalizePlayerPreferenceKind(kind);
        string normalizedSubject = NormalizeMemorySummary(subject);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string identity = string.IsNullOrWhiteSpace(normalizedSubject)
            ? normalizedSummary
            : normalizedSubject;
        return normalizedKind == "none" || string.IsNullOrWhiteSpace(identity)
            ? string.Empty
            : $"{normalizedKind}:{identity}";
    }

    private static string BuildLongTermMemoryKey(string kind, string subject, string summary)
    {
        string normalizedKind = NormalizeLongTermMemoryKind(kind);
        string normalizedSubject = NormalizeMemorySummary(subject);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string identity = string.IsNullOrWhiteSpace(normalizedSubject)
            ? normalizedSummary
            : normalizedSubject;
        return string.IsNullOrWhiteSpace(identity)
            ? string.Empty
            : $"{normalizedKind}:{identity}";
    }

    private static string BuildCommunityImpressionKey(string subjectNpcName, string kind, string summary)
    {
        string normalizedSubject = NormalizeMemorySummary(subjectNpcName);
        string normalizedKind = NormalizeCommunityImpressionKind(kind);
        string normalizedSummary = NormalizeMemorySummary(summary);
        return string.IsNullOrWhiteSpace(normalizedSubject) || string.IsNullOrWhiteSpace(normalizedSummary)
            ? string.Empty
            : $"{normalizedSubject}:{normalizedKind}:{normalizedSummary}";
    }

    internal static int NormalizeTimeOfDay(int value)
    {
        if (value <= 0)
        {
            return 900;
        }

        int hours = System.Math.Clamp(value / 100, 6, 26);
        int minutes = System.Math.Clamp(value % 100, 0, 50);
        minutes -= minutes % 10;
        return (hours * 100) + minutes;
    }

    internal static string NormalizeCommitmentLocation(string value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (CommitmentLocationAliases.TryGetValue(candidate, out string? mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(candidate) ? "Town" : candidate;
    }

    private static string GetCommitmentLocationLabel(string locationName)
    {
        return locationName switch
        {
            "Farm" => "the farm",
            "Town" => "Pelican Town",
            "Mountain" => "the mountain",
            "Beach" => "the beach",
            "Forest" => "Cindersap Forest",
            "BusStop" => "the bus stop",
            "Saloon" => "the Stardrop Saloon",
            "SeedShop" => "Pierre's General Store",
            "ArchaeologyHouse" => "the museum and library",
            "Hospital" => "the clinic",
            _ => locationName
        };
    }

    private static string BuildCommitmentKey(string type, string summary, int dueTotalDays, int timeOfDay, string locationName)
    {
        string normalizedType = NormalizeCommitmentType(type);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string normalizedLocation = NormalizeCommitmentLocation(locationName, string.Empty);
        return normalizedType == "none" || string.IsNullOrWhiteSpace(normalizedSummary)
            ? string.Empty
            : $"{normalizedType}:{normalizedSummary}:{dueTotalDays}:{timeOfDay}:{normalizedLocation}";
    }

    private static string BuildDialogueBehaviorInfluenceKey(string type, string summary, string targetLocation)
    {
        string normalizedType = NormalizeDialogueBehaviorInfluenceType(type);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string normalizedLocation = NormalizeCommitmentLocation(targetLocation, string.Empty);
        return normalizedType == "none" || string.IsNullOrWhiteSpace(normalizedSummary)
            ? string.Empty
            : $"{normalizedType}:{normalizedSummary}:{normalizedLocation}";
    }

    private static int CommitmentStatusOrder(string status)
    {
        return status switch
        {
            "Pending" => 0,
            "Expired" => 1,
            "Fulfilled" => 2,
            _ => 3
        };
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

    private static string GetConflictStatus(int severity)
    {
        return severity switch
        {
            <= 0 => "Resolved",
            < 30 => "Recovering",
            _ => "Active"
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

internal sealed class BehaviorMemorySaveData
{
    public Dictionary<string, List<BehaviorMemoryEntry>> EntriesByNpc { get; set; } = new();
    public Dictionary<string, LivingNpcState> StatesByNpc { get; set; } = new();
    public int LastStateDecayTotalDays { get; set; } = -1;
}

internal sealed class BehaviorMemoryEntry
{
    public string NpcName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Behavior";
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Season { get; set; } = string.Empty;
    public int Day { get; set; }
    public int TimeOfDay { get; set; }
    public int TotalDays { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string LocationDisplayName { get; set; } = string.Empty;
}

internal sealed record GiftMemoryDetails(
    string ItemId,
    string ItemName,
    string TasteLabel,
    string TastePromptLabel,
    int TasteScore
);

internal sealed record MemoryRecallContext(
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> Tokens
);

internal sealed record LongTermMemorySelection(
    LongTermMemoryFact Memory,
    int Score,
    string Reason
);

internal sealed record PlayerPreferenceSelection(
    PlayerPreferenceFact Memory,
    int Score,
    string Reason
);

internal sealed record CommunityImpressionSelection(
    CommunityImpressionFact Memory,
    int Score,
    string Reason
);

internal sealed record MemoryRecallPlan(
    MemoryRecallContext Context,
    IReadOnlyList<LongTermMemorySelection> LongTermMemories,
    IReadOnlyList<PlayerPreferenceSelection> PlayerPreferences
)
{
    public static MemoryRecallPlan Empty { get; } = new(
        new MemoryRecallContext(
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)),
        System.Array.Empty<LongTermMemorySelection>(),
        System.Array.Empty<PlayerPreferenceSelection>());
}

internal sealed class ValleyTalkExchangeAnalysis
{
    public int RapportDelta { get; set; }
    public bool EndConversation { get; set; }
    public ValleyTalkAmbientFollowUp AmbientFollowUp { get; set; } = new();
    public ValleyTalkEmotionImpact EmotionImpact { get; set; } = new();
    public List<ValleyTalkWorldActionRequest> Actions { get; set; } = new();
    public List<ValleyTalkBehaviorInfluenceCandidate> BehaviorInfluences { get; set; } = new();
    public List<ValleyTalkCommitmentCandidate> Commitments { get; set; } = new();
    public List<ValleyTalkHelpRequestCandidate> HelpRequests { get; set; } = new();
    public List<ValleyTalkHelpRequestUpdateCandidate> HelpRequestUpdates { get; set; } = new();
    public List<ValleyTalkConflictCandidate> Conflicts { get; set; } = new();
    public List<ValleyTalkMemoryCandidate> Memories { get; set; } = new();
}

internal sealed class ValleyTalkMemoryCandidate
{
    public string Kind { get; set; } = "fact";
    public string Summary { get; set; } = string.Empty;
    public int Importance { get; set; }
    public bool PlayerPreference { get; set; }
    public string PlayerPreferenceKind { get; set; } = "none";
    public string Subject { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

internal sealed class LongTermMemoryFact
{
    public string Kind { get; set; } = "fact";
    public string Subject { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; }
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastRecalledTotalDays { get; set; } = -1;
    public int LastRecalledTimeOfDay { get; set; }
    public int RecallCount { get; set; }
    public int TimesReinforced { get; set; }
}

internal sealed class PlayerPreferenceFact
{
    public string PreferenceKind { get; set; } = "none";
    public string Subject { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; }
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastRecalledTotalDays { get; set; } = -1;
    public int LastRecalledTimeOfDay { get; set; }
    public int RecallCount { get; set; }
    public int TimesReinforced { get; set; }
}

internal sealed class CommunityImpressionFact
{
    public string SubjectNpcName { get; set; } = string.Empty;
    public string SubjectDisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = "relationship_trend";
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = "CloseCircle";
    public string Visibility { get; set; } = "Public";
    public int Confidence { get; set; }
    public int Importance { get; set; }
    public int TransmissionDepth { get; set; }
    public int DistortionLevel { get; set; }
    public string HeardFromNpcName { get; set; } = string.Empty;
    public string CircleKey { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastRecalledTotalDays { get; set; } = -1;
    public int LastRecalledTimeOfDay { get; set; }
    public int LastSharedTotalDays { get; set; } = -1;
    public int LastSharedTimeOfDay { get; set; }
    public int ShareCount { get; set; }
    public int ExpiresTotalDays { get; set; } = -1;
    public int RecallCount { get; set; }
    public int TimesReinforced { get; set; }

    public string FreshnessStage
    {
        get
        {
            int age = this.LastUpdatedTotalDays < 0
                ? int.MaxValue
                : System.Math.Max(0, Game1.Date.TotalDays - this.LastUpdatedTotalDays);
            int remaining = this.ExpiresTotalDays < 0
                ? int.MaxValue
                : this.ExpiresTotalDays - Game1.Date.TotalDays;
            if (remaining < 0)
            {
                return "expired";
            }

            if (age <= 1)
            {
                return "fresh";
            }

            if (age <= 5 && remaining >= 2)
            {
                return "settled";
            }

            return "fading";
        }
    }

    public string PromptLabel => this.Source switch
    {
        "Witnessed" => $"directly witnessed, {this.FreshnessStage} ({this.Visibility.ToLowerInvariant()}): {this.Summary}",
        "CloseCircle" => $"heard through a close connection after {this.TransmissionDepth} retelling(s), {this.FreshnessStage} ({this.Visibility.ToLowerInvariant()}): {this.Summary}",
        _ => $"picked up as a faint public impression after {this.TransmissionDepth} retelling(s), {this.FreshnessStage} ({this.Visibility.ToLowerInvariant()}): {this.Summary}"
    };
}

internal sealed class NpcCommitmentFact
{
    public string Type { get; set; } = "meet_again";
    public string Summary { get; set; } = string.Empty;
    public int DueTotalDays { get; set; } = -1;
    public int TimeOfDay { get; set; } = 900;
    public string LocationName { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastMentionedTotalDays { get; set; } = -1;
    public int LastMentionedTimeOfDay { get; set; }
    public bool ArrivalGreetingShown { get; set; }
    public int FulfilledTotalDays { get; set; } = -1;
    public int FulfilledTimeOfDay { get; set; }
    public int FollowUpMentionedTotalDays { get; set; } = -1;
    public int FollowUpMentionedTimeOfDay { get; set; }
    public int DayBeforeReminderMentionedTotalDays { get; set; } = -1;
    public int DayBeforeReminderMentionedTimeOfDay { get; set; }
    public bool MorningReminderShown { get; set; }
    public bool RenewedAfterMiss { get; set; }
    public int RenewedTotalDays { get; set; } = -1;
    public int RenewedTimeOfDay { get; set; }
    public int TimesReinforced { get; set; }

    public string PromptLabel => $"{this.Type} at {this.LocationLabel} on total day {this.DueTotalDays} around {this.TimeOfDay}; status: {this.Status}; summary: {this.Summary}";
    public string FulfilledPromptLabel => $"{this.Type} at {this.LocationLabel} was fulfilled on total day {this.FulfilledTotalDays} around {this.FulfilledTimeOfDay}; summary: {this.Summary}";
}

internal sealed class SharedExperienceFact
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int Importance { get; set; }
    public int TimesReinforced { get; set; }
    public int FollowUpEligibleTotalDays { get; set; } = -1;
    public int FollowUpShownTotalDays { get; set; } = -1;
    public int FollowUpShownTimeOfDay { get; set; }

    public string PromptLabel =>
        $"{this.Type} at {this.LocationLabel}; shared on total day {this.CreatedTotalDays}; summary: {this.Summary}";
}

internal sealed class DialogueBehaviorInfluenceFact
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = string.Empty;
    public string TargetLocationLabel { get; set; } = string.Empty;
    public int Intensity { get; set; }
    public string Status { get; set; } = "Active";
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int ExpiresTotalDays { get; set; } = -1;
    public int LastTriggeredTotalDays { get; set; } = -1;
    public int LastTriggeredTimeOfDay { get; set; }
    public int TriggerCount { get; set; }
    public int MaxTriggers { get; set; } = 1;
    public int TimesReinforced { get; set; }

    public string PromptLabel =>
        $"{this.Type}, intensity {this.Intensity}/100, target {this.TargetLocationLabel}, status {this.Status}, expires total day {this.ExpiresTotalDays}; summary: {this.Summary}";
}

internal sealed class NpcHelpRequestFact
{
    public string NpcDisplayName { get; set; } = string.Empty;
    public string QuestLogId { get; set; } = string.Empty;
    public string Type { get; set; } = "item_request";
    public string Summary { get; set; } = string.Empty;
    public List<NpcHelpRequestStepFact> Steps { get; set; } = new();
    public int CurrentStepIndex { get; set; }
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public int DueTotalDays { get; set; } = -1;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Resolution { get; set; } = string.Empty;
    public string FollowUpPotential { get; set; } = "none";
    public string FailureReaction { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int AcceptedTotalDays { get; set; } = -1;
    public int AcceptedTimeOfDay { get; set; }
    public int DeclinedTotalDays { get; set; } = -1;
    public int DeclinedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastMentionedTotalDays { get; set; } = -1;
    public int LastMentionedTimeOfDay { get; set; }
    public int FulfilledTotalDays { get; set; } = -1;
    public int FulfilledTimeOfDay { get; set; }
    public int FollowUpEligibleTotalDays { get; set; } = -1;
    public int FollowUpShownTotalDays { get; set; } = -1;
    public int FollowUpShownTimeOfDay { get; set; }
    public int RewardFriendship { get; set; }
    public bool RewardGranted { get; set; }
    public bool RewardGiftGiven { get; set; }
    public int TimesReinforced { get; set; }

    public string PromptLabel =>
        $"{this.Type}, due on total day {this.DueTotalDays}, status {this.Status}, step {System.Math.Min(this.CurrentStepIndex + 1, System.Math.Max(1, this.Steps.Count))}/{System.Math.Max(1, this.Steps.Count)}; current step: {this.CurrentStepPromptLabel}; summary: {this.Summary}";

    public string FulfilledPromptLabel =>
        $"{this.Type} was fulfilled on total day {this.FulfilledTotalDays}; summary: {this.Summary}; follow-up potential: {this.FollowUpPotential}";

    public string CurrentStepPromptLabel
    {
        get
        {
            var step = this.Steps.Count == 0
                ? null
                : this.Steps[System.Math.Clamp(this.CurrentStepIndex, 0, this.Steps.Count - 1)];
            if (step == null)
            {
                return this.Type == "item_request"
                    ? $"bring {this.RequestedItemLabel} {this.RequestedItemId}".Trim()
                    : this.QuestionTopic;
            }

            return step.PromptLabel;
        }
    }
}

internal sealed class NpcHelpRequestStepFact
{
    public string Type { get; set; } = "item_request";
    public string Summary { get; set; } = string.Empty;
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Resolution { get; set; } = string.Empty;
    public int CompletedTotalDays { get; set; } = -1;
    public int CompletedTimeOfDay { get; set; }

    public string PromptLabel => this.Type == "item_request"
        ? $"item step: {this.Summary}; needs {this.RequestedItemLabel} {this.RequestedItemId}; status {this.Status}"
        : $"conversation step: {this.Summary}; topic {this.QuestionTopic}; status {this.Status}";
}

internal sealed class NpcConflictFact
{
    public string CauseKind { get; set; } = "dialogue";
    public string Summary { get; set; } = string.Empty;
    public int Severity { get; set; }
    public int PeakSeverity { get; set; }
    public string Status { get; set; } = "Active";
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int ResolvedTotalDays { get; set; } = -1;
    public int ResolvedTimeOfDay { get; set; }
    public int RecoveryMentionedTotalDays { get; set; } = -1;
    public int RecoveryMentionedTimeOfDay { get; set; }
    public int RepairScore { get; set; }
    public int ApologyCount { get; set; }
    public bool RequiresComplexRepair { get; set; }
    public string RepairStage { get; set; } = "Simple";
    public bool ApologyReceived { get; set; }
    public bool MeaningfulGiftReceived { get; set; }
    public bool SpecificRepairTalkReceived { get; set; }
    public int MinimumRepairTotalDays { get; set; } = -1;
    public string LastRepairGiftName { get; set; } = string.Empty;
    public bool RepairGrowthGranted { get; set; }
    public int TimesReinforced { get; set; }

    public string PromptLabel => $"{this.Status.ToLowerInvariant()} conflict, severity {this.Severity}/100, cause {this.CauseKind}, repair stage {this.RepairStage}: {this.Summary}";
    public string ResolvedPromptLabel => $"resolved conflict from total day {this.CreatedTotalDays}, cause {this.CauseKind}: {this.Summary}";
}

internal sealed class ValleyTalkAmbientFollowUp
{
    public string Text { get; set; } = string.Empty;
    public int DelayMinutes { get; set; }
}

internal sealed class ValleyTalkEmotionImpact
{
    public string Emotion { get; set; } = "none";
    public int IntensityDelta { get; set; }
    public bool Apology { get; set; }
    public int RepairDelta { get; set; }
    public string Reason { get; set; } = string.Empty;

    public bool HasEffect => this.Emotion != "none"
        || this.IntensityDelta != 0
        || this.Apology
        || this.RepairDelta > 0;
}

internal sealed class ValleyTalkWorldActionRequest
{
    public string Type { get; set; } = "none";
    public int Amount { get; set; }
    public int TileCount { get; set; }
    public int DurationMinutes { get; set; }
    public int DelayMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = string.Empty;
    public string QuestHint { get; set; } = string.Empty;
}

internal sealed class ValleyTalkBehaviorInfluenceCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = string.Empty;
    public string TargetLocationLabel { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public int Intensity { get; set; }
    public int MaxTriggers { get; set; }
}

internal sealed class ValleyTalkCommitmentCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public int DueInDays { get; set; }
    public int TimeOfDay { get; set; } = 900;
    public string Location { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
}

internal sealed class ValleyTalkHelpRequestCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public bool RequiresAcceptance { get; set; } = true;
    public List<ValleyTalkHelpRequestStepCandidate> Steps { get; set; } = new();
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public int DueInDays { get; set; } = 3;
    public string Reason { get; set; } = string.Empty;
    public string FollowUpPotential { get; set; } = "none";
}

internal sealed class ValleyTalkHelpRequestStepCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
}

internal sealed class ValleyTalkHelpRequestUpdateCandidate
{
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "none";
    public string Resolution { get; set; } = string.Empty;
}

internal sealed class ValleyTalkConflictCandidate
{
    public string CauseKind { get; set; } = "dialogue";
    public string Summary { get; set; } = string.Empty;
    public int Severity { get; set; }
}

internal sealed record ValleyTalkExchangeResult(
    int LongTermMemoriesStored,
    int PlayerPreferencesStored,
    int CommitmentsStored,
    int HelpRequestsStored,
    int HelpRequestsUpdated,
    int ConflictsStored,
    int BehaviorInfluencesStored,
    int ConflictsResolved,
    bool EmotionChanged,
    int AppliedFriendshipDelta,
    int RequestedFriendshipDelta,
    bool EndConversation,
    string AmbientFollowUpText,
    int AmbientFollowUpDelayMinutes,
    IReadOnlyList<ValleyTalkWorldActionRequest> Actions,
    IReadOnlyList<NpcHelpRequestFact> FulfilledHelpRequests
)
{
    public bool HasEffect => this.LongTermMemoriesStored > 0
        || this.PlayerPreferencesStored > 0
        || this.CommitmentsStored > 0
        || this.HelpRequestsStored > 0
        || this.HelpRequestsUpdated > 0
        || this.ConflictsStored > 0
        || this.BehaviorInfluencesStored > 0
        || this.ConflictsResolved > 0
        || this.EmotionChanged
        || this.AppliedFriendshipDelta > 0
        || !string.IsNullOrWhiteSpace(this.AmbientFollowUpText)
        || this.Actions.Count > 0
        || this.FulfilledHelpRequests.Count > 0;
}

internal sealed class LivingNpcState
{
    public string NpcName { get; set; } = string.Empty;
    public string Mood { get; set; } = "Neutral";
    public string CurrentEmotion { get; set; } = "Calm";
    public int EmotionIntensity { get; set; }
    public string LastEmotionReason { get; set; } = "none";
    public int LastEmotionUpdatedTotalDays { get; set; } = -1;
    public int LastEmotionUpdatedTimeOfDay { get; set; }
    public int Attention { get; set; } = 35;
    public int Openness { get; set; } = 50;
    public int Familiarity { get; set; }
    public int FamiliarityGainedToday { get; set; }
    public int LastFamiliarityGainTotalDays { get; set; } = -1;
    public int ConversationsToday { get; set; }
    public int ConsecutiveConversationDays { get; set; }
    public int LastConversationTotalDays { get; set; } = -1;
    public int LastConversationTimeOfDay { get; set; }
    public int LastConversationGapDays { get; set; } = -1;
    public string InteractionRhythm { get; set; } = "New";
    public string InteractionComfortTier { get; set; } = "Distant";
    public int DailyConversationComfortLimit { get; set; } = 2;
    public int RepeatedConversationPressure { get; set; }
    public int LastFriendshipHearts { get; set; }
    public string LastGiftName { get; set; } = string.Empty;
    public string LastGiftTaste { get; set; } = string.Empty;
    public int LastGiftTotalDays { get; set; } = -1;
    public int LastGiftTimeOfDay { get; set; }
    public int GiftsToday { get; set; }
    public string LastEventContext { get; set; } = string.Empty;
    public int LastEventTotalDays { get; set; } = -1;
    public int LastEventTimeOfDay { get; set; }
    public List<LongTermMemoryFact> LongTermMemories { get; set; } = new();
    public List<PlayerPreferenceFact> PlayerPreferenceMemories { get; set; } = new();
    public List<CommunityImpressionFact> CommunityImpressions { get; set; } = new();
    public List<NpcCommitmentFact> Commitments { get; set; } = new();
    public List<SharedExperienceFact> SharedExperiences { get; set; } = new();
    public List<DialogueBehaviorInfluenceFact> DialogueBehaviorInfluences { get; set; } = new();
    public List<NpcHelpRequestFact> HelpRequests { get; set; } = new();
    public List<NpcConflictFact> Conflicts { get; set; } = new();
    public bool RelationshipTrustInitialized { get; set; }
    public int RelationshipTrust { get; set; } = 20;
    public int LastRelationshipTrustUpdatedTotalDays { get; set; } = -1;
    public int LastRelationshipTrustUpdatedTimeOfDay { get; set; }
    public int CommitmentTrust { get; set; } = 50;
    public int MissedCommitments { get; set; }
    public int ConsecutiveMissedCommitments { get; set; }
    public int LastMissedCommitmentTotalDays { get; set; } = -1;
    public int LastMissedCommitmentTimeOfDay { get; set; }
    public int AiFriendshipGainedToday { get; set; }
    public int LastAiFriendshipTotalDays { get; set; } = -1;
    public int LastAiSmallGiftTotalDays { get; set; } = -1;
    public int LastAiMeaningfulGiftTotalDays { get; set; } = -1;
    public int LastAiMoneyGiftTotalDays { get; set; } = -1;
    public int LastAiFarmHelpTotalDays { get; set; } = -1;
    public int LastAiWalkTogetherTotalDays { get; set; } = -1;
    public int LastHelpRequestTotalDays { get; set; } = -1;
    public int LastHelpRequestTimeOfDay { get; set; }
    public string LastSceneContext { get; set; } = "none";
    public string LastSceneInfluence { get; set; } = "none";
    public string LastSceneInfluenceReason { get; set; } = "none";
    public string CurrentInclination { get; set; } = "Neutral";
    public string LastInteraction { get; set; } = "none yet";
    public string FarmerNickname { get; set; } = string.Empty;
    public string FarmerNicknameStatus { get; set; } = string.Empty;
    public int FarmerNicknameTotalDays { get; set; } = -1;
    public int FarmerNicknameTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; }
    public int LastUpdatedTimeOfDay { get; set; }

    public string MoodLabel => this.Mood switch
    {
        "Aware" => "注意到周围",
        "Attentive" => "专注",
        "Calm" => "放松",
        "Careful" => "谨慎",
        "Chilly" => "有些怕冷",
        "Comfortable" => "自在",
        "CrowdedButWarm" => "亲近但有点频繁",
        "Curious" => "好奇",
        "Delighted" => "非常高兴",
        "Engaged" => "投入",
        "EventAware" => "留意活动气氛",
        "Expressive" => "情绪外露",
        "Familiar" => "熟悉",
        "Focused" => "专注于事务",
        "Fresh" => "精神不错",
        "Guarded" => "警觉",
        "Hurried" => "匆忙",
        "Overloaded" => "有点应接不暇",
        "Pleased" => "高兴",
        "Polite" => "礼貌克制",
        "Public" => "留意公共场合",
        "Quiet" => "安静",
        "Sociable" => "有社交兴致",
        "Surprised" => "有些意外",
        "Upset" => "不太高兴",
        "Awkward" => "有些尴尬",
        "GiftAware" => "注意到礼物",
        "Warm" => "温和",
        _ => "普通"
    };

    public string EmotionLabel => this.CurrentEmotion switch
    {
        "Happy" => $"开心（{this.EmotionIntensity}）",
        "Jealous" => $"吃醋（{this.EmotionIntensity}）",
        "Worried" => $"担心（{this.EmotionIntensity}）",
        "Grateful" => $"感激（{this.EmotionIntensity}）",
        "Disappointed" => $"失望（{this.EmotionIntensity}）",
        "Uneasy" => $"有些不自在（{this.EmotionIntensity}）",
        "Upset" => $"不悦（{this.EmotionIntensity}）",
        "Angry" => $"生气（{this.EmotionIntensity}）",
        "Sad" => $"伤心（{this.EmotionIntensity}）",
        _ => $"平静（{this.EmotionIntensity}）"
    };

    public string EmotionPromptLabel => this.CurrentEmotion switch
    {
        "Happy" => $"happy, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Jealous" => $"jealous, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Worried" => $"worried, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Grateful" => $"grateful, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Disappointed" => $"disappointed, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Uneasy" => $"uneasy, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Upset" => $"upset, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Angry" => $"angry, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Sad" => $"sad, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        _ => $"calm, intensity {this.EmotionIntensity}/100"
    };

    public bool HasUnresolvedConflict => this.Conflicts.Any(conflict => conflict.Status is "Active" or "Recovering");

    public int HighestUnresolvedConflictSeverity => this.Conflicts
        .Where(conflict => conflict.Status is "Active" or "Recovering")
        .Select(conflict => conflict.Severity)
        .DefaultIfEmpty(0)
        .Max();

    public string FamiliarityLabel => this.Familiarity switch
    {
        >= 75 => "亲近",
        >= 45 => "熟悉",
        >= 18 => "眼熟",
        _ => "刚认识"
    };

    public string FamiliarityPromptLabel => this.Familiarity switch
    {
        >= 75 => "close and comfortable",
        >= 45 => "familiar",
        >= 18 => "recognizes the farmer",
        _ => "new or barely familiar"
    };

    public string AttentionLabel => this.Attention switch
    {
        >= 75 => "高",
        >= 45 => "中",
        _ => "低"
    };

    public string InclinationLabel => this.CurrentInclination switch
    {
        "Acknowledging" => "会简单回应",
        "Aware" => "注意到玩家",
        "Businesslike" => "偏事务性回应",
        "Careful" => "谨慎回应",
        "Comfortable" => "自在回应",
        "Focused" => "保持专注",
        "GentleBoundary" => "温和地保留空间",
        "Measured" => "礼貌但有分寸",
        "NeedsSpace" => "需要一点空间",
        "OpenToTalk" => "愿意继续回应",
        "Public" => "顾及周围的人",
        "Quiet" => "安静回应",
        "Reacting" => "正在反应",
        "Reconnecting" => "重新熟悉",
        "Reserved" => "保守回应",
        "Sheltering" => "想避开天气",
        _ => "普通"
    };

    public string InteractionRhythmLabel => this.InteractionRhythm switch
    {
        "AfterLongGap" => $"隔了 {this.LastConversationGapDays} 天才再次聊天",
        "AtComfortLimit" => $"今天第 {this.ConversationsToday} 次聊天，接近日常舒适上限",
        "BuildingRoutine" => $"连续 {this.ConsecutiveConversationDays} 天打招呼",
        "CheckedInAgain" => $"今天第 {this.ConversationsToday} 次聊天",
        "ComfortableRepeat" => $"今天第 {this.ConversationsToday} 次聊天，关系足够熟所以仍然自然",
        "CrowdedToday" => $"今天已经聊了 {this.ConversationsToday} 次，有点频繁",
        "DailyRoutine" => $"连续 {this.ConsecutiveConversationDays} 天聊天，像日常习惯",
        "FirstConversation" => "第一次记录到对话",
        "FreshToday" => "今天第一次聊天",
        "LongQuietGap" => $"已经 {this.LastConversationGapDays} 天没有聊天",
        "NoConversationToday" => this.LastConversationGapDays <= 1
            ? "今天还没聊天，昨天聊过"
            : $"今天还没聊天，上次聊天在 {this.LastConversationGapDays} 天前",
        "PoliteRepeat" => $"今天第 {this.ConversationsToday} 次聊天，关系还不深所以会更客气",
        _ => "暂无稳定节奏"
    };

    public string InteractionComfortTierLabel => this.InteractionComfortTier switch
    {
        "Intimate" => $"非常亲近（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        "Trusted" => $"亲近（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        "Friendly" => $"友好（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        "Familiar" => $"熟悉（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        _ => $"不熟（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）"
    };

    public string InteractionComfortTierPromptLabel => this.InteractionComfortTier switch
    {
        "Intimate" => $"very close; {this.LastFriendshipHearts} hearts; up to {this.DailyConversationComfortLimit} short conversations today can feel normal",
        "Trusted" => $"trusted; {this.LastFriendshipHearts} hearts; up to {this.DailyConversationComfortLimit} short conversations today can still feel natural",
        "Friendly" => $"friendly; {this.LastFriendshipHearts} hearts; repeated conversations are acceptable but should not feel endlessly eager",
        "Familiar" => $"familiar; {this.LastFriendshipHearts} hearts; repeated conversations should stay polite and modest",
        _ => $"distant; {this.LastFriendshipHearts} hearts; repeated conversations should feel more cautious or formal"
    };

    public string InteractionRhythmPromptLabel => this.InteractionRhythm switch
    {
        "AfterLongGap" => $"they are speaking again after {this.LastConversationGapDays} days without a recorded conversation",
        "AtComfortLimit" => $"this is conversation {this.ConversationsToday} today, around the normal comfort limit for this relationship",
        "BuildingRoutine" => $"the farmer has checked in for {this.ConsecutiveConversationDays} consecutive days",
        "CheckedInAgain" => $"this is conversation {this.ConversationsToday} with the farmer today",
        "ComfortableRepeat" => $"this is conversation {this.ConversationsToday} today, but the relationship is close enough that it can still feel natural",
        "CrowdedToday" => $"the farmer has already spoken with them {this.ConversationsToday} times today, so the attention may feel repetitive",
        "DailyRoutine" => $"the farmer has spoken with them for {this.ConsecutiveConversationDays} consecutive days, forming a familiar daily rhythm",
        "FirstConversation" => "this is the first recorded LivingNPCs conversation with the farmer",
        "FreshToday" => "this is the first recorded conversation with the farmer today",
        "LongQuietGap" => $"there has been no recorded conversation with the farmer for {this.LastConversationGapDays} days",
        "NoConversationToday" => this.LastConversationGapDays <= 1
            ? "there has been no recorded conversation with the farmer today, but they spoke yesterday"
            : $"there has been no recorded conversation with the farmer today; the last one was {this.LastConversationGapDays} days ago",
        "PoliteRepeat" => $"this is conversation {this.ConversationsToday} today, and the relationship is not close enough for repeated chats to feel fully casual",
        _ => "no stable interaction rhythm yet"
    };

    public string LastSceneInfluenceLabel => this.LastSceneInfluence switch
    {
        "none" => "暂无",
        _ => this.LastSceneInfluence
    };

    public string LastGiftLabel => string.IsNullOrWhiteSpace(this.LastGiftName)
        ? "暂无"
        : $"{this.LastGiftName}（{this.LastGiftTaste}，第 {this.LastGiftTotalDays} 天 {this.LastGiftTimeOfDay}，今天第 {this.GiftsToday} 次礼物记录）";

    public string LastGiftPromptLabel => string.IsNullOrWhiteSpace(this.LastGiftName)
        ? "no recent LivingNPCs gift memory"
        : $"last recorded gift: the farmer offered {this.LastGiftName}; gift taste: {this.LastGiftTaste}; gifts recorded today: {this.GiftsToday}";

    public string LastEventLabel => string.IsNullOrWhiteSpace(this.LastEventContext)
        ? "暂无"
        : $"{this.LastEventContext}（第 {this.LastEventTotalDays} 天 {this.LastEventTimeOfDay}）";

    public string LastEventPromptLabel => string.IsNullOrWhiteSpace(this.LastEventContext)
        ? "no recent LivingNPCs event memory"
        : $"last recorded event context: {this.LastEventContext}";

    public string LongTermMemoryPromptLabel
    {
        get
        {
            var memories = this.GetTopLongTermMemories(4).ToList();
            return memories.Count == 0
                ? "no durable personal memory has been recorded"
                : string.Join("; ", memories.Select(memory => memory.Summary));
        }
    }

    public string LongTermMemoryDebugLabel
    {
        get
        {
            var memories = this.GetTopLongTermMemories(4).ToList();
            return memories.Count == 0
                ? "暂无"
                : string.Join("；", memories.Select(memory => $"{memory.Summary}（重要度 {memory.Importance}）"));
        }
    }

    public string PlayerPreferencePromptLabel
    {
        get
        {
            var preferences = this.GetTopPlayerPreferences(6).ToList();
            return preferences.Count == 0
                ? "no durable farmer preference memory has been recorded"
                : string.Join("; ", preferences.Select(memory => memory.Summary));
        }
    }

    public string PlayerPreferenceDebugLabel
    {
        get
        {
            var preferences = this.GetTopPlayerPreferences(6).ToList();
            return preferences.Count == 0
                ? "暂无"
                : string.Join("；", preferences.Select(memory => $"{memory.Summary}（{memory.PreferenceKind}，重要度 {memory.Importance}）"));
        }
    }

    public string CommunityImpressionPromptLabel
    {
        get
        {
            var memories = this.GetTopCommunityImpressions(4).ToList();
            return memories.Count == 0
                ? "no community impression about the farmer has been recorded"
                : string.Join("; ", memories.Select(memory => memory.PromptLabel));
        }
    }

    public string CommunityImpressionDebugLabel
    {
        get
        {
            var memories = this.GetTopCommunityImpressions(4).ToList();
            return memories.Count == 0
                ? "暂无"
                : string.Join("；", memories.Select(memory =>
                    $"{memory.Summary}（{memory.Source}/{memory.Visibility}，{memory.FreshnessStage}，转述 {memory.TransmissionDepth} 次，失真 {memory.DistortionLevel}，重要度 {memory.Importance}）"));
        }
    }

    public string CommitmentPromptLabel
    {
        get
        {
            var commitments = this.GetTopCommitments(4).ToList();
            return commitments.Count == 0
                ? "no durable commitments are recorded"
                : string.Join("; ", commitments.Select(commitment => commitment.PromptLabel));
        }
    }

    public string SharedExperiencePromptLabel
    {
        get
        {
            var experiences = this.GetTopSharedExperiences(4).ToList();
            return experiences.Count == 0
                ? "no shared commitment-based experiences are recorded"
                : string.Join("; ", experiences.Select(experience => experience.PromptLabel));
        }
    }

    public string SharedExperienceDebugLabel
    {
        get
        {
            var experiences = this.GetTopSharedExperiences(4).ToList();
            return experiences.Count == 0
                ? "暂无"
                : string.Join("；", experiences.Select(experience =>
                    $"{experience.Summary}（{experience.Type}，{experience.LocationLabel}，第 {experience.CreatedTotalDays} 天）"));
        }
    }

    public IEnumerable<DialogueBehaviorInfluenceFact> ActiveDialogueBehaviorInfluences =>
        this.DialogueBehaviorInfluences.Where(influence =>
            influence.Status == "Active"
            && influence.ExpiresTotalDays >= Game1.Date.TotalDays
            && influence.TriggerCount < System.Math.Max(1, influence.MaxTriggers));

    public string DialogueBehaviorInfluencePromptLabel
    {
        get
        {
            var influences = this.ActiveDialogueBehaviorInfluences.Take(4).ToList();
            return influences.Count == 0
                ? "no active conversation-driven behavior tendency"
                : string.Join("; ", influences.Select(influence => influence.PromptLabel));
        }
    }

    public string DialogueBehaviorInfluenceDebugLabel
    {
        get
        {
            var influences = this.DialogueBehaviorInfluences
                .OrderBy(influence => BehaviorMemory.DialogueBehaviorInfluenceStatusOrder(influence.Status))
                .ThenBy(influence => influence.ExpiresTotalDays)
                .Take(4)
                .ToList();
            return influences.Count == 0
                ? "暂无"
                : string.Join("；", influences.Select(influence =>
                    $"{influence.Summary}（{influence.Type}，{influence.Status}，触发 {influence.TriggerCount}/{influence.MaxTriggers}，到第 {influence.ExpiresTotalDays} 天）"));
        }
    }

    public string CommitmentTrustPromptLabel => this.CommitmentTrust switch
    {
        >= 75 => $"high trust in keeping plans ({this.CommitmentTrust}/100)",
        >= 55 => $"steady trust in keeping plans ({this.CommitmentTrust}/100)",
        >= 35 => $"uncertain trust in keeping plans ({this.CommitmentTrust}/100)",
        _ => $"low trust in keeping plans ({this.CommitmentTrust}/100)"
    };

    public string CommitmentTrustDebugLabel =>
        $"{this.CommitmentTrust}/100，累计失约 {this.MissedCommitments} 次，连续失约 {this.ConsecutiveMissedCommitments} 次";

    public string HelpRequestPromptLabel
    {
        get
        {
            var requests = this.GetTopHelpRequests(4).ToList();
            return requests.Count == 0
                ? "no durable help requests are recorded"
                : string.Join("; ", requests.Select(request => request.PromptLabel));
        }
    }

    public string HelpRequestDebugLabel
    {
        get
        {
            var requests = this.GetTopHelpRequests(4).ToList();
            return requests.Count == 0
                ? "暂无"
                : string.Join("；", requests.Select(request =>
                    $"{request.Summary}（{request.Type}，截止第 {request.DueTotalDays} 天，{request.Status}）"));
        }
    }

    public string RelationshipTrustPromptLabel => this.RelationshipTrust switch
    {
        >= 80 => $"deep interpersonal trust ({this.RelationshipTrust}/100)",
        >= 60 => $"steady interpersonal trust ({this.RelationshipTrust}/100)",
        >= 35 => $"tentative interpersonal trust ({this.RelationshipTrust}/100)",
        _ => $"low interpersonal trust ({this.RelationshipTrust}/100)"
    };

    public string RelationshipTrustDebugLabel => $"{this.RelationshipTrust}/100";

    public string SecretSharingPromptLabel => this.RelationshipTrust switch
    {
        >= 80 => "deep trust; private hopes, fears, and history may surface naturally when the scene truly supports it",
        >= 60 => "steady trust; some vulnerable personal details may be shared when relevant",
        >= 35 => "limited trust; light personal details are fine, but deeper secrets should stay mostly guarded",
        _ => "low trust; keep disclosures surface-level and avoid volunteering private secrets"
    };

    public string CommitmentDebugLabel
    {
        get
        {
            var commitments = this.GetTopCommitments(4).ToList();
            return commitments.Count == 0
                ? "暂无"
                : string.Join("；", commitments.Select(commitment =>
                    $"{commitment.Summary}（{commitment.Type}，{commitment.LocationLabel}，第 {commitment.DueTotalDays} 天 {commitment.TimeOfDay}，{commitment.Status}）"));
        }
    }

    public string ConflictPromptLabel
    {
        get
        {
            var conflicts = this.GetTopConflicts(4).ToList();
            return conflicts.Count == 0
                ? "no durable conflict memory has been recorded"
                : string.Join("; ", conflicts.Select(conflict => conflict.PromptLabel));
        }
    }

    public string ConflictDebugLabel
    {
        get
        {
            var conflicts = this.GetTopConflicts(4).ToList();
            return conflicts.Count == 0
                ? "暂无"
                : string.Join("；", conflicts.Select(conflict =>
                    $"{conflict.Summary}（{conflict.CauseKind}，严重度 {conflict.Severity}，{conflict.Status}，修复阶段 {conflict.RepairStage}）"));
        }
    }

    public string FarmerNicknamePromptLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.FarmerNickname))
            {
                return "no personal name preference has been recorded";
            }

            return this.FarmerNicknameStatus switch
            {
                "Accepted" => $"the farmer asked to be called {this.FarmerNickname}, and this NPC accepted; when choosing to address the farmer, use that name instead of @ or the save-file name, and never combine both names in one reply",
                "Rejected" => $"the farmer asked to be called {this.FarmerNickname}, but this NPC did not accept; do not use that name unless the relationship later changes",
                _ => $"the farmer asked to be called {this.FarmerNickname}; acceptance is unclear, so the NPC may decide whether to use it based on personality and relationship"
            };
        }
    }

    public string FarmerNicknameLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.FarmerNickname))
            {
                return "暂无";
            }

            return this.FarmerNicknameStatus switch
            {
                "Accepted" => $"已接受称呼“{this.FarmerNickname}”",
                "Rejected" => $"未接受称呼“{this.FarmerNickname}”",
                _ => $"玩家请求称呼“{this.FarmerNickname}”，是否接受尚不明确"
            };
        }
    }

    public string LastInteractionLabel => this.LastInteraction switch
    {
        "none yet" => "暂无",
        "passive nearby reaction" => "附近发生了一次自然反应",
        "small behavior near the farmer" => "刚在玩家附近做过小动作",
        "the farmer started a conversation" => "玩家刚主动开始对话",
        "the farmer caused interpersonal friction" => "玩家刚造成了一次关系摩擦",
        "the farmer helped repair a conflict" => "玩家刚缓和了一次冲突",
        _ when this.LastInteraction.StartsWith("the farmer helped with a personal request", System.StringComparison.Ordinal) => "玩家刚帮忙完成了一次主动求助",
        _ when this.LastInteraction.StartsWith("the farmer declined a personal help request", System.StringComparison.Ordinal) => "玩家刚拒绝了一次主动求助",
        _ when this.LastInteraction.StartsWith("a personal help request went unanswered", System.StringComparison.Ordinal) => "一次主动求助没有得到回应",
        "time passed" => "时间过去，状态回落",
        _ => this.LastInteraction
    };

    public static int ClampScore(int value)
    {
        return System.Math.Clamp(value, 0, 100);
    }

    public static int MoveToward(int value, int target, int amount)
    {
        if (amount <= 0 || value == target)
        {
            return value;
        }

        return value < target
            ? System.Math.Min(value + amount, target)
            : System.Math.Max(value - amount, target);
    }

    public void Clamp()
    {
        this.Attention = ClampScore(this.Attention);
        this.Openness = ClampScore(this.Openness);
        this.Familiarity = ClampScore(this.Familiarity);
        this.RelationshipTrust = ClampScore(this.RelationshipTrust);
        this.CurrentEmotion = BehaviorMemory.NormalizeEmotion(this.CurrentEmotion);
        if (this.CurrentEmotion == "none")
        {
            this.CurrentEmotion = "Calm";
        }

        this.EmotionIntensity = ClampScore(this.EmotionIntensity);
        this.FamiliarityGainedToday = System.Math.Clamp(this.FamiliarityGainedToday, 0, 100);
        this.ConversationsToday = System.Math.Max(0, this.ConversationsToday);
        this.ConsecutiveConversationDays = System.Math.Max(0, this.ConsecutiveConversationDays);
        this.LastConversationGapDays = this.LastConversationGapDays < -1 ? -1 : this.LastConversationGapDays;
        this.DailyConversationComfortLimit = System.Math.Clamp(this.DailyConversationComfortLimit <= 0 ? 2 : this.DailyConversationComfortLimit, 1, 8);
        this.RepeatedConversationPressure = System.Math.Clamp(this.RepeatedConversationPressure, 0, 100);
        this.LastFriendshipHearts = System.Math.Clamp(this.LastFriendshipHearts, 0, 14);
        this.GiftsToday = System.Math.Max(0, this.GiftsToday);
        this.LongTermMemories ??= new List<LongTermMemoryFact>();
        this.LongTermMemories = this.LongTermMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(BehaviorMemory.NormalizeLongTermMemoryForStore)
            .OrderByDescending(BehaviorMemory.GetLongTermMemoryRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(BehaviorMemory.MaxLongTermMemoriesPerNpc)
            .ToList();
        this.PlayerPreferenceMemories ??= new List<PlayerPreferenceFact>();
        this.PlayerPreferenceMemories = this.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(BehaviorMemory.NormalizePlayerPreferenceMemoryForStore)
            .Where(memory => memory.PreferenceKind != "none")
            .OrderByDescending(BehaviorMemory.GetPlayerPreferenceRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(BehaviorMemory.MaxPlayerPreferenceMemoriesPerNpc)
            .ToList();
        this.CommunityImpressions ??= new List<CommunityImpressionFact>();
        this.CommunityImpressions = this.CommunityImpressions
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.SubjectNpcName)
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && (memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= Game1.Date.TotalDays))
            .Select(BehaviorMemory.NormalizeCommunityImpressionForStore)
            .OrderByDescending(BehaviorMemory.GetCommunityImpressionRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(BehaviorMemory.MaxCommunityImpressionsPerNpc)
            .ToList();
        this.Commitments ??= new List<NpcCommitmentFact>();
        this.Commitments = this.Commitments
            .Where(commitment => commitment != null && !string.IsNullOrWhiteSpace(commitment.Summary))
            .Select(commitment =>
            {
                commitment.Type = BehaviorMemory.NormalizeCommitmentType(commitment.Type);
                commitment.Summary = commitment.Summary.Trim();
                commitment.TimeOfDay = BehaviorMemory.NormalizeTimeOfDay(commitment.TimeOfDay);
                commitment.LocationName = BehaviorMemory.NormalizeCommitmentLocation(commitment.LocationName, "Town");
                commitment.LocationLabel = string.IsNullOrWhiteSpace(commitment.LocationLabel)
                    ? commitment.LocationName
                    : commitment.LocationLabel.Trim();
                commitment.Status = commitment.Status switch
                {
                    "Fulfilled" => "Fulfilled",
                    "Expired" => "Expired",
                    _ => "Pending"
                };
                if (commitment.Status == "Fulfilled" && commitment.FulfilledTotalDays < 0)
                {
                    commitment.FulfilledTotalDays = commitment.LastUpdatedTotalDays;
                    commitment.FulfilledTimeOfDay = commitment.LastUpdatedTimeOfDay;
                }

                commitment.TimesReinforced = System.Math.Max(0, commitment.TimesReinforced);
                return commitment;
            })
            .Where(commitment => commitment.Type != "none")
            .OrderBy(commitment => commitment.Status switch
            {
                "Pending" => 0,
                "Expired" => 1,
                "Fulfilled" => 2,
                _ => 3
            })
            .ThenBy(commitment => commitment.DueTotalDays)
            .ThenBy(commitment => commitment.TimeOfDay)
            .Take(12)
            .ToList();
        this.HelpRequests ??= new List<NpcHelpRequestFact>();
        this.HelpRequests = this.HelpRequests
            .Where(request => request != null && !string.IsNullOrWhiteSpace(request.Summary))
            .Select(request =>
            {
                request.Type = BehaviorMemory.NormalizeHelpRequestType(request.Type);
                request.NpcDisplayName = request.NpcDisplayName?.Trim() ?? string.Empty;
                request.QuestLogId = string.IsNullOrWhiteSpace(request.QuestLogId)
                    ? System.Guid.NewGuid().ToString("N")
                    : request.QuestLogId.Trim();
                request.Summary = request.Summary.Trim();
                request.RequestedItemId = request.RequestedItemId?.Trim() ?? string.Empty;
                request.RequestedItemLabel = request.RequestedItemLabel?.Trim() ?? string.Empty;
                request.QuestionTopic = request.QuestionTopic?.Trim() ?? string.Empty;
                request.Reason = request.Reason?.Trim() ?? string.Empty;
                request.FollowUpPotential = BehaviorMemory.NormalizeHelpRequestFollowUpPotential(request.FollowUpPotential);
                request.FailureReaction = request.FailureReaction?.Trim() ?? string.Empty;
                request.Steps ??= new List<NpcHelpRequestStepFact>();
                request.Steps = request.Steps
                    .Where(step => step != null)
                    .Select(step =>
                    {
                        step.Type = BehaviorMemory.NormalizeHelpRequestType(step.Type);
                        step.Summary = step.Summary?.Trim() ?? string.Empty;
                        step.RequestedItemId = step.RequestedItemId?.Trim() ?? string.Empty;
                        step.RequestedItemLabel = step.RequestedItemLabel?.Trim() ?? string.Empty;
                        step.QuestionTopic = step.QuestionTopic?.Trim() ?? string.Empty;
                        step.Status = step.Status == "Fulfilled" ? "Fulfilled" : "Pending";
                        step.Resolution = step.Resolution?.Trim() ?? string.Empty;
                        return step;
                    })
                    .Where(step => step.Type != "none" && !string.IsNullOrWhiteSpace(step.Summary))
                    .Take(3)
                    .ToList();
                request.Status = request.Status switch
                {
                    "Offered" => "Offered",
                    "Fulfilled" => "Fulfilled",
                    "Expired" => "Expired",
                    "Declined" => "Declined",
                    _ => "Pending"
                };
                request.CurrentStepIndex = System.Math.Clamp(request.CurrentStepIndex, 0, System.Math.Max(0, request.Steps.Count - 1));
                if (request.Steps.Count == 0)
                {
                    request.Steps.Add(new NpcHelpRequestStepFact
                    {
                        Type = request.Type,
                        Summary = request.Summary,
                        RequestedItemId = request.RequestedItemId,
                        RequestedItemLabel = request.RequestedItemLabel,
                        QuestionTopic = request.QuestionTopic,
                        Status = request.Status == "Fulfilled" ? "Fulfilled" : "Pending",
                        Resolution = request.Resolution,
                        CompletedTotalDays = request.FulfilledTotalDays,
                        CompletedTimeOfDay = request.FulfilledTimeOfDay
                    });
                }

                var currentStep = request.Steps[request.CurrentStepIndex];
                request.Type = currentStep.Type;
                request.RequestedItemId = currentStep.RequestedItemId;
                request.RequestedItemLabel = currentStep.RequestedItemLabel;
                request.QuestionTopic = currentStep.QuestionTopic;
                request.TimesReinforced = System.Math.Max(0, request.TimesReinforced);
                request.RewardFriendship = request.RewardFriendship <= 0
                    ? 50
                    : System.Math.Clamp(request.RewardFriendship, 0, 100);
                return request;
            })
            .Where(request => request.Type != "none")
            .OrderBy(request => BehaviorMemory.HelpRequestStatusOrder(request.Status))
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        this.SharedExperiences ??= new List<SharedExperienceFact>();
        this.SharedExperiences = this.SharedExperiences
            .Where(experience => experience != null && !string.IsNullOrWhiteSpace(experience.Summary))
            .Select(experience =>
            {
                experience.Type = BehaviorMemory.NormalizeCommitmentType(experience.Type);
                experience.Summary = experience.Summary.Trim();
                experience.LocationName = BehaviorMemory.NormalizeCommitmentLocation(experience.LocationName, "Town");
                experience.LocationLabel = string.IsNullOrWhiteSpace(experience.LocationLabel)
                    ? experience.LocationName
                    : experience.LocationLabel.Trim();
                experience.Key = string.IsNullOrWhiteSpace(experience.Key)
                    ? $"{experience.Type}:{experience.Summary}:{experience.LocationName}"
                    : experience.Key;
                experience.Importance = ClampScore(experience.Importance);
                experience.TimesReinforced = System.Math.Max(0, experience.TimesReinforced);
                return experience;
            })
            .Where(experience => experience.Type != "none")
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(12)
            .ToList();
        this.DialogueBehaviorInfluences ??= new List<DialogueBehaviorInfluenceFact>();
        this.DialogueBehaviorInfluences = this.DialogueBehaviorInfluences
            .Where(influence => influence != null && !string.IsNullOrWhiteSpace(influence.Summary))
            .Select(influence =>
            {
                influence.Type = BehaviorMemory.NormalizeDialogueBehaviorInfluenceType(influence.Type);
                influence.Summary = influence.Summary.Trim();
                influence.TargetLocation = BehaviorMemory.NormalizeCommitmentLocation(influence.TargetLocation, "Town");
                influence.TargetLocationLabel = string.IsNullOrWhiteSpace(influence.TargetLocationLabel)
                    ? influence.TargetLocation
                    : influence.TargetLocationLabel.Trim();
                influence.Intensity = ClampScore(influence.Intensity);
                influence.Status = influence.Status switch
                {
                    "Spent" => "Spent",
                    "Expired" => "Expired",
                    _ => "Active"
                };
                if (influence.Status == "Active" && influence.ExpiresTotalDays < Game1.Date.TotalDays)
                {
                    influence.Status = "Expired";
                }

                influence.TriggerCount = System.Math.Max(0, influence.TriggerCount);
                influence.MaxTriggers = System.Math.Clamp(influence.MaxTriggers <= 0 ? 1 : influence.MaxTriggers, 1, 4);
                influence.TimesReinforced = System.Math.Max(0, influence.TimesReinforced);
                return influence;
            })
            .Where(influence => influence.Type != "none")
            .OrderBy(influence => BehaviorMemory.DialogueBehaviorInfluenceStatusOrder(influence.Status))
            .ThenBy(influence => influence.ExpiresTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTimeOfDay)
            .Take(BehaviorMemory.MaxDialogueBehaviorInfluencesPerNpc)
            .ToList();
        bool likelyLegacyCommitmentState = this.CommitmentTrust == 0
            && this.MissedCommitments == 0
            && this.ConsecutiveMissedCommitments == 0
            && this.Commitments.Count == 0
            && this.SharedExperiences.Count == 0;
        this.CommitmentTrust = likelyLegacyCommitmentState
            ? 50
            : ClampScore(this.CommitmentTrust);
        this.MissedCommitments = System.Math.Max(0, this.MissedCommitments);
        this.ConsecutiveMissedCommitments = System.Math.Max(0, this.ConsecutiveMissedCommitments);
        this.Conflicts ??= new List<NpcConflictFact>();
        this.Conflicts = this.Conflicts
            .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary))
            .Select(conflict =>
            {
                conflict.CauseKind = BehaviorMemory.NormalizeConflictCauseKind(conflict.CauseKind);
                conflict.Summary = conflict.Summary.Trim();
                conflict.Severity = ClampScore(conflict.Severity);
                conflict.PeakSeverity = System.Math.Max(conflict.Severity, ClampScore(conflict.PeakSeverity));
                conflict.Status = conflict.Status switch
                {
                    "Resolved" => "Resolved",
                    "Recovering" => "Recovering",
                    _ => conflict.Severity <= 0 ? "Resolved" : "Active"
                };
                conflict.RepairScore = ClampScore(conflict.RepairScore);
                conflict.ApologyCount = System.Math.Max(0, conflict.ApologyCount);
                conflict.RepairStage = conflict.RepairStage switch
                {
                    "NeedsApology" => "NeedsApology",
                    "NeedsGesture" => "NeedsGesture",
                    "NeedsTime" => "NeedsTime",
                    "NeedsConversation" => "NeedsConversation",
                    "ReadyToResolve" => "ReadyToResolve",
                    "Resolved" => "Resolved",
                    _ => conflict.RequiresComplexRepair ? "NeedsApology" : "Simple"
                };
                if (conflict.RequiresComplexRepair && conflict.MinimumRepairTotalDays < 0)
                {
                    conflict.MinimumRepairTotalDays = conflict.CreatedTotalDays + BehaviorMemory.GetComplexRepairDelayDays(conflict.PeakSeverity);
                }
                conflict.TimesReinforced = System.Math.Max(0, conflict.TimesReinforced);
                if (conflict.Status == "Resolved" && conflict.ResolvedTotalDays < 0)
                {
                    conflict.ResolvedTotalDays = conflict.LastUpdatedTotalDays;
                    conflict.ResolvedTimeOfDay = conflict.LastUpdatedTimeOfDay;
                }

                return conflict;
            })
            .OrderBy(conflict => BehaviorMemory.ConflictStatusOrder(conflict.Status))
            .ThenByDescending(conflict => conflict.Severity)
            .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        this.AiFriendshipGainedToday = System.Math.Clamp(this.AiFriendshipGainedToday, 0, 30);
        if (string.IsNullOrWhiteSpace(this.Mood))
        {
            this.Mood = "Neutral";
        }

        if (string.IsNullOrWhiteSpace(this.CurrentInclination))
        {
            this.CurrentInclination = "Neutral";
        }

        if (string.IsNullOrWhiteSpace(this.LastInteraction))
        {
            this.LastInteraction = "none yet";
        }

        if (string.IsNullOrWhiteSpace(this.LastSceneContext))
        {
            this.LastSceneContext = "none";
        }

        if (string.IsNullOrWhiteSpace(this.LastSceneInfluence))
        {
            this.LastSceneInfluence = "none";
        }

        if (string.IsNullOrWhiteSpace(this.LastSceneInfluenceReason))
        {
            this.LastSceneInfluenceReason = "none";
        }

        if (string.IsNullOrWhiteSpace(this.LastEmotionReason))
        {
            this.LastEmotionReason = "none";
        }

        if (string.IsNullOrWhiteSpace(this.FarmerNicknameStatus) && !string.IsNullOrWhiteSpace(this.FarmerNickname))
        {
            this.FarmerNicknameStatus = "Requested";
        }

        if (string.IsNullOrWhiteSpace(this.InteractionRhythm))
        {
            this.InteractionRhythm = "New";
        }

        if (string.IsNullOrWhiteSpace(this.InteractionComfortTier))
        {
            this.InteractionComfortTier = "Distant";
        }

        if (this.LastGiftTotalDays != Game1.Date.TotalDays)
        {
            this.GiftsToday = 0;
        }

        if (this.LastAiFriendshipTotalDays != Game1.Date.TotalDays)
        {
            this.AiFriendshipGainedToday = 0;
        }
    }

    public LivingNpcState Clone()
    {
        return new LivingNpcState
        {
            NpcName = this.NpcName,
            Mood = this.Mood,
            CurrentEmotion = this.CurrentEmotion,
            EmotionIntensity = this.EmotionIntensity,
            LastEmotionReason = this.LastEmotionReason,
            LastEmotionUpdatedTotalDays = this.LastEmotionUpdatedTotalDays,
            LastEmotionUpdatedTimeOfDay = this.LastEmotionUpdatedTimeOfDay,
            Attention = this.Attention,
            Openness = this.Openness,
            Familiarity = this.Familiarity,
            FamiliarityGainedToday = this.FamiliarityGainedToday,
            LastFamiliarityGainTotalDays = this.LastFamiliarityGainTotalDays,
            ConversationsToday = this.ConversationsToday,
            ConsecutiveConversationDays = this.ConsecutiveConversationDays,
            LastConversationTotalDays = this.LastConversationTotalDays,
            LastConversationTimeOfDay = this.LastConversationTimeOfDay,
            LastConversationGapDays = this.LastConversationGapDays,
            InteractionRhythm = this.InteractionRhythm,
            InteractionComfortTier = this.InteractionComfortTier,
            DailyConversationComfortLimit = this.DailyConversationComfortLimit,
            RepeatedConversationPressure = this.RepeatedConversationPressure,
            LastFriendshipHearts = this.LastFriendshipHearts,
            LastGiftName = this.LastGiftName,
            LastGiftTaste = this.LastGiftTaste,
            LastGiftTotalDays = this.LastGiftTotalDays,
            LastGiftTimeOfDay = this.LastGiftTimeOfDay,
            GiftsToday = this.GiftsToday,
            LastEventContext = this.LastEventContext,
            LastEventTotalDays = this.LastEventTotalDays,
            LastEventTimeOfDay = this.LastEventTimeOfDay,
            LongTermMemories = this.LongTermMemories
                .Select(memory => new LongTermMemoryFact
                {
                    Kind = memory.Kind,
                    Subject = memory.Subject,
                    Summary = memory.Summary,
                    Tags = memory.Tags.ToList(),
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
                    LastRecalledTotalDays = memory.LastRecalledTotalDays,
                    LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
                    RecallCount = memory.RecallCount,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            PlayerPreferenceMemories = this.PlayerPreferenceMemories
                .Select(memory => new PlayerPreferenceFact
                {
                    PreferenceKind = memory.PreferenceKind,
                    Subject = memory.Subject,
                    Summary = memory.Summary,
                    Tags = memory.Tags.ToList(),
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
                    LastRecalledTotalDays = memory.LastRecalledTotalDays,
                    LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
                    RecallCount = memory.RecallCount,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            CommunityImpressions = this.CommunityImpressions
                .Select(memory => new CommunityImpressionFact
                {
                    SubjectNpcName = memory.SubjectNpcName,
                    SubjectDisplayName = memory.SubjectDisplayName,
                    Kind = memory.Kind,
                    Summary = memory.Summary,
                    Source = memory.Source,
                    Visibility = memory.Visibility,
                    Confidence = memory.Confidence,
                    TransmissionDepth = memory.TransmissionDepth,
                    DistortionLevel = memory.DistortionLevel,
                    HeardFromNpcName = memory.HeardFromNpcName,
                    CircleKey = memory.CircleKey,
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
                    LastRecalledTotalDays = memory.LastRecalledTotalDays,
                    LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
                    LastSharedTotalDays = memory.LastSharedTotalDays,
                    LastSharedTimeOfDay = memory.LastSharedTimeOfDay,
                    ShareCount = memory.ShareCount,
                    ExpiresTotalDays = memory.ExpiresTotalDays,
                    RecallCount = memory.RecallCount,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            Commitments = this.Commitments
                .Select(commitment => new NpcCommitmentFact
                {
                    Type = commitment.Type,
                    Summary = commitment.Summary,
                    DueTotalDays = commitment.DueTotalDays,
                    TimeOfDay = commitment.TimeOfDay,
                    LocationName = commitment.LocationName,
                    LocationLabel = commitment.LocationLabel,
                    Status = commitment.Status,
                    CreatedTotalDays = commitment.CreatedTotalDays,
                    CreatedTimeOfDay = commitment.CreatedTimeOfDay,
                    LastUpdatedTotalDays = commitment.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = commitment.LastUpdatedTimeOfDay,
                    LastMentionedTotalDays = commitment.LastMentionedTotalDays,
                    LastMentionedTimeOfDay = commitment.LastMentionedTimeOfDay,
                    ArrivalGreetingShown = commitment.ArrivalGreetingShown,
                    FulfilledTotalDays = commitment.FulfilledTotalDays,
                    FulfilledTimeOfDay = commitment.FulfilledTimeOfDay,
                    FollowUpMentionedTotalDays = commitment.FollowUpMentionedTotalDays,
                    FollowUpMentionedTimeOfDay = commitment.FollowUpMentionedTimeOfDay,
                    DayBeforeReminderMentionedTotalDays = commitment.DayBeforeReminderMentionedTotalDays,
                    DayBeforeReminderMentionedTimeOfDay = commitment.DayBeforeReminderMentionedTimeOfDay,
                    MorningReminderShown = commitment.MorningReminderShown,
                    RenewedAfterMiss = commitment.RenewedAfterMiss,
                    RenewedTotalDays = commitment.RenewedTotalDays,
                    RenewedTimeOfDay = commitment.RenewedTimeOfDay,
                    TimesReinforced = commitment.TimesReinforced
                })
                .ToList(),
            SharedExperiences = this.SharedExperiences
                .Select(experience => new SharedExperienceFact
                {
                    Key = experience.Key,
                    Type = experience.Type,
                    Summary = experience.Summary,
                    LocationName = experience.LocationName,
                    LocationLabel = experience.LocationLabel,
                    CreatedTotalDays = experience.CreatedTotalDays,
                    CreatedTimeOfDay = experience.CreatedTimeOfDay,
                    LastUpdatedTotalDays = experience.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = experience.LastUpdatedTimeOfDay,
                    Importance = experience.Importance,
                    TimesReinforced = experience.TimesReinforced,
                    FollowUpEligibleTotalDays = experience.FollowUpEligibleTotalDays,
                    FollowUpShownTotalDays = experience.FollowUpShownTotalDays,
                    FollowUpShownTimeOfDay = experience.FollowUpShownTimeOfDay
                })
                .ToList(),
            DialogueBehaviorInfluences = this.DialogueBehaviorInfluences
                .Select(influence => new DialogueBehaviorInfluenceFact
                {
                    Type = influence.Type,
                    Summary = influence.Summary,
                    TargetLocation = influence.TargetLocation,
                    TargetLocationLabel = influence.TargetLocationLabel,
                    Intensity = influence.Intensity,
                    Status = influence.Status,
                    CreatedTotalDays = influence.CreatedTotalDays,
                    CreatedTimeOfDay = influence.CreatedTimeOfDay,
                    LastUpdatedTotalDays = influence.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = influence.LastUpdatedTimeOfDay,
                    ExpiresTotalDays = influence.ExpiresTotalDays,
                    LastTriggeredTotalDays = influence.LastTriggeredTotalDays,
                    LastTriggeredTimeOfDay = influence.LastTriggeredTimeOfDay,
                    TriggerCount = influence.TriggerCount,
                    MaxTriggers = influence.MaxTriggers,
                    TimesReinforced = influence.TimesReinforced
                })
                .ToList(),
            HelpRequests = this.HelpRequests
                .Select(request => new NpcHelpRequestFact
                {
                    NpcDisplayName = request.NpcDisplayName,
                    QuestLogId = request.QuestLogId,
                    Type = request.Type,
                    Summary = request.Summary,
                    Steps = request.Steps
                        .Select(step => new NpcHelpRequestStepFact
                        {
                            Type = step.Type,
                            Summary = step.Summary,
                            RequestedItemId = step.RequestedItemId,
                            RequestedItemLabel = step.RequestedItemLabel,
                            QuestionTopic = step.QuestionTopic,
                            Status = step.Status,
                            Resolution = step.Resolution,
                            CompletedTotalDays = step.CompletedTotalDays,
                            CompletedTimeOfDay = step.CompletedTimeOfDay
                        })
                        .ToList(),
                    CurrentStepIndex = request.CurrentStepIndex,
                    RequestedItemId = request.RequestedItemId,
                    RequestedItemLabel = request.RequestedItemLabel,
                    QuestionTopic = request.QuestionTopic,
                    DueTotalDays = request.DueTotalDays,
                    Reason = request.Reason,
                    Status = request.Status,
                    Resolution = request.Resolution,
                    FollowUpPotential = request.FollowUpPotential,
                    FailureReaction = request.FailureReaction,
                    CreatedTotalDays = request.CreatedTotalDays,
                    CreatedTimeOfDay = request.CreatedTimeOfDay,
                    AcceptedTotalDays = request.AcceptedTotalDays,
                    AcceptedTimeOfDay = request.AcceptedTimeOfDay,
                    DeclinedTotalDays = request.DeclinedTotalDays,
                    DeclinedTimeOfDay = request.DeclinedTimeOfDay,
                    LastUpdatedTotalDays = request.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = request.LastUpdatedTimeOfDay,
                    LastMentionedTotalDays = request.LastMentionedTotalDays,
                    LastMentionedTimeOfDay = request.LastMentionedTimeOfDay,
                    FulfilledTotalDays = request.FulfilledTotalDays,
                    FulfilledTimeOfDay = request.FulfilledTimeOfDay,
                    FollowUpEligibleTotalDays = request.FollowUpEligibleTotalDays,
                    FollowUpShownTotalDays = request.FollowUpShownTotalDays,
                    FollowUpShownTimeOfDay = request.FollowUpShownTimeOfDay,
                    RewardFriendship = request.RewardFriendship,
                    RewardGranted = request.RewardGranted,
                    RewardGiftGiven = request.RewardGiftGiven,
                    TimesReinforced = request.TimesReinforced
                })
                .ToList(),
            Conflicts = this.Conflicts
                .Select(conflict => new NpcConflictFact
                {
                    CauseKind = conflict.CauseKind,
                    Summary = conflict.Summary,
                    Severity = conflict.Severity,
                    PeakSeverity = conflict.PeakSeverity,
                    Status = conflict.Status,
                    CreatedTotalDays = conflict.CreatedTotalDays,
                    CreatedTimeOfDay = conflict.CreatedTimeOfDay,
                    LastUpdatedTotalDays = conflict.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = conflict.LastUpdatedTimeOfDay,
                    ResolvedTotalDays = conflict.ResolvedTotalDays,
                    ResolvedTimeOfDay = conflict.ResolvedTimeOfDay,
                    RecoveryMentionedTotalDays = conflict.RecoveryMentionedTotalDays,
                    RecoveryMentionedTimeOfDay = conflict.RecoveryMentionedTimeOfDay,
                    RepairScore = conflict.RepairScore,
                    ApologyCount = conflict.ApologyCount,
                    RequiresComplexRepair = conflict.RequiresComplexRepair,
                    RepairStage = conflict.RepairStage,
                    ApologyReceived = conflict.ApologyReceived,
                    MeaningfulGiftReceived = conflict.MeaningfulGiftReceived,
                    SpecificRepairTalkReceived = conflict.SpecificRepairTalkReceived,
                    MinimumRepairTotalDays = conflict.MinimumRepairTotalDays,
                    LastRepairGiftName = conflict.LastRepairGiftName,
                    RepairGrowthGranted = conflict.RepairGrowthGranted,
                    TimesReinforced = conflict.TimesReinforced
                })
                .ToList(),
            AiFriendshipGainedToday = this.AiFriendshipGainedToday,
            RelationshipTrustInitialized = this.RelationshipTrustInitialized,
            RelationshipTrust = this.RelationshipTrust,
            LastRelationshipTrustUpdatedTotalDays = this.LastRelationshipTrustUpdatedTotalDays,
            LastRelationshipTrustUpdatedTimeOfDay = this.LastRelationshipTrustUpdatedTimeOfDay,
            CommitmentTrust = this.CommitmentTrust,
            MissedCommitments = this.MissedCommitments,
            ConsecutiveMissedCommitments = this.ConsecutiveMissedCommitments,
            LastMissedCommitmentTotalDays = this.LastMissedCommitmentTotalDays,
            LastMissedCommitmentTimeOfDay = this.LastMissedCommitmentTimeOfDay,
            LastAiFriendshipTotalDays = this.LastAiFriendshipTotalDays,
            LastAiSmallGiftTotalDays = this.LastAiSmallGiftTotalDays,
            LastAiMeaningfulGiftTotalDays = this.LastAiMeaningfulGiftTotalDays,
            LastAiMoneyGiftTotalDays = this.LastAiMoneyGiftTotalDays,
            LastAiFarmHelpTotalDays = this.LastAiFarmHelpTotalDays,
            LastAiWalkTogetherTotalDays = this.LastAiWalkTogetherTotalDays,
            LastHelpRequestTotalDays = this.LastHelpRequestTotalDays,
            LastHelpRequestTimeOfDay = this.LastHelpRequestTimeOfDay,
            LastSceneContext = this.LastSceneContext,
            LastSceneInfluence = this.LastSceneInfluence,
            LastSceneInfluenceReason = this.LastSceneInfluenceReason,
            CurrentInclination = this.CurrentInclination,
            LastInteraction = this.LastInteraction,
            FarmerNickname = this.FarmerNickname,
            FarmerNicknameStatus = this.FarmerNicknameStatus,
            FarmerNicknameTotalDays = this.FarmerNicknameTotalDays,
            FarmerNicknameTimeOfDay = this.FarmerNicknameTimeOfDay,
            LastUpdatedTotalDays = this.LastUpdatedTotalDays,
            LastUpdatedTimeOfDay = this.LastUpdatedTimeOfDay
        };
    }

    private IEnumerable<LongTermMemoryFact> GetTopLongTermMemories(int count)
    {
        return this.LongTermMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<PlayerPreferenceFact> GetTopPlayerPreferences(int count)
    {
        return this.PlayerPreferenceMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<CommunityImpressionFact> GetTopCommunityImpressions(int count)
    {
        return this.CommunityImpressions
            .OrderByDescending(memory => BehaviorMemory.GetCommunityImpressionRetentionScore(memory))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<NpcCommitmentFact> GetTopCommitments(int count)
    {
        return this.Commitments
            .OrderBy(commitment => commitment.Status switch
            {
                "Pending" => 0,
                "Expired" => 1,
                "Fulfilled" => 2,
                _ => 3
            })
            .ThenBy(commitment => commitment.DueTotalDays)
            .ThenBy(commitment => commitment.TimeOfDay)
            .Take(count);
    }

    private IEnumerable<SharedExperienceFact> GetTopSharedExperiences(int count)
    {
        return this.SharedExperiences
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<NpcHelpRequestFact> GetTopHelpRequests(int count)
    {
        return this.HelpRequests
            .OrderBy(request => BehaviorMemory.HelpRequestStatusOrder(request.Status))
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(count);
    }

    private IEnumerable<NpcConflictFact> GetTopConflicts(int count)
    {
        return this.Conflicts
            .OrderBy(conflict => conflict.Status switch
            {
                "Active" => 0,
                "Recovering" => 1,
                "Resolved" => 2,
                _ => 3
            })
            .ThenByDescending(conflict => conflict.Severity)
            .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
            .Take(count);
    }
}
