using System;
using System.Linq;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Tests.Dialogue.Llm;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Content;

[Collection("LlmLayer")]
public sealed class DialogueConfigMenuSectionTests : IDisposable
{
    private readonly FakeMonitor monitor = new();

    public DialogueConfigMenuSectionTests()
    {
        DialogueServices.Initialize(null!, this.monitor, new DialogueConfig());
    }

    public void Dispose()
    {
        Util.PromptFallback = null;
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData(" 3 ", 3)]
    [InlineData("9", 4)]
    [InlineData("-2", 0)]
    public void ParseFrequency_IntegerClampedToRange(string raw, int expected)
    {
        Assert.Equal(expected, DialogueConfigMenuSection.ParseFrequency(raw, fallback: 2));
    }

    [Fact]
    public void ParseFrequency_MatchesLocalizedDisplayName()
    {
        string display = DialogueConfigMenuSection.FormatFrequency(3);
        Assert.Equal(3, DialogueConfigMenuSection.ParseFrequency(display, fallback: 0));
    }

    [Theory]
    [InlineData("Whatever (0%)", 0)]
    [InlineData("总是 (100%)", 4)]
    [InlineData("Sometimes (50%) extra", 2)]
    public void ParseFrequency_FallsBackToInvariantPercentSubstring(string raw, int expected)
    {
        Assert.Equal(expected, DialogueConfigMenuSection.ParseFrequency(raw, fallback: 1));
    }

    [Fact]
    public void ParseFrequency_Unparseable_WarnsAndKeepsFallback()
    {
        Assert.Equal(2, DialogueConfigMenuSection.ParseFrequency("gibberish", fallback: 2));
        Assert.Contains(this.monitor.MessagesAt(LogLevel.Warn), m => m.Contains("gibberish"));
    }

    [Fact]
    public void FormatFrequency_ShowsPercentages()
    {
        Assert.EndsWith("(0%)", DialogueConfigMenuSection.FormatFrequency(0));
        Assert.EndsWith("(75%)", DialogueConfigMenuSection.FormatFrequency(3));
        Assert.EndsWith("(100%)", DialogueConfigMenuSection.FormatFrequency(9));
    }

    [Fact]
    public void ThinkingOptions_GoogleExcludesXHigh()
    {
        var config = new ModConfig { Provider = "Google" };
        string[] options = DialogueConfigMenuSection.ThinkingOptionsFor(config);

        Assert.DoesNotContain(LlmThinking.XHigh, options);
        Assert.Contains(LlmThinking.Auto, options);
    }

    [Fact]
    public void ThinkingOptions_OpenAiCompatibleWithGeminiModel_ExcludesXHigh()
    {
        var config = new ModConfig { Provider = "OpenAiCompatible", ModelName = "google/gemini-2.5-flash" };
        Assert.DoesNotContain(LlmThinking.XHigh, DialogueConfigMenuSection.ThinkingOptionsFor(config));

        config.ModelName = "openai/gpt-5-mini";
        Assert.Contains(LlmThinking.XHigh, DialogueConfigMenuSection.ThinkingOptionsFor(config));
    }

    [Fact]
    public void ThinkingOptions_OtherProviders_KeepFullSet()
    {
        var config = new ModConfig { Provider = "OpenAI" };
        Assert.Equal(LlmThinking.Options.Length, DialogueConfigMenuSection.ThinkingOptionsFor(config).Length);
    }

    [Fact]
    public void ResetEngineDefaults_LeavesBehaviorFieldsAndImportFlagAlone()
    {
        var config = new ModConfig
        {
            MaxMemoryEntriesPerNpc = 7,
            ApiKey = "sk-x",
            Provider = "Google",
            QueryTimeout = 33,
            LegacyConfigImported = true
        };

        DialogueConfigMenuSection.ResetEngineDefaults(config);

        Assert.Equal(7, config.MaxMemoryEntriesPerNpc);
        Assert.True(config.LegacyConfigImported);
        Assert.Equal(string.Empty, config.ApiKey);
        Assert.Equal("OpenAiCompatible", config.Provider);
        Assert.Equal(85, config.QueryTimeout);
    }
}
