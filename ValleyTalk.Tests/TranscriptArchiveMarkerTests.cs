using System;
using Xunit;
using ValleyTalk;

namespace ValleyTalk.Tests;

public sealed class TranscriptArchiveMarkerTests
{
    [Fact]
    public void MarkerRoundTrips()
    {
        string marker = ConversationTranscriptExporter.BuildArchiveMarker(12, 254, 1830);

        Assert.True(ConversationTranscriptExporter.TryParseArchiveMarker(marker, out int count, out int lastDay, out int lastTime));
        Assert.Equal(12, count);
        Assert.Equal(254, lastDay);
        Assert.Equal(1830, lastTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("## Conversation 3: Year 2, Summer 14 at 1830")]
    [InlineData("<!-- some other comment -->")]
    [InlineData("<!-- valleytalk:archive-end -->")]
    public void ParseRejectsNonMarkerLines(string line)
    {
        Assert.False(ConversationTranscriptExporter.TryParseArchiveMarker(line, out _, out _, out _));
    }

    [Fact]
    public void ExtractArchiveBlockSplitsArchiveAndLiveContent()
    {
        string[] lines =
        {
            "# Abigail",
            "",
            "- Save: TestFarm",
            "",
            ConversationTranscriptExporter.BuildArchiveMarker(2, 200, 1200),
            "## Conversation 1: Year 1, Spring 5 at 0900",
            "- **Player:** hi",
            "",
            "## Conversation 2: Year 1, Spring 8 at 1200",
            "- **Player:** hello again",
            "",
            "<!-- valleytalk:archive-end -->",
            "",
            "## Conversation 3: Year 2, Summer 14 at 1830",
            "- **Player:** newest"
        };

        var block = ConversationTranscriptExporter.ExtractArchiveBlock(lines);

        Assert.True(block.HasMarkers);
        Assert.Equal(2, block.Count);
        Assert.Equal(200, block.LastDay);
        Assert.Equal(1200, block.LastTime);
        Assert.Contains("Conversation 1", block.Content);
        Assert.Contains("Conversation 2", block.Content);
        Assert.DoesNotContain("Conversation 3", block.Content);
        Assert.Contains("Conversation 3", block.LiveContent);
        Assert.DoesNotContain("Conversation 1", block.LiveContent);
    }

    [Fact]
    public void ExtractArchiveBlockHandlesLegacyFilesWithoutMarkers()
    {
        string[] lines =
        {
            "# Abigail",
            "",
            "## Conversation 1: Year 1, Spring 5 at 0900",
            "- **Player:** hi"
        };

        var block = ConversationTranscriptExporter.ExtractArchiveBlock(lines);

        Assert.False(block.HasMarkers);
        Assert.Equal(0, block.Count);
        Assert.Equal(string.Empty, block.Content);
        Assert.Equal(string.Empty, block.LiveContent);
    }

    [Fact]
    public void ExtractArchiveBlockTreatsMissingEndMarkerAsLegacy()
    {
        string[] lines =
        {
            "# Abigail",
            ConversationTranscriptExporter.BuildArchiveMarker(2, 200, 1200),
            "## Conversation 1: Year 1, Spring 5 at 0900"
        };

        var block = ConversationTranscriptExporter.ExtractArchiveBlock(lines);

        Assert.False(block.HasMarkers);
    }

    [Fact]
    public void WatermarkComparisonUsesDayThenTime()
    {
        // Year 2, Summer 14 => 2*112 + 1*28 + 14 = 266 absolute days.
        var watermark = new StardewTime(2, StardewValley.Season.Summer, 14, 1830);
        int day = watermark.ToAbsoluteDays();
        Assert.Equal(266, day);

        // Equal day and time: not after (already archived).
        Assert.False(ConversationTranscriptExporter.IsAfterWatermark(watermark, day, 1830));
        // Same day, later time: after.
        Assert.True(ConversationTranscriptExporter.IsAfterWatermark(
            new StardewTime(2, StardewValley.Season.Summer, 14, 1840), day, 1830));
        // Next day, earlier time: after.
        Assert.True(ConversationTranscriptExporter.IsAfterWatermark(
            new StardewTime(2, StardewValley.Season.Summer, 15, 600), day, 1830));
        // Previous day, later time: not after.
        Assert.False(ConversationTranscriptExporter.IsAfterWatermark(
            new StardewTime(2, StardewValley.Season.Summer, 13, 2600), day, 1830));
    }
}
