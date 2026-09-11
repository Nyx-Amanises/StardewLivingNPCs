using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// Immutable, already rendered world documents and cached document frequencies. Construction may
/// read prompt assets; Retrieve only reads these captured strings and the supplied request.
/// </summary>
internal sealed class WorldEntryIndex
{
    internal const int DefaultEntryBudget = 8;
    internal const int DefaultCharacterBudget = 6000;
    private const int MaxQueryCharacters = 8192;
    private const string ReferenceGuidance = "World reference entries may be selected for relevance. Omitted entries do not establish absence or ignorance; do not invent missing specifics.";
    private static readonly string[] SectionNames = { "Intro", "FarmerBackground", "Seasons", "Locations", "Festivals", "Villagers", "Outro" };
    private static readonly string[] SearchSections = { "Seasons", "Locations", "Festivals", "Villagers" };
    private static readonly string[] ContinuationPhrases =
    {
        "还有呢", "接着说", "继续说", "继续聊", "然后呢", "那边呢", "那里呢", "那它", "那他们", "那她们",
        "那里怎么走", "怎么去那里", "那儿怎么走", "那边怎么走", "怎么到那里", "怎么过去",
        "tell me more", "what else", "go on", "continue", "what about that", "what about them",
        "how do i get there", "how can i get there", "how to get there", "the way there", "where is that"
    };
    private readonly IndexedEntry[] entries;
    private readonly IndexedSection[] sections;
    private readonly Dictionary<string, double> inverseDocumentFrequency;
    private readonly string coreText;
    private readonly string fullText;
    private readonly string invalidReason;

    public WorldEntryIndex(WorldSummary? summary, WorldSummaryRenderer renderer)
    {
        var capturedEntries = new List<IndexedEntry>();
        var capturedSections = new List<IndexedSection>();
        var core = new StringBuilder();
        bool invalid = summary == null || summary.SectionOrder == null || summary.SectionOrder.Count == 0;
        KeyValuePair<string, bool>[] order = !invalid
            ? summary!.SectionOrder!.ToArray()
            : SectionNames.Where(name => summary?.GetSection(name) != null)
                .Select(name => new KeyValuePair<string, bool>(name, SearchSections.Contains(name))).ToArray();
        if (order.Any(pair => summary?.GetSection(pair.Key) == null))
        {
            invalid = true;
            order = order.Where(pair => summary?.GetSection(pair.Key) != null)
                .Concat(SectionNames.Where(name => summary?.GetSection(name) != null && !order.Any(pair => pair.Key == name))
                    .Select(name => new KeyValuePair<string, bool>(name, SearchSections.Contains(name)))).ToArray();
        }

        foreach (var sectionOrder in order)
        {
            WorldSummarySection? section = summary?.GetSection(sectionOrder.Key);
            if (section == null)
            {
                invalid = true;
                continue;
            }

            bool searchable = SearchSections.Contains(sectionOrder.Key);
            var sectionEntries = new List<IndexedEntry>();
            var renderedEntries = new List<(string Region, string Text)>();
            if (section.Entries == null)
                invalid = true;

            foreach (var pair in section.Entries ?? new Dictionary<string, WorldSummaryEntry>())
            {
                WorldSummaryEntry? original = pair.Value;
                if (original == null)
                {
                    invalid = true;
                    continue;
                }

                string[] aliases = WorldEntryAliases.ForEntry(sectionOrder.Key, pair.Key, original);
                string name = !string.IsNullOrWhiteSpace(original.Name) ? original.Name
                    : !string.IsNullOrWhiteSpace(original.Id) ? original.Id : pair.Key;
                var entry = new WorldSummaryEntry
                {
                    Id = original.Id ?? string.Empty,
                    Name = name,
                    Description = original.Description ?? string.Empty,
                    Region = original.Region ?? string.Empty,
                    Crops = original.Crops?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList(),
                    Forage = original.Forage?.Where(item => !string.IsNullOrWhiteSpace(item)).ToList()
                };
                string text = renderer.RenderEntry(entry);
                if (IsBlockedEntry(sectionOrder.Key, pair.Key, entry, text))
                    continue;

                string[] regionAliases = WorldEntryAliases.ForRegion(entry.Region);

                // Copy every prompt-facing value. Later Content Patcher edits must not mutate an
                // in-flight request, and long descriptions are indexed with a bound but rendered whole.
                var captured = new IndexedEntry(
                    capturedEntries.Count, sectionOrder.Key, entry.Region ?? string.Empty, text, aliases, regionAliases,
                    LocalTextSearch.Tokenize(string.Join(" ", aliases) + " " + string.Join(" ", regionAliases) + " " + text, 1024));
                renderedEntries.Add((captured.Region, captured.Text));
                if (searchable)
                {
                    capturedEntries.Add(captured);
                    sectionEntries.Add(captured);
                }
            }

            string sectionText = RsvAiPolicy.RemoveBlockedLines(section.Text);
            if (searchable)
                capturedSections.Add(new IndexedSection(sectionOrder.Key, sectionOrder.Value, sectionText, sectionEntries.ToArray()));
            else
                core.Append(WorldSummaryRenderer.RenderCapturedSection(sectionOrder.Key, sectionOrder.Value, sectionText, renderedEntries));
        }

        string translations = RsvAiPolicy.RemoveBlockedLines(renderer.GetTranslations());
        if (!string.IsNullOrWhiteSpace(translations))
            core.AppendLine(translations);
        core.AppendLine(ReferenceGuidance);

        this.entries = capturedEntries.ToArray();
        this.sections = capturedSections.ToArray();
        this.coreText = core.ToString().TrimEnd() + Environment.NewLine;
        this.invalidReason = invalid ? "InvalidWorldIndex" : string.Empty;
        this.fullText = this.RenderSelection(this.entries.Select(entry => entry.Ordinal).ToHashSet(), includeEmptySections: true);

        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IndexedEntry entry in this.entries)
        {
            foreach (string term in entry.Terms)
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
        }
        this.inverseDocumentFrequency = documentFrequency.ToDictionary(
            pair => pair.Key,
            pair => 1 + Math.Log((this.entries.Length + 1d) / (pair.Value + 1d)),
            StringComparer.Ordinal);
    }

    public WorldRetrievalResult Retrieve(
        WorldRetrievalQuery? query,
        int entryBudget = DefaultEntryBudget,
        int characterBudget = DefaultCharacterBudget)
    {
        if (this.invalidReason.Length > 0)
            return this.FullResult(this.invalidReason);
        if (this.entries.Length == 0)
            return this.FullResult("NoWorldEntries");
        if (query == null || IsMalformedText(query.PlayerText))
            return this.FullResult("InvalidQuery");

        string playerText = query.PlayerText ?? string.Empty;
        string recent = query.RecentDialogue ?? string.Empty;
        if (recent.Length > 1200)
            recent = recent[^1200..];
        IReadOnlySet<string> queryTerms = PrimaryTerms(playerText);
        IReadOnlySet<string> recentTerms = WorldEntryAliases.QueryTerms(recent, 64);
        var nearby = new HashSet<string>(
            (query.NearbyNpcNames ?? Array.Empty<string>()).Take(16).Select(SveContentRules.NormalizeName),
            StringComparer.OrdinalIgnoreCase);
        var ranked = this.entries.Select(entry => new RankedEntry(
            entry,
            MatchesPhrase(entry, playerText),
            IsPinned(entry, query),
            this.Score(entry, queryTerms),
            this.Score(entry, recentTerms) + (MatchesPhrase(entry, recent) ? 3 : 0),
            entry.Section == "Villagers" && entry.Aliases.Any(nearby.Contains))).ToArray();
        HashSet<string> broadSections = FindBroadSections(playerText, ranked.Any(item => item.Explicit));

        var selected = ranked.Where(item => item.Explicit || item.Pinned || broadSections.Contains(item.Entry.Section))
            .Select(item => item.Entry.Ordinal).ToHashSet();
        if (broadSections.Count > 0)
            return this.Result(selected, broadSections.Count == SearchSections.Length ? "BroadWorldQuery" : "BroadSectionQuery");

        AddRegionPins(selected, ranked, query);
        int characters = this.RenderSelection(selected).Length;
        bool hasPrimaryMatches = ranked.Any(item => item.Explicit || item.QueryScore > 0);
        bool continuing = !hasPrimaryMatches && IsContinuation(playerText);
        IEnumerable<RankedEntry> candidates = hasPrimaryMatches
            ? ranked.Where(item => item.QueryScore > 0)
                .OrderByDescending(item => item.QueryScore)
                .ThenByDescending(item => item.RecentScore)
                .ThenByDescending(item => item.Nearby)
                .ThenBy(item => item.Entry.Ordinal)
            : continuing
                ? ranked.Where(item => item.RecentScore > 0)
                    .OrderByDescending(item => item.RecentScore)
                    .ThenBy(item => item.Entry.Ordinal).Take(2)
                : Array.Empty<RankedEntry>();

        foreach (RankedEntry candidate in candidates)
        {
            if (selected.Contains(candidate.Entry.Ordinal))
                continue;
            if (selected.Count >= Math.Max(1, entryBudget))
                break;

            int addedCharacters = candidate.Entry.Text.Length + candidate.Entry.Region.Length + 64;
            if (characters + addedCharacters > Math.Max(1, characterBudget) && selected.Count > 0)
                continue;

            selected.Add(candidate.Entry.Ordinal);
            characters += addedCharacters;
        }

        bool hasRecentMatches = continuing && ranked.Any(item => item.RecentScore > 0);
        return this.Result(selected, hasPrimaryMatches || hasRecentMatches ? string.Empty : "NoRelevantEntries");
    }

    private WorldRetrievalResult Result(HashSet<int> selected, string reason) => new()
    {
        CoreText = this.coreText,
        RetrievedText = this.RenderSelection(selected),
        SelectedEntryCount = selected.Count,
        TotalEntryCount = this.entries.Length,
        FallbackReason = reason
    };

    private WorldRetrievalResult FullResult(string reason) => new()
    {
        CoreText = this.coreText,
        RetrievedText = this.fullText,
        SelectedEntryCount = this.entries.Length,
        TotalEntryCount = this.entries.Length,
        FallbackReason = reason
    };

    private string RenderSelection(HashSet<int> selected, bool includeEmptySections = false)
    {
        var builder = new StringBuilder();
        foreach (IndexedSection section in this.sections)
        {
            var selectedEntries = section.Entries.Where(entry => selected.Contains(entry.Ordinal))
                .Select(entry => (entry.Region, entry.Text)).ToArray();
            if (selectedEntries.Length == 0 && !includeEmptySections)
                continue;
            builder.Append(WorldSummaryRenderer.RenderCapturedSection(section.Name, section.ShowHeading, section.Text, selectedEntries));
        }
        return builder.Length == 0 ? string.Empty : builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private double Score(IndexedEntry entry, IReadOnlySet<string> queryTerms)
    {
        double score = 0;
        foreach (string term in queryTerms)
        {
            if (entry.Terms.Contains(term))
                score += this.inverseDocumentFrequency.GetValueOrDefault(term);
        }
        return score / (1 + Math.Min(entry.Terms.Count, 256) * 0.01);
    }

    private static bool IsPinned(IndexedEntry entry, WorldRetrievalQuery query) => entry.Section switch
    {
        "Villagers" => MatchesIdentity(entry, SveContentRules.NormalizeName(query.NpcName)) || MatchesIdentity(entry, query.NpcDisplayName),
        "Locations" => MatchesIdentity(entry, query.LocationName) || MatchesIdentity(entry, query.LocationDisplayName)
            || MatchesPhrase(entry, query.CurrentDestination),
        "Seasons" => MatchesIdentity(entry, query.Season),
        "Festivals" => MatchesIdentity(entry, query.FestivalName),
        _ => false
    };

    private static bool MatchesIdentity(IndexedEntry entry, string? value)
        => !string.IsNullOrWhiteSpace(value) && entry.Aliases.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool MatchesPhrase(IndexedEntry entry, string? text)
        => !string.IsNullOrWhiteSpace(text) && entry.Aliases.Any(alias => LocalTextSearch.ContainsPhrase(text, alias));

    private static void AddRegionPins(HashSet<int> selected, RankedEntry[] ranked, WorldRetrievalQuery query)
    {
        RankedEntry[] locations = ranked.Where(item => item.Entry.Section == "Locations").ToArray();
        bool hasCurrentEntry = locations.Any(item => MatchesIdentity(item.Entry, query.LocationName) || MatchesIdentity(item.Entry, query.LocationDisplayName));
        bool hasDestinationEntry = locations.Any(item => MatchesPhrase(item.Entry, query.CurrentDestination));
        var regions = locations.Where(item => item.Entry.Region.Length > 0)
            .GroupBy(item => item.Entry.Region, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                string[] aliases = group.First().Entry.RegionAliases;
                return new
                {
                    Entries = group.ToArray(),
                    Mentioned = aliases.Any(alias => LocalTextSearch.ContainsPhrase(query.PlayerText, alias)),
                    Destination = !hasDestinationEntry && aliases.Any(alias => LocalTextSearch.ContainsPhrase(query.CurrentDestination, alias)),
                    Current = !hasCurrentEntry && aliases.Any(alias =>
                        string.Equals(alias, query.LocationName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(alias, query.LocationDisplayName, StringComparison.OrdinalIgnoreCase))
                };
            })
            .Where(region => region.Mentioned || region.Destination || region.Current)
            .OrderByDescending(region => region.Mentioned)
            .ThenByDescending(region => region.Destination);

        // Town and Beach are regions in the shipped assets, not standalone entries. Keep at most
        // two representative complete notes per such region, four added notes overall. Explicit
        // entry mentions above are never removed or counted against this supplemental limit.
        int added = 0;
        foreach (var region in regions)
        {
            int retainedInRegion = region.Entries.Count(item => selected.Contains(item.Entry.Ordinal));
            foreach (RankedEntry item in region.Entries.OrderByDescending(item => item.QueryScore)
                         .ThenByDescending(item => item.RecentScore).ThenBy(item => item.Entry.Ordinal))
            {
                if (retainedInRegion >= 2 || added >= 4)
                    break;
                if (selected.Add(item.Entry.Ordinal))
                {
                    retainedInRegion++;
                    added++;
                }
            }
        }
    }

    private static bool IsBlockedEntry(string section, string key, WorldSummaryEntry entry, string renderedText)
    {
        if (RsvAiPolicy.IsBlockedContentId(key) || RsvAiPolicy.IsBlockedContentId(entry.Id)
            || RsvAiPolicy.ContainsBlockedReference(renderedText) || RsvAiPolicy.ContainsBlockedReference(entry.Region))
            return true;
        return section switch
        {
            "Villagers" => RsvAiPolicy.IsBlockedNpcName(key) || RsvAiPolicy.IsBlockedNpcName(entry.Id) || RsvAiPolicy.IsBlockedNpcName(entry.Name),
            "Locations" => RsvAiPolicy.IsBlockedLocationName(key) || RsvAiPolicy.IsBlockedLocationName(entry.Id) || RsvAiPolicy.IsBlockedLocationName(entry.Name),
            _ => false
        };
    }

    private static bool IsMalformedText(string? text)
    {
        if (text == null || text.Length > MaxQueryCharacters)
            return true;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                return true;
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= text.Length || !char.IsLowSurrogate(text[++index]))
                    return true;
            }
            else if (char.IsLowSurrogate(character))
                return true;
        }
        return false;
    }

    private static bool IsContinuation(string text) => AnyPhrase(text, ContinuationPhrases);

    private static IReadOnlySet<string> PrimaryTerms(string text)
    {
        var terms = new HashSet<string>(WorldEntryAliases.QueryTerms(text), StringComparer.Ordinal);
        foreach (string phrase in ContinuationPhrases)
        {
            if (LocalTextSearch.ContainsPhrase(text, phrase))
                terms.ExceptWith(LocalTextSearch.Tokenize(phrase));
        }
        return terms;
    }

    private static HashSet<string> FindBroadSections(string text, bool hasExplicitEntry)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        bool enumeration = AnyPhrase(text, "列出", "列举", "所有", "全部", "有哪些", "都有谁", "一览",
            "list", "all", "every", "what are", "which places", "who lives");
        bool introduction = AnyPhrase(text, "介绍一下", "介绍下", "概览", "overview");
        if (!enumeration && (!introduction || hasExplicitEntry))
            return result;

        if (AnyPhrase(text, "村民", "居民", "人物", "大家", "villagers", "residents", "townspeople", "people", "characters", "who lives"))
            result.Add("Villagers");
        if (AnyPhrase(text, "地点", "地方", "商店", "店铺", "设施", "建筑", "places", "locations", "shops", "stores", "buildings"))
            result.Add("Locations");
        if (AnyPhrase(text, "节日", "节庆", "庆典", "festival", "festivals", "celebrations"))
            result.Add("Festivals");
        if (AnyPhrase(text, "季节", "四季", "seasons"))
            result.Add("Seasons");
        if (result.Count == 0 && !hasExplicitEntry
            && AnyPhrase(text, "世界", "星露谷", "小镇", "鹈鹕镇", "这里", "一切", "world", "valley", "town", "everything"))
            result.UnionWith(SearchSections);
        return result;
    }

    private static bool AnyPhrase(string text, params string[] phrases)
        => phrases.Any(phrase => LocalTextSearch.ContainsPhrase(text, phrase));

    private sealed record IndexedEntry(int Ordinal, string Section, string Region, string Text, string[] Aliases, string[] RegionAliases, IReadOnlySet<string> Terms);
    private sealed record IndexedSection(string Name, bool ShowHeading, string Text, IndexedEntry[] Entries);
    private sealed record RankedEntry(IndexedEntry Entry, bool Explicit, bool Pinned, double QueryScore, double RecentScore, bool Nearby);
}
