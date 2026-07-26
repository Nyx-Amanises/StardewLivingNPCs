using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for the farmhand gate on host-simulated small behaviors (#5): movement
/// intents (PathFindController / Halt based) are classified as host-only so
/// <c>BehaviorEngine.CanExecute</c> rejects them on farmhands before anything is recorded or
/// charged against the daily budget, while purely visual intents (facing, emotes, glances) and
/// the purely local visit_location influence stay available everywhere.
/// </summary>
public sealed class HostOnlyBehaviorGateTests
{
    [Fact]
    public void MovementIntentsRequireHostSimulation()
    {
        Assert.True(BehaviorEngine.RequiresHostSimulation(BehaviorIntentType.ApproachPlayer));
        Assert.True(BehaviorEngine.RequiresHostSimulation(BehaviorIntentType.StepAway));
        Assert.True(BehaviorEngine.RequiresHostSimulation(BehaviorIntentType.Pause));
    }

    [Fact]
    public void VisualIntentsStayAvailableOnFarmhands()
    {
        Assert.False(BehaviorEngine.RequiresHostSimulation(BehaviorIntentType.FacePlayer));
        Assert.False(BehaviorEngine.RequiresHostSimulation(BehaviorIntentType.Emote));
        Assert.False(BehaviorEngine.RequiresHostSimulation(BehaviorIntentType.LookAround));
    }

    [Theory]
    [InlineData("stay_near", true)]
    [InlineData("comforted", true)]
    [InlineData("offended", true)]
    [InlineData("give_space", true)]
    [InlineData("pause_to_talk", true)]
    [InlineData("visit_location", false)]
    public void MovementInfluencesRequireHostSimulation(string influenceType, bool expected)
    {
        Assert.Equal(expected, DialogueBehaviorInfluenceRuntime.InfluenceRequiresHostSimulation(influenceType));
    }
}
