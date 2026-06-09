using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class GiftSelectionTests
{
    [Fact]
    public void CatalogContainsNinetySixReachableGifts()
    {
        var reachableIds = Enum.GetValues<GiftTier>()
            .SelectMany(tier => GiftCatalog.GetCommonCandidates(tier)
                .Concat(GiftCatalog.VanillaPersonalizedPools.Keys.SelectMany(npcName =>
                    GiftCatalog.GetPersonalizedCandidates(npcName, tier)
                )))
            .Select(candidate => candidate.ItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(96, GiftCatalog.CandidateCount);
        Assert.Equal(GiftCatalog.CandidateCount, reachableIds.Count);
    }

    [Fact]
    public void EveryVanillaGiftableNpcHasPersonalizedSmallAndMeaningfulGifts()
    {
        string[] expectedNpcNames =
        [
            "Abigail", "Alex", "Caroline", "Clint", "Demetrius", "Dwarf",
            "Elliott", "Emily", "Evelyn", "George", "Gus", "Haley", "Harvey",
            "Jas", "Jodi", "Kent", "Krobus", "Leah", "Leo", "Lewis", "Linus",
            "Marnie", "Maru", "Pam", "Penny", "Pierre", "Robin", "Sam", "Sandy",
            "Sebastian", "Shane", "Vincent", "Willy", "Wizard"
        ];

        Assert.Equal(expectedNpcNames.Length, GiftCatalog.VanillaPersonalizedPools.Count);
        foreach (string npcName in expectedNpcNames)
        {
            Assert.True(GiftCatalog.VanillaPersonalizedPools.ContainsKey(npcName));
            Assert.NotEmpty(GiftCatalog.GetPersonalizedCandidates(npcName, GiftTier.Small));
            Assert.NotEmpty(GiftCatalog.GetPersonalizedCandidates(npcName, GiftTier.Meaningful));
        }
    }

    [Fact]
    public void ModNpcUsesOnlySharedPoolForNow()
    {
        Assert.Empty(GiftCatalog.GetPersonalizedCandidates("Sophia", GiftTier.Small));
        Assert.Empty(GiftCatalog.GetPersonalizedCandidates("June", GiftTier.Meaningful));
        Assert.Equal(
            GiftCatalog.GetCommonCandidates(GiftTier.Small).Count,
            GiftCatalog.GetAvailableCandidates("Sophia", GiftTier.Small).Count
        );
    }

    [Fact]
    public void LowerScoredCandidatesRetainNonZeroWeight()
    {
        double bestWeight = GiftSelector.CalculateSelectionWeight(80, 80, personalized: false);
        double longTailWeight = GiftSelector.CalculateSelectionWeight(-20, 80, personalized: false);
        double personalizedWeight = GiftSelector.CalculateSelectionWeight(80, 80, personalized: true);

        Assert.True(bestWeight > 0);
        Assert.True(longTailWeight > 0);
        Assert.True(personalizedWeight > bestWeight);
    }

    [Fact]
    public void RecentGiftIdsAreRemovedWhenAlternativesExist()
    {
        IReadOnlyList<GiftCandidate> candidates = GiftCatalog.GetCommonCandidates(GiftTier.Small);
        var recentIds = candidates
            .Take(3)
            .Select(candidate => candidate.ItemId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<GiftCandidate> filtered = GiftSelector.ExcludeRecentCandidates(candidates, recentIds);

        Assert.DoesNotContain(filtered, candidate => recentIds.Contains(candidate.ItemId));
        Assert.Equal(candidates.Count - recentIds.Count, filtered.Count);
    }

    [Theory]
    [InlineData(0.00, 0)]
    [InlineData(0.24, 0)]
    [InlineData(0.25, 1)]
    [InlineData(0.74, 1)]
    [InlineData(0.75, 2)]
    [InlineData(0.99, 2)]
    public void WeightedDrawUsesTheFullProbabilityRange(double roll, int expectedIndex)
    {
        int index = GiftSelector.ChooseWeightedIndex([1, 2, 1], roll);

        Assert.Equal(expectedIndex, index);
    }
}
