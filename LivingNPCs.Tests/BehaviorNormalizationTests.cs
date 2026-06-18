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
    [InlineData("FlowerDance")]
    public void ExpandedLocationsArePublicOutingTargets(string locationName)
    {
        Assert.True(TravelLocationRules.IsKnownPublicOutingTarget(locationName));
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
