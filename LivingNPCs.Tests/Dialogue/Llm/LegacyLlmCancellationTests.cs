using System;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class LegacyLlmCancellationTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"cancel test\"}}]}";

    [Fact]
    public async Task LegacyBridge_ForwardsCanceledCallerToken_WithoutStartingHttp()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(CompletionJson);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LegacyLlm.Instance.RunInference(
                "system",
                string.Empty,
                string.Empty,
                "prompt",
                allowRetry: false,
                ct: cancellation.Token));

        Assert.Empty(Http.Requests);
    }

    [Fact]
    public async Task MetadataExtraction_RethrowsCallerCancellation_InsteadOfReturningFailure()
    {
        var blocking = new BlockingLegacyLlm();
        LegacyLlm.Instance = blocking;
        using var cancellation = new CancellationTokenSource();

        Task<LivingNpcMetadataExtractionResult> extraction = LivingNpcMetadataExtractionPass.TryExtractAsync(
            new Character("Abigail"),
            new DialogueContext
            {
                Location = "Town",
                TimeOfDay = "1200",
                LivingNpcExtraPrompt = "metadata context"
            },
            "player text",
            "NPC reply",
            Array.Empty<string>(),
            cancellation.Token);

        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(blocking.SeenToken.CanBeCanceled);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await extraction);
    }

    private sealed class BlockingLegacyLlm : LegacyLlm
    {
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken SeenToken { get; private set; }

        public override async Task<LlmResponse> RunInference(
            string systemPromptString,
            string gameCacheString,
            string npcCacheString,
            string promptString,
            string responseStart = "",
            int n_predict = 2048,
            string cacheContext = "",
            bool allowRetry = true,
            bool disableThinking = false,
            CancellationToken ct = default)
        {
            this.SeenToken = ct;
            this.Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new LlmResponse { IsSuccess = false };
        }
    }
}
