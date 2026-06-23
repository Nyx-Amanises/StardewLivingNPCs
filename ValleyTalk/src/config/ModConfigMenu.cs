using System;
using System.Collections.Generic;
using System.Linq;
using GenericModConfigMenu;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace ValleyTalk
{
    internal static class ModConfigMenu
    {
        private static IGenericModConfigMenuApi ConfigMenu;
        private static IManifest ModManifest;
        private static ModEntry _modEntry;
        private static bool _isRefreshingMenu;
        private static bool _refreshQueued;

        private static readonly string[] TypedResponseOptions = { "Always", "With Generated", "Never" };
        internal static void Register(ModEntry modEntry)
        {
            _modEntry = modEntry;
            var Config = ModEntry.Config;

            ModManifest = modEntry.ModManifest;

            ConfigMenu = GetConfigMenu(modEntry);
            if (ConfigMenu == null)
            {
                modEntry.Monitor.Log(Util.GetString("configGmcmNotInstalled", returnNull: true) ?? "Generic Mod Config Menu not installed.", LogLevel.Warn);
                return;
            }

            // register mod
            ConfigMenu.Register(
                mod: ModManifest,
                reset: () =>
                {
                    ModEntry.Config = new ModConfig();
                },
                save: () =>
                {
                    SetLlm();
                    modEntry.Helper.WriteConfig(ModEntry.Config);
                }
            );

            // add some config options
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configEnable", returnNull: true) ?? "Enable Mod",
                tooltip: () => Util.GetString("configEnableTooltip", returnNull: true) ?? "Enable or disable the mod.",
                getValue: () => Config.EnableMod,
                setValue: value => Config.EnableMod = value
            );
#if DEBUG
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configLogging", returnNull: true) ?? "Enable Logging",
                tooltip: () => Util.GetString("configLoggingTooltip", returnNull: true) ?? "Enable or disable logging of prompts and responses.",
                getValue: () => Config.Debug,
                setValue: value => Config.Debug = value
            );
#endif
            // Create a string array of the options in the LlmType enum
            var llmTypes = ModEntry.LlmMap.Keys.ToArray();
            if (!ModEntry.LlmMap.ContainsKey(Config.Provider))
            {
                Config.Provider = llmTypes.FirstOrDefault() ?? "Mistral";
            }
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configProvider", returnNull: true) ?? "AI Model Provider",
                getValue: () => Config.Provider,
                setValue: value => 
                {
                    if (value == Config.Provider) return;
                    Config.ApiKey = "";
                    Config.Provider = value; 
                    PersistConfig();
                    RefreshConfigMenu();
                },
                allowedValues: llmTypes,
                formatAllowedValue: FormatProviderOption,
                fieldId: "Provider"
            );
            var llmType = ModEntry.LlmMap[Config.Provider];
            var constructorParameters = llmType.GetConstructors().First().GetParameters().Select(x => x.Name).ToArray();
            if (constructorParameters.Contains("apiKey", StringComparer.OrdinalIgnoreCase))
            {
                ConfigMenu.AddTextOption(
                    mod: ModManifest,
                    name: () => Util.GetString("configApiKey", returnNull: true) ?? "API Key",
                    tooltip: () => Util.GetString("configApiKeyTooltip", returnNull: true) ?? "API Key for the AI model provider.",
                    getValue: () => Config.ApiKey,
                    setValue: (value) =>
                    {
                        Config.ApiKey = value;
                    },
                    fieldId: "ApiKey"
                );
            }

            if (constructorParameters.Contains("modelName", StringComparer.OrdinalIgnoreCase))
            {
                ConfigMenu.AddTextOption(
                    mod: ModManifest,
                    name: () => Util.GetString("configModelName", returnNull: true) ?? "Model Name",
                    tooltip: () => Util.GetString("configModelNameTooltip", returnNull: true) ?? "Name of the AI model to use.",
                    getValue: () => Config.ModelName,
                    setValue: (value) =>
                    {
                        Config.ModelName = value;
                    },
                    fieldId: "ModelName"
                );
            }
            if (constructorParameters.Contains("url", StringComparer.OrdinalIgnoreCase))
            {
                ConfigMenu.AddTextOption(
                    mod: ModManifest,
                    name: () => Util.GetString("configServerAddress", returnNull: true) ?? "Server Address",
                    tooltip: () => Util.GetString("configServerAddressTooltip", returnNull: true) ?? "URL of the server for local and Open AI compatible models.",
                    getValue: () => Config.ServerAddress,
                    setValue: (value) =>
                    {
                        Config.ServerAddress = value;
                    }
                );
            }
            ConfigMenu.AddNumberOption(
                mod: ModManifest,
                name: () => Util.GetString("configQueryTimeout", returnNull: true) ?? "Request timeout",
                tooltip: () => Util.GetString("configQueryTimeoutTooltip", returnNull: true) ?? "How many seconds to wait for a model response. Increase for slower models; lower for fast testing models.",
                getValue: () => Config.QueryTimeout,
                setValue: (value) =>
                {
                    Config.QueryTimeout = value;
                },
                min: 5,
                max: 180,
                interval: 5,
                fieldId: "QueryTimeout"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configTypedResponses", returnNull: true) ?? "Typed responses",
                tooltip: () => Util.GetString("configTypedResponsesTooltip", returnNull: true) ?? "Choose when the player can type a free-form response.",
                getValue: () => Config.TypedResponses,
                allowedValues: TypedResponseOptions,
                formatAllowedValue: FormatTypedResponseOption,
                setValue: (value) =>{ Config.TypedResponses = value; }
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configEnableSveCompatibility", returnNull: true) ?? "Enable SVE compatibility",
                tooltip: () => Util.GetString("configEnableSveCompatibilityTooltip", returnNull: true) ?? "When disabled, ValleyTalk stops using SVE-specific world summary text, character biographies, and dialogue samples in AI context.",
                getValue: () => Config.EnableSveCompatibility,
                setValue: (value) =>{ Config.EnableSveCompatibility = value; }
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configUseOptimizedPrompts", returnNull: true) ?? "Use optimized prompts",
                tooltip: () => Util.GetString("configUseOptimizedPromptsTooltip", returnNull: true) ?? "Warning: enabling this may increase the chance of model hallucinations.",
                getValue: () => Config.UseOptimizedPrompts,
                setValue: (value) =>{ Config.UseOptimizedPrompts = value; }
            );
            Config.SemanticContextRoutingTimeoutSeconds = Math.Clamp(Config.SemanticContextRoutingTimeoutSeconds, 2, 30);
            Config.RoutingThinkingLevel = LlmThinking.Normalize(Config.RoutingThinkingLevel, LlmThinking.Off);
            Config.ChatThinkingLevel = LlmThinking.Normalize(Config.ChatThinkingLevel, LlmThinking.Auto);
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configEnableSemanticContextRouting", returnNull: true) ?? "Semantic context routing",
                tooltip: () => Util.GetString("configEnableSemanticContextRoutingTooltip", returnNull: true) ?? "Before building the main prompt, run one compact semantic router to choose which context modules should be brief or full.",
                getValue: () => Config.EnableSemanticContextRouting,
                setValue: (value) =>{ Config.EnableSemanticContextRouting = value; }
            );
            ConfigMenu.AddNumberOption(
                mod: ModManifest,
                name: () => Util.GetString("configSemanticContextRoutingTimeoutSeconds", returnNull: true) ?? "Context routing timeout",
                tooltip: () => Util.GetString("configSemanticContextRoutingTimeoutSecondsTooltip", returnNull: true) ?? "Seconds to wait for semantic context routing before using the full conservative prompt.",
                getValue: () => Config.SemanticContextRoutingTimeoutSeconds,
                setValue: (value) =>{ Config.SemanticContextRoutingTimeoutSeconds = Math.Clamp(value, 2, 30); },
                min: 2,
                max: 30,
                interval: 1,
                fieldId: "SemanticContextRoutingTimeoutSeconds"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configRoutingThinkingLevel", returnNull: true) ?? "Routing thinking",
                tooltip: () => Util.GetString("configRoutingThinkingLevelTooltip", returnNull: true) ?? "Thinking level for compact routing/classifier passes. Off is fastest and recommended.",
                getValue: () => Config.RoutingThinkingLevel,
                setValue: (value) => { Config.RoutingThinkingLevel = LlmThinking.Normalize(value, LlmThinking.Off); },
                allowedValues: GetThinkingLevelOptions(Config),
                formatAllowedValue: FormatThinkingLevelOption,
                fieldId: "RoutingThinkingLevel"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configChatThinkingLevel", returnNull: true) ?? "Chat thinking",
                tooltip: () => Util.GetString("configChatThinkingLevelTooltip", returnNull: true) ?? "Thinking level for normal NPC replies when the selected model supports it. Auto leaves provider defaults unchanged.",
                getValue: () => Config.ChatThinkingLevel,
                setValue: (value) => { Config.ChatThinkingLevel = LlmThinking.Normalize(value, LlmThinking.Auto); },
                allowedValues: GetThinkingLevelOptions(Config),
                formatAllowedValue: FormatThinkingLevelOption,
                fieldId: "ChatThinkingLevel"
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configGenerateAiForNormalRightClick", returnNull: true) ?? "Generate AI dialogue on normal right-click",
                tooltip: () => Util.GetString("configGenerateAiForNormalRightClickTooltip", returnNull: true) ?? "When disabled, normal right-click keeps vanilla dialogue; hold the configured key while clicking an NPC to start a ValleyTalk AI chat.",
                getValue: () => Config.GenerateAiForNormalRightClick,
                setValue: (value) =>{ Config.GenerateAiForNormalRightClick = value; }
            );
            ConfigMenu.AddKeybind(
                mod: ModManifest,
                name: () => Util.GetString("configKeybind", returnNull: true) ?? "Key to start typed dialogue",
                tooltip: () => Util.GetString("configKeybindTooltip", returnNull: true) ?? "Hold this key while clicking an NPC to start ValleyTalk typed dialogue.",
                getValue: () => ModEntry.Config.InitiateTypedDialogueKey,
                setValue: (value) =>{ ModEntry.Config.InitiateTypedDialogueKey = value; }
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configTranslation", returnNull: true) ?? "Translate outputs",
                tooltip: () => Util.GetString("configTranslationTooltip", returnNull: true) ?? "Translate the AI model outputs to the game language.",
                getValue: () => Config.ApplyTranslation,
                setValue: (value) =>
                {
                    Config.ApplyTranslation = value;
                }
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configFrequencyGeneral", returnNull: true) ?? "Frequency of general lines",
                tooltip: () => Util.GetString("configFrequencyGeneralTooltip", returnNull: true) ?? "How often the mod should generate general lines.",
                getValue: () => GetFrequencyValue(Config.GeneralFrequency),
                setValue: (value) => { Config.GeneralFrequency = ResolveFrequencyValue(value, Config.GeneralFrequency); },
                allowedValues: GetFrequencyValues(),
                formatAllowedValue: FormatFrequencyValue,
                fieldId: "GeneralFrequency"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configFrequencyGift", returnNull: true) ?? "Frequency of gift lines",
                tooltip: () => Util.GetString("configFrequencyGiftTooltip", returnNull: true) ?? "How often the mod should generate gift reactions.",
                getValue: () => GetFrequencyValue(Config.GiftFrequency),
                setValue: (value) => { Config.GiftFrequency = ResolveFrequencyValue(value, Config.GiftFrequency); },
                allowedValues: GetFrequencyValues(),
                formatAllowedValue: FormatFrequencyValue,
                fieldId: "GiftFrequency"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configFrequencyMarriage", returnNull: true) ?? "Frequency of marriage lines",
                tooltip: () => Util.GetString("configFrequencyMarriageTooltip", returnNull: true) ?? "How often the mod should generate marriage lines.",
                getValue: () => GetFrequencyValue(Config.MarriageFrequency),
                setValue: (value) => { Config.MarriageFrequency = ResolveFrequencyValue(value, Config.MarriageFrequency); },
                allowedValues: GetFrequencyValues(),
                formatAllowedValue: FormatFrequencyValue,
                fieldId: "MarriageFrequency"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configDiableForCharacters", returnNull: true) ?? "Disable for characters",
                tooltip: () => Util.GetString("configDiableForCharactersTooltip", returnNull: true) ?? "Comma-separated list of villagers to disable, e.g. Abigail, Leah, Sam.",
                getValue: () => Config.DisableCharacters,
                setValue: (value) =>{ Config.DisableCharacters = value; }
            );
            ConfigMenu.AddParagraph(
                mod: ModManifest,
                text: () => {
                    var names = GetModelNames().ToList();
                    names.Sort();
                    if (names.Count() == 0) return Util.GetString("configNoModels", new { Provider = Config.Provider }, returnNull: true) ?? $"Unable to get model names for {Config.Provider} (maybe the API key wasn't set when this menu was opened?)";
                    var modelString = string.Join(", \n", names);
                    return Util.GetString("configModels", new { Provider = Config.Provider, Models = modelString }, returnNull: true) ?? $"The models available on provider {Config.Provider} are:\n{modelString}";
                }
            );
        }

        private static Dictionary<int, string> GetFrequencyOptions()
        {
            return new Dictionary<int, string>()
            {
                { 0, FormatFrequencyOption("configNever", "Never", "0%") },
                { 1, FormatFrequencyOption("configRarely", "Rarely", "25%") },
                { 2, FormatFrequencyOption("configOccasionally", "Occasionally", "50%") },
                { 3, FormatFrequencyOption("configMostly", "Mostly", "75%") },
                { 4, FormatFrequencyOption("configAlways", "Always", "100%") }
            };
        }

        private static string FormatFrequencyOption(string translationKey, string fallback, string percent)
        {
            var label = Util.GetString(translationKey, returnNull: true) ?? fallback;
            return $"{label} ({percent})";
        }

        private static string GetFrequencyLabel(int frequency)
        {
            var options = GetFrequencyOptions();
            return options.TryGetValue(frequency, out var label)
                ? label
                : options[4];
        }

        private static string[] GetFrequencyValues()
        {
            return GetFrequencyOptions().Keys
                .Select(key => key.ToString())
                .ToArray();
        }

        private static string GetFrequencyValue(int frequency)
        {
            int normalized = Math.Clamp(frequency, 0, 4);
            return normalized.ToString();
        }

        private static string FormatFrequencyValue(string value)
        {
            return GetFrequencyLabel(ResolveFrequencyValue(value, 4));
        }

        private static int ResolveFrequencyValue(string value, int currentValue)
        {
            if (int.TryParse(value, out int rawValue))
            {
                return Math.Clamp(rawValue, 0, 4);
            }

            var options = GetFrequencyOptions();
            var match = options.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Value))
            {
                return match.Key;
            }

            foreach (var fallback in GetInvariantFrequencyOptions())
            {
                if (string.Equals(fallback.Value, value, StringComparison.OrdinalIgnoreCase) || value?.Contains($"({fallback.Value})") == true)
                {
                    return fallback.Key;
                }
            }

            ModEntry.SMonitor?.Log($"Unable to parse ValleyTalk frequency option '{value}'. Keeping previous value {currentValue}.", LogLevel.Warn);
            return currentValue;
        }

        private static Dictionary<int, string> GetInvariantFrequencyOptions()
        {
            return new Dictionary<int, string>()
            {
                { 0, "0%" },
                { 1, "25%" },
                { 2, "50%" },
                { 3, "75%" },
                { 4, "100%" }
            };
        }

        private static string FormatTypedResponseOption(string value)
        {
            return value switch
            {
                "Always" => Util.GetString("configTypedResponsesAlways", returnNull: true) ?? "Always",
                "With Generated" => Util.GetString("configTypedResponsesWithGenerated", returnNull: true) ?? "With generated choices",
                "Never" => Util.GetString("configTypedResponsesNever", returnNull: true) ?? "Never",
                _ => value
            };
        }

        private static string FormatThinkingLevelOption(string value)
        {
            return LlmThinking.Normalize(value) switch
            {
                LlmThinking.Auto => Util.GetString("configThinkingAuto", returnNull: true) ?? "Auto",
                LlmThinking.Off => Util.GetString("configThinkingOff", returnNull: true) ?? "Off",
                LlmThinking.Minimal => Util.GetString("configThinkingMinimal", returnNull: true) ?? "Minimal",
                LlmThinking.Low => Util.GetString("configThinkingLow", returnNull: true) ?? "Low",
                LlmThinking.Medium => Util.GetString("configThinkingMedium", returnNull: true) ?? "Medium",
                LlmThinking.High => Util.GetString("configThinkingHigh", returnNull: true) ?? "High",
                LlmThinking.XHigh => Util.GetString("configThinkingXHigh", returnNull: true) ?? "Extra high",
                _ => value
            };
        }

        private static string[] GetThinkingLevelOptions(ModConfig config)
        {
            if (IsGeminiThinkingProvider(config))
            {
                return LlmThinking.Options
                    .Where(option => option != LlmThinking.XHigh)
                    .ToArray();
            }

            return LlmThinking.Options;
        }

        private static bool IsGeminiThinkingProvider(ModConfig config)
        {
            if (string.Equals(config.Provider, "Google", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(config.Provider, "OpenAiCompatible", StringComparison.OrdinalIgnoreCase)
                && LlmThinking.IsGeminiThinkingModel(config.ModelName);
        }

        private static string FormatProviderOption(string value)
        {
            return value switch
            {
                "OpenAI" => Util.GetString("configProviderOpenAi", returnNull: true) ?? "OpenAI official API",
                "OpenAiCompatible" => Util.GetString("configProviderOpenAiCompatible", returnNull: true) ?? "OpenAI-compatible API",
                "Google" => Util.GetString("configProviderGoogle", returnNull: true) ?? "Google Gemini",
                "Anthropic" => Util.GetString("configProviderAnthropic", returnNull: true) ?? "Anthropic Claude",
                "Mistral" => Util.GetString("configProviderMistral", returnNull: true) ?? "Mistral",
                "DeepSeek" => Util.GetString("configProviderDeepSeek", returnNull: true) ?? "DeepSeek",
                "VolcEngine" => Util.GetString("configProviderVolcEngine", returnNull: true) ?? "VolcEngine",
                "LlamaCpp" => Util.GetString("configProviderLlamaCpp", returnNull: true) ?? "Local Llama.cpp",
                _ => value
            };
        }

        private static void PersistConfig()
        {
            _modEntry.Helper.WriteConfig(ModEntry.Config);
        }

        private static void RefreshConfigMenu()
        {
            if (_isRefreshingMenu || _refreshQueued || ConfigMenu == null || ModManifest == null || _modEntry == null)
            {
                return;
            }

            _refreshQueued = true;
            _modEntry.Helper.Events.GameLoop.UpdateTicked += OnUpdateTickedRefreshConfigMenu;
        }

        private static void OnUpdateTickedRefreshConfigMenu(object sender, UpdateTickedEventArgs e)
        {
            if (_modEntry != null)
            {
                _modEntry.Helper.Events.GameLoop.UpdateTicked -= OnUpdateTickedRefreshConfigMenu;
            }

            _refreshQueued = false;
            RefreshConfigMenuNow();
        }

        private static void RefreshConfigMenuNow()
        {
            if (_isRefreshingMenu || ConfigMenu == null || ModManifest == null || _modEntry == null)
            {
                return;
            }

            try
            {
                _isRefreshingMenu = true;
                ConfigMenu.Unregister(ModManifest);
                Register(_modEntry);
                ConfigMenu.OpenModMenu(ModManifest);
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Unable to refresh ValleyTalk config menu: {ex}", LogLevel.Warn);
            }
            finally
            {
                _isRefreshingMenu = false;
            }
        }

        private static string[] GetModelNames()
        {
            var provider = ModEntry.LlmMap[ModEntry.Config.Provider];
            if (provider.GetInterfaces().Any(x => x.Name == "IGetModelNames"))
            {
                var paramsDict = new Dictionary<string, string>()
                {
                    { "apiKey", ModEntry.Config.ApiKey },
                    { "modelName", ModEntry.Config.ModelName },
                    { "url", ModEntry.Config.ServerAddress },
                    { "promptFormat", ModEntry.Config.PromptFormat }
                };
                var instance = Llm.CreateInstance(provider, paramsDict);
                return ((IGetModelNames)instance).GetModelNames();
            }
            else
            {
                return new string[] { };
            }
        }

        private static IGenericModConfigMenuApi GetConfigMenu(ModEntry modEntry)
        {
            // get Generic Mod Config Menu's API (if it's installed)
            return modEntry.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu"); 
        }

        private static void SetLlm()
        {
            if (!ModEntry.LlmMap.TryGetValue(ModEntry.Config.Provider, out var llmType))
            {
                ModEntry.SMonitor.Log($"Invalid LLM provider: {ModEntry.Config.Provider}", LogLevel.Error);
                return;
            }

            Llm.SetLlm(llmType, apiKey: ModEntry.Config.ApiKey, modelName: ModEntry.Config.ModelName, url: ModEntry.Config.ServerAddress, promptFormat: ModEntry.Config.PromptFormat);
        }
    }
}
