using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 全部真实提供商的公共骨架（WP11 §4.1/§4.2）：Android 入口检查、请求体候选序列、
/// 重试预算（允许重试 ? 3 : 1，失败间隔约 100ms）、异常链状态码提取、失败不抛异常。
/// 无流式能力的提供商继承默认 StreamAsync：内部调 CompleteAsync，全文作为单个增量 yield。
/// </summary>
internal abstract class LlmClientBase : ILlmClient, ILlmCapabilities
{
    /// <summary>测试用重试间隔覆盖（避免真实等待）；null 走各提供商默认。</summary>
    internal static TimeSpan? RetryDelayOverrideForTests;

    protected LlmClientBase(LlmConnectionSettings settings)
    {
        Settings = settings;
        ApiKey = settings.ApiKey ?? string.Empty;
        EffectiveModelName = string.IsNullOrWhiteSpace(settings.ModelName) ? DefaultModelName : settings.ModelName.Trim();
    }

    public abstract string ProviderId { get; }

    public virtual bool IsHighlySensoredModel => false;

    public virtual string ExtraInstructions => string.Empty;

    protected abstract string DefaultModelName { get; }

    protected LlmConnectionSettings Settings { get; }

    protected string ApiKey { get; }

    /// <summary>配置模型名（空则取提供商默认），组请求体与诊断都用它。</summary>
    public string EffectiveModelName { get; }

    protected virtual TimeSpan RetryDelay => RetryDelayOverrideForTests ?? TimeSpan.FromMilliseconds(100);

    public virtual async Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        try
        {
            NetworkAvailability.ThrowIfUnavailable();
        }
        catch (InvalidOperationException ex)
        {
            return LlmReply.Failure(ex.Message, 500);
        }

        IReadOnlyList<RequestCandidate> candidates = BuildRequestCandidates(request);
        int budget = request.AllowRetry ? 3 : 1;
        string lastError = "Request failed";
        int lastStatus = 500;
        bool thinkingFallbackWarned = false;

        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            RequestCandidate candidate = candidates[candidateIndex];
            bool abortCandidate = false;

            for (int attempt = 0; attempt < budget && !abortCandidate; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt > 0)
                {
                    await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
                }

                try
                {
                    AttemptOutcome outcome = await ExecuteCandidateAsync(candidate, request, ct).ConfigureAwait(false);
                    if (outcome.Reply.IsSuccess)
                    {
                        return outcome.Reply;
                    }

                    lastError = outcome.Reply.ErrorMessage;
                    lastStatus = outcome.Reply.HttpStatus;
                    abortCandidate = outcome.AbortCandidate;
                    LogAttemptFailure(candidateIndex, attempt, lastError);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    lastStatus = ExtractHttpStatus(ex);
                    LogAttemptFailure(candidateIndex, attempt, lastError);
                }
            }

            if (candidate.HasThinkingParameters && candidateIndex < candidates.Count - 1 && !thinkingFallbackWarned)
            {
                LlmThinking.LogThinkingFallbackWarning(EffectiveModelName, candidate.ThinkingLevel, candidate.ThinkingDescription, lastError);
                thinkingFallbackWarned = true;
            }
        }

        return LlmReply.Failure(lastError, lastStatus);
    }

    public virtual async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        LlmReply reply = await CompleteAsync(request, ct).ConfigureAwait(false);
        if (!reply.IsSuccess)
        {
            throw new LlmStreamException(reply.ErrorMessage, reply.HttpStatus);
        }

        if (!string.IsNullOrEmpty(reply.Text))
        {
            yield return LlmStreamEvent.Delta(reply.Text);
        }

        yield return LlmStreamEvent.ForUsage(reply.Usage);
        yield return LlmStreamEvent.Done();
    }

    /// <summary>每个候选独享完整重试预算，前一个候选耗尽才换下一个（§4.2）。</summary>
    protected abstract IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request);

    protected abstract Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct);

    /// <summary>从异常链提取 HTTP 状态码：HttpRequestException（本体或内层）的 StatusCode；取不到默认 500。</summary>
    protected internal static int ExtractHttpStatus(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: not null } httpException)
            {
                return (int)httpException.StatusCode.Value;
            }
        }

        return 500;
    }

    /// <summary>模型列表等一次性同步调用的公共外壳：任何异常记错误日志并返回空列表。</summary>
    protected IReadOnlyList<string> ListModelNamesSafely(Func<CancellationToken, Task<IReadOnlyList<string>>> fetch)
    {
        try
        {
            return fetch(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.listModelsFailed",
                    new { provider = ProviderId, error = ex.Message },
                    $"Failed to list models for {ProviderId}: {ex.Message}"),
                LogLevel.Error);
            return Array.Empty<string>();
        }
    }

    private void LogAttemptFailure(int candidateIndex, int attempt, string error)
    {
        // 瞬时请求失败仅 Debug 级日志，玩家不可见（§4.8）。
        DialogueServices.Monitor?.Log(
            I18n.Get(
                "log.dialogue.requestAttemptFailed",
                new { provider = ProviderId, candidate = candidateIndex + 1, attempt = attempt + 1, error }),
            LogLevel.Debug);
    }

    protected sealed class RequestCandidate
    {
        public RequestCandidate(string url, string json)
        {
            Url = url;
            Json = json;
        }

        public string Url { get; }

        public string Json { get; }

        public bool HasThinkingParameters { get; init; }

        public string ThinkingLevel { get; init; } = string.Empty;

        public string ThinkingDescription { get; init; } = string.Empty;
    }

    protected readonly struct AttemptOutcome
    {
        public AttemptOutcome(LlmReply reply, bool abortCandidate = false)
        {
            Reply = reply;
            AbortCandidate = abortCandidate;
        }

        public LlmReply Reply { get; }

        /// <summary>true = 该候选无需继续重试（如 Gemini 空文本 200），直接换下一个候选。</summary>
        public bool AbortCandidate { get; }
    }
}
