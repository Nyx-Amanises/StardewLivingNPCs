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

    private static readonly Dictionary<string, string> TravelLocationAliases = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Farm"] = "Farm",
        ["农场"] = "Farm",
        ["Town"] = "Town",
        ["Pelican Town"] = "Town",
        ["鹈鹕镇"] = "Town",
        ["Mountain"] = "Mountain",
        ["山地"] = "Mountain",
        ["山上"] = "Mountain",
        ["Mine"] = "Mine",
        ["Mines"] = "Mine",
        ["The Mines"] = "Mine",
        ["矿井"] = "Mine",
        ["矿洞"] = "Mine",
        ["矿山"] = "Mine",
        ["Beach"] = "Beach",
        ["海滩"] = "Beach",
        ["Forest"] = "Forest",
        ["Cindersap Forest"] = "Forest",
        ["森林"] = "Forest",
        ["煤矿森林"] = "Forest",
        ["BusStop"] = "BusStop",
        ["Bus Stop"] = "BusStop",
        ["巴士站"] = "BusStop",
        ["Trailer"] = "Trailer",
        ["Penny's Trailer"] = "Trailer",
        ["Pam's Trailer"] = "Trailer",
        ["Penny's House"] = "Trailer",
        ["Pam's House"] = "Trailer",
        ["Penny's Home"] = "Trailer",
        ["Pam's Home"] = "Trailer",
        ["潘妮家"] = "Trailer",
        ["潘妮的家"] = "Trailer",
        ["潘妮家里"] = "Trailer",
        ["帕姆家"] = "Trailer",
        ["帕姆的家"] = "Trailer",
        ["拖车"] = "Trailer",
        ["JoshHouse"] = "JoshHouse",
        ["Alex's House"] = "JoshHouse",
        ["亚历克斯家"] = "JoshHouse",
        ["HaleyHouse"] = "HaleyHouse",
        ["Haley's House"] = "HaleyHouse",
        ["Emily's House"] = "HaleyHouse",
        ["海莉家"] = "HaleyHouse",
        ["艾米丽家"] = "HaleyHouse",
        ["SamHouse"] = "SamHouse",
        ["Sam's House"] = "SamHouse",
        ["山姆家"] = "SamHouse",
        ["ScienceHouse"] = "ScienceHouse",
        ["Robin's House"] = "ScienceHouse",
        ["Sebastian's House"] = "ScienceHouse",
        ["Maru's House"] = "ScienceHouse",
        ["罗宾家"] = "ScienceHouse",
        ["塞巴斯蒂安家"] = "ScienceHouse",
        ["玛鲁家"] = "ScienceHouse",
        ["LeahHouse"] = "LeahHouse",
        ["Leah's Cottage"] = "LeahHouse",
        ["莉亚家"] = "LeahHouse",
        ["AnimalShop"] = "AnimalShop",
        ["Marnie's Ranch"] = "AnimalShop",
        ["玛妮牧场"] = "AnimalShop",
        ["玛妮家"] = "AnimalShop",
        ["ElliottHouse"] = "ElliottHouse",
        ["Elliott's Cabin"] = "ElliottHouse",
        ["艾利欧特家"] = "ElliottHouse",
        ["Blacksmith"] = "Blacksmith",
        ["铁匠铺"] = "Blacksmith",
        ["FishShop"] = "FishShop",
        ["鱼店"] = "FishShop",
        ["WizardHouse"] = "WizardHouse",
        ["Wizard's Tower"] = "WizardHouse",
        ["法师塔"] = "WizardHouse",
        ["Tent"] = "Tent",
        ["Linus's Tent"] = "Tent",
        ["莱纳斯帐篷"] = "Tent",
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
        return this.statesByNpc.Values.ToList();
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
        prompt.AppendLine($"- {this.BuildConversationStance(npc, state, disposition, world, emotionalStyle)}");

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
        prompt.AppendLine($"- World knowledge available to this NPC: {world.ProgressionKnowledge.PromptLabel}.");
        if (state != null)
        {
            prompt.AppendLine($"- Mood: {state.Mood}; attention to farmer: {state.Attention}/100; response inclination: {state.CurrentInclination}.");
            prompt.AppendLine($"- Interpersonal emotion: {state.EmotionPromptLabel}.");
            prompt.AppendLine($"- Emotional expression style: {emotionalStyle.PromptLabel}.");
            prompt.AppendLine($"- Long-term familiarity with the farmer: {state.Familiarity}/100 ({state.FamiliarityPromptLabel}).");
            prompt.AppendLine($"- Relationship trust in the farmer: {state.RelationshipTrustPromptLabel}.");
            prompt.AppendLine($"- Secret-sharing depth: {state.SecretSharingPromptLabel}.");
            prompt.AppendLine($"- Relationship-aware interaction rhythm: {state.InteractionRhythmPromptLabel}; comfort tier: {state.InteractionComfortTierPromptLabel}.");
            prompt.AppendLine($"- Recent gift context: {state.LastGiftPromptLabel}.");
            prompt.AppendLine($"- Recent event context: {state.LastEventPromptLabel}.");
            prompt.AppendLine($"- Durable memory store: {state.LongTermMemories.Count} long-term memories tracked; recall focus for this reply: {this.FormatLongTermMemoryPromptLabel(recallPlan.LongTermMemories)}.");
            prompt.AppendLine($"- Known farmer preferences: {this.FormatPlayerPreferencePromptLabel(recallPlan.PlayerPreferences)}.");
            prompt.AppendLine($"- Conversation-driven behavior tendencies: {state.DialogueBehaviorInfluencePromptLabel}.");
            prompt.AppendLine($"- Shared experiences from completed help requests: {state.SharedExperiencePromptLabel}.");
            prompt.AppendLine($"- Help requests involving the farmer: {state.HelpRequestPromptLabel}.");
            prompt.AppendLine($"- Community impressions about the farmer's ties with other NPCs: {this.FormatCommunityImpressionPromptLabel(npc, communityImpressions)}.");
            prompt.AppendLine($"- Stable community circles this NPC belongs to: {this.FormatSocialCirclePromptLabel(npc)}.");
            prompt.AppendLine("- Help-request lifecycle: Offered means the NPC has asked but the farmer has not accepted; Pending means accepted and active; only Pending requests should be treated like tasks.");
            prompt.AppendLine($"- Help-request readiness: {this.BuildHelpRequestReadinessLabel(npc, state, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, minRelationshipTrustForHelpRequests)}.");
            prompt.AppendLine($"- Help-request fit: {HelpRequestAdvisor.BuildPromptLabel(npc, world.Progression)}");
            prompt.AppendLine($"- Conflict memory: {state.ConflictPromptLabel}.");
            prompt.AppendLine($"- Personal memory context: {state.FarmerNicknamePromptLabel}.");
            prompt.AppendLine($"- Scene influence on mood: {state.LastSceneInfluenceReason}.");
            prompt.AppendLine($"- Last interaction: {state.LastInteraction}.");
        }
        else
        {
            prompt.AppendLine("- No persistent LivingNPCs state exists yet; use disposition and scene context conservatively.");
        }

        var priorityContext = this.BuildPriorityPromptContext(npc, state, world, recentEntries, recallPlan, communityImpressions, emotionalStyle).ToList();
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
        foreach (string guidance in this.BuildReplyGuidance(state, world, emotionalStyle))
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
        WorldContextSnapshot world,
        EmotionalExpressionCue emotionalStyle)
    {
        if (state == null)
        {
            return $"{npc.displayName} should sound {disposition.PromptLabel}, with emotional expression style: {emotionalStyle.PromptLabel}; shaped by {disposition.SourceLabel} profile context and the current scene: {world.PromptLabel}.";
        }

        string tone = this.BuildToneCue(state);
        string rhythm = this.BuildRhythmCue(state);
        string scenePressure = world.StateInfluence.HasMood
            ? $"scene pressure suggests {world.StateInfluence.Mood}/{world.StateInfluence.Inclination}"
            : "scene pressure is mild";

        return $"{npc.displayName} should sound {tone}; temperament is {disposition.PromptLabel}; emotional expression style is {emotionalStyle.PromptLabel}; profile source is {disposition.SourceLabel}; relationship is {state.FamiliarityPromptLabel}; {rhythm}; {scenePressure}.";
    }

    private IEnumerable<string> BuildPriorityPromptContext(
        NPC npc,
        LivingNpcState? state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        MemoryRecallPlan recallPlan,
        IReadOnlyList<CommunityImpressionSelection> communityImpressions,
        EmotionalExpressionCue emotionalStyle)
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

            var sharedExperience = state.SharedExperiences.FirstOrDefault(experience =>
                experience.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && experience.FollowUpShownTotalDays < 0
                && experience.CreatedTotalDays >= Game1.Date.TotalDays - 7
            );
            if (sharedExperience != null)
            {
                yield return $"Shared help milestone: {sharedExperience.PromptLabel}; if it fits, the NPC may warmly acknowledge that the farmer came through.";
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
                yield return $"Unresolved conflict: {activeConflict.PromptLabel}; while this remains unresolved, warmth should be reduced and the NPC may be brief, cool, or decline closeness; expression style: {emotionalStyle.ConflictPromptLabel}.";
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
                yield return $"Recently resolved conflict: {recoveredConflict.ResolvedPromptLabel}; if it fits naturally, the NPC may briefly make clear that the earlier issue is past now; recovery style: {emotionalStyle.RepairPromptLabel}.";
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

        yield return $"World-stage continuity: {world.ProgressionKnowledge.ReplyGuidance}";

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

    private IEnumerable<string> BuildReplyGuidance(
        LivingNpcState? state,
        WorldContextSnapshot world,
        EmotionalExpressionCue emotionalStyle)
    {
        if (state == null)
        {
            yield return "Let the reply be scene-aware and modest because there is no persistent state yet.";
            yield return "Keep continuity subtle; do not invent strong feelings from weak context.";
            yield return $"Emotion expression style: {emotionalStyle.ReplyGuidance}.";
            yield break;
        }

        yield return $"Tone target: {this.BuildToneCue(state)}.";
        yield return $"Emotion expression style: {emotionalStyle.ReplyGuidance}.";
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

        if (state.SharedExperiences.Any(experience =>
                experience.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && experience.FollowUpShownTotalDays < 0))
        {
            yield return "Completed help requests can deepen continuity; acknowledge them naturally without turning them into a formal future plan.";
        }

        if (state.HasUnresolvedConflict)
        {
            yield return $"An unresolved conflict is still shaping the relationship; do not answer with default warmth, and if the conflict is severe it is okay to keep the reply short or refuse a friendly invitation; express it through this NPC's style: {emotionalStyle.ConflictPromptLabel}.";
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
            yield return $"If a recently resolved conflict is relevant, it is okay to say in a natural in-character way that the earlier matter is behind you now; recovery style: {emotionalStyle.RepairPromptLabel}.";
        }

        if (state.RepeatedConversationPressure >= 20)
        {
            yield return "Because the farmer has been checking in repeatedly, it is okay to sound brief, amused, busy, or gently boundary-setting.";
        }

        if (world.StateInfluence.HasMood)
        {
            yield return $"Let the scene nudge tone through {world.StateInfluence.Reason}, without explicitly explaining the scene mechanics.";
        }

        yield return $"Keep references to town progress, the farmer's household, and how long she has lived here consistent with what this NPC could plausibly know: {world.ProgressionKnowledge.ReplyGuidance}";
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
        this.AddRecallSignals(world.ProgressionKnowledge.PromptLabel, tags, tokens);
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
        var emotionalStyle = EmotionalExpressionStyle.For(npc);
        LivingNpcState? state = null;
        if (includeState)
        {
            this.statesByNpc.TryGetValue(npc.Name, out state);
        }
        var world = WorldContext.For(npc, state);

        if ((entries == null || entries.Count == 0) && state == null)
        {
            return $"{npc.displayName} 还没有 LivingNPCs 行为/互动记忆或状态。\n- 行为倾向：{disposition.DebugLabel}\n- 情绪表达风格：{emotionalStyle.DebugSummaryLabel}\n- 当前场景：{world.DebugLabel}\n- 世界进度（客观）：{world.Progression.DebugLabel}\n- NPC 可知进度：{world.ProgressionKnowledge.DebugLabel}";
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
            var latestBehavior = entries?
                .LastOrDefault(entry => string.Equals(entry.Kind, "Behavior", System.StringComparison.OrdinalIgnoreCase));
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
            summary.AppendLine($"- 情绪表达风格：{emotionalStyle.DebugSummaryLabel}");
            summary.AppendLine($"- 社区圈层：{this.FormatSocialCircleDebugLabel(npc)}");
            summary.AppendLine($"- 社区印象：{state.CommunityImpressionDebugLabel}");
            summary.AppendLine($"- 当前检索社区印象：{this.FormatCommunityImpressionDebugLabel(communityImpressions)}");
            summary.AppendLine($"- 对话驱动行为：{state.DialogueBehaviorInfluenceDebugLabel}");
            summary.AppendLine($"- 最近行为选择：{this.FormatLatestBehaviorChoiceDebugLabel(latestBehavior)}");
            summary.AppendLine($"- 共同经历：{state.SharedExperienceDebugLabel}");
            summary.AppendLine($"- 主动求助：{state.HelpRequestDebugLabel}");
            summary.AppendLine($"- 求助生成适配：{HelpRequestAdvisor.BuildDebugLabel(npc, world.Progression)}");
            summary.AppendLine($"- 冲突记忆：{state.ConflictDebugLabel}");
            summary.AppendLine($"- 长期称呼记忆：{state.FarmerNicknameLabel}");
            summary.AppendLine($"- 今日 AI 对话额外好感：{state.AiFriendshipGainedToday}");
            summary.AppendLine($"- 角色资料：{disposition.SourceDebugLabel}");
            summary.AppendLine($"- 行为倾向：{disposition.DebugLabel}");
            summary.AppendLine($"- 情绪表达风格：{emotionalStyle.DebugSummaryLabel}");
            summary.AppendLine($"- 当前场景：{world.DebugLabel}");
            summary.AppendLine($"- 世界进度（客观）：{world.Progression.DebugLabel}");
            summary.AppendLine($"- NPC 可知进度：{world.ProgressionKnowledge.DebugLabel}");
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
            summary.AppendLine($"- 世界进度（客观）：{world.Progression.DebugLabel}");
            summary.AppendLine($"- NPC 可知进度：{world.ProgressionKnowledge.DebugLabel}");
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

    private string FormatLatestBehaviorChoiceDebugLabel(BehaviorMemoryEntry? entry)
    {
        if (entry == null)
        {
            return "暂无行为选择记录";
        }

        string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
        string locationPart = string.IsNullOrWhiteSpace(location) ? string.Empty : $"，地点 {location}";
        return $"{entry.Action}（第 {entry.TotalDays} 天 {entry.TimeOfDay}{locationPart}；原因：{entry.Reason}）";
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
                    action.ItemId = action.ItemId?.Trim() ?? string.Empty;
                    action.ItemLabel = action.ItemLabel?.Trim() ?? string.Empty;
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
                    influence.TargetLocation = NormalizeTravelLocation(influence.TargetLocation, string.Empty);
                    influence.TargetLocationLabel = influence.TargetLocationLabel?.Trim() ?? string.Empty;
                    influence.DurationDays = System.Math.Clamp(influence.DurationDays, 0, 7);
                    influence.Intensity = System.Math.Clamp(influence.Intensity, 0, 100);
                    influence.MaxTriggers = System.Math.Clamp(influence.MaxTriggers, 0, 4);
                    return influence;
                })
                .Where(influence => influence.Type != "none")
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
                    Game1.Date.TotalDays + GetComplexRepairDelayDays(state, existing.PeakSeverity)
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
                ? Game1.Date.TotalDays + GetComplexRepairDelayDays(state, candidate.Severity)
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
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        int totalRepair = emotionalStyle.AdjustRepairAmount(System.Math.Clamp(repairDelta + (apology ? 12 : 0), 0, 100));
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

    private void DecayEmotionAndConflicts(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        EmotionalExpressionCue emotionalStyle)
    {
        int adjustedEmotionDailyDecay = emotionalStyle.AdjustEmotionDecay(emotionDailyDecay);
        int adjustedConflictDailyDecay = emotionalStyle.AdjustConflictDecay(conflictDailyDecay);
        if (adjustedEmotionDailyDecay > 0 && state.EmotionIntensity > 0)
        {
            state.EmotionIntensity = LivingNpcState.MoveToward(state.EmotionIntensity, 0, adjustedEmotionDailyDecay);
            if (state.EmotionIntensity == 0)
            {
                state.CurrentEmotion = "Calm";
            }
        }

        if (adjustedConflictDailyDecay <= 0)
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
                    LivingNpcState.MoveToward(conflict.Severity, 0, adjustedConflictDailyDecay)
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

            conflict.Severity = LivingNpcState.MoveToward(conflict.Severity, 0, adjustedConflictDailyDecay);
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

    private static int GetComplexRepairDelayDays(LivingNpcState state, int severity)
    {
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        return emotionalStyle.AdjustComplexRepairDelay(GetComplexRepairDelayDays(severity));
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
        string targetLocation = NormalizeTravelLocation(candidate.TargetLocation, fallbackLocation);
        string targetLabel = string.IsNullOrWhiteSpace(candidate.TargetLocationLabel)
            ? GetTravelLocationLabel(targetLocation)
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

    private bool StoreHelpRequest(
        NPC npc,
        LivingNpcState state,
        ValleyTalkHelpRequestCandidate candidate,
        string playerText,
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
            RewardFriendship = DetermineHelpRequestFriendshipReward(
                npc,
                candidate,
                normalizedType
            ),
            RewardMoney = DetermineHelpRequestMoneyReward(steps),
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
        foreach (var rawStep in rawSteps.Take(this.GetMaxHelpRequestStepsForCurrentWorldStage()))
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
        request.SpecialFollowUpPlanned = ShouldPlanHelpRequestFollowUp(state, request);
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

    internal static int DetermineHelpRequestMoneyReward(IReadOnlyList<NpcHelpRequestStepFact> steps)
    {
        int total = 0;
        foreach (var step in steps.Where(step => step.Type == "item_request"))
        {
            total += DetermineHelpRequestItemMoneyReward(step.RequestedItemId);
        }

        return System.Math.Clamp(total <= 0 ? 200 : total, 200, 10000);
    }

    private static int DetermineHelpRequestItemMoneyReward(string itemId)
    {
        string normalized = NormalizeQualifiedObjectId(itemId);
        if (normalized is "(O)16" or "(O)20")
        {
            return 200;
        }

        if (normalized == "(O)74")
        {
            return 10000;
        }

        int basePrice = TryGetObjectBasePrice(normalized);
        if (basePrice <= 80)
        {
            return 200;
        }

        int reward = basePrice * 5;
        reward = ((reward + 24) / 25) * 25;
        return System.Math.Clamp(reward, 200, 10000);
    }

    private static int TryGetObjectBasePrice(string itemId)
    {
        string objectId = NormalizeQualifiedObjectId(itemId);
        if (objectId.StartsWith("(O)", System.StringComparison.OrdinalIgnoreCase))
        {
            objectId = objectId.Substring(3);
        }

        if (Game1.objectData != null && Game1.objectData.TryGetValue(objectId, out var data))
        {
            return data.Price;
        }

        try
        {
            var item = ItemRegistry.Create<StardewValley.Object>(NormalizeQualifiedObjectId(itemId));
            return item.salePrice(false);
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeQualifiedObjectId(string itemId)
    {
        string value = itemId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("(O)", System.StringComparison.OrdinalIgnoreCase))
        {
            return $"(O){value.Substring(3)}";
        }

        return $"(O){value}";
    }

    private static bool ShouldPlanHelpRequestFollowUp(LivingNpcState state, NpcHelpRequestFact request)
    {
        int chance = request.FollowUpPotential switch
        {
            "deeper_relationship" => 75,
            _ => 45
        };

        if (request.RewardMoney >= 1000)
        {
            chance += 10;
        }

        unchecked
        {
            string seed = $"{state.NpcName}:{request.Summary}:{request.RequestedItemId}:{request.FulfilledTotalDays}:{request.FollowUpPotential}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return System.Math.Abs(hash % 100) < System.Math.Clamp(chance, 0, 90);
        }
    }

    private int GetMaxHelpRequestStepsForCurrentWorldStage()
    {
        return WorldProgression.Current().ResidentStage switch
        {
            "first_spring_newcomer" => 1,
            "first_year_settling_in" => 2,
            _ => 3
        };
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
            "deeper_relationship" => "deeper_relationship",
            _ => "none"
        };
    }

    internal static string NormalizeSharedExperienceType(string type)
    {
        return type?.Trim().ToLowerInvariant() == "help_request"
            ? "help_request"
            : "none";
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

    internal static string NormalizeTravelLocation(string value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (TravelLocationAliases.TryGetValue(candidate, out string? mapped))
        {
            return mapped;
        }

        return string.IsNullOrWhiteSpace(candidate) ? "Town" : candidate;
    }

    private static string GetTravelLocationLabel(string locationName)
    {
        return locationName switch
        {
            "Farm" => "the farm",
            "Town" => "Pelican Town",
            "Mountain" => "the mountain",
            "Mine" => "the mines",
            "Beach" => "the beach",
            "Forest" => "Cindersap Forest",
            "BusStop" => "the bus stop",
            "Trailer" => "Penny and Pam's trailer",
            "JoshHouse" => "Alex's house",
            "HaleyHouse" => "Haley and Emily's house",
            "SamHouse" => "Sam's house",
            "ScienceHouse" => "Robin's house",
            "LeahHouse" => "Leah's cottage",
            "AnimalShop" => "Marnie's ranch",
            "ElliottHouse" => "Elliott's cabin",
            "Blacksmith" => "the blacksmith",
            "FishShop" => "the fish shop",
            "WizardHouse" => "the Wizard's tower",
            "Tent" => "Linus's tent",
            "Saloon" => "the Stardrop Saloon",
            "SeedShop" => "Pierre's General Store",
            "ArchaeologyHouse" => "the museum and library",
            "Hospital" => "the clinic",
            _ => locationName
        };
    }

    private static string BuildDialogueBehaviorInfluenceKey(string type, string summary, string targetLocation)
    {
        string normalizedType = NormalizeDialogueBehaviorInfluenceType(type);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string normalizedLocation = NormalizeTravelLocation(targetLocation, string.Empty);
        return normalizedType == "none" || string.IsNullOrWhiteSpace(normalizedSummary)
            ? string.Empty
            : $"{normalizedType}:{normalizedSummary}:{normalizedLocation}";
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
