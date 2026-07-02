using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class BehaviorNormalizationTests
{
    [Theory]
    [InlineData("Pelican Town", "Town")]
    [InlineData("潘妮家", "Trailer")]
    [InlineData("Trailer_Big", "Trailer")]
    [InlineData("The Mines", "Mine")]
    [InlineData("Blue Moon Vineyard", "Custom_BlueMoonVineyard")]
    [InlineData("花舞节", "FlowerDance")]
    public void TravelAliasesNormalizeToCanonicalLocations(string input, string expected)
    {
        Assert.Equal(expected, TravelLocationRules.Normalize(input, string.Empty));
    }

    [Theory]
    [InlineData("Custom_GrampletonCoast")]
    [InlineData("Custom_BlueMoonVineyard")]
    public void ExpandedLocationsArePublicOutingTargets(string locationName)
    {
        Assert.True(TravelLocationRules.IsKnownPublicOutingTarget(locationName));
    }

    [Fact]
    public void FlowerDanceIsNotAnOutingTargetButStillNormalizes()
    {
        // No GameLocation named "FlowerDance" exists (the festival loads on a Temp map), so it
        // must not be offered as an outing destination; the alias stays for festival anchors.
        Assert.False(TravelLocationRules.IsKnownPublicOutingTarget("FlowerDance"));
        Assert.Equal("FlowerDance", TravelLocationRules.Normalize("花舞节", string.Empty));
    }

    [Fact]
    public void MemoryTagsCombineExplicitAndInferredSignals()
    {
        var tags = BehaviorValueNormalizer.NormalizeMemoryTags(
            ["drink", "forbidden_tag"],
            "The farmer brought coffee to the library."
        );

        Assert.Contains("drink", tags);
        Assert.Contains("comfort", tags);
        Assert.Contains("work", tags);
        Assert.Contains("scholarly", tags);
        Assert.DoesNotContain("forbidden_tag", tags);
    }
}
