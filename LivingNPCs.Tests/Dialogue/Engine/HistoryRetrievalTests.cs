using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using Newtonsoft.Json;
using StardewValley;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class HistoryRetrievalTests
{
    private static readonly StardewTime Now = new(3, Season.Spring, 14, 1200);

    [Fact]
    public void CurrentTopicCanRecallAWholeConversationOutsideTheRecentTwentyEntries()
    {
        var history = RecentHistory();
        var conversation = new ConversationHistory(new()
        {
            new ConversationElement("My quartz collection is on the upper shelf.", true) { Id = "old-player" },
            new ConversationElement("I'll help you label the display.", false) { Id = "old-npc" }
        });
        history.Add(Now.AddDays(-40), conversation);
        string before = JsonConvert.SerializeObject(history);

        List<string> lines = Sample(history, "石英收藏摆在哪里？");

        Assert.Equal(3, lines.Count);
        Assert.Contains("Farmer: My quartz collection is on the upper shelf.", lines[0]);
        Assert.Contains("Penny: I'll help you label the display.", lines[0]);
        Assert.Contains("routine moment 2", lines[1]);
        Assert.Contains("routine moment 1", lines[2]);
        Assert.DoesNotContain(Sample(history, null), line => line.Contains("quartz collection"));
        Assert.Equal(before, JsonConvert.SerializeObject(history));
    }

    [Fact]
    public void UnmatchedTopicKeepsOnlyRecentContinuityWithoutClaimingAnAnswer()
    {
        List<string> lines = Sample(RecentHistory(), "unmatched_object");

        Assert.Equal(HistorySampler.RecentContinuityEntries, lines.Count);
        Assert.Contains("routine moment 2", lines[0]);
        Assert.Contains("routine moment 1", lines[1]);
        Assert.DoesNotContain(lines, line => line.Contains("unmatched_object"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the and could you please")]
    public void WithoutAUsefulCurrentQueryTheExistingRecencyWindowRemains(string? query)
    {
        var history = RecentHistory();

        Assert.Equal(Sample(history, null), Sample(history, query));
        Assert.Equal(HistorySampler.MaxEntries, Sample(history, query).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("coffee")]
    public void OversizedRecordDoesNotHideOtherHistoryOrGetCutIntoAPartialFact(string? query)
    {
        var history = new StardewEventHistory();
        history.Add(Now, new DialogueHistory(new() { new("coffee " + new string('x', HistorySampler.CharacterBudget)) }));
        history.Add(Now.AddDays(-1), new DialogueHistory(new() { new("We shared coffee by the window.") }));

        string line = Assert.Single(Sample(history, query));

        Assert.Contains("We shared coffee by the window.", line);
        Assert.DoesNotContain("xxxxx", line);
    }

    [Fact]
    public void ExactRelevantRecordGetsBudgetBeforeLongUnrelatedRecentRecords()
    {
        var history = new StardewEventHistory();
        history.Add(Now.AddDays(-10), new DialogueHistory(new() { new("The quartz display is blue. " + new string('x', 2500)) }));
        history.Add(Now.AddDays(-1), new DialogueHistory(new() { new("Other history A. " + new string('a', 1000)) }));
        history.Add(Now, new DialogueHistory(new() { new("Other history B. " + new string('b', 1000)) }));

        List<string> lines = Sample(history, "quartz display");

        Assert.Equal(2, lines.Count);
        Assert.Contains("The quartz display is blue.", lines[0]);
        Assert.Contains(new string('x', 2500), lines[0]);
        Assert.Contains("Other history B.", lines[1]);
        Assert.True(lines.Sum(line => line.Length) <= HistorySampler.CharacterBudget);
    }

    [Fact]
    public void LongOldPreferenceCannotDisplaceTheLatestPronounCorrection()
    {
        var history = new StardewEventHistory();
        history.Add(Now.AddDays(-20), new DialogueHistory(new()
        {
            new("I like coffee as a gift. " + new string('x', 3870))
        }));
        const string correction = "I no longer like it. Please stop giving it to me; this replaces what I said earlier.";
        history.Add(Now, new DialogueHistory(new() { new(correction) }));

        List<string> lines = Sample(history, "coffee");

        Assert.Contains(lines, line => line.Contains(correction));
        Assert.True(lines.Sum(line => line.Length) <= HistorySampler.CharacterBudget);
    }

    [Fact]
    public void NewerBilingualCorrectionSurvivesManyOlderLiteralMatches()
    {
        var history = RecentHistory();
        history.Add(Now.AddDays(-28), new DialogueHistory(new() { new("我已经不喜欢咖啡了，别再把它当成礼物。") }));
        for (int i = 40; i <= 50; i++)
        {
            history.Add(Now.AddDays(-i), new DialogueHistory(new() { new($"We enjoyed coffee on afternoon {i}.") }));
        }

        List<string> lines = Sample(history, "coffee");

        Assert.Contains(lines, line => line.Contains("我已经不喜欢咖啡了"));
        Assert.Contains(lines, line => line.Contains("routine moment 1"));
        Assert.True(lines.Count <= HistorySampler.RelatedEntries + HistorySampler.RecentContinuityEntries);
    }

    [Fact]
    public void RelatedHistoryStaysBoundedAndChronologicalWithParticipantRolesIntact()
    {
        var history = new StardewEventHistory();
        for (int i = 1; i <= 12; i++)
        {
            history.Add(Now.AddDays(-i), new ThirdPartyHistory("Sam", new() { new($"Coffee story {i}.") }, "Egg Festival"));
        }

        List<string> lines = Sample(history, "咖啡");

        Assert.Equal(HistorySampler.RelatedEntries, lines.Count);
        Assert.Contains("Coffee story 6.", lines[0]);
        Assert.Contains("Coffee story 1.", lines[^1]);
        Assert.All(lines, line =>
        {
            Assert.Contains("Penny ← Sam:", line);
            Assert.Contains("Egg Festival", line);
        });
    }

    [Fact]
    public void CurrentConversationAndBlockedSourcesNeverEnterTheRetrievedHistory()
    {
        var history = RecentHistory();
        history.Add(Now, new ConversationHistory(new() { new("Coffee in the current session.", true) { Id = "current" } }));
        history.Add(Now, new OverheardHistory("Torts", new() { new("Coffee from a blocked speaker.") }));
        history.Add(Now, new DialogueHistory(new() { new("Coffee with Torts.") }));
        history.Add(Now.AddDays(-40), new OverheardHistory("Sam", new() { new("I no longer drink coffee.") }));

        List<string> lines = Sample(history, "咖啡", conversationId: "current");

        Assert.Contains(lines, line => line.Contains("I no longer drink coffee."));
        Assert.DoesNotContain(lines, line => line.Contains("current session") || line.Contains("Torts") || line.Contains("blocked speaker"));
    }

    [Fact]
    public void BilingualWitnessIdentityIsIndependentOfTheEventName()
    {
        var history = RecentHistory();
        history.Add(Now.AddDays(-40), new ThirdPartyHistory("Sam", new() { new("I brought a guitar.") }, "Egg Festival"));

        string witness = Assert.Single(Sample(history, "山姆"), line => line.Contains("guitar"));

        Assert.Contains("Penny ← Sam:", witness);
        Assert.Contains("Egg Festival", witness);
    }

    [Fact]
    public void DuplicateRecordsDoNotUseUpRelatedSlots()
    {
        var history = new StardewEventHistory();
        for (int i = 0; i < HistorySampler.RelatedEntries; i++)
        {
            history.Add(Now, new DialogueHistory(new() { new("A coffee break.") }));
        }
        history.Add(Now.AddDays(-1), new DialogueHistory(new() { new("We bought coffee beans.") }));
        history.Add(Now.AddDays(-2), new DialogueHistory(new() { new("We stopped drinking coffee.") }));

        List<string> lines = Sample(history, "coffee");

        Assert.Equal(3, lines.Count);
        Assert.Contains(lines, line => line.Contains("stopped drinking coffee"));
    }

    [Fact]
    public void LocalizedTemplateWordsDoNotBecomeHistorySearchEvidence()
    {
        List<string> lines = Sample(RecentHistory(), "coffee", lookup: key =>
            key == "historyDialogueFormat" ? "Coffee-themed archive [{{when}}] {{speaker}}: {{text}}" : null);

        Assert.Equal(HistorySampler.RecentContinuityEntries, lines.Count);
    }

    [Fact]
    public void SpeakerDisplayLabelsAreNotConversationTopicEvidence()
    {
        var history = RecentHistory();
        history.Add(Now.AddDays(-40), new ConversationHistory(new()
        {
            new("An unrelated conversation.", true), new("Yes, I remember.", false)
        }));

        List<string> lines = Sample(history, "coffee", lookup: key =>
            key == "generalFarmerLabel" ? "Coffee farmer" : null);

        Assert.DoesNotContain(lines, line => line.Contains("unrelated conversation"));
        Assert.Equal(HistorySampler.RecentContinuityEntries, lines.Count);
    }

    private static StardewEventHistory RecentHistory()
    {
        var history = new StardewEventHistory();
        for (int i = 1; i <= 25; i++)
        {
            history.Add(Now.AddDays(-i), new DialogueHistory(new() { new($"A routine moment {i}.") }));
        }
        return history;
    }

    private static List<string> Sample(StardewEventHistory history, string? query,
        string conversationId = "", Func<string, string?>? lookup = null) =>
        HistorySampler.Sample(history, Array.Empty<KeyValuePair<string, int>>(), Now, "Penny", conversationId,
            lookup ?? (_ => null), query);
}
