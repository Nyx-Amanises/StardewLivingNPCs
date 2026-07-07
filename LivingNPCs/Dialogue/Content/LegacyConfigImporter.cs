using System;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 旧 ValleyTalk config.json → 新 ModConfig 的字段映射（WP15 §3.1，执行时机归 WP14 的
/// LegacyFolderMigrator）。幂等由 <c>LegacyConfigImported</c> 标志保证（WP14 裁决 5：
/// 无论是否找到旧配置，跑过一次即置 true）。ApiKey 为敏感值，本类不产生任何含其值的日志。
/// </summary>
internal static class LegacyConfigImporter
{
    /// <summary>应用映射；返回 true 表示 config 有变化（含首次置标志），调用方负责 WriteConfig。</summary>
    public static bool Apply(LegacyValleyTalkConfig? legacy, ModConfig config)
    {
        if (config.LegacyConfigImported)
        {
            return false;
        }

        config.LegacyConfigImported = true;
        if (legacy == null)
        {
            return true;
        }

        // 三个撞名字段（§3.1 映射表，裁决 2）。
        if (legacy.EnableMod.HasValue)
        {
            config.EnableDialogueEngine = legacy.EnableMod.Value;
        }

        config.Debug |= legacy.Debug == true;
        if (legacy.EnableSveCompatibility.HasValue)
        {
            config.EnableSveCompatibility &= legacy.EnableSveCompatibility.Value;
        }

        // 其余同名字段原样迁移。
        if (legacy.ExportAiResponseLogs.HasValue)
        {
            config.ExportAiResponseLogs = legacy.ExportAiResponseLogs.Value;
        }

        if (legacy.HasKnownProvider)
        {
            config.Provider = legacy.Provider!;
        }

        if (legacy.ApiKey != null)
        {
            config.ApiKey = legacy.ApiKey;
        }

        if (legacy.ModelName != null)
        {
            config.ModelName = legacy.ModelName;
        }

        if (!string.IsNullOrWhiteSpace(legacy.ServerAddress))
        {
            config.ServerAddress = legacy.ServerAddress;
        }

        if (!string.IsNullOrWhiteSpace(legacy.PromptFormat))
        {
            config.PromptFormat = legacy.PromptFormat;
        }

        if (legacy.QueryTimeout.HasValue)
        {
            config.QueryTimeout = Math.Clamp(legacy.QueryTimeout.Value, 5, 180);
        }

        if (legacy.ApplyTranslation.HasValue)
        {
            config.ApplyTranslation = legacy.ApplyTranslation.Value;
        }

        if (legacy.GeneralFrequency.HasValue)
        {
            config.GeneralFrequency = Math.Clamp(legacy.GeneralFrequency.Value, 0, 4);
        }

        if (legacy.MarriageFrequency.HasValue)
        {
            config.MarriageFrequency = Math.Clamp(legacy.MarriageFrequency.Value, 0, 4);
        }

        if (legacy.GiftFrequency.HasValue)
        {
            config.GiftFrequency = Math.Clamp(legacy.GiftFrequency.Value, 0, 4);
        }

        if (legacy.GenerateAiForNormalRightClick.HasValue)
        {
            config.GenerateAiForNormalRightClick = legacy.GenerateAiForNormalRightClick.Value;
        }

        string? typed = NormalizeTypedResponses(legacy.TypedResponses);
        if (typed != null)
        {
            config.TypedResponses = typed;
        }

        if (legacy.UseOptimizedPrompts.HasValue)
        {
            config.UseOptimizedPrompts = legacy.UseOptimizedPrompts.Value;
        }

        if (legacy.EnableSemanticContextRouting.HasValue)
        {
            config.EnableSemanticContextRouting = legacy.EnableSemanticContextRouting.Value;
        }

        if (legacy.SemanticContextRoutingTimeoutSeconds.HasValue)
        {
            config.SemanticContextRoutingTimeoutSeconds = Math.Clamp(legacy.SemanticContextRoutingTimeoutSeconds.Value, 2, 30);
        }

        if (!string.IsNullOrWhiteSpace(legacy.RoutingThinkingLevel))
        {
            config.RoutingThinkingLevel = LlmThinking.Normalize(legacy.RoutingThinkingLevel, LlmThinking.Off);
        }

        if (!string.IsNullOrWhiteSpace(legacy.ChatThinkingLevel))
        {
            config.ChatThinkingLevel = LlmThinking.Normalize(legacy.ChatThinkingLevel, LlmThinking.Auto);
        }

        if (legacy.DisableCharacters != null)
        {
            config.DisableCharacters = legacy.DisableCharacters;
        }

        if (!string.IsNullOrWhiteSpace(legacy.InitiateTypedDialogueKey)
            && Enum.TryParse(legacy.InitiateTypedDialogueKey, ignoreCase: true, out SButton key))
        {
            config.InitiateTypedDialogueKey = key;
        }

        if (legacy.SuppressConnectionCheck.HasValue)
        {
            config.SuppressConnectionCheck = legacy.SuppressConnectionCheck.Value;
        }

        return true;
    }

    /// <summary>TypedResponses 合法值 Always / With Generated / Never（大小写不敏感，归一到规范形）。</summary>
    internal static string? NormalizeTypedResponses(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (string candidate in ModConfig.TypedResponsesValues)
        {
            if (string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
