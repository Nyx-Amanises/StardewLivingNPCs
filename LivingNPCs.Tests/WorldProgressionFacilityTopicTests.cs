using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for the facility-topic detection (#6): route-specific key pairs
/// (cc_* / joja_*) and the active-topic window must both count. Previously only expired cc_*
/// topics were checked, so Joja saves reported the bus/greenhouse/minecarts as never repaired
/// and every save missed the first days right after a repair.
/// </summary>
public sealed class WorldProgressionFacilityTopicTests
{
    [Fact]
    public void ExpiredCommunityCenterTopicCounts()
    {
        var previous = new Dictionary<string, int> { ["cc_Bus"] = 4 };

        Assert.True(WorldProgression.HasFacilityTopic(_ => false, previous, "cc_Bus", "joja_Bus"));
    }

    [Fact]
    public void ExpiredJojaTopicCounts()
    {
        var previous = new Dictionary<string, int> { ["joja_Greenhouse"] = 2 };

        Assert.True(WorldProgression.HasFacilityTopic(_ => false, previous, "cc_Greenhouse", "joja_Greenhouse"));
    }

    [Fact]
    public void ActiveTopicCountsDuringItsFirstDays()
    {
        // Right after the repair the topic is still active and not yet in the previous set.
        Assert.True(WorldProgression.HasFacilityTopic(
            key => key == "joja_Bus",
            previousActivities: null,
            "cc_Bus",
            "joja_Bus"));
    }

    [Fact]
    public void NoTopicMeansNotUnlocked()
    {
        Assert.False(WorldProgression.HasFacilityTopic(
            _ => false,
            new Dictionary<string, int>(),
            "cc_Minecart",
            "joja_Minecart"));
        Assert.False(WorldProgression.HasFacilityTopic(_ => false, previousActivities: null, "movieTheater"));
    }

    [Fact]
    public void UnrelatedTopicsDoNotCount()
    {
        var previous = new Dictionary<string, int> { ["cc_Boulder"] = 3 };

        Assert.False(WorldProgression.HasFacilityTopic(
            key => key == "joja_Boulder",
            previous,
            "cc_Bus",
            "joja_Bus"));
    }
}
