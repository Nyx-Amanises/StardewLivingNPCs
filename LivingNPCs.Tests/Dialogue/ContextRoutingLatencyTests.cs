using System;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Llm;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

[Collection("LlmLayer")]
public sealed class ContextRoutingLatencyTests : LlmTestBase
{
    public ContextRoutingLatencyTests()
    {
        Config.EnableSemanticContextRouting = true;
        Config.ModelName = "routing-test-model";
        LegacyLlm.Instance = new StubRouter();
    }

    [Theory]
    [InlineData("你好", ContextDetail.Brief)]
    [InlineData("你好，刘易斯。", ContextDetail.Brief)]
    [InlineData("刘易斯，你好！", ContextDetail.Brief)]
    [InlineData("你好刘易斯", ContextDetail.Brief)]
    [InlineData("刘易斯你好", ContextDetail.Brief)]
    [InlineData("早上好，刘易斯！", ContextDetail.Brief)]
    [InlineData("HELLO, Lewis!", ContextDetail.Brief)]
    [InlineData("Lewis, good morning!", ContextDetail.Full)]
    [InlineData("  Hi, Lewis.  ", ContextDetail.Brief)]
    public async Task ExactOpeningGreeting_DoesNotSpendRouterRoundTrip(string greeting, ContextDetail expectedLivingNpcDetail)
    {
        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(
            Lewis(), Opening(greeting));

        Assert.Equal("greeting-deterministic", plan.RoutingOutcome);
        Assert.Equal(0, Router.Calls);
        Assert.Equal(0, plan.RoutingMilliseconds);
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.NpcProfile));
        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.CurrentConversation));
        // The existing additive travel cue matches "go" in "good morning". The greeting
        // shortcut must preserve that full-context boundary while avoiding the router call.
        Assert.Equal(expectedLivingNpcDetail, plan.Get(ContextModule.LivingNpc));
    }

    [Theory]
    [InlineData("你好，刘易斯，你记得昨天答应过我的事吗？")]
    [InlineData("Hi Lewis, what is the town history?")]
    [InlineData("你好，有什么我可以帮忙的吗？")]
    [InlineData("你好，阿比盖尔。")]
    [InlineData("Bonjour Lewis.")]
    public async Task GreetingWithSubstantiveOrUnrecognizedText_StillRoutesSemantically(string text)
    {
        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(
            Lewis(), Opening(text));

        Assert.Equal("success", plan.RoutingOutcome);
        Assert.Equal(1, Router.Calls);
        Assert.False(Router.AllowRetry);
        Assert.True(Router.DisableThinking);
        Assert.Contains(text, Router.LastPrompt);
    }

    [Fact]
    public async Task EnglishGreetingMustBeSeparatedFromCustomNpcName()
    {
        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(
            new Character("story"), Opening("history"));

        Assert.Equal("success", plan.RoutingOutcome);
        Assert.Equal(1, Router.Calls);
    }

    [Fact]
    public async Task OpeningGreetingPlan_IsNotCachedIntoNextSubstantiveTurn()
    {
        var context = Opening("你好，刘易斯。");
        await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);
        context.ChatHistory.Add(new ConversationElement("你好。", false));
        context.ChatHistory.Add(new ConversationElement("社区中心为什么变成这样？", true));

        ContextRoutingPlan second = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal("success", second.RoutingOutcome);
        Assert.Equal(1, Router.Calls);
        Assert.Contains("社区中心为什么变成这样？", Router.LastPrompt);
    }

    [Fact]
    public async Task GreetingAfterEarlierDialogue_DoesNotDiscardTheConversationTopic()
    {
        var context = Opening("记得昨天的约定吗？");
        context.ChatHistory.Add(new ConversationElement("我们需要再谈谈那件事。", false));
        context.ChatHistory.Add(new ConversationElement("你好。", true));

        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal("success", plan.RoutingOutcome);
        Assert.Equal(1, Router.Calls);
    }

    [Fact]
    public async Task GreetingFastPath_PreservesActiveOutingAndHardBoundaryContext()
    {
        var context = Opening("你好，刘易斯！");
        context.LivingNpcExtraPrompt = "## Active Companion Outing\nCurrent agreement and route.";

        ContextRoutingPlan plan = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal(0, Router.Calls);
        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.Location));
        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.LivingNpc));
        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.CurrentConversation));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Relationship));
    }

    [Theory]
    [InlineData("timeout", "timeout-full")]
    [InlineData("canceled", "timeout-full")]
    [InlineData("exception", "failed-full")]
    [InlineData("response", "response-failed-full")]
    [InlineData("empty", "response-failed-full")]
    [InlineData("invalid", "parse-failed-full")]
    [InlineData("confidence", "low-confidence-full")]
    public async Task FailedRouting_ReusesFullCoverageForTheRestOfThisConversation(string failure, string expectedOutcome)
    {
        Router.Respond = _ => Failure(failure);
        var context = Opening("社区中心为什么变成这样？");

        ContextRoutingPlan first = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);
        context.ChatHistory.Add(new ConversationElement("那是个很长的故事。", false));
        context.ChatHistory.Add(new ConversationElement("你的家人还好吗？", true));
        ContextRoutingPlan second = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal(expectedOutcome, first.RoutingOutcome);
        Assert.Equal("cached-fallback-full", second.RoutingOutcome);
        Assert.Equal(0, second.RoutingMilliseconds);
        Assert.Equal(1, Router.Calls);
        foreach (ContextModule module in Enum.GetValues<ContextModule>())
        {
            Assert.Equal(ContextDetail.Full, first.Get(module));
            Assert.Equal(ContextDetail.Full, second.Get(module));
        }
    }

    [Fact]
    public async Task FailedRouting_NewConversationCanTryAgain()
    {
        Router.Respond = _ => Failure("timeout");
        await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), Opening("这个地方有什么历史？"));
        Router.Respond = _ => Task.FromResult(Success());

        ContextRoutingPlan nextConversation = await ContextRoutingDecisionPass.BuildPlanAsync(
            Lewis(), Opening("你今天准备去哪里？"));

        Assert.Equal(2, Router.Calls);
        Assert.Equal("success", nextConversation.RoutingOutcome);
    }

    [Fact]
    public async Task FailedRouting_DifferentNpcCannotReuseTheFallback()
    {
        Router.Respond = _ => Failure("timeout");
        var context = Opening("这个地方有什么历史？");
        await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);
        Router.Respond = _ => Task.FromResult(Success());

        // Even identical conversation element ids must not carry a failure across villagers.
        ContextRoutingPlan otherNpc = await ContextRoutingDecisionPass.BuildPlanAsync(
            new Character("Haley"), context);

        Assert.Equal(2, Router.Calls);
        Assert.Equal("success", otherNpc.RoutingOutcome);
    }

    [Fact]
    public async Task FailedRouting_NewDayRetriesEvenIfConversationIdsWereRetained()
    {
        Router.Respond = _ => Failure("timeout");
        var context = Opening("这个地方有什么历史？");
        context.AbsoluteDay = 42;
        await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);
        context.AbsoluteDay = 43;
        Router.Respond = _ => Task.FromResult(Success());

        ContextRoutingPlan nextDay = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal(2, Router.Calls);
        Assert.Equal("success", nextDay.RoutingOutcome);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("provider")]
    [InlineData("server")]
    public async Task FailedRouting_ChangingConnectionSettingsRetriesImmediately(string setting)
    {
        Router.Respond = _ => Failure("timeout");
        var context = Opening("这个地方有什么历史？");
        await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);
        switch (setting)
        {
            case "model": Config.ModelName = "different-model"; break;
            case "provider": Config.Provider = "different-provider"; break;
            case "server": Config.ServerAddress = "https://different.example/v1"; break;
        }
        Router.Respond = _ => Task.FromResult(Success());

        ContextRoutingPlan retried = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal(2, Router.Calls);
        Assert.Equal("success", retried.RoutingOutcome);
    }

    [Fact]
    public async Task FailedRouting_ReplacingClientRetriesImmediately()
    {
        Router.Respond = _ => Failure("timeout");
        var context = Opening("这个地方有什么历史？");
        await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);
        var replacement = new StubRouter();
        LegacyLlm.Instance = replacement;

        ContextRoutingPlan retried = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal(1, replacement.Calls);
        Assert.Equal("success", retried.RoutingOutcome);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotInstallAFailedRoutingCache()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Router.Respond = async ct =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Success();
        };
        var context = Opening("这个地方有什么历史？");
        using var cancellation = new CancellationTokenSource();
        Task<ContextRoutingPlan> routing = ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await routing);
        Router.Respond = _ => Task.FromResult(Success());
        ContextRoutingPlan retried = await ContextRoutingDecisionPass.BuildPlanAsync(Lewis(), context);

        Assert.Equal(2, Router.Calls);
        Assert.Equal("success", retried.RoutingOutcome);
    }

    private StubRouter Router => (StubRouter)LegacyLlm.Instance;

    private static Character Lewis() => new("Lewis", displayName: "刘易斯");

    private static DialogueContext Opening(string text) => new()
    {
        ChatHistory = new() { new ConversationElement(text, true) }
    };

    private static LlmResponse Success() => new()
    {
        IsSuccess = true,
        Text = "{\"confidence\":0.95,\"world\":\"full\",\"npcProfile\":\"full\"}"
    };

    private static Task<LlmResponse> Failure(string failure) => failure switch
    {
        "timeout" => Task.FromException<LlmResponse>(new TimeoutException("simulated slow router")),
        "canceled" => Task.FromException<LlmResponse>(new OperationCanceledException("simulated router deadline")),
        "exception" => Task.FromException<LlmResponse>(new InvalidOperationException("simulated router failure")),
        "response" => Task.FromResult(new LlmResponse { IsSuccess = false, ErrorMessage = "failed" }),
        "empty" => Task.FromResult(new LlmResponse { IsSuccess = true }),
        "invalid" => Task.FromResult(new LlmResponse { IsSuccess = true, Text = "not JSON" }),
        "confidence" => Task.FromResult(new LlmResponse { IsSuccess = true, Text = "{\"confidence\":0.2}" }),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    private sealed class StubRouter : LegacyLlm
    {
        public int Calls { get; private set; }
        public bool AllowRetry { get; private set; }
        public bool DisableThinking { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;
        public Func<CancellationToken, Task<LlmResponse>> Respond { get; set; } = _ => Task.FromResult(Success());

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
            CancellationToken ct = default)
        {
            Calls++;
            AllowRetry = allowRetry;
            DisableThinking = disableThinking;
            LastPrompt = promptString;
            return Respond(ct);
        }
    }
}
