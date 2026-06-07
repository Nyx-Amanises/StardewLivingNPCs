using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class HelpRequestGateTests
{
    [Fact]
    public void LowTrustIsRejected()
    {
        var state = TestScenarios.TrustedState();
        state.RelationshipTrust = 25;

        var result = Evaluate(state);

        Assert.False(result.Allowed);
        Assert.Contains("not close enough", result.Reason);
    }

    [Fact]
    public void UnresolvedConflictIsRejected()
    {
        var state = TestScenarios.TrustedState();
        state.Conflicts.Add(new NpcConflictFact
        {
            Summary = "The farmer ignored a clear boundary.",
            Severity = 35,
            Status = "Active"
        });

        var result = Evaluate(state);

        Assert.False(result.Allowed);
        Assert.Contains("unresolved conflict", result.Reason);
    }

    [Fact]
    public void CooldownIsRejected()
    {
        var state = TestScenarios.TrustedState();
        state.LastHelpRequestTotalDays = TestScenarios.Today - 1;

        var result = Evaluate(state, cooldownDays: 3);

        Assert.False(result.Allowed);
        Assert.Contains("too fresh", result.Reason);
    }

    [Fact]
    public void ExistingOfferedOrPendingRequestIsRejected()
    {
        var state = TestScenarios.TrustedState();
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Summary = "Bring Emily a flower.",
            Status = "Pending"
        });

        var result = Evaluate(state, maxPendingRequests: 1);

        Assert.False(result.Allowed);
        Assert.Contains("already pending", result.Reason);
    }

    [Fact]
    public void TrustedRelationshipWithoutBlockersIsAllowed()
    {
        var result = Evaluate(TestScenarios.TrustedState());

        Assert.True(result.Allowed);
    }

    private static HelpRequestReadinessResult Evaluate(
        LivingNpcState state,
        int maxPendingRequests = 1,
        int cooldownDays = 3,
        int minTrust = 60,
        int friendshipHearts = 6)
    {
        return BehaviorMemory.EvaluateHelpRequestReadiness(
            state,
            friendshipHearts,
            maxPendingRequests,
            cooldownDays,
            minTrust,
            TestScenarios.Today
        );
    }
}
