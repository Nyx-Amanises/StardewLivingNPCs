using System.Runtime.CompilerServices;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

[Collection("LlmLayer")]
public sealed class SceneActionContractGenerationTests : IDisposable
{
    public void Dispose() => RetryFixEngineHarness.Reset();

    [Fact]
    public async Task OrdinaryPreferenceTurnKeepsMemoryAndUsesOnlyOneModelRequest()
    {
        const string player = "My favorite flowers are sunflowers.";
        var client = new MainClient("""
            - I'll remember that you like sunflowers.$h
            % Thank you.
            !LIVINGNPCS_META {"complete":true,"rapportDelta":2,"memories":[{"kind":"preference","summary":"The farmer likes sunflowers.","importance":70,"playerPreference":true,"playerPreferenceKind":"liked_item_category","subject":"sunflowers","tags":["flowers"]}]}
            """);
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var auxiliary = new MetadataClient();
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request(player), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(0, auxiliary.Calls);
        Assert.NotNull(client.LastRequest);
        Assert.Contains("No action details selected", client.LastRequest!.Tail);
        Assert.DoesNotContain("\"helpRequests\"", client.LastRequest.NpcContext);
        ConversationAnalysis analysis = ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson);
        Assert.Equal(2, analysis.RapportDelta);
        Assert.Equal("sunflowers", Assert.Single(analysis.Memories).Subject);
        Assert.NotNull(result.Commit);
        Assert.Empty(store.GetHistory("Penny").ConversationHistory);
        Assert.True(engine.CommitResult(result));
        Assert.False(engine.CommitResult(result));
    }

    [Theory]
    [InlineData("{\"complete\":false}")]
    [InlineData("{\"complete\":true")]
    public async Task IncompleteSceneClassificationUsesTheExistingFullClassifier(string metadata)
    {
        var client = new MainClient("- Good morning!$h\n!LIVINGNPCS_META " + metadata);
        var (engine, store) = RetryFixEngineHarness.Create(client);
        var auxiliary = new MetadataClient();
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request("Good morning!"), CancellationToken.None);

        Assert.False(result.IsFallback);
        Assert.Equal(1, client.Calls);
        Assert.Equal(1, auxiliary.Calls);
        Assert.Contains("No action details selected", client.LastRequest!.Tail);
        Assert.Contains("\"helpRequests\"", auxiliary.Prompt);
        Assert.Contains("\"helpRequestUpdates\"", auxiliary.Prompt);
        Assert.Contains("\"travelDecision\"", auxiliary.Prompt);
        Assert.Contains("\"giftDecision\"", auxiliary.Prompt);
        Assert.Contains("give_money", auxiliary.Prompt);
        Assert.Contains("festival_interaction", auxiliary.Prompt);
        Assert.Contains("Good morning!", auxiliary.Prompt);
        Assert.Equal(2, ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).RapportDelta);
        Assert.NotNull(result.Commit);
        Assert.Empty(store.GetHistory("Penny").ConversationHistory);
    }

    [Fact]
    public async Task UnexpectedVisibleGiftCannotPassAnEmptySceneCertificate()
    {
        var client = new MainClient("- This is for you. Please take this leek.$h\n!LIVINGNPCS_META {\"complete\":true}");
        var (engine, _) = RetryFixEngineHarness.Create(client);
        var auxiliary = new MetadataClient();
        LegacyLlm.Instance = auxiliary;

        GenerationResult result = await engine.GenerateAsync(Request("Good morning!"), CancellationToken.None);

        Assert.Equal(1, auxiliary.Calls);
        Assert.Contains("No action details selected", client.LastRequest!.Tail);
        Assert.Contains("This is for you", auxiliary.Prompt);
        Assert.Empty(ConversationAnalysis.Parse("!LIVINGNPCS_META " + result.AnalysisJson).Actions);
    }

    private static GenerationRequest Request(string player) => new()
    {
        NpcName = "Penny",
        NpcDisplayName = "Penny",
        Trigger = GenerationTrigger.Conversation,
        CurrentPlayerText = player,
        Conversation = new[] { new ConversationTurn(player, true, "current-turn") },
        BehaviorContext = SceneMetadataContractTests.RestrictedContext,
        Snapshot = new GameStateSnapshot { FarmerName = "Farmer", LocationName = "Town" }
    };

    private sealed class MainClient(string response) : ILlmClient
    {
        public string ProviderId => "scene-contract-test";
        public int Calls { get; private set; }
        public LlmRequest? LastRequest { get; private set; }

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            this.Calls++;
            this.LastRequest = request;
            return Task.FromResult(LlmReply.Success(response, null));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            LlmReply reply = await this.CompleteAsync(request, ct);
            yield return LlmStreamEvent.Delta(reply.Text);
            yield return LlmStreamEvent.Done();
        }
    }

    private sealed class MetadataClient : LegacyLlm
    {
        public int Calls { get; private set; }
        public string Prompt { get; private set; } = string.Empty;

        public override Task<LlmResponse> RunInference(
            string systemPromptString, string gameCacheString, string npcCacheString, string promptString,
            string responseStart = "", int n_predict = 2048, string cacheContext = "", bool allowRetry = true,
            bool disableThinking = false, CancellationToken ct = default,
            LlmOutputFormat outputFormat = LlmOutputFormat.Text, TimeSpan? timeoutOverride = null)
        {
            ct.ThrowIfCancellationRequested();
            this.Calls++;
            this.Prompt = promptString;
            return Task.FromResult(new LlmResponse
            {
                IsSuccess = true,
                Text = "!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":2}"
            });
        }
    }
}
