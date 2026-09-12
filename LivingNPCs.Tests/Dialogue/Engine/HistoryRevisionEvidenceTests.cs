using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using Newtonsoft.Json;
using StardewValley;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class HistoryRevisionEvidenceTests
{
    private static readonly StardewTime Now = new(3, Season.Spring, 20, 1200);

    [Theory]
    [InlineData("I promise to bring quartz on Tuesday.", "Push it back one day.", "Cancel that after all.", "石英还需要我带来吗？")]
    [InlineData("我答应周二带石英。", "后天吧。", "那件事还是算了。", "Do I still owe you quartz?")]
    public void DistantPromiseKeepsItsPronounRescheduleAndCancellationAcrossRecordedConversations(
        string promise, string reschedule, string cancellation, string query)
    {
        var history = RoutineHistory();
        AddConversation(history, -40, promise, "Agreed.");
        AddConversation(history, -35, reschedule, "Understood.");
        AddConversation(history, -30, cancellation, "All right.");
        string before = JsonConvert.SerializeObject(history);

        List<string> lines = Sample(history, query);

        AssertOrdered(lines, promise, reschedule, cancellation);
        Assert.Contains(lines, line => line.Contains("Farmer: " + promise) && line.Contains("Penny: Agreed."));
        Assert.Equal(before, JsonConvert.SerializeObject(history));
        AssertBudget(lines);
    }

    [Fact]
    public void LaterUnrelatedBoundariesCannotDisplaceTheCancellationOfAQueriedAgreement()
    {
        var history = RoutineHistory();
        AddConversation(history, -60, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -50, "Cancel that after all.", "Understood.");
        for (int i = 0; i < 7; i++)
        {
            AddConversation(history, -45 + i, $"Actually the picture frame {i} is blue.", "All right.");
        }

        List<string> lines = Sample(history, "quartz");

        AssertOrdered(lines, "promise to bring quartz", "Cancel that after all.");
        AssertBudget(lines);
    }

    [Fact]
    public void ExplicitChangeToASeparateAgreementDoesNotInvalidateTheQueriedOne()
    {
        var history = RoutineHistory();
        AddConversation(history, -50, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -45, "I promise to bring coffee on Friday.", "Agreed.");
        AddConversation(history, -40, "The coffee delivery is cancelled.", new string('x', 4300));

        List<string> lines = Sample(history, "quartz");

        Assert.Contains(lines, line => line.Contains("promise to bring quartz"));
        Assert.DoesNotContain(lines, line => line.Contains("withheld") || line.Contains("coffee"));
        AssertBudget(lines);
    }

    [Fact]
    public void AmbiguousPronounCarriesTheOtherAgreementInsteadOfChoosingItsReferent()
    {
        var history = RoutineHistory();
        AddConversation(history, -50, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -45, "I promise to bring coffee on Friday.", "Agreed.");
        AddConversation(history, -40, "Cancel it.", "All right.");

        List<string> lines = Sample(history, "quartz");

        AssertOrdered(lines, "quartz on Tuesday", "coffee on Friday", "Cancel it.");
        Assert.DoesNotContain(lines, line => line.Contains("quartz was cancelled"));
        AssertBudget(lines);
    }

    [Fact]
    public void OverheardCancellationDoesNotReviseTheNpcsOwnAgreement()
    {
        var history = RoutineHistory();
        AddConversation(history, -50, "I promise to bring quartz on Tuesday.", "Agreed.");
        history.Add(Now.AddDays(-40), new OverheardHistory("Sam", new() { new("Cancel it. " + new string('x', 4300)) }));

        List<string> lines = Sample(history, "quartz");

        Assert.Contains(lines, line => line.Contains("promise to bring quartz"));
        Assert.DoesNotContain(lines, line => line.Contains("withheld") || line.Contains("Sam"));
    }

    [Theory]
    [InlineData("quartz")]
    [InlineData(null)]
    public void OversizedLaterRevisionWithholdsTheOlderClaimInsteadOfShowingAStaleAnswer(string? query)
    {
        var history = new StardewEventHistory();
        AddConversation(history, -2, "The quartz is on the top shelf.", "I see it.");
        AddConversation(history, -1, "Actually it is on the bottom shelf.", new string('x', 4300));

        List<string> lines = Sample(history, query);

        Assert.DoesNotContain(lines, line => line.Contains("on the top shelf"));
        Assert.Contains(lines, line => line.Contains("latest status") && line.Contains("unavailable"));
        AssertBudget(lines);
    }

    [Theory]
    [InlineData("The quartz collection is on the upper shelf.", "The quartz collection is on the lower shelf.", "quartz collection")]
    [InlineData("The quartz collection is on the upper shelf.", "The quartz collection is on the lower shelf.", null)]
    [InlineData("石英收藏放在上层架子。", "石英收藏放在下层架子。", "石英收藏")]
    [InlineData("石英收藏放在上层架子。", "石英收藏放在下层架子。", null)]
    public void OversizedRepeatedTopicDoesNotNeedAnExplicitCorrectionWord(string oldFact, string newFact, string? query)
    {
        var history = new StardewEventHistory();
        AddConversation(history, -2, oldFact, "Noted.");
        AddConversation(history, -1, newFact, new string('x', 4300));
        string before = JsonConvert.SerializeObject(history);

        List<string> lines = Sample(history, query);

        Assert.DoesNotContain(lines, line => line.Contains(oldFact) || line.Contains(newFact));
        Assert.Contains(lines, line => line.Contains("latest status") && line.Contains("unavailable"));
        Assert.Equal(before, JsonConvert.SerializeObject(history));
        AssertBudget(lines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedRepeatedTopicAcrossEqualTimeSourcesCannotLeaveAnOlderClaim(bool oversizedInConversation)
    {
        var history = new StardewEventHistory();
        const string oldFact = "The quartz collection is on the upper shelf.";
        const string newFact = "The quartz collection is on the lower shelf. ";
        if (oversizedInConversation)
        {
            AddConversation(history, -1, newFact, new string('x', 4300));
            history.Add(Now.AddDays(-1), new DialogueHistory(new() { new(oldFact) }));
        }
        else
        {
            AddConversation(history, -1, oldFact, "Noted.");
            history.Add(Now.AddDays(-1), new DialogueHistory(new() { new(newFact + new string('x', 4300)) }));
        }

        List<string> lines = Sample(history, "quartz collection");

        Assert.DoesNotContain(lines, line => line.Contains(oldFact) || line.Contains(newFact));
        Assert.Contains(lines, line => line.Contains("latest status") && line.Contains("unavailable"));
        AssertBudget(lines);
    }

    [Fact]
    public void RevisionsAtTheSameRecordedTimeKeepTheirStoredOrder()
    {
        var history = RoutineHistory();
        AddConversation(history, -40, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -40, "Actually cancel it.", "All right.");

        List<string> lines = Sample(history, "quartz");

        AssertOrdered(lines, "promise to bring quartz", "Actually cancel it.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualClockTimesAcrossSourceArraysCannotHideACancellation(bool promiseInConversation)
    {
        var history = RoutineHistory();
        const string promise = "I promise to bring quartz on Tuesday.";
        const string cancel = "Actually cancel it.";
        if (promiseInConversation)
        {
            AddConversation(history, -40, promise, "Agreed.");
            history.Add(Now.AddDays(-40), new DialogueHistory(new() { new(cancel) }));
        }
        else
        {
            history.Add(Now.AddDays(-40), new DialogueHistory(new() { new(promise) }));
            AddConversation(history, -40, cancel, "All right.");
        }

        List<string> lines = Sample(history, "quartz");

        Assert.Contains(lines, line => line.Contains(promise));
        Assert.Contains(lines, line => line.Contains(cancel));
        Assert.Contains(lines, line => line.Contains("Order among different history sources") && line.Contains("unknown"));
        AssertBudget(lines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteEvidenceNoticeCannotSplitAnEqualTimeRevisionGroup(bool fillCharacterBudget)
    {
        var history = new StardewEventHistory();
        const string promise = "I promise to bring quartz on Tuesday.";
        const string cancellation = "Actually cancel it.";
        const string recent = "A quiet moment today.";
        AddConversation(history, -4, cancellation, "All right.");
        int promiseCount = fillCharacterBudget ? 1 : HistorySampler.MaxEntries - 2;
        for (int i = 0; i < promiseCount; i++)
        {
            history.Add(Now.AddDays(-4), new DialogueHistory(new() { new($"{promise} Note {i}.") }));
        }
        history.Add(Now, new DialogueHistory(new() { new(recent) }));

        List<string> withoutGap = Sample(history, null);
        Assert.Equal(promiseCount + 2, withoutGap.Count);
        if (fillCharacterBudget)
        {
            // The gap notice needs more than this spare space, but trimming the first
            // equal-time line alone frees enough room to hide the cancellation.
            history.DialogueHistory[0].Item2.Dialogues[0].Text += new string('x',
                HistorySampler.CharacterBudget - withoutGap.Sum(line => line.Length) - 100);
            Assert.Equal(HistorySampler.CharacterBudget - 100, Sample(history, null).Sum(line => line.Length));
        }
        else
        {
            Assert.Equal(HistorySampler.MaxEntries, withoutGap.Count);
        }
        AddConversation(history, -2, "The frame is blue.", "Noted.");
        AddConversation(history, -1, "Actually it is green.", new string('x', 4300));
        string before = JsonConvert.SerializeObject(history);

        List<string> lines = Sample(history, null);

        Assert.Contains(lines, line => line.Contains("latest status") && line.Contains("unavailable"));
        Assert.Contains(lines, line => line.Contains(recent));
        Assert.False(lines.Any(line => line.Contains(promise)) && !lines.Any(line => line.Contains(cancellation)),
            "Making room for the gap notice must not leave a promise after dropping its equal-time cancellation.");
        Assert.Equal(before, JsonConvert.SerializeObject(history));
        AssertBudget(lines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryFactCorrectionDoesNotWithholdOrChangeAnIndependentPromise(bool oversizedReply)
    {
        var history = RoutineHistory();
        AddConversation(history, -50, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -45, "The frame is blue.", "Noted.");
        AddConversation(history, -40, "Actually it is green.", oversizedReply ? new string('x', 4300) : "Understood.");

        List<string> lines = Sample(history, "quartz");

        Assert.Contains(lines, line => line.Contains("promise to bring quartz"));
        Assert.DoesNotContain(lines, line => line.Contains("Actually it is green") || line.Contains("withheld"));
        AssertBudget(lines);
    }

    [Fact]
    public void OrdinaryFactCorrectionKeepsTheFactThatExplainsItsPronoun()
    {
        var history = RoutineHistory();
        AddConversation(history, -50, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -45, "The frame is blue.", "Noted.");
        AddConversation(history, -40, "Actually it is green.", "Understood.");

        List<string> lines = Sample(history, "frame");

        AssertOrdered(lines, "The frame is blue.", "Actually it is green.");
        Assert.DoesNotContain(lines, line => line.Contains("quartz"));
        AssertBudget(lines);
    }

    [Fact]
    public void FollowingQuestionAndAnswerKeepAChangedFactWithoutAnExplicitCorrectionWord()
    {
        var history = RoutineHistory();
        AddConversation(history, -40, "Where is the quartz collection?", "The quartz collection is on the upper shelf.");
        AddConversation(history, -39, "Which shelf is it on now?", "The lower shelf.");
        AddConversation(history, -38, "Are you sure?", "Yes, the lower shelf.");

        List<string> lines = Sample(history, "quartz collection");

        AssertOrdered(lines, "on the upper shelf", "Which shelf is it on now", "Yes, the lower shelf.");
        AssertBudget(lines);
    }

    [Fact]
    public void ANewQuestionDoesNotLinkBackMerelyBecauseItsAnswerHasAPronoun()
    {
        var history = RoutineHistory();
        AddConversation(history, -40, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -39, "Tell me about the coffee.", "I like it. " + new string('x', 4300));

        List<string> lines = Sample(history, "quartz");

        Assert.Contains(lines, line => line.Contains("promise to bring quartz"));
        Assert.DoesNotContain(lines, line => line.Contains("withheld") || line.Contains("coffee"));
    }

    [Fact]
    public void BlockedRevisionTextNeverReachesThePrompt()
    {
        var history = RoutineHistory();
        AddConversation(history, -40, "I promise to bring quartz on Tuesday.", "Agreed.");
        AddConversation(history, -35, "Torts cancelled it.", "Torts confirmed it.");

        List<string> lines = Sample(history, "quartz");

        Assert.DoesNotContain(lines, line => line.Contains("Torts"));
        AssertBudget(lines);
    }

    private static StardewEventHistory RoutineHistory()
    {
        var history = new StardewEventHistory();
        for (int i = 1; i <= 25; i++)
        {
            history.Add(Now.AddDays(-i), new DialogueHistory(new() { new($"A routine moment {i}.") }));
        }
        return history;
    }

    private static void AddConversation(StardewEventHistory history, int offset, string player, string npc) =>
        history.Add(Now.AddDays(offset), new ConversationHistory(new() { new(player, true), new(npc, false) }));

    private static List<string> Sample(StardewEventHistory history, string? query) =>
        HistorySampler.Sample(history, Array.Empty<KeyValuePair<string, int>>(), Now, "Penny", string.Empty, _ => null, query);

    private static void AssertOrdered(IReadOnlyList<string> lines, params string[] facts)
    {
        string text = string.Join("\n", lines);
        int previous = -1;
        foreach (string fact in facts)
        {
            int position = text.IndexOf(fact, StringComparison.Ordinal);
            Assert.True(position > previous, $"Missing or misordered evidence: {fact}");
            previous = position;
        }
    }

    private static void AssertBudget(IReadOnlyList<string> lines)
    {
        Assert.InRange(lines.Count, 0, HistorySampler.MaxEntries);
        Assert.InRange(lines.Sum(line => line.Length), 0, HistorySampler.CharacterBudget);
    }
}
