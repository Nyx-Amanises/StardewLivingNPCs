using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

[Collection("LlmLayer")]
public sealed class LivingNpcActionDecisionLatencyTests : LlmTestBase
{
    private const string GeneralRules = """
        ## LivingNPCs Behavior
        - Help-request lifecycle: Offered = asked but not accepted; Pending = accepted/active; only Pending is a task.
        - Help-request fit: no currently reasonable item requests.
        - Schema: companion_outing, helpRequests, helpRequestUpdates, give_small_gift.
        ## LivingNPCs Gift Restriction: no NPC gift is authorized for this reply; include no gift action.
        """;
    private const string ActiveRequest = """
        - Help requests involving the farmer: item_request, due tomorrow, status Pending, step 1/1; current step: bring Wood (O)388; summary: bring some wood.
        """;
    private readonly CountingClassifier classifier = new();

    public LivingNpcActionDecisionLatencyTests()
    {
        Config.EnableLivingNpcActionDecisionPass = true;
        LegacyLlm.Instance = this.classifier;
    }

    [Theory]
    [InlineData("你好，刘易斯。", "早上好。这雨绿得不太对劲，我活了这些年，也没见过夏天来这么一场天。年轻人，照看农场是好事，可别在这种雨里待太久。")]
    [InlineData("我会小心的，谢谢您的关心。", "嗯，今天这场雨可真奇怪。")]
    [InlineData("Hello, Lewis.", "Good morning. What unusual weather today.")]
    [InlineData("How are you?", "I'm enjoying the sunshine.")]
    [InlineData("好的。", "嗯。")]
    [InlineData("Thanks!", "You're welcome.")]
    public async Task OrdinaryConversation_WithGenericActionRules_DoesNotCallClassifier(string player, string npcReply)
    {
        var analysis = new ConversationAnalysis { RapportDelta = 2 };

        LivingNpcActionDecisionResult result = await Run(Context(player), npcReply, analysis);

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
        Assert.Equal("skipped", result.Diagnostics.Outcome);
        Assert.Equal(0, result.Diagnostics.ElapsedMilliseconds);
        Assert.Same(analysis, result.Analysis);
        Assert.Equal(2, result.Analysis.RapportDelta);
    }

    [Theory]
    [InlineData("## LivingNPCs Gift Opportunity\n- The NPC may give a small gift today.")]
    [InlineData("## LivingNPCs Help Request Opportunity\n- The NPC may naturally ask for one modest favor.")]
    [InlineData(ActiveRequest)]
    public async Task OpportunityOrPendingRequest_WithoutDialogueEvidence_DoesNotTurnGreetingIntoAction(string state)
    {
        LivingNpcActionDecisionResult result = await Run(Context("你好。", state), "早上好，今天真晴朗。$h");

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
    }

    [Fact]
    public async Task UnselectedGeneratedOptions_DoNotTriggerActionRecovery()
    {
        LivingNpcActionDecisionResult result = await LivingNpcActionDecisionPass.TrySupplementAsync(
            new Character("Lewis"),
            Context("你好。"),
            ConversationAnalysis.Empty,
            new[] { "早上好。", "我们一起去海边吧。", "有什么需要帮忙的吗？" });

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
    }

    [Theory]
    [InlineData("我们一起去海边吧？", "好呀，现在就去。")]
    [InlineData("Would you come to the beach with me?", "Sure, let's go.")]
    [InlineData("今天天气不错。", "这块面包收下吧。")]
    [InlineData("How are you?", "I brought you a small gift.")]
    [InlineData("有什么需要帮忙的吗？", "能帮我带些木材吗？")]
    [InlineData("Hello.", "Could you pick up a leek for me?")]
    public async Task VisibleInvitationGiftOrHelp_StillCallsClassifier(string player, string npcReply)
    {
        LivingNpcActionDecisionResult result = await Run(Context(player), npcReply);

        Assert.Equal(1, this.classifier.Calls);
        Assert.True(result.Diagnostics.WasRun);
        Assert.True(this.classifier.DisableThinking);
        Assert.False(this.classifier.AllowRetry);
        Assert.Contains(player, this.classifier.LastPrompt);
        Assert.Contains(npcReply, this.classifier.LastPrompt);
    }

    [Theory]
    [InlineData("我们一起去海边吧。", "我只能去一小会儿，可以吗？", "好的。", "好呀。")]
    [InlineData("Would you come to the beach with me?", "Only briefly. Is that okay?", "Yes.", "Sure.")]
    [InlineData("今天天气不错。", "能帮我带些木材吗？", "好的。", "谢谢。")]
    [InlineData("Hello.", "Could you bring me a leek?", "Sure.", "Thanks.")]
    public async Task ShortAcceptance_RetainsActualRecentActionEvidence(string earlierPlayer, string earlierNpc, string player, string npcReply)
    {
        var context = Context(player);
        context.ChatHistory.InsertRange(0, new[]
        {
            new ConversationElement(earlierPlayer, true),
            new ConversationElement(earlierNpc, false)
        });

        LivingNpcActionDecisionResult result = await Run(context, npcReply);

        Assert.Equal(1, this.classifier.Calls);
        Assert.True(result.Diagnostics.WasRun);
        Assert.Contains("action_recent_conversation", this.classifier.LastPrompt);
        Assert.Contains(earlierPlayer, this.classifier.LastPrompt);
        Assert.Contains(earlierNpc, this.classifier.LastPrompt);
    }

    [Theory]
    [InlineData("好的。", "谢谢。")]
    [InlineData("Yes.", "Great.")]
    [InlineData("就是这些。", "一个不少。")]
    [InlineData("Here it is.", "That's everything.")]
    [InlineData("做不到。", "好吧。")]
    [InlineData("No.", "Okay.")]
    public async Task ExistingHelpRequest_AcceptanceDeliveryAndDecline_StillCallClassifier(string player, string npcReply)
    {
        LivingNpcActionDecisionResult result = await Run(Context(player, ActiveRequest), npcReply);

        Assert.Equal(1, this.classifier.Calls);
        Assert.True(result.Diagnostics.WasRun);
    }

    [Fact]
    public async Task ActiveHelpContinuityCue_PreservesTerseAcceptance()
    {
        var context = Context("好的。", "- Active help request: item_request, status Offered, summary: bring wood.");

        await Run(context, "谢谢。");

        Assert.Equal(1, this.classifier.Calls);
    }

    [Fact]
    public async Task PhysicalHelpItemHandIn_PreservesRecoveryWithoutTextualActionCues()
    {
        var context = Context(string.Empty, ActiveRequest);
        context.Accept = "(O)388";

        await Run(context, "正合适。$h");

        Assert.Equal(1, this.classifier.Calls);
    }

    [Theory]
    [InlineData("- Active help request: none.")]
    [InlineData("- Active help request: no active request.")]
    [InlineData("- Help requests involving the farmer: no durable help requests are recorded.")]
    [InlineData("- Help requests involving the farmer: item_request, status Fulfilled, summary: bring wood.")]
    public async Task EmptyOrCompletedHelpState_DoesNotMakeGenericAcknowledgementAnAction(string state)
    {
        LivingNpcActionDecisionResult result = await Run(Context("好的。", state), "嗯。");

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
    }

    [Fact]
    public async Task WithheldTurn_StopsRecoveryFromOlderActionConversation()
    {
        var context = Context("好的。");
        context.ChatHistory.InsertRange(0, new[]
        {
            new ConversationElement("我们一起去海边吧。", true),
            new ConversationElement(string.Empty, false)
        });

        LivingNpcActionDecisionResult result = await Run(context, "嗯。");

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
    }

    [Fact]
    public async Task OldActionOutsideRecentConversation_DoesNotTriggerRecovery()
    {
        var context = Context("好的。");
        context.ChatHistory.Insert(0, new ConversationElement("能帮我带些木材吗？", false));
        for (int index = 0; index < 6; index++)
        {
            context.ChatHistory.Insert(1, new ConversationElement("今天天气不错。", index % 2 == 0));
        }

        LivingNpcActionDecisionResult result = await Run(context, "嗯。");

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
    }

    [Fact]
    public async Task RecoveredImmediateGift_IsStillMergedIntoOriginalAnalysis()
    {
        this.classifier.Respond = _ => Task.FromResult(new LlmResponse
        {
            IsSuccess = true,
            Text = """!LIVINGNPCS_META {"actions":[{"type":"give_small_gift","itemId":"(O)20","itemLabel":"韭葱"}]}"""
        });
        var analysis = new ConversationAnalysis { RapportDelta = 2 };

        LivingNpcActionDecisionResult result = await Run(Context("早上好。"), "这根韭葱送给你，收下吧。", analysis);

        Assert.Equal(1, this.classifier.Calls);
        Assert.True(result.Diagnostics.Merged);
        Assert.Same(analysis, result.Analysis);
        Assert.Equal("(O)20", Assert.Single(result.Analysis.Actions).ItemId);
        Assert.Equal(2, result.Analysis.RapportDelta);
        Assert.NotNull(JObject.Parse(result.Diagnostics.ToSummaryJson())["ElapsedMilliseconds"]);
    }

    [Fact]
    public async Task ExistingWorldMetadata_IsNotClassifiedTwice()
    {
        var analysis = ConversationAnalysis.Parse("""!LIVINGNPCS_META {"actions":[{"type":"give_small_gift","itemId":"(O)20"}]}""");

        LivingNpcActionDecisionResult result = await Run(Context("你好。"), "这个给你。", analysis);

        Assert.Equal(0, this.classifier.Calls);
        Assert.False(result.Diagnostics.WasRun);
        Assert.Same(analysis, result.Analysis);
        Assert.Single(result.Analysis.Actions);
    }

    [Fact]
    public async Task CallerCancellation_CancelsTheSingleAuxiliaryRequest()
    {
        this.classifier.Respond = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return EmptyMetadata();
        };
        using var cancellation = new CancellationTokenSource();
        Task<LivingNpcActionDecisionResult> task = LivingNpcActionDecisionPass.TrySupplementAsync(
            new Character("Lewis"), Context("我们一起去海边吧。"), ConversationAnalysis.Empty,
            new[] { "走吧。" }, cancellation.Token);
        await this.classifier.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        Assert.Equal(1, this.classifier.Calls);
        Assert.True(this.classifier.SeenToken.IsCancellationRequested);
    }

    private static DialogueContext Context(string player, string extraState = "") => new()
    {
        Location = "Town",
        TimeOfDay = "1200",
        LivingNpcExtraPrompt = GeneralRules + "\n" + extraState,
        ChatHistory = new List<ConversationElement> { new(player, true) }
    };

    private static Task<LivingNpcActionDecisionResult> Run(
        DialogueContext context,
        string npcReply,
        ConversationAnalysis? analysis = null) =>
        LivingNpcActionDecisionPass.TrySupplementAsync(
            new Character("Lewis"), context, analysis ?? ConversationAnalysis.Empty, new[] { npcReply });

    private static LlmResponse EmptyMetadata() => new()
    {
        IsSuccess = true,
        Text = """!LIVINGNPCS_META {"actions":[],"helpRequests":[],"helpRequestUpdates":[]}"""
    };

    private sealed class CountingClassifier : LegacyLlm
    {
        public int Calls { get; private set; }
        public bool AllowRetry { get; private set; }
        public bool DisableThinking { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;
        public CancellationToken SeenToken { get; private set; }
        public Func<CancellationToken, Task<LlmResponse>> Respond { get; set; } = _ => Task.FromResult(EmptyMetadata());
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            this.Calls++;
            this.AllowRetry = allowRetry;
            this.DisableThinking = disableThinking;
            this.LastPrompt = promptString;
            this.SeenToken = ct;
            this.Started.TrySetResult(true);
            return this.Respond(ct);
        }
    }
}
