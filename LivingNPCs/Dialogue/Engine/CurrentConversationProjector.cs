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

    private static readonly string[] CommitmentPhrases =
    {
        "答应", "承诺", "说好了", "约好了", "说定", "约定", "我会帮", "我会给", "我会把",
        "我帮你", "我们明天", "promise", "promised", "agreed", "agreement", "i'll bring",
        "i will bring", "i'll help", "i will help", "we will meet", "we'll meet", "i'll meet", "i owe you"
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
        if (prefix.Length + notice.Length > characterBudget / 3)
        {
            prefix = "Current conversation:\n";
            notice = DefaultCompactedInstruction;
        }
        if (prefix.Length + notice.Length > characterBudget / 3)
        {
            notice = ShortCompactedInstruction;
        }

        var selected = new HashSet<int>();
        var replacements = new Dictionary<int, string>();
        bool latestMessageUnavailable = false;
        string Render() => RenderExcerpt(prefix, notice, exchanges, selected, replacements);

        bool TryKeep(IEnumerable<int> candidates, int limit = int.MaxValue)
        {
            int[] added = candidates.Where(index => index >= 0 && index < exchanges.Count
                    && exchanges[index].Text.Length > 0 && !selected.Contains(index))
                .Distinct().ToArray();
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

        // A contiguous recent suffix carries short answers and pronouns. Reserve some space
        // for older evidence instead of allowing four verbose exchanges to consume everything.
        int recentFloor = Math.Max(0, latest - RecentExchangeCount + (latestIsUnanswered ? 0 : 1));
        int recentLimit = Math.Max(Render().Length, characterBudget * 2 / 3);
        for (int index = latest - 1; index >= recentFloor; index--)
        {
            TryKeep(new[] { index }, index == latest - 1 ? characterBudget : recentLimit);
        }

        bool TryEvidence(int index, bool includePredecessor = false)
        {
            // A following reply may retract an agreement using only "actually, no". Keep the
            // adjacent exchange along with a recalled match whenever possible. A boundary's
            // preceding exchange also supplies the noun for "don't do that after all".
            int[] neighborhood = includePredecessor
                ? new[] { index - 1, index, index + 1 }
                : new[] { index, index + 1 };
            if (TryKeep(neighborhood))
            {
                return true;
            }
            if (exchanges[index].HasBoundary && TryKeep(new[] { index }))
            {
                TryKeep(new[] { index - 1 });
                TryKeep(new[] { index + 1 });
                return true;
            }
            return false;
        }

        // Recent refusals/corrections get an opportunity before any older literal match or
        // promise can consume the remaining budget. This is evidence selection, not a ledger.
        var older = Enumerable.Range(0, Math.Max(0, recentFloor)).Reverse().ToArray();
        foreach (int index in older.Where(index => exchanges[index].HasBoundary).Take(2))
        {
            TryEvidence(index, includePredecessor: true);
        }

        MemoryTopicQuery query = MemoryTopicQuery.Create(
            RsvAiPolicy.ContainsBlockedReference(currentPlayerText ?? string.Empty) ? null : currentPlayerText);
        var matches = older
            .Where(index => exchanges[index].Text.Length > 0)
            .Select(index => (Index: index, Score: exchanges[index].Turns
                .Where(turn => IsAllowed(turn.Text))
                .Select(turn => query.Score(string.Empty, turn.Text))
                .DefaultIfEmpty().Max()))
            .Where(match => match.Score > 0)
            .ToArray();

        int relatedKept = 0;
        // First protect the newest match, including a bilingual correction which may score
        // lower than an older literal mention. Then add high-scoring evidence from anywhere.
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

        foreach (int index in older.Where(index => exchanges[index].HasBoundary || exchanges[index].HasCommitment)
            .Take(ConstraintExchangeCount))
        {
            TryEvidence(index, includePredecessor: exchanges[index].HasBoundary);
        }

        // If optional evidence was small or absent, give any skipped recent complete exchange
        // the remaining space. Never fill gaps with unrelated old chatter just to hit a target.
        for (int index = latest - 1; index >= recentFloor; index--)
        {
            TryKeep(new[] { index });
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
                && CommitmentPhrases.Any(phrase => LocalTextSearch.ContainsPhrase(turn.Text, phrase)));
        }

        public IReadOnlyList<ConversationTurn> Turns { get; }
        public string Text { get; }
        public bool HasBoundary { get; }
        public bool HasCommitment { get; }
    }
}
