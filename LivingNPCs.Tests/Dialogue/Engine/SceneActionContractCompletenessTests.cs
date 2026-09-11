using System.Collections.Generic;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class SceneActionContractCompletenessTests
{
    private static readonly IReadOnlyList<HelpRequestItemAlias> RequestableItems =
    [
        new("(O)245", "糖", ["糖", "Sugar"]),
        new("(O)402", "甜豌豆", ["甜豌豆", "Sweet Pea"])
    ];

    [Theory]
    [InlineData("现在要不要和我一起去农场？", "那我们走吧。")]
    [InlineData("Would you come with me to the farm?", "Sure, let's go.")]
    [InlineData("Would you come with me to the farm?", "Sure.$h#$b#Let’s go.$h")]
    [InlineData("现在要不要和我一起去农场？", "好呀，等会儿再去。先让我收拾一下。")]
    public void OmittedTravelWithExplicitInvitationAndDepartureRequiresClassification(string player, string reply)
    {
        var analysis = new ConversationAnalysis();

        string? reason = Find(analysis, player, reply);

        Assert.Contains("travel", reason);
        Assert.Empty(analysis.Actions);
        Assert.Empty(analysis.HelpRequests);
        Assert.False(analysis.EndConversation);
    }

    [Fact]
    public void ShortContinuationCanUseRecentInvitationWithoutChangingCapturedHistory()
    {
        DialogueContext context = Context(
            Player("现在要不要和我一起去农场？"),
            Npc("只去一会儿，这样可以吗？"),
            Player("太好了，我们走吧。"));
        List<ConversationElement> originalHistory = context.ChatHistory;
        ConversationElement originalInvitation = originalHistory[0];
        ConversationElement originalCurrent = originalHistory[^1];

        string? reason = Find(new(), originalCurrent.Text, "那我们走吧。", context: context);

        Assert.Contains("travel", reason);
        Assert.Same(originalHistory, context.ChatHistory);
        Assert.Equal(3, context.ChatHistory.Count);
        Assert.Same(originalInvitation, context.ChatHistory[0]);
        Assert.Same(originalCurrent, context.ChatHistory[^1]);
        Assert.Equal("现在要不要和我一起去农场？", originalInvitation.Text);
    }

    [Theory]
    [InlineData("海边的景色真好。", "那我们走吧。")]
    [InlineData("你去过农场吗？", "好啊，我去过农场。")]
    [InlineData("Have you been to the farm?", "Yes, the farm is beautiful.")]
    [InlineData("The farmer is ready.", "Let's go.")]
    [InlineData("我给你带了一包糖。", "好呀，谢谢你。")]
    [InlineData("Here's a gift for you.", "Sure, thank you.")]
    [InlineData("现在要不要和我一起去农场？", "好的，谢谢你的邀请。")]
    [InlineData("Would you come with me to the farm?", "Sure, thanks for inviting me.")]
    [InlineData("现在要不要和我一起去农场？", "好呀，我们走过那条路。")]
    [InlineData("现在要不要和我一起去农场？", "好啊，我陪你去过农场，当然记得。")]
    [InlineData("现在要不要和我一起去农场？", "好啊，不过今天不行。")]
    [InlineData("现在要不要和我一起去农场？", "我们走吧，明天再去。")]
    [InlineData("现在要不要和我一起去农场？", "我陪你去，不过得先把事情忙完。")]
    [InlineData("现在要不要和我一起去农场？", "如果有空，那我们走吧。")]
    [InlineData("现在要不要和我一起去农场？", "我们走吧，这样可以吗？")]
    [InlineData("Would you come with me to the farm?", "I can go with you tomorrow.")]
    [InlineData("Would you come with me to the farm?", "Sure, I can't go with you today.")]
    [InlineData("Would you come with me to the farm?", "Maybe let's go.")]
    [InlineData("Would you come with me to the farm?", "Let's go? Is that okay?")]
    [InlineData("Would you come with me to the farm?", "I said 'let's go' last week.")]
    public void MentionsThanksRefusalsAndUnsettledPlansDoNotTriggerTravel(string player, string reply)
    {
        Assert.Null(Find(new(), player, reply));
    }

    [Fact]
    public void CurrentUnrelatedInputStopsOldInvitationRecovery()
    {
        DialogueContext context = Context(Player("Would you come with me to the farm?"), Npc("Maybe."));

        Assert.Null(Find(new(), "How was your day?", "Let's go.", context: context));
    }

    [Theory]
    [InlineData("yes", "Let's go.")]
    [InlineData("好的", "那我们走吧。")]
    public void BarePlayerAcceptanceNeedsAnActualPlayerInvitation(string player, string reply)
    {
        Assert.Null(Find(new(), player, reply));
        Assert.Null(Find(new(), player, reply, context: Context(Npc("Let's go to the farm."))));
        Assert.Null(Find(new(), player, reply, context: Context(Player("I like the farm."))));
    }

    [Theory]
    [InlineData("yes", "Sure, thanks.")]
    [InlineData("好的", "好呀，谢谢你。")]
    public void BareAcceptanceAndThanksAreNotDepartureEvenWithRecentInvitation(string player, string reply)
    {
        DialogueContext context = Context(Player("Would you come with me to the farm?"), Npc("One moment."));

        Assert.Null(Find(new(), player, reply, context: context));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]")]
    [InlineData("Would you come with me to Ridgeside Village and the farm?")]
    public void MissingOrWithheldCurrentInputDoesNotReviveOldInvitation(string player)
    {
        DialogueContext context = Context(Player("Would you come with me to the farm?"));

        Assert.Null(Find(new(), player, "Let's go.", context: context));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("", false)]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]", true)]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]", false)]
    [InlineData("Ridgeside Village", true)]
    [InlineData("Ridgeside Village", false)]
    public void InvitationRecoveryNeverCrossesWithheldHistory(string withheld, bool isPlayer)
    {
        DialogueContext context = Context(
            Player("Would you come with me to the farm?"),
            new ConversationElement(withheld, isPlayer));

        Assert.Null(Find(new(), "Great, let's go.", "Let's go.", context: context));
    }

    [Fact]
    public void NpcAuthoredDestinationAndRuntimeContextCannotSupplyPlayerInvitation()
    {
        DialogueContext context = Context(Npc("Let's go to the farm."));
        context.Location = "Farm";
        context.NextScheduleLocation = "Farm";
        context.LivingNpcExtraPrompt = "Memory: the player invited Penny to the farm.";

        Assert.Null(Find(new(), "Great, let's go.", "Let's go.", context: context));
    }

    [Fact]
    public void OlderInvitationOutsideRecentTurnWindowDoesNotTriggerTravel()
    {
        DialogueContext context = Context(
            Player("Would you come with me to the farm?"),
            Npc("One moment."), Npc("One moment."), Npc("One moment."),
            Npc("One moment."), Npc("One moment."));

        Assert.Null(Find(new(), "Great, let's go.", "Let's go.", context: context));
    }

    [Fact]
    public void OlderInvitationOutsideRecentPlayerWindowDoesNotTriggerTravel()
    {
        DialogueContext context = Context(
            Player("Would you come with me to the farm?"),
            Player("Great, let's go."), Player("Great, let's go."), Player("Great, let's go."));

        Assert.Null(Find(new(), "Great, let's go.", "Let's go.", context: context));
    }

    [Fact]
    public void SelectedTravelFamilyUsesExistingValidation()
    {
        var plan = new SceneActionContractPlan { IncludeTravel = true };

        Assert.Null(Find(new(), "Would you come with me to the farm?", "Let's go.", plan));
    }

    [Fact]
    public void ExistingTravelMetadataIsNotReclassifiedEvenIfItsConsentNeedsValidation()
    {
        var action = new ConversationWorldActionRequest
        {
            Type = "companion_outing", TravelConsent = "tentative", TargetLocation = "Farm"
        };
        var analysis = new ConversationAnalysis { Actions = [action] };

        Assert.Null(Find(analysis, "Would you come with me to the farm?", "Let's go."));
        Assert.Same(action, Assert.Single(analysis.Actions));
        Assert.Equal("tentative", action.TravelConsent);
    }

    [Fact]
    public void UnrelatedActionDoesNotHideMissingTravelMetadata()
    {
        var analysis = new ConversationAnalysis
        {
            Actions = [new ConversationWorldActionRequest { Type = "give_small_gift" }]
        };

        Assert.Contains("travel", Find(analysis, "Would you come with me to the farm?", "Let's go."));
        Assert.Equal("give_small_gift", Assert.Single(analysis.Actions).Type);
    }

    [Theory]
    [InlineData("请帮我带一包糖来。")]
    [InlineData("Could you bring me some Sugar?")]
    [InlineData("谢谢，你能帮我带糖吗？")]
    [InlineData("Thanks, could you find a Sweet Pea?")]
    [InlineData("请帮我找甜豌豆。$h#$b#另外帮我带糖，这两样我都需要。")]
    public void OmittedConcreteHelpRequestRequiresClassificationWithoutCreatingTask(string reply)
    {
        var analysis = new ConversationAnalysis();

        Assert.Contains("helpRequests", Find(analysis, string.Empty, reply));
        Assert.Empty(analysis.HelpRequests);
        Assert.Empty(analysis.HelpRequestUpdates);
        Assert.Empty(analysis.Actions);
    }

    [Theory]
    [InlineData("我喜欢糖和甜豌豆。")]
    [InlineData("Sugar tastes sweet.")]
    [InlineData("谢谢你帮我带糖。")]
    [InlineData("Thanks for bringing me Sugar.")]
    [InlineData("不用帮我找甜豌豆了。")]
    [InlineData("Don't bring me Sugar.")]
    [InlineData("我可以带糖给你。")]
    [InlineData("I'll bring you some Sugar.")]
    [InlineData("请帮我带一瓶枫糖浆。")]
    [InlineData("Could you bring me Maple Syrup?")]
    [InlineData("只要带上你的好心情。")]
    [InlineData("Just bring a smile.")]
    public void ItemMentionsThanksNpcGiftsAndUnknownItemsDoNotCreateMissingHelpEffect(string reply)
    {
        Assert.Null(Find(new(), "我给你带了一包糖。", reply));
    }

    [Fact]
    public void PlayerRequestAloneDoesNotBecomeNpcHelpRequest()
    {
        Assert.Null(Find(new(), "Could you bring me Sugar?", "Sure, I can bring you Sugar."));
    }

    [Fact]
    public void HelpDetectionNeedsReasonableItemAliases()
    {
        Assert.Null(SceneActionContractCompleteness.FindMissingEffect(
            new(), new(), string.Empty, "Could you bring me Sugar?", new(), []));
    }

    [Fact]
    public void SelectedHelpFamilyUsesExistingValidation()
    {
        var plan = new SceneActionContractPlan { IncludeNewHelp = true };

        Assert.Null(Find(new(), string.Empty, "Could you bring me Sugar?", plan));
    }

    [Fact]
    public void PartialHelpMetadataUsesExistingConsistencyValidation()
    {
        var request = new ConversationHelpRequestCandidate
        {
            Type = "item_request", RequestedItemId = "(O)402", RequestedItemLabel = "Sweet Pea"
        };
        var analysis = new ConversationAnalysis { HelpRequests = [request] };

        Assert.Null(Find(analysis, string.Empty, "Please bring me a Sweet Pea and Sugar."));
        Assert.Same(request, Assert.Single(analysis.HelpRequests));
        Assert.Equal("(O)402", request.RequestedItemId);
    }

    [Fact]
    public void ExistingHelpUpdateDoesNotBecomeAnUnclassifiedNewTask()
    {
        var analysis = new ConversationAnalysis
        {
            HelpRequestUpdates = [new ConversationHelpRequestUpdateCandidate { Status = "accepted" }]
        };

        Assert.Null(Find(analysis, "Sure, I'll help.", "Please bring me Sugar."));
        Assert.Empty(analysis.HelpRequests);
        Assert.Single(analysis.HelpRequestUpdates);
    }

    [Theory]
    [InlineData("Pending", "还要带什么？", "请帮我带一包糖来。")]
    [InlineData("Offered", "还要带什么？", "请帮我带一包糖来。")]
    [InlineData("Pending", "What do you still need?", "Please bring me some Sugar.")]
    [InlineData("Offered", "What do you still need?", "Please bring me some Sugar.")]
    public void ExistingTaskReminderNeedsNoNewMetadataWhenHelpUpdatesAreSelected(
        string status,
        string player,
        string reply)
    {
        var plan = new SceneActionContractPlan { IncludeHelpUpdates = true };
        var context = new DialogueContext
        {
            LivingNpcExtraPrompt = $"Active help request: bring Sugar ((O)245); status {status}."
        };
        var analysis = new ConversationAnalysis();

        Assert.Null(Find(analysis, player, reply, plan, context));
        Assert.Empty(analysis.HelpRequests);
        Assert.Empty(analysis.HelpRequestUpdates);
        Assert.Empty(analysis.Actions);
    }

    [Theory]
    [InlineData("Let's go to the farm with Ridgeside Village friends.")]
    [InlineData("Could you bring me Sugar from Ridgeside Village?")]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]")]
    [InlineData("")]
    public void WithheldOrEmptyReplyCannotTriggerMissingEffect(string reply)
    {
        Assert.Null(Find(new(), "Would you come with me to the farm?", reply));
    }

    [Theory]
    [InlineData("这份礼物给你，请收下。")]
    [InlineData("Here is a gift for you. Take this.")]
    [InlineData("给你一些金币。")]
    [InlineData("Here is some gold for you.")]
    [InlineData("我们一起在花舞节跳舞吧。")]
    [InlineData("Let's dance together at the Flower Dance.")]
    public void GiftMoneyAndFestivalRemainOutsideThisGuardCoverage(string reply)
    {
        Assert.Null(Find(new(), "Hello.", reply));
    }

    [Fact]
    public void FullContractNeverNeedsOmissionGuard()
    {
        Assert.Null(Find(new(), "Would you come with me to the farm?",
            "Let's go. Please bring me Sugar.", SceneActionContractPlan.Full()));
    }

    private static string? Find(
        ConversationAnalysis analysis,
        string player,
        string reply,
        SceneActionContractPlan? plan = null,
        DialogueContext? context = null)
    {
        return SceneActionContractCompleteness.FindMissingEffect(
            analysis, plan ?? new(), player, reply, context ?? new(), RequestableItems);
    }

    private static DialogueContext Context(params ConversationElement[] turns) => new() { ChatHistory = [.. turns] };

    private static ConversationElement Player(string text) => new(text, true);

    private static ConversationElement Npc(string text) => new(text, false);
}
