using System;
using System.Collections.Generic;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class SceneActionContractSelectorTests
{
    [Theory]
    [InlineData("你好，潘妮。")]
    [InlineData("今天心情不错。")]
    [InlineData("Good morning!")]
    [InlineData("Would you say this book is good?")]
    [InlineData("A golden afternoon.")]
    public void OrdinaryConversationNeedsNoActionSchema(string player)
    {
        AssertCoreOnly(Select(player));
    }

    [Fact]
    public void RuleProseNegativeCapabilitiesAndOldMemoriesDoNotSelectActions()
    {
        string context = Context(extra: string.Join("\n",
            PromptFragments.Outing.UnavailableSection(),
            "- Reply guidance: invitations may involve going together; gifts must use give_small_gift; money needs evidence.",
            "- Relevant long-term memories: the farmer once said 'let's go to the Beach' and brought a gift.",
            "- Help-request lifecycle: Offered = asked but not accepted; Pending = accepted/active; only Pending is a task."));

        AssertCoreOnly(Select("你好。", context));
    }

    [Fact]
    public void CurrentOpportunitiesKeepTheirFullFamiliesForNeutralPlayerText()
    {
        SceneActionContractPlan plan = Select("Hi.", Context(helpAllowed: true, giftOpportunity: true,
            extra: PromptFragments.HelpRequestOpportunity.Section("Penny")));

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeGifts);
        Assert.True(plan.IncludeNewHelp);
        Assert.False(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeTravel);
        Assert.False(plan.IncludeMoney);
        Assert.False(plan.IncludeFestival);
    }

    [Fact]
    public void NoReasonableItemCandidatesSuppressNewRequestsEvenWhenRelationshipIsReady()
    {
        string context = Context(helpAllowed: true,
            extra: PromptFragments.Context.HelpRequestFitLine("no currently reasonable item request; do not open a help request now"));

        AssertCoreOnly(Select("有什么需要我帮忙的吗？", context));
    }

    [Theory]
    [InlineData("Help requests involving the farmer", "Offered")]
    [InlineData("Help requests involving the farmer", "Pending")]
    [InlineData("Help requests", "Offered")]
    [InlineData("Help requests", "Pending")]
    [InlineData("Active help request", "Offered")]
    [InlineData("Active help request", "Pending")]
    public void ExistingRequestsKeepUpdatesWhileNewRequestsAreBlocked(string field, string status)
    {
        string context = Context(extra: $"- {field}: item_request, due tomorrow, status {status}, step 1/2; current step: Wood (O)388; summary: first Wood, then Milk.");
        SceneActionContractPlan plan = Select("好的。", context);

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IncludeTravel);
    }

    [Theory]
    [InlineData("none.")]
    [InlineData("no active request.")]
    [InlineData("item_request, status Fulfilled, summary: bring Wood.")]
    [InlineData("item_request, status Expired, summary: bring Wood.")]
    [InlineData("item_request, status Declined, summary: bring Wood.")]
    public void AbsentOrFinishedRequestsDoNotTurnAcknowledgementIntoAnUpdate(string state)
    {
        AssertCoreOnly(Select("好的。", Context(extra: $"- Active help request: {state}")));
    }

    [Theory]
    [InlineData("好的。")]
    [InlineData("这两样都齐了。")]
    [InlineData("先是木材，再是牛奶，对吧？")]
    [InlineData("Here they are.")]
    [InlineData("I cannot finish it.")]
    public void OrderedActiveRequestRemainsAvailableAcrossAcceptanceDeliveryAndDecline(string player)
    {
        string context = Context(extra: "- Active help request: item_request, status Pending, step 1/2; current step: Wood (O)388; summary: first Wood, then Milk.");
        SceneActionContractPlan plan = Select(player, context);

        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IsFallback);
    }

    [Fact]
    public void PhysicalHandInKeepsUpdatesWithoutSpokenCuesOrAnOutgoingGift()
    {
        SceneActionContractPlan plan = SceneActionContractSelector.Select(
            string.Empty, Context(), null, GenerationTrigger.Gift, isPhysicalItemHandIn: true);

        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IsFallback);
    }

    [Theory]
    [InlineData("## LivingNPCs Help Request Gift Response")]
    [InlineData("## LivingNPCs Immediate Help Request Delivery")]
    public void CapturedHandInSectionKeepsUpdatesAfterRequestHasAlreadyCompleted(string header)
    {
        SceneActionContractPlan plan = Select(string.Empty, Context(extra: header
            + "\n- Help request status: Fulfilled; summary: bring Wood."
            + "\n- LivingNPCs already granted a system money reward of 200g."
            + "\n- LivingNPCs scheduled a small thank-you item by mail for tomorrow."));

        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IncludeMoney);
        Assert.False(plan.IsFallback);
    }

    [Theory]
    [InlineData("这是我送你的花。")]
    [InlineData("I brought you a gift.")]
    [InlineData("This is for you.")]
    [InlineData("")]
    public void IncomingGiftTriggerDoesNotSelectOutgoingGiftActions(string player)
    {
        AssertCoreOnly(SceneActionContractSelector.Select(player, Context(), null, GenerationTrigger.Gift));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredMailSuppressesImmediateOpportunityInsteadOfActivatingGiftFamily(bool isBirthday)
    {
        string context = Context(giftOpportunity: true,
            extra: PromptFragments.GiftResponseMail.Section("Penny", "Parsnip", isBirthday));

        AssertCoreOnly(SceneActionContractSelector.Select("This is for you.", context, null, GenerationTrigger.Gift));
    }

    [Theory]
    [InlineData("可以送我一朵花吗？")]
    [InlineData("Could you give me an apple?")]
    public void ExplicitRequestToReceiveAnItemKeepsGiftDocumentation(string player)
    {
        SceneActionContractPlan plan = Select(player);

        Assert.True(plan.IncludeGifts);
        Assert.False(plan.IsFallback);
        Assert.False(plan.IncludeNewHelp);
    }

    [Theory]
    [InlineData("要不要一起去海边？")]
    [InlineData("一起去我的农场坐一会儿吧。")]
    [InlineData("我们明天一起去海边吧。")]
    [InlineData("Let's go to the Beach.")]
    [InlineData("Can you come to the farm?")]
    [InlineData("Could you accompany me to the Beach?")]
    [InlineData("Would you come with me to the farm tomorrow?")]
    [InlineData("Could you walk with me to a place you have not heard of?")]
    public void InvitationKeepsConsentAndDestinationRulesWithoutAuthorizingTravel(string player)
    {
        SceneActionContractPlan plan = Select(player);

        Assert.True(plan.IncludeTravel);
        Assert.False(plan.IsFallback);
        Assert.False(plan.IncludeGifts);
    }

    [Theory]
    [InlineData("海边。")]
    [InlineData("你去过农场吗？")]
    [InlineData("我们一起坐在这里吧。")]
    [InlineData("Have you ever been to the Beach?")]
    [InlineData("Would you say the Beach is good?")]
    [InlineData("Let's stay here.")]
    [InlineData("Let's sit here for a moment.")]
    public void PlaceNamesPastVisitsAndStayingHereAreNotTravelScenes(string player)
    {
        AssertCoreOnly(Select(player));
    }

    [Theory]
    [InlineData("好呀。")]
    [InlineData("等会再去。")]
    [InlineData("明天吧。")]
    [InlineData("不去了。")]
    [InlineData("Sounds good.")]
    [InlineData("Not today.")]
    public void ShortContinuationRetainsRecentInvitationForAnyConsentOutcome(string player)
    {
        SceneActionContractPlan plan = Select(player, history: new[]
        {
            Player("要不要一起去海边？"),
            Npc("让我先把书收好，再拿件外套。"),
            Player(player)
        });

        Assert.True(plan.IncludeTravel);
        Assert.False(plan.IsFallback);
    }

    [Fact]
    public void MixedRecentReplyKeepsGiftAndTravelTogether()
    {
        SceneActionContractPlan plan = Select("好呀。", history: new[]
        {
            Player("我们一起去海边吧。"),
            Npc("可以，这个给你，是刚摘的花。"),
            Player("好呀。")
        });

        Assert.True(plan.IncludeTravel);
        Assert.True(plan.IncludeGifts);
        Assert.False(plan.IsFallback);
    }

    [Fact]
    public void MultipleItemsInRecentAskRemainAHelpContinuation()
    {
        SceneActionContractPlan plan = Select("Here they are.", history: new[]
        {
            Player("Do you need anything?"),
            Npc("Could you bring Wood first, and then Milk? I need them both.")
        });

        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IsFallback);
    }

    [Theory]
    [InlineData("我以后会寄给你，这个是给你的礼物。")]
    [InlineData("I will mail this tomorrow. This is for you.")]
    [InlineData("I'll take this, thank you for the gift.")]
    [InlineData("You can bring a smile.")]
    [InlineData("Please do not bring Wood.")]
    [InlineData("Thank you for bringing Wood.")]
    public void DeferredGiftsIncomingAcceptanceAndNonRequestsDoNotCreateRecentActionTopic(string reply)
    {
        AssertCoreOnly(Select("Okay.", history: new[] { Player("Hi."), Npc(reply) }));
    }

    [Fact]
    public void NewCurrentTopicDoesNotReuseEarlierActionNegotiation()
    {
        AssertCoreOnly(Select("最近在读什么书？", history: new[]
        {
            Player("一起去海边吧。"), Npc("可以，我们走吧。")
        }));
    }

    [Fact]
    public void InterveningPlayerTopicStopsRecentRecovery()
    {
        AssertCoreOnly(Select("好的。", history: new[]
        {
            Player("一起去海边吧。"), Npc("可以。"),
            Player("今天读的书真有意思。"), Npc("我也很喜欢那个故事。")
        }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]")]
    [InlineData("一起去 Ridgeside Village 吧。")]
    public void WithheldHistoryIsAHardBoundary(string boundary)
    {
        AssertCoreOnly(Select("好的。", history: new[]
        {
            Player("一起去海边吧。"), Npc("可以。"),
            Player(boundary), Npc("嗯。")
        }));
    }

    [Fact]
    public void RecentRecoveryIsBounded()
    {
        var history = new List<ConversationElement> { Player("一起去海边吧。") };
        for (int index = 0; index < 6; index++)
        {
            history.Add(new ConversationElement("好的。", index % 2 == 0));
        }

        AssertCoreOnly(Select("好的。", history: history));
    }

    [Fact]
    public void ActiveOutingRemainsRelevantWithoutANewInvitation()
    {
        SceneActionContractPlan plan = Select("这里的风真舒服。", Context(extra:
            "## Active Companion Outing\n- Penny and the farmer have an active shared outing to Beach.\n- Phase: spending time together at the destination."));

        Assert.True(plan.IncludeTravel);
        Assert.False(plan.IsFallback);
    }

    [Fact]
    public void MixedCurrentTopicsPreserveAllApplicableFamilies()
    {
        SceneActionContractPlan plan = SceneActionContractSelector.Select(
            "一起去海边吧，能送我一朵花吗？ Could you lend me 50 gold?", Context(helpAllowed: true),
            null, GenerationTrigger.Conversation, isFestival: true);

        Assert.True(plan.IncludeTravel);
        Assert.True(plan.IncludeGifts);
        Assert.True(plan.IncludeNewHelp);
        Assert.True(plan.IncludeMoney);
        Assert.True(plan.IncludeFestival);
        Assert.False(plan.IsFallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Custom behavior context with unknown capabilities.")]
    [InlineData("## LivingNPCs Context: Penny\nCurrent state:\n- No persistent LivingNPCs state exists yet.")]
    public void MissingOrUnknownBehaviorCapabilitiesUseFullContract(string? context)
    {
        AssertFull(Select("Hi.", context, useDefaultContext: false));
    }

    [Fact]
    public void UnknownReadinessAndConflictingCapabilitiesUseFullContract()
    {
        AssertFull(Select("Hi.", Context(extra: "- Help-request readiness: unknown mode.")));
        AssertFull(Select("Hi.", Context(giftOpportunity: true,
            extra: PromptFragments.GiftOpportunity.NoOpportunitySection())));
        AssertFull(Select("Hi.", Context(extra: PromptFragments.HelpRequestOpportunity.Section("Penny"))));
        AssertFull(Select("Hi.", Context(extra: "- Active help request: status FutureUnknownState.")));
    }

    [Fact]
    public void TruncatedGiftOpportunityDoesNotCountAsKnownCapability()
    {
        string context = Context().Replace(PromptFragments.GiftOpportunity.NoOpportunitySection(),
            "## LivingNPCs Gift Opportunity", StringComparison.Ordinal);

        AssertFull(Select("Hi.", context));
    }

    [Theory]
    [InlineData("fr", "Bonjour.")]
    [InlineData("ja", "こんにちは。")]
    [InlineData(null, "Let's go to the Beach. こんにちは。")]
    [InlineData(null, "Привет.")]
    public void UnsupportedAndMixedUnknownLanguagesUseFullContract(string? locale, string player)
    {
        AssertFull(SceneActionContractSelector.Select(player, Context(), null, GenerationTrigger.Conversation, locale: locale));
    }

    [Theory]
    [InlineData("zh-CN", "你好。")]
    [InlineData("zh_TW", "你好。")]
    [InlineData("en-US", "Good morning.")]
    [InlineData("English", "Hello.")]
    public void RecognizedLocalesUseLocalSelection(string locale, string player)
    {
        AssertCoreOnly(SceneActionContractSelector.Select(player, Context(), null, GenerationTrigger.Conversation, locale: locale));
    }

    [Fact]
    public void UnknownRecentLanguageCannotSilentlyNarrowAShortContinuation()
    {
        AssertFull(Select("Okay.", history: new[] { Npc("一緒に海に行きませんか？") }));
    }

    [Theory]
    [InlineData("[WITHHELD_PLAYER_MESSAGE]")]
    [InlineData("一起去 Ridgeside Village 吧。")]
    public void BlockedCurrentTextCannotRecallOrExposeAnOlderAction(string player)
    {
        SceneActionContractPlan plan = Select(player, history: new[] { Player("一起去海边吧。") });

        AssertFull(plan);
        Assert.DoesNotContain("Ridgeside", plan.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlockedMemoryLinesDoNotSelectFamiliesOrInvalidateKnownSafeCapabilities()
    {
        AssertCoreOnly(Select("你好。", Context(extra:
            "- Relevant long-term memories: let's go to Ridgeside Village and give gifts.")));
    }

    [Fact]
    public void UnsupportedTriggerAndExplicitFullPlanKeepAllFamilies()
    {
        AssertFull(SceneActionContractSelector.Select("Hi.", Context(), null, (GenerationTrigger)999));
        AssertFull(SceneActionContractPlan.Full());
    }

    private static SceneActionContractPlan Select(
        string player,
        string? context = null,
        IReadOnlyList<ConversationElement>? history = null,
        bool useDefaultContext = true) => SceneActionContractSelector.Select(
            player, useDefaultContext ? context ?? Context() : context,
            history, GenerationTrigger.Conversation);

    private static string Context(bool helpAllowed = false, bool giftOpportunity = false, string extra = "") => string.Join("\n",
        PromptFragments.Context.Header("Penny"),
        PromptFragments.Context.CurrentStateHeading,
        "- Scene: a quiet afternoon; location: Town.",
        PromptFragments.Context.HelpRequestReadinessLine(helpAllowed
            ? PromptFragments.Context.HelpRequestReadinessAllowed("the relationship is ready")
            : PromptFragments.Context.HelpRequestReadinessBlocked("no new favor is appropriate")),
        giftOpportunity
            ? PromptFragments.GiftOpportunity.Section("Penny", "one small everyday gift today", "(O)20 Leek", "(O)18 Daffodil")
            : PromptFragments.GiftOpportunity.NoOpportunitySection(),
        extra);

    private static ConversationElement Player(string text) => new(text, true);
    private static ConversationElement Npc(string text) => new(text, false);

    private static void AssertCoreOnly(SceneActionContractPlan plan)
    {
        Assert.False(plan.IsFallback, plan.Reason);
        Assert.False(plan.IncludeTravel);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeMoney);
        Assert.False(plan.IncludeFestival);
    }

    private static void AssertFull(SceneActionContractPlan plan)
    {
        Assert.True(plan.IsFallback);
        Assert.True(plan.IncludeTravel);
        Assert.True(plan.IncludeGifts);
        Assert.True(plan.IncludeNewHelp);
        Assert.True(plan.IncludeHelpUpdates);
        Assert.True(plan.IncludeMoney);
        Assert.True(plan.IncludeFestival);
        Assert.NotEmpty(plan.Reason);
    }
}
