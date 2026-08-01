using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue.Content;

namespace LivingNPCs.Tests.Dialogue.Content;

public sealed class SveContentRulesTests
{
    [Theory]
    [InlineData("Abigail", "Abigail")]
    [InlineData("Abigail·", "Abigail")]
    [InlineData("Abigail•", "Abigail")]
    [InlineData("Abigail-", "Abigail")]
    [InlineData(" Abigail ", "Abigail")]
    [InlineData("GuntherSilvian", "Gunther")]
    [InlineData("MarlonFay", "Marlon")]
    [InlineData("MorrisTod", "Morris")]
    [InlineData("HankSVE", "Hank")]
    [InlineData("marlonfay", "Marlon")]
    public void NormalizeName_TrimsSuffixesAndResolvesAliases(string input, string expected)
    {
        Assert.Equal(expected, SveContentRules.NormalizeName(input));
    }

    [Fact]
    public void ExpansionCompatibility_SveCharacterWithSwitchOff_IsDisabled()
    {
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("Olivia", sveCompatibilityEnabled: false));
        Assert.True(SveContentRules.IsExpansionCompatibilityEnabled("Olivia", sveCompatibilityEnabled: true));
        // 别名也要命中名单。
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("GuntherSilvian", sveCompatibilityEnabled: false));
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("MarlonFay", sveCompatibilityEnabled: false));
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("HankSVE", sveCompatibilityEnabled: false));
        // 调用方已经规整过名字时也必须命中。
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("Marlon", sveCompatibilityEnabled: false));
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("Hank", sveCompatibilityEnabled: false));
    }

    [Theory]
    [InlineData("Gunther", "Gunther", "GuntherSilvian")]
    [InlineData("MarlonFay", "Marlon", "MarlonFay")]
    [InlineData("Morris", "Morris", "MorrisTod")]
    [InlineData("HankSVE", "Hank", "HankSVE")]
    public void GameDataLookupNames_UsesSveOriginalThenCanonicalFallback(
        string input,
        string canonical,
        string original)
    {
        Assert.Equal(new[] { original, canonical }, SveContentRules.GetGameDataLookupNames(input));
    }

    [Fact]
    public void GameDataLookupNames_OrdinaryNpc_UsesCanonicalOnly()
    {
        Assert.Equal(new[] { "Abigail" }, SveContentRules.GetGameDataLookupNames("Abigail·"));
    }

    [Fact]
    public void ExpansionCompatibility_RsvBlockedNpc_IsAlwaysDisabled()
    {
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("Bert", sveCompatibilityEnabled: true));
        Assert.False(SveContentRules.IsExpansionCompatibilityEnabled("Bert", sveCompatibilityEnabled: false));
    }

    [Fact]
    public void ExpansionCompatibility_OrdinaryNpc_IsEnabled()
    {
        Assert.True(SveContentRules.IsExpansionCompatibilityEnabled("Abigail", sveCompatibilityEnabled: false));
    }

    [Fact]
    public void MergeWorldDelta_AddsAndOverridesEntries_WithoutMutatingBase()
    {
        var baseSummary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Locations"] = true, ["Villagers"] = true },
            Locations = Section(("TheFarm", "base farm"), ("Beach", "base beach")),
            Villagers = Section(("Abigail", "base abigail"))
        };
        var delta = new WorldSummary
        {
            Locations = Section(("Highlands", "sve highlands"), ("Beach", "sve beach")),
            Villagers = Section(("Sophia", "sve sophia"))
        };

        WorldSummary merged = SveContentRules.MergeWorldDelta(baseSummary, delta);

        Assert.Equal(3, merged.Locations!.Entries.Count);
        Assert.Equal("sve beach", merged.Locations.Entries["Beach"].Description);
        Assert.Equal("sve highlands", merged.Locations.Entries["Highlands"].Description);
        Assert.Contains("Sophia", merged.Villagers!.Entries.Keys);

        // 入参不被改动。
        Assert.Equal(2, baseSummary.Locations!.Entries.Count);
        Assert.Equal("base beach", baseSummary.Locations.Entries["Beach"].Description);
    }

    [Fact]
    public void MergeWorldDelta_NullDelta_ReturnsEquivalentCopy()
    {
        var baseSummary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Villagers"] = true },
            Villagers = Section(("Abigail", "a"))
        };

        WorldSummary merged = SveContentRules.MergeWorldDelta(baseSummary, null);

        Assert.NotSame(baseSummary, merged);
        Assert.Equal("a", merged.Villagers!.Entries["Abigail"].Description);
    }

    [Fact]
    public void ApplyRelationshipPatch_PreservesExistingKeyAndAddsMissingKey()
    {
        var bio = new NpcBio();
        bio.Relationships["Lewis"] = new BioListEntry { Id = "Lewis", Heading = "Lewis", Description = "old" };

        SveContentRules.ApplyRelationshipPatch(bio, new Dictionary<string, BioListEntry>
        {
            ["Lewis"] = new() { Id = "Lewis", Heading = "Lewis", Description = "sve" },
            ["Sophia"] = new() { Id = "Sophia", Heading = "Sophia", Description = "new" }
        });

        Assert.Equal("old", bio.Relationships["Lewis"].Description);
        Assert.Equal("new", bio.Relationships["Sophia"].Description);
    }

    private static WorldSummarySection Section(params (string Key, string Description)[] entries)
    {
        var section = new WorldSummarySection();
        foreach (var (key, description) in entries)
        {
            section.Entries[key] = new WorldSummaryEntry { Id = key, Name = key, Description = description };
        }

        return section;
    }
}
