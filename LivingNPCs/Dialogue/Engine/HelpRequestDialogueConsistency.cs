using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LivingNPCs.Behavior;

namespace LivingNPCs.Dialogue.Engine;

internal sealed record HelpRequestDialogueCleanupResult(
    string DialogueLine,
    bool RemovedOptionalAddOn,
    bool IsRetraction = false,
    bool HasValidRequest = false);

/// <summary>
/// Keeps the visible favor aligned with hidden help-request metadata. This postprocessor is
/// intentionally narrow: it removes unsupported optional item add-ons while preserving ordinary
/// dialogue and every clearly required item. Optional task steps do not exist in the runtime, so
/// wording like "another item would be better" can never be allowed to become a mandatory quest.
/// </summary>
internal static class HelpRequestDialogueConsistency
{
    private static readonly Regex PageMarkerPattern = new(
        @"(#\$[be]#)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PortraitSuffixPattern = new(
        @"(?<portrait>\$[A-Za-z0-9_]+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SentenceBoundaryPattern = new(
        @"(?<=[。！？!?；;，,])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DependentFollowUpPattern = new(
        @"^(?:(?:那|所以)?你(?:觉得(?:呢|怎么样)?|怎么想|看呢)|(?:这样)?(?:可以|行|好吗|好不好|愿意吗))\s*[。！？!?]*$|^(?:what\s+do\s+you\s+think|how\s+does\s+that\s+sound|would\s+that\s+be\s+okay|is\s+that\s+okay|okay)\s*[.!?]*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OptionalLeadInPattern = new(
        @"^(?:当然|不过|而且|另外|还有|顺便|对了)\s*[，,]$|^(?:of\s+course|also|and|besides|additionally|but)\s*,$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OptionalContinuationPattern = new(
        @"^(?:好?让我|让我|用来|好用来|这样|这样一来|那|那么|我(?:会|就|可以|能)|这(?:样|会))|^(?:so\b|so\s+that\b|that\s+would\b|then\b|i(?:'d|\s+would|\s+could|\s+can)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string RemoveUntrackedOptionalItemAddOns(
        string dialogue,
        ConversationAnalysis analysis,
        Func<string, string>? resolveItemDisplayName = null,
        IReadOnlyList<HelpRequestItemAlias>? requestableItems = null)
    {
        return Reconcile(dialogue, analysis, resolveItemDisplayName, requestableItems).DialogueLine;
    }

    public static HelpRequestDialogueCleanupResult Reconcile(
        string dialogue,
        ConversationAnalysis analysis,
        Func<string, string>? resolveItemDisplayName = null,
        IReadOnlyList<HelpRequestItemAlias>? requestableItems = null)
    {
        if (string.IsNullOrWhiteSpace(dialogue) || analysis == null)
        {
            return new HelpRequestDialogueCleanupResult(dialogue ?? string.Empty, false);
        }

        IReadOnlyList<string> trackedLabels = CollectTrackedLabels(analysis, resolveItemDisplayName);
        IReadOnlyList<HelpRequestItemAlias> itemAliases = MergeItemAliases(
            BuildAnalysisItemAliases(analysis, resolveItemDisplayName),
            requestableItems);
        string[] parts = PageMarkerPattern.Split(dialogue);
        var rebuilt = new StringBuilder(dialogue.Length);
        var removedTrackedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? pendingBoundary = null;
        bool removedAny = false;
        bool hasPriorRequiredRequest = false;
        bool hasValidRequest = false;
        bool removingOptionalContinuation = false;
        bool removedRequestImmediatelyBefore = false;
        var requiredItemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in parts)
        {
            if (PageMarkerPattern.IsMatch(part))
            {
                pendingBoundary = part;
                continue;
            }

            PageCleanupResult pageCleanup = RemoveFromPage(
                part,
                trackedLabels,
                itemAliases,
                hasPriorRequiredRequest,
                removingOptionalContinuation,
                removedRequestImmediatelyBefore,
                requiredItemIds);
            removedAny |= pageCleanup.Removed;
            removedTrackedLabels.UnionWith(pageCleanup.RemovedTrackedLabels);
            requiredItemIds.UnionWith(pageCleanup.NewRequiredItemIds);
            hasPriorRequiredRequest |= pageCleanup.HasRequiredRequest;
            hasValidRequest |= pageCleanup.HasRequiredRequest;
            removingOptionalContinuation = pageCleanup.ContinueOptionalRemoval;
            removedRequestImmediatelyBefore = pageCleanup.RemovedRequestImmediatelyBefore;
            if (string.IsNullOrWhiteSpace(pageCleanup.Page))
            {
                continue;
            }

            if (rebuilt.Length > 0 && pendingBoundary != null)
            {
                rebuilt.Append(pendingBoundary);
            }

            rebuilt.Append(pageCleanup.Page);
            pendingBoundary = null;
        }

        if (!removedAny)
        {
            return new HelpRequestDialogueCleanupResult(dialogue, false);
        }

        ReconcileAnalysisAfterCleanup(analysis, removedTrackedLabels, resolveItemDisplayName);
        bool isRetraction = rebuilt.Length == 0;
        string cleanedDialogue = isRetraction
            ? BuildOptionalRequestRetraction(dialogue)
            : rebuilt.ToString();
        return new HelpRequestDialogueCleanupResult(
            cleanedDialogue,
            true,
            isRetraction,
            hasValidRequest);
    }

    public static List<string> ReconcileOptionsAfterCleanup(
        IReadOnlyList<string> originalOptions,
        HelpRequestDialogueCleanupResult cleanup,
        string locale)
    {
        if (cleanup.RemovedOptionalAddOn && !cleanup.HasValidRequest)
        {
            return new List<string>();
        }

        if (!cleanup.RemovedOptionalAddOn || originalOptions == null || originalOptions.Count == 0)
        {
            return originalOptions?.ToList() ?? new List<string>();
        }

        // Once a generated add-on sentence is removed, an option can still refer to it indirectly
        // (for example "Can I have one of those cookies?") without repeating the tracked item
        // label. Semantic filtering would be guesswork, so replace the whole set with neutral
        // accept/try/decline choices in the active game language.
        if ((locale ?? string.Empty).StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "好，我会帮你留意的。",
                "我会尽量帮你找找。",
                "抱歉，我现在做不到。"
            ];
        }

        return
        [
            "Sure, I'll keep an eye out.",
            "I'll find it for you.",
            "Sorry, I can't help right now."
        ];
    }

    private static PageCleanupResult RemoveFromPage(
        string page,
        IReadOnlyList<string> trackedLabels,
        IReadOnlyList<HelpRequestItemAlias> itemAliases,
        bool hasPriorRequiredRequest,
        bool continueOptionalRemoval,
        bool priorRequestWasRemoved,
        IReadOnlySet<string> priorRequiredItemIds)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return new PageCleanupResult(
                string.Empty,
                false,
                new HashSet<string>(),
                false,
                continueOptionalRemoval,
                priorRequestWasRemoved,
                new HashSet<string>());
        }

        string body = page;
        string portrait = string.Empty;
        Match portraitMatch = PortraitSuffixPattern.Match(body);
        if (portraitMatch.Success)
        {
            portrait = portraitMatch.Groups["portrait"].Value;
            body = body[..portraitMatch.Index].TrimEnd();
        }

        IReadOnlyList<ExplicitHelpRequestItem> detectedRequests =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                body,
                itemAliases,
                hasPriorRequiredRequest,
                priorRequiredItemIds);
        string[] sentences = SentenceBoundaryPattern.Split(body);
        bool removed = false;
        bool removedRequestImmediatelyBefore = priorRequestWasRemoved;
        bool removingOptionalContinuation = continueOptionalRemoval;
        bool requiredRequestSeen = hasPriorRequiredRequest;
        int sentenceStart = 0;
        var removedTrackedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newRequiredItemIds = detectedRequests
            .Where(request => !request.IsOptionalAddOn)
            .Select(request => request.ItemId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>(sentences.Length);
        foreach (string sentence in sentences)
        {
            int sentenceEnd = sentenceStart + sentence.Length;
            string fragment = sentence.Trim();
            if (fragment.Length == 0)
            {
                sentenceStart = sentenceEnd;
                continue;
            }

            if (removingOptionalContinuation)
            {
                if (IsOptionalContinuationFragment(fragment))
                {
                    removed = true;
                    CollectRemovedTrackedLabels(fragment, trackedLabels, removedTrackedLabels);
                    removingOptionalContinuation = ShouldContinueOptionalRemoval(fragment);
                    removedRequestImmediatelyBefore = !removingOptionalContinuation;
                    sentenceStart = sentenceEnd;
                    continue;
                }

                // The add-on has ended even though the model used another comma. Re-process this
                // fragment normally so unrelated follow-up such as weather or a photo plan stays.
                removingOptionalContinuation = false;
                removedRequestImmediatelyBefore = true;
            }

            bool hasDetectedOptionalItem = detectedRequests.Any(request =>
                request.IsOptionalAddOn
                && request.TextIndex >= sentenceStart
                && request.TextIndex < sentenceEnd);
            string surroundingSentence = GetSentenceRemainder(body, sentenceStart);
            bool optionalAddOn = hasDetectedOptionalItem
                || HelpRequestDialogueAnalyzer.IsOptionalAddOnRequest(
                    fragment,
                    requiredRequestSeen,
                    surroundingSentence);
            if (optionalAddOn)
            {
                removed = true;
                if (kept.Count > 0 && OptionalLeadInPattern.IsMatch(kept[^1].Trim()))
                {
                    kept.RemoveAt(kept.Count - 1);
                }

                CollectRemovedTrackedLabels(fragment, trackedLabels, removedTrackedLabels);

                removingOptionalContinuation = ShouldContinueOptionalRemoval(fragment);
                removedRequestImmediatelyBefore = !removingOptionalContinuation;
                sentenceStart = sentenceEnd;
                continue;
            }

            if (removedRequestImmediatelyBefore && DependentFollowUpPattern.IsMatch(fragment))
            {
                removed = true;
                removedRequestImmediatelyBefore = false;
                sentenceStart = sentenceEnd;
                continue;
            }

            // Preserve the original leading whitespace between English sentences. Inspection uses
            // the trimmed fragment, but reconstruction should not glue "request.Thanks" together.
            kept.Add(sentence);
            if (detectedRequests.Any(request =>
                    !request.IsOptionalAddOn
                    && request.TextIndex >= sentenceStart
                    && request.TextIndex < sentenceEnd))
            {
                requiredRequestSeen = true;
            }

            removedRequestImmediatelyBefore = false;
            sentenceStart = sentenceEnd;
        }

        if (!removed)
        {
            return new PageCleanupResult(
                page,
                false,
                removedTrackedLabels,
                detectedRequests.Any(request => !request.IsOptionalAddOn),
                removingOptionalContinuation,
                removedRequestImmediatelyBefore,
                newRequiredItemIds);
        }

        string cleaned = string.Concat(kept).Trim();
        if (cleaned.Length == 0)
        {
            return new PageCleanupResult(
                string.Empty,
                true,
                removedTrackedLabels,
                detectedRequests.Any(request => !request.IsOptionalAddOn),
                removingOptionalContinuation,
                removedRequestImmediatelyBefore,
                newRequiredItemIds);
        }

        if (cleaned.EndsWith("；", StringComparison.Ordinal)
            || cleaned.EndsWith("，", StringComparison.Ordinal))
        {
            cleaned = cleaned[..^1] + "。";
        }
        else if (cleaned.EndsWith(";", StringComparison.Ordinal)
                 || cleaned.EndsWith(",", StringComparison.Ordinal))
        {
            cleaned = cleaned[..^1] + ".";
        }

        return new PageCleanupResult(
            portrait.Length == 0 ? cleaned : cleaned + portrait,
            true,
            removedTrackedLabels,
            detectedRequests.Any(request => !request.IsOptionalAddOn),
            removingOptionalContinuation,
            removedRequestImmediatelyBefore,
            newRequiredItemIds);
    }

    private static bool EndsWithClauseContinuation(string fragment)
    {
        string trimmed = fragment.TrimEnd();
        return trimmed.EndsWith("，", StringComparison.Ordinal)
            || trimmed.EndsWith(",", StringComparison.Ordinal);
    }

    private static bool ShouldContinueOptionalRemoval(string fragment)
    {
        return EndsWithClauseContinuation(fragment)
            && !HelpRequestDialogueAnalyzer.HasOptionalOutcomeSignal(fragment);
    }

    private static bool IsOptionalContinuationFragment(string fragment)
    {
        return OptionalContinuationPattern.IsMatch(fragment)
            || HelpRequestDialogueAnalyzer.HasOptionalOutcomeSignal(fragment);
    }

    private static string GetSentenceRemainder(string text, int start)
    {
        int end = Math.Clamp(start, 0, text.Length);
        while (end < text.Length && text[end] is not ('。' or '！' or '？' or '.' or '!' or '?' or '；' or ';' or '\n' or '\r'))
        {
            end++;
        }

        if (end < text.Length)
        {
            end++;
        }

        return text[Math.Clamp(start, 0, text.Length)..end];
    }

    private static void CollectRemovedTrackedLabels(
        string fragment,
        IReadOnlyList<string> trackedLabels,
        ISet<string> removedTrackedLabels)
    {
        foreach (string label in trackedLabels.Where(label =>
                     HelpRequestDialogueAnalyzer.ContainsItemAlias(fragment, label)))
        {
            removedTrackedLabels.Add(label);
        }
    }

    private static void ReconcileAnalysisAfterCleanup(
        ConversationAnalysis analysis,
        IReadOnlySet<string> removedTrackedLabels,
        Func<string, string>? resolveItemDisplayName)
    {
        // Free-text derivative metadata can reintroduce the deleted hallucination into the next
        // prompt even when the player never saw it. A contract-violating reply is not a safe turn
        // from which to retain inferred memories or behavior influences.
        analysis.AmbientFollowUp.Text = string.Empty;
        analysis.AmbientFollowUp.DelayMinutes = 0;
        analysis.EmotionImpact.Reason = string.Empty;
        analysis.Memories.Clear();
        analysis.BehaviorInfluences.Clear();
        foreach (ConversationWorldActionRequest action in analysis.Actions)
        {
            action.Reason = string.Empty;
        }

        if (removedTrackedLabels.Count == 0 || analysis.HelpRequests.Count == 0)
        {
            return;
        }

        var reconciledRequests = new List<ConversationHelpRequestCandidate>();
        foreach (ConversationHelpRequestCandidate request in analysis.HelpRequests)
        {
            List<ConversationHelpRequestStepCandidate> steps = request.Steps
                ?? new List<ConversationHelpRequestStepCandidate>();
            if (steps.Count == 0)
            {
                if (!ItemMatchesAnyRemovedLabel(
                        request.RequestedItemId,
                        request.RequestedItemLabel,
                        removedTrackedLabels,
                        resolveItemDisplayName))
                {
                    reconciledRequests.Add(request);
                }

                continue;
            }

            List<ConversationHelpRequestStepCandidate> remaining = steps
                .Where(step => !ItemMatchesAnyRemovedLabel(
                    step.RequestedItemId,
                    step.RequestedItemLabel,
                    removedTrackedLabels,
                    resolveItemDisplayName))
                .ToList();
            if (remaining.Count == 0)
            {
                continue;
            }

            if (remaining.Count != steps.Count)
            {
                ConversationHelpRequestStepCandidate first = remaining[0];
                request.RequestedItemId = first.RequestedItemId;
                request.RequestedItemLabel = first.RequestedItemLabel;
                request.QuestionTopic = first.QuestionTopic;
                request.Summary = first.Summary;
                request.Reason = string.Empty;
                request.Steps = remaining.Count == 1
                    ? new List<ConversationHelpRequestStepCandidate>()
                    : remaining;
            }

            reconciledRequests.Add(request);
        }

        analysis.HelpRequests = reconciledRequests;
    }

    private static bool ItemMatchesAnyRemovedLabel(
        string itemId,
        string itemLabel,
        IReadOnlySet<string> removedTrackedLabels,
        Func<string, string>? resolveItemDisplayName)
    {
        if (removedTrackedLabels.Contains(itemLabel?.Trim() ?? string.Empty))
        {
            return true;
        }

        if (resolveItemDisplayName == null || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        try
        {
            return removedTrackedLabels.Contains(resolveItemDisplayName(itemId)?.Trim() ?? string.Empty);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildOptionalRequestRetraction(string originalDialogue)
    {
        Match portraitMatch = PortraitSuffixPattern.Match(originalDialogue ?? string.Empty);
        string portrait = portraitMatch.Success
            ? portraitMatch.Groups["portrait"].Value
            : string.Empty;
        bool usesCjk = (originalDialogue ?? string.Empty).Any(character =>
            character is >= '\u3400' and <= '\u4DBF'
                or >= '\u4E00' and <= '\u9FFF'
                or >= '\uF900' and <= '\uFAFF');
        string line = usesCjk
            ? "算了，暂时不用麻烦你了。"
            : "Never mind, I don't need anything right now.";
        return line + portrait;
    }

    private static IReadOnlyList<HelpRequestItemAlias> BuildAnalysisItemAliases(
        ConversationAnalysis analysis,
        Func<string, string>? resolveItemDisplayName)
    {
        var aliases = new List<HelpRequestItemAlias>();
        foreach (ConversationHelpRequestCandidate request in analysis.HelpRequests)
        {
            List<ConversationHelpRequestStepCandidate> steps = request.Steps
                ?? new List<ConversationHelpRequestStepCandidate>();
            if (steps.Count == 0)
            {
                AddAnalysisItemAlias(
                    aliases,
                    request.RequestedItemId,
                    request.RequestedItemLabel,
                    resolveItemDisplayName);
                continue;
            }

            foreach (ConversationHelpRequestStepCandidate step in steps)
            {
                AddAnalysisItemAlias(
                    aliases,
                    step.RequestedItemId,
                    step.RequestedItemLabel,
                    resolveItemDisplayName);
            }
        }

        return aliases;
    }

    private static void AddAnalysisItemAlias(
        ICollection<HelpRequestItemAlias> aliases,
        string itemId,
        string itemLabel,
        Func<string, string>? resolveItemDisplayName)
    {
        string normalizedId = BehaviorValueNormalizer.NormalizeQualifiedObjectItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        string resolvedLabel = string.Empty;
        if (resolveItemDisplayName != null)
        {
            try
            {
                resolvedLabel = resolveItemDisplayName(normalizedId)?.Trim() ?? string.Empty;
            }
            catch
            {
                // The metadata label still lets the consistency guard recognize this item.
            }
        }

        string label = string.IsNullOrWhiteSpace(itemLabel) ? resolvedLabel : itemLabel.Trim();
        aliases.Add(new HelpRequestItemAlias(
            normalizedId,
            label,
            new[] { itemLabel?.Trim() ?? string.Empty, resolvedLabel }));
    }

    private static IReadOnlyList<HelpRequestItemAlias> MergeItemAliases(
        IReadOnlyList<HelpRequestItemAlias> analysisAliases,
        IReadOnlyList<HelpRequestItemAlias>? requestableItems)
    {
        var merged = new Dictionary<string, (string Label, HashSet<string> Aliases)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (HelpRequestItemAlias item in analysisAliases
                     .Concat(requestableItems ?? Array.Empty<HelpRequestItemAlias>()))
        {
            if (string.IsNullOrWhiteSpace(item.ItemId))
            {
                continue;
            }

            if (!merged.TryGetValue(item.ItemId, out var entry))
            {
                entry = (item.Label, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            foreach (string alias in item.Aliases)
            {
                entry.Aliases.Add(alias);
            }

            string label = string.IsNullOrWhiteSpace(entry.Label) ? item.Label : entry.Label;
            merged[item.ItemId] = (label, entry.Aliases);
        }

        return merged
            .Select(pair => new HelpRequestItemAlias(
                pair.Key,
                pair.Value.Label,
                pair.Value.Aliases))
            .ToList();
    }

    private static IReadOnlyList<string> CollectTrackedLabels(
        ConversationAnalysis analysis,
        Func<string, string>? resolveItemDisplayName)
    {
        var labels = new List<string>();
        foreach (ConversationHelpRequestCandidate request in analysis.HelpRequests)
        {
            List<ConversationHelpRequestStepCandidate> steps = request.Steps
                ?? new List<ConversationHelpRequestStepCandidate>();
            if (steps.Count == 0)
            {
                AddLabel(labels, request.RequestedItemLabel);
                AddResolvedLabel(labels, request.RequestedItemId, resolveItemDisplayName);
                continue;
            }

            // Runtime storage treats steps as authoritative whenever they exist; the visible-text
            // guard must use the same rule so a stray top-level label cannot legitimize an
            // otherwise-untracked add-on sentence.
            foreach (ConversationHelpRequestStepCandidate step in steps)
            {
                AddLabel(labels, step.RequestedItemLabel);
                AddResolvedLabel(labels, step.RequestedItemId, resolveItemDisplayName);
            }
        }

        return labels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(label => label.Length)
            .ToList();
    }

    private static void AddResolvedLabel(
        ICollection<string> labels,
        string itemId,
        Func<string, string>? resolveItemDisplayName)
    {
        if (resolveItemDisplayName == null || string.IsNullOrWhiteSpace(itemId))
        {
            return;
        }

        try
        {
            AddLabel(labels, resolveItemDisplayName(itemId));
        }
        catch
        {
            // Display-name lookup is only an alias aid; metadata labels still provide the guard.
        }
    }

    private static void AddLabel(ICollection<string> labels, string label)
    {
        string normalized = label?.Trim() ?? string.Empty;
        if (normalized.Length > 0)
        {
            labels.Add(normalized);
        }
    }

    private sealed record PageCleanupResult(
        string Page,
        bool Removed,
        IReadOnlySet<string> RemovedTrackedLabels,
        bool HasRequiredRequest,
        bool ContinueOptionalRemoval,
        bool RemovedRequestImmediatelyBefore,
        IReadOnlySet<string> NewRequiredItemIds);
}
