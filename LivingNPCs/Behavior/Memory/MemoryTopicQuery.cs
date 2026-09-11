using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

/// <summary>
/// A transient local query shared by durable-memory and historical-context selection. Search
/// vocabulary changes relevance only; it never rewrites facts, infers consent, or grants access.
/// </summary>
internal sealed class MemoryTopicQuery
{
    // The native input box accepts 500 characters, including a full CJK turn after bigrams.
    private const int MaxQueryTokens = 512;

    private readonly IReadOnlySet<string> tokens;
    private readonly IReadOnlyList<MemoryRecallVocabulary.Concept> concepts;

    private MemoryTopicQuery(string? text)
    {
        this.tokens = LocalTextSearch.Tokenize(text, MaxQueryTokens);
        this.concepts = MemoryRecallVocabulary.FindConcepts(text);
    }

    public static MemoryTopicQuery Create(string? text) => new(text);

    public bool HasQuery => this.tokens.Count > 0 || this.concepts.Count > 0;

    /// <summary>
    /// Returns the previous lexical score unchanged when words match. Controlled aliases are a
    /// weaker fallback (at most 56; one direct token is at least 58). Zero means no current topic.
    /// </summary>
    public int Score(string subject, string summary, IReadOnlyList<string>? tags = null)
    {
        if (!this.HasQuery)
        {
            return 0;
        }

        var subjectTokens = LocalTextSearch.Tokenize(subject, maxTokens: 64);
        var memoryTokens = new HashSet<string>(subjectTokens, StringComparer.OrdinalIgnoreCase);
        memoryTokens.UnionWith(LocalTextSearch.Tokenize(summary, maxTokens: 256));
        if (tags != null)
        {
            memoryTokens.UnionWith(tags);
        }

        int overlap = memoryTokens.Count(this.tokens.Contains);
        if (overlap > 0)
        {
            int subjectOverlap = subjectTokens.Count(this.tokens.Contains);
            int coverageBonus = 16 * overlap / this.tokens.Count;
            return Math.Min(100, 48 + (Math.Min(4, overlap) * 10)
                + (Math.Min(2, subjectOverlap) * 8) + coverageBonus);
        }

        int conceptMatches = 0;
        bool subjectMatches = false;
        foreach (var concept in this.concepts)
        {
            bool subjectMatch = concept.MatchesSubject(subject);
            if (subjectMatch || concept.MatchesSummary(summary))
            {
                conceptMatches++;
                subjectMatches |= subjectMatch;
            }
        }

        // Do not expand inferred tags: e.g. a beach memory may be tagged "fishing" without
        // describing fishing. A bilingual match needs the actual subject or saved summary.
        return conceptMatches == 0 ? 0 : 44 + (Math.Min(2, conceptMatches) * 4) + (subjectMatches ? 4 : 0);
    }
}
