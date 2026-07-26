using System;
using System.Linq;
using System.Text;
using LivingNPCs.Dialogue.Diagnostics;

namespace LivingNPCs.Tests.Dialogue.Diagnostics;

/// <summary>
/// Pins the adaptive fence used by every diagnostics exporter: the fence must always be longer
/// than any tilde run inside the content, so raw LLM output containing "~~~" lines can never
/// close the fence early and corrupt the markdown log structure.
/// </summary>
public sealed class DiagnosticFenceInjectionTests
{
    private static string[] Render(string? text, string language = "text")
    {
        var builder = new StringBuilder();
        DiagnosticMarkdownLogWriter.AppendFencedBlock(builder, text, language);
        return builder.ToString()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n')
            .Split('\n');
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("no tildes here", 0)]
    [InlineData("~", 1)]
    [InlineData("a~~b~~~c", 3)]
    [InlineData("~~~~~", 5)]
    [InlineData("line1\n~~~~\nline3", 4)]
    public void LongestTildeRun_CountsTheLongestRunAnywhere(string text, int expected)
    {
        Assert.Equal(expected, DiagnosticMarkdownLogWriter.LongestTildeRun(text));
    }

    [Fact]
    public void PlainContent_UsesMinimumThreeTildeFence()
    {
        string[] lines = Render("hello world");

        Assert.Equal("~~~text", lines[0]);
        Assert.Equal("hello world", lines[1]);
        Assert.Equal("~~~", lines[2]);
    }

    [Fact]
    public void WhitespaceContent_RendersEmptyPlaceholder()
    {
        string[] lines = Render("   ");

        Assert.Equal(new[] { "~~~text", "<empty>", "~~~" }, lines);
    }

    [Fact]
    public void ContentWithFenceLine_CannotCloseTheFenceEarly()
    {
        // A model emitting its own markdown fence used to terminate the block at "~~~".
        string[] lines = Render("before\n~~~\nafter");

        Assert.Equal("~~~~text", lines[0]);
        Assert.Equal("~~~~", lines[^1]);
        Assert.Equal(new[] { "before", "~~~", "after" }, lines[1..^1]);

        // No interior line reaches the fence length, so markdown keeps the block open until
        // the real closing fence.
        string fence = new string('~', 4);
        Assert.All(lines[1..^1], line => Assert.False(line.TrimStart().StartsWith(fence, StringComparison.Ordinal)));
    }

    [Fact]
    public void FenceGrowsBeyondTheLongestInteriorRun()
    {
        string[] lines = Render("x\n~~~~~~~~\ny", "json"); // eight tildes inside

        Assert.Equal(new string('~', 9) + "json", lines[0]);
        Assert.Equal(new string('~', 9), lines[^1]);
    }

    [Fact]
    public void InlineTildesDoNotInflateBeyondTheirRun()
    {
        // Tildes embedded mid-line still count toward the run (conservative), never less.
        string[] lines = Render("strike~~~through");

        Assert.Equal("~~~~text", lines[0]);
        Assert.Equal("~~~~", lines[^1]);
    }

    [Fact]
    public void TrailingWhitespaceIsTrimmedFromContent()
    {
        string[] lines = Render("payload\n\n\n");

        Assert.Equal(new[] { "~~~text", "payload", "~~~" }, lines);
    }

    [Fact]
    public void FenceLineCarriesTheLanguageTag()
    {
        string[] lines = Render("{\"a\":1}", "json");

        Assert.Equal("~~~json", lines[0]);
        Assert.Equal("~~~", lines[^1]);
    }
}
