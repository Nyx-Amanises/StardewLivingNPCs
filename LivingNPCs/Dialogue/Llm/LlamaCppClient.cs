using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// llama.cpp server（Provider 值 "LlamaCpp"，WP11 §3.5）。ServerAddress 原样作为完整端点，无认证。
/// 旧实现失败无限重试（睡 1 秒直到成功）；新实现改为有限 3 次（裁决 1，明确的行为修正）。
/// </summary>
internal sealed class LlamaCppClient : LlmClientBase, ITokenProbabilities
{
    private readonly string _endpoint;
    private readonly string _promptFormat;

    public LlamaCppClient(LlmConnectionSettings settings)
        : base(settings)
    {
        _endpoint = (settings.ServerAddress ?? string.Empty).Trim();
        _promptFormat = string.IsNullOrEmpty(settings.PromptFormat)
            ? "[INST] {system}\n{prompt}[/INST]\n{response_start}"
            : settings.PromptFormat;
    }

    public override string ProviderId => "LlamaCpp";

    protected override string DefaultModelName => string.Empty;

    /// <summary>唯一的非空 ExtraInstructions：本地小模型需要显式约束输出形态（§4.7）。</summary>
    public override string ExtraInstructions => "Include only the new line and any responses in the output, no descriptions or explanations.";

    protected override TimeSpan RetryDelay => RetryDelayOverrideForTests ?? TimeSpan.FromSeconds(1);

    protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
    {
        return new[] { new RequestCandidate(_endpoint, BuildBody(request).ToString()) };
    }

    protected override async Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
    {
        string body = await LlmHttp.SendAsync(candidate.Url, candidate.Json, null, null, request.TimeoutOverride, ct).ConfigureAwait(false);
        var json = JObject.Parse(body);
        string? text = json["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AttemptOutcome(LlmReply.Failure(body, 200));
        }

        // usage 由 timings 折算，随响应交给调用方，经 WP10 上报 WP14 的会话级用量统计。
        TokenUsage? usage = json["timings"] is JObject timings && timings.HasValues
            ? TokenUsage.FromLlamaCppTimings(timings)
            : null;
        return new AttemptOutcome(LlmReply.Success(text, usage));
    }

    /// <summary>概率查询（本提供商独有，供"多选一"判定；§3.5）。</summary>
    public async Task<IReadOnlyDictionary<string, double>> GetOptionProbabilitiesAsync(
        LlmRequest request,
        IReadOnlyList<string> options,
        CancellationToken ct)
    {
        NetworkAvailability.ThrowIfUnavailable();
        var body = new JObject
        {
            ["prompt"] = FormatPrompt(request),
            ["n_predict"] = request.MaxTokens,
            ["stream"] = false,
            ["temperature"] = 0.8,
            ["top_p"] = 0.88,
            ["min_p"] = 0.05,
            ["cache_prompt"] = true,
            ["n_probs"] = 10
        };

        string responseBody = await LlmHttp.SendAsync(_endpoint, body.ToString(), null, null, request.TimeoutOverride, ct).ConfigureAwait(false);
        var json = JObject.Parse(responseBody);
        var positions = new List<IReadOnlyDictionary<string, double>>();
        foreach (JToken position in json["completion_probabilities"] as JArray ?? new JArray())
        {
            var tokenProbabilities = new Dictionary<string, double>();
            foreach (JToken prob in position["probs"] as JArray ?? new JArray())
            {
                string? token = prob["tok_str"]?.ToString();
                if (token != null && !tokenProbabilities.ContainsKey(token))
                {
                    tokenProbabilities[token] = prob["prob"]?.Value<double>() ?? 0;
                }
            }

            positions.Add(tokenProbabilities);
        }

        return TokenProbabilityMath.AggregateOptions(positions, options);
    }

    /// <summary>PromptFormat 模板按占位符字面替换（唯一消费 responseStart 与 PromptFormat 的提供商）。</summary>
    internal string FormatPrompt(LlmRequest request)
    {
        return _promptFormat
            .Replace("{system}", request.SystemPrompt)
            .Replace("{prompt}", request.ConcatenatedUserContent())
            .Replace("{response_start}", request.ResponseStart);
    }

    internal JObject BuildBody(LlmRequest request)
    {
        return new JObject
        {
            ["prompt"] = FormatPrompt(request),
            ["n_predict"] = request.MaxTokens,
            ["stream"] = false,
            // 让服务端保留上次 KV cache，相同前缀免重算——老版本 llama.cpp 默认关闭，必须显式发。
            ["cache_prompt"] = true,
            ["temperature"] = request.MaxTokens == 1 ? 0 : 1.5,
            ["top_p"] = 0.88,
            ["min_p"] = 0.05,
            ["repeat_penalty"] = 1.05
        };
    }
}
