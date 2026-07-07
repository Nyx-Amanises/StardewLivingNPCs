namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 工厂入参：与 WP15 的 7 个配置字段一一对应（WP11 §4.5）。
/// 默认值按裁决 6：新装默认 Provider = "OpenAiCompatible"（与默认 openrouter 地址组合自洽）。
/// </summary>
internal sealed class LlmConnectionSettings
{
    public string Provider { get; init; } = "OpenAiCompatible";

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>空串用提供商各自默认模型。</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>仅 OpenAiCompatible 与 LlamaCpp 消费。</summary>
    public string ServerAddress { get; init; } = "https://openrouter.ai/api";

    /// <summary>仅 LlamaCpp 消费。</summary>
    public string PromptFormat { get; init; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";

    /// <summary>秒；运行期超时经 LlmHttp.DefaultTimeout 读全局配置实时生效，此字段仅供落表。</summary>
    public int QueryTimeout { get; init; } = 85;

    public bool SuppressConnectionCheck { get; init; }

    public static LlmConnectionSettings FromConfig(DialogueConfig config)
    {
        return new LlmConnectionSettings
        {
            Provider = config.Provider ?? string.Empty,
            ApiKey = config.ApiKey ?? string.Empty,
            ModelName = config.ModelName ?? string.Empty,
            ServerAddress = config.ServerAddress ?? string.Empty,
            PromptFormat = config.PromptFormat ?? string.Empty,
            QueryTimeout = config.QueryTimeout,
            SuppressConnectionCheck = config.SuppressConnectionCheck
        };
    }
}
