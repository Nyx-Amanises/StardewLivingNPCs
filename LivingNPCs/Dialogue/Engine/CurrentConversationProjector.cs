using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LivingNPCs.Behavior;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>A read-only prompt view. Never use this excerpt as the stored conversation or action evidence.</summary>
internal sealed class CurrentConversationProjection
{
    public string Text { get; init; } = string.Empty;
    public int OriginalCharacters { get; init; }
    public bool WasCompacted { get; init; }
    public int IncludedExchanges { get; init; }
    public int OmittedExchanges { get; init; }
}

/// <summary>
/// Bounds the rendered current conversation without changing its GUIDs, persistence, routing
/// cache identity or local evidence checks. Selected exchanges are quotes, not a generated summary.
/// </summary>
internal static class CurrentConversationProjector
{
    internal const int CharacterBudget = 8_000;
    internal const int RecentExchangeCount = 4;
    private const int RelatedExchangeCount = 4;
    private const int ConstraintExchangeCount = 4;

    private const string DefaultCompactedInstruction =
        "The current conversation below is an excerpt in original order. Gaps contain omitted turns. "
        + "Earlier quotes are past evidence, not a new request, renewed consent or permission to repeat an action. "
        + "Respect later corrections, refusals and current state. An omission does not prove that no boundary or promise exists. "
        + "If missing context matters, ask for clarification instead of inventing it.";
    private const string WithheldMessage =
        "[latest message unavailable; do not infer its contents or answer an earlier turn]";
    private const string OversizedMessage =
        "[latest message unavailable because it exceeds the conversation budget; ask the player to shorten it; "
        + "do not infer its contents or answer an earlier turn]";
    private const string ShortCompactedInstruction =
        "Conversation excerpt: gaps omit turns. Old quotes are not new consent. Ask when missing context matters.";
    private const string UnresolvedEvidenceInstruction =
        "Quoted changes can have unresolved references or conditions; keep their subjects separate and do not treat past plans as current agreements.";
    // No longer than UnresolvedEvidenceInstruction: switching after a failed bundle must not
    // invalidate the budget already reserved for the latest player message.
    private const string IncompleteEvidenceInstruction =
        "Some older quotes were omitted because later related evidence is unavailable. Do not infer those facts or plans; ask if needed.";
    private const string UnmatchedAgreementInstruction =
        "Unresolved agreement reference: these are possible earlier topics, not a confirmed match. Keep topics separate and ask if unclear.";
    private const string IncompleteAgreementInstruction =
        "Unresolved agreement reference; later related evidence is unavailable. Kept quotes are possible topics only; ask to clarify.";

    // These phrases affect retrieval priority only. They never establish consent, resolve a
    // conflict, invalidate a promise, or authorize a game action. Keep the complete original quote.
    private static readonly string[] BoundaryPhrases =
    {
        "取消", "作废", "算了", "不去了", "不用了", "别再", "不要再", "不想", "不愿",
        "不可以", "不能", "别碰", "拒绝", "不喜欢", "不再", "改主意", "改成", "改到",
        "不是", "说错", "更正", "先别", "暂时别", "不要告诉", "保密",
        "cancel", "cancelled", "canceled", "forget it", "never mind", "no longer", "not anymore",
        "don't", "do not", "won't", "will not", "can't", "cannot", "refuse", "decline", "stop",
        "instead", "actually", "changed my mind", "not today", "not now", "keep it private"
    };

    internal static CurrentConversationProjection Build(
        IReadOnlyList<ConversationTurn> conversation,
        string? currentPlayerText,
        string farmerLabel,
        string npcDisplayName,
        string prefix = "",
        string? compactedInstruction = null,
        int characterBudget = CharacterBudget)
    {
        if (characterBudget < 512)
        {
            throw new ArgumentOutOfRangeException(nameof(characterBudget), "Conversation budget must be at least 512 characters.");
        }

        // Names and localized headings are normally short, but content packs must not be able to
        // consume the whole budget before a single current message can be included.
        farmerLabel = BoundedLabel(farmerLabel, "Farmer");
        npcDisplayName = BoundedLabel(npcDisplayName, "NPC");
        prefix ??= string.Empty;
        List<Exchange> exchanges = GroupExchanges(conversation, farmerLabel, npcDisplayName);
        string original = RenderFull(prefix, exchanges);
        if (original.Length <= characterBudget)
        {
            return new CurrentConversationProjection
            {
                Text = original,
                OriginalCharacters = original.Length,
                IncludedExchanges = exchanges.Count(exchange => exchange.Text.Length > 0)
            };
        }

        string notice = string.IsNullOrWhiteSpace(compactedInstruction)
            ? DefaultCompactedInstruction
            : compactedInstruction.Trim();
        string evidenceNotice = UnresolvedEvidenceInstruction;
        if (prefix.Length + notice.Length + evidenceNotice.Length > characterBudget / 3)
        {
            prefix = "Current conversation:\n";
            notice = DefaultCompactedInstruction;
        }
        if (prefix.Length + notice.Length + evidenceNotice.Length > characterBudget / 3)
        {
            notice = ShortCompactedInstruction;
        }

        var selected = new HashSet<int>();
        var replacements = new Dictionary<int, string>();
        bool latestMessageUnavailable = false;
        string Render() => RenderExcerpt(prefix, notice + (evidenceNotice.Length > 0 ? "\n" + evidenceNotice : string.Empty),
            exchanges, selected, replacements);

        bool TryKeep(IEnumerable<int> candidates, int limit = int.MaxValue)
        {
            int[] requested = candidates.Distinct().ToArray();
            // A kept player line or omission marker cannot stand in for this exchange's
            // missing NPC answer when an older quote depends on that answer's correction.
            if (requested.Any(replacements.ContainsKey))
            {
                return false;
            }
            int[] added = requested.Where(index => index >= 0 && index < exchanges.Count
                    && exchanges[index].Text.Length > 0 && !selected.Contains(index))
                .ToArray();
            if (added.Length == 0)
            {
                return false;
            }
            foreach (int index in added)
            {
                selected.Add(index);
            }
            if (Render().Length <= Math.Min(limit, characterBudget))
            {
                return true;
            }
            foreach (int index in added)
            {
                selected.Remove(index);
            }
            return false;
        }

        int latest = exchanges.FindLastIndex(exchange => exchange.Text.Length > 0);
        bool latestIsUnanswered = latest >= 0 && exchanges[latest].Turns.LastOrDefault()?.IsPlayerLine == true;
        if (latest >= 0 && !TryKeep(new[] { latest }))
        {
            // Sacrifice optional explanatory prose before discarding an otherwise fitting
            // current message. In particular, a long external input may fit without the intro.
            string previousPrefix = prefix;
            string previousNotice = notice;
            prefix = "Current conversation:\n";
            notice = ShortCompactedInstruction;
            if (!TryKeep(new[] { latest }))
            {
                prefix = previousPrefix;
                notice = previousNotice;
            }
        }
        if (latest >= 0 && !selected.Contains(latest))
        {
            // An abnormally large external request or NPC response is never sliced into a
            // misleading partial fact. Preserve the latest player line if it fits on its own;
            // otherwise explicitly mark it unavailable. Native typed input is already <=500.
            Exchange exchange = exchanges[latest];
            ConversationTurn? lastPlayer = exchange.Turns.LastOrDefault(turn => turn.IsPlayerLine);
            bool playerIsCurrent = lastPlayer != null && (latestIsUnanswered
                || (!string.IsNullOrWhiteSpace(currentPlayerText)
                    && string.Equals(currentPlayerText.Trim(), lastPlayer.Text.Trim(), StringComparison.Ordinal)));
            string currentLine = playerIsCurrent ? FormatTurn(lastPlayer!, farmerLabel, npcDisplayName) : string.Empty;
            replacements[latest] = currentLine.Length > 0
                ? "[other lines in this exchange omitted because it exceeds the budget]\n" + currentLine
                : "[latest completed exchange unavailable because it exceeds the conversation budget; "
                    + "its earlier question has already been answered; do not infer the missing reply]";
            latestMessageUnavailable = !playerIsCurrent;
            selected.Add(latest);
            if (Render().Length > characterBudget)
            {
                replacements[latest] = farmerLabel + ": " + OversizedMessage;
                latestMessageUnavailable = true;
            }
            if (Render().Length > characterBudget)
            {
                // Small custom budgets still include every label, omission marker and wrapper.
                // A fixed short fallback must itself fit; do not truncate the wrapped evidence.
                prefix = string.Empty;
                notice = "Latest message unavailable: ask the player to shorten it, without answering an earlier turn.";
                evidenceNotice = string.Empty;
                replacements[latest] = "Farmer: " + OversizedMessage;
                latestMessageUnavailable = true;
            }
        }

        if (latestMessageUnavailable)
        {
            return new CurrentConversationProjection
            {
                Text = Render(),
                OriginalCharacters = original.Length,
                WasCompacted = true,
                IncludedExchanges = selected.Count,
                OmittedExchanges = exchanges.Count(exchange => exchange.Text.Length > 0) - selected.Count
            };
        }

        int recentFloor = Math.Max(0, latest - RecentExchangeCount + (latestIsUnanswered ? 0 : 1));
        MemoryTopicQuery query = MemoryTopicQuery.Create(
            RsvAiPolicy.ContainsBlockedReference(currentPlayerText ?? string.Empty) ? null : currentPlayerText);
        var evidence = new EvidenceLinks(exchanges, MemoryTopicQuery.Create(
            RsvAiPolicy.ContainsBlockedReference(currentPlayerText ?? string.Empty)
                ? null : ConversationEvidenceCues.GetTopicText(currentPlayerText)), latestIsUnanswered ? latest : exchanges.Count);
        var older = Enumerable.Range(0, Math.Max(0, recentFloor)).Reverse().ToArray();
        var matches = older
            .Where(index => exchanges[index].Text.Length > 0)
            .Select(index => (Index: index, Score: exchanges[index].Turns
                .Where(turn => IsAllowed(turn.Text))
                .Select(turn => query.Score(string.Empty, turn.Text))
                .DefaultIfEmpty().Max()))
            .Where(match => match.Score > 0)
            .ToArray();
        bool usingAgreementFallback = matches.Length == 0
            && ConversationEvidenceCues.HasAgreementReferenceCue(currentPlayerText);
        if (usingAgreementFallback)
        {
            evidenceNotice = UnmatchedAgreementInstruction;
        }

        bool TryEvidence(int index, bool includePredecessor = false, int limit = int.MaxValue, bool addNeighbors = true)
        {
            int[] required = evidence.RequiredFor(index);
            if (!required.All(candidate => selected.Contains(candidate) && !replacements.ContainsKey(candidate))
                && !TryKeep(required, limit))
            {
                if (required.Any(candidate => candidate > index) || exchanges[index].HasRevision)
                {
                    evidenceNotice = usingAgreementFallback ? IncompleteAgreementInstruction : IncompleteEvidenceInstruction;
                }
                return false;
            }

            if (addNeighbors)
            {
                // A nearby quote is optional unless it contains a reference/clarification. It
                // must satisfy its own dependencies too: an unrelated adjacent promise must not
                // sneak in without its later cancellation merely as another quote's neighbor.
                foreach (int neighbor in includePredecessor ? new[] { index - 1, index + 1 } : new[] { index + 1 })
                {
                    if (neighbor >= 0 && neighbor < exchanges.Count)
                    {
                        TryKeep(evidence.RequiredFor(neighbor), limit);
                    }
                }
            }
            return true;
        }

        // A recent suffix carries short answers and pronouns. Even a recent old claim must carry
        // a later correction before it can be quoted. Reserve room for topic-specific evidence.
        int recentLimit = Math.Max(Render().Length, characterBudget * 2 / 3);
        for (int index = latest - 1; index >= recentFloor; index--)
        {
            TryEvidence(index, limit: index == latest - 1 ? characterBudget : recentLimit, addNeighbors: false);
        }

        int relatedKept = 0;
        // Related updates are bundled before global boundary snippets compete for the budget.
        // Otherwise several unrelated refusals can push out the cancellation of this very topic.
        // First protect the newest match, including weaker bilingual matches, then older evidence.
        foreach (int index in matches.OrderByDescending(match => match.Index).Take(1).Select(match => match.Index)
            .Concat(matches.OrderByDescending(match => match.Score).ThenByDescending(match => match.Index)
                .Select(match => match.Index)).Distinct())
        {
            if (relatedKept >= RelatedExchangeCount)
            {
                break;
            }
            if (TryEvidence(index))
            {
                relatedKept++;
            }
        }

        if (usingAgreementFallback)
        {
            // A pronoun or an untranslated topic may leave the local vocabulary with no match.
            // Try a few complete promise/change groups before unrelated global boundaries use
            // their budget. They are candidate prior topics, never a resolved referent or state.
            int agreementsKept = 0;
            foreach (int index in older.Where(index => exchanges[index].HasCommitment))
            {
                if (TryEvidence(index) && ++agreementsKept >= ConstraintExchangeCount)
                {
                    break;
                }
            }
        }

        foreach (int index in older.Where(index => exchanges[index].HasBoundary).Take(2))
        {
            TryEvidence(index, includePredecessor: true);
        }

        foreach (int index in older.Where(index => exchanges[index].HasBoundary || exchanges[index].HasCommitment)
            .Take(ConstraintExchangeCount))
        {
            TryEvidence(index, includePredecessor: exchanges[index].HasBoundary);
        }

        // If optional evidence was small or absent, give any skipped recent complete exchange
        // the remaining space. Never fill gaps with unrelated old chatter just to hit a target.
        for (int index = latest - 1; index >= recentFloor; index--)
        {
            TryEvidence(index, addNeighbors: false);
        }

        string text = Render();
        return new CurrentConversationProjection
        {
            Text = text,
            OriginalCharacters = original.Length,
            WasCompacted = true,
            IncludedExchanges = selected.Count,
            OmittedExchanges = exchanges.Count(exchange => exchange.Text.Length > 0) - selected.Count
        };
    }

    private static List<Exchange> GroupExchanges(
        IReadOnlyList<ConversationTurn> conversation, string farmerLabel, string npcDisplayName)
    {
        var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
            conversation, turn => turn.Text, turn => turn.IsPlayerLine);
        // Match the engine's latest-question policy for direct assembler callers too. Never
        // expose an earlier question as the latest one after withholding the actual question.
        ConversationTurn? latestPlayer = cleaned.LastOrDefault(turn => turn.IsPlayerLine);
        if (latestPlayer != null && (RsvAiPolicy.IsWithheldPlayerMessage(latestPlayer.Text)
            || RsvAiPolicy.ContainsBlockedReference(latestPlayer.Text)))
        {
            return new List<Exchange>
            {
                new(new List<ConversationTurn> { latestPlayer }, farmerLabel + ": " + WithheldMessage)
            };
        }

        var groups = new List<List<ConversationTurn>>();
        List<ConversationTurn>? current = null;
        bool hasNpc = false;
        foreach (ConversationTurn turn in cleaned)
        {
            // Establish boundaries before filtering: a withheld or blank player turn must
            // still separate the NPC's answer from the preceding question.
            if (current == null || (turn.IsPlayerLine && hasNpc))
            {
                current = new List<ConversationTurn>();
                groups.Add(current);
                hasNpc = false;
            }
            current.Add(turn);
            hasNpc |= !turn.IsPlayerLine;
        }

        return groups.Select(turns => new Exchange(turns,
                string.Join("\n", turns.Select(turn => FormatTurn(turn, farmerLabel, npcDisplayName))
                    .Where(text => text.Length > 0))))
            .ToList();
    }

    private static string FormatTurn(ConversationTurn turn, string farmerLabel, string npcDisplayName)
    {
        if (RsvAiPolicy.IsWithheldPlayerMessage(turn.Text))
        {
            return farmerLabel + ": " + WithheldMessage;
        }
        return IsAllowed(turn.Text)
            ? $"{(turn.IsPlayerLine ? farmerLabel : npcDisplayName)}: {turn.Text}".TrimEnd()
            : string.Empty;
    }

    private static bool IsAllowed(string text) => !string.IsNullOrWhiteSpace(text)
        && !ConversationTextPostProcessor.LooksLikeWrongLanguage(text)
        && !RsvAiPolicy.ContainsBlockedReference(text);

    private static string BoundedLabel(string text, string fallback) =>
        string.IsNullOrWhiteSpace(text) || PromptDataBoundary.Escape(text).Length > 128 ? fallback : text;

    private static string RenderFull(string prefix, IReadOnlyList<Exchange> exchanges) =>
        prefix + PromptDataBoundary.Wrap("conversation_history",
            string.Join("\n", exchanges.Where(exchange => exchange.Text.Length > 0).Select(exchange => exchange.Text))) + Environment.NewLine;

    private static string RenderExcerpt(
        string prefix, string notice, IReadOnlyList<Exchange> exchanges,
        IReadOnlySet<int> selected, IReadOnlyDictionary<int, string> replacements)
    {
        var transcript = new StringBuilder();
        int previous = -1;
        foreach (int index in selected.OrderBy(index => index))
        {
            if (index > previous + 1)
            {
                transcript.Append("[... ").Append(index - previous - 1).Append(" exchange(s) omitted ...]\n");
            }
            transcript.Append("[Exchange ").Append(index + 1).Append("]\n");
            transcript.Append(replacements.TryGetValue(index, out string? replacement)
                ? replacement : exchanges[index].Text).Append('\n');
            previous = index;
        }
        if (previous < exchanges.Count - 1)
        {
            transcript.Append("[... ").Append(exchanges.Count - previous - 1).Append(" exchange(s) omitted ...]\n");
        }
        return prefix + notice + "\n" + PromptDataBoundary.Wrap("conversation_history", transcript.ToString()) + Environment.NewLine;
    }

    private sealed class Exchange
    {
        public Exchange(List<ConversationTurn> turns, string text)
        {
            this.Turns = turns;
            this.Text = text;
            this.HasBoundary = turns.Any(turn => IsAllowed(turn.Text)
                && BoundaryPhrases.Any(phrase => LocalTextSearch.ContainsPhrase(turn.Text, phrase)));
            this.HasCommitment = turns.Any(turn => IsAllowed(turn.Text)
                && ConversationEvidenceCues.HasCommitmentCue(turn.Text));
            this.HasReference = turns.Any(turn => IsAllowed(turn.Text)
                && ConversationEvidenceCues.HasAnaphoricReference(turn.Text));
            this.HasAgreementRevision = turns.Any(turn => IsAllowed(turn.Text)
                && ConversationEvidenceCues.HasAgreementRevisionCue(turn.Text));
            this.HasReference |= this.HasAgreementRevision;
            this.HasFollowingReference = turns.Any(turn => turn.IsPlayerLine && IsAllowed(turn.Text)
                    && ConversationEvidenceCues.HasAnaphoricReference(turn.Text))
                || (!turns.Any(turn => turn.IsPlayerLine) && this.HasReference);
            this.HasRevision = turns.Any(turn => IsAllowed(turn.Text)
                    && ConversationEvidenceCues.HasRevisionCue(turn.Text))
                || (this.HasBoundary && this.HasReference);
            this.HasBoundary |= this.HasRevision;
            this.TopicLines = turns.Where(turn => IsAllowed(turn.Text))
                .Select(turn => ConversationEvidenceCues.GetTopicText(turn.Text)).ToArray();
            this.NpcTopicLines = turns.Where(turn => !turn.IsPlayerLine && IsAllowed(turn.Text))
                .Select(turn => ConversationEvidenceCues.GetTopicText(turn.Text)).ToArray();
        }

        public IReadOnlyList<ConversationTurn> Turns { get; }
        public string Text { get; }
        public bool HasBoundary { get; }
        public bool HasCommitment { get; }
        public bool HasReference { get; }
        public bool HasFollowingReference { get; }
        public bool HasRevision { get; }
        public bool HasAgreementRevision { get; }
        public IReadOnlyList<string> TopicLines { get; }
        public IReadOnlyList<string> NpcTopicLines { get; }
    }

    /// <summary>
    /// Links evidence, never agreement state. A pronoun can have several possible antecedents;
    /// keeping their actual words lets the reply ask when the reference remains unresolved.
    /// </summary>
    private sealed class EvidenceLinks
    {
        private readonly IReadOnlyList<Exchange> exchanges;
        private readonly int[] revisions;
        private readonly int[] currentTopicMatches;
        private readonly int latestCurrentTopicEvidence;
        private readonly Dictionary<int, int[]> required = new();
        private readonly Dictionary<int, (int Index, int Score, int[] Scores)> antecedents = new();

        public EvidenceLinks(IReadOnlyList<Exchange> exchanges, MemoryTopicQuery currentTopic, int completedEnd)
        {
            this.exchanges = exchanges;
            this.revisions = Enumerable.Range(0, exchanges.Count).Where(index => exchanges[index].HasRevision).ToArray();
            this.currentTopicMatches = Enumerable.Range(0, completedEnd)
                .Where(index => Score(currentTopic, exchanges[index]) > 0).ToArray();
            this.latestCurrentTopicEvidence = this.currentTopicMatches.LastOrDefault(index =>
                exchanges[index].NpcTopicLines.Any(line => currentTopic.Score(string.Empty, line) > 0), -1);
        }

        public int[] RequiredFor(int index)
        {
            if (this.required.TryGetValue(index, out int[]? cached))
            {
                return cached;
            }

            var result = new HashSet<int> { index };
            var pending = new Queue<int>();
            pending.Enqueue(index);
            void Add(int candidate)
            {
                if (candidate >= 0 && candidate < this.exchanges.Count && result.Add(candidate))
                {
                    pending.Enqueue(candidate);
                }
            }

            while (pending.Count > 0)
            {
                int source = pending.Dequeue();
                Exchange exchange = this.exchanges[source];
                // Short clarification chains can be longer than one exchange ("Are you sure?",
                // "Which shelf?"). Stop at the first new non-referential topic.
                if (source + 1 < this.exchanges.Count && this.exchanges[source + 1].HasFollowingReference)
                {
                    Add(source + 1);
                }

                if (this.currentTopicMatches.Contains(source) && this.latestCurrentTopicEvidence > source)
                {
                    // A newer explicit fact can correct an older one without saying "actually".
                    // Never keep the old match by itself when the newest match cannot fit.
                    // A repeated player question is not itself a newer answer. An unrelated
                    // oversized question containing the topic must not erase a usable old fact.
                    Add(this.latestCurrentTopicEvidence);
                }

                if (exchange.HasRevision)
                {
                    Add(this.AntecedentFor(source).Index);
                }

                foreach (int revision in this.revisions)
                {
                    if (revision <= source)
                    {
                        continue;
                    }
                    var antecedent = this.AntecedentFor(revision);
                    int score = antecedent.Scores[source];
                    Exchange change = this.exchanges[revision];
                    bool unresolvedReference = antecedent.Score == 0 && change.HasReference
                        && (change.HasAgreementRevision
                            ? exchange.HasCommitment || exchange.HasAgreementRevision || this.currentTopicMatches.Contains(source)
                            : source == antecedent.Index);
                    // A stronger explicit match to another subject prevents applying e.g. a
                    // library cancellation to the separate lake promise just because both meet.
                    if ((score > 0 && score >= antecedent.Score) || unresolvedReference)
                    {
                        Add(revision);
                    }
                }
            }

            int[] bundle = result.OrderBy(candidate => candidate).ToArray();
            this.required[index] = bundle;
            return bundle;
        }

        private (int Index, int Score, int[] Scores) AntecedentFor(int revision)
        {
            if (this.antecedents.TryGetValue(revision, out var cached))
            {
                return cached;
            }

            Exchange change = this.exchanges[revision];
            var queries = change.TopicLines.Select(line => MemoryTopicQuery.Create(
                change.HasReference ? ConversationEvidenceCues.GetRevisionTopicText(line) : line)).ToArray();
            var scores = new int[revision];
            int bestIndex = -1;
            int bestScore = 0;
            for (int index = revision - 1; index >= 0; index--)
            {
                int score = queries.Select(query => Score(query, this.exchanges[index])).DefaultIfEmpty().Max();
                scores[index] = score;
                if (score > bestScore)
                {
                    bestIndex = index;
                    bestScore = score;
                }
            }

            if (bestIndex < 0 && change.HasReference)
            {
                // "Cancel that" can concern an earlier promise. A plain "actually, it is green"
                // instead needs the nearer ordinary fact, not an unrelated older commitment.
                // These remain possible antecedents; neither branch resolves agreement state.
                for (int index = revision - 1; index >= 0; index--)
                {
                    if (change.HasAgreementRevision
                        ? this.exchanges[index].HasCommitment
                        : this.exchanges[index].Text.Length > 0)
                    {
                        bestIndex = index;
                        break;
                    }
                }
            }
            var result = (bestIndex, bestScore, scores);
            this.antecedents[revision] = result;
            return result;
        }

        private static int Score(MemoryTopicQuery query, Exchange exchange) =>
            exchange.TopicLines.Select(line => query.Score(string.Empty, line)).DefaultIfEmpty().Max();
    }
}
