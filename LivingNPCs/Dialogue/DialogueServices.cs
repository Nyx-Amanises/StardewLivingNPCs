using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue;

internal static class DialogueServices
{
    public static IModHelper Helper { get; private set; } = null!;
    public static IMonitor Monitor { get; private set; } = null!;
    public static DialogueConfig Config { get; private set; } = new();

    public static void Initialize(IModHelper helper, IMonitor monitor, DialogueConfig? config = null)
    {
        Helper = helper;
        Monitor = monitor;
        if (config != null)
        {
            Config = config;
        }
    }
}

/// <summary>
/// 对话引擎的运行时配置视图（引擎代码只读它，不直接碰 LivingNPCs.ModConfig）。
/// 由 <see cref="DialogueConfig.SyncFrom"/> 在启动与 GMCM 保存时从 ModConfig 整体刷新（WP15 §3.1）。
/// </summary>
internal sealed class DialogueConfig
{
    /// <summary>行为决策附加 pass：裁决 3 维持内部常量、不持久化（§3.1 表尾两行）。</summary>
    public const bool ActionDecisionPassDefault = true;
    public const int ActionDecisionTimeoutSecondsDefault = 12;

    public bool EnableDialogueEngine { get; set; } = true;
    public bool Debug { get; set; }
    public bool ExportAiResponseLogs { get; set; }
    public bool EnableSemanticContextRouting { get; set; }
    public bool EnableLivingNpcActionDecisionPass { get; set; } = ActionDecisionPassDefault;
    public string Provider { get; set; } = "unset";
    public string ModelName { get; set; } = "unset";
    public string ApiKey { get; set; } = "";
    public string ServerAddress { get; set; } = string.Empty;
    public string PromptFormat { get; set; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";
    public bool SuppressConnectionCheck { get; set; }
    public int QueryTimeout { get; set; } = 85;
    public int SemanticContextRoutingTimeoutSeconds { get; set; } = 8;
    public int LivingNpcActionDecisionTimeoutSeconds { get; set; } = ActionDecisionTimeoutSecondsDefault;
    public string RoutingThinkingLevel { get; set; } = "off";
    public string ChatThinkingLevel { get; set; } = "auto";
    public SButton InitiateTypedDialogueKey { get; set; } = SButton.None;
    public bool AllowWakeSleepingNpc { get; set; } = true;
    public bool ApplyTranslation { get; set; } = true;
    public bool UseOptimizedPrompts { get; set; }
    public bool EnableSveCompatibility { get; set; } = true;
    public int GeneralFrequency { get; set; } = 4;
    public int MarriageFrequency { get; set; } = 4;
    public int GiftFrequency { get; set; } = 4;
    public bool GenerateAiForNormalRightClick { get; set; }
    public string TypedResponses { get; set; } = "With Generated";
    public IReadOnlyList<string> DisabledCharacters { get; set; } = Array.Empty<string>();

    /// <summary>从持久化配置整体刷新运行时视图（Entry 读配置后与 GMCM 保存回调调用）。</summary>
    public void SyncFrom(ModConfig source)
    {
        this.EnableDialogueEngine = source.EnableDialogueEngine;
        this.Debug = source.Debug;
        this.ExportAiResponseLogs = source.ExportAiResponseLogs;
        this.EnableSemanticContextRouting = source.EnableSemanticContextRouting;
        this.Provider = source.Provider;
        this.ModelName = source.ModelName;
        this.ApiKey = source.ApiKey;
        this.ServerAddress = source.ServerAddress;
        this.PromptFormat = source.PromptFormat;
        this.SuppressConnectionCheck = source.SuppressConnectionCheck;
        this.QueryTimeout = source.QueryTimeout;
        this.SemanticContextRoutingTimeoutSeconds = Math.Clamp(source.SemanticContextRoutingTimeoutSeconds, 2, 30);
        this.RoutingThinkingLevel = source.RoutingThinkingLevel;
        this.ChatThinkingLevel = source.ChatThinkingLevel;
        this.InitiateTypedDialogueKey = source.InitiateTypedDialogueKey;
        this.AllowWakeSleepingNpc = source.AllowWakeSleepingNpc;
        this.ApplyTranslation = source.ApplyTranslation;
        this.UseOptimizedPrompts = source.UseOptimizedPrompts;
        this.EnableSveCompatibility = source.EnableSveCompatibility;
        this.GeneralFrequency = source.GeneralFrequency;
        this.MarriageFrequency = source.MarriageFrequency;
        this.GiftFrequency = source.GiftFrequency;
        this.GenerateAiForNormalRightClick = source.GenerateAiForNormalRightClick;
        this.TypedResponses = source.TypedResponses;
        this.DisabledCharacters = source.DisabledCharacterList;
        this.EnableLivingNpcActionDecisionPass = ActionDecisionPassDefault;
        this.LivingNpcActionDecisionTimeoutSeconds = ActionDecisionTimeoutSecondsDefault;
    }
}

/// <summary>
/// 引擎侧字符串查找（WP15 §4.8 重写）：玩家可见文案走 SMAPI i18n，键统一带 <c>dialogue.</c>
/// 前缀（未带前缀的旧调用点自动补上）；i18n 查不到时回退提示词表（老代码里 ui/输出类键
/// 混用两族，回退保证两族都可达）。缺失时不返回 SMAPI 占位串。
/// </summary>
internal static class Util
{
    /// <summary>提示词表回退（DialogueContentService 装配时挂上；(key, tokens) → 值或 null）。</summary>
    internal static Func<string, object?, string?>? PromptFallback;

    private const string I18nPrefix = "dialogue.";

    public static string GetString(object? context, string key, object? tokens = null, bool returnNull = false)
    {
        return GetString(key, tokens, returnNull);
    }

    public static string GetConsoleString(string key, object? tokens, string fallback)
    {
        string? value = Resolve(key, tokens);
        return string.IsNullOrWhiteSpace(value) ? fallback : value!;
    }

    public static string GetString(string key, object? tokens = null, bool returnNull = false)
    {
        string? value = Resolve(key, tokens);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value!;
        }

        return returnNull ? null! : key;
    }

    private static string? Resolve(string key, object? tokens)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var helper = DialogueServices.Helper;
        if (helper != null)
        {
            string i18nKey = key.StartsWith(I18nPrefix, StringComparison.Ordinal) ? key : I18nPrefix + key;
            Translation translation = tokens == null
                ? helper.Translation.Get(i18nKey)
                : helper.Translation.Get(i18nKey, tokens);
            string? text = translation.UsePlaceholder(false).ToString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return PromptFallback?.Invoke(key, tokens);
    }
}
