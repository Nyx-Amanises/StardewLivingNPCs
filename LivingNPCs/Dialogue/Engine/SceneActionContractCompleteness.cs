using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Requests full classification when an omitted contract family has an unmistakable visible
/// effect but no metadata. This check never creates actions, changes consent, or edits history.
/// </summary>
internal static class SceneActionContractCompleteness
{
    private const int MaxRecentTurns = 6;
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // The shared resolver also accepts a bare "sure / 好啊". That is useful for repairing
    // model-confirmed travel, but insufficient evidence for an entirely missing action.
    private static readonly Regex ExplicitDepartureClause = new(
        @"(?:^|[。.!！,，;；\r\n])\s*(?:(?:那(?:我们|咱们)(?:走|去)|(?:我们|咱们)(?:现在|就)(?:走|去)|我(?:陪|跟|和|带)你去)(?!过|了|年|的时候|时)|(?:我们|咱们|一起)(?:走|去)吧|出发吧|走吧|(?:现在|马上|这就)(?:走|去)(?!过|了)|(?:等会儿?|待会儿?|一会儿?)(?:再|就)?(?:走|去)|(?:let's|let us)\s+go\b|i(?:'ll|\s+will|\s+can)\s+(?:go|come)\s+with\s+you\b|we\s+can\s+go\s+now\b|we(?:'ll|\s+will)\s+leave\s+in\s+a\s+moment\b)",
        Options);
    private static readonly Regex UnsettledDeparture = new(
        @"[?？]|也许|或许|可能|说不定|如果|要是|不(?:想|愿意|打算|会|要)去|\b(?:maybe|perhaps|possibly|if|unless|won't|will not|don't want to|do not want to|would rather not)\b",
        Options);

    internal static string? FindMissingEffect(
        ConversationAnalysis analysis,
        SceneActionContractPlan plan,
        string playerText,
        string visibleNpcReply,
        DialogueContext context,
        IReadOnlyList<HelpRequestItemAlias> helpItemAliases)
    {
        string reply = SafeCueText(visibleNpcReply);
        if (reply.Length == 0)
        {
            return null;
        }

        if (!plan.IncludeTravel
            && !analysis.Actions.Any(action => string.Equals(action.Type, "companion_outing", StringComparison.OrdinalIgnoreCase))
            && HasUnclassifiedDeparture(playerText, reply, context))
        {
            return "scene action contract omitted travel metadata for a visible departure commitment";
        }

        if (!plan.IncludeNewHelp
            && !plan.IncludeHelpUpdates
            && analysis.HelpRequests.Count == 0
            && analysis.HelpRequestUpdates.Count == 0
            && HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, helpItemAliases)
                .Any(request => !request.IsOptionalAddOn))
        {
            // The selected update contract can cover a reminder about an existing task without
            // emitting any new metadata. Without reliable item-to-task matching, do not treat
            // such a reminder as an omitted new request. Partial/mismatched help metadata also
            // remains the responsibility of the existing consistency validators.
            return "scene action contract omitted helpRequests metadata for a visible explicit item request";
        }

        // The extraction pass already guards explicit gift handoffs. Money's existing runtime
        // cue is too broad (it also matches deferred/negated offers), and festival actions have
        // no pure-text commitment predicate. Do not guess those effects or help-update intent
        // here; their selected contracts and existing full classifier/runtime remain authoritative.
        return null;
    }

    private static bool HasUnclassifiedDeparture(
        string playerText,
        string visibleNpcReply,
        DialogueContext context)
    {
        string player = SafeCueText(playerText);
        if (player.Length == 0
            || !RecentOutingInvitationResolver.IsDecisiveDepartureCommitment(visibleNpcReply)
            || !ExplicitDepartureClause.IsMatch(visibleNpcReply)
            || UnsettledDeparture.IsMatch(visibleNpcReply)
            || ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel(player, visibleNpcReply))
        {
            return false;
        }

        // Preserve blocked/withheld turns as empty hard boundaries, including NPC-authored
        // turns. Never fill a missing destination from location, schedules or memory prose.
        var recent = new List<ConversationElement>();
        IReadOnlyList<ConversationElement> history = context.ChatHistory ?? new List<ConversationElement>();
        for (int index = Math.Max(0, history.Count - MaxRecentTurns); index < history.Count; index++)
        {
            ConversationElement turn = history[index];
            recent.Add(new ConversationElement(SafeCueText(turn.Text), turn.IsPlayerLine) { Id = turn.Id });
        }

        // The current input may already be in captured history. Do not count it twice against
        // the resolver's short recency window; an external current input is appended locally.
        if (recent.Count == 0 || !recent[^1].IsPlayerLine
            || !string.Equals(recent[^1].Text, player, StringComparison.Ordinal))
        {
            recent.Add(new ConversationElement(player, true));
        }

        return RecentOutingInvitationResolver.TryFindRecentExplicitInvitationTarget(recent, out _);
    }

    private static string SafeCueText(string? text)
    {
        string safe = RsvPromptSanitizer.SafeInline(text);
        return RsvAiPolicy.IsWithheldPlayerMessage(safe)
            ? string.Empty
            : safe.Replace('’', '\'').Replace("#$b#", "\n").Replace("#$e#", "\n");
    }
}
