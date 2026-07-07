using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 旧 ValleyTalk config.json 的字段全集（WP14 §3.2.2，字段名逐字精确）。本包只负责读取；
/// 字段语义与新旧映射表归 WP15（含 LegacyConfigImported 判据，裁决 5）。
/// ApiKey 为敏感值，不得写入日志。
/// </summary>
internal sealed class LegacyValleyTalkConfig
{
    /// <summary>旧 Provider 合法值（迁移校验用，大小写不敏感）。</summary>
    public static readonly string[] KnownProviders =
    {
        "LlamaCpp", "Google", "Anthropic", "OpenAI", "Mistral", "DeepSeek", "VolcEngine", "OpenAiCompatible"
    };

    public bool? EnableMod { get; set; }
    public bool? Debug { get; set; }
    public bool? ExportAiResponseLogs { get; set; }
    public string? Provider { get; set; }
    public string? ModelName { get; set; }
    public string? ServerAddress { get; set; }
    public string? PromptFormat { get; set; }
    public int? QueryTimeout { get; set; }
    public string? ApiKey { get; set; }
    public bool? ApplyTranslation { get; set; }
    public int? GeneralFrequency { get; set; }
    public int? MarriageFrequency { get; set; }
    public int? GiftFrequency { get; set; }
    public bool? GenerateAiForNormalRightClick { get; set; }
    public string? TypedResponses { get; set; }
    public bool? EnableSveCompatibility { get; set; }
    public bool? UseOptimizedPrompts { get; set; }
    public bool? EnableSemanticContextRouting { get; set; }
    public int? SemanticContextRoutingTimeoutSeconds { get; set; }
    public string? RoutingThinkingLevel { get; set; }
    public string? ChatThinkingLevel { get; set; }
    public string? DisableCharacters { get; set; }
    public string? InitiateTypedDialogueKey { get; set; }
    public bool? SuppressConnectionCheck { get; set; }

    /// <summary>原始 JSON（枚举/按键都是字符串形态），供 WP15 映射器取未建模的细节。</summary>
    [JsonIgnore]
    public JObject? Raw { get; set; }

    public bool HasKnownProvider =>
        !string.IsNullOrWhiteSpace(this.Provider)
        && Array.Exists(KnownProviders, candidate => string.Equals(candidate, this.Provider, StringComparison.OrdinalIgnoreCase));

    public static LegacyValleyTalkConfig? TryParse(string json)
    {
        try
        {
            var raw = JObject.Parse(json);
            var config = raw.ToObject<LegacyValleyTalkConfig>();
            if (config != null)
            {
                config.Raw = raw;
            }

            return config;
        }
        catch
        {
            return null;
        }
    }
}
