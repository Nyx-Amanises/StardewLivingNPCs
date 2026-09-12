using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using StardewModdingAPI;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class GiftMailTimeoutTests : LlmTestBase
{
    private const string GeneratedProse = "Thank you for the tea. I hope you enjoy this little gift.";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(2, 5)]
    [InlineData(85, 85)]
    [InlineData(180, 180)]
    [InlineData(999, 180)]
    public async Task MailBudgetReachesTheModelIndependentlyOfDialogueTimeout(int configured, int expected)
    {
        Config.QueryTimeout = 5;
        var clock = new ManualDeadlineScheduler();
        var model = new ControlledLegacyLlm();
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator(clock.Schedule);

        Task<string?> generation = generator.GenerateAsync(Mail(configured), CancellationToken.None);
        PendingCall call = await model.NextCallAsync();

        Assert.Equal(TimeSpan.FromSeconds(expected), Assert.Single(clock.Durations));
        Assert.Equal(TimeSpan.FromSeconds(expected), call.TimeoutOverride);
        Assert.Equal(10_000, call.MaxTokens);
        Assert.Equal(LlmOutputFormat.Text, call.OutputFormat);
        Assert.False(call.AllowRetry);
        Assert.True(call.DisableThinking);

        call.Complete();

        Assert.Equal("@,^" + GeneratedProse, await generation.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task QueuedMailGetsItsFullBudgetOnlyAfterAConcurrentSlotIsFree()
    {
        var clock = new ManualDeadlineScheduler();
        var model = new ControlledLegacyLlm();
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator(clock.Schedule);

        Task<string?> first = generator.GenerateAsync(Mail(180), CancellationToken.None);
        PendingCall firstCall = await model.NextCallAsync();
        Task<string?> second = generator.GenerateAsync(Mail(180), CancellationToken.None);
        PendingCall secondCall = await model.NextCallAsync();
        Task<string?> queued = generator.GenerateAsync(Mail(5), CancellationToken.None);

        Assert.Equal(2, model.CallCount);
        Assert.Equal(2, clock.Durations.Count);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(queued.IsCompleted);
        Assert.Equal(2, model.CallCount);
        Assert.Equal(2, clock.Durations.Count);

        firstCall.Complete();
        PendingCall queuedCall = await model.NextCallAsync();

        Assert.Equal(TimeSpan.FromSeconds(5), queuedCall.TimeoutOverride);
        Assert.Equal(new[] { 180d, 180d, 5d }, clock.Durations.Select(duration => duration.TotalSeconds));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(queued.IsCompleted);
        Assert.False(queuedCall.Token.IsCancellationRequested);

        queuedCall.Complete();
        secondCall.Complete();
        string?[] results = await Task.WhenAll(first, second, queued).WaitAsync(TestTimeout);
        Assert.All(results, result => Assert.Equal("@,^" + GeneratedProse, result));
    }

    [Fact]
    public async Task OwnDeadlineReturnsTemplateFallbackAndLogsRequestBudget()
    {
        var clock = new ManualDeadlineScheduler();
        var model = new ControlledLegacyLlm();
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator(clock.Schedule);

        Task<string?> generation = generator.GenerateAsync(Mail(85), CancellationToken.None);
        PendingCall call = await model.NextCallAsync();
        clock.Advance(TimeSpan.FromSeconds(84));
        Assert.False(generation.IsCompleted);
        Assert.False(call.Token.IsCancellationRequested);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(await generation.WaitAsync(TestTimeout));
        Assert.True(call.Token.IsCancellationRequested);
        string failure = Assert.Single(Monitor.MessagesAt(LogLevel.Info));
        Assert.Contains("timeout", failure, StringComparison.Ordinal);
        Assert.Matches(@"request \d+ ms, budget 85s, queue \d+ ms", failure);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutTimeoutLogAndFreesItsSlot()
    {
        var clock = new ManualDeadlineScheduler();
        var model = new ControlledLegacyLlm();
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator(clock.Schedule);
        using var caller = new CancellationTokenSource();

        Task<string?> canceled = generator.GenerateAsync(Mail(85), caller.Token);
        PendingCall canceledCall = await model.NextCallAsync();
        Task<string?> occupied = generator.GenerateAsync(Mail(180), CancellationToken.None);
        PendingCall occupiedCall = await model.NextCallAsync();

        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceled.WaitAsync(TestTimeout));
        Assert.True(canceledCall.Token.IsCancellationRequested);
        Assert.Empty(Monitor.MessagesAt(LogLevel.Info));

        Task<string?> next = generator.GenerateAsync(Mail(85), CancellationToken.None);
        PendingCall nextCall = await model.NextCallAsync();
        Assert.Equal(3, model.CallCount);
        nextCall.Complete();
        occupiedCall.Complete();
        Assert.All(await Task.WhenAll(occupied, next).WaitAsync(TestTimeout), result => Assert.NotNull(result));
        Assert.DoesNotContain(Monitor.MessagesAt(LogLevel.Info), message => message.Contains("timeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueuedCallerCancellationDoesNotArmADeadlineOrReleaseAnUnownedSlot()
    {
        var clock = new ManualDeadlineScheduler();
        var model = new ControlledLegacyLlm();
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator(clock.Schedule);
        using var caller = new CancellationTokenSource();

        Task<string?> first = generator.GenerateAsync(Mail(180), CancellationToken.None);
        PendingCall firstCall = await model.NextCallAsync();
        Task<string?> second = generator.GenerateAsync(Mail(180), CancellationToken.None);
        PendingCall secondCall = await model.NextCallAsync();
        Task<string?> canceled = generator.GenerateAsync(Mail(5), caller.Token);

        caller.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await canceled.WaitAsync(TestTimeout));
        Assert.Empty(Monitor.MessagesAt(LogLevel.Info));
        Assert.Equal(2, clock.Durations.Count);

        Task<string?> queued = generator.GenerateAsync(Mail(5), CancellationToken.None);
        Assert.Equal(2, model.CallCount);
        Assert.False(queued.IsCompleted);
        firstCall.Complete();
        PendingCall queuedCall = await model.NextCallAsync();
        Assert.Equal(3, clock.Durations.Count);

        queuedCall.Complete();
        secondCall.Complete();
        Assert.All(await Task.WhenAll(first, second, queued).WaitAsync(TestTimeout), result => Assert.NotNull(result));
    }

    [Fact]
    public async Task ExpiredDeadlineRejectsAnAlreadyCompletedSuccessResponse()
    {
        var clock = new ManualDeadlineScheduler();
        var model = new ControlledLegacyLlm
        {
            ImmediateResponder = _ =>
            {
                clock.Advance(TimeSpan.FromSeconds(85));
                return Success();
            }
        };
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator(clock.Schedule);

        string? result = await generator.GenerateAsync(Mail(85), CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Null(result);
        Assert.Equal(1, model.CallCount);
        Assert.Contains("timeout", Assert.Single(Monitor.MessagesAt(LogLevel.Info)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlreadyExpiredDeadlineDoesNotStartAModelRequest()
    {
        var model = new ControlledLegacyLlm();
        LegacyLlm.Instance = model;
        var generator = new GiftMailGenerator((source, _) => source.Cancel());

        string? result = await generator.GenerateAsync(Mail(85), CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Null(result);
        Assert.Equal(0, model.CallCount);
        Assert.Contains("timeout", Assert.Single(Monitor.MessagesAt(LogLevel.Info)), StringComparison.Ordinal);
    }

    private static GiftMailRequest Mail(int timeoutSeconds)
        => new("Penny", "Penny", "reciprocal", "(O)346", "Beer", "Tea", "small", timeoutSeconds);

    private static LlmResponse Success() => new() { IsSuccess = true, Text = GeneratedProse };

    private sealed class ManualDeadlineScheduler
    {
        private readonly object _gate = new();
        private readonly List<(CancellationTokenSource Source, TimeSpan Deadline)> _deadlines = new();
        private readonly List<TimeSpan> _durations = new();
        private TimeSpan _now;

        public IReadOnlyList<TimeSpan> Durations
        {
            get
            {
                lock (_gate)
                {
                    return _durations.ToArray();
                }
            }
        }

        public void Schedule(CancellationTokenSource source, TimeSpan duration)
        {
            lock (_gate)
            {
                _durations.Add(duration);
                _deadlines.Add((source, _now + duration));
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            CancellationTokenSource[] due;
            lock (_gate)
            {
                _now += elapsed;
                due = _deadlines.Where(deadline => deadline.Deadline <= _now).Select(deadline => deadline.Source).ToArray();
                _deadlines.RemoveAll(deadline => deadline.Deadline <= _now);
            }

            foreach (CancellationTokenSource source in due)
            {
                try
                {
                    source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // A completed request disposes its real timer before the simulated deadline.
                }
            }
        }
    }

    private sealed class ControlledLegacyLlm : LegacyLlm
    {
        private readonly Channel<PendingCall> _calls = Channel.CreateUnbounded<PendingCall>();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public Func<PendingCall, LlmResponse>? ImmediateResponder { get; init; }

        public Task<PendingCall> NextCallAsync() => _calls.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);

        public override Task<LlmResponse> RunInference(
            string systemPromptString,
            string gameCacheString,
            string npcCacheString,
            string promptString,
            string responseStart = "",
            int n_predict = 2048,
            string cacheContext = "",
            bool allowRetry = true,
            bool disableThinking = false,
            CancellationToken ct = default,
            LlmOutputFormat outputFormat = LlmOutputFormat.Text,
            TimeSpan? timeoutOverride = null)
        {
            var call = new PendingCall(n_predict, allowRetry, disableThinking, outputFormat, timeoutOverride, ct);
            Interlocked.Increment(ref _callCount);
            _calls.Writer.TryWrite(call);
            return ImmediateResponder != null
                ? Task.FromResult(ImmediateResponder(call))
                : call.Completion.Task.WaitAsync(ct);
        }
    }

    private sealed record PendingCall(
        int MaxTokens,
        bool AllowRetry,
        bool DisableThinking,
        LlmOutputFormat OutputFormat,
        TimeSpan? TimeoutOverride,
        CancellationToken Token)
    {
        public TaskCompletionSource<LlmResponse> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => Completion.TrySetResult(Success());
    }
}
