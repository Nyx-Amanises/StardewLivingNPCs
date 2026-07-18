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

[Collection("LlmLayer")]
public sealed class GenerationSchedulerCancellationTests : IDisposable
{
    private readonly DelayedClient client = new();
    private readonly DialogueHistoryStore store;
    private readonly DialogueEngine engine;

    public GenerationSchedulerCancellationTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig
        {
            EnableSemanticContextRouting = false,
            EnableLivingNpcActionDecisionPass = false,
            TypedResponses = "With Generated"
        });
        ThirdPartyContentPolicy.ResetForTests();
        LegacyLlm.Instance = new LegacyLlmDummy();

        this.store = new DialogueHistoryStore(new FakePersistenceEnvironment()) { ArchiveSink = null };
        this.engine = new DialogueEngine(new DialogueEngineServices
        {
            GetBio = _ => new NpcBio
            {
                Biography = "A biography for testing.",
                ValidPortraits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "h", "s", "l", "a" }
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
            Random = new Random(3)
        });
    }

    public void Dispose()
    {
        DialogueEngine.RecordExchangeCallback = null;
        LegacyLlm.Instance = new LegacyLlmDummy();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Fact]
    public void Esc_Before_Start_Drops_Pending_Request()
    {
        var drawn = new List<string>();
        var scheduler = new GenerationScheduler(
            this.engine,
            subscribeEvents: false,
            drawOverride: (_, _, text) =>
            {
                drawn.Add(text);
                return true;
            });

        Assert.True(scheduler.Enqueue(null!, Request("pending")));
        Assert.True(scheduler.IsBusy);

        scheduler.CancelActiveGeneration();
        scheduler.OnUpdateTicked();

        Assert.False(scheduler.IsBusy);
        Assert.Null(scheduler.ActiveTaskForTests);
        Assert.Empty(drawn);
        Assert.Empty(this.store.GetHistory("Abigail").ConversationHistory);
        Assert.Empty(this.engine.History.PeekConversationContext("Abigail"));
    }

    [Fact]
    public async Task Esc_Drops_Late_Success_Without_Drawing_Committing_Or_Caching()
    {
        var drawn = new List<string>();
        int callbackCalls = 0;
        DialogueEngine.RecordExchangeCallback = (_, _, _, _) => callbackCalls++;
        var scheduler = new GenerationScheduler(
            this.engine,
            subscribeEvents: false,
            drawOverride: (_, _, text) =>
            {
                drawn.Add(text);
                return true;
            });

        Assert.True(scheduler.Enqueue(null!, Request("before Esc")));
        scheduler.OnUpdateTicked();
        DelayedClient.PendingCall call = await this.client.WaitForCallAsync();
        Task worker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose its active worker");

        scheduler.CancelActiveGeneration();
        Assert.True(call.Token.IsCancellationRequested);

        // The provider intentionally ignores cancellation and completes successfully.
        call.Complete("late response");
        await worker.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(drawn);
        Assert.Equal(0, callbackCalls);
        Assert.Empty(this.store.GetHistory("Abigail").ConversationHistory);
        Assert.Empty(this.engine.History.PeekConversationContext("Abigail"));
    }

    [Fact]
    public async Task Canceled_Old_Save_Cannot_Commit_After_New_Save_Starts()
    {
        var drawn = new List<string>();
        var callbacks = new List<string>();
        DialogueEngine.RecordExchangeCallback = (_, player, _, _) => callbacks.Add(player);
        var scheduler = new GenerationScheduler(
            this.engine,
            subscribeEvents: false,
            drawOverride: (_, _, text) =>
            {
                drawn.Add(text);
                return true;
            });

        Assert.True(scheduler.Enqueue(null!, Request("old save")));
        scheduler.OnUpdateTicked();
        DelayedClient.PendingCall oldCall = await this.client.WaitForCallAsync();
        Task oldWorker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose old worker");

        scheduler.CancelActiveGeneration();
        this.store.OnReturnedToTitle();
        this.engine.History.ClearContext();

        Assert.True(scheduler.Enqueue(null!, Request("new save")));
        scheduler.OnUpdateTicked();
        DelayedClient.PendingCall newCall = await this.client.WaitForCallAsync();
        Task newWorker = scheduler.ActiveTaskForTests
            ?? throw new InvalidOperationException("scheduler did not expose new worker");

        newCall.Complete("new save response");
        await newWorker.WaitAsync(TimeSpan.FromSeconds(5));

        oldCall.Complete("old save response");
        await oldWorker.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(drawn);
        Assert.Contains("new save response", drawn[0], StringComparison.Ordinal);
        Assert.Single(callbacks);
        Assert.Equal("new save", callbacks[0]);

        var elements = this.store.GetHistory("Abigail")
            .ConversationHistory[0]
            .Item2
            .ConversationElements;
        Assert.Contains(elements, element => element.Text == "new save");
        Assert.Contains(elements, element => element.Text == "new save response");
        Assert.DoesNotContain(elements, element => element.Text == "old save");
        Assert.DoesNotContain(elements, element => element.Text == "old save response");
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

    private sealed class DelayedClient : ILlmClient, ILlmCapabilities
    {
        private readonly object gate = new();
        private readonly Queue<PendingCall> pending = new();
        private readonly SemaphoreSlim available = new(0);

        public string ProviderId => "DelayedTest";
        public bool IsHighlySensoredModel => false;
        public string ExtraInstructions => string.Empty;

        public Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            var call = new PendingCall(ct);
            lock (this.gate)
            {
                this.pending.Enqueue(call);
            }

            this.available.Release();
            return call.Completion.Task;
        }

        public async Task<PendingCall> WaitForCallAsync()
        {
            if (!await this.available.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("timed out waiting for LLM call");
            }

            lock (this.gate)
            {
                return this.pending.Dequeue();
            }
        }

        public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(
            LlmRequest request,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public sealed class PendingCall
        {
            internal PendingCall(CancellationToken token)
            {
                this.Token = token;
            }

            internal CancellationToken Token { get; }
            internal TaskCompletionSource<LlmReply> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal void Complete(string line)
            {
                this.Completion.TrySetResult(LlmReply.Success($"- {line}", null));
            }
        }
    }
}
