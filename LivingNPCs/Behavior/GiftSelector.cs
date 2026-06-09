using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class GiftSelector
{
    private static readonly Dictionary<string, string[]> ExplicitNpcTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abigail"] = ["adventurous", "curious"],
        ["Alex"] = ["active", "practical"],
        ["Caroline"] = ["comfort", "nature"],
        ["Clint"] = ["practical", "work", "mineral"],
        ["Demetrius"] = ["scholarly", "nature"],
        ["Dwarf"] = ["mineral", "adventurous", "magical"],
        ["Elliott"] = ["artistic", "flower", "refined"],
        ["Emily"] = ["artistic", "sweet", "magical"],
        ["Evelyn"] = ["comfort", "flower", "homestyle"],
        ["George"] = ["comfort", "practical", "homestyle"],
        ["Gus"] = ["food", "comfort", "refined"],
        ["Haley"] = ["flower", "artistic", "refined"],
        ["Harvey"] = ["scholarly", "practical", "refined"],
        ["Jas"] = ["youthful", "sweet"],
        ["Jodi"] = ["comfort", "food", "homestyle"],
        ["Kent"] = ["practical", "comfort"],
        ["Krobus"] = ["magical", "comfort"],
        ["Leah"] = ["artistic", "nature", "food"],
        ["Leo"] = ["nature", "fish", "adventurous"],
        ["Lewis"] = ["practical", "work", "homestyle"],
        ["Linus"] = ["nature", "forage", "practical"],
        ["Marnie"] = ["comfort", "practical", "homestyle"],
        ["Maru"] = ["scholarly", "practical", "mineral"],
        ["Pam"] = ["food", "practical", "drink"],
        ["Penny"] = ["scholarly", "comfort", "flower"],
        ["Pierre"] = ["practical", "work", "food"],
        ["Robin"] = ["practical", "work", "active"],
        ["Sam"] = ["youthful", "sweet", "active"],
        ["Sandy"] = ["flower", "sweet", "refined"],
        ["Sebastian"] = ["scholarly", "drink", "magical"],
        ["Shane"] = ["practical", "food", "drink"],
        ["Vincent"] = ["youthful", "sweet"],
        ["Willy"] = ["practical", "nature", "fish"],
        ["Wizard"] = ["magical", "scholarly", "mineral"],

        ["Andy"] = ["practical", "nature", "food"],
        ["Apples"] = ["magical", "youthful", "sweet"],
        ["Claire"] = ["practical", "drink"],
        ["Lance"] = ["adventurous", "practical"],
        ["Magnus"] = ["magical", "scholarly"],
        ["Martin"] = ["youthful", "practical"],
        ["Morgan"] = ["magical", "scholarly"],
        ["Morris"] = ["practical", "work"],
        ["Olivia"] = ["artistic", "sweet"],
        ["Scarlett"] = ["active", "comfort"],
        ["Sophia"] = ["artistic", "sweet", "comfort"],
        ["Susan"] = ["practical", "nature", "food"],
        ["Victor"] = ["scholarly", "practical"],
        ["Alesia"] = ["adventurous", "practical"],
        ["Camilla"] = ["magical", "scholarly"],
        ["Isaac"] = ["adventurous", "practical"],
        ["Jadu"] = ["magical"],
        ["Jolyne"] = ["practical"],

        ["Lenny"] = ["comfort", "practical"],
        ["Richard"] = ["comfort", "food"],
        ["Ysabelle"] = ["sweet", "artistic"],
        ["Kenneth"] = ["scholarly", "practical"],
        ["Shiro"] = ["comfort", "practical"],
        ["Yuuma"] = ["youthful", "sweet"],
        ["Naomi"] = ["comfort", "practical"],
        ["Flor"] = ["scholarly", "comfort"],
        ["Ian"] = ["practical", "nature"],
        ["June"] = ["artistic", "flower"],
        ["Jio"] = ["adventurous", "practical"],
        ["Maddie"] = ["youthful", "sweet"],
        ["Sean"] = ["active", "sweet"],
        ["Philip"] = ["scholarly", "practical"],
        ["Jeric"] = ["active", "practical"],
        ["Blair"] = ["artistic", "sweet"],
        ["Alissa"] = ["artistic", "sweet"],
        ["Corine"] = ["comfort", "food"],
        ["Daia"] = ["artistic", "flower"],
        ["Irene"] = ["comfort", "practical"],
        ["Keahi"] = ["active", "practical"],
        ["Kiarra"] = ["active", "practical"],
        ["Malaya"] = ["scholarly", "comfort"],
        ["Paula"] = ["comfort", "practical"],
        ["Zayne"] = ["adventurous", "practical"]
    };

    private readonly Random random;

    public GiftSelector(Random random)
    {
        this.random = random;
    }

    public GiftSelection Choose(
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse
    )
    {
        return this.ChooseFromPool(GiftTier.Small, npc, state, playerText, npcResponse);
    }

    public GiftSelection ChooseMeaningful(
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse
    )
    {
        return this.ChooseFromPool(GiftTier.Meaningful, npc, state, playerText, npcResponse);
    }

    public bool TryChooseRequested(
        NPC npc,
        string itemId,
        GiftTier tier,
        out GiftSelection? selection
    )
    {
        selection = null;
        string normalizedItemId = NormalizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalizedItemId))
        {
            return false;
        }

        GiftCandidate? candidate = GiftCatalog.FindAvailableCandidate(npc.Name, tier, normalizedItemId);
        if (candidate == null)
        {
            return false;
        }

        bool personalized = GiftCatalog.IsPersonalizedFor(npc.Name, tier, candidate.ItemId);
        selection = new GiftSelection(
            candidate.ItemId,
            candidate.DebugName,
            tier,
            personalized
                ? "the AI named a gift from this NPC's personalized pool"
                : "the AI named a gift from the shared pool",
            string.Empty
        );
        return true;
    }

    public bool TryChooseMentioned(
        NPC npc,
        string text,
        GiftTier tier,
        out GiftSelection? selection
    )
    {
        selection = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        GiftCandidate? candidate = GiftCatalog.GetAvailableCandidates(npc.Name, tier)
            .FirstOrDefault(candidate => GiftCatalog.TextMentionsCandidate(text, candidate));
        if (candidate == null)
        {
            return false;
        }

        bool personalized = GiftCatalog.IsPersonalizedFor(npc.Name, tier, candidate.ItemId);
        selection = new GiftSelection(
            candidate.ItemId,
            candidate.DebugName,
            tier,
            personalized
                ? "the visible dialogue named a gift from this NPC's personalized pool"
                : "the visible dialogue named a gift from the shared pool",
            string.Empty
        );
        return true;
    }

    public bool HasMeaningfulMemoryCue(
        LivingNpcState state,
        string playerText,
        string npcResponse
    )
    {
        var conversationTags = BuildConversationTags(playerText, npcResponse);
        var memoryTags = BuildMemoryTags(state);
        var playerPreferences = BuildPlayerPreferenceSignals(state);
        bool matchingTags = conversationTags.Overlaps(memoryTags)
            || conversationTags.Overlaps(playerPreferences.LikedTags)
            || conversationTags.Overlaps(playerPreferences.DislikedTags);
        bool freshImportantMemory = state.LongTermMemories.Any(memory =>
            memory.Importance >= 85
            && memory.LastUpdatedTotalDays >= Game1.Date.TotalDays - 1
        );
        bool freshImportantPreference = state.PlayerPreferenceMemories.Any(memory =>
            memory.Importance >= 85
            && memory.LastUpdatedTotalDays >= Game1.Date.TotalDays - 1
        );

        return matchingTags || freshImportantMemory || freshImportantPreference;
    }

    public string BuildCommonPromptList(GiftTier tier)
    {
        return GiftCatalog.BuildPromptList(string.Empty, tier, personalized: false);
    }

    public string BuildPersonalizedPromptList(NPC npc, GiftTier tier)
    {
        return GiftCatalog.BuildPromptList(npc.Name, tier, personalized: true);
    }

    private GiftSelection ChooseFromPool(
        GiftTier tier,
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse
    )
    {
        string season = Game1.season.ToString().ToLowerInvariant();
        var disposition = NpcDisposition.For(npc);
        var npcTags = this.BuildNpcTags(npc, disposition);
        var recentGiftItemIds = new HashSet<string>(
            state.RecentAiGiftItemIds ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase
        );
        var recentCategoryTags = recentGiftItemIds
            .Select(GiftCatalog.FindCandidate)
            .Where(candidate => candidate != null)
            .SelectMany(candidate => candidate!.Tags)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var topicTags = BuildConversationTags(playerText, npcResponse);
        var memoryTags = BuildMemoryTags(state);
        var playerPreferences = BuildPlayerPreferenceSignals(state);

        IReadOnlyList<GiftCandidate> fullPool = GiftCatalog.GetAvailableCandidates(npc.Name, tier);
        var eligible = fullPool
            .Where(candidate => !playerPreferences.DislikedSubjects.Any(subject =>
                SubjectMatchesCandidate(subject, candidate)
            ))
            .ToList();
        if (eligible.Count == 0)
        {
            eligible = fullPool.ToList();
        }

        eligible = ExcludeRecentCandidates(eligible, recentGiftItemIds).ToList();

        var scored = eligible
            .Select(candidate =>
            {
                bool personalized = GiftCatalog.IsPersonalizedFor(npc.Name, tier, candidate.ItemId);
                int score = Score(
                    candidate,
                    tier,
                    personalized,
                    season,
                    npcTags,
                    recentGiftItemIds,
                    recentCategoryTags,
                    topicTags,
                    memoryTags,
                    playerPreferences,
                    state
                );
                return new ScoredGift(candidate, score, personalized, 0);
            })
            .ToList();

        int bestScore = scored.Max(candidate => candidate.Score);
        scored = scored
            .Select(candidate => candidate with
            {
                Weight = CalculateSelectionWeight(candidate.Score, bestScore, candidate.Personalized)
            })
            .ToList();

        int chosenIndex = ChooseWeightedIndex(
            scored.Select(candidate => candidate.Weight).ToList(),
            this.random.NextDouble()
        );
        ScoredGift chosen = scored[chosenIndex];
        string matchedPlayerPreference = FindMatchedPlayerPreference(chosen.Candidate, playerPreferences);
        double totalWeight = scored.Sum(candidate => candidate.Weight);
        double selectionPercent = totalWeight <= 0
            ? 0
            : chosen.Weight / totalWeight * 100;

        return new GiftSelection(
            chosen.Candidate.ItemId,
            chosen.Candidate.DebugName,
            tier,
            $"pool: {(chosen.Personalized ? "personalized" : "shared")}; profile tags: {FormatTags(npcTags)}; recent AI gifts: {FormatTags(recentGiftItemIds)}; conversation tags: {FormatTags(topicTags)}; memory tags: {FormatTags(memoryTags)}; player-liked tags: {FormatTags(playerPreferences.LikedTags)}; player-disliked tags: {FormatTags(playerPreferences.DislikedTags)}; score: {chosen.Score}; weight: {chosen.Weight:F3}; draw share: {selectionPercent:F1}%",
            matchedPlayerPreference
        );
    }

    internal static double CalculateSelectionWeight(int score, int bestScore, bool personalized)
    {
        double scoreDistance = Math.Clamp((score - bestScore) / 20.0, -5, 0);
        double relevanceWeight = Math.Pow(2, scoreDistance);
        double poolMultiplier = personalized ? 2.5 : 1;
        return Math.Max(0.03, relevanceWeight * poolMultiplier);
    }

    internal static IReadOnlyList<GiftCandidate> ExcludeRecentCandidates(
        IReadOnlyList<GiftCandidate> candidates,
        IReadOnlySet<string> recentGiftItemIds
    )
    {
        var withoutRecentItems = candidates
            .Where(candidate => !recentGiftItemIds.Contains(candidate.ItemId))
            .ToList();
        return withoutRecentItems.Count >= Math.Min(4, candidates.Count)
            ? withoutRecentItems
            : candidates;
    }

    internal static int ChooseWeightedIndex(IReadOnlyList<double> weights, double roll)
    {
        if (weights.Count == 0)
        {
            throw new ArgumentException("At least one gift weight is required.", nameof(weights));
        }

        double totalWeight = weights.Sum(weight => Math.Max(0, weight));
        if (totalWeight <= 0)
        {
            return 0;
        }

        double target = Math.Clamp(roll, 0, 0.999999999999) * totalWeight;
        double cumulative = 0;
        for (int index = 0; index < weights.Count; index++)
        {
            cumulative += Math.Max(0, weights[index]);
            if (target < cumulative)
            {
                return index;
            }
        }

        return weights.Count - 1;
    }

    private HashSet<string> BuildNpcTags(NPC npc, NpcDispositionProfile disposition)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ExplicitNpcTags.TryGetValue(npc.Name, out string[]? explicitTags))
        {
            tags.UnionWith(explicitTags);
        }

        string profile = $"{disposition.PromptLabel} {disposition.BackgroundPrompt} {disposition.DialoguePrompt}".ToLowerInvariant();
        AddTagsForText(tags, profile);
        return tags;
    }

    private static HashSet<string> BuildConversationTags(string playerText, string npcResponse)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string text = $"{playerText} {npcResponse}".ToLowerInvariant();

        if (ContainsAny(text, "咖啡", "茶", "饮料", "困", "累", "熬夜", "coffee", "tea", "drink", "tired", "sleepy"))
        {
            tags.Add("drink");
            tags.Add("practical");
        }

        if (ContainsAny(text, "面包", "早餐", "饿", "吃点", "做饭", "料理", "bread", "breakfast", "hungry", "cook", "meal"))
        {
            tags.Add("food");
            tags.Add("practical");
        }

        if (ContainsAny(text, "甜", "点心", "饼干", "蛋糕", "cookie", "dessert", "sweet", "cake"))
        {
            tags.Add("sweet");
            tags.Add("comfort");
        }

        if (ContainsAny(text, "花", "花园", "漂亮", "香", "flower", "garden"))
        {
            tags.Add("flower");
            tags.Add("artistic");
        }

        if (ContainsAny(text, "采集", "森林", "野外", "山上", "forage", "forest", "nature"))
        {
            tags.Add("forage");
            tags.Add("nature");
        }

        if (ContainsAny(text, "鱼", "钓鱼", "海", "河", "fish", "fishing", "ocean", "river"))
        {
            tags.Add("fish");
            tags.Add("nature");
        }

        if (ContainsAny(text, "冒险", "矿洞", "战斗", "探险", "adventure", "mine", "combat"))
        {
            tags.Add("adventurous");
            tags.Add("practical");
        }

        if (ContainsAny(text, "锻炼", "运动", "训练", "exercise", "sport", "training"))
        {
            tags.Add("active");
            tags.Add("practical");
        }

        if (ContainsAny(text, "矿石", "矿物", "宝石", "水晶", "gem", "mineral", "crystal"))
        {
            tags.Add("mineral");
            tags.Add("adventurous");
        }

        if (ContainsAny(text, "书", "图书馆", "学习", "研究", "book", "library", "study", "research"))
        {
            tags.Add("scholarly");
            tags.Add("drink");
        }

        if (ContainsAny(text, "家", "家常", "家庭", "home", "family", "homestyle"))
        {
            tags.Add("homestyle");
            tags.Add("comfort");
        }

        return tags;
    }

    private static PlayerPreferenceSignals BuildPlayerPreferenceSignals(LivingNpcState state)
    {
        var likedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dislikedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var likedSubjects = new List<string>();
        var dislikedSubjects = new List<string>();
        var giftRelevantMemories = new List<PlayerPreferenceFact>();

        foreach (var memory in state.PlayerPreferenceMemories
                     .Where(memory => memory.Importance >= 55)
                     .OrderByDescending(memory => memory.Importance)
                     .ThenByDescending(memory => memory.LastUpdatedTotalDays)
                     .Take(8))
        {
            var memoryTags = new HashSet<string>(memory.Tags, StringComparer.OrdinalIgnoreCase);
            memoryTags.UnionWith(BuildConversationTags($"{memory.Subject} {memory.Summary}", string.Empty));
            ExpandPreferenceTags(memoryTags);

            switch (memory.PreferenceKind)
            {
                case "liked_item_category":
                    likedTags.UnionWith(memoryTags);
                    AddSubject(likedSubjects, memory.Subject);
                    giftRelevantMemories.Add(memory);
                    break;

                case "disliked_item":
                    dislikedTags.UnionWith(memoryTags);
                    AddSubject(dislikedSubjects, memory.Subject);
                    giftRelevantMemories.Add(memory);
                    break;

                case "habit":
                case "value":
                case "goal":
                    likedTags.UnionWith(memoryTags);
                    giftRelevantMemories.Add(memory);
                    break;
            }
        }

        return new PlayerPreferenceSignals(
            likedTags,
            dislikedTags,
            likedSubjects,
            dislikedSubjects,
            giftRelevantMemories
        );
    }

    private static void ExpandPreferenceTags(HashSet<string> tags)
    {
        if (tags.Contains("mining"))
        {
            tags.Add("mineral");
            tags.Add("adventurous");
        }

        if (tags.Contains("fishing"))
        {
            tags.Add("fish");
            tags.Add("nature");
            tags.Add("practical");
        }

        if (tags.Contains("farming"))
        {
            tags.Add("food");
            tags.Add("nature");
            tags.Add("practical");
        }

        if (tags.Contains("morning"))
        {
            tags.Add("drink");
            tags.Add("practical");
        }

        if (tags.Contains("night"))
        {
            tags.Add("drink");
            tags.Add("comfort");
        }
    }

    private static void AddSubject(List<string> subjects, string subject)
    {
        if (!string.IsNullOrWhiteSpace(subject))
        {
            subjects.Add(subject.Trim());
        }
    }

    private static HashSet<string> BuildMemoryTags(LivingNpcState state)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var memory in state.LongTermMemories
                     .Where(memory => memory.Importance >= 70)
                     .Take(4))
        {
            AddTagsForText(tags, memory.Summary.ToLowerInvariant());
            tags.UnionWith(BuildConversationTags(memory.Summary, string.Empty));
        }

        return tags;
    }

    private static void AddTagsForText(HashSet<string> tags, string text)
    {
        if (ContainsAny(text, "warm", "caring", "motherly", "family", "gentle", "community"))
        {
            tags.Add("comfort");
            tags.Add("homestyle");
        }

        if (ContainsAny(text, "practical", "work", "farm", "farmer", "rural", "busy"))
        {
            tags.Add("practical");
        }

        if (ContainsAny(text, "curious", "scholarly", "educated", "student", "technical", "engineering"))
        {
            tags.Add("scholarly");
        }

        if (ContainsAny(text, "artistic", "expressive", "stylish", "performer", "creative"))
        {
            tags.Add("artistic");
        }

        if (ContainsAny(text, "elegant", "wealthy", "refined", "polished"))
        {
            tags.Add("refined");
        }

        if (ContainsAny(text, "adventurer", "fighter", "danger", "brave", "battle"))
        {
            tags.Add("adventurous");
        }

        if (ContainsAny(text, "magical", "arcane", "wizard"))
        {
            tags.Add("magical");
        }

        if (ContainsAny(text, "young", "childlike", "youthful"))
        {
            tags.Add("youthful");
        }

        if (ContainsAny(text, "nature", "outdoorsy"))
        {
            tags.Add("nature");
        }
    }

    private static int Score(
        GiftCandidate candidate,
        GiftTier tier,
        bool personalized,
        string season,
        IReadOnlySet<string> npcTags,
        IReadOnlySet<string> recentGiftItemIds,
        IReadOnlySet<string> recentCategoryTags,
        IReadOnlySet<string> topicTags,
        IReadOnlySet<string> memoryTags,
        PlayerPreferenceSignals playerPreferences,
        LivingNpcState state
    )
    {
        int score = 20;
        if (personalized)
        {
            score += 12;
        }

        if (recentGiftItemIds.Contains(candidate.ItemId))
        {
            score -= 40;
        }

        score -= candidate.Tags.Count(tag => recentCategoryTags.Contains(tag)) * 2;
        score += candidate.Tags.Count(tag => npcTags.Contains(tag)) * 4;
        score += candidate.Tags.Count(tag => topicTags.Contains(tag)) * 7;
        score += candidate.Tags.Count(tag => memoryTags.Contains(tag)) * 5;
        score += candidate.Tags.Count(tag => playerPreferences.LikedTags.Contains(tag)) * 8;
        score -= candidate.Tags.Count(tag => playerPreferences.DislikedTags.Contains(tag)) * 12;

        if (playerPreferences.LikedSubjects.Any(subject => SubjectMatchesCandidate(subject, candidate)))
        {
            score += 16;
        }

        if (playerPreferences.DislikedSubjects.Any(subject => SubjectMatchesCandidate(subject, candidate)))
        {
            score -= 100;
        }

        if (!string.IsNullOrWhiteSpace(candidate.Season) && candidate.Season == season)
        {
            score += 5;
        }

        if (state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate"
            && candidate.Tags.Any(tag => tag is "comfort" or "sweet" or "flower"))
        {
            score += 3;
        }

        if (state.InteractionComfortTier == "Familiar"
            && candidate.Tags.Any(tag => tag is "comfort" or "sweet"))
        {
            score -= 1;
        }

        if (tier == GiftTier.Meaningful && candidate.Tags.Contains("special"))
        {
            score += state.InteractionComfortTier is "Trusted" or "Intimate" ? 5 : 2;
        }

        return score;
    }

    private static string FindMatchedPlayerPreference(
        GiftCandidate candidate,
        PlayerPreferenceSignals playerPreferences
    )
    {
        var match = playerPreferences.GiftRelevantMemories.FirstOrDefault(memory =>
            memory.PreferenceKind != "disliked_item"
            && (
                memory.Tags.Any(tag => candidate.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                || SubjectMatchesCandidate(memory.Subject, candidate)
            )
        );
        return match?.Summary ?? string.Empty;
    }

    private static bool SubjectMatchesCandidate(string subject, GiftCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        string normalizedSubject = subject.Trim().ToLowerInvariant();
        string normalizedEnglishName = candidate.DebugName.Trim().ToLowerInvariant();
        string normalizedChineseName = candidate.ChineseName.Trim().ToLowerInvariant();
        return normalizedSubject.Contains(normalizedEnglishName, StringComparison.OrdinalIgnoreCase)
            || normalizedEnglishName.Contains(normalizedSubject, StringComparison.OrdinalIgnoreCase)
            || normalizedSubject.Contains(normalizedChineseName, StringComparison.OrdinalIgnoreCase)
            || normalizedChineseName.Contains(normalizedSubject, StringComparison.OrdinalIgnoreCase)
            || candidate.Aliases?.Any(alias =>
                normalizedSubject.Contains(alias, StringComparison.OrdinalIgnoreCase)
                || alias.Contains(normalizedSubject, StringComparison.OrdinalIgnoreCase)
            ) == true;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeItemId(string itemId)
    {
        string trimmed = itemId.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return int.TryParse(trimmed, out int rawId)
            ? $"(O){rawId}"
            : trimmed;
    }

    private static string FormatTags(IEnumerable<string> tags)
    {
        string[] materialized = tags
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return materialized.Length == 0
            ? "none"
            : string.Join(", ", materialized);
    }
}

internal sealed record ScoredGift(
    GiftCandidate Candidate,
    int Score,
    bool Personalized,
    double Weight
);

internal sealed record GiftSelection(
    string ItemId,
    string DebugName,
    GiftTier Tier,
    string Reason,
    string MatchedPlayerPreference
);

internal sealed record PlayerPreferenceSignals(
    IReadOnlySet<string> LikedTags,
    IReadOnlySet<string> DislikedTags,
    IReadOnlyList<string> LikedSubjects,
    IReadOnlyList<string> DislikedSubjects,
    IReadOnlyList<PlayerPreferenceFact> GiftRelevantMemories
);
