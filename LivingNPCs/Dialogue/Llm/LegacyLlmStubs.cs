using System;
using System.Threading;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

internal class LegacyLlm
{
    public static LegacyLlm Instance { get; set; } = new LegacyLlmDummy();

    public virtual Task<LlmResponse> RunInference(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true,
        bool disableThinking = false,
        CancellationToken ct = default)
    {
        // WP10-TODO: 引擎重写后调用点改为直接消费 ILlmClient.CompleteAsync/StreamAsync，本过渡门面删除。
        throw new NotImplementedException("WP10-TODO: legacy call sites should migrate to ILlmClient.");
    }
}

internal sealed class LegacyLlmDummy : LegacyLlm
{
    public override Task<LlmResponse> RunInference(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true,
        bool disableThinking = false,
        CancellationToken ct = default)
    {
        return Task.FromResult(new LlmResponse
        {
            IsSuccess = false,
            ErrorMessage = "LLM client is not configured"
        });
    }
}

/// <summary>
/// 过渡桥：把搬运件的 LegacyLlm 调用（礼物邮件、记忆印象、语义路由、行动判定）
/// 转发到 Host 的当前客户端（带熔断防护）。四段参数与 LlmRequest 一一对应；
/// cacheContext 为旧世界死代码（CacheContexts 已废弃，WP11 §2），忽略。
/// </summary>
internal sealed class LegacyLlmBridge : LegacyLlm
{
    private readonly LlmClientHost _host;

    public LegacyLlmBridge(LlmClientHost host)
    {
        _host = host;
    }

    public override async Task<LlmResponse> RunInference(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true,
        bool disableThinking = false,
        CancellationToken ct = default)
    {
        ILlmClient? client = _host.Current;
        if (client == null)
        {
            return new LlmResponse
            {
                IsSuccess = false,
                ErrorMessage = "LLM client is not configured"
            };
        }

        // LegacyLlm is still the transport used by the router, metadata, action, gift-mail, and
        // memory side channels. Keep a final no-RSV boundary here so a newly added side channel
        // cannot accidentally bypass the structured collectors above.
        static string Sanitize(string? value)
            => global::LivingNPCs.RsvAiPolicy.RemoveBlockedLines(value);

        LlmReply reply = await client.CompleteAsync(
            new LlmRequest
            {
                SystemPrompt = Sanitize(systemPromptString),
                StableContext = Sanitize(gameCacheString),
                NpcContext = Sanitize(npcCacheString),
                Tail = Sanitize(promptString),
                ResponseStart = Sanitize(responseStart),
                MaxTokens = n_predict,
                AllowRetry = allowRetry,
                DisableThinking = disableThinking
            },
            ct).ConfigureAwait(false);

        return new LlmResponse
        {
            IsSuccess = reply.IsSuccess,
            Text = reply.Text,
            ErrorMessage = reply.ErrorMessage,
            Usage = reply.Usage
        };
    }
}

internal sealed class LlmResponse
{
    public bool IsSuccess { get; init; }
    public string Text { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public TokenUsage Usage { get; init; } = new();
}
