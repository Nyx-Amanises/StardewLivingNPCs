using System;
using System.Collections.Generic;
using System.Linq;
using GenericModConfigMenu;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// GMCM 的对话引擎段（WP15 §4.9）：并入 LivingNPCs 现有一次 Register。
/// 连接字段按提供商元数据（WP11 注册表）动态显隐；切换 Provider 在用户点击保存后
/// 清 ApiKey、写盘并延迟重建菜单。GMCM 会在下拉项悬停时触发 OnFieldChanged，因此不能用该事件提交提供商变更。
/// </summary>
internal static class DialogueConfigMenuSection
{
    private const string ProviderFieldId = "dialogue.provider";

    private static bool isRefreshingMenu;
    private static bool refreshQueued;

    private static string? modelListCacheKey;
    private static string modelListCache = string.Empty;

    /// <summary>把引擎段控件追加到已 Register 的菜单上（ModConfigMenu.Register 末尾调用）。</summary>
    public static void Append(IGenericModConfigMenuApi api, ModEntry modEntry, ModConfig config)
    {
        var manifest = modEntry.ModManifest;

        // 打开菜单时归一化（§4.9）：钳制路由超时、思考档位过 Normalize。
        config.SemanticContextRoutingTimeoutSeconds = Math.Clamp(config.SemanticContextRoutingTimeoutSeconds, 2, 30);
        config.RoutingThinkingLevel = LlmThinking.Normalize(config.RoutingThinkingLevel, LlmThinking.Off);
        config.ChatThinkingLevel = LlmThinking.Normalize(config.ChatThinkingLevel, LlmThinking.Auto);

        LlmClientFactory.TryGetMetadata(config.Provider, out LlmProviderMetadata? metadata);

        api.AddSectionTitle(manifest, () => T("dialogue.config.section"));

        api.AddBoolOption(
            mod: manifest,
            name: () => T("dialogue.config.enableEngine.name"),
            tooltip: () => T("dialogue.config.enableEngine.tooltip"),
            getValue: () => config.EnableDialogueEngine,
            setValue: value => config.EnableDialogueEngine = value);

#if DEBUG
        api.AddBoolOption(
            mod: manifest,
            name: () => T("dialogue.config.debug.name"),
            tooltip: () => T("dialogue.config.debug.tooltip"),
            getValue: () => config.Debug,
            setValue: value => config.Debug = value);
#endif

        api.AddTextOption(
            mod: manifest,
            name: () => T("dialogue.config.provider.name"),
            tooltip: () => T("dialogue.config.provider.tooltip"),
            getValue: () => config.Provider,
            setValue: value => config.Provider = value ?? config.Provider,
            allowedValues: LlmClientFactory.ProviderIds.ToArray(),
            formatAllowedValue: FormatProvider,
            fieldId: ProviderFieldId);

        if (metadata is { RequiresApiKey: true })
        {
            api.AddTextOption(
                mod: manifest,
                name: () => T("dialogue.config.apiKey.name"),
                tooltip: () => T("dialogue.config.apiKey.tooltip"),
                getValue: () => config.ApiKey,
                setValue: value => config.ApiKey = value ?? string.Empty);
        }

        if (metadata is { RequiresModelName: true })
        {
            api.AddTextOption(
                mod: manifest,
                name: () => T("dialogue.config.modelName.name"),
                tooltip: () => T("dialogue.config.modelName.tooltip"),
                getValue: () => config.ModelName,
                setValue: value => config.ModelName = value ?? string.Empty);
        }

        if (metadata is { RequiresServerAddress: true })
        {
            api.AddTextOption(
                mod: manifest,
                name: () => T("dialogue.config.serverAddress.name"),
                tooltip: () => T("dialogue.config.serverAddress.tooltip"),
                getValue: () => config.ServerAddress,
                setValue: value => config.ServerAddress = value ?? string.Empty);
        }

        api.AddNumberOption(
            mod: manifest,
            name: () => T("dialogue.config.queryTimeout.name"),
            tooltip: () => T("dialogue.config.queryTimeout.tooltip"),
            getValue: () => config.QueryTimeout,
            setValue: value => config.QueryTimeout = value,
            min: 5,
            max: 180,
            interval: 5);

        // EnableSveCompatibility 与行为系统共用一个字段，已在"兼容"段展示，引擎段不重复列出。

        api.AddBoolOption(
            mod: manifest,
            name: () => T("dialogue.config.useOptimizedPrompts.name"),
            tooltip: () => T("dialogue.config.useOptimizedPrompts.tooltip"),
            getValue: () => config.UseOptimizedPrompts,
            setValue: value => config.UseOptimizedPrompts = value);

        api.AddBoolOption(
            mod: manifest,
            name: () => T("dialogue.config.enableRouting.name"),
            tooltip: () => T("dialogue.config.enableRouting.tooltip"),
            getValue: () => config.EnableSemanticContextRouting,
            setValue: value => config.EnableSemanticContextRouting = value);

        api.AddNumberOption(
            mod: manifest,
            name: () => T("dialogue.config.routingTimeout.name"),
            tooltip: () => T("dialogue.config.routingTimeout.tooltip"),
            getValue: () => config.SemanticContextRoutingTimeoutSeconds,
            setValue: value => config.SemanticContextRoutingTimeoutSeconds = Math.Clamp(value, 2, 30),
            min: 2,
            max: 30,
            interval: 1);

        string[] thinkingOptions = ThinkingOptionsFor(config);

        api.AddTextOption(
            mod: manifest,
            name: () => T("dialogue.config.routingThinking.name"),
            tooltip: () => T("dialogue.config.routingThinking.tooltip"),
            getValue: () => LlmThinking.Normalize(config.RoutingThinkingLevel, LlmThinking.Off),
            setValue: value => config.RoutingThinkingLevel = LlmThinking.Normalize(value, LlmThinking.Off),
            allowedValues: thinkingOptions,
            formatAllowedValue: FormatThinkingLevel);

        api.AddTextOption(
            mod: manifest,
            name: () => T("dialogue.config.chatThinking.name"),
            tooltip: () => T("dialogue.config.chatThinking.tooltip"),
            getValue: () => LlmThinking.Normalize(config.ChatThinkingLevel, LlmThinking.Auto),
            setValue: value => config.ChatThinkingLevel = LlmThinking.Normalize(value, LlmThinking.Auto),
            allowedValues: thinkingOptions,
            formatAllowedValue: FormatThinkingLevel);

        api.AddBoolOption(
            mod: manifest,
            name: () => T("dialogue.config.normalRightClick.name"),
            tooltip: () => T("dialogue.config.normalRightClick.tooltip"),
            getValue: () => config.GenerateAiForNormalRightClick,
            setValue: value => config.GenerateAiForNormalRightClick = value);

        api.AddKeybind(
            mod: manifest,
            name: () => T("dialogue.config.typedDialogueKey.name"),
            tooltip: () => T("dialogue.config.typedDialogueKey.tooltip"),
            getValue: () => config.InitiateTypedDialogueKey,
            setValue: value => config.InitiateTypedDialogueKey = value);

        AddFrequencyOption(api, manifest, "dialogue.config.generalFrequency",
            () => config.GeneralFrequency, value => config.GeneralFrequency = value);
        AddFrequencyOption(api, manifest, "dialogue.config.giftFrequency",
            () => config.GiftFrequency, value => config.GiftFrequency = value);
        AddFrequencyOption(api, manifest, "dialogue.config.marriageFrequency",
            () => config.MarriageFrequency, value => config.MarriageFrequency = value);

        api.AddTextOption(
            mod: manifest,
            name: () => T("dialogue.config.disableCharacters.name"),
            tooltip: () => T("dialogue.config.disableCharacters.tooltip"),
            getValue: () => config.DisableCharacters,
            setValue: value => config.DisableCharacters = value ?? string.Empty);

        if (metadata is { SupportsModelList: true })
        {
            api.AddParagraph(manifest, () => BuildModelListText(config));
        }
    }

    /// <summary>合并 reset 里的引擎部分：只动引擎字段（§4.9）。</summary>
    public static void ResetEngineDefaults(ModConfig config)
    {
        config.ResetDialogueEngineDefaults();
    }

    /// <summary>
    /// 合并 save 里的引擎部分：先按当前连接参数重建 LLM 客户端（WP11 入口），
    /// 再由调用方 WriteConfig（§4.9）。同时刷新运行时配置视图与 SVE 相关缓存。
    /// </summary>
    public static void OnSave(ModEntry modEntry, ModConfig config)
    {
        bool providerChanged = ClearApiKeyWhenProviderChanged(config, DialogueServices.Config.Provider);
        bool sveChanged = DialogueServices.Config.EnableSveCompatibility != config.EnableSveCompatibility;
        DialogueServices.Config.SyncFrom(config);

        if (sveChanged)
        {
            // Bios 资产按 SVE 开关选目录（§4.4）：开关翻转必须让内容管线重新提供。
            try
            {
                modEntry.Helper.GameContent.InvalidateCache(info => info.Name.StartsWith(ContentAssetNames.BiosPrefix));
            }
            catch (Exception ex)
            {
                modEntry.Monitor.Log(
                    Util.GetConsoleString(
                        "dialogue.log.stepFailed",
                        new { step = "invalidate biography assets after the SVE toggle", error = ex.Message },
                        $"Failed to invalidate biography assets after the SVE toggle: {ex.Message}"),
                    LogLevel.Warn);
            }

            DialogueContentService.Instance?.InvalidateWorldSummaries();
        }

        if (config.EnableDialogueEngine && !DialoguePersistence.LegacyValleyTalkLoaded)
        {
            LlmClientHost.Instance.ReplaceClient(LlmConnectionSettings.FromConfig(DialogueServices.Config));
        }

        if (providerChanged)
        {
            QueueMenuRefresh(modEntry, config);
        }
    }

    /// <summary>
    /// GMCM 的下拉框在鼠标悬停于选项时只更新自己的缓存值，真正调用 setValue 是在保存阶段。
    /// 因此提供商变更的副作用必须放在 OnSave，不能放在 OnFieldChanged。
    /// </summary>
    internal static bool ClearApiKeyWhenProviderChanged(ModConfig config, string? previousProvider)
    {
        if (string.Equals(config.Provider, previousProvider, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        config.ApiKey = string.Empty;
        return true;
    }

    private static void QueueMenuRefresh(ModEntry modEntry, ModConfig config)
    {
        if (isRefreshingMenu)
        {
            refreshQueued = true;
            return;
        }

        isRefreshingMenu = true;

        void Handler(object? sender, UpdateTickedEventArgs e)
        {
            modEntry.Helper.Events.GameLoop.UpdateTicked -= Handler;
            try
            {
                var api = modEntry.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
                if (api != null)
                {
                    api.Unregister(modEntry.ModManifest);
                    ModConfigMenu.Register(modEntry, config);
                    api.OpenModMenu(modEntry.ModManifest);
                }
            }
            catch (Exception ex)
            {
                modEntry.Monitor.Log(
                    Util.GetConsoleString(
                        "dialogue.log.stepFailed",
                        new { step = "refresh the config menu after a provider change", error = ex.Message },
                        $"Failed to refresh the config menu after a provider change: {ex.Message}"),
                    LogLevel.Warn);
            }
            finally
            {
                isRefreshingMenu = false;
                if (refreshQueued)
                {
                    refreshQueued = false;
                    QueueMenuRefresh(modEntry, config);
                }
            }
        }

        modEntry.Helper.Events.GameLoop.UpdateTicked += Handler;
    }

    private static void AddFrequencyOption(
        IGenericModConfigMenuApi api,
        IManifest manifest,
        string keyBase,
        Func<int> getValue,
        Action<int> setValue)
    {
        api.AddTextOption(
            mod: manifest,
            name: () => T(keyBase + ".name"),
            tooltip: () => T(keyBase + ".tooltip"),
            getValue: () => getValue().ToString(),
            setValue: value => setValue(ParseFrequency(value, getValue())),
            allowedValues: new[] { "0", "1", "2", "3", "4" },
            formatAllowedValue: value => FormatFrequency(ParseFrequency(value, 4)));
    }

    /// <summary>档位显示："{档位名} ({百分比})"，0–4 档对应 0/25/50/75/100%。</summary>
    internal static string FormatFrequency(int level)
    {
        int clamped = Math.Clamp(level, 0, 4);
        return $"{T($"dialogue.config.freq.{clamped}")} ({clamped * 25}%)";
    }

    /// <summary>解析容错（§4.9）：整数钳 [0,4] → 本地化显示名匹配 → 不变式 "(N%)" 子串 → 记 Warn 保持原值。</summary>
    internal static int ParseFrequency(string? raw, int fallback)
    {
        string value = raw?.Trim() ?? string.Empty;
        if (int.TryParse(value, out int numeric))
        {
            return Math.Clamp(numeric, 0, 4);
        }

        for (int level = 0; level <= 4; level++)
        {
            if (string.Equals(value, FormatFrequency(level), StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        for (int level = 0; level <= 4; level++)
        {
            if (value.Contains($"({level * 25}%)", StringComparison.Ordinal))
            {
                return level;
            }
        }

        DialogueServices.Monitor?.Log(
            Util.GetConsoleString(
                "dialogue.log.freqParseFailed",
                new { value = raw },
                $"Could not parse dialogue frequency value '{raw}'; keeping the previous setting."),
            LogLevel.Warn);
        return fallback;
    }

    /// <summary>思考档位下拉的选项集：Gemini 系提供商剔除 XHigh（§4.9）。</summary>
    internal static string[] ThinkingOptionsFor(ModConfig config)
    {
        bool isGemini = string.Equals(config.Provider, "Google", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(config.Provider, "OpenAiCompatible", StringComparison.OrdinalIgnoreCase)
                && LlmThinking.IsGeminiThinkingModel(config.ModelName));
        return isGemini
            ? LlmThinking.Options.Where(option => option != LlmThinking.XHigh).ToArray()
            : LlmThinking.Options.ToArray();
    }

    private static string BuildModelListText(ModConfig config)
    {
        string cacheKey = string.Join("", config.Provider, config.ApiKey, config.ModelName, config.ServerAddress);
        if (string.Equals(modelListCacheKey, cacheKey, StringComparison.Ordinal))
        {
            return modelListCache;
        }

        string text;
        try
        {
            LlmClientBase? client = LlmClientFactory.TryCreate(new LlmConnectionSettings
            {
                Provider = config.Provider,
                ApiKey = config.ApiKey,
                ModelName = config.ModelName,
                ServerAddress = config.ServerAddress,
                PromptFormat = config.PromptFormat,
                QueryTimeout = config.QueryTimeout,
                SuppressConnectionCheck = true
            });

            List<string> names = client is IModelNameSource source
                ? source.GetModelNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

            text = names.Count == 0
                ? T("dialogue.configNoModels")
                : Util.GetConsoleString(
                    "dialogue.configModels",
                    new { Provider = config.Provider, Models = string.Join(",\n", names) },
                    $"Models available from {config.Provider}:\n{string.Join(",\n", names)}");
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.listModelsFailed",
                    new { provider = config.Provider, error = ex.Message },
                    $"Failed to list models for {config.Provider}: {ex.Message}"),
                LogLevel.Warn);
            text = T("dialogue.configNoModels");
        }

        modelListCacheKey = cacheKey;
        modelListCache = text;
        return text;
    }

    private static string FormatProvider(string providerId)
    {
        return Util.GetConsoleString($"dialogue.config.provider.{providerId.ToLowerInvariant()}", null, providerId);
    }

    private static string FormatThinkingLevel(string level)
    {
        return Util.GetConsoleString($"dialogue.config.thinking.{level.ToLowerInvariant()}", null, level);
    }

    private static string T(string key)
    {
        return Util.GetConsoleString(key, null, key);
    }
}
