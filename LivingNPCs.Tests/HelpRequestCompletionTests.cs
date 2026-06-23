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
