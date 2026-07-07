using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Content;

[Collection("LlmLayer")]
public sealed class WorldSummaryRendererTests : IDisposable
{
    private readonly FakeMonitor monitor = new();
    private readonly Dictionary<string, string> prompts = new()
    {
        ["generalAnd"] = "and",
        ["seasonCrops"] = "Crops include",
        ["seasonForage"] = "Forage includes",
        ["gameSummaryTranslations"] = ""
    };

    public WorldSummaryRendererTests()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, this.monitor, new());
    }

    public void Dispose()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, null!, new());
    }

    private WorldSummaryRenderer CreateRenderer()
    {
        return new WorldSummaryRenderer(
            key => this.prompts.TryGetValue(key, out string? value) ? value : string.Empty,
            this.monitor);
    }

    [Fact]
    public void Render_FollowsSectionOrderAndHeadingFlags()
    {
        var summary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Intro"] = false, ["Villagers"] = true },
            Intro = new WorldSummarySection { Text = "intro text" },
            Villagers = new WorldSummarySection
            {
                Entries = { ["Abigail"] = new WorldSummaryEntry { Name = "Abigail", Description = "likes games" } }
            }
        };

        string text = this.CreateRenderer().Render(summary);

        Assert.DoesNotContain("### Intro", text);
        Assert.Contains("intro text", text);
        Assert.Contains("### Villagers :", text);
        Assert.Contains("- **Abigail** - likes games", text);
        // Intro 在 Villagers 之前。
        Assert.True(text.IndexOf("intro text", StringComparison.Ordinal) < text.IndexOf("Abigail", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_SeasonEntries_AppendCropAndForageLists()
    {
        var summary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Seasons"] = true },
            Seasons = new WorldSummarySection
            {
                Entries =
                {
                    ["Spring"] = new WorldSummaryEntry
                    {
                        Name = "Spring",
                        Description = "It rains.",
                        Crops = new List<string> { "Parsnip", "Potato", "Kale" },
                        Forage = new List<string> { "Leek", "Daffodil" }
                    }
                }
            }
        };

        string text = this.CreateRenderer().Render(summary);

        Assert.Contains("- **Spring** - It rains. Crops include Parsnip, Potato and Kale. Forage includes Leek and Daffodil.", text);
    }

    [Fact]
    public void Render_LocationEntries_GroupByRegion()
    {
        var summary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Locations"] = true },
            Locations = new WorldSummarySection
            {
                Entries =
                {
                    ["Farm"] = new WorldSummaryEntry { Name = "Farm", Description = "farm", Region = "Valley" },
                    ["Desert"] = new WorldSummaryEntry { Name = "Desert", Description = "desert", Region = "Calico" },
                    ["Beach"] = new WorldSummaryEntry { Name = "Beach", Description = "beach", Region = "Valley" }
                }
            }
        };

        string text = this.CreateRenderer().Render(summary);

        int valley = text.IndexOf("**Valley**", StringComparison.Ordinal);
        int calico = text.IndexOf("**Calico**", StringComparison.Ordinal);
        Assert.True(valley >= 0 && calico > valley);
        // Valley 组两个条目连在 Region 行之后、Calico 行之前。
        Assert.True(text.IndexOf("- **Beach**", StringComparison.Ordinal) < calico);
    }

    [Fact]
    public void Render_MissingSectionInOrder_LogsErrorAndSkips()
    {
        var summary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Festivals"] = true, ["Villagers"] = true },
            Villagers = new WorldSummarySection
            {
                Entries = { ["Abigail"] = new WorldSummaryEntry { Name = "Abigail", Description = "x" } }
            }
        };

        string text = this.CreateRenderer().Render(summary);

        Assert.Contains("Abigail", text);
        Assert.Contains(this.monitor.MessagesAt(StardewModdingAPI.LogLevel.Error), m => m.Contains("Festivals"));
    }

    [Fact]
    public void Render_NoSectionOrder_LogsErrorAndReturnsEmpty()
    {
        var summary = new WorldSummary { Villagers = new WorldSummarySection { Text = "x" } };

        string text = this.CreateRenderer().Render(summary);

        Assert.Equal(string.Empty, text);
        Assert.NotEmpty(this.monitor.MessagesAt(StardewModdingAPI.LogLevel.Error));
    }

    [Fact]
    public void Render_TranslationsLine_AppendedOnlyWhenNonEmpty()
    {
        this.prompts["gameSummaryTranslations"] = "Translated names follow the game language.";
        var summary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Intro"] = false },
            Intro = new WorldSummarySection { Text = "hi" }
        };

        string text = this.CreateRenderer().Render(summary);

        Assert.Contains("Translated names follow the game language.", text);
    }

    [Theory]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A and B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B and C")]
    public void JoinNaturally_UsesAndWord(string[] items, string expected)
    {
        Assert.Equal(expected, WorldSummaryRenderer.JoinNaturally(items, "and"));
    }

    [Fact]
    public void JoinNaturally_EmptyAndWord_FallsBackToCommas()
    {
        Assert.Equal("A, B, C", WorldSummaryRenderer.JoinNaturally(new[] { "A", "B", "C" }, ""));
    }
}
