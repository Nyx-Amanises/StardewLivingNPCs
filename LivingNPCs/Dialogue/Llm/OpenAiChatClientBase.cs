using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// OpenAI 兼容家族共用基类（WP11 §3.1）：OpenAI / DeepSeek / Mistral / 自定义兼容端点。
/// 差异只在基地址、默认模型与三个能力开关（缓存键、流式 usage、instructions 回退）。
/// </summary>
internal abstract class OpenAiChatClientBase : LlmClientBase, IModelNameSource
{
    protected OpenAiChatClientBase(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    /// <summary>基地址（无尾部斜杠）；端点为 {基地址}/v1/chat/completions。</summary>
    protected abstract string BaseAddress { get; }

    /// <summary>仅 OpenAI 官方端点按 prompt_cache_key 路由缓存分片。</summary>
    protected virtual bool SendPromptCacheKey => false;

    /// <summary>OpenAI/DeepSeek 流式请求带 stream_options.include_usage 换真实 usage 回传。</summary>
    protected virtual bool SendStreamUsageOptions => false;

    /// <summary>仅兼容端点：标准形态耗尽后追加 instructions 回退形态（部分网关不认 system 角色）。</summary>
    protected virtual bool SupportsInstructionsFallback => false;

    /// <summary>官方端点 ApiKey 为空时模型列表直接返回空、不发请求；兼容端点无条件发（可能无鉴权）。</summary>
    protected virtual bool RequireApiKeyForModelList => true;

    protected string ChatEndpoint => BaseAddress + "/v1/chat/completions";

    protected string ModelsEndpoint => BaseAddress + "/v1/models";

    protected string? AuthTokenOrNull => string.IsNullOrEmpty(ApiKey) ? null : ApiKey;

    protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
    {
        string level = LlmThinking.ForCall(fastPass: request.DisableThinking);
        var candidates = new List<RequestCandidate>();
        AppendCandidatePair(candidates, request, level, instructionsForm: false);
        if (SupportsInstructionsFallback)
        {
            AppendCandidatePair(candidates, request, level, instructionsForm: true);
        }

        return candidates;
    }

    protected override async Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
    {
        string body = await LlmHttp.SendAsync(candidate.Url, candidate.Json, AuthTokenOrNull, null, request.TimeoutOverride, ct).ConfigureAwait(false);
        (string? text, TokenUsage? usage) = ExtractTextAndUsage(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            // 全文为空白视为本次尝试失败；原始响应文本作为错误消息。
            return new AttemptOutcome(LlmReply.Failure(body, 200));
        }

        return new AttemptOutcome(LlmReply.Success(text, usage));
    }

    public override async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            NetworkAvailability.ThrowIfUnavailable();
        }
        catch (InvalidOperationException ex)
        {
            throw new LlmStreamException(ex.Message, 500);
        }

        // 流式思考档位按"非快速通道"取值，且没有思考参数回退序列（§3.1）。
        string level = LlmThinking.ForCall(fastPass: false);
        JObject body = BuildBody(request, level, instructionsForm: false);
        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, EffectiveModelName, level);
        body["stream"] = true;
        if (SendStreamUsageOptions)
        {
            body["stream_options"] = new JObject { ["include_usage"] = true };
        }

        string json = body.ToString();
        int budget = request.AllowRetry ? 3 : 1;
        bool gotAnyDelta = false;
        string lastRaw = string.Empty;
        string lastError = "Streaming request failed";
        int lastStatus = 500;
        TokenUsage? streamUsage = null;
        var fullText = new StringBuilder();

        for (int attempt = 0; attempt < budget && !gotAnyDelta; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
            }

            var context = new SseAttemptContext();
            IAsyncEnumerator<string> deltas = ReadSseDeltasAsync(json, context, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    bool moved;
                    string? delta = null;
                    try
                    {
                        moved = await deltas.MoveNextAsync().ConfigureAwait(false);
                        if (moved)
                        {
                            delta = deltas.Current;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        lastStatus = ExtractHttpStatus(ex);
                        break;
                    }

                    if (!moved || delta == null)
                    {
                        break;
                    }

                    gotAnyDelta = true;
                    fullText.Append(delta);
                    yield return LlmStreamEvent.Delta(delta);
                }
            }
            finally
            {
                await deltas.DisposeAsync().ConfigureAwait(false);
            }

            lastRaw = context.RawText.ToString();
            streamUsage = context.Usage ?? streamUsage;
        }

        // 流结束后若拿到过增量则成功返回；usage 优先流内真实值，否则 CJK 感知估算。
        if (gotAnyDelta)
        {
            TokenUsage usage = streamUsage ?? TokenUsage.Estimate(
                request.SystemPrompt + request.ConcatenatedUserContent(),
                fullText.ToString(),
                "stream estimate");
            yield return LlmStreamEvent.ForUsage(usage);
            yield return LlmStreamEvent.Done();
            yield break;
        }

        // 降级 ①：把累计原始响应按非流式格式再解析（有的端点无视 stream 参数直接回完整 JSON）。
        ct.ThrowIfCancellationRequested();
        (string? recoveredText, TokenUsage? _) = TryExtractTextAndUsage(lastRaw);
        if (!string.IsNullOrWhiteSpace(recoveredText))
        {
            yield return LlmStreamEvent.Delta(recoveredText);
            yield return LlmStreamEvent.ForUsage(TokenUsage.Estimate(
                request.SystemPrompt + request.ConcatenatedUserContent(),
                recoveredText,
                "stream fallback estimate"));
            yield return LlmStreamEvent.Done();
            yield break;
        }

        // 降级 ②：非流式通道兜底（禁止重试，外层已有预算）。
        ct.ThrowIfCancellationRequested();
        LlmReply fallback = await CompleteAsync(request.CloneWithoutRetry(), ct).ConfigureAwait(false);
        if (fallback.IsSuccess)
        {
            yield return LlmStreamEvent.Delta(fallback.Text);
            yield return LlmStreamEvent.ForUsage(fallback.Usage);
            yield return LlmStreamEvent.Done();
            yield break;
        }

        // 降级 ③：全部失败，最后一次原始文本 + 状态码。
        string finalError = !string.IsNullOrWhiteSpace(fallback.ErrorMessage)
            ? fallback.ErrorMessage
            : (!string.IsNullOrWhiteSpace(lastRaw) ? lastRaw : lastError);
        throw new LlmStreamException(finalError, fallback.HttpStatus != 0 ? fallback.HttpStatus : lastStatus);
    }

    public IReadOnlyList<string> GetModelNames()
    {
        return ListModelNamesSafely(FetchModelNamesAsync);
    }

    /// <summary>兼容端点地址规整：依次剥掉尾部 /、尾部 /chat/completions、尾部 /v1（§3.1）。</summary>
    internal static string NormalizeBaseAddress(string? serverAddress)
    {
        string address = (serverAddress ?? string.Empty).Trim();
        if (address.EndsWith("/", StringComparison.Ordinal))
        {
            address = address[..^1];
        }

        if (address.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            address = address[..^"/chat/completions".Length];
        }

        if (address.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            address = address[..^"/v1".Length];
        }

        return address;
    }

    /// <summary>
    /// OpenAI 官方缓存键：对"系统段 U+0001 稳定世界段 U+0001 NPC 段"求 SHA-256，
    /// 取前 8 字节大写十六进制，拼 livingnpcs-{16位hex}（前缀已按裁决 5 改名；哈希输入与分隔符不变）。
    /// </summary>
    internal static string ComputePromptCacheKey(LlmRequest request)
    {
        string material = request.SystemPrompt + "\u0001" + request.StableContext + "\u0001" + request.NpcContext;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var hex = new StringBuilder(16);
        for (int i = 0; i < 8; i++)
        {
            hex.Append(hash[i].ToString("X2"));
        }

        return $"livingnpcs-{hex}";
    }

    /// <summary>非流式响应解析：标准 JSON、SSE 误回、旧式 completions text 三种形态（§3.1）。</summary>
    internal static (string? Text, TokenUsage? Usage) ExtractTextAndUsage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        if (body.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return ReassembleSseBody(body);
        }

        try
        {
            var json = JObject.Parse(body);
            JToken? firstChoice = (json["choices"] as JArray)?.FirstOrDefault();
            string? text = firstChoice?["message"]?["content"]?.ToString()
                ?? firstChoice?["text"]?.ToString()
                ?? json["text"]?.ToString();
            TokenUsage? usage = json["usage"] is JObject usageJson && usageJson.HasValues
                ? TokenUsage.FromOpenAiUsage(usageJson)
                : null;
            return (text, usage);
        }
        catch (JsonException)
        {
            // 无 data: 前缀的 SSE / 混杂输出：按逐行裸 JSON 再试一次。
            return ReassembleSseBody(body);
        }
    }

    protected JObject BuildBody(LlmRequest request, string level, bool instructionsForm)
    {
        var messages = new JArray();
        if (!instructionsForm)
        {
            messages.Add(new JObject { ["role"] = "system", ["content"] = request.SystemPrompt });
        }

        messages.Add(new JObject { ["role"] = "user", ["content"] = request.ConcatenatedUserContent() });

        var body = new JObject
        {
            ["model"] = EffectiveModelName,
            // gpt-5/o 系推理模型拒绝 max_tokens（400），须发 max_completion_tokens；其余模型保持 max_tokens。
            [LlmThinking.OpenAiMaxTokensFieldName(EffectiveModelName)] = request.MaxTokens,
            ["messages"] = messages
        };
        // 不发送 temperature/top_p：沿用各端点默认采样（现状行为，保留）。

        if (instructionsForm)
        {
            body["instructions"] = request.SystemPrompt;
        }

        if (SendPromptCacheKey)
        {
            body["prompt_cache_key"] = ComputePromptCacheKey(request);
        }

        // 快速 JSON 通道 + 档位 off：response_format 防弱模型把 JSON 包散文里。
        // 只在 DisableThinking 路径生效（与 §3.4 VolcEngine 语义对齐），避免用户把聊天档位设 Off 时污染正常对话。
        if (request.DisableThinking && LlmThinking.IsOff(level))
        {
            body["response_format"] = new JObject { ["type"] = "json_object" };
        }

        return body;
    }

    protected virtual async Task<IReadOnlyList<string>> FetchModelNamesAsync(CancellationToken ct)
    {
        if (RequireApiKeyForModelList && string.IsNullOrWhiteSpace(ApiKey))
        {
            return Array.Empty<string>();
        }

        string body = await LlmHttp.SendAsync(ModelsEndpoint, null, AuthTokenOrNull, null, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
        var json = JObject.Parse(body);
        return (json["data"] as JArray)?
            .Select(item => item["id"]?.ToString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private void AppendCandidatePair(List<RequestCandidate> candidates, LlmRequest request, string level, bool instructionsForm)
    {
        JObject bare = BuildBody(request, level, instructionsForm);
        var withThinking = (JObject)bare.DeepClone();
        LlmThinking.AddOpenAiCompatibleThinkingParameters(withThinking, EffectiveModelName, level);

        if (!JToken.DeepEquals(bare, withThinking))
        {
            candidates.Add(new RequestCandidate(ChatEndpoint, withThinking.ToString())
            {
                HasThinkingParameters = true,
                ThinkingLevel = level,
                ThinkingDescription = LlmThinking.DescribeThinkingParameters(withThinking)
            });
        }

        candidates.Add(new RequestCandidate(ChatEndpoint, bare.ToString()));
    }

    private static (string? Text, TokenUsage? Usage) TryExtractTextAndUsage(string body)
    {
        try
        {
            return ExtractTextAndUsage(body);
        }
        catch
        {
            return (null, null);
        }
    }

    private static (string? Text, TokenUsage? Usage) ReassembleSseBody(string body)
    {
        var text = new StringBuilder();
        TokenUsage? usage = null;
        foreach (string rawLine in body.Split('\n'))
        {
            (string? delta, TokenUsage? chunkUsage) = ParseSseLine(rawLine);
            if (delta != null)
            {
                text.Append(delta);
            }

            usage = chunkUsage ?? usage;
        }

        return (text.ToString(), usage);
    }

    /// <summary>单行 SSE 解析：data: 前缀（大小写不敏感）后为 JSON 载荷；无前缀按裸 JSON 试；[DONE] 与非 JSON 行忽略。</summary>
    private static (string? Delta, TokenUsage? Usage) ParseSseLine(string rawLine)
    {
        string line = rawLine.Trim();
        if (line.Length == 0)
        {
            return (null, null);
        }

        string payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? line[5..].Trim() : line;
        if (payload.Length == 0 || payload == "[DONE]" || payload[0] != '{')
        {
            return (null, null);
        }

        try
        {
            var chunk = JObject.Parse(payload);
            TokenUsage? usage = chunk["usage"] is JObject usageJson && usageJson.HasValues
                ? TokenUsage.FromOpenAiUsage(usageJson)
                : null;
            string? delta = (chunk["choices"] as JArray)?.FirstOrDefault()?["delta"]?["content"]?.ToString();
            return (string.IsNullOrEmpty(delta) ? null : delta, usage);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private async IAsyncEnumerable<string> ReadSseDeltasAsync(string json, SseAttemptContext context, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using HttpResponseMessage response = await LlmHttp.SendForStreamAsync(ChatEndpoint, json, AuthTokenOrNull, null, null, ct).ConfigureAwait(false);
        using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        // net6 的 ReadLineAsync 不收令牌：取消时直接掐断响应，把随之而来的 IO 异常翻译回取消。
        using CancellationTokenRegistration registration = ct.Register(static state => ((HttpResponseMessage)state!).Dispose(), response);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException("Request was cancelled", ct);
            }

            if (line == null)
            {
                yield break;
            }

            context.RawText.AppendLine(line);
            (string? delta, TokenUsage? usage) = ParseSseLine(line);
            if (usage != null)
            {
                context.Usage = usage;
            }

            string trimmed = line.Trim();
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && trimmed[5..].Trim() == "[DONE]")
            {
                yield break;
            }

            if (delta != null)
            {
                yield return delta;
            }
        }
    }

    private sealed class SseAttemptContext
    {
        public StringBuilder RawText { get; } = new();

        public TokenUsage? Usage { get; set; }
    }
}
