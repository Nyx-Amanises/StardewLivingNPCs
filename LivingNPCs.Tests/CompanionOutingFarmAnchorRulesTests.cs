using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Behavior;
using Microsoft.Xna.Framework;
using Xunit;

namespace LivingNPCs.Tests;

public sealed class CompanionOutingFarmAnchorRulesTests
{
    [Fact]
    public void ActualArrivalTileOverridesInferredFarmEntry()
    {
        var inferred = new[] { new Point(1, 2) };
        Point actual = new(7, 4);

        IReadOnlyList<Point> seeds = CompanionOutingAnchorSelector.ResolveFarmReachabilitySeedsForTesting(
            actual,
            inferred
        );

        Assert.Equal(new[] { actual }, seeds);
    }

    [Fact]
    public void InferredBlockedFarmEntryKeepsCompatibilityFallback()
    {
        Assert.False(CompanionOutingAnchorSelector.ShouldEnforceFarmReachabilityForTesting(
            hasActualFarmOrigin: false,
            seedCount: 1,
            reachableTileCount: 0));
        Assert.True(CompanionOutingAnchorSelector.ShouldEnforceFarmReachabilityForTesting(
            hasActualFarmOrigin: false,
            seedCount: 1,
            reachableTileCount: 12));
    }

    [Fact]
    public void ActualFarmArrivalAlwaysEnforcesItsReachableComponent()
    {
        Assert.True(CompanionOutingAnchorSelector.ShouldEnforceFarmReachabilityForTesting(
            hasActualFarmOrigin: true,
            seedCount: 1,
            reachableTileCount: 0));
    }

    [Fact]
    public void ReachabilityStartsFromActualArrivalComponent()
    {
        Point inferred = new(1, 2);
        Point actual = new(5, 2);
        var wall = new HashSet<Point>(Enumerable.Range(0, 5).Select(y => new Point(3, y)));
        IReadOnlyList<Point> seeds = CompanionOutingAnchorSelector.ResolveFarmReachabilitySeedsForTesting(
            actual,
            [inferred]
        );

        IReadOnlySet<Point> reachable = CompanionOutingAnchorSelector.BuildReachableTileSetForTesting(
            width: 7,
            height: 5,
            seeds,
            tile => !wall.Contains(tile),
            forceSeedTiles: true
        );

        Assert.Contains(actual, reachable);
        Assert.Contains(new Point(6, 2), reachable);
        Assert.DoesNotContain(inferred, reachable);
    }

    [Fact]
    public void DynamicObstacleBreaksFarmReachability()
    {
        Point obstacle = new(3, 1);
        IReadOnlySet<Point> reachable = CompanionOutingAnchorSelector.BuildReachableTileSetForTesting(
            width: 7,
            height: 3,
            [new Point(1, 1)],
            tile => tile.Y == 1 && tile != obstacle,
            forceSeedTiles: true
        );

        Assert.Contains(new Point(2, 1), reachable);
        Assert.DoesNotContain(obstacle, reachable);
        Assert.DoesNotContain(new Point(5, 1), reachable);
    }

    [Fact]
    public void BlockedActualOriginIsStillASeedButDoesNotOpenBlockedMap()
    {
        Point actual = new(2, 2);
        IReadOnlySet<Point> reachable = CompanionOutingAnchorSelector.BuildReachableTileSetForTesting(
            width: 5,
            height: 5,
            [actual],
            _ => false,
            forceSeedTiles: true
        );

        Assert.Equal(new[] { actual }, reachable);
    }

    [Fact]
    public void FarmhouseYardBiasPrefersBelowAndSlightlyBesideDoor()
    {
        Point door = new(71, 13);
        int belowBeside = CompanionOutingAnchorSelector.ScoreFarmhouseYardBias(new Point(75, 18), door);
        int directlyBelow = CompanionOutingAnchorSelector.ScoreFarmhouseYardBias(new Point(71, 18), door);
        int aboveDoor = CompanionOutingAnchorSelector.ScoreFarmhouseYardBias(new Point(75, 10), door);

        Assert.True(belowBeside > directlyBelow);
        Assert.True(directlyBelow > aboveDoor);
    }

    [Fact]
    public void FarmEntranceCannotBeUsedAsArrivalFallback()
    {
        Assert.False(CompanionOutingRuntime.CanUseDestinationEntryFallbackForTesting("Farm"));
        Assert.True(CompanionOutingRuntime.CanUseDestinationEntryFallbackForTesting("Beach"));
    }

    [Fact]
    public void AbortedFarmArrivalMayUseEmergencyScheduleTeleport()
    {
        Assert.True(CompanionOutingRuntime.ShouldAllowScheduleFallbackTeleportForTesting(
            emergencyRecovery: true,
            isFarmTarget: true,
            npcIsOffscreen: false));
        Assert.False(CompanionOutingRuntime.ShouldAllowScheduleFallbackTeleportForTesting(
            emergencyRecovery: false,
            isFarmTarget: true,
            npcIsOffscreen: true));
    }

    [Fact]
    public void EmptyLocalPathIsRejectedImmediately()
    {
        Assert.False(NpcTravelRuntime.HasUsableLocalPathForTesting(null));
        Assert.False(NpcTravelRuntime.HasUsableLocalPathForTesting(new Stack<Point>()));
        Assert.True(NpcTravelRuntime.HasUsableLocalPathForTesting(
            new Stack<Point>(new[] { new Point(2, 3) })));
    }

    [Theory]
    [InlineData(156, 65, 118, 26)]
    [InlineData(85, 50, 71, 13)]
    public void CustomFarmhouseCoordinatesProduceInBoundsPreferredYard(
        int width,
        int height,
        int doorX,
        int doorY)
    {
        Point door = new(doorX, doorY);
        Point yard = new(doorX + 4, doorY + 5);

        Assert.InRange(yard.X, 0, width - 1);
        Assert.InRange(yard.Y, 0, height - 1);
        Assert.True(CompanionOutingAnchorSelector.ScoreFarmhouseYardBias(yard, door) > 0);
    }
}
