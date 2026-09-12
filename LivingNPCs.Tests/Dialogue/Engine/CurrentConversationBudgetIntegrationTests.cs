using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Persistence;
using Xunit;
using PromptFragments = LivingNPCs.Behavior.PromptFragments;

namespace LivingNPCs.Tests.Dialogue.Engine;

[Collection("LlmLayer")]
public sealed class CurrentConversationBudgetIntegrationTests : IDisposable
{
    private const string Heading = "CURRENT_CONVERSATION_START";
    private const string Intro = "Earlier dialogue follows:";
    private const string CompactNotice = "Conversation excerpt; omitted turns remain in local history.";
    private const string JustSpokeNotice = "Penny has just spoken to the farmer.";
    private const string CurrentText = "That sounds pleasant.";
    private readonly LegacyLlm oldLegacyClient = LegacyLlm.Instance;

    public CurrentConversationBudgetIntegrationTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig
        {
            EnableSemanticContextRouting = false,
            EnableLivingNpcActionDecisionPass = false,
            ModelName = "current-conversation-test-model",
            TypedResponses = "With Generated"
        });
        ThirdPartyContentPolicy.ResetForTests();
        LegacyLlm.Instance = new CountingRouter();
    }

    public void Dispose()
    {
        LegacyLlm.Instance = this.oldLegacyClient;
        ThirdPartyContentPolicy.ResetForTests();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Theory]
    [InlineData(40)]
    [InlineData(400)]
    public void EntireConversationSectionIncludesHeadingsNoticeAndBoundaryInItsBudget(int exchanges)
    {
        List<ConversationTurn> conversation = LongConversation(exchanges);
        ConversationTurn[] original = conversation.ToArray();

        AssembledPrompt prompt = Assemble(conversation);

        Assert.InRange(prompt.CorePrompt.Length, 1, CurrentConversationProjector.CharacterBudget);
        Assert.Equal(prompt.CorePrompt.Length, prompt.SectionLengths["CurrentConversation"]);
        Assert.True(prompt.SectionLengths["CurrentConversationBeforeBudget"] > CurrentConversationProjector.CharacterBudget);
        Assert.StartsWith(Heading + Environment.NewLine + Intro + Environment.NewLine, prompt.CorePrompt);
        Assert.Contains(CompactNotice, prompt.CorePrompt);
        Assert.Contains("Farmer: " + CurrentText, prompt.CorePrompt);
        Assert.DoesNotContain(original[0].Text, prompt.CorePrompt);
        AssertSingleBoundary(prompt.CorePrompt, "conversation_history");
        Assert.Equal(original, conversation);
    }

    [Fact]
    public void ShortConversationKeepsLocalizedPrefixAndCompleteDataBoundary()
    {
        ConversationTurn[] conversation =
        [
            new("How was your day?", true, "short-player"),
            new("It was peaceful.", false, "short-npc")
        ];

        AssembledPrompt prompt = Assemble(conversation);

        string expected = Heading + Environment.NewLine + Intro + Environment.NewLine
            + PromptDataBoundary.Wrap("conversation_history", "Farmer: How was your day?\nPenny: It was peaceful.") + Environment.NewLine;
        Assert.Equal(expected, prompt.CorePrompt);
        Assert.Equal(expected.Length, prompt.SectionLengths["CurrentConversation"]);
        Assert.Equal(expected.Length, prompt.SectionLengths["CurrentConversationBeforeBudget"]);
        Assert.DoesNotContain(CompactNotice, prompt.CorePrompt);
    }

    [Fact]
    public void CompleteOverrideAtTheWrappedBudgetIsPreserved()
    {
        const string source = "third_party_section_CurrentConversation";
        int overhead = (PromptDataBoundary.Wrap(source, "x") + "\n").Length - 1;
        string replacement = new('x', CurrentConversationProjector.CharacterBudget - overhead);
        string expected = PromptDataBoundary.Wrap(source, replacement) + "\n";

        AssembledPrompt prompt = Assemble(LongConversation(40), overrides: Override(replacement));

        Assert.Equal(CurrentConversationProjector.CharacterBudget, expected.Length);
        Assert.Equal(expected, prompt.CorePrompt);
        Assert.Equal(expected.Length, prompt.SectionLengths["CurrentConversation"]);
        Assert.DoesNotContain(CurrentText, prompt.CorePrompt);
        AssertSingleBoundary(prompt.CorePrompt, "third_party_section_currentconversation");
    }

    [Fact]
    public void OverrideThatExceedsBudgetOnlyAfterWrappingFallsBackToNativeHistory()
    {
        string replacement = "OVERRIDE_START "
            + new string('x', CurrentConversationProjector.CharacterBudget - 32) + " CANCEL_OLD_PLAN";
        Assert.True(replacement.Length < CurrentConversationProjector.CharacterBudget);
        Assert.True((PromptDataBoundary.Wrap("third_party_section_CurrentConversation", replacement) + "\n").Length
            > CurrentConversationProjector.CharacterBudget);

        AssembledPrompt prompt = Assemble(LongConversation(40), overrides: Override(replacement));

        Assert.InRange(prompt.CorePrompt.Length, 1, CurrentConversationProjector.CharacterBudget);
        Assert.Equal(prompt.CorePrompt.Length, prompt.SectionLengths["CurrentConversation"]);
        Assert.Contains(CurrentText, prompt.CorePrompt);
        Assert.DoesNotContain("OVERRIDE_START", prompt.CorePrompt);
        Assert.DoesNotContain("CANCEL_OLD_PLAN", prompt.CorePrompt);
        AssertSingleBoundary(prompt.CorePrompt, "conversation_history");
    }

    [Fact]
    public void OverrideThatFitsAfterPolicyFilteringKeepsItsCompleteSafeRemainder()
    {
        string replacement = "Ridgeside Village " + new string('x', CurrentConversationProjector.CharacterBudget)
            + "\nSAFE_OVERRIDE_REMAINDER";
        string expected = global::LivingNPCs.RsvAiPolicy.RemoveBlockedLines(
            PromptDataBoundary.Wrap("third_party_section_CurrentConversation", replacement) + "\n");

        AssembledPrompt prompt = Assemble(LongConversation(40), overrides: Override(replacement));

        Assert.InRange(expected.Length, 1, CurrentConversationProjector.CharacterBudget);
        Assert.Equal(expected, prompt.CorePrompt);
        Assert.Contains("SAFE_OVERRIDE_REMAINDER", prompt.CorePrompt);
        Assert.DoesNotContain(CurrentText, prompt.CorePrompt);
        AssertSingleBoundary(prompt.CorePrompt, "third_party_section_currentconversation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedOverrideWithoutNativeHistoryCannotBypassBudgetThroughJustSpoke(bool justSpoke)
    {
        string huge = "UNBOUNDED_TEXT " + new string('x', CurrentConversationProjector.CharacterBudget);
        AssembledPrompt prompt = Assemble([], justSpoke: justSpoke, overrides: Override(huge), lookup: (key, _, _) =>
            key is "currentConversationHeading" or "currentConversationJustSpoke" ? huge : null);

        Assert.InRange(prompt.CorePrompt.Length, 0, CurrentConversationProjector.CharacterBudget);
        Assert.Equal(prompt.CorePrompt.Length, prompt.SectionLengths["CurrentConversation"]);
        Assert.DoesNotContain("UNBOUNDED_TEXT", prompt.CorePrompt);
        if (justSpoke)
        {
            Assert.Contains("The NPC has just spoken to the farmer.", prompt.CorePrompt);
        }
        else
        {
            Assert.Empty(prompt.CorePrompt);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JustSpokeKeepsItsCompleteLocalizedNoticeWhenTheHeadingIsTooLong(bool oversizedHeading)
    {
        string heading = oversizedHeading ? new string('x', CurrentConversationProjector.CharacterBudget) : Heading;

        AssembledPrompt prompt = Assemble([], justSpoke: true, lookup: (key, _, _) => key switch
        {
            "currentConversationHeading" => heading,
            "currentConversationJustSpoke" => JustSpokeNotice,
            _ => null
        });

        string expected = (oversizedHeading ? string.Empty : Heading + Environment.NewLine)
            + JustSpokeNotice + Environment.NewLine;
        Assert.Equal(expected, prompt.CorePrompt);
        Assert.Equal(expected.Length, prompt.SectionLengths["CurrentConversation"]);
    }

    [Fact]
    public async Task SuccessfulCommitPreservesAllSourceTurnsAndConversationRoutingCacheIdentity()
    {
        DialogueServices.Config!.EnableSemanticContextRouting = true;
        var router = new CountingRouter();
        LegacyLlm.Instance = router;
        var client = new CapturingClient();
        (DialogueEngine engine, DialogueHistoryStore store) = CreateEngine(client);
        List<ConversationTurn> conversation = LongConversation(80);
        ConversationTurn[] original = conversation.ToArray();
        GenerationRequest request = Request(conversation, CurrentText);

        GenerationResult first = await engine.GenerateAsync(request, CancellationToken.None);

        Assert.False(first.IsFallback);
        Assert.Single(client.Requests);
        Assert.Equal(original, request.Conversation);
        Assert.Equal(original, first.Commit!.Conversation);
        Assert.Empty(engine.History.PeekConversationContext("Penny"));
        Assert.Empty(store.GetHistory("Penny").ConversationHistory);
        Assert.True(engine.CommitResult(first));
        Assert.False(engine.CommitResult(first));

        IReadOnlyList<ConversationTurn> cached = engine.History.PeekConversationContext("Penny");
        Assert.Equal(original.Length + 1, cached.Count);
        Assert.Equal(original, cached.Take(original.Length));
        var stored = Assert.Single(store.GetHistory("Penny").ConversationHistory).Item2.ConversationElements;
        Assert.Equal(original.Length + 1, stored.Count);
        // Persistence has always generated new element IDs after the conversation's first
        // GUID. The continuation cache keeps all original IDs; stored text and roles stay whole.
        Assert.Equal(original[0].Id, stored[0].Id);
        Assert.Equal(original.Select(turn => (turn.Text, turn.IsPlayerLine)),
            stored.Take(original.Length).Select(turn => (turn.Text, turn.IsPlayerLine)));

        var nextPlayer = new ConversationTurn("It was peaceful.", true, Guid.NewGuid().ToString("N"));
        GenerationResult next = await engine.GenerateAsync(Request([nextPlayer], nextPlayer.Text), CancellationToken.None);

        Assert.False(next.IsFallback);
        Assert.Equal(1, router.Calls);
        Assert.Equal(original[0].Id, next.Commit!.Conversation[0].Id);
        Assert.Equal(original, next.Commit.Conversation.Take(original.Length));
        Assert.True(engine.CommitResult(next));
        Assert.Equal(original.Length + 3, engine.History.PeekConversationContext("Penny").Count);
        Assert.Equal(original[0].Id, engine.History.PeekConversationContext("Penny")[0].Id);
        Assert.Single(store.GetHistory("Penny").ConversationHistory);
        Assert.Equal(original, conversation);
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(client.Requests[0].StableContext, client.Requests[1].StableContext);
        Assert.Equal(client.Requests[0].NpcContext, client.Requests[1].NpcContext);
        foreach (LlmRequest sent in client.Requests)
        {
            string section = ConversationSection(sent.Tail);
            Assert.InRange(section.Length, 1, CurrentConversationProjector.CharacterBudget);
            Assert.Contains(CompactNotice, section);
            Assert.DoesNotContain(original[0].Text, section);
            AssertSingleBoundary(section, "conversation_history");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusalOmittedFromPromptStillControlsLocalInterpersonalEvidence(bool priorRefusal)
    {
        const string current = "Where is your husband?";
        List<ConversationTurn> conversation = LongConversation(80, current);
        string oldReply = (priorRefusal ? "EARLY_REFUSAL: Please leave. " : "EARLY_REPLY: It was a quiet afternoon. ")
            + new string('x', CurrentConversationProjector.CharacterBudget + 512);
        conversation.InsertRange(0,
        [
            new ConversationTurn("Tell me about yesterday.", true, "early-player"),
            new ConversationTurn(oldReply, false, "early-npc")
        ]);
        var client = new CapturingClient
        {
            Reply = """
                - He is at home.$a
                !LIVINGNPCS_META {"complete":true,"emotionImpact":{"emotion":"angry","intensityDelta":25,"apology":false,"repairDelta":0,"reason":"question about her partner crossed a boundary"},"behaviorInfluences":[{"type":"offended","summary":"annoyed by the question","durationDays":1,"intensity":30,"maxTriggers":1}],"conflicts":[{"causeKind":"boundary","summary":"asked about partner","severity":25}]}
                """
        };
        (DialogueEngine engine, _) = CreateEngine(client);

        GenerationResult result = await engine.GenerateAsync(Request(conversation, current), CancellationToken.None);

        Assert.False(result.IsFallback);
        string section = ConversationSection(Assert.Single(client.Requests).Tail);
        Assert.InRange(section.Length, 1, CurrentConversationProjector.CharacterBudget);
        Assert.DoesNotContain("EARLY_REFUSAL", section);
        Assert.DoesNotContain("EARLY_REPLY", section);
        Assert.Contains(current, section);
        Assert.Equal(oldReply, conversation[1].Text);
        Assert.Equal(oldReply, result.Commit!.Conversation[1].Text);
        ConversationAnalysis analysis = ConversationAnalysis.Parse($"!LIVINGNPCS_META {result.AnalysisJson}");
        Assert.Equal(priorRefusal ? "Angry" : "Uneasy", analysis.EmotionImpact.Emotion);
        Assert.Equal(priorRefusal ? 1 : 0, analysis.Conflicts.Count);
        Assert.Equal(priorRefusal ? 1 : 0, analysis.BehaviorInfluences.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromptProjectionDoesNotChangeTheExistingRecentActionWindows(bool recentInvitation)
    {
        const string invitation = "Would you come with me to the farm?";
        const string current = "Sounds good.";
        List<ConversationTurn> conversation = LongConversation(80, current);
        conversation.InsertRange(recentInvitation ? conversation.Count - 1 : 0,
        [
            new ConversationTurn(invitation, true, "invitation-player"),
            new ConversationTurn("I promise to think about it.", false, "invitation-npc")
        ]);
        ConversationTurn[] original = conversation.ToArray();
        string behavior = string.Join("\n",
            PromptFragments.Context.Header("Penny"),
            PromptFragments.Context.CurrentStateHeading,
            "- Scene: a quiet afternoon; location: Town.",
            PromptFragments.Context.HelpRequestReadinessLine(
                PromptFragments.Context.HelpRequestReadinessBlocked("no new favor is appropriate")),
            PromptFragments.GiftOpportunity.NoOpportunitySection());

        AssembledPrompt prompt = Assemble(conversation, request: Request(conversation, current, behavior));

        Assert.InRange(prompt.SectionLengths["CurrentConversation"], 1, CurrentConversationProjector.CharacterBudget);
        Assert.Contains(invitation, prompt.CorePrompt);
        Assert.False(prompt.ActionContract.IsFallback);
        Assert.Equal(recentInvitation, prompt.ActionContract.IncludeTravel);
        var context = new DialogueContext
        {
            ChatHistory = conversation.Select(turn => new ConversationElement(turn.Text, turn.IsPlayerLine) { Id = turn.Id }).ToList()
        };
        string? missingEffect = SceneActionContractCompleteness.FindMissingEffect(
            new(), new(), current, "Let's go.", context, []);
        Assert.Equal(recentInvitation, missingEffect?.Contains("travel", StringComparison.Ordinal) == true);
        Assert.Equal(original, conversation);
        Assert.Equal(original.Select(turn => (turn.Id, turn.Text, turn.IsPlayerLine)),
            context.ChatHistory.Select(turn => (turn.Id, turn.Text, turn.IsPlayerLine)));
    }

    private static AssembledPrompt Assemble(
        IReadOnlyList<ConversationTurn> conversation,
        bool justSpoke = false,
        IReadOnlyDictionary<string, string>? overrides = null,
        PromptTextLookup? lookup = null,
        GenerationRequest? request = null) => new PromptAssembler(new PromptAssemblyInput
    {
        // Scheduled requests omit the action-contract section so CorePrompt is exactly the
        // section under test. The engine and action-window tests use conversation requests.
        Request = request ?? new GenerationRequest { NpcName = "Penny", Trigger = GenerationTrigger.Scheduled, CurrentPlayerText = CurrentText },
        NpcName = "Penny", NpcDisplayName = "Penny", Locale = "en",
        Conversation = conversation, JustSpoke = justSpoke,
        SectionOverrides = overrides ?? new Dictionary<string, string>(),
        Lookup = lookup ?? Lookup
    }).Assemble();

    private static string? Lookup(string key, object? _, bool __) => key switch
    {
        "currentConversationHeading" => Heading,
        "currentConversationIntro" => Intro,
        "currentConversationCompacted" => CompactNotice,
        "currentConversationJustSpoke" => JustSpokeNotice,
        "generalFarmerLabel" => "Farmer",
        _ => null
    };

    private static Dictionary<string, string> Override(string text) => new() { ["CurrentConversation"] = text };

    private static List<ConversationTurn> LongConversation(int exchanges, string current = CurrentText)
    {
        var turns = new List<ConversationTurn>();
        for (int index = 0; index < exchanges; index++)
        {
            turns.Add(new ConversationTurn($"Past player line {index:D4}: " + new string('a', 140), true, $"player-{index}"));
            turns.Add(new ConversationTurn($"Past NPC line {index:D4}: " + new string('b', 140), false, $"npc-{index}"));
        }
        turns.Add(new ConversationTurn(current, true, Guid.NewGuid().ToString("N")));
        return turns;
    }

    private static GenerationRequest Request(
        IReadOnlyList<ConversationTurn> conversation, string current, string behavior = "") => new()
    {
        NpcName = "Penny", NpcDisplayName = "Penny", Trigger = GenerationTrigger.Conversation,
        Conversation = conversation, CurrentPlayerText = current, BehaviorContext = behavior,
        UsesCapturedWorldContext = true,
        Snapshot = CreateSnapshot()
    };

    private static GameStateSnapshot CreateSnapshot() => new()
    {
        FarmerName = "Farmer", FriendshipPoints = 1500, LocationName = "Town",
        Year = 3, SeasonIndex = 0, SeasonName = "spring", DayOfMonth = 14, TimeOfDay = 1200
    };

    private static (DialogueEngine Engine, DialogueHistoryStore Store) CreateEngine(CapturingClient client)
    {
        var environment = new FakePersistenceEnvironment { Now = CreateSnapshot().Now };
        var store = new DialogueHistoryStore(environment) { ArchiveSink = null };
        var engine = new DialogueEngine(new DialogueEngineServices
        {
            GetBio = _ => new NpcBio { Biography = "Penny is a quiet teacher.", ValidPortraits = new() { "h", "a" } },
            LookupPrompt = (key, _, _, _) => Lookup(key, null, false),
            GetClient = () => client,
            History = new EngineHistoryWriter(store), GetHistory = store.GetHistory,
            GetSamples = (_, _) => new Dictionary<string, string>(),
            Now = () => environment.Now,
            GetNpcDisplayName = name => name, GetLocale = () => "en"
        });
        return (engine, store);
    }

    private static string ConversationSection(string tail)
    {
        int start = tail.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(start >= 0);
        const string closing = "</untrusted_data>";
        int end = tail.IndexOf(closing, start, StringComparison.Ordinal);
        Assert.True(end > start);
        end += closing.Length;
        while (end < tail.Length && tail[end] is '\r' or '\n')
        {
            end++;
        }
        return tail[start..end];
    }

    private static void AssertSingleBoundary(string text, string source)
    {
        Assert.Contains($"<untrusted_data source=\"{source}\">", text);
        Assert.Equal(1, text.Split("<untrusted_data ", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, text.Split("</untrusted_data>", StringSplitOptions.None).Length - 1);
    }

    private sealed class CountingRouter : LegacyLlm
    {
        public int Calls { get; private set; }

        public override Task<LlmResponse> RunInference(
            string systemPromptString, string gameCacheString, string npcCacheString, string promptString,
            string responseStart = "", int n_predict = 2048, string cacheContext = "",
            bool allowRetry = true, bool disableThinking = false, CancellationToken ct = default,
            LlmOutputFormat outputFormat = LlmOutputFormat.Text)
        {
            this.Calls++;
            return Task.FromResult(new LlmResponse
            {
                IsSuccess = true,
                Text = "{\"confidence\":0.95,\"world\":\"full\",\"eventHistory\":\"none\",\"recentEvents\":\"none\"}"
            });
        }
    }

    private sealed class CapturingClient : ILlmClient
    {
        public List<LlmRequest> Requests { get; } = new();
        public string Reply { get; init; } = "- It is a peaceful day.$h\n!LIVINGNPCS_META {\"complete\":true}";
        public string ProviderId => "LocalTest";

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            this.Requests.Add(request);
            return Task.FromResult(LlmReply.Success(this.Reply, null));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            LlmReply reply = await this.CompleteAsync(request, ct);
            yield return LlmStreamEvent.Delta(reply.Text);
            yield return LlmStreamEvent.Done();
        }
    }
}
