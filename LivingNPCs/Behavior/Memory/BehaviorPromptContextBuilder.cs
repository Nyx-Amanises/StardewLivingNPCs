using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class BehaviorPromptContextBuilder
{
    public static string BuildPromptContext(
        NPC npc,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        LivingNpcState? state,
        WorldContextSnapshot world,
        NpcDispositionProfile disposition,
        EmotionalExpressionCue emotionalStyle,
        MemoryRecallPlan recallPlan,
        IReadOnlyList<CommunityImpressionSelection> communityImpressions,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int currentTotalDays)
    {
        if (ModEntry.ActiveConfig.ConcisePromptContext)
        {
            return BuildConcisePromptContext(
                npc, recentEntries, state, world, disposition, emotionalStyle, recallPlan,
                maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, currentTotalDays);
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
        prompt.AppendLine($"- {BuildConversationStance(npc, state, disposition, world, emotionalStyle)}");

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
            prompt.AppendLine($"- Durable memory store: {state.LongTermMemories.Count} long-term memories tracked; recall focus for this reply: {MemoryRecallService.FormatLongTermMemoryPromptLabel(recallPlan.LongTermMemories)}.");
            prompt.AppendLine($"- Known farmer preferences: {MemoryRecallService.FormatPlayerPreferencePromptLabel(recallPlan.PlayerPreferences)}.");
            prompt.AppendLine($"- Conversation-driven behavior tendencies: {state.DialogueBehaviorInfluencePromptLabel}.");
            prompt.AppendLine($"- Shared experiences with the farmer: {state.SharedExperiencePromptLabel}.");
            prompt.AppendLine($"- Help requests involving the farmer: {state.HelpRequestPromptLabel}.");
            prompt.AppendLine($"- Community impressions about the farmer's ties with other NPCs: {MemoryRecallService.FormatCommunityImpressionPromptLabel(npc, communityImpressions)}.");
            prompt.AppendLine($"- Stable community circles this NPC belongs to: {FormatSocialCirclePromptLabel(npc)}.");
            prompt.AppendLine("- Help-request lifecycle: Offered means the NPC has asked but the farmer has not accepted; Pending means accepted and active; only Pending requests should be treated like tasks.");
            prompt.AppendLine($"- Help-request readiness: {BuildHelpRequestReadinessLabel(state, world, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, currentTotalDays)}.");
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

        var priorityContext = BuildPriorityPromptContext(npc, state, world, recentEntries, recallPlan, communityImpressions, emotionalStyle, currentTotalDays).ToList();
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
                prompt.AppendLine($"- {FormatPromptEntry(entry)}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("Next reply guidance:");
        foreach (string guidance in BuildReplyGuidance(state, world, emotionalStyle, currentTotalDays))
        {
            prompt.AppendLine($"- {guidance}");
        }

        return prompt.ToString();
    }

    private static string BuildConcisePromptContext(
        NPC npc,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        LivingNpcState? state,
        WorldContextSnapshot world,
        NpcDispositionProfile disposition,
        EmotionalExpressionCue emotionalStyle,
        MemoryRecallPlan recallPlan,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int currentTotalDays)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"## LivingNPCs Context: {npc.displayName}");
        prompt.AppendLine("Rules: hidden body language and memory for the next in-character reply; do not quote it or mention LivingNPCs/AI/JSON; use at most one or two details naturally.");
        prompt.AppendLine();
        prompt.AppendLine("Current state:");
        prompt.AppendLine($"- Disposition: {disposition.PromptLabel}.");
        prompt.AppendLine($"- Scene: {world.PromptLabel}; location: {world.LocationDisplayName}; {world.Season} {world.DayOfMonth}, {world.TimeOfDay}.");

        if (state == null)
        {
            prompt.AppendLine("- No persistent LivingNPCs state yet; use disposition and scene conservatively.");
            return prompt.ToString().TrimEnd();
        }

        prompt.AppendLine($"- Mood: {state.Mood}; emotion: {state.EmotionPromptLabel}; inclination: {state.CurrentInclination}.");
        prompt.AppendLine($"- Emotional expression style: {emotionalStyle.PromptLabel}.");
        prompt.AppendLine($"- Familiarity {state.Familiarity}/100; trust: {state.RelationshipTrustPromptLabel}; rhythm: {state.InteractionRhythmPromptLabel}.");
        AppendIfMeaningful(prompt, "Recall focus", MemoryRecallService.FormatLongTermMemoryPromptLabel(recallPlan.LongTermMemories));
        AppendIfMeaningful(prompt, "Known farmer preferences", MemoryRecallService.FormatPlayerPreferencePromptLabel(recallPlan.PlayerPreferences));
        AppendIfMeaningful(prompt, "Behavior tendencies", state.DialogueBehaviorInfluencePromptLabel);
        AppendIfMeaningful(prompt, "Recent gift", state.LastGiftPromptLabel);
        AppendIfMeaningful(prompt, "Recent event", state.LastEventPromptLabel);
        AppendIfMeaningful(prompt, "Shared experiences", state.SharedExperiencePromptLabel);
        AppendIfMeaningful(prompt, "Conflict", state.ConflictPromptLabel);
        AppendIfMeaningful(prompt, "Personal memory", state.FarmerNicknamePromptLabel);

        bool helpRelevant = state.HelpRequests.Any(request => request.Status is "Offered" or "Pending")
            || state.DailyHelpRequestOpportunityTotalDays == currentTotalDays
            || HelpRequestReadinessRules.Evaluate(
                state,
                world.FriendshipHearts,
                maxPendingHelpRequestsPerNpc,
                helpRequestCooldownDays,
                currentTotalDays).Allowed;
        if (helpRelevant)
        {
            AppendIfMeaningful(prompt, "Help requests", state.HelpRequestPromptLabel);
            prompt.AppendLine("- Help-request lifecycle: Offered = asked but not accepted; Pending = accepted/active; only Pending is a task.");
            prompt.AppendLine($"- Help-request readiness: {BuildHelpRequestReadinessLabel(state, world, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, currentTotalDays)}.");
            prompt.AppendLine($"- Help-request fit: {HelpRequestAdvisor.BuildPromptLabel(npc, world.Progression)}");
        }

        AppendIfMeaningful(prompt, "Last interaction", state.LastInteraction);

        if (recentEntries.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("Recent tracked moments, oldest to newest:");
            foreach (var entry in recentEntries.TakeLast(3))
            {
                prompt.AppendLine($"- {FormatPromptEntry(entry)}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine("- Reply guidance: let the mood, emotion, and relationship pace above shape tone and word choice; surface at most one or two details, and keep references to town progress, the farmer's household, and shared history consistent with what this NPC plausibly knows.");

        return prompt.ToString().TrimEnd();
    }

    private static void AppendIfMeaningful(StringBuilder prompt, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // The empty form of these labels reads as "no recent…/no durable…/no shared…"; skip those
        // so the concise context only carries lines that actually say something.
        if (value.TrimStart().StartsWith("no ", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        prompt.AppendLine($"- {label}: {value}");
    }

    public static string BuildDebugSummary(
        NPC npc,
        IReadOnlyList<BehaviorMemoryEntry> entries,
        int maxEntries,
        LivingNpcState? state,
        WorldContextSnapshot world,
        NpcDispositionProfile disposition,
        EmotionalExpressionCue emotionalStyle,
        MemoryRecallPlan recallPlan,
        IReadOnlyList<CommunityImpressionSelection> communityImpressions)
    {
        if (entries.Count == 0 && state == null)
        {
            var emptySummary = new StringBuilder();
            emptySummary.AppendLine(I18n.Get("debug.summary.noState", new { npc = npc.displayName }));
            AppendDebugBullet(emptySummary, "debug.label.behaviorDisposition", disposition.DebugLabel);
            AppendDebugBullet(emptySummary, "debug.label.emotionStyle", emotionalStyle.DebugSummaryLabel);
            AppendDebugBullet(emptySummary, "debug.label.currentScene", world.DebugLabel);
            AppendDebugBullet(emptySummary, "debug.label.worldProgression", world.Progression.DebugLabel);
            AppendDebugBullet(emptySummary, "debug.label.npcKnowledge", world.ProgressionKnowledge.DebugLabel);
            return emptySummary.ToString().TrimEnd();
        }

        var summary = new StringBuilder();
        if (state != null)
        {
            var latestBehavior = entries
                .LastOrDefault(entry => string.Equals(entry.Kind, "Behavior", System.StringComparison.OrdinalIgnoreCase));
            summary.AppendLine(I18n.Get("debug.summary.currentState", new { npc = npc.displayName }));
            AppendDebugBullet(summary, "debug.label.mood", state.MoodLabel);
            AppendDebugBullet(summary, "debug.label.interpersonalEmotion", state.EmotionLabel);
            AppendDebugBullet(summary, "debug.label.attention", $"{state.AttentionLabel} ({state.Attention})");
            AppendDebugBullet(summary, "debug.label.familiarity", $"{state.FamiliarityLabel} ({state.Familiarity})");
            AppendDebugBullet(summary, "debug.label.relationshipTrust", state.RelationshipTrustDebugLabel);
            AppendDebugBullet(summary, "debug.label.interactionRhythm", state.InteractionRhythmLabel);
            AppendDebugBullet(summary, "debug.label.interactionComfort", state.InteractionComfortTierLabel);
            AppendDebugBullet(summary, "debug.label.lastGift", state.LastGiftLabel);
            AppendDebugBullet(summary, "debug.label.lastEvent", state.LastEventLabel);
            AppendDebugBullet(summary, "debug.label.longTermMemory", state.LongTermMemoryDebugLabel);
            AppendDebugBullet(summary, "debug.label.currentLongTermRecall", MemoryRecallService.FormatLongTermMemoryDebugLabel(recallPlan.LongTermMemories));
            AppendDebugBullet(summary, "debug.label.playerPreferenceMemory", state.PlayerPreferenceDebugLabel);
            AppendDebugBullet(summary, "debug.label.currentPreferenceRecall", MemoryRecallService.FormatPlayerPreferenceDebugLabel(recallPlan.PlayerPreferences));
            AppendDebugBullet(summary, "debug.label.communityTone", CommunityReactionStyle.For(npc).DebugLabel);
            AppendDebugBullet(summary, "debug.label.emotionStyle", emotionalStyle.DebugSummaryLabel);
            AppendDebugBullet(summary, "debug.label.socialCircles", FormatSocialCircleDebugLabel(npc));
            AppendDebugBullet(summary, "debug.label.communityImpression", state.CommunityImpressionDebugLabel);
            AppendDebugBullet(summary, "debug.label.currentCommunityRecall", MemoryRecallService.FormatCommunityImpressionDebugLabel(communityImpressions));
            AppendDebugBullet(summary, "debug.label.dialogueBehavior", state.DialogueBehaviorInfluenceDebugLabel);
            AppendDebugBullet(summary, "debug.label.latestBehaviorChoice", FormatLatestBehaviorChoiceDebugLabel(latestBehavior));
            AppendDebugBullet(summary, "debug.label.sharedExperience", state.SharedExperienceDebugLabel);
            AppendDebugBullet(summary, "debug.label.helpRequests", state.HelpRequestDebugLabel);
            AppendDebugBullet(summary, "debug.label.helpRequestFit", HelpRequestAdvisor.BuildDebugLabel(npc, world.Progression));
            AppendDebugBullet(summary, "debug.label.conflictMemory", state.ConflictDebugLabel);
            AppendDebugBullet(summary, "debug.label.nicknameMemory", state.FarmerNicknameLabel);
            AppendDebugBullet(summary, "debug.label.aiFriendshipToday", state.AiFriendshipGainedToday.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendDebugBullet(summary, "debug.label.profileSource", disposition.SourceDebugLabel);
            AppendDebugBullet(summary, "debug.label.behaviorDisposition", disposition.DebugLabel);
            AppendDebugBullet(summary, "debug.label.emotionStyle", emotionalStyle.DebugSummaryLabel);
            AppendDebugBullet(summary, "debug.label.currentScene", world.DebugLabel);
            AppendDebugBullet(summary, "debug.label.worldProgression", world.Progression.DebugLabel);
            AppendDebugBullet(summary, "debug.label.npcKnowledge", world.ProgressionKnowledge.DebugLabel);
            AppendDebugBullet(summary, "debug.label.sceneInfluence", state.LastSceneInfluenceLabel);
            AppendDebugBullet(summary, "debug.label.responseInclination", state.InclinationLabel);
            AppendDebugBullet(summary, "debug.label.lastInteraction", state.LastInteractionLabel);
        }
        else
        {
            summary.AppendLine(I18n.Get("debug.summary.currentDisposition", new { npc = npc.displayName }));
            AppendDebugBullet(summary, "debug.label.profileSource", disposition.SourceDebugLabel);
            AppendDebugBullet(summary, "debug.label.behaviorDisposition", disposition.DebugLabel);
            AppendDebugBullet(summary, "debug.label.currentScene", world.DebugLabel);
            AppendDebugBullet(summary, "debug.label.worldProgression", world.Progression.DebugLabel);
            AppendDebugBullet(summary, "debug.label.npcKnowledge", world.ProgressionKnowledge.DebugLabel);
        }

        if (entries.Count > 0)
        {
            if (summary.Length > 0)
            {
                summary.AppendLine();
            }

            summary.AppendLine(I18n.Get("debug.summary.recentEntries", new { npc = npc.displayName, count = System.Math.Min(entries.Count, maxEntries) }));
            foreach (var entry in entries.TakeLast(maxEntries))
            {
                string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
                string locationSuffix = string.IsNullOrWhiteSpace(location)
                    ? string.Empty
                    : I18n.Get("debug.summary.entryLocationSuffix", new { location });
                summary.AppendLine(I18n.Get(
                    "debug.summary.entry",
                    new
                    {
                        day = entry.TotalDays,
                        time = entry.TimeOfDay,
                        location = locationSuffix,
                        kind = FormatDebugKind(entry),
                        action = entry.Action,
                        reason = entry.Reason
                    }));
            }
        }

        return summary.ToString().TrimEnd();
    }

    private static string BuildConversationStance(
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

        string tone = BuildToneCue(state);
        string rhythm = BuildRhythmCue(state);
        string scenePressure = world.StateInfluence.HasMood
            ? $"scene pressure suggests {world.StateInfluence.Mood}/{world.StateInfluence.Inclination}"
            : "scene pressure is mild";

        return $"{npc.displayName} should sound {tone}; temperament is {disposition.PromptLabel}; emotional expression style is {emotionalStyle.PromptLabel}; profile source is {disposition.SourceLabel}; relationship is {state.FamiliarityPromptLabel}; {rhythm}; {scenePressure}.";
    }

    private static IEnumerable<string> BuildPriorityPromptContext(
        NPC npc,
        LivingNpcState? state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        MemoryRecallPlan recallPlan,
        IReadOnlyList<CommunityImpressionSelection> communityImpressions,
        EmotionalExpressionCue emotionalStyle,
        int currentTotalDays)
    {
        if (state != null)
        {
            int giftAge = GetMemoryAge(state.LastGiftTotalDays, currentTotalDays);
            if (!string.IsNullOrWhiteSpace(state.LastGiftName) && giftAge <= 7)
            {
                string freshness = FormatMemoryAge(state.LastGiftTotalDays, currentTotalDays);
                yield return $"Gift memory: the farmer offered {state.LastGiftName} {freshness}; taste was {state.LastGiftTaste}; let this affect warmth, surprise, or distance only if relevant.";
            }

            int eventAge = GetMemoryAge(state.LastEventTotalDays, currentTotalDays);
            if (!string.IsNullOrWhiteSpace(state.LastEventContext) && eventAge <= 3)
            {
                string freshness = FormatMemoryAge(state.LastEventTotalDays, currentTotalDays);
                yield return $"Event memory: {state.LastEventContext} ({freshness}); acknowledge only if the conversation naturally continues it.";
            }

            if (!string.IsNullOrWhiteSpace(state.FarmerNickname))
            {
                yield return $"Personal name memory: {state.FarmerNicknamePromptLabel}.";
            }

            if (recallPlan.LongTermMemories.Count > 0)
            {
                yield return $"Relevant long-term memories for this reply: {MemoryRecallService.FormatLongTermMemoryPromptLabel(recallPlan.LongTermMemories)}; use at most one if it naturally matters now.";
            }

            if (recallPlan.PlayerPreferences.Count > 0)
            {
                yield return $"Relevant farmer preference memories for this reply: {MemoryRecallService.FormatPlayerPreferencePromptLabel(recallPlan.PlayerPreferences)}; when a gift or topic naturally matches one, it is okay to acknowledge remembering it briefly.";
            }

            if (communityImpressions.Count > 0)
            {
                yield return $"Community impressions: {MemoryRecallService.FormatCommunityImpressionPromptLabel(npc, communityImpressions)}; use at most one, keep indirect reports tentative, and do not reveal knowledge the NPC would not plausibly have.";
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
                && request.FulfilledTotalDays >= currentTotalDays - 3
                && request.LastMentionedTotalDays < 0
            );
            if (recentlyFulfilledHelpRequest != null)
            {
                yield return $"Recently fulfilled help request: {recentlyFulfilledHelpRequest.FulfilledPromptLabel}; if it fits, the NPC may briefly thank the farmer for following through.";
            }

            var expiredHelpRequest = state.HelpRequests.FirstOrDefault(request =>
                request.Status == "Expired"
                && request.LastMentionedTotalDays < currentTotalDays);
            if (expiredHelpRequest != null)
            {
                string reaction = string.IsNullOrWhiteSpace(expiredHelpRequest.FailureReaction)
                    ? "the NPC noticed the request did not work out"
                    : expiredHelpRequest.FailureReaction;
                yield return $"Unfinished help request: {expiredHelpRequest.PromptLabel}; reaction: {reaction}; the next conversation may acknowledge this according to personality, without over-punishing if the farmer never accepted.";
            }

            var sharedExperience = state.SharedExperiences.FirstOrDefault(experience =>
                experience.FollowUpEligibleTotalDays <= currentTotalDays
                && experience.FollowUpShownTotalDays < 0
                && experience.CreatedTotalDays >= currentTotalDays - 7
            );
            if (sharedExperience != null)
            {
                yield return $"Shared experience: {sharedExperience.PromptLabel}; if it fits, the NPC may acknowledge the time they spent together without formally reciting the memory.";
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
                && conflict.ResolvedTotalDays >= currentTotalDays - 3
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

    private static IEnumerable<string> BuildReplyGuidance(
        LivingNpcState? state,
        WorldContextSnapshot world,
        EmotionalExpressionCue emotionalStyle,
        int currentTotalDays)
    {
        if (state == null)
        {
            yield return "Let the reply be scene-aware and modest because there is no persistent state yet.";
            yield return "Keep continuity subtle; do not invent strong feelings from weak context.";
            yield return $"Emotion expression style: {emotionalStyle.ReplyGuidance}.";
            yield break;
        }

        yield return $"Tone target: {BuildToneCue(state)}.";
        yield return $"Emotion expression style: {emotionalStyle.ReplyGuidance}.";
        yield return $"Relationship pacing: {state.InteractionComfortTierPromptLabel}.";
        yield return $"Disclosure pacing: {state.SecretSharingPromptLabel}.";
        yield return $"Invitation policy: {BuildTravelInvitationPolicyPromptLabel(state)}.";

        if (!string.IsNullOrWhiteSpace(state.LastGiftName) && state.LastGiftTotalDays == currentTotalDays)
        {
            yield return "If the gift is conversationally relevant, a brief natural acknowledgement is allowed; avoid turning the whole reply into gift analysis.";
        }

        if (state.PlayerPreferenceMemories.Count > 0)
        {
            yield return "If choosing to mention a remembered farmer preference, keep it short and human, not like reciting a profile.";
        }

        if (state.SharedExperiences.Any(experience =>
                experience.FollowUpEligibleTotalDays <= currentTotalDays
                && experience.FollowUpShownTotalDays < 0))
        {
            yield return "Completed shared experiences can deepen continuity; acknowledge them naturally without turning them into a formal recap or future plan.";
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
                && conflict.ResolvedTotalDays >= currentTotalDays - 3
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

    private static string BuildHelpRequestReadinessLabel(
        LivingNpcState state,
        WorldContextSnapshot world,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int currentTotalDays)
    {
        var result = HelpRequestReadinessRules.Evaluate(
            state,
            world.FriendshipHearts,
            maxPendingHelpRequestsPerNpc,
            helpRequestCooldownDays,
            currentTotalDays
        );
        return result.Allowed
            ? $"may naturally ask for one modest favor now; {result.Reason}; if you do ask, ask once and clearly, then let the farmer reply — do not also withdraw it or answer for them in the same message"
            : $"should not open a new help request now ({result.Reason}); even if the farmer offers to help, gently decline or deflect rather than naming a task, accepting the favor, or committing to one";
    }

    private static string BuildTravelInvitationPolicyPromptLabel(LivingNpcState state)
    {
        string relationshipPolicy = state.InteractionComfortTier switch
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
        return $"{relationshipPolicy}; ordinary daily schedule stops are soft constraints for LivingNPCs companion outings, so do not decline only because of a normal future destination; if the requested destination matches a current or upcoming ordinary schedule stop, treating it as going together or showing the farmer the way is especially natural; still refuse during events, sleep, severe conflict, unsafe scenes, or truly story-critical obligations";
    }

    private static string BuildToneCue(LivingNpcState state)
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

    private static string BuildRhythmCue(LivingNpcState state)
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

    private static int GetMemoryAge(int totalDays, int currentTotalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - totalDays);
    }

    private static string FormatMemoryAge(int totalDays, int currentTotalDays)
    {
        int age = GetMemoryAge(totalDays, currentTotalDays);
        return age switch
        {
            0 => "today",
            1 => "yesterday",
            int.MaxValue => "at an unknown time",
            _ => $"{age} days ago"
        };
    }

    private static string FormatLatestBehaviorChoiceDebugLabel(BehaviorMemoryEntry? entry)
    {
        if (entry == null)
        {
            return I18n.Get("debug.latestBehavior.none");
        }

        string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
        string locationPart = string.IsNullOrWhiteSpace(location)
            ? string.Empty
            : I18n.Get("debug.latestBehavior.location", new { location });
        return I18n.Get("debug.latestBehavior.value", new { action = entry.Action, day = entry.TotalDays, time = entry.TimeOfDay, location = locationPart, reason = entry.Reason });
    }

    private static string FormatPromptEntry(BehaviorMemoryEntry entry)
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

    private static string FormatSocialCircleDebugLabel(NPC npc)
    {
        var labels = NpcSocialGraph.GetStableCircleLabels(npc.Name);
        return labels.Count == 0
            ? I18n.Get("debug.socialCircles.none")
            : string.Join(I18n.Get("debug.listSeparator"), labels);
    }

    private static string FormatSocialCirclePromptLabel(NPC npc)
    {
        var labels = NpcSocialGraph.GetStableCircleLabels(npc.Name);
        return labels.Count == 0
            ? "no stable small-circle affiliation is currently tracked"
            : string.Join(", ", labels);
    }

    private static string FormatDebugKind(BehaviorMemoryEntry entry)
    {
        return entry.Kind.ToLowerInvariant() switch
        {
            "conversation" => I18n.Get("debug.kind.conversation"),
            "gift" => I18n.Get("debug.kind.gift"),
            "event" => I18n.Get("debug.kind.event"),
            "longtermmemory" => I18n.Get("debug.kind.longTermMemory"),
            "helprequest" => I18n.Get("debug.kind.helpRequest"),
            "helprequestupdate" => I18n.Get("debug.kind.helpRequestUpdate"),
            "conflict" => I18n.Get("debug.kind.conflict"),
            "npcaction" => I18n.Get("debug.kind.npcAction"),
            "socialmemory" => I18n.Get("debug.kind.socialMemory"),
            _ => I18n.Get("debug.kind.behavior")
        };
    }

    private static void AppendDebugBullet(StringBuilder summary, string labelKey, string value)
    {
        summary.AppendLine(I18n.Get("debug.summary.bullet", new { label = I18n.Get(labelKey), value }));
    }
}
