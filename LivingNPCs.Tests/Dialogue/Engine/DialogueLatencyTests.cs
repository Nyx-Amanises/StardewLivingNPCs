using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

[Collection("LlmLayer")]
public sealed class DialogueLatencyTests : IDisposable
{
    public void Dispose() => RetryFixEngineHarness.Reset();

    [Fact]
    public async Task TerminalProviderFailureDoesNotRestartTheSameGeneration()
    {
        var usage = new TokenUsage { PromptTokens = 100, CompletionTokens = 2048, ReasoningTokens = 2048 };
        var client = new ScriptedClient(LlmReply.Failure("Output budget exhausted", 200, retryable: false, usage));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var auxiliary = new AuxiliaryClient { ResponseText = "!LIVINGNPCS_META {\"complete\":true}" };
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(1, client.Calls);
        Assert.Equal(0, auxiliary.Calls);
        Assert.True(result.IsFallback);
        Assert.Equal(2048, result.Usage.ReasoningTokens);
        Assert.Null(result.Commit);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
    }

    [Fact]
    public async Task TerminalStreamFailureDoesNotRestartAndKeepsBilledUsage()
    {
        var usage = new TokenUsage { PromptTokens = 100, CompletionTokens = 512, ReasoningTokens = 512 };
        var client = new ScriptedClient(LlmReply.Failure("Output budget exhausted", 200, retryable: false, usage));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var sink = new RecordingSink();

        await engine.StreamAsync(Request(), sink, CancellationToken.None);

        Assert.Equal(1, client.Calls);
        Assert.Empty(sink.Tokens);
        Assert.NotNull(sink.Result);
        Assert.True(sink.Result!.IsFallback);
        Assert.Equal(512, sink.Result.Usage.ReasoningTokens);
        Assert.Null(sink.Result.Commit);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
    }

    [Fact]
    public async Task TransientProviderFailureStillRetries()
    {
        var client = new ScriptedClient(LlmReply.Failure("Temporary provider outage", 503));
        var (engine, _) = RetryFixEngineHarness.Create(client);

        GenerationResult result = await engine.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(2, client.Calls);
        Assert.False(result.IsFallback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteInlineMetadataUsesOnlyTheMainRequestAndDefersCommit(bool gift)
    {
        var client = new ScriptedClient(LlmReply.Success(
            "- Hello! The weather is pleasant today.$h\n%It is.\n!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":1}", null));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        DialogueServices.Config.EnableLivingNpcActionDecisionPass = true;
        var auxiliary = new AuxiliaryClient();
        LegacyLlm.Instance = auxiliary;
        int recordedExchanges = 0;
        DialogueEngine.RecordExchangeCallback = (_, _, _, _) => recordedExchanges++;
        try
        {
            GenerationResult result = await engine.GenerateAsync(
                Request(gift ? GenerationTrigger.Gift : GenerationTrigger.Conversation), CancellationToken.None);

            Assert.False(result.IsFallback);
            Assert.Equal(1, client.Calls);
            Assert.Equal(0, auxiliary.Calls);
            Assert.NotNull(client.LastRequest);
            Assert.False(client.LastRequest!.DisableThinking);
            Assert.Contains("!LIVINGNPCS_META", client.LastRequest.ConcatenatedUserContent());
            Assert.DoesNotContain("[instructionsDialogueOnly]", client.LastRequest.ConcatenatedUserContent());
            Assert.Equal(new[] { "Hello! The weather is pleasant today.$h", "It is." }, result.ParsedLines);
            Assert.DoesNotContain("!LIVINGNPCS_META", result.FormattedLine);
            Assert.DoesNotContain("\"complete\"", result.AnalysisJson);
            Assert.Equal(1, ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).RapportDelta);
            Assert.NotNull(result.Commit);
            Assert.Equal(0, recordedExchanges);
            Assert.Empty(store.GetHistory("Haley").ConversationHistory);

            Assert.True(engine.CommitResult(result));
            Assert.False(engine.CommitResult(result));
            Assert.Equal(1, recordedExchanges);
        }
        finally
        {
            DialogueEngine.RecordExchangeCallback = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonInteractiveTurnsKeepDialogueOnlyInstructions(bool opening)
    {
        var client = new ScriptedClient();
        var (engine, _) = RetryFixEngineHarness.Create(client);
        var auxiliary = new AuxiliaryClient();
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(new GenerationRequest
        {
            NpcName = "Haley",
            Trigger = opening ? GenerationTrigger.ConversationOpening : GenerationTrigger.Scheduled,
            Snapshot = new GameStateSnapshot { FarmerName = "Yuki", LocationName = "Town" }
        }, CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(0, auxiliary.Calls);
        Assert.NotNull(client.LastRequest);
        Assert.Contains("[instructionsDialogueOnly]", client.LastRequest!.ConcatenatedUserContent());
        Assert.DoesNotContain("!LIVINGNPCS_META", client.LastRequest.ConcatenatedUserContent());
    }

    [Fact]
    public async Task MissingInlineMetadataFallsBackToOneClassifierRequest()
    {
        var client = new ScriptedClient();
        var (engine, store) = RetryFixEngineHarness.Create(client);
        DialogueServices.Config.EnableSemanticContextRouting = true;
        DialogueServices.Config.EnableLivingNpcActionDecisionPass = true;
        var auxiliary = new AuxiliaryClient { ResponseText = "!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":1}" };
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request(), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        Assert.Contains("metadata classifier", auxiliary.SystemPrompt);
        Assert.Equal(1, ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).RapportDelta);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
        Assert.NotNull(result.Commit);
    }

    [Theory]
    [InlineData("!LIVINGNPCS_META {\"rapportDelta\":9}")]
    [InlineData("!LIVINGNPCS_META {\"complete\":false,\"rapportDelta\":9}")]
    [InlineData("!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":9,\"rapportDelta\":12}")]
    [InlineData("!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":\"nine\"}")]
    [InlineData("!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":9")]
    [InlineData("!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":9}\nAdditional explanation")]
    [InlineData("!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":9}\n!LIVINGNPCS_META {\"complete\":true}")]
    public async Task InvalidInlineEnvelopeCannotBypassTheClassifier(string metadata)
    {
        var client = new ScriptedClient(LlmReply.Success("- Hello!$h\n%Hi!\n" + metadata, null));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var auxiliary = new AuxiliaryClient { ResponseText = "!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":1}" };
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request(), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        Assert.Contains("metadata classifier", auxiliary.SystemPrompt);
        Assert.Equal(1, ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).RapportDelta);
        Assert.DoesNotContain("!LIVINGNPCS_META", result.FormattedLine);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
    }

    [Fact]
    public async Task PreferenceBelowPersistenceThresholdFallsBackWithoutSilentlyLosingTheMemory()
    {
        var client = new ScriptedClient(LlmReply.Success(
            "- I'll remember that you like sunflowers.\n!LIVINGNPCS_META {\"complete\":true,\"memories\":[{\"kind\":\"preference\",\"summary\":\"The farmer likes sunflowers.\",\"importance\":3,\"playerPreference\":true,\"playerPreferenceKind\":\"liked_item_category\",\"subject\":\"sunflowers\"}]}", null));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var auxiliary = new AuxiliaryClient
        {
            ResponseText = "!LIVINGNPCS_META {\"complete\":true,\"memories\":[{\"kind\":\"preference\",\"summary\":\"The farmer likes sunflowers.\",\"importance\":65,\"playerPreference\":true,\"playerPreferenceKind\":\"liked_item_category\",\"subject\":\"sunflowers\"}]}"
        };
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(
            Request(playerText: "Sunflowers are my favorite flowers."), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        ConversationMemoryCandidate memory = Assert.Single(ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).Memories);
        Assert.True(memory.PlayerPreference);
        Assert.Equal(65, memory.Importance);
        Assert.Equal("sunflowers", memory.Subject);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
    }

    [Fact]
    public async Task MissingPromisedGiftFallsBackToClassifierBeforeTheReplyCanBeCommitted()
    {
        var client = new ScriptedClient(LlmReply.Success(
            "- Take this leek.\n!LIVINGNPCS_META {\"complete\":true}", null));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var auxiliary = new AuxiliaryClient
        {
            ResponseText = "!LIVINGNPCS_META {\"complete\":true,\"giftDecision\":{\"isGiftReply\":true,\"timing\":\"now\",\"tier\":\"small\",\"itemId\":\"(O)20\",\"itemLabel\":\"leek\"}}"
        };
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request(), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        ConversationWorldActionRequest action = Assert.Single(ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).Actions);
        Assert.Equal("give_small_gift", action.Type);
        Assert.Equal("(O)20", action.ItemId);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
        Assert.NotNull(result.Commit);
    }

    [Fact]
    public async Task FailedClassifierStillRecoversAnExplicitTravelCommitment()
    {
        var client = new ScriptedClient(LlmReply.Success("- Yes, let's go to the beach now.$h\n%Let's go!", null));
        var (engine, store) = RetryFixEngineHarness.Create(client);
        DialogueServices.Config.EnableLivingNpcActionDecisionPass = true;
        var auxiliary = new AuxiliaryClient
        {
            ResponseForCall = call => call == 1
                ? string.Empty
                : "!LIVINGNPCS_META {\"actions\":[{\"type\":\"companion_outing\",\"targetLocation\":\"Beach\",\"travelConsent\":\"accepted_now\",\"durationMinutes\":60}]}"
        };
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(
            Request(playerText: "Let's go to the beach together now."), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(2, auxiliary.Calls);
        Assert.Contains("action metadata", auxiliary.SystemPrompt);
        var analysis = ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson);
        Assert.Equal("Beach", Assert.Single(analysis.Actions).TargetLocation);
        Assert.True(result.EndConversation);
        Assert.Single(result.ParsedLines);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
        Assert.NotNull(result.Commit);
    }

    [Fact]
    public async Task FailedMetadataOnSmallTalkDoesNotStartActionRecoveryFromRuleText()
    {
        var client = new ScriptedClient();
        var (engine, _) = RetryFixEngineHarness.Create(client);
        DialogueServices.Config.EnableLivingNpcActionDecisionPass = true;
        var auxiliary = new AuxiliaryClient();
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request(), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        Assert.Empty(ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).Actions);
    }

    [Fact]
    public async Task CancelDuringMetadataDoesNotReturnACommitOrWriteHistory()
    {
        var client = new ScriptedClient();
        var (engine, store) = RetryFixEngineHarness.Create(client);
        using var cancellation = new CancellationTokenSource();
        var auxiliary = new AuxiliaryClient { OnRequest = cancellation.Cancel };
        LegacyLlm.Instance = auxiliary;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GenerateAsync(Request(), cancellation.Token));

        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        Assert.Empty(store.GetHistory("Haley").ConversationHistory);
    }

    private static GenerationRequest Request(
        GenerationTrigger trigger = GenerationTrigger.Conversation,
        string playerText = "Hello, Haley!") => new()
    {
        NpcName = "Haley",
        NpcDisplayName = "Haley",
        Trigger = trigger,
        GiftItemId = trigger == GenerationTrigger.Gift ? "72" : string.Empty,
        GiftTaste = 0,
        Conversation = new List<ConversationTurn> { new(playerText, true, Guid.NewGuid().ToString()) },
        BehaviorContext = "[LivingNPCs]\nGift Opportunity: No gift authorized.\nHelp-request lifecycle: only act when accepted.\nSupported schema: companion_outing, helpRequests, give_small_gift.",
        Snapshot = new GameStateSnapshot { FarmerName = "Yuki", LocationName = "Town" }
    };

    private sealed class ScriptedClient : ILlmClient
    {
        private readonly LlmReply? firstReply;

        public ScriptedClient(LlmReply? firstReply = null) => this.firstReply = firstReply;

        public string ProviderId => "scripted";
        public int Calls { get; private set; }
        public LlmRequest? LastRequest { get; private set; }

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            this.Calls++;
            this.LastRequest = request;
            return Task.FromResult(this.Calls == 1 && this.firstReply != null
                ? this.firstReply
                : LlmReply.Success("- Hello! The weather is pleasant today.$h\n%It is.", null));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            LlmReply reply = await this.CompleteAsync(request, ct);
            if (!reply.IsSuccess)
            {
                throw new LlmStreamException(reply.ErrorMessage, reply.HttpStatus, reply.Retryable, reply.Usage);
            }

            yield return LlmStreamEvent.Delta(reply.Text);
            yield return LlmStreamEvent.Done();
        }
    }

    private sealed class AuxiliaryClient : LegacyLlm
    {
        public string ResponseText { get; init; } = string.Empty;
        public Func<int, string>? ResponseForCall { get; init; }
        public string SystemPrompt { get; private set; } = string.Empty;
        public int Calls { get; private set; }
        public Action? OnRequest { get; init; }

        public override Task<LlmResponse> RunInference(
            string systemPromptString, string gameCacheString, string npcCacheString, string promptString,
            string responseStart = "", int n_predict = 2048, string cacheContext = "", bool allowRetry = true,
            bool disableThinking = false, CancellationToken ct = default)
        {
            this.Calls++;
            this.SystemPrompt = systemPromptString;
            this.OnRequest?.Invoke();
            ct.ThrowIfCancellationRequested();
            string responseText = this.ResponseForCall?.Invoke(this.Calls) ?? this.ResponseText;
            return Task.FromResult(new LlmResponse
            {
                IsSuccess = responseText.Length > 0,
                Text = responseText,
                ErrorMessage = responseText.Length == 0 ? "Auxiliary request failed" : string.Empty
            });
        }
    }

    private sealed class RecordingSink : IStreamSink
    {
        public List<string> Tokens { get; } = new();
        public GenerationResult? Result { get; private set; }
        public void OnToken(string delta) => this.Tokens.Add(delta);
        public void OnCompleted(GenerationResult result, IReadOnlyList<StreamingResponseOption> options) => this.Result = result;
        public void OnFailed() { }
    }
}
