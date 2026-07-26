using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Dialogue.Ui;
using LivingNPCs.Tests.Dialogue.Persistence;
using StardewValley;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

/// <summary>
/// 流式编排（原版呈现版）：token 进 StreamingReplyPreview 供思考窗实时显示，
/// 定稿与非流式共用 Present（原版对话框 + 打字机 + 选项），取消/结束都清空预览。
/// </summary>
[Collection("LlmLayer")]
public sealed class GenerationSchedulerStreamingTests : IDisposable
{
    private readonly StreamingClient client = new();
    private readonly DialogueHistoryStore store;
    private readonly DialogueEngine engine;

    public GenerationSchedulerStreamingTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig
        {
            EnableSemanticContextRouting = false,
            EnableLivingNpcActionDecisionPass = false,
            EnableStreamingDialogue = true,
            TypedResponses = "With Generated"
        });
        ThirdPartyContentPolicy.ResetForTests();
        LegacyLlm.Instance = new LegacyLlmDummy();
        GenerationScheduler.ResetFailureNoticeForTests();
        StreamingReplyPreview.End();

        this.store = new DialogueHistoryStore(new FakePersistenceEnvironment()) { ArchiveSink = null };
        this.engine = new DialogueEngine(new DialogueEngineServices
        {
            GetBio = _ => new NpcBio
            {
                Biography = "A biography for testing.",
                ValidPortraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "h", "s" }
            },
            LookupPrompt = (key, _, _, _) => $"[{key}]",
            GetWorldSummaryText = optimized => optimized ? "BRIEF" : "FULL",
            GetSamples = (_, _) => new Dictionary<string, string>(),
            GetClient = () => this.client,
            History = new EngineHistoryWriter(this.store),
            GetHistory = this.store.GetHistory,
            Now = () => new StardewTime(2, Season.Summer, 5, 1300),
            GetItemDisplayName = id => "Item-" + id,
            GetNpcDisplayName = name => name,
            GetLocale = () => "en",
            Random = new Random(11)
        });
    }

    public void Dispose()
    {
        GenerationScheduler.ResetFailureNoticeForTests();
        StreamingReplyPreview.End();
        LlmHudNotifier.SinkForTests = null;
        LegacyLlm.Instance = new LegacyLlmDummy();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Fact]
    public async Task Engine_Stream_Feeds_Preview_And_Finalizes_Result()
    {
        // 思考窗依赖真实游戏字典，单测无法驱动完整调度分支；此处直接驱动引擎流式接口，
        // 验证"token → 预览缓冲实时可见 → 定稿结果可交给统一 Present"这条契约。
        this.client.Deltas = new[] { "- Hel", "lo friend." };
        this.client.HoldBeforeCompleting = true;
        StreamingReplyPreview.Begin();
        var sink = new CollectingPreviewSink();

        Task run = this.engine.StreamAsync(Request("hi"), sink, CancellationToken.None);

        await this.client.TokensEmitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(StreamingReplyPreview.TryGetPreview(out string preview));
        Assert.Contains("Hel", preview);

        this.client.Release();
        await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.NotNull(sink.Result);
        Assert.False(sink.Result!.IsFallback);
        Assert.Contains("Hello friend.", sink.Result.ParsedLines[0], StringComparison.Ordinal);
        StreamingReplyPreview.End();
    }

    /// <summary>与调度器 PreviewStreamSink 同构：token 喂预览缓冲，定稿结果暂存。</summary>
    private sealed class CollectingPreviewSink : IStreamSink
    {
        public GenerationResult? Result { get; private set; }

        public void OnToken(string delta)
        {
            StreamingReplyPreview.Append(delta);
        }

        public void OnCompleted(GenerationResult result, IReadOnlyList<StreamingResponseOption> options)
        {
            this.Result = result;
        }

        public void OnFailed()
        {
            this.Result = null;
        }
    }

    [Fact]
    public void Preview_Buffer_Filters_Metadata_Truncates_And_Resets()
    {
        StreamingReplyPreview.Begin();
        StreamingReplyPreview.Append("- 你好呀");
        StreamingReplyPreview.Append("，今天天气不错。");
        Assert.True(StreamingReplyPreview.TryGetPreview(out string text));
        Assert.Contains("你好呀", text);
        Assert.DoesNotContain("- ", text, StringComparison.Ordinal);

        // 重试控制记号清空已积累的预览。
        StreamingReplyPreview.Append(StreamingControlTokens.RetryReset);
        Assert.False(StreamingReplyPreview.TryGetPreview(out _));

        // 超长截断加省略号。
        StreamingReplyPreview.Append("- " + new string('字', StreamingReplyPreview.MaxPreviewChars + 40));
        Assert.True(StreamingReplyPreview.TryGetPreview(out string truncated));
        Assert.True(truncated.Length <= StreamingReplyPreview.MaxPreviewChars + 1);
        Assert.EndsWith("…", truncated);

        // End 后不再提供预览，Append 也不再累积。
        StreamingReplyPreview.End();
        StreamingReplyPreview.Append("- late");
        Assert.False(StreamingReplyPreview.TryGetPreview(out _));
    }

    [Fact]
    public void Cancel_During_Streaming_Clears_Preview()
    {
        StreamingReplyPreview.Begin();
        StreamingReplyPreview.Append("- partial reply");
        Assert.True(StreamingReplyPreview.TryGetPreview(out _));

        var scheduler = new GenerationScheduler(this.engine, subscribeEvents: false);
        scheduler.CancelActiveGeneration();

        Assert.False(StreamingReplyPreview.TryGetPreview(out _));
        Assert.False(scheduler.IsBusy);
    }

    private static GenerationRequest Request(string playerText)
    {
        return new GenerationRequest
        {
            NpcName = "Abigail",
            Trigger = GenerationTrigger.Conversation,
            Conversation = new List<ConversationTurn>
            {
                new(playerText, true, Guid.NewGuid().ToString("N"))
            },
            Snapshot = new GameStateSnapshot { FarmerName = "Yuki" }
        };
    }

    /// <summary>流式假客户端：吐完 Deltas 后可选地挂起等 Release（观察在途预览用）。</summary>
    private sealed class StreamingClient : ILlmClient, ILlmCapabilities
    {
        private readonly SemaphoreSlim hold = new(0);

        public string ProviderId => "StreamingTest";
        public bool IsHighlySensoredModel => false;
        public string ExtraInstructions => string.Empty;

        public IReadOnlyList<string> Deltas { get; set; } = Array.Empty<string>();
        public bool HoldBeforeCompleting { get; set; }
        public TaskCompletionSource TokensEmitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release()
        {
            this.hold.Release();
        }

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            return Task.FromResult(LlmReply.Failure("non-streaming path should not be used here", 500));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (string delta in this.Deltas)
            {
                ct.ThrowIfCancellationRequested();
                yield return LlmStreamEvent.Delta(delta);
                await Task.Yield();
            }

            this.TokensEmitted.TrySetResult();

            if (this.HoldBeforeCompleting)
            {
                await this.hold.WaitAsync(ct);
            }
        }
    }
}
