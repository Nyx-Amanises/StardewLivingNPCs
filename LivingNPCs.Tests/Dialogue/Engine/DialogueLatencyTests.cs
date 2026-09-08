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

    [Fact]
    public async Task OrdinaryGreetingUsesOnlyMainAndMetadataRequests()
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

    private static GenerationRequest Request() => new()
    {
        NpcName = "Haley",
        NpcDisplayName = "Haley",
        Trigger = GenerationTrigger.Conversation,
        Conversation = new List<ConversationTurn> { new("Hello, Haley!", true, Guid.NewGuid().ToString()) },
        BehaviorContext = "[LivingNPCs]\nGift Opportunity: No gift authorized.\nHelp-request lifecycle: only act when accepted.\nSupported schema: companion_outing, helpRequests, give_small_gift.",
        Snapshot = new GameStateSnapshot { FarmerName = "Yuki", LocationName = "Town" }
    };

    private sealed class ScriptedClient : ILlmClient
    {
        private readonly LlmReply? firstReply;

        public ScriptedClient(LlmReply? firstReply = null) => this.firstReply = firstReply;

        public string ProviderId => "scripted";
        public int Calls { get; private set; }

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            this.Calls++;
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
            return Task.FromResult(new LlmResponse
            {
                IsSuccess = this.ResponseText.Length > 0,
                Text = this.ResponseText,
                ErrorMessage = this.ResponseText.Length == 0 ? "Auxiliary request failed" : string.Empty
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
