using System.Text.Json;
using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class MemoryRecallTests
{
    [Fact]
    public void LocationChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer loved quiet fishing at the beach.",
            importance: 52,
            tags: ["fishing", "nature"]));

        var plan = Recall(state, TestScenarios.World("Beach", "Beach"));

        Assert.Contains("beach", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeOfDayChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer promised to check in during a late night festival.",
            importance: 52,
            kind: "promise",
            tags: "night"));

        var plan = Recall(state, TestScenarios.World(timeOfDay: 1900));

        Assert.Contains("night", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LastGiftChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.LastGiftName = "Coffee";
        state.LastGiftTaste = "liked";
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer once brought coffee after a tiring morning.",
            importance: 50,
            tags: ["drink", "comfort"]));

        var plan = Recall(state, TestScenarios.World());

        Assert.Contains("coffee", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingHelpRequestChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Type = "item_request",
            Summary = "Bring quartz for the library display.",
            Status = "Pending",
            DueTotalDays = TestScenarios.Today + 1
        });
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer promised to bring quartz for the library display.",
            importance: 48,
            kind: "promise",
            subject: "library quartz",
            tags: ["scholarly", "mineral"]));

        var plan = Recall(state, TestScenarios.World("ArchaeologyHouse", "Library"));

        Assert.Contains("quartz", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Torts Aurorean Iris coffee")]
    public void RsvLongTermAndPreferenceMemoriesAreExcludedBeforeRecall(string? currentPlayerText)
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory(
            "Met Torts near the ridge.",
            importance: 100,
            subject: "Torts",
            tags: ["RSV"]));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer enjoys coffee in the library.",
            importance: 80,
            tags: "coffee"));
        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact
        {
            PreferenceKind = "liked_item_category",
            Subject = "Rafseazz.RSVCP_Aurorean_Iris",
            Summary = "The farmer likes the Aurorean Iris.",
            Tags = new List<string> { "RSV" },
            Importance = 100,
            CreatedTotalDays = TestScenarios.Today - 1,
            LastUpdatedTotalDays = TestScenarios.Today - 1
        });

        MemoryRecallPlan plan = new BehaviorMemory().BuildMemoryRecallPlanForTesting(
            state,
            TestScenarios.World(),
            Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 5,
            preferenceCount: 5,
            currentTotalDays: TestScenarios.Today,
            currentPlayerText: currentPlayerText);

        Assert.Single(plan.LongTermMemories);
        Assert.Contains("coffee", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(plan.PlayerPreferences);
    }

    [Fact]
    public void CurrentTopicOutranksAMoreSalientMemoryFromTheOldScene()
    {
        var state = TestScenarios.TrustedState();
        state.LastGiftName = "Coffee";
        state.LastInteraction = "Fishing at the beach after work, with coffee and a quiet morning.";
        state.LastEventContext = "Fishing at the beach after work, with coffee and a quiet morning.";
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Type = "item_request",
            Summary = "Bring coffee to the beach after fishing.",
            Status = "Pending",
            DueTotalDays = TestScenarios.Today + 1
        });
        var oldTopic = TestScenarios.Memory(
            "Fishing at the beach after work, with coffee and a quiet morning.",
            importance: 100,
            kind: "relationship",
            subject: "fishing beach coffee",
            tags: ["fishing", "nature", "drink", "comfort", "work", "morning"]);
        var currentTopic = TestScenarios.Memory(
            "The quartz display in the library was arranged together.",
            importance: 25,
            subject: "quartz",
            tags: ["scholarly", "mineral"]);
        state.LongTermMemories.AddRange([oldTopic, currentTopic]);
        var world = TestScenarios.World("Beach", "Beach", promptLabel: state.LastInteraction);

        Assert.Same(oldTopic, Recall(state, world).LongTermMemories[0].Memory);

        var plan = Recall(state, world, "What happened to the quartz display?");

        Assert.Same(currentTopic, plan.LongTermMemories[0].Memory);
        Assert.Contains("current topic", plan.LongTermMemories[0].Reason);
    }

    [Theory]
    [InlineData("农夫说累的时候最爱喝咖啡。", "咖啡我还是很喜欢，忙完再喝。")]
    [InlineData("玩家把石英收藏摆在书架上。", "石英的那份收藏能再讲讲吗？")]
    public void ChineseTopicsMatchAcrossDifferentSentenceStructures(string summary, string query)
    {
        var state = TestScenarios.TrustedState();
        var relevant = TestScenarios.Memory(summary, importance: 25);
        state.LongTermMemories.Add(TestScenarios.Memory("We spent an afternoon fishing at the beach.", importance: 100));
        state.LongTermMemories.Add(relevant);

        var plan = Recall(state, TestScenarios.World("Beach", "海滩"), query);

        Assert.Same(relevant, plan.LongTermMemories[0].Memory);
    }

    [Fact]
    public void CurrentTopicAlsoChangesPlayerPreferenceRanking()
    {
        var state = TestScenarios.TrustedState();
        var fishing = Preference("fishing", "The farmer likes fishing at the beach.", importance: 95);
        var coffee = Preference("咖啡", "忙完以后，玩家喜欢坐下来喝咖啡。", importance: 25);
        state.PlayerPreferenceMemories.AddRange([fishing, coffee]);
        var world = TestScenarios.World("Beach", "Beach");

        Assert.Same(fishing, Recall(state, world, preferenceCount: 1).PlayerPreferences[0].Memory);

        var plan = Recall(state, world, "咖啡还是和从前一样合我口味。", preferenceCount: 1);

        Assert.Same(coffee, Assert.Single(plan.PlayerPreferences).Memory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n")]
    [InlineData("the and could you please")]
    [InlineData("unmatched_topic")]
    public void EmptyOrUnmatchedQueriesKeepExistingSceneRanking(string? query)
    {
        var state = TestScenarios.TrustedState();
        state.LastGiftName = "Coffee";
        state.LongTermMemories.Add(TestScenarios.Memory("A morning coffee break.", importance: 50));
        state.LongTermMemories.Add(TestScenarios.Memory("A late night promise.", importance: 65, kind: "promise"));
        state.PlayerPreferenceMemories.Add(Preference("coffee", "The farmer enjoys coffee.", importance: 50));
        state.PlayerPreferenceMemories.Add(Preference("night", "The farmer prefers quiet nights.", importance: 70));
        var world = TestScenarios.World(timeOfDay: 1900);
        var baseline = Recall(state, world, preferenceCount: 2);

        var plan = Recall(state, world, query, preferenceCount: 2);

        Assert.Equal(baseline.LongTermMemories, plan.LongTermMemories);
        Assert.Equal(baseline.PlayerPreferences, plan.PlayerPreferences);
        Assert.True(baseline.Context.Tags.SetEquals(plan.Context.Tags));
        Assert.True(baseline.Context.Tokens.SetEquals(plan.Context.Tokens));
    }

    [Fact]
    public void ShortNpcNamesDoNotMatchInsideOtherEnglishWords()
    {
        var state = TestScenarios.TrustedState();
        var unrelated = TestScenarios.Memory("A sample painting was discussed.", importance: 100, subject: "sample");
        var sam = TestScenarios.Memory("Sam brought his guitar.", importance: 20, subject: "Sam");
        state.LongTermMemories.AddRange([unrelated, sam]);

        var plan = Recall(state, TestScenarios.World(), "Sam?");

        Assert.Same(sam, plan.LongTermMemories[0].Memory);
        Assert.DoesNotContain("current topic", plan.LongTermMemories.Single(item => item.Memory == unrelated).Reason);
    }

    [Fact]
    public void RepeatedQueryWordsDoNotIncreaseRecallScores()
    {
        var state = TestScenarios.TrustedState();
        var memory = TestScenarios.Memory("Quartz on the library shelf.", importance: 70, subject: "quartz");
        memory.TimesReinforced = int.MaxValue;
        state.LongTermMemories.Add(memory);
        var baseline = Recall(state, TestScenarios.World(), "quartz");

        var repeated = Recall(state, TestScenarios.World(), string.Concat(Enumerable.Repeat("quartz ", 2000)));

        Assert.Equal(Assert.Single(baseline.LongTermMemories).Score, Assert.Single(repeated.LongTermMemories).Score);
        Assert.InRange(repeated.LongTermMemories[0].Score, 45, 512);
    }

    [Fact]
    public void TopicNearTheEndOfANativeInputTurnStillParticipatesInRecall()
    {
        var state = TestScenarios.TrustedState();
        var coffee = TestScenarios.Memory("Coffee by the fireplace.", importance: 25, subject: "coffee");
        state.LongTermMemories.Add(TestScenarios.Memory("Fishing at the beach.", importance: 100));
        state.LongTermMemories.Add(coffee);
        // Many distinct CJK fragments before the topic, still within the input box's 500-character limit.
        string query = string.Concat(Enumerable.Range(0, 350).Select(index => (char)('\u4E00' + index))) + " coffee";

        var plan = Recall(state, TestScenarios.World("Beach", "Beach"), query);

        Assert.Same(coffee, plan.LongTermMemories[0].Memory);
    }

    [Fact]
    public void QueryIsTransientAndRecallCountersChangeOnlyWhenMarked()
    {
        var state = TestScenarios.TrustedState();
        var memory = LongTermMemoryStore.NormalizeForStore(TestScenarios.Memory("Quartz on a shelf.", subject: "quartz"));
        var preference = PlayerPreferenceMemoryStore.NormalizeForStore(Preference("quartz", "The farmer collects quartz.", 55));
        state.LongTermMemories.Add(memory);
        state.PlayerPreferenceMemories.Add(preference);
        string before = JsonSerializer.Serialize(state);

        var plan = Recall(state, TestScenarios.World(), "quartz unsaved_query_marker", preferenceCount: 1);

        Assert.Equal(before, JsonSerializer.Serialize(state));
        Assert.DoesNotContain("unsaved_query_marker", plan.Context.Tokens);
        Assert.Equal(0, memory.RecallCount);
        Assert.Equal(0, preference.RecallCount);

        MemoryRecallService.MarkRecalled(plan, TestScenarios.Today, 1200);
        MemoryRecallService.MarkRecalled(plan, TestScenarios.Today, 1200);

        Assert.Equal(1, memory.RecallCount);
        Assert.Equal(1, preference.RecallCount);
        Assert.Equal(TestScenarios.Today, memory.LastRecalledTotalDays);
        Assert.Equal(1200, preference.LastRecalledTimeOfDay);
    }

    [Fact]
    public void CommunityQueryKeepsKnownSourcePrivacyAndExpiryBoundaries()
    {
        var state = TestScenarios.TrustedState();
        var known = Impression("Penny", "Penny quietly borrowed a novel.", importance: 35);
        known.SubjectDisplayName = "潘妮";
        known.Source = "CloseCircle";
        known.HeardFromNpcName = "Sam";
        known.Visibility = "Private";
        known.CircleKey = "close_friends";
        var unrelated = Impression("Haley", "Haley took photographs.", importance: 100);
        var expired = Impression("Penny", "Penny had an old library visit.", importance: 100);
        expired.ExpiresTotalDays = TestScenarios.Today - 1;
        var blockedSubject = Impression("Torts", "Penny and Torts talked about a novel.", importance: 100);
        var blockedSource = Impression("Penny", "Penny borrowed another novel.", importance: 100);
        blockedSource.HeardFromNpcName = "Torts";
        state.CommunityImpressions.AddRange([known, unrelated, expired, blockedSubject, blockedSource]);

        Assert.Same(unrelated, MemoryRecallService.BuildCommunityImpressionPlan(state, 5, TestScenarios.Today)[0].Memory);

        var plan = MemoryRecallService.BuildCommunityImpressionPlan(state, 5, TestScenarios.Today, "潘妮最近怎么样？");

        Assert.Equal(2, plan.Count);
        Assert.Same(known, plan[0].Memory);
        Assert.Equal("Private", plan[0].Memory.Visibility);
        Assert.Equal("CloseCircle", plan[0].Memory.Source);
        Assert.Equal("Sam", plan[0].Memory.HeardFromNpcName);
        Assert.Equal("close_friends", plan[0].Memory.CircleKey);
        Assert.Equal(0, known.RecallCount);
        Assert.Empty(MemoryRecallService.BuildCommunityImpressionPlan(TestScenarios.TrustedState("Leah"), 5, TestScenarios.Today, "潘妮"));

        MemoryRecallService.MarkCommunityImpressionsRecalled(plan, TestScenarios.Today, 1200);
        MemoryRecallService.MarkCommunityImpressionsRecalled(plan, TestScenarios.Today, 1200);

        Assert.Equal(1, known.RecallCount);
        Assert.Equal(0, expired.RecallCount);
        Assert.Equal(0, blockedSource.RecallCount);
    }

    [Fact]
    public void CommunityScoresStayBoundedEvenWithOversizedReinforcementCounts()
    {
        var state = TestScenarios.TrustedState();
        var memory = Impression("Penny", "Penny borrowed a novel.", importance: 100);
        memory.TimesReinforced = int.MaxValue;
        state.CommunityImpressions.Add(memory);

        var plan = MemoryRecallService.BuildCommunityImpressionPlan(state, 1, TestScenarios.Today, "Penny");

        Assert.InRange(Assert.Single(plan).Score, 45, 512);
    }

    [Fact]
    public void TokenizerRemovesFillerButPreservesGameTopicsAndChineseKeywords()
    {
        var tokens = LocalTextSearch.Tokenize("Could you tell me about the COFFEE and coffee at the mine? 今天能喝咖啡吗？");

        Assert.Contains("coffee", tokens);
        Assert.Contains("mine", tokens);
        Assert.Contains("咖啡", tokens);
        Assert.DoesNotContain("the", tokens);
        Assert.DoesNotContain("could", tokens);
        Assert.DoesNotContain("and", tokens);
        Assert.Equal(1, tokens.Count(token => token.Equals("coffee", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void LocalSearchBoundsInputAndUniqueTokensWithoutMakingTruncatedWords()
    {
        string manyWords = string.Join(" ", Enumerable.Range(0, 5000).Select(index => $"topic{index}"));
        Assert.Equal(8, LocalTextSearch.Tokenize(manyWords, maxTokens: 8).Count);
        Assert.InRange(LocalTextSearch.Tokenize(manyWords, maxTokens: int.MaxValue).Count, 1, 1024);
        Assert.Empty(LocalTextSearch.Tokenize(manyWords, maxTokens: -1));
        Assert.Empty(LocalTextSearch.Tokenize(new string(' ', 9000) + "quartz"));
        Assert.False(LocalTextSearch.ContainsPhrase(new string(' ', 9000) + "quartz", "quartz"));
        Assert.Empty(LocalTextSearch.Tokenize(new string('a', 10000)));
        Assert.DoesNotContain("sa", LocalTextSearch.Tokenize(new string(' ', 8190) + "samwise"));
        Assert.False(LocalTextSearch.ContainsPhrase(new string(' ', 8190) + "samwise", "sa"));
    }

    [Theory]
    [InlineData("The same afternoon", "Sam", false)]
    [InlineData("Annual fair", "ann", false)]
    [InlineData("Sam's guitar", "Sam", true)]
    [InlineData("A sample, then Sam.", "Sam", true)]
    [InlineData("今天Sam想去图书馆。", "Sam", true)]
    [InlineData("今天Sammy来图书馆。", "Sam", false)]
    [InlineData("地上的石英收藏很漂亮", "石英", true)]
    [InlineData("sam_id", "sam", false)]
    [InlineData("sam123", "sam", false)]
    [InlineData("123sam", "sam", false)]
    [InlineData("Sam\u0301", "sam", false)]
    [InlineData("Sam", " ", false)]
    [InlineData(null, "Sam", false)]
    [InlineData("Sam", null, false)]
    public void PhraseSearchRespectsWordBoundaries(string? text, string? phrase, bool expected)
    {
        Assert.Equal(expected, LocalTextSearch.ContainsPhrase(text, phrase));
    }

    private static PlayerPreferenceFact Preference(string subject, string summary, int importance)
    {
        return new PlayerPreferenceFact
        {
            PreferenceKind = "liked_item_category",
            Subject = subject,
            Summary = summary,
            Importance = importance,
            CreatedTotalDays = TestScenarios.Today - 1,
            LastUpdatedTotalDays = TestScenarios.Today - 1,
            TimesReinforced = 1
        };
    }

    private static CommunityImpressionFact Impression(string subject, string summary, int importance)
    {
        return new CommunityImpressionFact
        {
            SubjectNpcName = subject,
            SubjectDisplayName = subject,
            Summary = summary,
            Source = "Witnessed",
            Visibility = "Public",
            Importance = importance,
            Confidence = 80,
            LastUpdatedTotalDays = TestScenarios.Today - 1,
            ExpiresTotalDays = TestScenarios.Today + 5,
            TimesReinforced = 1
        };
    }

    private static MemoryRecallPlan Recall(
        LivingNpcState state,
        WorldContextSnapshot world,
        string? currentPlayerText = null,
        int preferenceCount = 0)
    {
        return new BehaviorMemory().BuildMemoryRecallPlanForTesting(
            state,
            world,
            Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 2,
            preferenceCount: preferenceCount,
            currentTotalDays: TestScenarios.Today,
            currentPlayerText: currentPlayerText
        );
    }
}
