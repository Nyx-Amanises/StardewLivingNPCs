using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class BehaviorNormalizationTests
{
    [Theory]
    [InlineData("Pelican Town", "Town")]
    [InlineData("潘妮家", "Trailer")]
    [InlineData("Trailer_Big", "Trailer")]
    [InlineData("The Mines", "Mine")]
    public void TravelAliasesNormalizeToCanonicalLocations(string input, string expected)
    {
        Assert.Equal(expected, TravelLocationRules.Normalize(input, string.Empty));
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
