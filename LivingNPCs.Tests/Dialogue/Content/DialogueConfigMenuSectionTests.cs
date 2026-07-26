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
            ServerAddress = "https://example.test",
            QueryTimeout = 33,
            LegacyConfigImported = true
        };

        DialogueConfigMenuSection.ResetEngineDefaults(config);

        Assert.Equal(7, config.MaxMemoryEntriesPerNpc);
        Assert.True(config.LegacyConfigImported);
        Assert.Equal(string.Empty, config.ApiKey);
        Assert.Equal("OpenAiCompatible", config.Provider);
        Assert.Equal(string.Empty, config.ServerAddress);
        Assert.Equal(85, config.QueryTimeout);
    }

    [Fact]
    public void Validate_ReenablesHiddenAlwaysOnDialogueOptions()
    {
        var config = new ModConfig
        {
            EnableMod = false,
            ShowHudMessages = false,
            AllowWakeSleepingNpc = false,
            ApplyTranslation = false,
            AllowAiFestivalInteractions = false,
            TypedResponses = "Never"
        };

        Assert.True(config.Validate());
        Assert.True(config.EnableMod);
        Assert.True(config.ShowHudMessages);
        Assert.True(config.AllowWakeSleepingNpc);
        Assert.True(config.ApplyTranslation);
        Assert.True(config.AllowAiFestivalInteractions);
        Assert.Equal("Always", config.TypedResponses);
    }

    [Fact]
    public void ClearApiKeyWhenProviderChanged_OnlyClearsAfterCommittedProviderChange()
    {
        var unchanged = new ModConfig { Provider = "Google", ApiKey = "keep-me" };
        Assert.False(DialogueConfigMenuSection.ClearApiKeyWhenProviderChanged(unchanged, "google", "keep-me"));
        Assert.Equal("keep-me", unchanged.ApiKey);

        // 只切换提供商、Key 未动：旧提供商的 Key 应被清空。
        var changed = new ModConfig { Provider = "Anthropic", ApiKey = "clear-me" };
        Assert.True(DialogueConfigMenuSection.ClearApiKeyWhenProviderChanged(changed, "Google", "clear-me"));
        Assert.Equal(string.Empty, changed.ApiKey);
    }

    [Fact]
    public void ClearApiKeyWhenProviderChanged_KeepsKeyEnteredInSameSave()
    {
        // 同一次保存里既切了提供商又填了新 Key：新 Key 必须保留，不能被"清旧 Key"逻辑吞掉。
        var config = new ModConfig { Provider = "DeepSeek", ApiKey = "new-deepseek-key" };
        Assert.True(DialogueConfigMenuSection.ClearApiKeyWhenProviderChanged(config, "OpenAiCompatible", "old-key"));
        Assert.Equal("new-deepseek-key", config.ApiKey);
    }

    [Fact]
    public void ConnectionSettingsChanged_OnlyFlagsConnectionFields()
    {
        var previous = new DialogueConfig
        {
            Provider = "OpenAI",
            ApiKey = "key",
            ModelName = "gpt-4o",
            ServerAddress = "",
            PromptFormat = "fmt"
        };

        // 只改非连接字段（礼物概率、频率等不进比较）：不触发客户端重建与付费自检。
        var unchanged = new ModConfig
        {
            Provider = "OpenAI",
            ApiKey = "key",
            ModelName = "gpt-4o",
            ServerAddress = "",
            PromptFormat = "fmt",
            GeneralFrequency = 1,
            AiDailyGiftChanceMaxPercent = 9
        };
        Assert.False(DialogueConfigMenuSection.ConnectionSettingsChanged(unchanged, previous));

        Assert.True(DialogueConfigMenuSection.ConnectionSettingsChanged(
            new ModConfig { Provider = "DeepSeek", ApiKey = "key", ModelName = "gpt-4o", ServerAddress = "", PromptFormat = "fmt" }, previous));
        Assert.True(DialogueConfigMenuSection.ConnectionSettingsChanged(
            new ModConfig { Provider = "OpenAI", ApiKey = "other", ModelName = "gpt-4o", ServerAddress = "", PromptFormat = "fmt" }, previous));
        Assert.True(DialogueConfigMenuSection.ConnectionSettingsChanged(
            new ModConfig { Provider = "OpenAI", ApiKey = "key", ModelName = "gpt-4o-mini", ServerAddress = "", PromptFormat = "fmt" }, previous));
        Assert.True(DialogueConfigMenuSection.ConnectionSettingsChanged(
            new ModConfig { Provider = "OpenAI", ApiKey = "key", ModelName = "gpt-4o", ServerAddress = "https://gw.example", PromptFormat = "fmt" }, previous));
        Assert.True(DialogueConfigMenuSection.ConnectionSettingsChanged(
            new ModConfig { Provider = "OpenAI", ApiKey = "key", ModelName = "gpt-4o", ServerAddress = "", PromptFormat = "[INST]" }, previous));
    }
}
