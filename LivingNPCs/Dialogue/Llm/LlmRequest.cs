using System;
using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Dialogue;

internal enum LlmOutputFormat
{
    Text,
    JsonObject
}

/// <summary>
/// 统一请求模型（WP11 §4.1）：四段提示词是各家 Prompt Caching 策略的地基，
/// 不做请求级缓存声明的提供商把后三段按序拼接为 user 内容。
/// </summary>
internal sealed class LlmRequest
{
    /// <summary>系统段：角色扮演总指令（全局稳定）。</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>稳定世界段：世界观/游戏摘要（全局稳定；Claude 缓存断点 1）。</summary>
    public string StableContext { get; init; } = string.Empty;

    /// <summary>NPC 段：传记、示例对话（按 NPC 稳定；Claude 缓存断点 2）。</summary>
    public string NpcContext { get; init; } = string.Empty;

    /// <summary>可变尾段：历史、现场上下文、指令（每轮变化，永不进缓存）。</summary>
    public string Tail { get; init; } = string.Empty;

    /// <summary>响应引导前缀；仅 llama.cpp 的 PromptFormat 模板消费。</summary>
    public string ResponseStart { get; init; } = string.Empty;

    public int MaxTokens { get; init; } = 2048;

    /// <summary>旧名保留的辅助请求标记，用于传输选择；所有请求共享 ThinkingLevel，输出格式由 OutputFormat 独立指定。</summary>
    public bool DisableThinking { get; init; }

    /// <summary>Whether the caller expects plain text or a JSON object, independent of thinking level.</summary>
    public LlmOutputFormat OutputFormat { get; init; } = LlmOutputFormat.Text;

    public bool AllowRetry { get; init; } = true;

    /// <summary>一次性调用（连接自检等）显式覆盖单请求超时；空则用配置 QueryTimeout。</summary>
    public TimeSpan? TimeoutOverride { get; init; }

    /// <summary>Optional safe timing callback, once per OpenAI-compatible HTTP attempt, including failures.</summary>
    public Action<LlmTransportTiming>? TransportTimingObserver { get; init; }

    /// <summary>后三段按序拼接（无额外分隔符，段内换行由调用方自带）。</summary>
    public string ConcatenatedUserContent()
    {
        return string.Concat(StableContext, NpcContext, Tail);
    }

    public LlmRequest CloneWithoutRetry()
    {
        return new LlmRequest
        {
            SystemPrompt = SystemPrompt,
            StableContext = StableContext,
            NpcContext = NpcContext,
            Tail = Tail,
            ResponseStart = ResponseStart,
            MaxTokens = MaxTokens,
            DisableThinking = DisableThinking,
            OutputFormat = OutputFormat,
            AllowRetry = false,
            TimeoutOverride = TimeoutOverride,
            TransportTimingObserver = TransportTimingObserver
        };
    }
}
