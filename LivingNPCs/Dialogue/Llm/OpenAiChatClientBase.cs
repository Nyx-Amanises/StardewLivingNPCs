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
using StardewModdingAPI;

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

    /// <summary>Buffered transports can require a provider completion marker before accepting the reply.</summary>
    protected virtual bool RequireStreamCompletionMarker => false;

    /// <summary>仅兼容端点：标准形态耗尽后追加 instructions 回退形态（部分网关不认 system 角色）。</summary>
    protected virtual bool SupportsInstructionsFallback => false;

    /// <summary>官方端点 ApiKey 为空时模型列表直接返回空、不发请求；兼容端点无条件发（可能无鉴权）。</summary>
    protected virtual bool RequireApiKeyForModelList => true;

    protected virtual string ChatEndpoint => BaseAddress + "/v1/chat/completions";

    protected virtual string ModelsEndpoint => BaseAddress + "/v1/models";

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
            ResponseDiagnostics diagnostics = InspectResponse(body);
            // A finished generation with no answer is not a transport failure. Repeating it through
            // bare/instructions candidates wastes tokens and may restore the provider's default thinking.
            return new AttemptOutcome(
                LlmReply.Failure(diagnostics.DescribeEmptyResponse(request.MaxTokens), 200,
                    retryable: !diagnostics.IsTerminal, usage: usage));
        }

        return new AttemptOutcome(LlmReply.Success(text, usage));
    }

    protected override bool ShouldAbortCandidate(Exception exception)
    {
        // Retrying an identical rejected body cannot repair it; preserve retries for transient errors.
        return ExtractHttpStatus(exception) is 400 or 422;
    }

    protected override bool IsExceptionRetryable(Exception exception)
    {
        return ExtractHttpStatus(exception) is not (401 or 403);
    }

    protected override bool CanChangeRequestShapeAfter(Exception exception)
    {
        if (ExtractHttpStatus(exception) is 401 or 403 or 429 or 502 or 503 or 504)
        {
            return false;
        }

        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is TimeoutException or IOException or System.Net.Sockets.SocketException
                || current is HttpRequestException { StatusCode: null })
            {
                return false;
            }
        }

        // Keep HTTP 400/422 body compatibility and historical HTTP 500 gateway fallbacks.
        return true;
    }

    public override async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        bool requireCompletionMarker = RequireStreamCompletionMarker;
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
        bool sawCompletionMarker = false;
        bool midStreamFailure = false;
        bool allowFormatFallback = true;
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
            allowFormatFallback = true;
            bool attemptFailed = false;
            bool abortStreamRetries = false;
            IAsyncEnumerator<string> deltas = ReadSseDeltasAsync(json, context, request.TimeoutOverride, ct).GetAsyncEnumerator(ct);
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
                        streamUsage = context.Usage ?? streamUsage;
                        lastError = ex.Message;
                        lastStatus = ExtractHttpStatus(ex);
                        attemptFailed = true;
                        abortStreamRetries = ShouldAbortCandidate(ex);
                        allowFormatFallback = CanChangeRequestShapeAfter(ex);
                        if (!IsExceptionRetryable(ex))
                        {
                            throw new LlmStreamException(lastError, lastStatus, retryable: false, usage: streamUsage);
                        }
                        // 已向消费方交付过增量后才异常 = 中途断连；尚无增量的失败仍走整轮重试。
                        midStreamFailure = gotAnyDelta;
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
            sawCompletionMarker = context.HasCompletionMarker;

            if (!gotAnyDelta && !attemptFailed)
            {
                ct.ThrowIfCancellationRequested();
                // Some compatible gateways ignore stream and return a complete JSON response.
                // Consume that response immediately instead of paying for the same generation three times.
                (string? recoveredText, TokenUsage? recoveredUsage) = TryExtractTextAndUsage(lastRaw);
                if (!string.IsNullOrWhiteSpace(recoveredText))
                {
                    yield return LlmStreamEvent.Delta(recoveredText);
                    yield return LlmStreamEvent.ForUsage(recoveredUsage ?? TokenUsage.Estimate(
                        request.SystemPrompt + request.ConcatenatedUserContent(),
                        recoveredText,
                        "stream fallback estimate"));
                    yield return LlmStreamEvent.Done();
                    yield break;
                }

                ResponseDiagnostics diagnostics = InspectResponse(lastRaw);
                if (diagnostics.IsTerminal)
                {
                    throw new LlmStreamException(diagnostics.DescribeEmptyResponse(request.MaxTokens), 200,
                        retryable: false, usage: diagnostics.Usage);
                }
            }

            if (abortStreamRetries)
            {
                break;
            }
        }

        ct.ThrowIfCancellationRequested();

        // 中途断连（增量已交付后传输异常）：已 yield 的文本无法撤回，本层也不能重试
        // （重试会把新一轮增量拼在旧残句后面）。记 Warn 日志并以流式异常上抛，
        // 让调用方丢弃半截文本按失败路径处理；熔断装饰器经异常路径记失败而非成功。
        if (midStreamFailure)
        {
            DialogueServices.Monitor?.Log(
                $"[{ProviderId}] Streaming connection lost mid-reply after {fullText.Length} chars were already delivered; the partial text is discarded and the caller retries. Error: {lastError}",
                LogLevel.Warn);
            throw new LlmStreamException($"Streaming connection lost mid-reply: {lastError}", lastStatus, usage: streamUsage);
        }

        // 流自然收尾（读到 [DONE] 或干净 EOF）且拿到过增量 → 成功；usage 优先流内真实值，否则 CJK 感知估算。
        if (gotAnyDelta)
        {
            string completedText = fullText.ToString();
            TokenUsage usage = streamUsage ?? TokenUsage.Estimate(
                request.SystemPrompt + request.ConcatenatedUserContent(),
                completedText,
                "stream estimate");
            if (requireCompletionMarker && !sawCompletionMarker)
            {
                throw new LlmStreamException(
                    "Streaming response ended before a completion marker; partial reply discarded", 500,
                    retryable: true, usage: usage);
            }

            if (string.IsNullOrWhiteSpace(completedText))
            {
                ResponseDiagnostics diagnostics = InspectResponse(lastRaw);
                throw new LlmStreamException(diagnostics.DescribeEmptyResponse(request.MaxTokens), 200,
                    retryable: !diagnostics.IsTerminal, usage: usage);
            }

            yield return LlmStreamEvent.ForUsage(usage);
            yield return LlmStreamEvent.Done();
            yield break;
        }

        // Changing stream/message formats cannot fix authentication, rate limiting, an unavailable
        // gateway, or a lost connection. Transient failures retain only the same-format retry budget.
        if (!allowFormatFallback)
        {
            throw new LlmStreamException(lastError, lastStatus, usage: streamUsage);
        }

        // 降级 ②：非流式通道兜底（禁止重试，外层已有预算）。
        // Call the non-streaming implementation directly: a compatible CompleteAsync override
        // may itself buffer this stream, so a virtual call here would recurse into another stream.
        ct.ThrowIfCancellationRequested();
        LlmReply fallback = await base.CompleteAsync(request.CloneWithoutRetry(), ct).ConfigureAwait(false);
        if (fallback.IsSuccess)
        {
            yield return LlmStreamEvent.Delta(fallback.Text);
            yield return LlmStreamEvent.ForUsage(fallback.Usage);
            yield return LlmStreamEvent.Done();
            yield break;
        }

        // 降级 ③：全部失败，传递诊断、重试资格与已消耗的用量。
        string finalError = !string.IsNullOrWhiteSpace(fallback.ErrorMessage)
            ? fallback.ErrorMessage
            : LlmThinking.SummarizeProviderError(lastError);
        throw new LlmStreamException(finalError, fallback.HttpStatus != 0 ? fallback.HttpStatus : lastStatus,
            fallback.Retryable, fallback.Usage);
    }

    public IReadOnlyList<string> GetModelNames()
    {
        return ListModelNamesSafely(FetchModelNamesAsync);
    }

    /// <summary>
    /// 完整端点直用判定（审查发现 4）：地址以 /chat/completions 结尾但其前缀不以 /v1 结尾
    /// （Cloudflare AI Gateway 的 /compat/chat/completions 等网关路径），返回去掉尾部斜杠后的
    /// 完整端点原样——此时"剥 /chat/completions 再拼 /v1/chat/completions"会凭空插入用户从未
    /// 配置的 /v1 段导致 404。主流 /v1 形态与裸基地址返回 null，仍走 NormalizeBaseAddress 拼接。
    /// </summary>
    internal static string? DetectVerbatimChatEndpoint(string? serverAddress)
    {
        string address = (serverAddress ?? string.Empty).Trim();
        if (address.EndsWith("/", StringComparison.Ordinal))
        {
            address = address[..^1];
        }

        if (!address.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string prefix = address[..^"/chat/completions".Length];
        return prefix.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? null : address;
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
            // choices[0] / message 可能被兼容端点写成字面量 null（JValue）：对非容器节点再取子值会抛
            // InvalidOperationException（与 TokenUsage 的 details 字段同型缺陷），一律先判型 JObject。
            var firstChoice = (json["choices"] as JArray)?.FirstOrDefault() as JObject;
            string? text = (firstChoice?["message"] as JObject)?["content"]?.ToString()
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

        // Omitting DeepSeek's disabled switch turns thinking back on (default effort: high).
        // Keep the switch in instructions fallbacks too; never silently undo an explicit Off request.
        if (!LlmThinking.IsDeepSeekThinkingModel(EffectiveModelName) || !LlmThinking.IsOff(level))
        {
            candidates.Add(new RequestCandidate(ChatEndpoint, bare.ToString())
            {
                RequiresRequestRejection = !JToken.DeepEquals(bare, withThinking)
            });
        }
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

    private static ResponseDiagnostics InspectResponse(string body)
    {
        var diagnostics = new ResponseDiagnostics();
        if (string.IsNullOrWhiteSpace(body))
        {
            return diagnostics;
        }

        try
        {
            diagnostics.Read(JObject.Parse(body));
        }
        catch (JsonException)
        {
            foreach (string rawLine in body.Split('\n'))
            {
                string line = rawLine.Trim();
                string payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? line[5..].Trim() : line;
                if (!payload.StartsWith("{", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    diagnostics.Read(JObject.Parse(payload));
                }
                catch (JsonException)
                {
                }
            }
        }

        return diagnostics;
    }

    private sealed class ResponseDiagnostics
    {
        private string? _finishReason;
        private bool _hasReasoning;
        private bool _hasRefusal;
        private TokenUsage? _usage;

        public TokenUsage? Usage => _usage;

        public bool IsTerminal => !string.IsNullOrWhiteSpace(_finishReason)
            || _hasReasoning || _hasRefusal || (_usage?.ReasoningTokens ?? 0) > 0;

        public void Read(JObject json)
        {
            var choice = (json["choices"] as JArray)?.FirstOrDefault() as JObject;
            string? finishReason = choice?["finish_reason"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(finishReason))
            {
                _finishReason = finishReason.Length <= 40 ? finishReason : finishReason[..40];
            }

            var message = choice?["message"] as JObject ?? choice?["delta"] as JObject;
            _hasReasoning |= !string.IsNullOrWhiteSpace(message?["reasoning_content"]?.ToString());
            _hasRefusal |= !string.IsNullOrWhiteSpace(message?["refusal"]?.ToString());
            if (json["usage"] is JObject usage && usage.HasValues)
            {
                _usage = TokenUsage.FromOpenAiUsage(usage);
            }
        }

        public string DescribeEmptyResponse(int maxTokens)
        {
            string explanation = string.Equals(_finishReason, "length", StringComparison.OrdinalIgnoreCase)
                ? "The model exhausted its output budget before returning visible text"
                : "The provider returned no visible text";
            // Never include message content or reasoning_content in error logs.
            return $"{explanation} (finish_reason={_finishReason ?? "(none)"}; "
                + $"completion_tokens={_usage?.CompletionTokens ?? 0}; reasoning_tokens={_usage?.ReasoningTokens ?? 0}; "
                + $"reasoning_present={_hasReasoning}; max_tokens={maxTokens}).";
        }
    }

    private static (string? Text, TokenUsage? Usage) ReassembleSseBody(string body)
    {
        var text = new StringBuilder();
        TokenUsage? usage = null;
        foreach (string rawLine in body.Split('\n'))
        {
            var (delta, chunkUsage, _) = ParseSseLine(rawLine);
            if (delta != null)
            {
                text.Append(delta);
            }

            usage = chunkUsage ?? usage;
        }

        return (text.ToString(), usage);
    }

    /// <summary>单行 SSE 解析：data: 前缀后为 JSON；真实 [DONE] 或非空 finish_reason 标记完成，不把推理内容当成结束。</summary>
    private static (string? Delta, TokenUsage? Usage, bool HasCompletionMarker) ParseSseLine(string rawLine)
    {
        string line = rawLine.Trim();
        if (line.Length == 0)
        {
            return (null, null, false);
        }

        bool hasDataPrefix = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        string payload = hasDataPrefix ? line[5..].Trim() : line;
        if (payload == "[DONE]")
        {
            return (null, null, hasDataPrefix);
        }

        if (payload.Length == 0 || payload[0] != '{')
        {
            return (null, null, false);
        }

        try
        {
            var chunk = JObject.Parse(payload);
            TokenUsage? usage = chunk["usage"] is JObject usageJson && usageJson.HasValues
                ? TokenUsage.FromOpenAiUsage(usageJson)
                : null;
            // choices[0] / delta 为字面量 null 时不得对 JValue 取子值（会抛非 JsonException 的异常打断流）。
            var choice = (chunk["choices"] as JArray)?.FirstOrDefault() as JObject;
            var deltaObject = choice?["delta"] as JObject;
            string? delta = deltaObject?["content"]?.ToString();
            bool hasCompletionMarker = choice?["finish_reason"] is JValue { Type: JTokenType.String } finishReason
                && !string.IsNullOrWhiteSpace(finishReason.Value<string>());
            return (string.IsNullOrEmpty(delta) ? null : delta, usage, hasCompletionMarker);
        }
        catch (JsonException)
        {
            return (null, null, false);
        }
    }

    private async IAsyncEnumerable<string> ReadSseDeltasAsync(
        string json,
        SseAttemptContext context,
        TimeSpan? headerTimeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using HttpResponseMessage response = await LlmHttp.SendForStreamAsync(ChatEndpoint, json, AuthTokenOrNull, null, headerTimeout, ct).ConfigureAwait(false);
        using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        // net6 的 ReadLineAsync 不收令牌：取消时直接掐断响应，把随之而来的 IO 异常翻译回取消。
        using CancellationTokenRegistration registration = ct.Register(static state => ((HttpResponseMessage)state!).Dispose(), response);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
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
            var (delta, usage, hasCompletionMarker) = ParseSseLine(line);
            context.HasCompletionMarker |= hasCompletionMarker;
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

        public bool HasCompletionMarker { get; set; }
    }
}
