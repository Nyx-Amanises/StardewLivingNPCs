using System;
using System.Collections.Generic;
using System.Linq;
using GenericModConfigMenu;
using StardewModdingAPI;

namespace ValleyTalk
{
    internal static class ModConfigMenu
    {
        private static IGenericModConfigMenuApi ConfigMenu;
        private static IManifest ModManifest;
        private static ModEntry _modEntry;
        private static bool _isRefreshingMenu;

        private static readonly string[] TypedResponseOptions = { "Always", "With Generated", "Never" };

        internal static void Register(ModEntry modEntry)
        {
            _modEntry = modEntry;
            var Config = ModEntry.Config;
            Config.EnsureModelProfiles();

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
                    ModEntry.Config.EnsureModelProfiles();
                },
                save: () =>
                {
                    if (!string.IsNullOrWhiteSpace(ModEntry.Config.ModelProfileNameToSave))
                    {
                        ModEntry.Config.SaveCurrentAsModelProfile(ModEntry.Config.ModelProfileNameToSave);
                        ModEntry.Config.ModelProfileNameToSave = string.Empty;
                    }
                    else
                    {
                        ModEntry.Config.SaveCurrentToActiveModelProfile();
                    }

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
            ConfigMenu.AddSectionTitle(
                mod: ModManifest,
                text: () => Util.GetString("configModelProfilesSection", returnNull: true) ?? "模型配置档案"
            );

            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configSaveModelProfile", returnNull: true) ?? "新档案名 / 覆盖档案名",
                tooltip: () => Util.GetString("configSaveModelProfileTooltip", returnNull: true) ?? "输入档案名后，点击“保存为此档案”会把当前模型设置保存下来；如果同名档案已存在则覆盖。",
                getValue: () => Config.ModelProfileNameToSave,
                setValue: value =>
                {
                    Config.ModelProfileNameToSave = value?.Trim() ?? string.Empty;
                },
                fieldId: "ModelProfileNameToSave"
            );

            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configSaveNamedModelProfile", returnNull: true) ?? "保存为此档案",
                tooltip: () => Util.GetString("configSaveNamedModelProfileTooltip", returnNull: true) ?? "点右侧方框会立刻把当前模型设置保存到上面填写的档案名，并切换到该档案。",
                getValue: () => false,
                setValue: value =>
                {
                    if (!value)
                    {
                        return;
                    }

                    if (Config.SaveCurrentAsModelProfile(Config.ModelProfileNameToSave))
                    {
                        Config.ModelProfileNameToSave = string.Empty;
                        SetLlm();
                        PersistConfig(saveActiveProfile: false);
                        RefreshConfigMenu();
                    }
                },
                fieldId: "SaveNamedModelProfile"
            );

            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configActiveModelProfile", returnNull: true) ?? "一键切换模型档案",
                tooltip: () => Util.GetString("configActiveModelProfileTooltip", returnNull: true) ?? "选择要使用的模型档案。若下方字段没有立刻刷新，点“应用所选档案”即可立即写入 config.json。",
                getValue: () => Config.ActiveModelProfile,
                setValue: value =>
                {
                    if (string.Equals(value, Config.ActiveModelProfile, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return;
                    }

                    Config.SaveCurrentToActiveModelProfile();
                    if (Config.ApplyModelProfile(value))
                    {
                        SetLlm();
                        PersistConfig(saveActiveProfile: false);
                        RefreshConfigMenu();
                    }
                },
                allowedValues: Config.ModelProfiles.Select(profile => profile.Name).ToArray(),
                fieldId: "ActiveModelProfile"
            );

            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configApplySelectedModelProfile", returnNull: true) ?? "应用所选档案",
                tooltip: () => Util.GetString("configApplySelectedModelProfileTooltip", returnNull: true) ?? "点右侧方框会立刻重新套用当前下拉框选择的模型档案，并写入 config.json。",
                getValue: () => false,
                setValue: value =>
                {
                    if (!value)
                    {
                        return;
                    }

                    if (Config.ApplyModelProfile(Config.ActiveModelProfile))
                    {
                        SetLlm();
                        PersistConfig(saveActiveProfile: false);
                        RefreshConfigMenu();
                    }
                },
                fieldId: "ApplySelectedModelProfile"
            );

            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configSaveActiveModelProfile", returnNull: true) ?? "保存当前档案",
                tooltip: () => Util.GetString("configSaveActiveModelProfileTooltip", returnNull: true) ?? "点右侧方框会立刻把当前 Provider、API Key、模型名、Base URL、超时等字段保存到当前档案。",
                getValue: () => false,
                setValue: value =>
                {
                    if (!value)
                    {
                        return;
                    }

                    Config.SaveCurrentToActiveModelProfile();
                    PersistConfig(saveActiveProfile: false);
                    RefreshConfigMenu();
                },
                fieldId: "SaveActiveModelProfile"
            );

            ConfigMenu.AddParagraph(
                mod: ModManifest,
                text: () => Util.GetString(
                    "configActiveModelProfileSummary",
                    new
                    {
                        Profile = Config.ActiveModelProfile,
                        Provider = Config.Provider,
                        Model = string.IsNullOrWhiteSpace(Config.ModelName) ? "(default)" : Config.ModelName,
                        Timeout = Config.QueryTimeout
                    },
                    returnNull: true
                ) ?? $"当前档案：{Config.ActiveModelProfile}\nProvider：{Config.Provider}\n模型：{(string.IsNullOrWhiteSpace(Config.ModelName) ? "(default)" : Config.ModelName)}\n超时：{Config.QueryTimeout} 秒"
            );

            // Create a string array of the options in the LlmType enum
            var llmTypes = ModEntry.LlmMap.Keys.ToArray();
            if (!ModEntry.LlmMap.ContainsKey(Config.Provider))
            {
                Config.Provider = llmTypes.FirstOrDefault() ?? "Mistral";
                Config.SaveCurrentToActiveModelProfile();
            }
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configProvider", returnNull: true) ?? "AI Model Provider",
                getValue: () => Config.Provider,
                setValue: value => 
                {
                    if (value == Config.Provider) return;
                    Config.SaveCurrentToActiveModelProfile();
                    Config.ApiKey = "";
                    Config.Provider = value; 
                    Config.SaveCurrentToActiveModelProfile();
                    SetLlm();
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
                    setValue: (value) =>{ Config.ApiKey = value; Config.SaveCurrentToActiveModelProfile(); SetLlm(); },
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
                        Config.ModelName = value; Config.SaveCurrentToActiveModelProfile(); SetLlm(); 
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
                    setValue: (value) =>{ Config.ServerAddress = value; Config.SaveCurrentToActiveModelProfile(); SetLlm(); }
                );
            }
            ConfigMenu.AddNumberOption(
                mod: ModManifest,
                name: () => Util.GetString("configQueryTimeout", returnNull: true) ?? "请求超时",
                tooltip: () => Util.GetString("configQueryTimeoutTooltip", returnNull: true) ?? "等待模型回复的秒数。较慢模型可以调高，快速测试模型可以调低。",
                getValue: () => Config.QueryTimeout,
                setValue: (value) =>{ Config.QueryTimeout = value; Config.SaveCurrentToActiveModelProfile(); },
                min: 5,
                max: 180,
                interval: 5,
                fieldId: "QueryTimeout"
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configTypedResponses", returnNull: true) ?? "自由输入",
                tooltip: () => Util.GetString("configTypedResponsesTooltip", returnNull: true) ?? "控制玩家什么时候可以自由输入回复。",
                getValue: () => Config.TypedResponses,
                allowedValues: TypedResponseOptions,
                formatAllowedValue: FormatTypedResponseOption,
                setValue: (value) =>{ Config.TypedResponses = value; }
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configAllowLocalContentPackDialogueForAi", returnNull: true) ?? "允许本地内容包对白进入 AI 上下文",
                tooltip: () => Util.GetString("configAllowLocalContentPackDialogueForAiTooltip", returnNull: true) ?? "即使内容包没有声明 PermitAiUse，也允许已加载的内容包对白进入 AI 上下文。",
                getValue: () => Config.AllowLocalContentPackDialogueForAi,
                setValue: (value) =>{ Config.AllowLocalContentPackDialogueForAi = value; }
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configGenerateAiForNormalRightClick", returnNull: true) ?? "普通右键也生成 AI 对话",
                tooltip: () => Util.GetString("configGenerateAiForNormalRightClickTooltip", returnNull: true) ?? "关闭时，普通右键保留原版对白；只有按住设置的热键点击 NPC 才会开启 ValleyTalk AI 聊天。",
                getValue: () => Config.GenerateAiForNormalRightClick,
                setValue: (value) =>{ Config.GenerateAiForNormalRightClick = value; }
            );
            ConfigMenu.AddKeybind(
                mod: ModManifest,
                name: () => Util.GetString("configKeybind", returnNull: true) ?? "开启自由输入的按键",
                tooltip: () => Util.GetString("configKeybindTooltip", returnNull: true) ?? "按住这个键点击 NPC，会开启 ValleyTalk 的自由输入对话。",
                getValue: () => ModEntry.Config.InitiateTypedDialogueKey,
                setValue: (value) =>{ ModEntry.Config.InitiateTypedDialogueKey = value; }
            );
            ConfigMenu.AddBoolOption(
                mod: ModManifest,
                name: () => Util.GetString("configTranslation", returnNull: true) ?? "翻译模型输出",
                tooltip: () => Util.GetString("configTranslationTooltip", returnNull: true) ?? "把模型输出翻译成游戏语言。中文模型或中文提示词下通常建议关闭。",
                getValue: () => Config.ApplyTranslation,
                setValue: (value) =>{ Config.ApplyTranslation = value; Config.SaveCurrentToActiveModelProfile(); }
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configFrequencyGeneral", returnNull: true) ?? "普通对白生成频率",
                tooltip: () => Util.GetString("configFrequencyGeneralTooltip", returnNull: true) ?? "控制普通对白由 AI 生成的频率。",
                getValue: () => GetFrequencyOptions()[Config.GeneralFrequency],
                setValue: (value) =>{ Config.GeneralFrequency = GetFrequencyOptions().First(x => x.Value == value).Key; },
                allowedValues: GetFrequencyOptions().Values.ToArray()
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configFrequencyGift", returnNull: true) ?? "礼物回应生成频率",
                tooltip: () => Util.GetString("configFrequencyGiftTooltip", returnNull: true) ?? "控制送礼回应由 AI 生成的频率。",
                getValue: () => GetFrequencyOptions()[Config.GiftFrequency],
                setValue: (value) =>{ Config.GiftFrequency = GetFrequencyOptions().First(x => x.Value == value).Key; },
                allowedValues: GetFrequencyOptions().Values.ToArray()
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configFrequencyMarriage", returnNull: true) ?? "婚后对白生成频率",
                tooltip: () => Util.GetString("configFrequencyMarriageTooltip", returnNull: true) ?? "控制婚后对白由 AI 生成的频率。",
                getValue: () => GetFrequencyOptions()[Config.MarriageFrequency],
                setValue: (value) =>{ Config.MarriageFrequency = GetFrequencyOptions().First(x => x.Value == value).Key; },
                allowedValues: GetFrequencyOptions().Values.ToArray()
            );
            ConfigMenu.AddTextOption(
                mod: ModManifest,
                name: () => Util.GetString("configDiableForCharacters", returnNull: true) ?? "禁用角色",
                tooltip: () => Util.GetString("configDiableForCharactersTooltip", returnNull: true) ?? "用逗号分隔要禁用 ValleyTalk 的角色英文名，例如 Abigail, Leah, Sam。",
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
                { 0, Util.GetString("configNever", returnNull: true) is { } never ? $"{never} (0%)" : "从不 (0%)" },
                { 1, Util.GetString("configRarely", returnNull: true) is { } rarely ? $"{rarely} (25%)" : "很少 (25%)" },
                { 2, Util.GetString("configOccasionally", returnNull: true) is { } occasionally ? $"{occasionally} (50%)" : "偶尔 (50%)" },
                { 3, Util.GetString("configMostly", returnNull: true) is { } mostly ? $"{mostly} (75%)" : "经常 (75%)" },
                { 4, Util.GetString("configAlways", returnNull: true) is { } always ? $"{always} (100%)" : "总是 (100%)" }
            };
        }

        private static string FormatTypedResponseOption(string value)
        {
            return value switch
            {
                "Always" => Util.GetString("configTypedResponsesAlways", returnNull: true) ?? "总是",
                "With Generated" => Util.GetString("configTypedResponsesWithGenerated", returnNull: true) ?? "有 AI 选项时",
                "Never" => Util.GetString("configTypedResponsesNever", returnNull: true) ?? "从不",
                _ => value
            };
        }

        private static string FormatProviderOption(string value)
        {
            return value switch
            {
                "OpenAI" => Util.GetString("configProviderOpenAi", returnNull: true) ?? "OpenAI 官方接口",
                "OpenAiCompatible" => Util.GetString("configProviderOpenAiCompatible", returnNull: true) ?? "OpenAI 兼容接口",
                "Google" => Util.GetString("configProviderGoogle", returnNull: true) ?? "Google Gemini",
                "Anthropic" => Util.GetString("configProviderAnthropic", returnNull: true) ?? "Anthropic Claude",
                "Mistral" => Util.GetString("configProviderMistral", returnNull: true) ?? "Mistral",
                "DeepSeek" => Util.GetString("configProviderDeepSeek", returnNull: true) ?? "DeepSeek",
                "VolcEngine" => Util.GetString("configProviderVolcEngine", returnNull: true) ?? "火山引擎",
                "LlamaCpp" => Util.GetString("configProviderLlamaCpp", returnNull: true) ?? "本地 Llama.cpp",
                _ => value
            };
        }

        private static void PersistConfig(bool saveActiveProfile = true)
        {
            if (saveActiveProfile)
            {
                ModEntry.Config.SaveCurrentToActiveModelProfile();
            }

            _modEntry.Helper.WriteConfig(ModEntry.Config);
        }

        private static void RefreshConfigMenu()
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
