using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemory
{
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

            this.RefreshConversationDay(state);

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
        LivingNpcState? state = null;
        if (includeState)
        {
            this.statesByNpc.TryGetValue(npc.Name, out state);
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("## LivingNPCs behavior and interaction context");
        prompt.AppendLine($"{npc.displayName} has recent small in-world actions and player interaction moments tracked by LivingNPCs.");
        prompt.AppendLine("Use these as quiet scene context for the next reply. Do not mention LivingNPCs, prompts, mods, JSON, or AI systems.");
        prompt.AppendLine("If an entry is relevant, the character may naturally acknowledge it as something that just happened or affected the mood of the scene.");
        prompt.AppendLine($"Behavior disposition: {disposition.PromptLabel}.");
        prompt.AppendLine($"Current scene: {world.PromptLabel}; location: {world.LocationDisplayName}; date: {world.Season} {world.DayOfMonth}; time: {world.TimeOfDay}.");

        if (state != null)
        {
            prompt.AppendLine();
            prompt.AppendLine("Current lightweight NPC state:");
            prompt.AppendLine($"- Mood: {state.Mood}; attention to farmer: {state.Attention}/100; response inclination: {state.CurrentInclination}.");
            prompt.AppendLine($"- Long-term familiarity with the farmer: {state.Familiarity}/100 ({state.FamiliarityPromptLabel}).");
            prompt.AppendLine($"- Relationship-aware interaction rhythm: {state.InteractionRhythmPromptLabel}; comfort tier: {state.InteractionComfortTierPromptLabel}.");
            prompt.AppendLine($"- Scene influence on mood: {state.LastSceneInfluenceReason}.");
            prompt.AppendLine($"- Last interaction: {state.LastInteraction}.");
        }

        if (entries is { Count: > 0 })
        {
            prompt.AppendLine();
            prompt.AppendLine("Recent tracked moments:");
            foreach (var entry in entries.TakeLast(maxEntries))
            {
                prompt.AppendLine($"- {this.FormatPromptEntry(entry)}");
            }
        }

        prompt.AppendLine("Keep the next line in character and consistent with the current Stardew Valley scene.");
        return prompt.ToString();
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
            summary.AppendLine($"- 行为倾向：{disposition.DebugLabel}");
            summary.AppendLine($"- 当前场景：{world.DebugLabel}");
            summary.AppendLine($"- 情境影响：{state.LastSceneInfluenceLabel}");
            summary.AppendLine($"- 回应倾向：{state.InclinationLabel}");
            summary.AppendLine($"- 最近互动：{state.LastInteractionLabel}");
        }
        else
        {
            summary.AppendLine($"{npc.displayName} 当前 LivingNPCs 倾向：");
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
        string kind = string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase)
            ? "conversation"
            : "behavior";

        return $"{entry.Season} {entry.Day}, {entry.TimeOfDay}{locationSuffix}: {kind} - {entry.Action}; reason: {entry.Reason}";
    }

    private string FormatDebugKind(BehaviorMemoryEntry entry)
    {
        return string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase)
            ? "[对话]"
            : "[行为]";
    }

    private void RebuildDailyCounts()
    {
        foreach (var pair in this.entriesByNpc)
        {
            int count = pair.Value.Count(entry =>
                entry.TotalDays == Game1.Date.TotalDays
                && !string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase)
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
    public string LastSceneContext { get; set; } = "none";
    public string LastSceneInfluence { get; set; } = "none";
    public string LastSceneInfluenceReason { get; set; } = "none";
    public string CurrentInclination { get; set; } = "Neutral";
    public string LastInteraction { get; set; } = "none yet";
    public int LastUpdatedTotalDays { get; set; }
    public int LastUpdatedTimeOfDay { get; set; }

    public string MoodLabel => this.Mood switch
    {
        "Aware" => "注意到周围",
        "Attentive" => "专注",
        "Calm" => "放松",
        "Chilly" => "有些怕冷",
        "Comfortable" => "自在",
        "CrowdedButWarm" => "亲近但有点频繁",
        "Curious" => "好奇",
        "Engaged" => "投入",
        "Expressive" => "情绪外露",
        "Familiar" => "熟悉",
        "Focused" => "专注于事务",
        "Fresh" => "精神不错",
        "Guarded" => "警觉",
        "Hurried" => "匆忙",
        "Overloaded" => "有点应接不暇",
        "Polite" => "礼貌克制",
        "Public" => "留意公共场合",
        "Quiet" => "安静",
        "Sociable" => "有社交兴致",
        "Surprised" => "有些意外",
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

        if (string.IsNullOrWhiteSpace(this.InteractionRhythm))
        {
            this.InteractionRhythm = "New";
        }

        if (string.IsNullOrWhiteSpace(this.InteractionComfortTier))
        {
            this.InteractionComfortTier = "Distant";
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
            LastSceneContext = this.LastSceneContext,
            LastSceneInfluence = this.LastSceneInfluence,
            LastSceneInfluenceReason = this.LastSceneInfluenceReason,
            CurrentInclination = this.CurrentInclination,
            LastInteraction = this.LastInteraction,
            LastUpdatedTotalDays = this.LastUpdatedTotalDays,
            LastUpdatedTimeOfDay = this.LastUpdatedTimeOfDay
        };
    }
}
