using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GenericModConfigMenu;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Tests.Dialogue.Llm;
using Newtonsoft.Json;
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

    [Theory]
    [InlineData("OpenAiCompatible", "deepseek-flash", "medium", "Medium")]
    [InlineData("OpenAiCompatible", "deppseek-flash", "Low", "Low")]
    [InlineData("OpenAiCompatible", "custom-model", "Max", "Max")]
    [InlineData("OpenAiCompatible", "", "High", "High")]
    [InlineData("DeepSeek", "deepseek-v4-pro", "xhigh", "XHigh")]
    [InlineData("OpenAI", "gpt-6-astra", "Off", "Off")]
    [InlineData("OpenAI", "gpt-5.6-sol", " Ultra ", "Ultra")]
    [InlineData("OpenAI", "gpt-4o", "Minimal", "Minimal")]
    [InlineData("Google", "gemini-3.8-flash", "Minimal", "Minimal")]
    [InlineData("Google", "gemini-2.5-flash", "Max", "Max")]
    [InlineData("Anthropic", "claude-opus-4-6", "XHigh", "XHigh")]
    [InlineData("Anthropic", "claude-fable-5-1", "Off", "Off")]
    public void ThinkingMenus_OpenSaveAndReload_PreservePreferences(string provider, string model, string saved, string expected)
    {
        var config = new ModConfig
        {
            EnableDialogueEngine = false,
            Provider = provider,
            ModelName = model,
            RoutingThinkingLevel = saved,
            ChatThinkingLevel = saved
        };
        DialogueServices.Config.SyncFrom(config);
        MenuApiProxy menu = OpenMenu(config);

        AssertThinkingOptions(menu, expected, expected);
        ModConfig reloaded = SaveAndReloadConfig(menu, config);

        Assert.Equal(expected, reloaded.RoutingThinkingLevel);
        Assert.Equal(expected, reloaded.ChatThinkingLevel);
        AssertThinkingOptions(OpenMenu(reloaded), expected, expected);
    }

    [Theory]
    [InlineData("Low", "Max")]
    [InlineData("Medium", "Ultra")]
    [InlineData("Off", "High")]
    public void ModelNameChange_SaveAndReopen_PreservesBothThinkingPreferences(string routing, string chat)
    {
        var config = new ModConfig
        {
            EnableDialogueEngine = false,
            Provider = "OpenAiCompatible",
            ModelName = "deepseek-flash",
            RoutingThinkingLevel = routing,
            ChatThinkingLevel = chat
        };
        DialogueServices.Config.SyncFrom(config);
        MenuApiProxy menu = OpenMenu(config);

        foreach (string nextModel in new[] { "deppseek-flash", "gateway-custom-model", "", "deepseek-flash" })
        {
            // GMCM edits its cache, then commits the model before the two thinking fields.
            menu.Option("modelName").CachedValue = nextModel;
            ModConfig reloaded = SaveAndReloadConfig(menu, config);

            Assert.Equal(nextModel, reloaded.ModelName);
            Assert.Equal(nextModel, DialogueServices.Config.ModelName);
            Assert.Equal(routing, DialogueServices.Config.RoutingThinkingLevel);
            Assert.Equal(chat, DialogueServices.Config.ChatThinkingLevel);

            // Closing/reopening reuses the registered options; a fresh registration also works.
            menu.ReloadValues();
            AssertThinkingOptions(menu, routing, chat);
            config = reloaded;
            menu = OpenMenu(config);
            AssertThinkingOptions(menu, routing, chat);
        }
    }

    [Theory]
    [InlineData("gpt-6-astra", "deppseek-flash", "Medium", "Ultra")]
    [InlineData("gpt-6-astra", "gpt-4o", "Low", "Max")]
    [InlineData("custom-model", "deepseek-flash", "Medium", "Ultra")]
    [InlineData("deppseek-flash", "deppseek-flash", "Low", "Max")]
    public void ModelAndThinkingEdits_InSameSave_PreserveNewSelections(string previousModel, string nextModel, string routing, string chat)
    {
        var config = new ModConfig
        {
            EnableDialogueEngine = false,
            Provider = "OpenAiCompatible",
            ModelName = previousModel,
            RoutingThinkingLevel = "Auto",
            ChatThinkingLevel = "Auto"
        };
        DialogueServices.Config.SyncFrom(config);
        MenuApiProxy menu = OpenMenu(config);

        menu.Option("modelName").CachedValue = nextModel;
        menu.Option("routingThinking").CachedValue = routing;
        menu.Option("chatThinking").CachedValue = chat;
        ModConfig reloaded = SaveAndReloadConfig(menu, config);

        Assert.Equal(nextModel, reloaded.ModelName);
        AssertThinkingOptions(OpenMenu(reloaded), routing, chat);
    }

    [Fact]
    public void ProviderChange_ReregisteringMenu_KeepsBothThinkingPreferences()
    {
        var config = new ModConfig
        {
            Provider = "OpenAiCompatible",
            ModelName = "deepseek-flash",
            RoutingThinkingLevel = "Medium",
            ChatThinkingLevel = "Ultra"
        };
        MenuApiProxy menu = OpenMenu(config);

        menu.Option("provider").CachedValue = "Google";
        menu.Option("modelName").CachedValue = "gemini-3.8-flash";
        menu.CommitValues();
        config.Validate();

        Assert.Equal("Google", config.Provider);
        Assert.Equal("gemini-3.8-flash", config.ModelName);
        AssertThinkingOptions(OpenMenu(config), "Medium", "Ultra");
    }

    [Fact]
    public void ThinkingMenus_ResetRefreshesCachedValuesBeforeSavingDefaults()
    {
        var config = new ModConfig
        {
            Provider = "Google",
            ModelName = "gemini-3.8-flash",
            RoutingThinkingLevel = "Ultra",
            ChatThinkingLevel = "Low"
        };
        MenuApiProxy menu = OpenMenu(config);

        DialogueConfigMenuSection.ResetEngineDefaults(config);
        // GMCM's AfterReset reloads getters before its immediate SaveConfig.
        menu.ReloadValues();
        menu.CommitValues();
        config.Validate();

        Assert.Equal("Off", config.RoutingThinkingLevel);
        Assert.Equal("Auto", config.ChatThinkingLevel);
        AssertThinkingOptions(menu, "Off", "Auto");
        AssertThinkingOptions(OpenMenu(config), "Off", "Auto");
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

    private static MenuApiProxy OpenMenu(ModConfig config)
    {
        var api = DispatchProxy.Create<IGenericModConfigMenuApi, MenuApiProxy>();
        DialogueConfigMenuSection.Append(api, new ModEntry(), config);
        var menu = (MenuApiProxy)api;
        menu.ReloadValues();
        return menu;
    }

    private static ModConfig SaveAndReloadConfig(MenuApiProxy menu, ModConfig config)
    {
        menu.CommitValues();
        config.Validate();
        // Engine disabled: exercise the actual save/sync path without starting model requests.
        // A model-only save must not need a GMCM re-registration via ModEntry.Helper.
        DialogueConfigMenuSection.OnSave(new ModEntry(), config);
        return JsonConvert.DeserializeObject<ModConfig>(JsonConvert.SerializeObject(config))!;
    }

    private static void AssertThinkingOptions(MenuApiProxy menu, string routing, string chat)
    {
        foreach ((string name, string expected) in new[] { ("routingThinking", routing), ("chatThinking", chat) })
        {
            TextOption option = menu.Option(name);
            Assert.Equal(new[] { "Auto", "Off", "Minimal", "Low", "Medium", "High", "XHigh", "Max", "Ultra" }, option.AllowedValues);
            Assert.Equal(expected, option.GetValue());
            Assert.Equal(expected, option.CachedValue);
        }
    }

    internal sealed record TextOption(string Name, Func<string> GetValue, Action<string> SetValue, string[]? AllowedValues)
    {
        public string CachedValue { get; set; } = string.Empty;
    }

    public class MenuApiProxy : DispatchProxy
    {
        private readonly List<TextOption> textOptions = new();

        internal TextOption Option(string name) => this.textOptions.Single(option => option.Name == $"dialogue.config.{name}.name");

        internal void ReloadValues()
        {
            foreach (TextOption option in this.textOptions)
            {
                option.CachedValue = option.GetValue();
            }
        }

        internal void CommitValues()
        {
            foreach (TextOption option in this.textOptions)
            {
                option.SetValue(option.CachedValue);
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IGenericModConfigMenuApi.AddTextOption))
            {
                this.textOptions.Add(new TextOption(
                    ((Func<string>)args![3]!)(),
                    (Func<string>)args[1]!,
                    (Action<string>)args[2]!,
                    ((string[]?)args[5])?.ToArray()));
            }

            return null;
        }
    }
}
