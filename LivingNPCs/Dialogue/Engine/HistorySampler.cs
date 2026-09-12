using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 提示词内历史采样（WP10 §4.15）：四类记录 + 第三方目击 + 活动事件合成，按时间合并后
/// 无本轮查询时取最近 20 条；有查询时保留最近接续与匹配记录，统一受 4000 字符预算约束。
/// 完整记录按时间正序输出，不裁剪当前会话、不改写存档。纯逻辑、可单测。
/// </summary>
internal static class HistorySampler
{
    public const int MaxEntries = 20;
    public const int CharacterBudget = 4000;
    internal const int RecentContinuityEntries = 2;
    internal const int RelatedEntries = 6;

    private sealed record HistoryEntry(StardewTime Time, string Text, string Subject, string TopicText,
        string DirectSource = "", bool HasFollowingReference = false)
    {
        public bool IsDirect => this.DirectSource.Length > 0;
    }

    private const string IncompleteEvidenceNotice =
        "[Some earlier direct records were withheld because later related evidence could not fit. "
        + "The latest status of those facts or agreements is unavailable; do not infer it from older quotes.]";

    /// <summary>可合成为历史条目的活动事件键（§4.15）。</summary>
    private static readonly Dictionary<string, (string PromptKey, string Fallback)> SynthesizableEvents = new(StringComparer.Ordinal)
    {
        ["cc_Bus"] = ("cc_Bus_Repaired", "The farmer helped restore bus service to Calico Desert."),
        ["cc_Boulder"] = ("cc_Boulder_Removed", "The farmer helped clear the mountain boulder."),
        ["cc_Bridge"] = ("cc_Bridge", "The farmer helped repair the bridge to the quarry."),
        ["cc_Complete"] = ("cc_Complete", "The farmer helped restore the Community Center."),
        ["cc_Greenhouse"] = ("cc_Greenhouse", "The farmer restored the old greenhouse on the farm."),
        ["cc_Minecart"] = ("cc_Minecart", "The farmer helped get the minecarts running again."),
        ["wonIceFishing"] = ("wonIceFishing", "The farmer won the ice fishing contest."),
        ["wonGrange"] = ("wonGrange", "The farmer won the grange display at the Stardew Valley Fair."),
        ["wonEggHunt"] = ("wonEggHunt", "The farmer won the Egg Festival hunt.")
    };

    private static readonly Regex TemplateTokens = new(@"\{\{\s*([\w.\-]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>采样并格式化为提示词历史行（时间正序）。</summary>
    public static List<string> Sample(
        StardewEventHistory history,
        IReadOnlyList<KeyValuePair<string, int>> activeDialogueEvents,
        StardewTime now,
        string npcDisplayName,
        string currentConversationId,
        Func<string, string?>? getPrompt = null,
        string? currentPlayerText = null)
    {
        getPrompt ??= LookupPrompt;
        var entries = new List<HistoryEntry>();

        foreach (var entry in history.ConversationHistory)
        {
            string id = entry.Item2.ConversationElements.FirstOrDefault()?.Id ?? string.Empty;
            if (!string.IsNullOrEmpty(currentConversationId) && id == currentConversationId)
            {
                // 当前进行中的会话不重复入历史（§4.15）。
                continue;
            }

            string transcript = JoinConversation(entry.Item2.ConversationElements, npcDisplayName, getPrompt,
                out string topicText, out bool hasFollowingReference);
            if (transcript.Length > 0)
            {
                entries.Add(new(entry.Item1, Format("historyConversationFormat", entry.Item1, transcript, npcDisplayName, string.Empty, npcDisplayName, null, getPrompt), string.Empty, topicText,
                    DirectSource: "conversation", HasFollowingReference: hasFollowingReference));
            }
        }

        foreach (var entry in history.DialogueHistory)
        {
            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add(new(entry.Item1, Format("historyDialogueFormat", entry.Item1, text, npcDisplayName, string.Empty, npcDisplayName, null, getPrompt), string.Empty, text,
                    DirectSource: "dialogue", HasFollowingReference: ConversationEvidenceCues.HasAnaphoricReference(text)));
            }
        }

        foreach (var entry in history.EventHistory)
        {
            if (RsvAiPolicy.ContainsBlockedReference(entry.Item2.EventName)
                || RsvAiPolicy.IsBlockedDialogueKey(entry.Item2.EventName))
            {
                continue;
            }

            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add(new(entry.Item1, Format("historyEventFormat", entry.Item1, text, npcDisplayName, entry.Item2.EventName, npcDisplayName, entry.Item2.Listeners, getPrompt), entry.Item2.EventName, text));
            }
        }

        foreach (var entry in history.OverheardHistory)
        {
            if (RsvAiPolicy.IsBlockedNpcName(entry.Item2.SpeakerName))
            {
                continue;
            }

            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add(new(entry.Item1, Format("historyOverheardFormat", entry.Item1, text, entry.Item2.SpeakerName, string.Empty, npcDisplayName, null, getPrompt), entry.Item2.SpeakerName, text));
            }
        }

        foreach (var entry in history.ThirdPartyHistory)
        {
            if (RsvAiPolicy.IsBlockedNpcName(entry.Item2.SpeakerName)
                || RsvAiPolicy.ContainsBlockedReference(entry.Item2.EventName)
                || RsvAiPolicy.IsBlockedDialogueKey(entry.Item2.EventName))
            {
                continue;
            }

            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                // historyThirdPartyFestival is only an event-name suffix, never a complete record.
                entries.Add(new(entry.Item1, Format("historyThirdPartyFormat", entry.Item1, text, entry.Item2.SpeakerName, entry.Item2.EventName, npcDisplayName, null, getPrompt), entry.Item2.SpeakerName, $"{entry.Item2.EventName}\n{text}"));
            }
        }

        foreach (var synthesized in SynthesizeActiveEvents(activeDialogueEvents, now, getPrompt))
        {
            entries.Add(new(synthesized.Time, synthesized.Text, string.Empty, synthesized.Text));
        }

        // These arrays have no shared sequence number. Equal timestamps from two source
        // types cannot be ordered by the incidental order in which their arrays were read.
        var ambiguousTimes = entries.Where(entry => entry.IsDirect).GroupBy(entry => entry.Time)
            .Where(group => group.Select(entry => entry.DirectSource).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet();
        var newestFirst = entries.Select(entry => entry.IsDirect && ambiguousTimes.Contains(entry.Time)
                ? entry with { Text = entry.Text + " [Order among different history sources at this timestamp is unknown.]" }
                : entry)
            .Distinct().OrderByDescending(entry => entry.Time).ToList();
        var directEntries = newestFirst.Where(entry => entry.IsDirect).OrderBy(entry => entry.Time).ToArray();
        var revisionEvidence = new Dictionary<HistoryEntry, IReadOnlyList<HistoryEntry>>();
        var kept = new HashSet<HistoryEntry>();
        int budget = CharacterBudget;
        bool incompleteEvidence = false;
        bool TryKeep(HistoryEntry entry)
        {
            if (kept.Contains(entry))
            {
                return false;
            }

            if (!revisionEvidence.TryGetValue(entry, out var revisions))
            {
                revisions = FindLaterRevisionEvidence(entry, directEntries);
                revisionEvidence[entry] = revisions;
            }
            var additions = revisions.Prepend(entry).Distinct().Where(candidate => !kept.Contains(candidate)).ToArray();
            if (additions.Sum(candidate => (long)candidate.Text.Length) > budget || kept.Count + additions.Length > MaxEntries)
            {
                // Never show an old claim on its own when a known possible correction was
                // dropped for size. Keep already-selected recent evidence; no storage changes.
                incompleteEvidence |= revisions.Count > 0;
                return false;
            }

            foreach (HistoryEntry addition in additions)
            {
                budget -= addition.Text.Length;
                kept.Add(addition);
            }
            return true;
        }

        MemoryTopicQuery query = MemoryTopicQuery.Create(currentPlayerText);
        if (!query.HasQuery)
        {
            foreach (HistoryEntry entry in newestFirst.Take(MaxEntries))
            {
                // One oversized transcript must not suppress every smaller record behind it.
                TryKeep(entry);
            }
        }
        else
        {
            // Search the allowed stored records before applying the old recency window. Search
            // raw evidence, not localized boilerplate which can contain unrelated topic words.
            var matches = newestFirst
                .Where(entry => entry.Text.Length <= CharacterBudget)
                .Select(entry => (Entry: entry, Score: query.Score(entry.Subject, entry.TopicText)))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Entry.Time)
                .ToList();

            // Reserve one recent complete record before an older match can fill the budget: it
            // may contain a correction such as "I no longer like it" without the query's noun.
            foreach (HistoryEntry entry in newestFirst)
            {
                if (TryKeep(entry))
                {
                    break;
                }
            }
            // Also retain the latest matching evidence so a newer bilingual correction does
            // not lose every slot to older literal matches. Similarity never resolves facts.
            var latestMatch = matches.OrderByDescending(match => match.Entry.Time)
                .FirstOrDefault(match => !kept.Contains(match.Entry) && match.Entry.Text.Length <= budget);
            if (latestMatch.Entry != null)
            {
                TryKeep(latestMatch.Entry);
            }
            foreach (var match in matches)
            {
                if (TryKeep(match.Entry))
                {
                    break;
                }
            }
            foreach (HistoryEntry entry in newestFirst.Take(RecentContinuityEntries))
            {
                TryKeep(entry);
            }
            int relatedKept = matches.Count(match => kept.Contains(match.Entry));
            foreach (var match in matches)
            {
                if (relatedKept >= RelatedEntries)
                {
                    break;
                }
                if (TryKeep(match.Entry))
                {
                    relatedKept++;
                }
            }
        }

        var selected = kept
            .OrderBy(entry => entry.Time)
            .ThenBy(entry => newestFirst.IndexOf(entry))
            .ToList();
        if (incompleteEvidence)
        {
            // Entries added as another record's dependencies may never have reached TryKeep
            // themselves. Retain their dependency graph until all budget trimming is done.
            foreach (HistoryEntry entry in selected)
            {
                if (!revisionEvidence.ContainsKey(entry))
                {
                    revisionEvidence[entry] = FindLaterRevisionEvidence(entry, directEntries);
                }
            }
            // At an ambiguous timestamp the first line can be the correction. Removing it
            // must also remove any claims that would otherwise outlive their evidence.
            while (selected.Count > 0 && (selected.Count >= MaxEntries || selected.Sum(entry => entry.Text.Length) + IncompleteEvidenceNotice.Length > CharacterBudget))
            {
                kept.Remove(selected[0]);
                HistoryEntry[] unsupported;
                do
                {
                    unsupported = selected.Where(kept.Contains)
                        .Where(entry => revisionEvidence[entry].Any(revision => !kept.Contains(revision)))
                        .ToArray();
                    kept.ExceptWith(unsupported);
                }
                while (unsupported.Length > 0);
                selected.RemoveAll(entry => !kept.Contains(entry));
            }
        }
        var result = selected.Select(entry => entry.Text).ToList();
        if (incompleteEvidence)
        {
            result.Insert(0, IncompleteEvidenceNotice);
        }
        return result;
    }

    private static IReadOnlyList<HistoryEntry> FindLaterRevisionEvidence(HistoryEntry anchor, IReadOnlyList<HistoryEntry> directEntries)
    {
        if (!anchor.IsDirect)
        {
            return Array.Empty<HistoryEntry>();
        }

        string anchorTopic = ConversationEvidenceCues.GetTopicText(anchor.TopicText);
        var anchorQuery = MemoryTopicQuery.Create(anchorTopic);
        var anchorTokens = LocalTextSearch.Tokenize(anchorTopic, maxTokens: 512);
        bool HasOversizedTopicContinuation(HistoryEntry entry) => entry.Text.Length > CharacterBudget
            && LocalTextSearch.Tokenize(ConversationEvidenceCues.GetTopicText(entry.TopicText), maxTokens: 512)
                .Count(anchorTokens.Contains) >= 2;

        var revisions = directEntries.Where(entry => entry.Time == anchor.Time && entry.DirectSource != anchor.DirectSource
                && (ConversationEvidenceCues.HasRevisionCue(entry.TopicText) || ConversationEvidenceCues.HasRevisionCue(anchor.TopicText)
                    || HasOversizedTopicContinuation(entry)))
            .ToList();
        var related = new HashSet<HistoryEntry>(revisions) { anchor };
        int anchorIndex = directEntries.ToList().IndexOf(anchor);
        for (int index = anchorIndex + 1; index < directEntries.Count; index++)
        {
            HistoryEntry candidate = directEntries[index];
            bool hasRevision = ConversationEvidenceCues.HasRevisionCue(candidate.TopicText);
            bool followsReference = candidate.HasFollowingReference && related.Contains(directEntries[index - 1]);
            // A later record can give a new value without saying "actually" or using a
            // pronoun. If that whole record cannot fit, do not leave an older repeated topic
            // as the only evidence. Require multiple lexical terms, not one shared noun;
            // this conservative omission neither merges facts nor declares a revision.
            if (!hasRevision && !followsReference && !HasOversizedTopicContinuation(candidate))
            {
                continue;
            }

            string topic = hasRevision
                ? ConversationEvidenceCues.GetRevisionTopicText(candidate.TopicText)
                : ConversationEvidenceCues.GetTopicText(candidate.TopicText);
            var revisionQuery = MemoryTopicQuery.Create(topic);
            int score = anchorQuery.Score(string.Empty, topic);
            int chainScore = related.Max(entry => revisionQuery.Score(string.Empty, ConversationEvidenceCues.GetTopicText(entry.TopicText)));
            var preceding = directEntries.Skip(anchorIndex + 1).Take(index - anchorIndex - 1).ToArray();
            var competing = preceding.Where(entry => !related.Contains(entry))
                .Select(entry => (Entry: entry, Score: revisionQuery.Score(string.Empty, ConversationEvidenceCues.GetTopicText(entry.TopicText))))
                .OrderByDescending(match => match.Score).ThenByDescending(match => match.Entry.Time)
                .FirstOrDefault();
            bool agreementChange = ConversationEvidenceCues.HasAgreementRevisionCue(candidate.TopicText);
            bool possiblePronoun = ConversationEvidenceCues.HasAnaphoricReference(candidate.TopicText)
                || agreementChange || !revisionQuery.HasQuery;
            if (competing.Score > Math.Max(score, chainScore)
                || (score == 0 && chainScore == 0 && !possiblePronoun))
            {
                continue;
            }

            // A bare "cancel it" may refer to a second agreement. Include its nearest
            // commitment context as raw evidence too; neither the sampler nor a score decides
            // which agreement changed. Witnessed/overheard speech never revises direct speech.
            if (possiblePronoun && score == 0 && chainScore == 0)
            {
                HistoryEntry nearest = preceding.LastOrDefault() ?? anchor;
                if (!agreementChange && !related.Contains(nearest))
                {
                    // A factual "actually it is green" normally continues the nearest
                    // ordinary fact, not an older promise. Do not drop that promise because
                    // a separate object's correction is large or missing its noun.
                    continue;
                }
                var antecedent = agreementChange
                    ? preceding.Prepend(anchor).LastOrDefault(entry => ConversationEvidenceCues.HasCommitmentCue(entry.TopicText)
                        || ConversationEvidenceCues.HasAgreementRevisionCue(entry.TopicText)) ?? nearest
                    : nearest;
                if (antecedent != null && related.Add(antecedent))
                {
                    revisions.Add(antecedent);
                }
            }
            if (related.Add(candidate))
            {
                revisions.Add(candidate);
            }
        }
        return revisions;
    }

    /// <summary>活动事件合成（键限定集合；天数 &lt;112 或为 112 整倍数；时间戳 = 今天 − 天数）。</summary>
    internal static List<(StardewTime Time, string Text)> SynthesizeActiveEvents(
        IReadOnlyList<KeyValuePair<string, int>> activeDialogueEvents,
        StardewTime now,
        Func<string, string?>? getPrompt = null)
    {
        getPrompt ??= LookupPrompt;
        var result = new List<(StardewTime, string)>();
        foreach (var pair in activeDialogueEvents)
        {
            if (!SynthesizableEvents.TryGetValue(pair.Key, out var definition))
            {
                continue;
            }

            int days = pair.Value;
            if (days >= 112 && days % 112 != 0)
            {
                continue;
            }

            StardewTime when = now.AddDays(-days);
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["days"] = days.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["when"] = FormatTime(when)
            };
            // Preserve custom legacy keys, then use the localized assets that actually exist.
            string text = RenderTemplate("historyActiveEvent_" + pair.Key, tokens, getPrompt)
                ?? RenderTemplate(definition.PromptKey, tokens, getPrompt)
                ?? definition.Fallback;
            result.Add((when, WithTime(text, tokens["when"])));
        }

        return result;
    }

    private static string JoinConversation(List<ConversationElement> elements, string npcDisplayName,
        Func<string, string?> getPrompt, out string topicText, out bool hasFollowingReference)
    {
        string farmerLabel = getPrompt("generalFarmerLabel") ?? "Farmer";
        var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
            elements,
            element => element.Text,
            element => element.IsPlayerLine);
        var allowed = cleaned
            .Where(element => !string.IsNullOrWhiteSpace(element.Text)
                && !RsvAiPolicy.ContainsBlockedReference(element.Text))
            .ToArray();
        topicText = string.Join(" / ", allowed.Select(element => element.Text.Trim()));
        hasFollowingReference = allowed.Any(element => element.IsPlayerLine
                && ConversationEvidenceCues.HasAnaphoricReference(element.Text))
            || (!allowed.Any(element => element.IsPlayerLine)
                && allowed.Any(element => ConversationEvidenceCues.HasAnaphoricReference(element.Text)));
        return string.Join(" / ", allowed.Select(element =>
            $"{(element.IsPlayerLine ? farmerLabel : npcDisplayName)}: {element.Text.Trim()}"));
    }

    private static string JoinLines(List<HistoryLine> lines)
    {
        return string.Join(
            " / ",
            lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text)
                    && !RsvAiPolicy.ContainsBlockedReference(line.Text))
                .Select(line => line.Text.Trim()));
    }

    private static string Format(
        string key,
        StardewTime time,
        string text,
        string speaker,
        string eventName,
        string observer,
        IReadOnlyList<string>? listeners,
        Func<string, string?> getPrompt)
    {
        string when = FormatTime(time);
        string[] allowedListeners = (listeners ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && !RsvAiPolicy.IsBlockedNpcName(name)
                && !RsvAiPolicy.ContainsBlockedReference(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string listenerText = string.Join(", ", allowedListeners);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["when"] = when,
            ["text"] = text,
            ["speaker"] = speaker,
            ["observer"] = observer,
            ["eventName"] = eventName,
            ["listeners"] = listenerText,
            // Text/event aliases have stable meanings across older content packs.
            ["builder"] = text,
            ["totalDialogue"] = text,
            ["allListeners"] = listenerText.Length > 0
                ? listenerText
                : key == "historyDialogueFormat" ? getPrompt("generalFarmerLabel") ?? "Farmer" : string.Empty,
            ["festivalName"] = eventName
        };
        if (key is not "historyThirdPartyFormat" and not "historyOverheardFormat")
        {
            tokens["npcName"] = speaker;
            tokens["name"] = speaker;
        }
        // Legacy witness templates used Name/npcName for opposite roles in their base and
        // gender variants. Leave those ambiguous aliases unresolved so they take the complete
        // fact fallback; custom witness templates can use explicit observer/speaker instead.
        string eventContext = string.IsNullOrWhiteSpace(eventName)
            ? string.Empty
            : RenderTemplate("historyThirdPartyFestival", tokens, getPrompt) ?? $" ({eventName})";
        if (!string.IsNullOrWhiteSpace(eventName) && !eventContext.Contains(eventName, StringComparison.Ordinal))
        {
            eventContext = $" ({eventName})";
        }

        tokens["festivalNameString"] = eventContext;
        tokens["listenerContext"] = listenerText.Length == 0
            ? string.Empty
            : RenderTemplate("historyListeners", tokens, getPrompt) ?? $" ({listenerText})";

        string? formatted = RenderTemplate(key, tokens, getPrompt);
        // A localized/custom template must retain the evidence, not replace it with boilerplate.
        // Inspect the template's parameters before substitution so literal {{...}} in dialogue
        // remains ordinary data rather than being mistaken for an unresolved template parameter.
        if (!string.IsNullOrWhiteSpace(formatted)
            && new[] { text, speaker, eventName, observer }.Concat(allowedListeners)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .All(value => formatted.Contains(value, StringComparison.Ordinal)))
        {
            return WithTime(formatted, when);
        }

        // No persistence is changed: only the sampled prompt falls back to a complete fact line.
        string observerLabel = !string.IsNullOrWhiteSpace(observer) && observer != speaker ? $"{observer} ← " : string.Empty;
        string speakerLabel = string.IsNullOrWhiteSpace(speaker) ? string.Empty : $"{speaker}: ";
        string eventLabel = string.IsNullOrWhiteSpace(eventName) ? string.Empty : $" ({eventName})";
        string listenerLabel = listenerText.Length == 0 ? string.Empty : $" [{listenerText}]";
        return $"[{when}] {observerLabel}{speakerLabel}{text}{eventLabel}{listenerLabel}";
    }

    private static string? RenderTemplate(string key, IReadOnlyDictionary<string, string> tokens, Func<string, string?> getPrompt)
    {
        string? template = getPrompt(key);
        if (string.IsNullOrWhiteSpace(template) || template == key
            || TemplateTokens.Matches(template).Cast<Match>().Any(match => !tokens.ContainsKey(match.Groups[1].Value)))
        {
            return null;
        }

        return PromptTable.ReplaceTokens(template, tokens);
    }

    private static string FormatTime(StardewTime time) =>
        $"Y{time.year} {time.season} {time.dayOfMonth} {GameStateSnapshot.ClockText(time.timeOfDay)}";

    private static string WithTime(string text, string when) =>
        text.Contains(when, StringComparison.Ordinal) ? text : $"[{when}] {text}";

    private static string? LookupPrompt(string key) => Util.GetString(key, returnNull: true);
}
