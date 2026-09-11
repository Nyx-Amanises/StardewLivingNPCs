using System.Text.Json;
using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class MemoryTopicRecallTests
{
    // Fixed relevance labels, independent of scores. Each case has eight more salient but
    // unrelated candidates and uses the production 3-personal/4-preference prompt budgets.
    // Public MemberData also lets the offline benchmark replay the same labels against old DLLs.
    public static IEnumerable<object[]> BilingualRecallCases()
    {
        yield return Case("coffee_zh_en", "忙完还想喝咖啡。", "coffee", "The farmer enjoys coffee breaks.");
        yield return Case("coffee_en_zh", "Coffee, please.", "咖啡", "玩家喜欢忙完之后喝一杯咖啡。");
        yield return Case("quartz_zh_en", "还记得我的石英收藏吗？", "quartz", "The farmer collects quartz.");
        yield return Case("quartz_en_zh", "Tell me about quartz.", "石英", "玩家把石英收藏放在书架上。");
        yield return Case("quiet_crowds_zh_en", "人多吵得我紧张，想清静一点。", "quiet settings", "The farmer prefers quiet evenings and avoids crowds.");
        yield return Case("quiet_en_zh", "I need some peace and quiet.", "清静", "玩家宁愿找个清静的地方坐一会儿。");
        yield return Case("reading_zh_en", "借来的书读完了，还想继续阅读。", "reading", "The farmer enjoys reading novels.");
        yield return Case("cooking_en_zh", "Could we cook a meal?", "烹饪", "玩家喜欢亲手烹饪。");
        yield return Case("fishing_zh_en", "今天想去垂钓。", "angling", "The farmer enjoys fishing on weekends.");
        yield return Case("mining_en_zh", "Let's go mining.", "挖矿", "玩家喜欢深入矿洞挖矿。");
        yield return Case("guitar_zh_en", "我想练吉他。", "guitar", "The farmer practices the guitar every weekend.");
        yield return Case("piano_en_zh", "Do you remember the piano?", "钢琴", "玩家过去常常练习钢琴。");
        yield return Case("sam_zh_en", "山姆怎么样？", "Sam", "Sam lent the farmer a guitar.");
        yield return Case("penny_en_zh", "What did Penny promise?", "潘妮", "潘妮答应周日归还围巾。");
        yield return Case("library_zh_en", "图书馆那次还记得吗？", "library", "We met in the library to return a borrowed atlas.");
        yield return Case("library_en_zh", "Do you remember the library?", "图书馆", "我们在图书馆一起整理过地图集。");
    }

    [Theory]
    [MemberData(nameof(BilingualRecallCases))]
    public void BilingualTopicsReachTheExistingPromptBudget(string caseId, string query, string subject, string summary)
    {
        var state = SaturatedState();
        var relevantMemory = TestScenarios.Memory(summary, importance: 25, subject: subject);
        var relevantPreference = Preference(subject, summary, importance: 25);
        state.LongTermMemories.Add(relevantMemory);
        state.PlayerPreferenceMemories.Add(relevantPreference);

        var before = Recall(state, null);
        Assert.DoesNotContain(before.LongTermMemories, selection => selection.Memory == relevantMemory);
        Assert.DoesNotContain(before.PlayerPreferences, selection => selection.Memory == relevantPreference);

        var recalled = Recall(state, query);

        Assert.True(recalled.LongTermMemories[0].Memory == relevantMemory, caseId);
        Assert.True(recalled.PlayerPreferences[0].Memory == relevantPreference, caseId);
        Assert.Equal(3, recalled.LongTermMemories.Count);
        Assert.Equal(4, recalled.PlayerPreferences.Count);
    }

    [Fact]
    public void ADirectItemMatchOutranksAWeakerAliasAndDoesNotExpandToAssociatedPlaces()
    {
        var state = TestScenarios.TrustedState();
        var wine = TestScenarios.Memory("The farmer enjoys wine in the saloon.", importance: 100, subject: "wine");
        var alias = TestScenarios.Memory("The farmer likes coffee.", importance: 100, subject: "coffee");
        var direct = TestScenarios.Memory("玩家会在清晨喝咖啡。", importance: 25, subject: "咖啡");
        state.LongTermMemories.AddRange([wine, alias, direct]);

        var plan = Recall(state, "咖啡？");

        Assert.Same(direct, plan.LongTermMemories[0].Memory);
        Assert.Same(alias, plan.LongTermMemories[1].Memory);
        Assert.DoesNotContain("current topic", plan.LongTermMemories.Single(selection => selection.Memory == wine).Reason);
    }

    [Fact]
    public void AliasRecallPreservesNegationAndPreferenceKindWithoutSavingTheQuery()
    {
        var state = SaturatedState();
        var preference = Preference("coffee", "The farmer dislikes coffee and no longer wants it as a gift.", importance: 25);
        preference.PreferenceKind = "disliked_item";
        PlayerPreferenceMemoryStore.NormalizeForStore(preference);
        state.PlayerPreferenceMemories.Add(preference);
        foreach (var memory in state.LongTermMemories)
            LongTermMemoryStore.NormalizeForStore(memory);
        foreach (var candidate in state.PlayerPreferenceMemories)
            PlayerPreferenceMemoryStore.NormalizeForStore(candidate);
        string savedBefore = JsonSerializer.Serialize(state);

        var plan = Recall(state, "别再送我咖啡了，unsaved_query_marker。");

        Assert.Same(preference, plan.PlayerPreferences[0].Memory);
        Assert.Equal("disliked_item", preference.PreferenceKind);
        Assert.Equal("The farmer dislikes coffee and no longer wants it as a gift.", preference.Summary);
        Assert.Equal(savedBefore, JsonSerializer.Serialize(state));
        Assert.Equal(0, preference.RecallCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n")]
    [InlineData("the and could you please")]
    [InlineData("unmatched_topic")]
    [InlineData("Coffeeberry")]
    public void NoUsefulOrMatchingQueryKeepsSalienceOrder(string? query)
    {
        var state = SaturatedState();
        state.LongTermMemories.Add(TestScenarios.Memory("The farmer remembers coffee.", importance: 25, subject: "coffee"));
        state.PlayerPreferenceMemories.Add(Preference("咖啡", "玩家喜欢咖啡。", importance: 25));

        var baseline = Recall(state, null);
        var plan = Recall(state, query);

        Assert.Equal(baseline.LongTermMemories, plan.LongTermMemories);
        Assert.Equal(baseline.PlayerPreferences, plan.PlayerPreferences);
    }

    [Theory]
    [InlineData("Sam", "A sample painting was discussed.", "sample")]
    [InlineData("山姆", "Penny mentioned Sam while discussing a parcel.", "Penny")]
    [InlineData("山姆", "Sam and Penny exchanged a parcel.", "Sam and Penny")]
    [InlineData("苹果派", "Apples left a message.", "Apples")]
    [InlineData("sam_id", "山姆归还了围巾。", "山姆")]
    [InlineData("Sammy", "山姆归还了围巾。", "山姆")]
    public void NamesRequireStoredIdentityAndWordBoundaries(string query, string summary, string subject)
    {
        var state = SaturatedState();
        var unrelated = TestScenarios.Memory(summary, importance: 25, subject: subject);
        state.LongTermMemories.Add(unrelated);

        var baseline = Recall(state, null);
        var plan = Recall(state, query);

        Assert.Equal(baseline.LongTermMemories, plan.LongTermMemories);
        Assert.DoesNotContain(plan.LongTermMemories, selection => selection.Memory == unrelated);
    }

    [Fact]
    public void BilingualCommunityRecallKeepsExpiryPrivacySourceAndNpcOwnership()
    {
        var state = TestScenarios.TrustedState();
        var known = Impression("Sam", "Sam", "He returned an atlas.", importance: 25);
        known.Visibility = "Private";
        known.Source = "CloseCircle";
        known.HeardFromNpcName = "Penny";
        known.CircleKey = "close_friends";
        var expired = Impression("Sam", "Sam", "He kept an old atlas.", importance: 100);
        expired.ExpiresTotalDays = TestScenarios.Today - 1;
        var blockedSource = Impression("Sam", "Sam", "He bought a new atlas.", importance: 100);
        blockedSource.HeardFromNpcName = "Torts";
        var blockedContent = Impression("Sam", "Sam", "Sam visited Torts.", importance: 100);
        state.CommunityImpressions.AddRange([
            Impression("Haley", "Haley", "She folded a scarf.", importance: 100),
            Impression("Leah", "Leah", "She repaired a coat.", importance: 95),
            known, expired, blockedSource, blockedContent
        ]);

        var plan = MemoryRecallService.BuildCommunityImpressionPlan(state, 2, TestScenarios.Today, "山姆怎么样？");

        Assert.Same(known, plan[0].Memory);
        Assert.Equal("Private", plan[0].Memory.Visibility);
        Assert.Equal("CloseCircle", plan[0].Memory.Source);
        Assert.Equal("Penny", plan[0].Memory.HeardFromNpcName);
        Assert.Equal("close_friends", plan[0].Memory.CircleKey);
        Assert.DoesNotContain(plan, selection => selection.Memory == expired || selection.Memory == blockedSource || selection.Memory == blockedContent);
        Assert.Empty(MemoryRecallService.BuildCommunityImpressionPlan(TestScenarios.TrustedState("Leah"), 2, TestScenarios.Today, "山姆"));
        Assert.Equal(0, known.RecallCount);
    }

    [Fact]
    public void AliasMatchingDoesNotExpandInferredSceneTags()
    {
        var state = SaturatedState();
        var beach = TestScenarios.Memory("We returned a parcel at the beach.", importance: 25, subject: "parcel");
        LongTermMemoryStore.NormalizeForStore(beach);
        Assert.Contains("fishing", beach.Tags);
        state.LongTermMemories.Add(beach);

        var baseline = Recall(state, null);
        var plan = Recall(state, "垂钓。");

        Assert.Equal(baseline.LongTermMemories, plan.LongTermMemories);
    }

    [Fact]
    public void RepetitionDoesNotBoostAliasesAndFarBeyondInputLimitDoesNotMatch()
    {
        var state = SaturatedState();
        var relevant = TestScenarios.Memory("The farmer enjoys coffee breaks.", importance: 25, subject: "coffee");
        state.LongTermMemories.Add(relevant);
        var once = Recall(state, "咖啡");
        var repeated = Recall(state, string.Concat(Enumerable.Repeat("咖啡。", 2000)));

        Assert.Same(relevant, once.LongTermMemories[0].Memory);
        Assert.Equal(once.LongTermMemories[0].Score, repeated.LongTermMemories[0].Score);
        Assert.Equal(Recall(state, null).LongTermMemories, Recall(state, new string(' ', 9000) + "咖啡").LongTermMemories);
    }

    private static object[] Case(string id, string query, string subject, string summary) => [id, query, subject, summary];

    private static LivingNpcState SaturatedState()
    {
        var state = TestScenarios.TrustedState();
        string[] summaries =
        [
            "The farmer mended a torn coat.", "An old red ribbon was kept.",
            "A blue plate was returned.", "The wooden chair has a loose leg.",
            "The farmer described a broken clock.", "The farmer found a spare button.",
            "A small parcel arrived.", "The curtains were repaired."
        ];
        for (int index = 0; index < summaries.Length; index++)
        {
            string subject = $"unrelated_{index}";
            state.LongTermMemories.Add(TestScenarios.Memory(summaries[index], importance: 95 - index, subject: subject));
            state.PlayerPreferenceMemories.Add(Preference(subject, summaries[index], importance: 95 - index));
        }

        return state;
    }

    private static PlayerPreferenceFact Preference(string subject, string summary, int importance)
    {
        return new PlayerPreferenceFact
        {
            PreferenceKind = "habit",
            Subject = subject,
            Summary = summary,
            Importance = importance,
            CreatedTotalDays = TestScenarios.Today - 1,
            LastUpdatedTotalDays = TestScenarios.Today - 1,
            TimesReinforced = 1
        };
    }

    private static CommunityImpressionFact Impression(string name, string displayName, string summary, int importance)
    {
        return new CommunityImpressionFact
        {
            SubjectNpcName = name,
            SubjectDisplayName = displayName,
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

    private static MemoryRecallPlan Recall(LivingNpcState state, string? text)
    {
        return MemoryRecallService.BuildPlan(state, TestScenarios.World(), Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 3, preferenceCount: 4, currentTotalDays: TestScenarios.Today, currentPlayerText: text);
    }
}
