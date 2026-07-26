using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// Anthropic Claude（Provider 值 "Anthropic"，WP11 §3.2）。无流式实现，引擎侧经基类默认包装走非流式。
/// </summary>
internal sealed class ClaudeClient : LlmClientBase, IModelNameSource
{
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string ModelsEndpoint = "https://api.anthropic.com/v1/models";

    /// <summary>四段尾部全空的退化请求用的最小中性 user 内容（Anthropic 拒绝空内容消息）。</summary>
    internal const string EmptyUserContentPlaceholder = "(continue)";

    public ClaudeClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "Anthropic";

    public override bool IsHighlySensoredModel => true;

    // claude-3-5-haiku 已于 2026-02 退役，请求会 404，故默认 claude-haiku-4-5。
    protected override string DefaultModelName => "claude-haiku-4-5";

    protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
    {
        return new[] { new RequestCandidate(MessagesEndpoint, BuildBody(request).ToString()) };
    }

    protected override async Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
    {
        string body = await LlmHttp.SendAsync(candidate.Url, candidate.Json, null, BuildHeaders(), request.TimeoutOverride, ct).ConfigureAwait(false);
        var json = JObject.Parse(body);
        string? text = (json["content"] as JArray)?.FirstOrDefault()?["text"]?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AttemptOutcome(LlmReply.Failure(body, 200));
        }

        TokenUsage? usage = json["usage"] is JObject usageJson && usageJson.HasValues
            ? TokenUsage.FromClaudeUsage(usageJson)
            : null;
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

            string body = await LlmHttp.SendAsync(ModelsEndpoint, null, null, BuildHeaders(), TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            var json = JObject.Parse(body);
            return (json["data"] as JArray)?
                .Select(item => item["id"]?.ToString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        });
    }

    /// <summary>认证走 x-api-key + anthropic-version，不用 Bearer；缓存已 GA，不需要 beta header。</summary>
    private IReadOnlyDictionary<string, string> BuildHeaders()
    {
        return new Dictionary<string, string>
        {
            ["x-api-key"] = ApiKey,
            ["anthropic-version"] = "2023-06-01"
        };
    }

    /// <summary>
    /// 缓存断点放置（§3.2，近期精修逐字保留）：断点 1 盖住"系统提示词+世界摘要"全 NPC 共享稳定前缀，
    /// 断点 2 让同一 NPC 的连续轮次复用传记/示例段；可变尾段永远不进缓存。
    /// 低于模型最小可缓存前缀（Haiku 4.5 为 4096 token）的断点被服务端静默忽略、不报错，属预期行为。
    /// 空文本块一律不发（Anthropic 端点拒绝空 text 块，自检等场景稳定段可为空）；
    /// NpcContext 与 Tail 皆空的退化调用以 <see cref="EmptyUserContentPlaceholder"/> 占位，
    /// 保证 user 消息内容非空合法（Anthropic 对空内容消息直接 400）。
    /// </summary>
    internal JObject BuildBody(LlmRequest request)
    {
        var body = new JObject
        {
            ["model"] = EffectiveModelName,
            ["max_tokens"] = request.MaxTokens
        };

        var systemBlocks = new JArray();
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            systemBlocks.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = request.SystemPrompt
            });
        }

        if (!string.IsNullOrEmpty(request.StableContext))
        {
            systemBlocks.Add(new JObject
            {
                ["type"] = "text",
                ["text"] = request.StableContext,
                ["cache_control"] = new JObject { ["type"] = "ephemeral" }
            });
        }

        if (systemBlocks.Count > 0)
        {
            body["system"] = systemBlocks;
        }

        JToken userContent;
        if (string.IsNullOrEmpty(request.NpcContext))
        {
            // 发现 5 兜底：两段皆空时不发空串（正常引擎请求 Tail 恒非空，仅旁路退化调用可达）。
            userContent = string.IsNullOrEmpty(request.Tail) ? EmptyUserContentPlaceholder : request.Tail;
        }
        else
        {
            var blocks = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = request.NpcContext,
                    ["cache_control"] = new JObject { ["type"] = "ephemeral" }
                }
            };
            if (!string.IsNullOrEmpty(request.Tail))
            {
                blocks.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = request.Tail
                });
            }

            userContent = blocks;
        }

        body["messages"] = new JArray
        {
            new JObject
            {
                ["role"] = "user",
                ["content"] = userContent
            }
        };

        return body;
    }
}
