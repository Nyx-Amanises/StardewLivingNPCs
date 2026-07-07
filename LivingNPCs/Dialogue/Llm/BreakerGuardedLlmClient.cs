using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 熔断装饰器：包住真实客户端，观测每次请求结果并驱动 LlmCircuitBreaker。
/// 挂起期间非流式直接回失败响应（WP10 据 IsSuccess 回退原版台词）、流式直接抛 LlmStreamException。
/// 玩家/系统取消不计入熔断统计。
/// </summary>
internal sealed class BreakerGuardedLlmClient : ILlmClient, ILlmCapabilities
{
    private readonly LlmClientBase _inner;
    private readonly LlmCircuitBreaker _breaker;
    private readonly Action<LlmSuspensionKind> _onTrip;

    public BreakerGuardedLlmClient(LlmClientBase inner, LlmCircuitBreaker breaker, Action<LlmSuspensionKind> onTrip)
    {
        _inner = inner;
        _breaker = breaker;
        _onTrip = onTrip;
    }

    internal LlmClientBase Inner => _inner;

    public string ProviderId => _inner.ProviderId;

    public bool IsHighlySensoredModel => _inner.IsHighlySensoredModel;

    public string ExtraInstructions => _inner.ExtraInstructions;

    public async Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        LlmSuspensionKind suspension = _breaker.CurrentSuspension;
        if (suspension != LlmSuspensionKind.None)
        {
            return LlmReply.Failure(SuspensionMessage(suspension), suspension == LlmSuspensionKind.Auth ? 401 : 429);
        }

        LlmReply reply = await _inner.CompleteAsync(request, ct).ConfigureAwait(false);
        RecordAndNotify(reply.IsSuccess, reply.HttpStatus);
        return reply;
    }

    public async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        LlmSuspensionKind suspension = _breaker.CurrentSuspension;
        if (suspension != LlmSuspensionKind.None)
        {
            throw new LlmStreamException(SuspensionMessage(suspension), suspension == LlmSuspensionKind.Auth ? 401 : 429);
        }

        IAsyncEnumerator<LlmStreamEvent> events = _inner.StreamAsync(request, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await events.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (LlmStreamException ex)
                {
                    RecordAndNotify(success: false, ex.HttpStatus);
                    throw;
                }
                catch (Exception ex)
                {
                    RecordAndNotify(success: false, LlmClientBase.ExtractHttpStatus(ex));
                    throw;
                }

                if (!moved)
                {
                    break;
                }

                yield return events.Current;
            }
        }
        finally
        {
            await events.DisposeAsync().ConfigureAwait(false);
        }

        // 内部流只在成功路径正常收尾（失败以异常表达），完整走完即成功。
        RecordAndNotify(success: true, 200);
    }

    private void RecordAndNotify(bool success, int httpStatus)
    {
        LlmSuspensionKind tripped = _breaker.RecordOutcome(success, httpStatus);
        if (tripped != LlmSuspensionKind.None)
        {
            _onTrip(tripped);
        }
    }

    private static string SuspensionMessage(LlmSuspensionKind kind)
    {
        return kind == LlmSuspensionKind.Auth
            ? "LLM requests are suspended after repeated authentication failures (HTTP 401/403); change the provider settings to resume."
            : "LLM requests are suspended after repeated rate-limit errors (HTTP 429); they resume automatically after the cooldown.";
    }
}
