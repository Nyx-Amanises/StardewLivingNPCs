using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// Google Gemini（Provider 值 "Google"，WP11 §3.3）。认证走 URL query 参数；
/// 不拆缓存段，依赖 Gemini 2.5 系隐式前缀缓存。无流式实现（裁决 4：不补，保持行为面等价）。
/// </summary>
internal sealed class GeminiClient : LlmClientBase, IModelNameSource
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public GeminiClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "Google";

    protected override string DefaultModelName => "gemini-2.5-flash";

    protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
    {
        string level = LlmThinking.ForCall(fastPass: request.DisableThinking);
        string url = $"{BaseUrl}/models/{EffectiveModelName}:generateContent?key={Uri.EscapeDataString(ApiKey)}";
        JObject? thinkingConfig = LlmThinking.BuildGeminiThinkingConfig(level, EffectiveModelName);

        JObject bare = BuildBody(request, level);
        if (thinkingConfig == null)
        {
            return new[] { new RequestCandidate(url, bare.ToString()) };
        }

        // 候选序列：先发带 thinkingConfig 的版本，随后发裸版本兜底；各自独享完整重试预算（§3.3/§4.2）。
        var withThinking = (JObject)bare.DeepClone();
        withThinking["generationConfig"]!["thinkingConfig"] = thinkingConfig;
        return new[]
        {
            new RequestCandidate(url, withThinking.ToString())
            {
                HasThinkingParameters = true,
                ThinkingLevel = level,
                ThinkingDescription = LlmThinking.DescribeThinkingParameters(withThinking)
            },
            new RequestCandidate(url, bare.ToString())
        };
    }

    protected override async Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
    {
        string body = await LlmHttp.SendAsync(candidate.Url, candidate.Json, null, null, request.TimeoutOverride, ct).ConfigureAwait(false);
        var json = JObject.Parse(body);
        JToken? firstCandidate = (json["candidates"] as JArray)?.FirstOrDefault();

        // 截断/安全拦截一律进重试：必须 finishReason == "STOP" 才接受。
        string? finishReason = firstCandidate?["finishReason"]?.ToString();
        if (!string.Equals(finishReason, "STOP", StringComparison.Ordinal))
        {
            return new AttemptOutcome(LlmReply.Failure($"finishReason={finishReason ?? "(none)"}: {body}", 200));
        }

        string? text = (firstCandidate?["content"]?["parts"] as JArray)?.FirstOrDefault()?["text"]?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            // HTTP 200 但文本为空白：返回错误响应，不再重试该候选（§3.3）。
            return new AttemptOutcome(LlmReply.Failure("Empty response", 200), abortCandidate: true);
        }

        TokenUsage? usage = json["usageMetadata"] is JObject usageJson && usageJson.HasValues
            ? TokenUsage.FromGeminiUsage(usageJson)
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

            string body = await LlmHttp.SendAsync($"{BaseUrl}/models?key={Uri.EscapeDataString(ApiKey)}", null, null, null, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            var json = JObject.Parse(body);
            return (json["models"] as JArray)?
                .Select(item => item["name"]?.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name)
                .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        });
    }

    internal JObject BuildBody(LlmRequest request, string level)
    {
        var generationConfig = new JObject
        {
            ["maxOutputTokens"] = request.MaxTokens,
            ["temperature"] = 1.5,
            ["topP"] = 0.9
        };

        // 快速 JSON 通道 + 档位 off：强制 JSON MIME（与 OpenAI 家族 response_format 的门控口径一致）。
        if (request.DisableThinking && LlmThinking.IsOff(level))
        {
            generationConfig["responseMimeType"] = "application/json";
        }

        return new JObject
        {
            // 有意放开露骨类（恋爱/配偶对话常被误杀）；骚扰类保中档（§3.3，逐字保留）。
            ["safetySettings"] = new JArray
            {
                new JObject
                {
                    ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                    ["threshold"] = "BLOCK_NONE"
                },
                new JObject
                {
                    ["category"] = "HARM_CATEGORY_HARASSMENT",
                    ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE"
                }
            },
            ["system_instruction"] = new JObject
            {
                ["parts"] = new JArray { new JObject { ["text"] = request.SystemPrompt } }
            },
            ["contents"] = new JArray
            {
                new JObject
                {
                    ["parts"] = new JArray { new JObject { ["text"] = request.ConcatenatedUserContent() } }
                }
            },
            ["generationConfig"] = generationConfig
        };
    }
}
