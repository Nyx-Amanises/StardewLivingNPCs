using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemory
{
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

    public ValleyTalkExchangeResult RecordValleyTalkExchange(
        NPC npc,
        string playerText,
        string npcResponse,
        string analysisJson,
        int maxEntriesPerNpc,
        int maxExtraFriendshipPerDay)
    {
        var analysis = ParseExchangeAnalysis(analysisJson);
        var state = this.GetOrCreateState(npc);
        int storedMemories = 0;
        int storedPlayerPreferences = 0;

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

        // Keep one deterministic fallback for nickname requests if the model forgets metadata.
        if (storedMemories == 0 && storedPlayerPreferences == 0 && TryExtractNicknameRequest(playerText, out string nickname))
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
        if (storedMemories > 0 || storedPlayerPreferences > 0 || appliedFriendship > 0)
        {
            state.LastInteraction = storedMemories > 0 || storedPlayerPreferences > 0
                ? "the farmer shared something worth remembering"
                : "the farmer had an AI conversation";
            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        }

        return new ValleyTalkExchangeResult(
            storedMemories,
            storedPlayerPreferences,
            appliedFriendship,
            analysis.RapportDelta,
            analysis.EndConversation,
            analysis.AmbientFollowUp.Text,
            analysis.AmbientFollowUp.DelayMinutes,
            analysis.Actions
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

        this.AddFamiliarity(state, familiarityGain, dailyCap: 8);
        state.Attention = LivingNpcState.ClampScore(state.Attention + attentionDelta);
        state.Openness = LivingNpcState.ClampScore(state.Openness + opennessDelta);
        this.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer offered a gift";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
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
        this.ApplyConversationRhythmInfluence(state);
        this.ApplyWorldStateInfluence(state, world);
        state.LastInteraction = "the farmer started a conversation";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public void DecayStates(int dailyDecay)
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

            if (dailyDecay <= 0)
            {
                continue;
            }

            state.Attention = LivingNpcState.MoveToward(state.Attention, 35, dailyDecay);
            state.Openness = LivingNpcState.MoveToward(state.Openness, 50, dailyDecay / 2);
            state.CurrentInclination = state.Attention >= 55 ? "Aware" : "Neutral";
            state.Mood = state.Openness >= 58 ? "Calm" : "Neutral";
            state.LastInteraction = "time passed";
            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        }
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

    public string BuildPromptContext(NPC npc, int maxEntries, bool includeState)
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
        if (state != null)
        {
            prompt.AppendLine($"- Mood: {state.Mood}; attention to farmer: {state.Attention}/100; response inclination: {state.CurrentInclination}.");
            prompt.AppendLine($"- Long-term familiarity with the farmer: {state.Familiarity}/100 ({state.FamiliarityPromptLabel}).");
            prompt.AppendLine($"- Relationship-aware interaction rhythm: {state.InteractionRhythmPromptLabel}; comfort tier: {state.InteractionComfortTierPromptLabel}.");
            prompt.AppendLine($"- Recent gift context: {state.LastGiftPromptLabel}.");
            prompt.AppendLine($"- Recent event context: {state.LastEventPromptLabel}.");
            prompt.AppendLine($"- Long-term personal memory: {state.LongTermMemoryPromptLabel}.");
            prompt.AppendLine($"- Known farmer preferences: {state.PlayerPreferencePromptLabel}.");
            prompt.AppendLine($"- Personal memory context: {state.FarmerNicknamePromptLabel}.");
            prompt.AppendLine($"- Scene influence on mood: {state.LastSceneInfluenceReason}.");
            prompt.AppendLine($"- Last interaction: {state.LastInteraction}.");
        }
        else
        {
            prompt.AppendLine("- No persistent LivingNPCs state exists yet; use disposition and scene context conservatively.");
        }

        var priorityContext = this.BuildPriorityPromptContext(state, world, recentEntries).ToList();
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
        LivingNpcState? state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries)
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

            if (state.LongTermMemories.Count > 0)
            {
                yield return $"Long-term memory: {state.LongTermMemoryPromptLabel}; use only if one detail naturally matters now.";
            }

            if (state.PlayerPreferenceMemories.Count > 0)
            {
                yield return $"Farmer preference memory: {state.PlayerPreferencePromptLabel}; when a gift or topic naturally matches one of these, it is okay to acknowledge remembering it briefly.";
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

        if (!string.IsNullOrWhiteSpace(state.LastGiftName) && state.LastGiftTotalDays == Game1.Date.TotalDays)
        {
            yield return "If the gift is conversationally relevant, a brief natural acknowledgement is allowed; avoid turning the whole reply into gift analysis.";
        }

        if (state.PlayerPreferenceMemories.Count > 0)
        {
            yield return "If choosing to mention a remembered farmer preference, keep it short and human, not like reciting a profile.";
        }

        if (state.RepeatedConversationPressure >= 20)
        {
            yield return "Because the farmer has been checking in repeatedly, it is okay to sound brief, amused, busy, or gently boundary-setting.";
        }

        if (world.StateInfluence.HasMood)
        {
            yield return $"Let the scene nudge tone through {world.StateInfluence.Reason}, without explicitly explaining the scene mechanics.";
        }
    }

    private string BuildToneCue(LivingNpcState state)
    {
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

        return $"{state.Mood.ToLowerInvariant()}, {attention}, and {openness}";
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
            return $"{npc.displayName} 还没有 LivingNPCs 行为/互动记忆或状态。\n- 行为倾向：{disposition.DebugLabel}\n- 当前场景：{world.DebugLabel}";
        }

        var summary = new StringBuilder();
        if (state != null)
        {
            summary.AppendLine($"{npc.displayName} 当前 LivingNPCs 状态：");
            summary.AppendLine($"- 心情：{state.MoodLabel}");
            summary.AppendLine($"- 对玩家注意度：{state.AttentionLabel} ({state.Attention})");
            summary.AppendLine($"- 对玩家熟悉度：{state.FamiliarityLabel} ({state.Familiarity})");
            summary.AppendLine($"- 互动节奏：{state.InteractionRhythmLabel}");
            summary.AppendLine($"- 互动舒适度：{state.InteractionComfortTierLabel}");
            summary.AppendLine($"- 最近礼物：{state.LastGiftLabel}");
            summary.AppendLine($"- 最近事件：{state.LastEventLabel}");
            summary.AppendLine($"- 长期记忆：{state.LongTermMemoryDebugLabel}");
            summary.AppendLine($"- 玩家偏好记忆：{state.PlayerPreferenceDebugLabel}");
            summary.AppendLine($"- 长期称呼记忆：{state.FarmerNicknameLabel}");
            summary.AppendLine($"- 今日 AI 对话额外好感：{state.AiFriendshipGainedToday}");
            summary.AppendLine($"- 角色资料：{disposition.SourceDebugLabel}");
            summary.AppendLine($"- 行为倾向：{disposition.DebugLabel}");
            summary.AppendLine($"- 当前场景：{world.DebugLabel}");
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
        return state;
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
            "npcaction" => "npc action",
            _ => "behavior"
        };

        return $"{entry.Season} {entry.Day}, {entry.TimeOfDay}{locationSuffix}: {kind} - {entry.Action}; reason: {entry.Reason}";
    }

    private string FormatDebugKind(BehaviorMemoryEntry entry)
    {
        return entry.Kind.ToLowerInvariant() switch
        {
            "conversation" => "[对话]",
            "gift" => "[礼物]",
            "event" => "[事件]",
            "longtermmemory" => "[长期记忆]",
            "npcaction" => "[NPC动作]",
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
            analysis.Memories = analysis.Memories
                .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
                .Select(memory =>
                {
                    memory.Kind = NormalizeLongTermMemoryKind(memory.Kind);
                    memory.Summary = memory.Summary.Trim();
                    memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
                    memory.PlayerPreferenceKind = NormalizePlayerPreferenceKind(memory.PlayerPreferenceKind);
                    memory.Subject = memory.Subject?.Trim() ?? string.Empty;
                    memory.Tags = NormalizePlayerPreferenceTags(memory.Tags);
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
                    return action;
                })
                .Where(action => action.Type != "none")
                .Take(1)
                .ToList();
            return analysis;
        }
        catch
        {
            return new ValleyTalkExchangeAnalysis();
        }
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
            existing.Summary = candidate.Summary.Trim();
            existing.Importance = System.Math.Max(existing.Importance, candidate.Importance);
            existing.Tags = existing.Tags
                .Concat(candidate.Tags)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact
        {
            PreferenceKind = candidate.PlayerPreferenceKind,
            Subject = candidate.Subject.Trim(),
            Summary = candidate.Summary.Trim(),
            Tags = candidate.Tags.ToList(),
            Importance = candidate.Importance,
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            TimesReinforced = 1
        });

        state.PlayerPreferenceMemories = state.PlayerPreferenceMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(16)
            .ToList();
        return true;
    }

    private bool StoreLongTermMemory(LivingNpcState state, ValleyTalkMemoryCandidate candidate)
    {
        string normalizedSummary = NormalizeMemorySummary(candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedSummary))
        {
            return false;
        }

        var existing = state.LongTermMemories.FirstOrDefault(memory =>
            NormalizeMemorySummary(memory.Summary) == normalizedSummary);
        if (existing != null)
        {
            existing.Kind = candidate.Kind;
            existing.Importance = System.Math.Max(existing.Importance, candidate.Importance);
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            this.TryUpdateNicknameStateFromMemory(state, existing);
            return true;
        }

        state.LongTermMemories.Add(new LongTermMemoryFact
        {
            Kind = candidate.Kind,
            Summary = candidate.Summary.Trim(),
            Importance = candidate.Importance,
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            TimesReinforced = 1
        });

        state.LongTermMemories = state.LongTermMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(12)
            .ToList();

        this.TryUpdateNicknameStateFromMemory(state, state.LongTermMemories.LastOrDefault(memory =>
            NormalizeMemorySummary(memory.Summary) == normalizedSummary));
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

    private static string NormalizeWorldActionType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "give_small_gift" => "give_small_gift",
            "give_meaningful_gift" => "give_meaningful_gift",
            "give_money" => "give_money",
            "water_nearby_crops" => "water_nearby_crops",
            "walk_together" => "walk_together",
            _ => "none"
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
    string ItemName,
    string TasteLabel,
    string TastePromptLabel,
    int TasteScore
);

internal sealed class ValleyTalkExchangeAnalysis
{
    public int RapportDelta { get; set; }
    public bool EndConversation { get; set; }
    public ValleyTalkAmbientFollowUp AmbientFollowUp { get; set; } = new();
    public List<ValleyTalkWorldActionRequest> Actions { get; set; } = new();
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
    public string Summary { get; set; } = string.Empty;
    public int Importance { get; set; }
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
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
    public int TimesReinforced { get; set; }
}

internal sealed class ValleyTalkAmbientFollowUp
{
    public string Text { get; set; } = string.Empty;
    public int DelayMinutes { get; set; }
}

internal sealed class ValleyTalkWorldActionRequest
{
    public string Type { get; set; } = "none";
    public int Amount { get; set; }
    public int TileCount { get; set; }
    public int DurationMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed record ValleyTalkExchangeResult(
    int LongTermMemoriesStored,
    int PlayerPreferencesStored,
    int AppliedFriendshipDelta,
    int RequestedFriendshipDelta,
    bool EndConversation,
    string AmbientFollowUpText,
    int AmbientFollowUpDelayMinutes,
    IReadOnlyList<ValleyTalkWorldActionRequest> Actions
)
{
    public bool HasEffect => this.LongTermMemoriesStored > 0
        || this.PlayerPreferencesStored > 0
        || this.AppliedFriendshipDelta > 0
        || !string.IsNullOrWhiteSpace(this.AmbientFollowUpText)
        || this.Actions.Count > 0;
}

internal sealed class LivingNpcState
{
    public string NpcName { get; set; } = string.Empty;
    public string Mood { get; set; } = "Neutral";
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
    public int AiFriendshipGainedToday { get; set; }
    public int LastAiFriendshipTotalDays { get; set; } = -1;
    public int LastAiSmallGiftTotalDays { get; set; } = -1;
    public int LastAiMeaningfulGiftTotalDays { get; set; } = -1;
    public int LastAiMoneyGiftTotalDays { get; set; } = -1;
    public int LastAiFarmHelpTotalDays { get; set; } = -1;
    public int LastAiWalkTogetherTotalDays { get; set; } = -1;
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
            .Select(memory =>
            {
                memory.Kind = string.IsNullOrWhiteSpace(memory.Kind) ? "fact" : memory.Kind;
                memory.Summary = memory.Summary.Trim();
                memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
                memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
                return memory;
            })
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(12)
            .ToList();
        this.PlayerPreferenceMemories ??= new List<PlayerPreferenceFact>();
        this.PlayerPreferenceMemories = this.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(memory =>
            {
                memory.PreferenceKind = BehaviorMemory.NormalizePlayerPreferenceKind(memory.PreferenceKind);
                memory.Subject = memory.Subject?.Trim() ?? string.Empty;
                memory.Summary = memory.Summary.Trim();
                memory.Tags = BehaviorMemory.NormalizePlayerPreferenceTags(memory.Tags);
                memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
                memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
                return memory;
            })
            .Where(memory => memory.PreferenceKind != "none")
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(16)
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
                    Summary = memory.Summary,
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
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
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            AiFriendshipGainedToday = this.AiFriendshipGainedToday,
            LastAiFriendshipTotalDays = this.LastAiFriendshipTotalDays,
            LastAiSmallGiftTotalDays = this.LastAiSmallGiftTotalDays,
            LastAiMeaningfulGiftTotalDays = this.LastAiMeaningfulGiftTotalDays,
            LastAiMoneyGiftTotalDays = this.LastAiMoneyGiftTotalDays,
            LastAiFarmHelpTotalDays = this.LastAiFarmHelpTotalDays,
            LastAiWalkTogetherTotalDays = this.LastAiWalkTogetherTotalDays,
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
}
