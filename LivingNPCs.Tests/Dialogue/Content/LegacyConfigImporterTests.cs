using System;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Persistence;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Content;

public sealed class LegacyConfigImporterTests
{
    private static LegacyValleyTalkConfig Parse(string json)
    {
        LegacyValleyTalkConfig? config = LegacyValleyTalkConfig.TryParse(json);
        Assert.NotNull(config);
        return config!;
    }

    [Fact]
    public void Apply_NullLegacy_StillSetsFlagOnce()
    {
        var config = new ModConfig();

        Assert.True(LegacyConfigImporter.Apply(null, config));
        Assert.True(config.LegacyConfigImported);
        // 幂等：第二次不再有任何动作。
        Assert.False(LegacyConfigImporter.Apply(null, config));
    }

    [Fact]
    public void Apply_AlreadyImported_IgnoresLegacyValues()
    {
        var config = new ModConfig { LegacyConfigImported = true };
        var legacy = Parse("{\"ApiKey\":\"sk-old\"}");

        Assert.False(LegacyConfigImporter.Apply(legacy, config));
        Assert.Equal(string.Empty, config.ApiKey);
    }

    [Fact]
    public void Apply_RenamesEnableModToEnableDialogueEngine()
    {
        var config = new ModConfig { EnableMod = true };
        var legacy = Parse("{\"EnableMod\":false}");

        LegacyConfigImporter.Apply(legacy, config);

        Assert.False(config.EnableDialogueEngine);
        // LivingNPCs 自己的总开关不受影响。
        Assert.True(config.EnableMod);
    }

    [Fact]
    public void Apply_DebugMergesWithLogicalOr()
    {
        var offConfig = new ModConfig { Debug = false };
        LegacyConfigImporter.Apply(Parse("{\"Debug\":true}"), offConfig);
        Assert.True(offConfig.Debug);

        var onConfig = new ModConfig { Debug = true };
        LegacyConfigImporter.Apply(Parse("{\"Debug\":false}"), onConfig);
        Assert.True(onConfig.Debug);
    }

    [Fact]
    public void Apply_SveMergesWithLogicalAnd()
    {
        var config = new ModConfig { EnableSveCompatibility = true };
        LegacyConfigImporter.Apply(Parse("{\"EnableSveCompatibility\":false}"), config);
        Assert.False(config.EnableSveCompatibility);

        var config2 = new ModConfig { EnableSveCompatibility = false };
        LegacyConfigImporter.Apply(Parse("{\"EnableSveCompatibility\":true}"), config2);
        Assert.False(config2.EnableSveCompatibility);
    }

    [Fact]
    public void Apply_CopiesConnectionFields_ApiKeyVerbatim()
    {
        var config = new ModConfig();
        var legacy = Parse("{\"Provider\":\"anthropic\",\"ApiKey\":\"sk-secret\",\"ModelName\":\"claude\",\"ServerAddress\":\"https://example.test\",\"PromptFormat\":\"fmt\"}");

        LegacyConfigImporter.Apply(legacy, config);

        Assert.Equal("anthropic", config.Provider);
        Assert.Equal("sk-secret", config.ApiKey);
        Assert.Equal("claude", config.ModelName);
        Assert.Equal("https://example.test", config.ServerAddress);
        Assert.Equal("fmt", config.PromptFormat);
    }

    [Fact]
    public void Apply_UnknownProvider_KeepsNewDefault()
    {
        var config = new ModConfig();
        LegacyConfigImporter.Apply(Parse("{\"Provider\":\"NotAProvider\"}"), config);
        Assert.Equal("OpenAiCompatible", config.Provider);
    }

    [Fact]
    public void Apply_ClampsNumericFields()
    {
        var config = new ModConfig();
        var legacy = Parse("{\"QueryTimeout\":9999,\"SemanticContextRoutingTimeoutSeconds\":0,\"GeneralFrequency\":9,\"GiftFrequency\":-1,\"MarriageFrequency\":2}");

        LegacyConfigImporter.Apply(legacy, config);

        Assert.Equal(180, config.QueryTimeout);
        Assert.Equal(2, config.SemanticContextRoutingTimeoutSeconds);
        Assert.Equal(4, config.GeneralFrequency);
        Assert.Equal(0, config.GiftFrequency);
        Assert.Equal(2, config.MarriageFrequency);
    }

    [Fact]
    public void Apply_NormalizesEnumsAndParsesKeybind()
    {
        var config = new ModConfig();
        var legacy = Parse("{\"TypedResponses\":\"with generated\",\"RoutingThinkingLevel\":\"HIGH\",\"ChatThinkingLevel\":\"bogus\",\"InitiateTypedDialogueKey\":\"LeftControl\"}");

        LegacyConfigImporter.Apply(legacy, config);

        Assert.Equal("With Generated", config.TypedResponses);
        Assert.Equal("High", config.RoutingThinkingLevel);
        Assert.Equal("Auto", config.ChatThinkingLevel);
        Assert.Equal(SButton.LeftControl, config.InitiateTypedDialogueKey);
    }

    [Fact]
    public void Apply_DisableCharacters_ParsesTitleCaseList()
    {
        var config = new ModConfig();
        LegacyConfigImporter.Apply(Parse("{\"DisableCharacters\":\"abigail, SAM  sebastian\"}"), config);

        Assert.Equal(new[] { "Abigail", "Sam", "Sebastian" }, config.DisabledCharacterList);
    }

    [Fact]
    public void NormalizeTypedResponses_RejectsUnknownValue()
    {
        Assert.Null(LegacyConfigImporter.NormalizeTypedResponses("sometimes"));
        Assert.Equal("Never", LegacyConfigImporter.NormalizeTypedResponses(" never "));
        Assert.Null(LegacyConfigImporter.NormalizeTypedResponses(null));
    }
}
