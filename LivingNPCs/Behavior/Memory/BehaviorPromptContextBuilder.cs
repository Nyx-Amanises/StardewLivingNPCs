using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Assembles the hidden LivingNPCs context for ValleyTalk. This class only decides *which*
/// sections and cues appear and in what order; every English sentence it emits lives in
/// <see cref="PromptFragments"/> (Chinese debug text in <see cref="StateDebugLabels"/>).
/// </summary>
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
        int currentTotalDays,
        int currentTimeOfDay)
    {
        if (ModEntry.ActiveConfig.ConcisePromptContext)
        {
            return BuildConcisePromptContext(
                npc, recentEntries, state, world, disposition, emotionalStyle, recallPlan,
                maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, currentTotalDays);
        }

        var prompt = new StringBuilder();
        prompt.AppendLine(PromptFragments.Context.Header(npc.displayName));
        prompt.AppendLine(PromptFragments.Context.Purpose);
        prompt.AppendLine(PromptFragments.Context.RulesHeading);
        foreach (string rule in PromptFragments.Context.Rules)
        {
            prompt.AppendLine(rule);
        }

        prompt.AppendLine();
        prompt.AppendLine(PromptFragments.Context.StanceHeading);
        prompt.AppendLine($"- {PromptFragments.Context.ConversationStance(npc, state, disposition, world, emotionalStyle)}");

        prompt.AppendLine();
        prompt.AppendLine(PromptFragments.Context.CurrentStateHeading);
        prompt.AppendLine(PromptFragments.Context.ProfileSourceLine(disposition.SourceLabel));
        prompt.AppendLine(PromptFragments.Context.DispositionLine(disposition.PromptLabel));
        if (disposition.HasProfileContext)
        {
            if (!string.IsNullOrWhiteSpace(disposition.BackgroundPrompt))
            {
                prompt.AppendLine(PromptFragments.Context.BackgroundLine(disposition.BackgroundPrompt));
            }

            if (!string.IsNullOrWhiteSpace(disposition.DialoguePrompt))
            {
                prompt.AppendLine(PromptFragments.Context.DialogueCueLine(disposition.DialoguePrompt));
            }
        }

        prompt.AppendLine(PromptFragments.Context.SceneLine(world));
        prompt.AppendLine(PromptFragments.Context.WorldKnowledgeLine(world.ProgressionKnowledge.PromptLabel));
        if (state != null)
        {
            prompt.AppendLine(PromptFragments.Context.MoodLine(state));
            prompt.AppendLine(PromptFragments.Context.EmotionLine(state));
            prompt.AppendLine(PromptFragments.Context.ExpressionStyleLine(emotionalStyle.PromptLabel));
            prompt.AppendLine(PromptFragments.Context.FamiliarityLine(state));
            prompt.AppendLine(PromptFragments.Context.TrustLine(state));
            if (!string.IsNullOrWhiteSpace(state.RelationshipImpression))
            {
                prompt.AppendLine(PromptFragments.Context.RelationshipImpressionLine(state.RelationshipImpression));
            }

            // Durable stores render one line each when populated; empty ones collapse into a single
            // closing line. Recalled content itself appears exactly once, under high-priority
            // continuity — the state section only says what exists. Secret-sharing depth and the
            // comfort tier live in the guidance section; rhythm lives in the stance line and its
            // priority cue; an accepted nickname is carried by its priority cue.
            foreach (string storeLine in BuildDurableStoreLines(state, currentTotalDays))
            {
                prompt.AppendLine(storeLine);
            }

            prompt.AppendLine(PromptFragments.Context.SocialCirclesLine(NpcSocialGraph.GetStableCircleLabels(npc.Name)));
            prompt.AppendLine(PromptFragments.Context.HelpRequestLifecycleLine);
            prompt.AppendLine(PromptFragments.Context.HelpRequestReadinessLine(
                BuildHelpRequestReadinessLabel(state, world, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, currentTotalDays)));
            prompt.AppendLine(PromptFragments.Context.HelpRequestFitLine(HelpRequestAdvisor.BuildPromptLabel(npc, world.Progression)));
            prompt.AppendLine(PromptFragments.Context.SceneInfluenceLine(state.LastSceneInfluenceReason));
            prompt.AppendLine(PromptFragments.Context.LastInteractionLine(state.LastInteraction));
        }
        else
        {
            prompt.AppendLine(PromptFragments.Context.NoStateLine);
        }

        var priorityContext = BuildPriorityPromptContext(npc, state, world, recallPlan, communityImpressions, emotionalStyle, currentTotalDays).ToList();
        if (priorityContext.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine(PromptFragments.Context.PriorityHeading);
            foreach (string item in priorityContext)
            {
                prompt.AppendLine($"- {item}");
            }
        }

        if (recentEntries.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine(PromptFragments.Context.RecentMomentsHeading);
            foreach (var entry in recentEntries)
            {
                prompt.AppendLine($"- {PromptFragments.Context.PromptEntry(entry)}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine(PromptFragments.Context.GuidanceHeading);
        foreach (string guidance in BuildReplyGuidance(state, world, emotionalStyle, currentTotalDays))
        {
            prompt.AppendLine($"- {guidance}");
        }

        if (state != null)
        {
            MarkFollowUpCuesMentioned(state, currentTotalDays, currentTimeOfDay);
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
        prompt.AppendLine(PromptFragments.Context.Header(npc.displayName));
        prompt.AppendLine(PromptFragments.Context.ConciseRules);
        prompt.AppendLine();
        prompt.AppendLine(PromptFragments.Context.CurrentStateHeading);
        prompt.AppendLine(PromptFragments.Context.DispositionLine(disposition.PromptLabel));
        prompt.AppendLine(PromptFragments.Context.SceneLineConcise(world));

        if (state == null)
        {
            prompt.AppendLine(PromptFragments.Context.NoStateLineConcise);
            return prompt.ToString().TrimEnd();
        }

        prompt.AppendLine(PromptFragments.Context.MoodLineConcise(state));
        prompt.AppendLine(PromptFragments.Context.ExpressionStyleLine(emotionalStyle.PromptLabel));
        prompt.AppendLine(PromptFragments.Context.FamiliarityTrustRhythmLineConcise(state));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelRelationshipImpression, state.RelationshipImpression);
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelRecallFocus, PromptFragments.Recall.LongTermMemories(recallPlan.LongTermMemories));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelKnownPreferences, PromptFragments.Recall.PlayerPreferences(recallPlan.PlayerPreferences));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelBehaviorTendencies, PromptFragments.State.DialogueBehaviorInfluences(state, currentTotalDays));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelRecentGift, PromptFragments.State.LastGift(state));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelRecentEvent, PromptFragments.State.LastEvent(state));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelSharedExperiences, PromptFragments.State.SharedExperiences(state, currentTotalDays));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelConflict, PromptFragments.State.Conflicts(state));
        AppendIfMeaningful(prompt, PromptFragments.Context.LabelPersonalMemory, PromptFragments.State.FarmerNickname(state));

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
            AppendIfMeaningful(prompt, PromptFragments.Context.LabelHelpRequests, PromptFragments.State.HelpRequests(state, currentTotalDays));
            prompt.AppendLine(PromptFragments.Context.HelpRequestLifecycleLineConcise);
            prompt.AppendLine(PromptFragments.Context.HelpRequestReadinessLine(
                BuildHelpRequestReadinessLabel(state, world, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, currentTotalDays)));
            prompt.AppendLine(PromptFragments.Context.HelpRequestFitLine(HelpRequestAdvisor.BuildPromptLabel(npc, world.Progression)));
        }

        AppendIfMeaningful(prompt, PromptFragments.Context.LabelLastInteraction, state.LastInteraction);

        if (recentEntries.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine(PromptFragments.Context.RecentMomentsHeading);
            foreach (var entry in recentEntries.TakeLast(3))
            {
                prompt.AppendLine($"- {PromptFragments.Context.PromptEntry(entry)}");
            }
        }

        prompt.AppendLine();
        prompt.AppendLine(PromptFragments.Context.ConciseReplyGuidanceLine);

        return prompt.ToString().TrimEnd();
    }

    /// <summary>
    /// The durable-store lines of the full context's state section. Populated stores render their
    /// one-line summary (recalled content itself appears once, under high-priority continuity);
    /// empty stores collapse into a single closing line so the "there is no such shared history"
    /// anti-hallucination signal survives without spending a full line per store.
    /// </summary>
    internal static IReadOnlyList<string> BuildDurableStoreLines(LivingNpcState state, int currentTotalDays)
    {
        var lines = new List<string>();
        var emptyStores = new List<string>();

        string lastGift = PromptFragments.State.LastGift(state);
        if (lastGift == PromptFragments.State.EmptyGiftMemory)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelGifts);
        }
        else
        {
            lines.Add(PromptFragments.Context.GiftContextLine(lastGift));
        }

        string lastEvent = PromptFragments.State.LastEvent(state);
        if (lastEvent == PromptFragments.State.EmptyEventMemory)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelEvents);
        }
        else
        {
            lines.Add(PromptFragments.Context.EventContextLine(lastEvent));
        }

        if (state.LongTermMemories.Count == 0)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelLongTermMemories);
        }
        else
        {
            lines.Add(PromptFragments.Context.MemoryStoreLine(state.LongTermMemories.Count));
        }

        if (state.PlayerPreferenceMemories.Count == 0)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelPreferences);
        }
        else
        {
            lines.Add(PromptFragments.Context.KnownPreferencesLine(state.PlayerPreferenceMemories.Count));
        }

        string tendencies = PromptFragments.State.DialogueBehaviorInfluences(state, currentTotalDays);
        if (tendencies == PromptFragments.State.EmptyBehaviorInfluences)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelBehaviorTendencies);
        }
        else
        {
            lines.Add(PromptFragments.Context.BehaviorTendenciesLine(tendencies));
        }

        string sharedExperiences = PromptFragments.State.SharedExperiences(state, currentTotalDays);
        if (sharedExperiences == PromptFragments.State.EmptySharedExperiences)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelSharedExperiences);
        }
        else
        {
            lines.Add(PromptFragments.Context.SharedExperiencesLine(sharedExperiences));
        }

        string helpRequests = PromptFragments.State.HelpRequests(state, currentTotalDays);
        if (helpRequests == PromptFragments.State.EmptyHelpRequests)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelHelpRequests);
        }
        else
        {
            lines.Add(PromptFragments.Context.HelpRequestsLine(helpRequests));
        }

        if (state.CommunityImpressions.Count == 0)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelCommunityImpressions);
        }
        else
        {
            lines.Add(PromptFragments.Context.CommunityImpressionsLine(state.CommunityImpressions.Count));
        }

        string conflicts = PromptFragments.State.Conflicts(state);
        if (conflicts == PromptFragments.State.EmptyConflicts)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelConflicts);
        }
        else
        {
            lines.Add(PromptFragments.Context.ConflictMemoryLine(conflicts));
        }

        // A recorded nickname is carried once by its priority cue; only its absence needs a line.
        if (PromptFragments.State.FarmerNickname(state) == PromptFragments.State.EmptyNickname)
        {
            emptyStores.Add(PromptFragments.Context.StoreLabelNickname);
        }

        if (emptyStores.Count > 0)
        {
            lines.Add(PromptFragments.Context.EmptyStoresLine(emptyStores));
        }

        return lines;
    }

    private static void AppendIfMeaningful(StringBuilder prompt, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // The empty form of these labels reads as "no recent…/no durable…/no shared…"; skip those
        // so the concise context only carries lines that actually say something. Matching the
        // known empty forms exactly (instead of sniffing a "no " prefix) keeps real content that
        // happens to start with "No …" — a memory summary, a relationship impression — intact.
        if (PromptFragments.Context.IsEmptyStateValue(value))
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
            AppendDebugBullet(summary, "debug.label.mood", StateDebugLabels.Mood(state));
            AppendDebugBullet(summary, "debug.label.interpersonalEmotion", StateDebugLabels.Emotion(state));
            AppendDebugBullet(summary, "debug.label.attention", $"{StateDebugLabels.Attention(state)} ({state.Attention})");
            AppendDebugBullet(summary, "debug.label.familiarity", $"{StateDebugLabels.Familiarity(state)} ({state.Familiarity})");
            AppendDebugBullet(summary, "debug.label.relationshipTrust", StateDebugLabels.RelationshipTrust(state));
            AppendDebugBullet(summary, "debug.label.interactionRhythm", StateDebugLabels.InteractionRhythm(state));
            AppendDebugBullet(summary, "debug.label.interactionComfort", StateDebugLabels.InteractionComfortTier(state));
            AppendDebugBullet(summary, "debug.label.lastGift", StateDebugLabels.LastGift(state));
            AppendDebugBullet(summary, "debug.label.lastEvent", StateDebugLabels.LastEvent(state));
            AppendDebugBullet(summary, "debug.label.longTermMemory", StateDebugLabels.LongTermMemories(state));
            AppendDebugBullet(summary, "debug.label.relationshipImpression", StateDebugLabels.RelationshipImpression(state));
            AppendDebugBullet(summary, "debug.label.currentLongTermRecall", MemoryRecallService.FormatLongTermMemoryDebugLabel(recallPlan.LongTermMemories));
            AppendDebugBullet(summary, "debug.label.playerPreferenceMemory", StateDebugLabels.PlayerPreferences(state));
            AppendDebugBullet(summary, "debug.label.currentPreferenceRecall", MemoryRecallService.FormatPlayerPreferenceDebugLabel(recallPlan.PlayerPreferences));
            AppendDebugBullet(summary, "debug.label.communityTone", CommunityReactionStyle.For(npc).DebugLabel);
            AppendDebugBullet(summary, "debug.label.emotionStyle", emotionalStyle.DebugSummaryLabel);
            AppendDebugBullet(summary, "debug.label.socialCircles", FormatSocialCircleDebugLabel(npc));
            AppendDebugBullet(summary, "debug.label.communityImpression", StateDebugLabels.CommunityImpressions(state));
            AppendDebugBullet(summary, "debug.label.currentCommunityRecall", MemoryRecallService.FormatCommunityImpressionDebugLabel(communityImpressions));
            AppendDebugBullet(summary, "debug.label.dialogueBehavior", StateDebugLabels.DialogueBehaviorInfluences(state));
            AppendDebugBullet(summary, "debug.label.latestBehaviorChoice", FormatLatestBehaviorChoiceDebugLabel(latestBehavior));
            AppendDebugBullet(summary, "debug.label.sharedExperience", StateDebugLabels.SharedExperiences(state));
            AppendDebugBullet(summary, "debug.label.helpRequests", StateDebugLabels.HelpRequests(state));
            AppendDebugBullet(summary, "debug.label.helpRequestFit", HelpRequestAdvisor.BuildDebugLabel(npc, world.Progression));
            AppendDebugBullet(summary, "debug.label.conflictMemory", StateDebugLabels.Conflicts(state));
            AppendDebugBullet(summary, "debug.label.nicknameMemory", StateDebugLabels.FarmerNickname(state));
            AppendDebugBullet(summary, "debug.label.aiFriendshipToday", state.AiFriendshipGainedToday.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendDebugBullet(summary, "debug.label.profileSource", disposition.SourceDebugLabel);
            AppendDebugBullet(summary, "debug.label.behaviorDisposition", disposition.DebugLabel);
            AppendDebugBullet(summary, "debug.label.emotionStyle", emotionalStyle.DebugSummaryLabel);
            AppendDebugBullet(summary, "debug.label.currentScene", world.DebugLabel);
            AppendDebugBullet(summary, "debug.label.worldProgression", world.Progression.DebugLabel);
            AppendDebugBullet(summary, "debug.label.npcKnowledge", world.ProgressionKnowledge.DebugLabel);
            AppendDebugBullet(summary, "debug.label.sceneInfluence", StateDebugLabels.LastSceneInfluence(state));
            AppendDebugBullet(summary, "debug.label.responseInclination", StateDebugLabels.Inclination(state));
            AppendDebugBullet(summary, "debug.label.lastInteraction", StateDebugLabels.LastInteraction(state));
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

    private static IEnumerable<string> BuildPriorityPromptContext(
        NPC npc,
        LivingNpcState? state,
        WorldContextSnapshot world,
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
                yield return PromptFragments.Context.GiftMemoryCue(
                    state.LastGiftName,
                    PromptFragments.Context.MemoryAge(giftAge),
                    state.LastGiftTaste);
            }

            int eventAge = GetMemoryAge(state.LastEventTotalDays, currentTotalDays);
            if (!string.IsNullOrWhiteSpace(state.LastEventContext) && eventAge <= 3)
            {
                yield return PromptFragments.Context.EventMemoryCue(
                    state.LastEventContext,
                    PromptFragments.Context.MemoryAge(eventAge));
            }

            if (!string.IsNullOrWhiteSpace(state.FarmerNickname))
            {
                yield return PromptFragments.Context.NicknameCue(state);
            }

            if (recallPlan.LongTermMemories.Count > 0)
            {
                yield return PromptFragments.Context.LongTermRecallCue(
                    PromptFragments.Recall.LongTermMemories(recallPlan.LongTermMemories));
            }

            if (recallPlan.PlayerPreferences.Count > 0)
            {
                yield return PromptFragments.Context.PreferenceRecallCue(
                    PromptFragments.Recall.PlayerPreferences(recallPlan.PlayerPreferences));
            }

            if (communityImpressions.Count > 0)
            {
                yield return PromptFragments.Context.CommunityImpressionCue(
                    PromptFragments.Recall.CommunityImpressions(npc, communityImpressions, currentTotalDays));
            }

            var activeBehaviorInfluence = state.GetActiveDialogueBehaviorInfluences(currentTotalDays).FirstOrDefault();
            if (activeBehaviorInfluence != null)
            {
                yield return PromptFragments.Context.BehaviorTendencyCue(activeBehaviorInfluence, currentTotalDays);
            }

            var activeHelpRequest = state.HelpRequests.FirstOrDefault(request => request.Status is "Offered" or "Pending");
            if (activeHelpRequest != null)
            {
                yield return PromptFragments.Context.ActiveHelpRequestCue(activeHelpRequest, currentTotalDays);
            }

            var recentlyFulfilledHelpRequest = SelectRecentlyFulfilledHelpRequestToMention(state, currentTotalDays);
            if (recentlyFulfilledHelpRequest != null)
            {
                yield return PromptFragments.Context.FulfilledHelpRequestCue(recentlyFulfilledHelpRequest, currentTotalDays);
            }

            var expiredHelpRequest = SelectExpiredHelpRequestToMention(state, currentTotalDays);
            if (expiredHelpRequest != null)
            {
                string reaction = string.IsNullOrWhiteSpace(expiredHelpRequest.FailureReaction)
                    ? PromptFragments.Context.DefaultExpiredHelpRequestReaction
                    : expiredHelpRequest.FailureReaction;
                yield return PromptFragments.Context.ExpiredHelpRequestCue(expiredHelpRequest, reaction, currentTotalDays);
            }

            var sharedExperience = SelectSharedExperienceFollowUp(state, currentTotalDays);
            if (sharedExperience != null)
            {
                yield return PromptFragments.Context.SharedExperienceCue(sharedExperience, currentTotalDays);
            }

            if (state.RelationshipTrust < 35)
            {
                yield return PromptFragments.Context.LowTrustCue(state.RelationshipTrust);
            }
            else if (state.RelationshipTrust >= 80)
            {
                yield return PromptFragments.Context.HighTrustCue(state.RelationshipTrust);
            }

            var activeConflict = state.Conflicts
                .Where(conflict => conflict.Status is "Active" or "Recovering")
                .OrderByDescending(conflict => conflict.Severity)
                .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
                .FirstOrDefault();
            if (activeConflict != null)
            {
                yield return PromptFragments.Context.UnresolvedConflictCue(activeConflict, emotionalStyle.ConflictPromptLabel);
                if (activeConflict.RequiresComplexRepair)
                {
                    yield return PromptFragments.Context.ComplexRepairCue(activeConflict.RepairStage);
                }
            }

            var recoveredConflict = SelectRecentlyResolvedConflictToMention(state, currentTotalDays);
            if (recoveredConflict != null)
            {
                yield return PromptFragments.Context.ResolvedConflictCue(recoveredConflict, emotionalStyle.RepairPromptLabel, currentTotalDays);
            }

            if (!string.IsNullOrWhiteSpace(state.InteractionRhythm)
                && state.InteractionRhythm is not "New" and not "NoConversationToday")
            {
                yield return PromptFragments.Context.RhythmReminderCue(state);
            }

            if (state.RepeatedConversationPressure >= 20)
            {
                yield return PromptFragments.Context.BoundaryCue(state.RepeatedConversationPressure);
            }

            if (state.Familiarity >= 18 || state.LastFriendshipHearts > 0)
            {
                yield return PromptFragments.Context.RelationshipWarmthCue(state.Familiarity, state.LastFriendshipHearts);
            }
        }

        if (world.StateInfluence.HasMood && world.StateInfluence.Priority >= 35)
        {
            yield return PromptFragments.Context.SceneCue(
                world.StateInfluence.Reason,
                world.StateInfluence.Mood,
                world.StateInfluence.Inclination);
        }

        yield return PromptFragments.Context.WorldStageCue(world.ProgressionKnowledge.ReplyGuidance);

        if (world.NearbyNpcNames.Count > 0)
        {
            yield return PromptFragments.Context.NearbyNpcsCue(world.NearbyNpcNames);
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
            yield return PromptFragments.Context.GuidanceNoStateModest;
            yield return PromptFragments.Context.GuidanceNoStateSubtle;
            yield return PromptFragments.Context.GuidanceExpressionStyle(emotionalStyle.ReplyGuidance);
            yield break;
        }

        yield return PromptFragments.Context.GuidanceExpressionStyle(emotionalStyle.ReplyGuidance);
        yield return PromptFragments.Context.GuidanceRelationshipPacing(state);
        yield return PromptFragments.Context.GuidanceDisclosurePacing(state);
        yield return PromptFragments.Context.GuidanceInvitationPolicy(state);

        if (!string.IsNullOrWhiteSpace(state.LastGiftName) && state.LastGiftTotalDays == currentTotalDays)
        {
            yield return PromptFragments.Context.GuidanceFreshGift;
        }

        if (state.PlayerPreferenceMemories.Count > 0)
        {
            yield return PromptFragments.Context.GuidancePreferenceMention;
        }

        if (SelectSharedExperienceFollowUp(state, currentTotalDays) != null)
        {
            yield return PromptFragments.Context.GuidanceSharedExperience;
        }

        if (state.HasUnresolvedConflict)
        {
            yield return PromptFragments.Context.GuidanceUnresolvedConflict(emotionalStyle.ConflictPromptLabel);
            if (state.Conflicts.Any(conflict => conflict.RequiresComplexRepair && conflict.Status is "Active" or "Recovering"))
            {
                yield return PromptFragments.Context.GuidanceComplexRepair;
            }
        }

        if (SelectRecentlyResolvedConflictToMention(state, currentTotalDays) != null)
        {
            yield return PromptFragments.Context.GuidanceResolvedConflict(emotionalStyle.RepairPromptLabel);
        }

        if (state.RepeatedConversationPressure >= 20)
        {
            yield return PromptFragments.Context.GuidanceRepeatedPressure;
        }

        if (world.StateInfluence.HasMood)
        {
            yield return PromptFragments.Context.GuidanceSceneNudge(world.StateInfluence.Reason);
        }

        yield return PromptFragments.Context.GuidanceWorldStage(world.ProgressionKnowledge.ReplyGuidance);
    }

    /// <summary>
    /// The single recently-resolved conflict the full context asks the NPC to acknowledge once.
    /// Shared by the priority cue, the reply guidance, and the post-build mark so the three can
    /// never disagree about which conflict was mentioned.
    /// </summary>
    private static NpcConflictFact? SelectRecentlyResolvedConflictToMention(LivingNpcState state, int currentTotalDays)
    {
        return state.Conflicts.FirstOrDefault(conflict =>
            conflict.Status == "Resolved"
            && conflict.ResolvedTotalDays >= currentTotalDays - 3
            && conflict.RecoveryMentionedTotalDays < 0);
    }

    /// <summary>The single recently-fulfilled help request the full context thanks the farmer for once.</summary>
    private static NpcHelpRequestFact? SelectRecentlyFulfilledHelpRequestToMention(LivingNpcState state, int currentTotalDays)
    {
        return state.HelpRequests.FirstOrDefault(request =>
            request.Status == "Fulfilled"
            && request.FulfilledTotalDays >= currentTotalDays - 3
            && request.LastMentionedTotalDays < 0);
    }

    /// <summary>The single expired help request the full context lets the NPC react to (at most once per day).</summary>
    private static NpcHelpRequestFact? SelectExpiredHelpRequestToMention(LivingNpcState state, int currentTotalDays)
    {
        return state.HelpRequests.FirstOrDefault(request =>
            request.Status == "Expired"
            && request.LastMentionedTotalDays < currentTotalDays);
    }

    /// <summary>
    /// The single shared experience whose one-shot follow-up is due. Freshness uses
    /// LastUpdatedTotalDays (not CreatedTotalDays) so a repeated outing that re-arms its follow-up
    /// (FollowUpShownTotalDays reset to -1) still qualifies even when the fact itself is old, and a
    /// FollowUpEligibleTotalDays of -1 means "no follow-up planned" like it does for help requests.
    /// </summary>
    private static SharedExperienceFact? SelectSharedExperienceFollowUp(LivingNpcState state, int currentTotalDays)
    {
        return state.SharedExperiences.FirstOrDefault(experience =>
            experience.FollowUpEligibleTotalDays >= 0
            && experience.FollowUpEligibleTotalDays <= currentTotalDays
            && experience.FollowUpShownTotalDays < 0
            && experience.LastUpdatedTotalDays >= currentTotalDays - 7);
    }

    /// <summary>
    /// Consumes the one-shot follow-up cues the full context just carried, so the next build stops
    /// repeating them. This must run after the whole prompt is assembled (the guidance section
    /// reads the same gates), and only for the full context — the concise context never emits
    /// these cues. Marking used to happen at conversation start instead, but SMAPI raises
    /// ButtonPressed before the click reaches NPC.checkAction and the dialogue generation that
    /// builds this prompt, so the cues were suppressed in the very prompt meant to deliver them.
    /// </summary>
    private static void MarkFollowUpCuesMentioned(LivingNpcState state, int currentTotalDays, int currentTimeOfDay)
    {
        var recoveredConflict = SelectRecentlyResolvedConflictToMention(state, currentTotalDays);
        if (recoveredConflict != null)
        {
            recoveredConflict.RecoveryMentionedTotalDays = currentTotalDays;
            recoveredConflict.RecoveryMentionedTimeOfDay = currentTimeOfDay;
        }

        var fulfilledHelpRequest = SelectRecentlyFulfilledHelpRequestToMention(state, currentTotalDays);
        if (fulfilledHelpRequest != null)
        {
            fulfilledHelpRequest.LastMentionedTotalDays = currentTotalDays;
            fulfilledHelpRequest.LastMentionedTimeOfDay = currentTimeOfDay;
        }

        var expiredHelpRequest = SelectExpiredHelpRequestToMention(state, currentTotalDays);
        if (expiredHelpRequest != null)
        {
            expiredHelpRequest.LastMentionedTotalDays = currentTotalDays;
            expiredHelpRequest.LastMentionedTimeOfDay = currentTimeOfDay;
        }

        var sharedExperience = SelectSharedExperienceFollowUp(state, currentTotalDays);
        if (sharedExperience != null)
        {
            sharedExperience.FollowUpShownTotalDays = currentTotalDays;
            sharedExperience.FollowUpShownTimeOfDay = currentTimeOfDay;
        }
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
            ? PromptFragments.Context.HelpRequestReadinessAllowed(result.Reason)
            : PromptFragments.Context.HelpRequestReadinessBlocked(result.Reason);
    }

    private static int GetMemoryAge(int totalDays, int currentTotalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - totalDays);
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

    private static string FormatSocialCircleDebugLabel(NPC npc)
    {
        var labels = NpcSocialGraph.GetStableCircleLabels(npc.Name);
        return labels.Count == 0
            ? I18n.Get("debug.socialCircles.none")
            : string.Join(I18n.Get("debug.listSeparator"), labels);
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
