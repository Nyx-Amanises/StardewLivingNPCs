using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for shared-time accrual during a companion outing stay (#8): minutes count
/// only while BOTH the NPC and the farmer are at the destination. Previously the farmer waiting
/// alone kept accruing shared minutes while an event script or another mod had pulled the NPC
/// off-map, which could fabricate a "spent time together" memory.
/// </summary>
public sealed class CompanionOutingSharedMinutesTests
{
    [Fact]
    public void BothPresentAccruesElapsedMinutes()
    {
        // 9:50 -> 10:10 in Stardew clock values crosses the hour boundary: 20 minutes.
        Assert.Equal(
            20,
            CompanionOutingRuntime.ComputeSharedMinutesGain(
                npcAtDestination: true,
                farmerAtDestination: true,
                lastObservedTimeOfDay: 950,
                currentTimeOfDay: 1010));
    }

    [Fact]
    public void NpcPulledOffMapAccruesNothing()
    {
        Assert.Equal(
            0,
            CompanionOutingRuntime.ComputeSharedMinutesGain(
                npcAtDestination: false,
                farmerAtDestination: true,
                lastObservedTimeOfDay: 950,
                currentTimeOfDay: 1010));
    }

    [Fact]
    public void FarmerElsewhereAccruesNothing()
    {
        Assert.Equal(
            0,
            CompanionOutingRuntime.ComputeSharedMinutesGain(
                npcAtDestination: true,
                farmerAtDestination: false,
                lastObservedTimeOfDay: 950,
                currentTimeOfDay: 1010));
    }

    [Fact]
    public void BackwardsClockIsClampedToZero()
    {
        Assert.Equal(
            0,
            CompanionOutingRuntime.ComputeSharedMinutesGain(
                npcAtDestination: true,
                farmerAtDestination: true,
                lastObservedTimeOfDay: 1010,
                currentTimeOfDay: 950));
    }
}
