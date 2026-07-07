using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 火山引擎 Ark（Provider 值 "VolcEngine"，WP11 §3.4）。路径不含 /v1，不能复用 OpenAI 家族端点拼接。
/// 无流式实现。
/// </summary>
internal sealed class VolcEngineClient : LlmClientBase, IModelNameSource
{
    private const string ChatEndpoint = "https://ark.cn-beijing.volces.com/api/v3/chat/completions";
    private const string ModelsEndpoint = "https://ark.cn-beijing.volces.com/api/v3/models";

    /// <summary>强制思考模型：thinking 字段永不发送（发了直接报参数错误）。先于白名单判定。</summary>
    private static readonly string[] AlwaysThinkingModels =
    {
        "seed-1.6-thinking",
        "seed-1-6-thinking",
        "deepseek-r1"
    };

    /// <summary>可关闭思考的混合模型：仅这些才发 thinking:disabled；名单之外一律不发（保守设计，宁可少提速）。</summary>
    private static readonly string[] SwitchableThinkingModels =
    {
        "seed-1.6",
        "seed-1-6",
        "1.5-thinking",
        "1-5-thinking",
        "deepseek-v3.1",
        "deepseek-v3-1"
    };

    public VolcEngineClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "VolcEngine";

    protected override string DefaultModelName => "doubao-1.5-pro";

    /// <summary>白/黑名单（小写子串匹配）：本次是快速路由调用且当前模型确实支持关闭思考才发 thinking:disabled。</summary>
    internal static bool SupportsDisableThinking(string modelName)
    {
        string lower = (modelName ?? string.Empty).ToLowerInvariant();
        if (AlwaysThinkingModels.Any(lower.Contains))
        {
            return false;
        }

        return SwitchableThinkingModels.Any(lower.Contains);
    }

    protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
    {
        return new[] { new RequestCandidate(ChatEndpoint, BuildBody(request).ToString()) };
    }

    protected override async Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
    {
        string body = await LlmHttp.SendAsync(candidate.Url, candidate.Json, string.IsNullOrEmpty(ApiKey) ? null : ApiKey, null, request.TimeoutOverride, ct).ConfigureAwait(false);
        (string? text, TokenUsage? usage) = OpenAiChatClientBase.ExtractTextAndUsage(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AttemptOutcome(LlmReply.Failure(body, 200));
        }

        return new AttemptOutcome(LlmReply.Success(text, usage));
    }

    public IReadOnlyList<string> GetModelNames()
    {
        return ListModelNamesSafely(async ct =>
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                return Array.Empty<string>();
            }

            string body = await LlmHttp.SendAsync(ModelsEndpoint, null, ApiKey, null, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            var json = JObject.Parse(body);
            return (json["data"] as JArray)?
                .Select(item => item["id"]?.ToString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        });
    }

    /// <summary>JObject 手工构造天然忽略 null 字段（§3.4"序列化时忽略 null 字段"）。thinking 与 model 同级。</summary>
    internal JObject BuildBody(LlmRequest request)
    {
        var body = new JObject
        {
            ["model"] = EffectiveModelName,
            ["max_tokens"] = request.MaxTokens,
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "system", ["content"] = request.SystemPrompt },
                new JObject { ["role"] = "user", ["content"] = request.ConcatenatedUserContent() }
            }
        };

        if (request.DisableThinking && SupportsDisableThinking(EffectiveModelName))
        {
            body["thinking"] = new JObject { ["type"] = "disabled" };
        }

        // response_format 仅 disableThinking 路径（快速 JSON 通道），主对话请求体不带。
        if (request.DisableThinking)
        {
            body["response_format"] = new JObject { ["type"] = "json_object" };
        }

        return body;
    }
}
