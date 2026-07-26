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
using LivingNPCs.Tests.Dialogue.Persistence;
using StardewValley;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

/// <summary>生成失败时的游戏内 HUD 提示：占位行"..."不再静默出现（含 20 秒节流）。</summary>
[Collection("LlmLayer")]
public sealed class GenerationSchedulerFailureNoticeTests : IDisposable
{
    private readonly FailingClient client = new();
    private readonly DialogueHistoryStore store;
    private readonly DialogueEngine engine;

    public GenerationSchedulerFailureNoticeTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig
        {
            EnableSemanticContextRouting = false,
            EnableLivingNpcActionDecisionPass = false,
            TypedResponses = "With Generated"
        });
        ThirdPartyContentPolicy.ResetForTests();
        LegacyLlm.Instance = new LegacyLlmDummy();
        GenerationScheduler.ResetFailureNoticeForTests();

        this.store = new DialogueHistoryStore(new FakePersistenceEnvironment()) { ArchiveSink = null };
        this.engine = new DialogueEngine(new DialogueEngineServices
        {
            GetBio = _ => new NpcBio { Biography = "A biography for testing." },
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
            Random = new Random(7)
        });
    }

    public void Dispose()
    {
        LlmHudNotifier.SinkForTests = null;
        GenerationScheduler.ResetFailureNoticeForTests();
        LegacyLlm.Instance = new LegacyLlmDummy();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Fact]
    public async Task Fallback_Result_Shows_Throttled_Hud_Notice()
    {
        var hud = new List<string>();
        LlmHudNotifier.SinkForTests = hud.Add;
        var drawn = new List<string>();
        var scheduler = new GenerationScheduler(
            this.engine,
            subscribeEvents: false,
            drawOverride: (_, _, text) =>
            {
                drawn.Add(text);
                return true;
            });

        Assert.True(scheduler.Enqueue(null!, Request("hello")));
        scheduler.OnUpdateTicked();
        Task worker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose its active worker");
        await worker.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(this.client.Calls > 0);
        Assert.Contains(drawn, line => line.Contains(EngineConstants.FallbackLine, StringComparison.Ordinal));
        string notice = Assert.Single(hud);
        Assert.Contains("failed to generate", notice, StringComparison.OrdinalIgnoreCase);

        // 20 秒节流窗口内的第二次失败不再重复弹 HUD。
        Assert.True(scheduler.Enqueue(null!, Request("hello again")));
        scheduler.OnUpdateTicked();
        Task second = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose its second worker");
        await second.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Single(hud);
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

    private sealed class FailingClient : ILlmClient, ILlmCapabilities
    {
        private int calls;

        public string ProviderId => "FailingTest";
        public bool IsHighlySensoredModel => false;
        public string ExtraInstructions => string.Empty;

        public int Calls => Volatile.Read(ref this.calls);

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref this.calls);
            return Task.FromResult(LlmReply.Failure("simulated outage", 500));
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
