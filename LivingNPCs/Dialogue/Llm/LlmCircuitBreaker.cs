using System;

namespace LivingNPCs.Dialogue.Llm;

internal enum LlmSuspensionKind
{
    None,

    /// <summary>401/403 连续 2 次：挂起直至配置变更（GMCM 保存或 config 重载），密钥不会自愈，不做定时恢复。</summary>
    Auth,

    /// <summary>429 连续 5 次：冷却 10 分钟（现实时间）后自动恢复。</summary>
    RateLimit
}

/// <summary>
/// 熔断器（WP11 §8 裁决 2）：同一 provider 连续同类致命错误触发挂起。
/// 任何一次成功响应清零计数；非致命类错误（超时、5xx 等）同样打断"连续"，双计数清零；
/// 连接自检不经过本组件（Host 用原始客户端自检），天然不计入统计。
/// </summary>
internal sealed class LlmCircuitBreaker
{
    public const int AuthTripThreshold = 2;
    public const int RateTripThreshold = 5;
    public static readonly TimeSpan RateCooldown = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly Func<DateTime> _utcNow;
    private int _consecutiveAuthFailures;
    private int _consecutiveRateFailures;
    private LlmSuspensionKind _suspension;
    private DateTime _rateResumeAtUtc;

    public LlmCircuitBreaker(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (static () => DateTime.UtcNow);
    }

    public LlmSuspensionKind CurrentSuspension
    {
        get
        {
            lock (_gate)
            {
                if (_suspension == LlmSuspensionKind.RateLimit && _utcNow() >= _rateResumeAtUtc)
                {
                    _suspension = LlmSuspensionKind.None;
                    _consecutiveRateFailures = 0;
                }

                return _suspension;
            }
        }
    }

    /// <summary>记录一次请求结果；仅当本次记录**造成**挂起转变时返回被触发的类别（供一次性提示）。</summary>
    public LlmSuspensionKind RecordOutcome(bool success, int httpStatus)
    {
        lock (_gate)
        {
            if (success)
            {
                _consecutiveAuthFailures = 0;
                _consecutiveRateFailures = 0;
                return LlmSuspensionKind.None;
            }

            switch (httpStatus)
            {
                case 401 or 403:
                    _consecutiveAuthFailures++;
                    _consecutiveRateFailures = 0;
                    if (_consecutiveAuthFailures >= AuthTripThreshold && _suspension != LlmSuspensionKind.Auth)
                    {
                        _suspension = LlmSuspensionKind.Auth;
                        return LlmSuspensionKind.Auth;
                    }

                    break;
                case 429:
                    _consecutiveRateFailures++;
                    _consecutiveAuthFailures = 0;
                    if (_consecutiveRateFailures >= RateTripThreshold && _suspension == LlmSuspensionKind.None)
                    {
                        _suspension = LlmSuspensionKind.RateLimit;
                        _rateResumeAtUtc = _utcNow() + RateCooldown;
                        return LlmSuspensionKind.RateLimit;
                    }

                    break;
                default:
                    _consecutiveAuthFailures = 0;
                    _consecutiveRateFailures = 0;
                    break;
            }

            return LlmSuspensionKind.None;
        }
    }

    /// <summary>配置变更（客户端重建）时复位。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _consecutiveAuthFailures = 0;
            _consecutiveRateFailures = 0;
            _suspension = LlmSuspensionKind.None;
        }
    }
}
