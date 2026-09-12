using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Tests.Dialogue.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests;

[Collection("LlmLayer")]
public sealed class ThinkingConfigMigrationTests : LlmTestBase
{
    [Fact]
    public void NewDefaultsUseOneAutomaticPreferenceWithoutMigration()
    {
        var config = new ModConfig();

        Assert.Equal(LlmThinking.Auto, config.ThinkingLevel);
        Assert.False(config.Migrate());
        Assert.False(config.Validate());
        SerializeUnified(config, LlmThinking.Auto);
    }

    [Theory]
    [InlineData("{\"ChatThinkingLevel\":\"High\"}", "High")]
    [InlineData("{\"RoutingThinkingLevel\":\"Low\"}", "Low")]
    [InlineData("{\"ChatThinkingLevel\":\"High\",\"RoutingThinkingLevel\":\"Off\"}", "High")]
    [InlineData("{\"ChatThinkingLevel\":\"Auto\",\"RoutingThinkingLevel\":\"High\"}", "Auto")]
    [InlineData("{\"ChatThinkingLevel\":\"Off\",\"RoutingThinkingLevel\":\"Low\"}", "Off")]
    [InlineData("{\"ChatThinkingLevel\":\" mEdIuM \",\"RoutingThinkingLevel\":\"Ultra\"}", "Medium")]
    [InlineData("{\"ChatThinkingLevel\":\"invalid\",\"RoutingThinkingLevel\":\"Max\"}", "Max")]
    [InlineData("{\"ChatThinkingLevel\":\"\",\"RoutingThinkingLevel\":\"Low\"}", "Low")]
    [InlineData("{\"ChatThinkingLevel\":\"  \",\"RoutingThinkingLevel\":\"High\"}", "High")]
    [InlineData("{\"ChatThinkingLevel\":null,\"RoutingThinkingLevel\":\"Medium\"}", "Medium")]
    [InlineData("{\"ChatThinkingLevel\":\"Minimal\",\"RoutingThinkingLevel\":\"High\"}", "Low")]
    [InlineData("{\"RoutingThinkingLevel\":\" minimal \"}", "Low")]
    [InlineData("{\"ChatThinkingLevel\":\"invalid\",\"RoutingThinkingLevel\":\"Minimal\"}", "Low")]
    [InlineData("{\"ChatThinkingLevel\":\"invalid\"}", "Auto")]
    [InlineData("{\"RoutingThinkingLevel\":\"invalid\"}", "Auto")]
    [InlineData("{\"ChatThinkingLevel\":\"invalid\",\"RoutingThinkingLevel\":\"also invalid\"}", "Auto")]
    [InlineData("{\"ChatThinkingLevel\":null}", "Auto")]
    [InlineData("{\"RoutingThinkingLevel\":null}", "Auto")]
    [InlineData("{\"ChatThinkingLevel\":\"\",\"RoutingThinkingLevel\":\" \"}", "Auto")]
    public void LegacyPreferenceMigratesDuringDeserializationRegardlessOfKeyOrder(string json, string expected)
    {
        var original = JObject.Parse(json);
        var reversed = new JObject(original.Properties().Reverse().Select(property => property.DeepClone()));

        foreach (JObject ordered in new[] { original, reversed })
        {
            ModConfig config = Deserialize(ordered.ToString(Formatting.None));

            // Callers can consume or serialize the migrated preference before Migrate acknowledges a rewrite.
            Assert.Equal(expected, config.ThinkingLevel);
            SerializeUnified(config, expected);
            Assert.True(config.Migrate());
            Assert.False(config.Migrate());
            Assert.False(config.Validate());

            ModConfig reloaded = Deserialize(SerializeUnified(config, expected));
            Assert.Equal(expected, reloaded.ThinkingLevel);
            Assert.False(reloaded.Migrate());
            Assert.False(reloaded.Validate());
        }
    }

    [Theory]
    [InlineData("Auto", "Auto")]
    [InlineData("High", "High")]
    [InlineData("Off", "Off")]
    [InlineData("Minimal", "Low")]
    [InlineData(" medium ", "Medium")]
    [InlineData("invalid", "Auto")]
    [InlineData("", "Auto")]
    [InlineData(null, "Auto")]
    public void ExplicitUnifiedPreferenceWinsOverLegacyFieldsInEveryKeyOrder(string? value, string expected)
    {
        var fields = new JObject
        {
            ["ThinkingLevel"] = value == null ? JValue.CreateNull() : new JValue(value),
            ["ChatThinkingLevel"] = "Ultra",
            ["RoutingThinkingLevel"] = "Off"
        };
        JProperty[] properties = fields.Properties().ToArray();
        int[][] orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];

        foreach (int[] order in orders)
        {
            var ordered = new JObject(order.Select(index => properties[index].DeepClone()));
            ModConfig config = Deserialize(ordered.ToString(Formatting.None));
            config.Validate();

            Assert.Equal(expected, config.ThinkingLevel);
            Assert.True(config.Migrate());
            Assert.Equal(expected, config.ThinkingLevel);
            Assert.False(config.Migrate());
            Assert.False(config.Validate());

            ModConfig reloaded = Deserialize(SerializeUnified(config, expected));
            Assert.Equal(expected, reloaded.ThinkingLevel);
            Assert.False(reloaded.Migrate());
        }
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Off")]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("XHigh")]
    [InlineData("Max")]
    [InlineData("Ultra")]
    public void UnifiedConfigurationRoundTripsWithoutReportingLegacyMigration(string preference)
    {
        var config = new ModConfig { ThinkingLevel = preference };

        ModConfig reloaded = Deserialize(SerializeUnified(config, preference));

        Assert.Equal(preference, reloaded.ThinkingLevel);
        Assert.False(reloaded.Migrate());
        Assert.False(reloaded.Validate());
        Assert.Equal(preference, Deserialize(SerializeUnified(reloaded, preference)).ThinkingLevel);
    }

    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("  ", "Auto")]
    [InlineData("invalid", "Auto")]
    [InlineData(" aUtO ", "Auto")]
    [InlineData("OFF", "Off")]
    [InlineData("Minimal", "Low")]
    [InlineData(" minimal ", "Low")]
    [InlineData("low", "Low")]
    [InlineData(" Medium ", "Medium")]
    [InlineData("high", "High")]
    [InlineData("xhigh", "XHigh")]
    [InlineData("max", "Max")]
    [InlineData("ultra", "Ultra")]
    public void ValidateNormalizesTheUnifiedPreferenceAndIsIdempotent(string? raw, string expected)
    {
        var config = new ModConfig { ThinkingLevel = raw! };

        Assert.True(config.Validate());
        Assert.Equal(expected, config.ThinkingLevel);
        Assert.Equal(expected, LlmThinking.NormalizePreference(raw!));
        Assert.False(config.Validate());
        SerializeUnified(config, expected);
    }

    [Fact]
    public void ThinkingMigrationKeepsUnrelatedConfigurationValues()
    {
        ModConfig config = Deserialize("""
            {
              "ChatThinkingLevel": "Max",
              "RoutingThinkingLevel": "Off",
              "Provider": "OpenAiCompatible",
              "ModelName": "unit-test-model",
              "ApiKey": "fake-unit-test-key",
              "ServerAddress": "https://llm.example.test/v1",
              "QueryTimeout": 37,
              "MaxMemoryEntriesPerNpc": 71,
              "AllowAiMoneyGifts": false,
              "DisableCharacters": "penny leah"
            }
            """);

        Assert.True(config.Migrate());
        Assert.False(config.Validate());
        ModConfig reloaded = Deserialize(SerializeUnified(config, LlmThinking.Max));

        Assert.Equal("OpenAiCompatible", reloaded.Provider);
        Assert.Equal("unit-test-model", reloaded.ModelName);
        Assert.Equal("fake-unit-test-key", reloaded.ApiKey);
        Assert.Equal("https://llm.example.test/v1", reloaded.ServerAddress);
        Assert.Equal(37, reloaded.QueryTimeout);
        Assert.Equal(71, reloaded.MaxMemoryEntriesPerNpc);
        Assert.False(reloaded.AllowAiMoneyGifts);
        Assert.Equal("penny leah", reloaded.DisableCharacters);
        Assert.Equal(new[] { "Penny", "Leah" }, reloaded.DisabledCharacterList);
        Assert.False(reloaded.Migrate());
    }

    [Fact]
    public void ThinkingMigrationDoesNotSkipExistingConfigurationMigrations()
    {
        ModConfig config = Deserialize("{\"ChatThinkingLevel\":\"High\",\"EnableValleyTalkPromptBridge\":false}");

        Assert.Equal(LlmThinking.High, config.ThinkingLevel);
        Assert.True(config.Migrate());
        Assert.False(config.EnableBehaviorContextInDialogue);
        Assert.False(config.Migrate());

        JObject serialized = JObject.Parse(SerializeUnified(config, LlmThinking.High));
        Assert.Null(serialized["EnableValleyTalkPromptBridge"]);
        Assert.False(serialized.Value<bool>("EnableBehaviorContextInDialogue"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogueResetUsesAutoWithoutRevivingLegacyPreferences(bool acknowledgeMigrationBeforeReset)
    {
        ModConfig config = Deserialize("{\"ChatThinkingLevel\":\"High\",\"RoutingThinkingLevel\":\"Low\"}");
        config.MaxMemoryEntriesPerNpc = 71;
        config.EnableAiPlanner = true;
        if (acknowledgeMigrationBeforeReset)
        {
            Assert.True(config.Migrate());
        }

        config.ResetDialogueEngineDefaults();
        config.Migrate();

        Assert.Equal(LlmThinking.Auto, config.ThinkingLevel);
        Assert.Equal(71, config.MaxMemoryEntriesPerNpc);
        Assert.True(config.EnableAiPlanner);
        Assert.False(config.Migrate());
        Assert.False(config.Validate());
        ModConfig reloaded = Deserialize(SerializeUnified(config, LlmThinking.Auto));
        Assert.Equal(LlmThinking.Auto, reloaded.ThinkingLevel);
        Assert.False(reloaded.Migrate());
    }

    [Theory]
    [InlineData("{\"ChatThinkingLevel\":\"Auto\",\"RoutingThinkingLevel\":\"High\"}", "Auto")]
    [InlineData("{\"ChatThinkingLevel\":\"Minimal\"}", "Low")]
    [InlineData("{\"ChatThinkingLevel\":\"invalid\",\"RoutingThinkingLevel\":\"Max\"}", "Max")]
    [InlineData("{\"ThinkingLevel\":\"Off\",\"ChatThinkingLevel\":\"High\"}", "Off")]
    public void RuntimeSyncAndEveryCallUseTheSameUnifiedPreference(string json, string expected)
    {
        ModConfig config = Deserialize(json);
        config.Validate();

        Config.SyncFrom(config);

        Assert.Equal(expected, Config.ThinkingLevel);
        Assert.Equal(expected, LlmThinking.ForCall());
        foreach (string next in new[] { LlmThinking.Ultra, LlmThinking.Off, LlmThinking.Auto })
        {
            config.ThinkingLevel = next;
            Config.SyncFrom(config);
            Assert.Equal(next, Config.ThinkingLevel);
            Assert.Equal(next, LlmThinking.ForCall());
        }
    }

    [Theory]
    [InlineData("Minimal", "Low")]
    [InlineData(" minimal ", "Low")]
    [InlineData("high", "High")]
    [InlineData("invalid", "Auto")]
    public void ForCallNormalizesTheSharedPreferenceEvenBeforeValidation(string raw, string expected)
    {
        Config.ThinkingLevel = raw;

        Assert.Equal(expected, LlmThinking.ForCall());
    }

    private static ModConfig Deserialize(string json)
    {
        ModConfig? config = JsonConvert.DeserializeObject<ModConfig>(json);
        Assert.NotNull(config);
        return config!;
    }

    private static string SerializeUnified(ModConfig config, string expected)
    {
        string json = JsonConvert.SerializeObject(config);
        JObject serialized = JObject.Parse(json);
        Assert.Equal(expected, serialized.Value<string>("ThinkingLevel"));
        Assert.DoesNotContain(serialized.Properties(), property =>
            property.Name.Equals("RoutingThinkingLevel", StringComparison.OrdinalIgnoreCase)
            || property.Name.Equals("ChatThinkingLevel", StringComparison.OrdinalIgnoreCase));
        return json;
    }
}
