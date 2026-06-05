using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class GiftSelector
{
    private static readonly IReadOnlyList<GiftCandidate> SmallCandidates =
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

    private static readonly IReadOnlyList<GiftCandidate> MeaningfulCandidates =
    [
        new("(O)66", "Amethyst", ["mineral", "adventurous", "magical", "artistic"]),
        new("(O)72", "Diamond", ["mineral", "artistic", "refined", "special"]),
        new("(O)80", "Quartz", ["mineral", "scholarly", "magical"]),
        new("(O)82", "Fire Quartz", ["mineral", "adventurous", "magical"]),
        new("(O)84", "Frozen Tear", ["mineral", "comfort", "magical", "special"]),
        new("(O)86", "Earth Crystal", ["mineral", "nature", "magical"]),
        new("(O)220", "Chocolate Cake", ["comfort", "sweet", "special"]),
        new("(O)221", "Pink Cake", ["comfort", "sweet", "flower", "special"]),
        new("(O)234", "Blueberry Tart", ["food", "sweet", "comfort"]),
        new("(O)240", "Farmer's Lunch", ["food", "practical", "special"]),
        new("(O)421", "Sunflower", ["flower", "artistic", "special"], "summer"),
        new("(O)591", "Tulip", ["flower", "artistic"], "spring"),
        new("(O)593", "Summer Spangle", ["flower", "artistic"], "summer"),
        new("(O)595", "Fairy Rose", ["flower", "artistic", "magical", "special"], "fall"),
        new("(O)597", "Blue Jazz", ["flower", "artistic"], "spring"),
        new("(O)608", "Pumpkin Pie", ["food", "comfort", "special"], "fall")
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> GiftMentionAliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["(O)16"] = ["wild horseradish", "horseradish", "野山葵", "辣根"],
            ["(O)18"] = ["daffodil", "黄水仙", "水仙"],
            ["(O)20"] = ["leek", "韭葱"],
            ["(O)22"] = ["dandelion", "蒲公英"],
            ["(O)66"] = ["amethyst", "紫水晶"],
            ["(O)72"] = ["diamond", "钻石"],
            ["(O)80"] = ["quartz", "石英"],
            ["(O)82"] = ["fire quartz", "火水晶"],
            ["(O)84"] = ["frozen tear", "冰封泪晶", "冰泪"],
            ["(O)86"] = ["earth crystal", "地晶"],
            ["(O)216"] = ["bread", "面包"],
            ["(O)220"] = ["chocolate cake", "巧克力蛋糕"],
            ["(O)221"] = ["pink cake", "粉红蛋糕"],
            ["(O)223"] = ["cookie", "cookies", "饼干"],
            ["(O)234"] = ["blueberry tart", "蓝莓千层酥", "蓝莓挞"],
            ["(O)240"] = ["farmer's lunch", "farmers lunch", "农夫午餐"],
            ["(O)395"] = ["coffee", "咖啡"],
            ["(O)396"] = ["spice berry", "香料浆果", "香味浆果"],
            ["(O)398"] = ["grape", "grapes", "葡萄"],
            ["(O)402"] = ["sweet pea", "甜豌豆"],
            ["(O)404"] = ["common mushroom", "mushroom", "普通蘑菇", "蘑菇"],
            ["(O)406"] = ["wild plum", "plum", "野梅"],
            ["(O)408"] = ["hazelnut", "榛子"],
            ["(O)410"] = ["blackberry", "blackberries", "黑莓"],
            ["(O)412"] = ["winter root", "冬根"],
            ["(O)414"] = ["crystal fruit", "水晶果"],
            ["(O)416"] = ["snow yam", "雪山药"],
            ["(O)418"] = ["crocus", "番红花"],
            ["(O)421"] = ["sunflower", "向日葵"],
            ["(O)591"] = ["tulip", "郁金香"],
            ["(O)593"] = ["summer spangle", "夏季亮片"],
            ["(O)595"] = ["fairy rose", "玫瑰仙子", "仙女玫瑰"],
            ["(O)597"] = ["blue jazz", "蓝爵"],
            ["(O)608"] = ["pumpkin pie", "南瓜派"]
        };

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

    private static readonly Dictionary<string, string[]> ExplicitNpcPreferredItemIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abigail"] = ["(O)66", "(O)80", "(O)82", "(O)223"],
        ["Alex"] = ["(O)216", "(O)240", "(O)395"],
        ["Caroline"] = ["(O)18", "(O)22", "(O)402", "(O)597"],
        ["Clint"] = ["(O)86", "(O)82", "(O)80", "(O)395"],
        ["Demetrius"] = ["(O)404", "(O)414", "(O)395"],
        ["Elliott"] = ["(O)395", "(O)402", "(O)591", "(O)597"],
        ["Emily"] = ["(O)66", "(O)72", "(O)595", "(O)402"],
        ["Evelyn"] = ["(O)591", "(O)595", "(O)223", "(O)221"],
        ["Gus"] = ["(O)216", "(O)220", "(O)223", "(O)395"],
        ["Haley"] = ["(O)421", "(O)221", "(O)402", "(O)591"],
        ["Harvey"] = ["(O)395", "(O)216", "(O)597"],
        ["Jas"] = ["(O)223", "(O)221", "(O)398", "(O)402"],
        ["Jodi"] = ["(O)220", "(O)240", "(O)216"],
        ["Kent"] = ["(O)395", "(O)408", "(O)216"],
        ["Leah"] = ["(O)406", "(O)404", "(O)402", "(O)595"],
        ["Linus"] = ["(O)16", "(O)20", "(O)22", "(O)404", "(O)412"],
        ["Marnie"] = ["(O)240", "(O)608", "(O)216"],
        ["Maru"] = ["(O)80", "(O)86", "(O)395"],
        ["Pam"] = ["(O)395", "(O)216", "(O)410"],
        ["Penny"] = ["(O)22", "(O)18", "(O)223", "(O)591", "(O)597"],
        ["Pierre"] = ["(O)395", "(O)216", "(O)408"],
        ["Robin"] = ["(O)395", "(O)216", "(O)20"],
        ["Sam"] = ["(O)223", "(O)395", "(O)398"],
        ["Sandy"] = ["(O)402", "(O)418", "(O)221", "(O)595"],
        ["Sebastian"] = ["(O)395", "(O)84", "(O)80"],
        ["Shane"] = ["(O)216", "(O)395", "(O)410"],
        ["Vincent"] = ["(O)223", "(O)398", "(O)221"],
        ["Willy"] = ["(O)408", "(O)412", "(O)414"],
        ["Wizard"] = ["(O)82", "(O)66", "(O)86", "(O)414"],

        ["Andy"] = ["(O)20", "(O)408", "(O)240"],
        ["Claire"] = ["(O)395", "(O)223", "(O)402"],
        ["Lance"] = ["(O)82", "(O)414", "(O)240"],
        ["Magnus"] = ["(O)82", "(O)86", "(O)414"],
        ["Morgan"] = ["(O)80", "(O)84", "(O)597"],
        ["Olivia"] = ["(O)221", "(O)220", "(O)72"],
        ["Sophia"] = ["(O)221", "(O)402", "(O)593"],
        ["Victor"] = ["(O)80", "(O)395", "(O)216"],

        ["June"] = ["(O)402", "(O)591", "(O)597"],
        ["Flor"] = ["(O)223", "(O)597", "(O)395"],
        ["Maddie"] = ["(O)223", "(O)398", "(O)221"],
        ["Blair"] = ["(O)221", "(O)402", "(O)72"],
        ["Daia"] = ["(O)421", "(O)595", "(O)402"]
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
        return this.ChooseFromPool(SmallCandidates, GiftTier.Small, npc, state, playerText, npcResponse);
    }

    public GiftSelection ChooseMeaningful(
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse
    )
    {
        return this.ChooseFromPool(MeaningfulCandidates, GiftTier.Meaningful, npc, state, playerText, npcResponse);
    }

    public bool TryChooseRequested(
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

        string season = Game1.season.ToString().ToLowerInvariant();
        IReadOnlyList<GiftCandidate> pool = tier == GiftTier.Meaningful
            ? MeaningfulCandidates
            : SmallCandidates;
        GiftCandidate? candidate = pool.FirstOrDefault(candidate =>
            candidate.ItemId.Equals(normalizedItemId, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(candidate.Season) || candidate.Season == season)
        );
        if (candidate == null)
        {
            return false;
        }

        selection = new GiftSelection(
            candidate.ItemId,
            candidate.DebugName,
            tier,
            "the AI named this gift in hidden metadata and it passed the allowed gift pool check",
            string.Empty
        );
        return true;
    }

    public bool TryChooseMentioned(
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

        string season = Game1.season.ToString().ToLowerInvariant();
        IReadOnlyList<GiftCandidate> pool = tier == GiftTier.Meaningful
            ? MeaningfulCandidates
            : SmallCandidates;
        GiftCandidate? candidate = pool.FirstOrDefault(candidate =>
            (string.IsNullOrWhiteSpace(candidate.Season) || candidate.Season == season)
            && this.TextMentionsCandidate(text, candidate)
        );
        if (candidate == null)
        {
            return false;
        }

        selection = new GiftSelection(
            candidate.ItemId,
            candidate.DebugName,
            tier,
            "the visible dialogue named this allowed gift",
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

    private GiftSelection ChooseFromPool(
        IReadOnlyList<GiftCandidate> pool,
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
        var npcPreferredItemIds = BuildNpcPreferredItemIds(npc);
        var topicTags = BuildConversationTags(playerText, npcResponse);
        var memoryTags = BuildMemoryTags(state);
        var playerPreferences = BuildPlayerPreferenceSignals(state);

        var scored = pool
            .Where(candidate => string.IsNullOrWhiteSpace(candidate.Season) || candidate.Season == season)
            .Select(candidate => new ScoredGift(candidate, Score(candidate, tier, npcTags, npcPreferredItemIds, topicTags, memoryTags, playerPreferences, state)))
            .ToList();

        int bestScore = scored.Max(candidate => candidate.Score);
        var finalists = scored
            .Where(candidate => candidate.Score >= bestScore - 2)
            .ToList();
        var chosen = finalists[this.random.Next(finalists.Count)];
        string matchedPlayerPreference = FindMatchedPlayerPreference(chosen.Candidate, playerPreferences);

        return new GiftSelection(
            chosen.Candidate.ItemId,
            chosen.Candidate.DebugName,
            tier,
            $"profile tags: {FormatTags(npcTags)}; preferred item ids: {FormatTags(npcPreferredItemIds)}; chosen preferred: {npcPreferredItemIds.Contains(chosen.Candidate.ItemId)}; conversation tags: {FormatTags(topicTags)}; memory tags: {FormatTags(memoryTags)}; player-liked tags: {FormatTags(playerPreferences.LikedTags)}; player-disliked tags: {FormatTags(playerPreferences.DislikedTags)}; score: {chosen.Score}",
            matchedPlayerPreference
        );
    }

    private static HashSet<string> BuildNpcPreferredItemIds(NPC npc)
    {
        return ExplicitNpcPreferredItemIds.TryGetValue(npc.Name, out string[]? itemIds)
            ? new HashSet<string>(itemIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            tags.Add("nature");
            tags.Add("practical");
        }

        if (tags.Contains("farming"))
        {
            tags.Add("food");
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
        IReadOnlySet<string> npcTags,
        IReadOnlySet<string> npcPreferredItemIds,
        IReadOnlySet<string> topicTags,
        IReadOnlySet<string> memoryTags,
        PlayerPreferenceSignals playerPreferences,
        LivingNpcState state
    )
    {
        int score = 10;
        if (npcPreferredItemIds.Contains(candidate.ItemId))
        {
            score += 18;
        }

        score += candidate.Tags.Count(tag => npcTags.Contains(tag)) * 5;
        score += candidate.Tags.Count(tag => topicTags.Contains(tag)) * 8;
        score += candidate.Tags.Count(tag => memoryTags.Contains(tag)) * 7;
        score += candidate.Tags.Count(tag => playerPreferences.LikedTags.Contains(tag)) * 12;
        score -= candidate.Tags.Count(tag => playerPreferences.DislikedTags.Contains(tag)) * 18;

        if (playerPreferences.LikedSubjects.Any(subject => SubjectMatchesCandidate(subject, candidate)))
        {
            score += 18;
        }

        if (playerPreferences.DislikedSubjects.Any(subject => SubjectMatchesCandidate(subject, candidate)))
        {
            score -= 30;
        }

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

        if (tier == GiftTier.Meaningful
            && candidate.Tags.Contains("special"))
        {
            score += state.InteractionComfortTier is "Trusted" or "Intimate" ? 5 : 2;
        }

        return score;
    }

    private static string FindMatchedPlayerPreference(GiftCandidate candidate, PlayerPreferenceSignals playerPreferences)
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
        string normalizedCandidate = candidate.DebugName.Trim().ToLowerInvariant();
        return normalizedSubject.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.Contains(normalizedSubject, StringComparison.OrdinalIgnoreCase);
    }

    private bool TextMentionsCandidate(string text, GiftCandidate candidate)
    {
        if (text.Contains(candidate.DebugName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GiftMentionAliases.TryGetValue(candidate.ItemId, out IReadOnlyList<string>? aliases)
            && aliases.Any(alias => text.Contains(alias, StringComparison.OrdinalIgnoreCase));
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

internal enum GiftTier
{
    Small,
    Meaningful
}
