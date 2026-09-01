using System;
using System.Collections.Generic;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Persistence;
using LivingNpcs.Shared;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Repairs an accepted outing whose model metadata omitted the destination by looking only at the
/// current, sanitized conversation. The destination must come from a recent explicit player
/// invitation; map state, schedules, memories, and NPC-authored location mentions are never used.
/// </summary>
internal static class RecentOutingInvitationResolver
{
    private const int MaxRecentTurns = 6;
    private const int MaxRecentPlayerTurns = 3;

    private static readonly string[] DepartureCommitmentFragments =
    [
        "一起去吧",
        "一起走吧",
        "我陪你去",
        "我跟你去",
        "我和你去",
        "我带你去",
        "我们走吧",
        "咱们走吧",
        "那我们走",
        "那我们去",
        "我们现在去",
        "咱们现在去",
        "我们就走",
        "咱们就走",
        "现在走",
        "现在去",
        "马上去",
        "这就去",
        "出发吧",
        "走吧",
        "let's go",
        "let us go",
        "i'll go with you",
        "i can go with you",
        "i'll come with you",
        "i can come with you",
        "we can go now",
        "we'll leave in a moment"
    ];

    private static readonly string[] SameConversationDepartureFragments =
    [
        "等会去",
        "等会再去",
        "等会儿去",
        "等会儿再去",
        "待会去",
        "待会再去",
        "待会儿去",
        "待会儿再去",
        "一会去",
        "一会再去",
        "一会儿去",
        "一会儿再去",
        "等一下就去",
        "等我一下就去",
        "等一下就走",
        "等我一下就走",
        "一会儿就走",
        "待会儿就走",
        "准备一下就走",
        "收拾一下就走",
        "go in a moment",
        "leave in a moment",
        "we'll leave in a moment",
        "we will leave in a moment"
    ];

    private static readonly string[] DirectAgreementFragments =
    [
        "好啊",
        "好呀",
        "好吧",
        "好的",
        "当然",
        "愿意",
        "可以",
        "行啊",
        "没问题",
        "okay",
        "sure",
        "yes"
    ];

    private static readonly string[] TravelMotionFragments =
    [
        "去",
        "走",
        "出发",
        "陪你",
        "跟你",
        "带你",
        "go",
        "come",
        "head",
        "walk",
        "leave"
    ];

    private static readonly string[] PendingConfirmationFragments =
    [
        "这样可以吗",
        "这样好吗",
        "可以吗",
        "好吗",
        "行吗",
        "行不行",
        "你觉得呢",
        "okay?",
        "is that okay",
        "does that work"
    ];

    private static readonly string[] ExplicitInvitationFragments =
    [
        "一起",
        "陪我",
        "跟我",
        "和我",
        "带我",
        "要不要去",
        "要不要来",
        "愿不愿意",
        "愿意和我",
        "我们去",
        "咱们去",
        "来我",
        "去我",
        "go with me",
        "come with me",
        "walk with me",
        "together",
        "shall we",
        "let's",
        "would you",
        "could you",
        "want to go",
        "want to come"
    ];

    private static readonly string[] NegotiationContinuationFragments =
    [
        "陪我去",
        "跟我去",
        "我们走",
        "咱们走",
        "一起走",
        "走吧",
        "出发",
        "太好了",
        "不嘛",
        "花不了",
        "不会太久",
        "只去一会",
        "去一会",
        "待一会",
        "快去快回",
        "好啊",
        "好呀",
        "好的",
        "可以",
        "当然",
        "没问题",
        "go with me",
        "come with me",
        "let's go",
        "sounds good",
        "great"
    ];

    private static readonly string[] ExperienceQuestionFragments =
    [
        "去过",
        "来过",
        "到过",
        "有没有去",
        "有没有来",
        "have you been",
        "ever been",
        "been to"
    ];

    private static readonly string[] FutureOrCancellationFragments =
    [
        "明天",
        "明日",
        "改天",
        "改日",
        "下次",
        "以后再",
        "过几天",
        "另一天",
        "不去了",
        "还是算了",
        "别去了",
        "tomorrow",
        "next time",
        "another day",
        "another time",
        "some other time",
        "let's not"
    ];

    public static void NormalizeCompanionOutingActions(
        ConversationAnalysis analysis,
        DialogueContext context,
        string visibleNpcReply)
    {
        if (analysis?.Actions == null)
        {
            return;
        }

        foreach (ConversationWorldActionRequest action in analysis.Actions)
        {
            if (!string.Equals(action.Type, "companion_outing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Kept in the JSON contract for backward compatibility, but outings no longer use a
            // game-time delay queue. The runtime begins after the final dialogue is dismissed.
            action.DelayMinutes = 0;

            string consent = LivingNpcMetadataRules.NormalizeTravelConsent(action.TravelConsent);
            bool acceptedNow = string.Equals(consent, "accepted_now", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(consent, "accepted_later", StringComparison.OrdinalIgnoreCase)
                && IsSameConversationDepartureCommitment(visibleNpcReply))
            {
                // With no delayed queue, a short "wait a moment / let's go in a moment" promise
                // launches when the final line closes. Preserve accepted_later for a genuinely
                // future meeting such as tomorrow, another day, or after today's obligations.
                action.TravelConsent = "accepted_now";
                action.Reason = AppendReason(
                    action.Reason,
                    "short same-conversation departure wording normalized to accepted_now");
                acceptedNow = true;
            }

            if (acceptedNow && IsPendingConfirmationQuestion(visibleNpcReply))
            {
                // "I can only go briefly... is that okay?" is still negotiating the agreement.
                // Do not let an over-eager classifier close the dialogue or start the outing before
                // the farmer gives the final confirmation.
                action.TravelConsent = "tentative";
                action.Reason = AppendReason(
                    action.Reason,
                    "visible reply still asks the player to confirm the outing");
                continue;
            }

            string normalizedTarget = TravelLocationRules.Normalize(action.TargetLocation, string.Empty);
            if (TravelLocationRules.IsKnownPublicOutingTarget(normalizedTarget))
            {
                action.TargetLocation = normalizedTarget;
                continue;
            }

            if (!acceptedNow
                || !IsDecisiveDepartureCommitment(visibleNpcReply)
                || !TryFindRecentExplicitInvitationTarget(context?.ChatHistory, out string recoveredTarget))
            {
                continue;
            }

            action.TargetLocation = recoveredTarget;
            action.Reason = AppendReason(
                action.Reason,
                $"recovered destination {recoveredTarget} from the recent explicit player invitation");
        }

        if (analysis.Actions.Count == 0
            && !IsPendingConfirmationQuestion(visibleNpcReply)
            && IsDecisiveDepartureCommitment(visibleNpcReply)
            && TryFindRecentExplicitInvitationTarget(context?.ChatHistory, out string synthesizedTarget))
        {
            // The auxiliary classifier can omit actions entirely after labeling "等会再去" as
            // accepted_later. Recover the promised outing from visible evidence instead of losing
            // it merely because the destination was only stated in an earlier turn.
            analysis.Actions.Add(new ConversationWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = synthesizedTarget,
                TravelConsent = "accepted_now",
                DurationMinutes = CompanionOutingRules.MinimumStayMinutes,
                DelayMinutes = 0,
                Reason = $"visible departure commitment recovered destination {synthesizedTarget} from the recent explicit player invitation"
            });
        }
    }

    internal static bool TryFindRecentExplicitInvitationTarget(
        IReadOnlyList<ConversationElement>? chatHistory,
        out string targetLocation)
    {
        targetLocation = string.Empty;
        if (chatHistory == null || chatHistory.Count == 0)
        {
            return false;
        }

        int inspectedTurns = 0;
        int inspectedPlayerTurns = 0;
        for (int index = chatHistory.Count - 1;
             index >= 0 && inspectedTurns < MaxRecentTurns && inspectedPlayerTurns < MaxRecentPlayerTurns;
             index--)
        {
            ConversationElement turn = chatHistory[index];
            inspectedTurns++;
            if (string.IsNullOrWhiteSpace(turn.Text))
            {
                // A withheld/blocked turn is a hard boundary; never recover a target across it.
                return false;
            }

            if (!turn.IsPlayerLine)
            {
                continue;
            }

            inspectedPlayerTurns++;
            InvitationLineKind kind = ClassifyPlayerLine(turn.Text, out string candidate);
            if (kind == InvitationLineKind.Valid)
            {
                targetLocation = candidate;
                return true;
            }

            if (kind == InvitationLineKind.Stop)
            {
                return false;
            }
        }

        return false;
    }

    internal static bool IsDecisiveDepartureCommitment(string visibleNpcReply)
    {
        if (string.IsNullOrWhiteSpace(visibleNpcReply)
            || ContainsAny(visibleNpcReply, FutureOrCancellationFragments))
        {
            return false;
        }

        if (ContainsAny(visibleNpcReply, DepartureCommitmentFragments)
            || IsSameConversationDepartureCommitment(visibleNpcReply))
        {
            return true;
        }

        if (IsPendingConfirmationQuestion(visibleNpcReply))
        {
            return false;
        }

        return ContainsAny(visibleNpcReply, DirectAgreementFragments);
    }

    internal static bool IsSameConversationDepartureCommitment(string visibleNpcReply)
    {
        return !string.IsNullOrWhiteSpace(visibleNpcReply)
            && !ContainsAny(visibleNpcReply, FutureOrCancellationFragments)
            && ContainsAny(visibleNpcReply, SameConversationDepartureFragments);
    }

    private static bool IsPendingConfirmationQuestion(string visibleNpcReply)
    {
        return ContainsAny(visibleNpcReply, PendingConfirmationFragments)
            && !ContainsAny(visibleNpcReply, DepartureCommitmentFragments);
    }

    private static InvitationLineKind ClassifyPlayerLine(string text, out string targetLocation)
    {
        targetLocation = string.Empty;
        if (ContainsAny(text, FutureOrCancellationFragments)
            || ContainsAny(text, ExperienceQuestionFragments))
        {
            return InvitationLineKind.Stop;
        }

        IReadOnlyList<string> mentionedTargets = TravelLocationRules.FindMentionedPublicOutingTargets(text);
        if (mentionedTargets.Count > 1)
        {
            return InvitationLineKind.Stop;
        }

        if (mentionedTargets.Count == 1)
        {
            if (!ContainsAny(text, ExplicitInvitationFragments)
                || !ContainsAny(text, TravelMotionFragments))
            {
                // A background location mention is not an invitation and ends the recovery chain.
                return InvitationLineKind.Stop;
            }

            targetLocation = mentionedTargets[0];
            return InvitationLineKind.Valid;
        }

        return ContainsAny(text, NegotiationContinuationFragments)
            ? InvitationLineKind.Continue
            : InvitationLineKind.Stop;
    }

    private static bool ContainsAny(string text, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string AppendReason(string reason, string addition)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? addition
            : $"{reason.Trim()}; {addition}";
    }

    private enum InvitationLineKind
    {
        Continue,
        Valid,
        Stop
    }
}
