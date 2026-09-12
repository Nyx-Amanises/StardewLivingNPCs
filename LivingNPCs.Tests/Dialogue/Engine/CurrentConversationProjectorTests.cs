using System.Reflection;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Engine;

[CollectionDefinition("Current conversation locale", DisableParallelization = true)]
public sealed class CurrentConversationLocaleCollection
{
}

[Collection("Current conversation locale")]
public sealed class CurrentConversationProjectorTests
{
    [Fact]
    public void ShortConversationPreservesCompleteTextAndExistingPromptBoundary()
    {
        const string prefix = "Current conversation:\nThe following lines were spoken today.\n";
        var conversation = new[]
        {
            Npc("Hello, farmer.\nThe library is open.", "opening"),
            Player("Where is the display?", "question"),
            Npc("On the upper shelf, beside the blue vase.", "answer"),
            Player("Thank you.", "current")
        };
        string transcript = string.Join("\n", conversation.Select(turn =>
            $"{(turn.IsPlayerLine ? "Farmer" : "Penny")}: {turn.Text}"));

        var projection = CurrentConversationProjector.Build(
            conversation, "Thank you.", "Farmer", "Penny", prefix, "COMPACTION_NOTICE");

        Assert.Equal(prefix + PromptDataBoundary.Wrap("conversation_history", transcript) + Environment.NewLine, projection.Text);
        Assert.Equal(projection.Text.Length, projection.OriginalCharacters);
        Assert.False(projection.WasCompacted);
        Assert.Equal(0, projection.OmittedExchanges);
        Assert.DoesNotContain("COMPACTION_NOTICE", projection.Text);
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(CurrentConversationProjector.CharacterBudget)]
    public void LongConversationKeepsTheLatestQuestionAndFourWholeRecentExchangesWithinBudget(int budget)
    {
        var conversation = new List<ConversationTurn>();
        AddRoutineExchanges(conversation, 120);
        const string current = "Please explain that last part.";
        conversation.Add(Player(current, "current"));

        var projection = Project(conversation, current, budget);

        AssertCompacted(projection, budget);
        for (int index = 120 - CurrentConversationProjector.RecentExchangeCount; index < 120; index++)
        {
            Assert.Contains("Farmer: " + conversation[index * 2].Text, projection.Text);
            Assert.Contains("Penny: " + conversation[index * 2 + 1].Text, projection.Text);
        }
        Assert.Contains("Farmer: " + current, projection.Text);
        Assert.True(projection.Text.LastIndexOf(current, StringComparison.Ordinal)
            > projection.Text.LastIndexOf("Routine answer 119:", StringComparison.Ordinal));
        Assert.Contains("omitted", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("石英收藏放在哪里？", "My quartz collection is on the upper shelf.")]
    [InlineData("Where is the quartz collection?", "石英收藏放在书架最上层。")]
    public void ExplicitCurrentTopicRecallsACompleteOldExchangeAcrossLanguages(string query, string evidence)
    {
        var conversation = new List<ConversationTurn>
        {
            Player("Tell me about your display.", "old-player"),
            Npc(evidence, "old-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player(query, "current"));

        var projection = Project(conversation, query);

        AssertCompacted(projection);
        Assert.Contains("Farmer: Tell me about your display.", projection.Text);
        Assert.Contains("Penny: " + evidence, projection.Text);
        Assert.True(projection.Text.IndexOf(evidence, StringComparison.Ordinal)
            < projection.Text.LastIndexOf(query, StringComparison.Ordinal));
    }

    [Fact]
    public void NewerPronounCorrectionGetsSpaceBeforeManyOldLiteralPromises()
    {
        var conversation = new List<ConversationTurn>();
        for (int index = 0; index < 32; index++)
        {
            conversation.Add(Player($"Could you bring coffee for visit {index}?", $"promise-player-{index}"));
            conversation.Add(Npc($"I promise to bring coffee for visit {index}. " + new string('p', 280), $"promise-npc-{index}"));
        }
        const string correction = "Actually, I changed my mind. Do not bring it.";
        const string acknowledgement = "Understood; the earlier promise is canceled.";
        conversation.Add(Player(correction, "correction-player"));
        conversation.Add(Npc(acknowledgement, "correction-npc"));
        AddRoutineExchanges(conversation, 12);
        conversation.Add(Player("What about coffee?", "current"));

        var projection = Project(conversation, "coffee", 4000);

        AssertCompacted(projection, 4000);
        Assert.Contains("I promise to bring coffee for visit 31.", projection.Text);
        Assert.Contains(correction, projection.Text);
        Assert.Contains(acknowledgement, projection.Text);
        Assert.True(projection.Text.IndexOf(correction, StringComparison.Ordinal)
            > projection.Text.IndexOf("I promise to bring coffee for visit 31.", StringComparison.Ordinal));
        Assert.True(projection.Text.IndexOf(acknowledgement, StringComparison.Ordinal)
            < projection.Text.LastIndexOf("What about coffee?", StringComparison.Ordinal));
    }

    [Fact]
    public void RecalledExchangeKeepsItsAdjacentClarificationInOriginalOrder()
    {
        const string oldAnswer = "The quartz is on the upper shelf.";
        const string clarification = "The lower shelf; the earlier answer named the wrong shelf.";
        var conversation = new List<ConversationTurn>
        {
            Player("Where is the mineral?", "old-player"),
            Npc(oldAnswer, "old-npc"),
            Player("Which shelf was that?", "clarification-player"),
            Npc(clarification, "clarification-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("quartz", "current"));

        var projection = Project(conversation, "quartz");

        AssertCompacted(projection);
        Assert.Contains(oldAnswer, projection.Text);
        Assert.Contains("Farmer: Which shelf was that?", projection.Text);
        Assert.Contains("Penny: " + clarification, projection.Text);
        Assert.True(projection.Text.IndexOf(oldAnswer, StringComparison.Ordinal)
            < projection.Text.IndexOf(clarification, StringComparison.Ordinal));
        Assert.True(projection.Text.IndexOf(clarification, StringComparison.Ordinal)
            < projection.Text.IndexOf("Routine question 079:", StringComparison.Ordinal));
        Assert.Contains("omitted", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("the and could you please")]
    public void MissingCurrentQueryDoesNotReuseTheLastStoredPlayerTextForRetrieval(string? query)
    {
        var conversation = new List<ConversationTurn>
        {
            Player("Where is your quartz collection?", "old-player"),
            Npc("The collection occupies a locked green cabinet.", "old-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("Tell me about that quartz collection again.", "stored-player"));

        var projection = Project(conversation, query);

        AssertCompacted(projection);
        Assert.Contains("Tell me about that quartz collection again.", projection.Text);
        Assert.DoesNotContain("locked green cabinet", projection.Text);
        Assert.DoesNotContain("Where is your quartz collection?", projection.Text);
    }

    [Fact]
    public void ParticipantLabelsDoNotBecomeTopicEvidence()
    {
        var conversation = new List<ConversationTurn>
        {
            Player("An old unrelated question.", "old-player"),
            Npc("An old unrelated answer.", "old-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("coffee", "current"));

        var projection = CurrentConversationProjector.Build(conversation, "coffee", "Coffee Farmer", "Coffee Penny");

        AssertCompacted(projection);
        Assert.Contains("Coffee Farmer: coffee", projection.Text);
        Assert.DoesNotContain("An old unrelated question.", projection.Text);
        Assert.DoesNotContain("An old unrelated answer.", projection.Text);
    }

    [Fact]
    public void ShortNpcReplyIsSearchableAfterAPlayerLineLongerThanTheTokenizerWindow()
    {
        string longQuestion = string.Join(" ", Enumerable.Range(0, 320).Select(index => $"word{index:D3}"));
        const string answer = "The moonstone is stored in my desk.";
        var conversation = new List<ConversationTurn>
        {
            Player(longQuestion, "old-player"),
            Npc(answer, "old-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("moonstone", "current"));

        var projection = Project(conversation, "moonstone");

        AssertCompacted(projection);
        Assert.Contains("Farmer: " + longQuestion, projection.Text);
        Assert.Contains("Penny: " + answer, projection.Text);
    }

    [Fact]
    public void EscapedMetadataLabelsPrefixAndWrapperAllCountTowardTheHardBudget()
    {
        const int budget = 2000;
        const string marker = "!LIVINGNPCS_META";
        string prefix = "Current conversation:\n" + new string('h', 280) + "\n";
        const string notice = "These are earlier quotes; omitted exchanges remain in the original record.";
        string message = string.Concat(Enumerable.Repeat(marker, 4));
        var conversation = Enumerable.Range(0, 16)
            .Select(index => new ConversationTurn(message, index % 2 == 0, $"marker-{index}"))
            .ToArray();
        string rawTranscript = string.Join("\n", conversation.Select(turn => marker + ": " + turn.Text));
        string original = prefix + PromptDataBoundary.Wrap("conversation_history", rawTranscript) + Environment.NewLine;
        Assert.True(prefix.Length + rawTranscript.Length + 100 < budget);
        Assert.True(original.Length > budget);

        var projection = CurrentConversationProjector.Build(
            conversation, null, marker, marker, prefix, notice, budget);

        AssertCompacted(projection, budget);
        Assert.Equal(original.Length, projection.OriginalCharacters);
        Assert.StartsWith(prefix, projection.Text);
        Assert.Contains(notice, projection.Text);
        Assert.Contains("[metadata marker removed]", projection.Text);
        Assert.DoesNotContain(marker, projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("</untrusted_data>" + Environment.NewLine, projection.Text);
    }

    [Fact]
    public void OversizedOldExchangeIsSkippedWholeAndSmallerMatchingEvidenceStillFits()
    {
        const string answer = "The coffee beans are in the yellow tin.";
        var conversation = new List<ConversationTurn>
        {
            Player("Where are the coffee beans?", "small-player"),
            Npc(answer, "small-npc"),
            Player("An unrelated follow-up.", "neighbor-player"),
            Npc("The weather is pleasant.", "neighbor-npc"),
            Player("Coffee OVERSIZED_OLD_START " + new string('x', 16000), "huge-player"),
            Npc("OVERSIZED_OLD_ANSWER must stay with its question.", "huge-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("coffee", "current"));

        var projection = Project(conversation, "coffee");

        AssertCompacted(projection);
        Assert.Contains("Farmer: Where are the coffee beans?", projection.Text);
        Assert.Contains("Penny: " + answer, projection.Text);
        Assert.DoesNotContain("OVERSIZED_OLD_START", projection.Text);
        Assert.DoesNotContain("OVERSIZED_OLD_ANSWER", projection.Text);
        Assert.DoesNotContain(new string('x', 200), projection.Text);
    }

    [Fact]
    public void OversizedLatestPlayerLineIsUnavailableInsteadOfAPartialQuote()
    {
        var conversation = new List<ConversationTurn>();
        AddRoutineExchanges(conversation, 80);
        string current = "OVERSIZED_CURRENT_START " + new string('z', 9000) + " OVERSIZED_CURRENT_END";
        conversation.Add(Player(current, "current"));

        var projection = Project(conversation, current);

        AssertCompacted(projection);
        Assert.DoesNotContain("OVERSIZED_CURRENT_START", projection.Text);
        Assert.DoesNotContain("OVERSIZED_CURRENT_END", projection.Text);
        Assert.DoesNotContain(new string('z', 200), projection.Text);
        Assert.Contains("latest message unavailable", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shorten", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not infer its contents or answer an earlier turn", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.True(projection.Text.LastIndexOf("unavailable", StringComparison.OrdinalIgnoreCase)
            > projection.Text.LastIndexOf("Routine answer 079:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(512)]
    [InlineData(CurrentConversationProjector.CharacterBudget)]
    public void OversizedLatestFallbackFitsIncludingLongLabelsAndGapMarkers(int budget)
    {
        var conversation = new List<ConversationTurn>();
        AddRoutineExchanges(conversation, 100);
        conversation.Add(Player(new string('x', 10000), "current"));

        var projection = CurrentConversationProjector.Build(
            conversation, null, new string('f', 128), new string('n', 128), characterBudget: budget);

        AssertCompacted(projection, budget);
        Assert.Contains("latest message unavailable", projection.Text);
        Assert.DoesNotContain("Routine question", projection.Text);
        Assert.EndsWith("</untrusted_data>" + Environment.NewLine, projection.Text);
    }

    [Fact]
    public void LongCurrentMessageKeepsItsWholeTextBeforeOptionalIntroduction()
    {
        string current = "Complete question " + new string('q', 7400) + " final qualification.";
        var conversation = new[] { Player(current, "current") };

        var projection = CurrentConversationProjector.Build(
            conversation, current, "Farmer", "Penny", new string('i', 1200) + "\n");

        Assert.True(projection.WasCompacted);
        Assert.True(projection.Text.Length <= CurrentConversationProjector.CharacterBudget);
        Assert.Contains("Farmer: " + current, projection.Text);
        Assert.DoesNotContain("unavailable", projection.Text);
    }

    [Fact]
    public void NoUnansweredInputKeepsAtMostFourRecentCompleteExchanges()
    {
        var conversation = new List<ConversationTurn>();
        AddRoutineExchanges(conversation, 100);

        var projection = Project(conversation, null);

        AssertCompacted(projection);
        Assert.DoesNotContain("Routine question 095:", projection.Text);
        Assert.Contains("Routine question 096:", projection.Text);
        Assert.Contains("Routine answer 099:", projection.Text);
    }

    [Fact]
    public void OversizedCompletedExchangeDoesNotPresentItsOldQuestionAsUnanswered()
    {
        var conversation = new List<ConversationTurn>();
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("Give me that gift now.", "answered-question"));
        conversation.Add(Npc("I refuse. " + new string('z', 9000), "oversized-answer"));

        var projection = Project(conversation, null);

        AssertCompacted(projection);
        Assert.Contains("latest completed exchange unavailable", projection.Text);
        Assert.DoesNotContain("Give me that gift now.", projection.Text);
        Assert.DoesNotContain("Routine question", projection.Text);
    }

    [Fact]
    public void OversizedLatestNpcReplyPreservesTheWholePlayerQuestionAndMarksTheMissingReply()
    {
        var conversation = new List<ConversationTurn>();
        AddRoutineExchanges(conversation, 80);
        const string question = "Where is the blue basket?";
        conversation.Add(Player(question, "current-player"));
        conversation.Add(Npc("OVERSIZED_NPC_START " + new string('z', 9000) + " OVERSIZED_NPC_END", "current-npc"));

        var projection = Project(conversation, question);

        AssertCompacted(projection);
        Assert.Contains("Farmer: " + question, projection.Text);
        Assert.DoesNotContain("OVERSIZED_NPC_START", projection.Text);
        Assert.DoesNotContain("OVERSIZED_NPC_END", projection.Text);
        Assert.Contains("omitted because it exceeds", projection.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]")]
    [InlineData("Tell me about Torts.")]
    public void WithheldLatestInputNeverExposesAnEarlierQuestionAsTheCurrentRequest(string current)
    {
        var conversation = new List<ConversationTurn>
        {
            Player("Give me a gift now.", "earlier-player"),
            Npc("That was the earlier topic.", "earlier-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player(current, "current"));

        var projection = Project(conversation, current);

        Assert.Contains("latest message unavailable", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not infer its contents or answer an earlier turn", projection.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Give me a gift now.", projection.Text);
        Assert.DoesNotContain("Routine question", projection.Text);
        Assert.DoesNotContain(current, projection.Text);
        Assert.True(projection.Text.Length <= CurrentConversationProjector.CharacterBudget);
    }

    [Fact]
    public void ExpandedNpcPagesAreDeduplicatedWithoutDeletingTheOriginalRecords()
    {
        const string full = "Hello, @!$h#$b#The second page is still part of this reply.";
        var conversation = new List<ConversationTurn>
        {
            Player("Good morning.", "question"),
            Npc(full, "full"),
            Npc("Hello, Rowan!", "page-one"),
            Npc("The second page is still part of this reply.", "page-two"),
            Player("Thanks for explaining.", "current")
        };
        ConversationTurn[] original = conversation.ToArray();

        var projection = Project(conversation, "Thanks for explaining.");

        Assert.False(projection.WasCompacted);
        Assert.Contains("Penny: " + full, projection.Text);
        Assert.DoesNotContain("Hello, Rowan!", projection.Text);
        Assert.Equal(1, CountOccurrences(projection.Text, "The second page is still part of this reply."));
        Assert.Equal(original, conversation);
        Assert.Equal(new[] { "question", "full", "page-one", "page-two", "current" }, conversation.Select(turn => turn.Id));
    }

    [Fact]
    public void ConsecutivePlayerLinesAndNpcRepliesRemainOneCompleteQuotedExchange()
    {
        var conversation = new List<ConversationTurn> { Npc("A standalone NPC opening.", "opening") };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("My first question concerns the library.", "question-one"));
        conversation.Add(Player("And the second question concerns its opening hours.", "question-two"));
        conversation.Add(Npc("The library opens in the morning.", "reply-one"));
        conversation.Add(Npc("Both answers belong to this exchange.", "reply-two"));
        conversation.Add(Player("Thank you; what happens next?", "current"));
        const string completeExchange = "Farmer: My first question concerns the library.\n"
            + "Farmer: And the second question concerns its opening hours.\n"
            + "Penny: The library opens in the morning.\n"
            + "Penny: Both answers belong to this exchange.";

        var projection = Project(conversation, null);

        AssertCompacted(projection);
        Assert.Contains(completeExchange, projection.Text);
        Assert.Contains("Farmer: Thank you; what happens next?", projection.Text);
        Assert.DoesNotContain("A standalone NPC opening.", projection.Text);
        Assert.Equal("opening", conversation[0].Id);
    }

    [Fact]
    public void BlankAndBlockedRsvTurnsAreFilteredWithoutLosingAllowedText()
    {
        var conversation = new[]
        {
            Player("An ordinary question about the farm.", "safe-player"),
            Npc("A safe answer about the farm.", "safe-npc"),
            Player("Torts brought a map.", "blocked-player"),
            Npc("Meet me at Custom_Ridgeside_RidgesideVillage.", "blocked-npc"),
            Npc(" \t\r\n ", "blank"),
            Player("What is growing today?", "current")
        };

        var projection = Project(conversation, "What is growing today?");

        Assert.Contains("Farmer: An ordinary question about the farm.", projection.Text);
        Assert.Contains("Penny: A safe answer about the farm.", projection.Text);
        Assert.Contains("Farmer: What is growing today?", projection.Text);
        Assert.DoesNotContain("Torts", projection.Text);
        Assert.DoesNotContain("Ridgeside", projection.Text);
        Assert.Equal(6, conversation.Length);
    }

    [Fact]
    public void ConfiguredWrongLanguageFilterIsAppliedBeforeRendering()
    {
        IModHelper originalHelper = DialogueServices.Helper;
        IMonitor originalMonitor = DialogueServices.Monitor;
        ITranslationHelper translation = DispatchProxy.Create<ITranslationHelper, GetterProxy>();
        ((GetterProxy)(object)translation).Values["get_Locale"] = "zh-CN";
        IModHelper helper = DispatchProxy.Create<IModHelper, GetterProxy>();
        ((GetterProxy)(object)helper).Values["get_Translation"] = translation;
        var conversation = new[]
        {
            Player("我们去图书馆看看吧。", "player"),
            Npc("This entire English reply belongs to another language.", "wrong-language"),
            Npc("好的，我们可以一起去图书馆。", "npc"),
            Player("那里今天有什么活动？", "current")
        };
        try
        {
            DialogueServices.Initialize(helper, originalMonitor);

            var projection = Project(conversation, "那里今天有什么活动？");

            Assert.Contains("我们去图书馆看看吧。", projection.Text);
            Assert.Contains("好的，我们可以一起去图书馆。", projection.Text);
            Assert.Contains("那里今天有什么活动？", projection.Text);
            Assert.DoesNotContain("This entire English reply", projection.Text);
            Assert.Equal("wrong-language", conversation[1].Id);
        }
        finally
        {
            DialogueServices.Initialize(originalHelper, originalMonitor);
        }
    }

    [Fact]
    public void OmittedOriginalRecordsAndIdsRemainAvailableForALaterTopic()
    {
        const string evidence = "The quartz collection is in a violet box.";
        var conversation = new List<ConversationTurn>
        {
            Player("Where do you keep the quartz collection?", "original-player"),
            Npc(evidence, "original-npc")
        };
        AddRoutineExchanges(conversation, 80);
        conversation.Add(Player("How is the weather?", "first-current"));
        ConversationTurn[] original = conversation.ToArray();

        var first = Project(conversation, "weather");
        var laterConversation = conversation.Append(Player("quartz", "later-current")).ToArray();
        var later = Project(laterConversation, "quartz");

        AssertCompacted(first);
        AssertCompacted(later);
        Assert.DoesNotContain(evidence, first.Text);
        Assert.Contains("Penny: " + evidence, later.Text);
        Assert.Equal(original.Length, conversation.Count);
        for (int index = 0; index < original.Length; index++)
        {
            Assert.Same(original[index], conversation[index]);
            Assert.Equal(original[index].Id, conversation[index].Id);
            Assert.Equal(original[index].Text, conversation[index].Text);
            Assert.Equal(original[index].IsPlayerLine, conversation[index].IsPlayerLine);
        }
    }

    private static CurrentConversationProjection Project(
        IReadOnlyList<ConversationTurn> conversation, string? query, int budget = CurrentConversationProjector.CharacterBudget) =>
        CurrentConversationProjector.Build(conversation, query, "Farmer", "Penny", characterBudget: budget);

    private static ConversationTurn Player(string text, string id) => new(text, true, id);

    private static ConversationTurn Npc(string text, string id) => new(text, false, id);

    private static void AddRoutineExchanges(List<ConversationTurn> conversation, int count)
    {
        for (int index = 0; index < count; index++)
        {
            conversation.Add(Player($"Routine question {index:D3}: " + new string('x', 80), $"routine-player-{index}"));
            conversation.Add(Npc($"Routine answer {index:D3}: " + new string('y', 80), $"routine-npc-{index}"));
        }
    }

    private static void AssertCompacted(CurrentConversationProjection projection, int budget = CurrentConversationProjector.CharacterBudget)
    {
        Assert.True(projection.WasCompacted);
        Assert.True(projection.OriginalCharacters > budget);
        Assert.InRange(projection.Text.Length, 1, budget);
        Assert.True(projection.IncludedExchanges > 0);
        Assert.True(projection.OmittedExchanges > 0);
    }

    private static int CountOccurrences(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    public class GetterProxy : DispatchProxy
    {
        public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod != null && this.Values.TryGetValue(targetMethod.Name, out object? value)
                ? value
                : throw new NotSupportedException(targetMethod?.Name);
    }
}
