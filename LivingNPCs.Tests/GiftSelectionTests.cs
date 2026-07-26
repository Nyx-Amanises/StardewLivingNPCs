using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class GiftSelectionTests
{
    [Fact]
    public void CatalogContainsOnlyReachableGifts()
    {
        var reachableIds = Enum.GetValues<GiftTier>()
            .SelectMany(tier => GiftCatalog.GetCommonCandidates(tier)
                .Concat(GiftCatalog.VanillaPersonalizedPools.Keys
                    .Concat(GiftCatalog.SvePersonalizedPools.Keys)
                    .SelectMany(npcName => GiftCatalog.GetPersonalizedCandidates(npcName, tier)
                )))
            .Select(candidate => candidate.ItemId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 2026-07 扩容：96 → 122（新增 26 件个性池专属条目）。
        Assert.Equal(122, GiftCatalog.CandidateCount);
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
    public void SveCoreCastHasPersonalizedPoolsAndUnknownModNpcStaysShared()
    {
        // SVE 核心角色现在有个性池（含 Morris 的 SVE 内部名 MorrisTod）。
        string[] sveNames =
        [
            "Claire", "Sophia", "Andy", "Susan", "Olivia", "Victor",
            "Lance", "Scarlett", "Gunther", "Martin", "Morris", "MorrisTod"
        ];
        Assert.Equal(sveNames.Length, GiftCatalog.SvePersonalizedPools.Count);
        foreach (string npcName in sveNames)
        {
            Assert.True(GiftCatalog.SvePersonalizedPools.ContainsKey(npcName));
            Assert.NotEmpty(GiftCatalog.GetPersonalizedCandidates(npcName, GiftTier.Small));
            Assert.NotEmpty(GiftCatalog.GetPersonalizedCandidates(npcName, GiftTier.Meaningful));
        }

        // 未收录的第三方 NPC 仍只用公共池。
        Assert.Empty(GiftCatalog.GetPersonalizedCandidates("June", GiftTier.Small));
        Assert.Empty(GiftCatalog.GetPersonalizedCandidates("June", GiftTier.Meaningful));
        Assert.Equal(
            GiftCatalog.GetCommonCandidates(GiftTier.Small).Count,
            GiftCatalog.GetAvailableCandidates("June", GiftTier.Small).Count
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
