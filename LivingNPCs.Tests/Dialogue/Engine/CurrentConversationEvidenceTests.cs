using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Tests.Dialogue.Engine;

[Collection("Current conversation locale")]
public sealed class CurrentConversationEvidenceTests
{
    [Theory]
    [InlineData("明天在湖边碰头吧。", "说好了，明天在湖边见。", "挪后一天吧。", "行，就按这个时间。", "湖边的约定是什么时候？")]
    [InlineData("Can we meet by the lake tomorrow?", "I promise to meet by the lake tomorrow.", "Push it back by one day.", "All right, one day later.", "When is our lake meeting?")]
    [InlineData("明天在湖边碰头吧。", "说好了，明天在湖边见。", "改到星期五吧。", "好，就星期五。", "湖边的约定是什么时候？")]
    [InlineData("Can we meet by the lake tomorrow?", "I promise to meet by the lake tomorrow.", "Saturday instead.", "Saturday works.", "When is our lake meeting?")]
    public void DistantEllipticalRescheduleStaysWithTheOriginalPromise(
        string question, string promise, string revision, string acknowledgment, string current)
    {
        var turns = new List<ConversationTurn>();
        Pair(turns, question, promise);
        Routine(turns, 3);
        Pair(turns, revision, acknowledgment);
        Routine(turns, 80);

        var projection = Project(turns, current);

        AssertWithinBudget(projection);
        Assert.Contains(promise, projection.Text);
        Assert.Contains(revision, projection.Text);
        Assert.Contains(acknowledgment, projection.Text);
        Assert.True(projection.Text.IndexOf(promise, StringComparison.Ordinal)
            < projection.Text.IndexOf(revision, StringComparison.Ordinal));
    }

    [Fact]
    public void UnrelatedNewerBoundariesCannotCrowdOutARecalledPromisesCancellation()
    {
        const string promise = "I promise to meet you by the lake tomorrow.";
        const string cancellation = "Cancel that. I changed my mind.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake tomorrow?", promise);
        Routine(turns, 3);
        Pair(turns, cancellation, "Understood.");
        Routine(turns, 5);
        foreach (string boundary in new[]
        {
            "Do not mention my hat.", "I cannot swim.", "Stop touching my coat.",
            "Keep it private, the birthday surprise."
        })
        {
            Pair(turns, boundary, "I understand.");
            Routine(turns, 2);
        }
        Routine(turns, 80);

        var projection = Project(turns, "What about our lake meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(promise, projection.Text);
        Assert.Contains(cancellation, projection.Text);
    }

    [Fact]
    public void ClarificationChainCanExtendBeyondTheImmediatelyFollowingExchange()
    {
        const string oldFact = "The quartz is on the upper shelf.";
        const string correction = "The lower shelf.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Where is the quartz?", oldFact);
        Pair(turns, "Are you sure?", "Let me think.");
        Pair(turns, "Which shelf?", correction);
        Routine(turns, 80);

        var projection = Project(turns, "quartz");

        AssertWithinBudget(projection);
        Assert.Contains(oldFact, projection.Text);
        Assert.Contains("Are you sure?", projection.Text);
        Assert.Contains(correction, projection.Text);
    }

    [Fact]
    public void CrossLanguageRevisionKeepsItsOriginalWords()
    {
        const string promise = "I promise to meet you at the library tomorrow.";
        const string revision = "图书馆那次改到后天吧。";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet at the library tomorrow?", promise);
        Routine(turns, 4);
        Pair(turns, revision, "好，就后天。 ");
        Routine(turns, 80);

        var projection = Project(turns, "When is our library meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(promise, projection.Text);
        Assert.Contains(revision, projection.Text);
    }

    [Fact]
    public void TwoParallelAgreementsKeepTheSubjectOfEachRevision()
    {
        const string lakePromise = "I promise to meet you by the lake on Tuesday.";
        const string libraryPromise = "I promise to meet you at the library on Thursday.";
        const string libraryCancellation = "Cancel the library meeting.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake on Tuesday?", lakePromise);
        Pair(turns, "Can we also meet at the library on Thursday?", libraryPromise);
        Routine(turns, 3);
        Pair(turns, libraryCancellation, "Understood, we can leave Thursday free.");
        Routine(turns, 80);

        var projection = Project(turns, "What about the lake meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(lakePromise, projection.Text);
        Assert.Contains(libraryPromise, projection.Text);
        Assert.Contains(libraryCancellation, projection.Text);
        Assert.True(projection.Text.IndexOf(libraryPromise, StringComparison.Ordinal)
            < projection.Text.IndexOf(libraryCancellation, StringComparison.Ordinal));
    }

    [Fact]
    public void DistantPronounCancellationKeepsTheOtherPossibleAntecedent()
    {
        const string lakePromise = "I promise to meet you by the lake on Tuesday.";
        const string libraryPromise = "I promise to meet you at the library on Thursday.";
        const string cancellation = "Cancel that after all.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake on Tuesday?", lakePromise);
        Pair(turns, "Can we also meet at the library on Thursday?", libraryPromise);
        Routine(turns, 3);
        Pair(turns, cancellation, "All right.");
        Routine(turns, 80);

        var projection = Project(turns, "What about the lake meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(lakePromise, projection.Text);
        Assert.Contains(libraryPromise, projection.Text);
        Assert.Contains(cancellation, projection.Text);
        Assert.Contains("unresolved", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OversizedNewerCorrectionCannotLeaveOnlyTheKnownOutdatedFact()
    {
        const string oldFact = "The quartz is on the upper shelf.";
        const string correction = "Actually, the quartz is on the lower shelf.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Where is the quartz?", oldFact);
        Routine(turns, 3);
        Pair(turns, "Where is it now?", correction + new string('z', 9_000));
        Routine(turns, 80);

        var projection = Project(turns, "quartz");

        AssertWithinBudget(projection);
        Assert.DoesNotContain(oldFact, projection.Text);
        Assert.DoesNotContain(correction, projection.Text);
        Assert.Contains("later related evidence", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unavailable", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Where is the quartz collection?", "The quartz collection is on the upper shelf.",
        "Actually, the quartz collection is on the lower shelf.")]
    [InlineData("What about our lake meeting?", "I promise to meet you by the lake tomorrow.",
        "Actually, the lake meeting is cancelled.")]
    public void RequestWithAnOmittedLatestNpcAnswerCannotReviveTheOldFact(
        string current, string oldFact, string revision)
    {
        var turns = new List<ConversationTurn>();
        Pair(turns, current, oldFact);
        Routine(turns, 80);
        Pair(turns, current, revision + new string('z', 9_000));
        ConversationTurn[] original = turns.ToArray();

        var prompt = new PromptAssembler(new PromptAssemblyInput
        {
            Request = new GenerationRequest
            {
                NpcName = "Penny", Trigger = GenerationTrigger.Conversation,
                CurrentPlayerText = current, Conversation = turns
            },
            NpcName = "Penny", NpcDisplayName = "Penny", Conversation = turns
        }).Assemble();
        var request = prompt.CreateLlmRequest();

        Assert.Contains("Farmer: " + current, request.Tail);
        Assert.Contains("omitted because it exceeds", request.Tail);
        Assert.Contains("later related evidence is unavailable", request.Tail);
        Assert.DoesNotContain(oldFact, request.Tail);
        Assert.DoesNotContain(revision, request.Tail);
        Assert.InRange(prompt.SectionLengths["CurrentConversation"], 1, CurrentConversationProjector.CharacterBudget);
        Assert.Equal(original, turns);
    }

    [Fact]
    public void OptionalNeighborCannotUseAnOmittedLatestReplyAsCompleteEvidence()
    {
        const string preference = "I don't like coffee.";
        const string promise = "I promise to meet you at the library on Friday.";
        const string current = "What now?";
        var turns = new List<ConversationTurn>();
        Pair(turns, "How do you feel about coffee?", preference);
        Pair(turns, "Can we meet at the library on Friday?", promise);
        Routine(turns, 80);
        Pair(turns, current, "Actually, the library meeting is cancelled. " + new string('z', 9_000));

        var projection = CurrentConversationProjector.Build(turns, current, "Farmer", "Penny");

        AssertWithinBudget(projection);
        Assert.Contains(preference, projection.Text);
        Assert.Contains("Farmer: " + current, projection.Text);
        Assert.DoesNotContain(promise, projection.Text);
        Assert.Contains("later related evidence is unavailable", projection.Text);
    }

    [Fact]
    public void OversizedExplicitRevisionOfAnotherAgreementDoesNotSuppressTheQueriedPromise()
    {
        const string lakePromise = "I promise to meet you by the lake on Tuesday.";
        const string libraryPromise = "I promise to meet you at the library on Thursday.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake on Tuesday?", lakePromise);
        Pair(turns, "Can we also meet at the library on Thursday?", libraryPromise);
        Routine(turns, 3);
        Pair(turns, "Cancel the library meeting.", "The library visit is canceled. " + new string('z', 9_000));
        Routine(turns, 80);

        var projection = Project(turns, "What about our lake meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(lakePromise, projection.Text);
        Assert.DoesNotContain(libraryPromise, projection.Text);
    }

    [Fact]
    public void ConditionalChangeRemainsAConditionalQuote()
    {
        const string promise = "I promise to meet you by the lake tomorrow.";
        const string condition = "If it rains, cancel that; otherwise our plan stands.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake tomorrow?", promise);
        Routine(turns, 3);
        Pair(turns, condition, "Yes, only if it rains.");
        Routine(turns, 80);
        ConversationTurn[] original = turns.ToArray();

        var projection = Project(turns, "What about our lake meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(promise, projection.Text);
        Assert.Contains(condition, projection.Text);
        Assert.DoesNotContain("the lake meeting is canceled", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, turns.Take(original.Length));
    }

    [Fact]
    public void PromiseRescheduleCancellationAndReinstatementRemainChronologicalQuotes()
    {
        string[] evidence =
        {
            "I promise to meet you by the lake tomorrow.",
            "Push it back by one day.",
            "Cancel that after all.",
            "Actually, let's return to the original plan."
        };
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake tomorrow?", evidence[0]);
        for (int index = 1; index < evidence.Length; index++)
        {
            Routine(turns, 3);
            Pair(turns, evidence[index], "I understand.");
        }
        Routine(turns, 80);

        var projection = Project(turns, "What did we decide about our lake meeting?");

        AssertWithinBudget(projection);
        int previous = -1;
        foreach (string quote in evidence)
        {
            int position = projection.Text.IndexOf(quote, StringComparison.Ordinal);
            Assert.True(position > previous, quote);
            previous = position;
        }
    }

    [Fact]
    public void NewerNpcFactDoesNotRequireAnExplicitCorrectionWord()
    {
        const string oldFact = "The quartz collection is on the upper shelf.";
        const string newFact = "The quartz collection is in the locked cabinet.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Where is the quartz collection?", oldFact);
        Routine(turns, 3);
        Pair(turns, "Where did you put the collection?", newFact);
        Routine(turns, 80);

        var projection = Project(turns, "quartz collection");

        AssertWithinBudget(projection);
        Assert.Contains(oldFact, projection.Text);
        Assert.Contains(newFact, projection.Text);
        Assert.True(projection.Text.IndexOf(oldFact, StringComparison.Ordinal)
            < projection.Text.IndexOf(newFact, StringComparison.Ordinal));
    }

    [Fact]
    public void NewDateAloneDoesNotResolveAReferenceToAnotherPromiseWithThatDate()
    {
        const string lakePromise = "I promise to meet by the lake tomorrow.";
        const string otherPromise = "I promise to meet at the library on Saturday.";
        const string revision = "Saturday instead.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake tomorrow?", lakePromise);
        Routine(turns, 3);
        Pair(turns, "Can we also meet at the library on Saturday?", otherPromise);
        Routine(turns, 3);
        Pair(turns, revision, "Saturday works.");
        Routine(turns, 3);
        for (int index = 0; index < 5; index++)
        {
            Pair(turns, "Cancel the order for unrelated item " + index + ".", "Understood.");
            Routine(turns, 2);
        }
        Routine(turns, 80);

        var projection = Project(turns, "What about our lake meeting?");

        AssertWithinBudget(projection);
        Assert.Contains(lakePromise, projection.Text);
        Assert.Contains(otherPromise, projection.Text);
        Assert.Contains(revision, projection.Text);
        Assert.Contains("unresolved", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(1200)]
    [InlineData(2000)]
    public void TightBudgetDoesNotSeparateAnOldPromiseFromItsLaterChanges(int budget)
    {
        const string promise = "I promise to meet you by the lake tomorrow.";
        const string revision = "Push it back by one day.";
        const string cancellation = "Cancel that after all.";
        const string current = "What about our lake meeting?";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Can we meet by the lake tomorrow?", promise);
        Routine(turns, 3);
        Pair(turns, revision, "One day later. " + new string('r', 900));
        Routine(turns, 3);
        Pair(turns, cancellation, "All right. " + new string('c', 900));
        Routine(turns, 80);
        turns.Add(new ConversationTurn(current, true, "current"));

        var projection = CurrentConversationProjector.Build(turns, current, "Farmer", "Penny", characterBudget: budget);

        Assert.InRange(projection.Text.Length, 1, budget);
        Assert.Contains(current, projection.Text);
        Assert.DoesNotContain(promise, projection.Text);
        Assert.Contains("later related evidence is unavailable", projection.Text);
    }

    [Fact]
    public void OversizedOrdinaryPronounCorrectionDoesNotRemoveAnIndependentPromise()
    {
        const string promise = "I promise to bring you quartz tomorrow.";
        const string frame = "The frame is blue.";
        const string correction = "Actually, it is green.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Could you bring me quartz?", promise);
        Pair(turns, "What color is the frame?", frame);
        Pair(turns, correction, "I understand. " + new string('z', 9_000));
        Routine(turns, 80);

        var projection = Project(turns, "What about the quartz?");

        AssertWithinBudget(projection);
        Assert.Contains(promise, projection.Text);
        Assert.DoesNotContain(frame, projection.Text);
        Assert.DoesNotContain(correction, projection.Text);
    }

    [Fact]
    public void OrdinaryPronounCorrectionKeepsTheNearerFactDespiteAnOversizedEarlierPromise()
    {
        const string frame = "The frame is blue.";
        const string correction = "Actually, it is green.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Could you bring me quartz?", "I promise to bring you quartz tomorrow. " + new string('z', 9_000));
        Pair(turns, "What color is the frame?", frame);
        Pair(turns, correction, "I understand.");
        Routine(turns, 80);

        var projection = Project(turns, "What color is the frame?");

        AssertWithinBudget(projection);
        Assert.Contains(frame, projection.Text);
        Assert.Contains(correction, projection.Text);
        Assert.DoesNotContain("I promise to bring you quartz", projection.Text);
    }

    [Theory]
    [InlineData("Actually, it is green.", false)]
    [InlineData("Correction: it is green.", false)]
    [InlineData("it is green", false)]
    [InlineData("它不再是蓝色。", false)]
    [InlineData("Cancel it.", true)]
    [InlineData("Please reschedule that.", true)]
    [InlineData("Push it back.", true)]
    [InlineData("Saturday instead.", true)]
    [InlineData("不需要了。", true)]
    [InlineData("换到别处吧。", true)]
    public void AgreementRevisionCueDoesNotClassifyOrdinaryFactCorrections(string text, bool expected) =>
        Assert.Equal(expected, ConversationEvidenceCues.HasAgreementRevisionCue(text));

    [Fact]
    public void RevisionTopicExcludesNewDateAsIdentityWhileOrdinaryTopicKeepsIt()
    {
        Assert.Contains("Saturday", ConversationEvidenceCues.GetTopicText("Saturday instead."));
        Assert.DoesNotContain("Saturday", ConversationEvidenceCues.GetRevisionTopicText("Saturday instead."));
        Assert.Contains("library", ConversationEvidenceCues.GetRevisionTopicText("Move the library meeting to Saturday."));
        Assert.DoesNotContain("星期五", ConversationEvidenceCues.GetRevisionTopicText("图书馆那次改到星期五吧。"));
        Assert.Contains("图书馆", ConversationEvidenceCues.GetRevisionTopicText("图书馆那次改到星期五吧。"));
    }

    [Theory]
    [InlineData("我们明天湖边见面的约定还算数吗？")]
    [InlineData("之前的约定还算数吗？")]
    public void AgreementQuestionWithoutATopicMatchGetsAnOriginalPromiseAndItsCancellation(string current)
    {
        const string promiseQuestion = "Could we meet by the lake tomorrow?";
        const string promise = "I promise to meet you by the lake tomorrow.";
        const string cancellation = "Cancel that. I changed my mind.";
        var turns = new List<ConversationTurn>();
        Pair(turns, promiseQuestion, promise);
        Routine(turns, 3);
        Pair(turns, cancellation, "Understood.");
        Routine(turns, 5);
        UnrelatedBoundaries(turns);
        Routine(turns, 80);

        var projection = Project(turns, current);

        AssertWithinBudget(projection);
        Assert.Contains(promiseQuestion, projection.Text);
        Assert.Contains(promise, projection.Text);
        Assert.Contains(cancellation, projection.Text);
        Assert.True(projection.Text.IndexOf(promise, StringComparison.Ordinal)
            < projection.Text.IndexOf(cancellation, StringComparison.Ordinal));
    }

    [Fact]
    public void UnmatchedAgreementReferenceKeepsMultiplePossibleSubjectsWithoutResolvingTheReference()
    {
        const string lakePromise = "I promise to meet you by the lake tomorrow.";
        const string bookPromise = "I promise to return your book on Friday.";
        const string bookCancellation = "Cancel the book arrangement.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Could we meet by the lake tomorrow?", lakePromise);
        Routine(turns, 3);
        Pair(turns, "Can you return my book on Friday?", bookPromise);
        Routine(turns, 3);
        Pair(turns, bookCancellation, "Understood.");
        Routine(turns, 5);
        UnrelatedBoundaries(turns);
        Routine(turns, 80);

        var projection = Project(turns, "之前的约定还算数吗？");

        AssertWithinBudget(projection);
        Assert.Contains(lakePromise, projection.Text);
        Assert.Contains(bookPromise, projection.Text);
        Assert.Contains(bookCancellation, projection.Text);
        Assert.Contains("unresolved", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("the lake meeting is canceled", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnmatchedOrdinaryQuestionDoesNotActivateAgreementFallback()
    {
        const string promise = "I promise to meet you by the lake tomorrow.";
        var turns = new List<ConversationTurn>();
        Pair(turns, "Could we meet by the lake tomorrow?", promise);
        Routine(turns, 3);
        Pair(turns, "Cancel that. I changed my mind.", "Understood.");
        Routine(turns, 5);
        UnrelatedBoundaries(turns);
        Routine(turns, 80);

        var projection = Project(turns, "门口的花长得怎么样？");

        AssertWithinBudget(projection);
        Assert.DoesNotContain(promise, projection.Text);
    }

    [Theory]
    [InlineData("visit the exhibit", false)]
    [InlineData("Move it one day later.", true)]
    [InlineData("挪后一天吧。", true)]
    public void ReferenceCueUsesWordBoundaries(string text, bool expected) =>
        Assert.Equal(expected, ConversationEvidenceCues.HasAnaphoricReference(text));

    private static CurrentConversationProjection Project(List<ConversationTurn> turns, string current)
    {
        turns.Add(new ConversationTurn(current, true, "current"));
        return CurrentConversationProjector.Build(turns, current, "Farmer", "Penny");
    }

    private static void Pair(List<ConversationTurn> turns, string player, string npc)
    {
        turns.Add(new ConversationTurn(player, true, "player-" + turns.Count));
        turns.Add(new ConversationTurn(npc, false, "npc-" + turns.Count));
    }

    private static void Routine(List<ConversationTurn> turns, int count)
    {
        for (int index = 0; index < count; index++)
        {
            Pair(turns, "Routine question " + turns.Count + ": " + new string('x', 80),
                "Routine answer " + turns.Count + ": " + new string('y', 80));
        }
    }

    private static void UnrelatedBoundaries(List<ConversationTurn> turns)
    {
        foreach (string boundary in new[]
        {
            "Do not mention my hat.", "I cannot swim.", "Stop touching my coat.",
            "Keep it private, the birthday surprise."
        })
        {
            Pair(turns, boundary, "I understand.");
            Routine(turns, 3);
        }
    }

    private static void AssertWithinBudget(CurrentConversationProjection projection)
    {
        Assert.True(projection.WasCompacted);
        Assert.True(projection.OmittedExchanges > 0);
        Assert.InRange(projection.Text.Length, 1, CurrentConversationProjector.CharacterBudget);
    }
}
