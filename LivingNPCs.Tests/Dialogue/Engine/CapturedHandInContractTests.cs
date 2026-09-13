using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using StardewValley;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class CapturedHandInContractTests
{
    [Theory]
    [InlineData(true, "Pending")]
    [InlineData(true, "Fulfilled")]
    [InlineData(false, "Pending")]
    [InlineData(false, "Fulfilled")]
    public void ProductionHandInRepliesKeepUpdatesWithoutUnrelatedActionDocumentation(bool giftFlow, string status)
    {
        string context = Context(giftFlow, status);
        SceneActionContractPlan plan = Select(context, giftFlow);

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IncludeMoney);
        Assert.False(plan.IncludeTravel);
        Assert.False(plan.IncludeFestival);

        string contract = LivingNpcMetadataContract.BuildSceneInstructions(plan);
        Assert.Contains("\"helpRequestUpdates\"", contract);
        Assert.DoesNotContain("\"helpRequests\"", contract);
        Assert.DoesNotContain("\"actions\"", contract);
        Assert.DoesNotContain("\"giftDecision\"", contract);
        Assert.DoesNotContain("\"travelDecision\"", contract);

        Assert.Contains(status == "Pending" ? "Bread ((O)216)" : "Milk ((O)184)", context);
        Assert.Contains("first Bread, then Milk", context);
        Assert.Contains(status, context);
        if (status == "Fulfilled")
        {
            Assert.Contains("200g", context);
            Assert.Contains("quest journal", context);
            Assert.Contains("tomorrow", context);
        }
        else if (giftFlow)
        {
            Assert.Contains("Current next step", context);
            Assert.Contains("(O)184", context);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHandInHeaderWithoutExplicitCapabilitiesStillKeepsTheFullFallback(bool giftFlow)
    {
        string context = Context(giftFlow).Replace(PromptFragments.HelpRequestHandIn.CapabilityLine, string.Empty);

        Assert.True(Select(context, giftFlow).IsFallback);
    }

    [Fact]
    public void ACapabilityLineWithoutItsRuntimeProfileDoesNotNarrowTheContract()
    {
        string context = Context(true).Replace(PromptFragments.HelpRequestHandIn.Header, "## External context");

        Assert.True(Select(context).IsFallback);
    }

    [Theory]
    [InlineData("## Future capability profile")]
    [InlineData("## Active Companion Outing")]
    [InlineData("## LivingNPCs Gift Opportunity")]
    [InlineData("## LivingNPCs Help Request Opportunity")]
    [InlineData("## LivingNPCs Birthday Gift Mail")]
    [InlineData("## LivingNPCs Reciprocal Gift Mail")]
    [InlineData("## LivingNPCs Context: Penny")]
    [InlineData("Current state:")]
    [InlineData("- Help-request readiness: may naturally ask for one modest favor now.")]
    [InlineData("- Help-request readiness: future unknown readiness.")]
    [InlineData("- Shared small gift IDs: (O)216.")]
    [InlineData("- This authorization applies to this one reply only.")]
    public void MixedIncompleteOrUnknownCapabilitiesRetainFullDocumentation(string extra)
    {
        Assert.True(Select(Context(true) + "\n" + extra).IsFallback);
    }

    [Fact]
    public void CachedHandInRecordDoesNotRevokeANewOpportunityInAnOrdinarySnapshot()
    {
        string context = SceneMetadataContractTests.RestrictedContext
            .Replace("should not open a new help request now", "may naturally ask for one modest favor now")
            + "\n" + Context(false);

        SceneActionContractPlan plan = Select(context);

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeNewHelp);
        Assert.True(plan.IncludeHelpUpdates);
    }

    [Fact]
    public void CachedHandInRecordDoesNotRevokeASeparatelyAuthorizedGift()
    {
        string context = SceneMetadataContractTests.RestrictedContext.Replace(
            PromptFragments.GiftOpportunity.NoOpportunitySection(),
            PromptFragments.GiftOpportunity.Section("Penny", "one small gift today", "(O)216 Bread", "(O)184 Milk"))
            + "\n" + Context(false);

        SceneActionContractPlan plan = Select(context, giftFlow: false);

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeGifts);
        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeNewHelp);
        Assert.False(plan.IncludeMoney);
    }

    [Theory]
    [InlineData("要不要一起去海边？")]
    [InlineData("Let's go to the Beach.")]
    public void AnExplicitCurrentInvitationStillKeepsTravelDocumentation(string player)
    {
        SceneActionContractPlan plan = Select(Context(true), player: player);

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeTravel);
        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeGifts);
        Assert.False(plan.IncludeNewHelp);
    }

    [Fact]
    public void CapturedFestivalStateStillKeepsFestivalDocumentation()
    {
        SceneActionContractPlan plan = SceneActionContractSelector.Select(
            string.Empty, Context(true), null, GenerationTrigger.Gift, isFestival: true);

        Assert.False(plan.IsFallback);
        Assert.True(plan.IncludeFestival);
        Assert.True(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeGifts);
    }

    private static SceneActionContractPlan Select(string context, bool giftFlow = true, string player = "") =>
        SceneActionContractSelector.Select(player, context, null,
            giftFlow ? GenerationTrigger.Gift : GenerationTrigger.Conversation, locale: "en");

    private static string Context(bool giftFlow, string status = "Fulfilled")
    {
        var npc = new NPC { Name = "Penny", displayName = "Penny" };
        var gift = status == "Pending"
            ? new GiftMemoryDetails("(O)216", "Bread", "neutral", "neutral", 8)
            : new GiftMemoryDetails("(O)184", "Milk", "neutral", "neutral", 8);
        var request = new NpcHelpRequestFact
        {
            Type = "item_request",
            Status = status,
            Summary = "Bring first Bread, then Milk.",
            Resolution = "The farmer handed in " + gift.ItemName + ".",
            RequestedItemId = "(O)184",
            RequestedItemLabel = "Milk",
            CurrentStepIndex = 1,
            RewardGranted = status == "Fulfilled",
            RewardFriendship = 50,
            RewardMoney = 200,
            RewardMoneyClaimQueued = status == "Fulfilled",
            RewardGiftGiven = status == "Fulfilled",
            Steps =
            [
                new NpcHelpRequestStepFact
                {
                    Type = "item_request", Status = "Fulfilled", Summary = "Bring Bread.",
                    RequestedItemId = "(O)216", RequestedItemLabel = "Bread"
                },
                new NpcHelpRequestStepFact
                {
                    Type = "item_request", Status = status, Summary = "Bring Milk.",
                    RequestedItemId = "(O)184", RequestedItemLabel = "Milk"
                }
            ]
        };

        return giftFlow
            ? ValleyTalkContextService.BuildHelpRequestGiftResponsePrompt(npc, gift, [request])
            : ConversationStartRecorder.BuildHelpRequestDeliveryPrompt(npc, gift, [request]);
    }
}
