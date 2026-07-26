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

/// <summary>流式编排：自由输入会话走流式窗（逐 token → 定稿 → 提交），取消关窗，开关关闭回经典路径。</summary>
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
        GenerationScheduler.StreamingPresenterFactoryForTests = null;
        GenerationScheduler.ResetFailureNoticeForTests();
        LlmHudNotifier.SinkForTests = null;
        LegacyLlm.Instance = new LegacyLlmDummy();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Fact]
    public async Task Conversation_Request_Streams_Tokens_And_Completes_In_Window()
    {
        var presenter = new FakePresenter();
        GenerationScheduler.StreamingPresenterFactoryForTests = (_, _) => presenter;
        this.client.Deltas = new[] { "- Hel", "lo friend." };
        var scheduler = new GenerationScheduler(this.engine, subscribeEvents: false);

        Assert.True(scheduler.Enqueue(new NPC(), Request("hi")));
        scheduler.OnUpdateTicked();
        Task worker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose its streaming worker");
        await worker.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(new[] { "- Hel", "lo friend." }, presenter.Tokens);
        Assert.NotNull(presenter.Completed);
        Assert.False(presenter.Completed!.IsFallback);
        Assert.Contains("Hello friend.", presenter.Completed.ParsedLines[0]);
        Assert.False(scheduler.IsBusy);

        // 定稿即提交：NPC 台词进入会话上下文，后续轮次可引用。
        Assert.NotEmpty(this.engine.History.PeekConversationContext("Abigail"));

        // 窗结束回调触发关闭。
        presenter.OnFinished!.Invoke();
        Assert.True(presenter.Closed);
    }

    [Fact]
    public async Task Streaming_Failure_Falls_Back_With_Hud_Notice()
    {
        var hud = new List<string>();
        LlmHudNotifier.SinkForTests = hud.Add;
        var presenter = new FakePresenter();
        GenerationScheduler.StreamingPresenterFactoryForTests = (_, _) => presenter;
        this.client.Deltas = Array.Empty<string>();
        var scheduler = new GenerationScheduler(this.engine, subscribeEvents: false);

        Assert.True(scheduler.Enqueue(new NPC(), Request("hi")));
        scheduler.OnUpdateTicked();
        Task worker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose its streaming worker");
        await worker.WaitAsync(TimeSpan.FromSeconds(20));

        // 空流 → 引擎兜底占位行仍在窗内定稿，并给游戏内失败提示。
        Assert.NotNull(presenter.Completed);
        Assert.True(presenter.Completed!.IsFallback);
        Assert.Single(hud);
    }

    [Fact]
    public void Cancel_During_Streaming_Closes_Window()
    {
        var presenter = new FakePresenter();
        GenerationScheduler.StreamingPresenterFactoryForTests = (_, _) => presenter;
        this.client.HoldOpen = true;
        var scheduler = new GenerationScheduler(this.engine, subscribeEvents: false);

        Assert.True(scheduler.Enqueue(new NPC(), Request("hi")));
        scheduler.OnUpdateTicked();

        scheduler.CancelActiveGeneration();

        Assert.True(presenter.Closed);
        Assert.False(scheduler.IsBusy);
        this.client.Release();
    }

    [Fact]
    public async Task Streaming_Disabled_Uses_Classic_Path()
    {
        DialogueServices.Config.EnableStreamingDialogue = false;
        bool presenterCreated = false;
        GenerationScheduler.StreamingPresenterFactoryForTests = (_, _) =>
        {
            presenterCreated = true;
            return new FakePresenter();
        };
        this.client.Deltas = new[] { "- Hello." };
        var drawn = new List<string>();
        var scheduler = new GenerationScheduler(
            this.engine,
            subscribeEvents: false,
            drawOverride: (_, _, text) =>
            {
                drawn.Add(text);
                return true;
            });

        // 经典路径在测试环境用 null NPC（与取消测试一致：ThinkingDialogueController 依赖游戏字典）。
        Assert.True(scheduler.Enqueue(null!, Request("hi")));
        scheduler.OnUpdateTicked();
        Task worker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose its worker");
        await worker.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.False(presenterCreated);
        Assert.NotEmpty(drawn);
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

    private sealed class FakePresenter : IStreamingDialoguePresenter
    {
        public List<string> Tokens { get; } = new();
        public GenerationResult? Completed { get; private set; }
        public Action? OnFinished { get; private set; }
        public bool Closed { get; private set; }

        public void AppendToken(string token)
        {
            lock (this.Tokens)
            {
                this.Tokens.Add(token);
            }
        }

        public void Complete(
            GenerationResult result,
            IReadOnlyList<StreamingResponseOption> options,
            Action<StreamingResponseOption> onResponseSelected,
            Action onFinished)
        {
            this.Completed = result;
            this.OnFinished = onFinished;
        }

        public void Close()
        {
            this.Closed = true;
        }
    }

    /// <summary>流式假客户端：按 Deltas 逐段吐 token；HoldOpen 时挂起直到 Release（用于取消测试）。</summary>
    private sealed class StreamingClient : ILlmClient, ILlmCapabilities
    {
        private readonly SemaphoreSlim hold = new(0);

        public string ProviderId => "StreamingTest";
        public bool IsHighlySensoredModel => false;
        public string ExtraInstructions => string.Empty;

        public IReadOnlyList<string> Deltas { get; set; } = Array.Empty<string>();
        public bool HoldOpen { get; set; }

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
            if (this.HoldOpen)
            {
                await this.hold.WaitAsync(ct);
            }

            foreach (string delta in this.Deltas)
            {
                ct.ThrowIfCancellationRequested();
                yield return LlmStreamEvent.Delta(delta);
                await Task.Yield();
            }
        }
    }
}
