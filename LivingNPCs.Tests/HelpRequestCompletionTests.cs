using System.Collections.Generic;
using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Reward-integrity guard: an AI "fulfilled" update in the dialogue must never complete an
/// item_request or trigger a reward on its own. Completion only happens when the farmer actually
/// hands the item over (TryCompleteItemHelpRequests, which consumes the item).
/// </summary>
public sealed class HelpRequestCompletionTests
{
    [Theory]
    [InlineData("给你，这就是你要的石英。")]
    [InlineData("Here you go, I brought the quartz.")]
    public void AiTextAloneDoesNotCompletePendingItemRequest(string playerText)
    {
        var state = TestScenarios.TrustedState();
        var request = PendingItemRequest();
        state.HelpRequests.Add(request);

        bool applied = NewService().ApplyUpdate(state, FulfilledUpdate(), playerText, out var fulfilled);

        Assert.False(applied);
        Assert.Null(fulfilled);
        Assert.Equal("Pending", request.Status);
        Assert.False(request.RewardGranted);
    }

    [Theory]
    [InlineData("今天天气真好。")]
    [InlineData("你最近在忙什么？")]
    public void AiAcceptedUpdateWithoutFarmerAffirmationIsIgnored(string playerText)
    {
        var state = TestScenarios.TrustedState();
        var request = PendingItemRequest();
        request.Status = "Offered";
        request.AcceptedTotalDays = -1;
        state.HelpRequests.Add(request);

        bool applied = NewService().ApplyUpdate(
            state,
            new ValleyTalkHelpRequestUpdateCandidate
            {
                Summary = "Bring Emily some quartz.",
                Status = "accepted",
                Resolution = string.Empty
            },
            playerText,
            out _);

        Assert.False(applied);
        Assert.Equal("Offered", request.Status);
        Assert.Equal(-1, request.AcceptedTotalDays);
    }

    [Theory]
    [InlineData("今天天气真好。")]
    [InlineData("石英听起来不错。")]
    public void AiDeclinedUpdateWithoutFarmerRefusalIsIgnored(string playerText)
    {
        var state = TestScenarios.TrustedState();
        var request = PendingItemRequest();
        request.Status = "Offered";
        state.HelpRequests.Add(request);

        bool applied = NewService().ApplyUpdate(
            state,
            new ValleyTalkHelpRequestUpdateCandidate
            {
                Summary = "Bring Emily some quartz.",
                Status = "declined",
                Resolution = string.Empty
            },
            playerText,
            out _);

        Assert.False(applied);
        Assert.Equal("Offered", request.Status);
    }

    [Fact]
    public void UnclaimedMoneyRewardIsNeverEvictedFirst()
    {
        // The retention rank must keep a fulfilled request with queued, unclaimed money ahead of
        // every other status; evicting it deletes the proxy quest and silently destroys the payout.
        var claimable = new NpcHelpRequestFact
        {
            Status = "Fulfilled",
            RewardMoney = 500,
            RewardMoneyClaimQueued = true,
            RewardMoneyGranted = false
        };
        var claimed = new NpcHelpRequestFact { Status = "Fulfilled", RewardMoney = 500, RewardMoneyGranted = true };
        var pending = new NpcHelpRequestFact { Status = "Pending" };
        var expired = new NpcHelpRequestFact { Status = "Expired" };

        int claimableRank = BehaviorMemory.HelpRequestRetentionRank(claimable);
        Assert.True(claimableRank < BehaviorMemory.HelpRequestRetentionRank(pending));
        Assert.True(claimableRank < BehaviorMemory.HelpRequestRetentionRank(expired));
        Assert.True(claimableRank < BehaviorMemory.HelpRequestRetentionRank(claimed));
    }

    private static NpcHelpRequestFact PendingItemRequest()
    {
        return new NpcHelpRequestFact
        {
            Type = "item_request",
            Summary = "Bring Emily some quartz.",
            Status = "Pending",
            RequestedItemId = "(O)80",
            RequestedItemLabel = "Quartz",
            CurrentStepIndex = 0,
            Steps = new List<NpcHelpRequestStepFact>
            {
                new()
                {
                    Type = "item_request",
                    RequestedItemId = "(O)80",
                    RequestedItemLabel = "Quartz",
                    Status = "Pending"
                }
            }
        };
    }

    private static ValleyTalkHelpRequestUpdateCandidate FulfilledUpdate()
    {
        return new ValleyTalkHelpRequestUpdateCandidate
        {
            Summary = "Bring Emily some quartz.",
            Status = "fulfilled",
            Resolution = string.Empty
        };
    }

    private static HelpRequestMemoryService NewService()
    {
        return new HelpRequestMemoryService(
            (_, _, _) => { },
            (_, _) => { },
            (_, _, _, _) => { },
            (_, _, _, _) => null!,
            (_, _) => { }
        );
    }
}
