using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for the ambient-remark visibility decision
/// (<see cref="BehaviorFeedbackService.PlanRemarkTick"/>): the decision must not depend on the
/// tile the remark was queued from. The old "wait until the NPC left the original tile"
/// condition made counter NPCs (Gus, Clint, Willy, Penny), who stand on one tile for hours,
/// silently swallow their delay-0 help-request thanks and dialogue follow-ups until the day
/// change dropped them unseen — the helper's signature has no tile inputs at all.
/// </summary>
public sealed class AmbientRemarkTickPlanTests
{
    private const int Today = TestScenarios.Today;

    [Fact]
    public void ImmediateRemarkShowsEvenWhenNpcNeverMoved()
    {
        // Delay-0 thanks queued mid-conversation with a counter NPC: same day, scheduled time
        // already reached, NPC still in the same location (and, implicitly, on the same tile).
        var plan = BehaviorFeedbackService.PlanRemarkTick(
            remarkTotalDays: Today,
            remarkNotBeforeTimeOfDay: 900,
            remarkLocationName: "Saloon",
            currentTotalDays: Today,
            currentTimeOfDay: 900,
            npcFound: true,
            npcLocationName: "Saloon",
            distanceToPlayerTiles: 1f,
            maxDistanceTiles: 3f);

        Assert.Equal(AmbientRemarkTickPlan.Show, plan);
    }

    [Fact]
    public void RemarkFromAnotherDayIsDropped()
    {
        var plan = BehaviorFeedbackService.PlanRemarkTick(
            remarkTotalDays: Today - 1,
            remarkNotBeforeTimeOfDay: 900,
            remarkLocationName: "Saloon",
            currentTotalDays: Today,
            currentTimeOfDay: 900,
            npcFound: true,
            npcLocationName: "Saloon",
            distanceToPlayerTiles: 1f,
            maxDistanceTiles: 3f);

        Assert.Equal(AmbientRemarkTickPlan.Drop, plan);
    }

    [Fact]
    public void RemarkWaitsUntilItsScheduledTime()
    {
        var plan = BehaviorFeedbackService.PlanRemarkTick(
            remarkTotalDays: Today,
            remarkNotBeforeTimeOfDay: 1010,
            remarkLocationName: "Saloon",
            currentTotalDays: Today,
            currentTimeOfDay: 1000,
            npcFound: true,
            npcLocationName: "Saloon",
            distanceToPlayerTiles: 1f,
            maxDistanceTiles: 3f);

        Assert.Equal(AmbientRemarkTickPlan.Wait, plan);
    }

    [Theory]
    [InlineData(false, "Saloon", 1f)] // NPC vanished from the current location
    [InlineData(true, "Town", 1f)] // NPC moved to another map
    [InlineData(true, "Saloon", 9f)] // player too far away
    [InlineData(true, "Saloon", null)] // player position unknown
    public void RemarkWaitsWhenNpcOrPlayerConditionsAreNotMet(
        bool npcFound,
        string npcLocationName,
        float? distanceToPlayerTiles)
    {
        var plan = BehaviorFeedbackService.PlanRemarkTick(
            remarkTotalDays: Today,
            remarkNotBeforeTimeOfDay: 900,
            remarkLocationName: "Saloon",
            currentTotalDays: Today,
            currentTimeOfDay: 1200,
            npcFound: npcFound,
            npcLocationName: npcLocationName,
            distanceToPlayerTiles: distanceToPlayerTiles,
            maxDistanceTiles: 3f);

        Assert.Equal(AmbientRemarkTickPlan.Wait, plan);
    }
}
