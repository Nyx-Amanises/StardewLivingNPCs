using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class GiftSelector
{
    private static readonly IReadOnlyList<GiftCandidate> Candidates =
    [
        new("(O)216", "Bread", ["food", "comfort", "practical"]),
        new("(O)223", "Cookie", ["food", "comfort", "sweet", "youthful"]),
        new("(O)395", "Coffee", ["drink", "practical", "scholarly", "work"]),

        new("(O)16", "Wild Horseradish", ["nature", "forage", "practical"], "spring"),
        new("(O)18", "Daffodil", ["nature", "forage", "flower", "artistic"], "spring"),
        new("(O)20", "Leek", ["nature", "forage", "food", "practical"], "spring"),
        new("(O)22", "Dandelion", ["nature", "forage", "flower"], "spring"),

        new("(O)396", "Spice Berry", ["nature", "forage", "food", "sweet"], "summer"),
        new("(O)398", "Grape", ["nature", "forage", "food", "sweet"], "summer"),
        new("(O)402", "Sweet Pea", ["nature", "forage", "flower", "artistic"], "summer"),

        new("(O)404", "Common Mushroom", ["nature", "forage", "practical", "adventurous"], "fall"),
        new("(O)406", "Wild Plum", ["nature", "forage", "food", "sweet"], "fall"),
        new("(O)408", "Hazelnut", ["nature", "forage", "food", "practical"], "fall"),
        new("(O)410", "Blackberry", ["nature", "forage", "food", "sweet"], "fall"),

        new("(O)412", "Winter Root", ["nature", "forage", "food", "practical"], "winter"),
        new("(O)414", "Crystal Fruit", ["nature", "forage", "food", "adventurous", "magical"], "winter"),
        new("(O)416", "Snow Yam", ["nature", "forage", "food", "practical"], "winter"),
        new("(O)418", "Crocus", ["nature", "forage", "flower", "artistic"], "winter")
    ];

    private static readonly Dictionary<string, string[]> ExplicitNpcTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abigail"] = ["adventurous", "curious"],
        ["Alex"] = ["active", "practical"],
        ["Caroline"] = ["comfort", "nature"],
        ["Clint"] = ["practical", "work"],
        ["Demetrius"] = ["scholarly", "nature"],
        ["Elliott"] = ["artistic", "flower"],
        ["Emily"] = ["artistic", "sweet"],
        ["Evelyn"] = ["comfort", "flower"],
        ["Gus"] = ["food", "comfort"],
        ["Haley"] = ["flower", "artistic"],
        ["Harvey"] = ["scholarly", "practical"],
        ["Jas"] = ["youthful", "sweet"],
        ["Jodi"] = ["comfort", "food"],
        ["Kent"] = ["practical"],
        ["Leah"] = ["artistic", "nature"],
        ["Linus"] = ["nature", "forage"],
        ["Marnie"] = ["comfort", "practical"],
        ["Maru"] = ["scholarly", "practical"],
        ["Pam"] = ["food", "practical"],
        ["Penny"] = ["scholarly", "comfort"],
        ["Pierre"] = ["practical", "work"],
        ["Robin"] = ["practical", "work"],
        ["Sam"] = ["youthful", "sweet"],
        ["Sandy"] = ["flower", "sweet"],
        ["Sebastian"] = ["scholarly", "drink"],
        ["Shane"] = ["practical", "food"],
        ["Vincent"] = ["youthful", "sweet"],
        ["Willy"] = ["practical", "nature"],
        ["Wizard"] = ["magical", "scholarly"],

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
        string season = Game1.season.ToString().ToLowerInvariant();
        var disposition = NpcDisposition.For(npc);
        var npcTags = this.BuildNpcTags(npc, disposition);
        var topicTags = BuildConversationTags(playerText, npcResponse);

        var scored = Candidates
            .Where(candidate => string.IsNullOrWhiteSpace(candidate.Season) || candidate.Season == season)
            .Select(candidate => new ScoredGift(candidate, Score(candidate, npcTags, topicTags, state)))
            .ToList();

        int bestScore = scored.Max(candidate => candidate.Score);
        var finalists = scored
            .Where(candidate => candidate.Score >= bestScore - 2)
            .ToList();
        var chosen = finalists[this.random.Next(finalists.Count)];

        return new GiftSelection(
            chosen.Candidate.ItemId,
            chosen.Candidate.DebugName,
            $"profile tags: {FormatTags(npcTags)}; conversation tags: {FormatTags(topicTags)}; score: {chosen.Score}"
        );
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

        if (ContainsAny(text, "咖啡", "困", "累", "熬夜", "coffee", "tired", "sleepy"))
        {
            tags.Add("drink");
            tags.Add("practical");
        }

        if (ContainsAny(text, "面包", "早餐", "饿", "吃点", "bread", "breakfast", "hungry"))
        {
            tags.Add("food");
            tags.Add("practical");
        }

        if (ContainsAny(text, "甜", "点心", "饼干", "cookie", "dessert", "sweet"))
        {
            tags.Add("sweet");
            tags.Add("comfort");
        }

        if (ContainsAny(text, "花", "花园", "漂亮", "香", "flower", "garden"))
        {
            tags.Add("flower");
            tags.Add("artistic");
        }

        if (ContainsAny(text, "采集", "森林", "野外", "山上", "forage", "forest"))
        {
            tags.Add("forage");
            tags.Add("nature");
        }

        if (ContainsAny(text, "冒险", "矿洞", "战斗", "探险", "adventure", "mine", "combat"))
        {
            tags.Add("adventurous");
            tags.Add("practical");
        }

        if (ContainsAny(text, "书", "图书馆", "学习", "研究", "book", "library", "study", "research"))
        {
            tags.Add("scholarly");
            tags.Add("drink");
        }

        return tags;
    }

    private static void AddTagsForText(HashSet<string> tags, string text)
    {
        if (ContainsAny(text, "warm", "caring", "motherly", "family", "gentle", "community"))
        {
            tags.Add("comfort");
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
        IReadOnlySet<string> npcTags,
        IReadOnlySet<string> topicTags,
        LivingNpcState state
    )
    {
        int score = 10;
        score += candidate.Tags.Count(tag => npcTags.Contains(tag)) * 5;
        score += candidate.Tags.Count(tag => topicTags.Contains(tag)) * 8;

        if (!string.IsNullOrWhiteSpace(candidate.Season))
        {
            score += 6;
        }

        if (state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate"
            && candidate.Tags.Any(tag => tag is "comfort" or "sweet" or "flower"))
        {
            score += 4;
        }

        if (state.InteractionComfortTier == "Familiar"
            && candidate.Tags.Any(tag => tag is "comfort" or "sweet"))
        {
            score -= 2;
        }

        return score;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        return needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
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

internal sealed record GiftCandidate(
    string ItemId,
    string DebugName,
    IReadOnlyList<string> Tags,
    string Season = ""
);

internal sealed record ScoredGift(
    GiftCandidate Candidate,
    int Score
);

internal sealed record GiftSelection(
    string ItemId,
    string DebugName,
    string Reason
);
