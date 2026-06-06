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
        int minRelationshipTrustForHelpRequests,
        int currentTotalDays)
    {
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
            prompt.AppendLine($"- Shared experiences from completed help requests: {state.SharedExperiencePromptLabel}.");
            prompt.AppendLine($"- Help requests involving the farmer: {state.HelpRequestPromptLabel}.");
            prompt.AppendLine($"- Community impressions about the farmer's ties with other NPCs: {MemoryRecallService.FormatCommunityImpressionPromptLabel(npc, communityImpressions)}.");
            prompt.AppendLine($"- Stable community circles this NPC belongs to: {FormatSocialCirclePromptLabel(npc)}.");
            prompt.AppendLine("- Help-request lifecycle: Offered means the NPC has asked but the farmer has not accepted; Pending means accepted and active; only Pending requests should be treated like tasks.");
            prompt.AppendLine($"- Help-request readiness: {BuildHelpRequestReadinessLabel(state, world, maxPendingHelpRequestsPerNpc, helpRequestCooldownDays, minRelationshipTrustForHelpRequests, currentTotalDays)}.");
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
            return $"{npc.displayName} 还没有 LivingNPCs 行为/互动记忆或状态。\n- 行为倾向：{disposition.DebugLabel}\n- 情绪表达风格：{emotionalStyle.DebugSummaryLabel}\n- 当前场景：{world.DebugLabel}\n- 世界进度（客观）：{world.Progression.DebugLabel}\n- NPC 可知进度：{world.ProgressionKnowledge.DebugLabel}";
        }

        var summary = new StringBuilder();
        if (state != null)
        {
            var latestBehavior = entries
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
            summary.AppendLine($"- 当前检索长期记忆：{MemoryRecallService.FormatLongTermMemoryDebugLabel(recallPlan.LongTermMemories)}");
            summary.AppendLine($"- 玩家偏好记忆：{state.PlayerPreferenceDebugLabel}");
            summary.AppendLine($"- 当前检索玩家偏好：{MemoryRecallService.FormatPlayerPreferenceDebugLabel(recallPlan.PlayerPreferences)}");
            summary.AppendLine($"- 社区消息口吻：{CommunityReactionStyle.For(npc).DebugLabel}");
            summary.AppendLine($"- 情绪表达风格：{emotionalStyle.DebugSummaryLabel}");
            summary.AppendLine($"- 社区圈层：{FormatSocialCircleDebugLabel(npc)}");
            summary.AppendLine($"- 社区印象：{state.CommunityImpressionDebugLabel}");
            summary.AppendLine($"- 当前检索社区印象：{MemoryRecallService.FormatCommunityImpressionDebugLabel(communityImpressions)}");
            summary.AppendLine($"- 对话驱动行为：{state.DialogueBehaviorInfluenceDebugLabel}");
            summary.AppendLine($"- 最近行为选择：{FormatLatestBehaviorChoiceDebugLabel(latestBehavior)}");
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

        if (entries.Count > 0)
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
                summary.AppendLine($"- 第 {entry.TotalDays} 天 {entry.TimeOfDay}{locationSuffix}: {FormatDebugKind(entry)} {entry.Action}; {entry.Reason}");
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
        int minRelationshipTrustForHelpRequests,
        int currentTotalDays)
    {
        var result = HelpRequestReadinessRules.Evaluate(
            state,
            world.FriendshipHearts,
            maxPendingHelpRequestsPerNpc,
            helpRequestCooldownDays,
            minRelationshipTrustForHelpRequests,
            currentTotalDays
        );
        return result.Allowed
            ? $"may naturally ask for one modest favor now; {result.Reason}"
            : $"should not open a new help request now; {result.Reason}";
    }

    private static string BuildTravelInvitationPolicyPromptLabel(LivingNpcState state)
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
            return "暂无行为选择记录";
        }

        string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
        string locationPart = string.IsNullOrWhiteSpace(location) ? string.Empty : $"，地点 {location}";
        return $"{entry.Action}（第 {entry.TotalDays} 天 {entry.TimeOfDay}{locationPart}；原因：{entry.Reason}）";
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
            ? "暂无稳定圈层"
            : string.Join("、", labels);
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
}
