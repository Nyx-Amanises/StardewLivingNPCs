using System;

namespace LivingNPCs.Dialogue;

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

    /// <summary>快速 JSON 通道（语义路由、礼物邮件、行动判定）为 true，按路由思考档位取值。</summary>
    public bool DisableThinking { get; init; }

    public bool AllowRetry { get; init; } = true;

    /// <summary>一次性调用（连接自检等）显式覆盖单请求超时；空则用配置 QueryTimeout。</summary>
    public TimeSpan? TimeoutOverride { get; init; }

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
            AllowRetry = false,
            TimeoutOverride = TimeoutOverride
        };
    }
}
