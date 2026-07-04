using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Drives the outing state machine's per-tick decision core with a fake world view — the piece
/// that used to be untestable because it read Game1 inline (see IOutingWorldView).
/// </summary>
public sealed class CompanionOutingTickPlanTests
{
    private sealed class FakeOutingWorldView : IOutingWorldView
    {
        public int TimeOfDay { get; set; } = 1200;
        public int TotalDays { get; set; } = 100;
        public bool IsMasterGame { get; set; } = true;
        public bool IsFestivalToday { get; set; }
        public bool IsLightning { get; set; }
        public bool IsEventActive { get; set; }
        public bool IsMenuOpen { get; set; }
        public string CurrentLocationName { get; set; } = "Town";
    }

    [Fact]
    public void StaleDayOrMissingNpcDropsTheOuting()
    {
        var world = new FakeOutingWorldView { TotalDays = 101 };

        Assert.Equal(
            CompanionOutingTickPlan.DropStaleOuting,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.AtDestination));
        Assert.Equal(
            CompanionOutingTickPlan.DropStaleOuting,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 101, npcExists: false, CompanionOutingPhase.Traveling));
    }

    [Fact]
    public void EventSceneStopsTheOutingBeforeAnyOtherCheck()
    {
        // Even with a menu open and the clock past the travel deadline, the event stop wins.
        var world = new FakeOutingWorldView { IsEventActive = true, IsMenuOpen = true, TimeOfDay = 2600 };

        Assert.Equal(
            CompanionOutingTickPlan.StopForEventScene,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.AtDestination));
    }

    [Fact]
    public void LateClockAbortsTravelButNotAnArrivedStay()
    {
        var world = new FakeOutingWorldView { TimeOfDay = CompanionOutingRules.LatestPlannedStayEndTime };

        Assert.Equal(
            CompanionOutingTickPlan.StopForLateTravel,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.Traveling));
        Assert.Equal(
            CompanionOutingTickPlan.StopForLateTravel,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.TravelingToFarmBoundary));

        // An outing that already arrived is handled by its own stay-until clock instead.
        Assert.Equal(
            CompanionOutingTickPlan.AdvanceStay,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.AtDestination));
    }

    [Fact]
    public void OpenMenuPausesTheOutingWithoutStoppingIt()
    {
        var world = new FakeOutingWorldView { IsMenuOpen = true };

        Assert.Equal(
            CompanionOutingTickPlan.WaitForMenu,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.Traveling));
    }

    [Fact]
    public void PhasesDispatchToTheirAdvanceActions()
    {
        var world = new FakeOutingWorldView();

        Assert.Equal(
            CompanionOutingTickPlan.AdvanceTravel,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.TravelingFromFarmBoundary));
        Assert.Equal(
            CompanionOutingTickPlan.AdvanceStay,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.AtDestination));
        Assert.Equal(
            CompanionOutingTickPlan.AdvanceReturn,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.Returning));
        Assert.Equal(
            CompanionOutingTickPlan.AdvanceReturn,
            CompanionOutingRules.PlanTick(world, outingTotalDays: 100, npcExists: true, CompanionOutingPhase.ReturningFromFarmBoundary));
    }
}
