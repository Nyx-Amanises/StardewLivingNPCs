using System;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 全局"当前客户端"的持有与切换（WP11 §4.5/§5）。游戏启动（读配置后）与 GMCM 保存时
/// 都经 ReplaceClient 整体替换实例；旧实例上在途请求继续跑完（不做取消），
/// 但其连接自检因陈旧防护而静默作废。替换同时清除"引擎禁用"标志并复位熔断（裁决 2：配置变更即复位）。
/// ReplaceClient 必须在主线程调用（i18n 预解析与 HUD 泵挂接都依赖主线程）。
/// </summary>
internal sealed class LlmClientHost
{
    public static LlmClientHost Instance { get; } = new();

    private readonly object _gate = new();
    private LlmClientBase? _raw;
    private BreakerGuardedLlmClient? _guarded;
    private LlmCircuitBreaker _breaker = new();
    private BreakerTexts _breakerTexts = BreakerTexts.EnglishDefaults();

    /// <summary>测试注入：熔断计时时钟。下一次 ReplaceClient 生效。</summary>
    internal Func<DateTime>? ClockForTests;

    /// <summary>对 WP10 暴露的当前客户端（带熔断防护）；未配置/配置无效时为 null。</summary>
    public ILlmClient? Current => _guarded;

    /// <summary>原始客户端，仅供自检与陈旧比对；不要用它发真实生成（会绕过熔断）。</summary>
    internal LlmClientBase? CurrentRaw => _raw;

    /// <summary>配置错误（Provider 无效）时为 true：引擎保持关闭，不崩游戏。</summary>
    public bool EngineDisabled { get; private set; }

    public LlmSuspensionKind Suspension => _breaker.CurrentSuspension;

    /// <summary>生成入口检查：挂起/禁用/未配置时直接回退原版台词（WP10/WP12 消费）。</summary>
    public bool CanGenerate => !EngineDisabled && _guarded != null && Suspension == LlmSuspensionKind.None;

    /// <summary>最近一次自检任务；仅供测试与诊断等待，游戏流程不依赖。</summary>
    internal Task? LastSelfCheck { get; private set; }

    public void ReplaceClient(LlmConnectionSettings settings)
    {
        // 主线程预解析熔断文案（后台触发时不能碰游戏内容管线），并把 HUD 泵挂上主线程 tick。
        _breakerTexts = BreakerTexts.Resolve();
        LlmHudNotifier.EnsurePump();

        LlmClientBase? client = LlmClientFactory.TryCreate(settings);
        lock (_gate)
        {
            _breaker = new LlmCircuitBreaker(ClockForTests);
            if (client == null)
            {
                _raw = null;
                _guarded = null;
                EngineDisabled = true;
            }
            else
            {
                _raw = client;
                _guarded = new BreakerGuardedLlmClient(client, _breaker, OnBreakerTrip);
                EngineDisabled = false;
            }
        }

        // 让搬运件（礼物邮件/记忆印象/路由等 LegacyLlm 调用点）立即接上新客户端。
        LegacyLlm.Instance = _guarded != null ? new LegacyLlmBridge(this) : new LegacyLlmDummy();

        if (client != null && !settings.SuppressConnectionCheck)
        {
            LastSelfCheck = ConnectionSelfCheck.Start(this, client, settings);
        }
        else
        {
            LastSelfCheck = null;
        }
    }

    private void OnBreakerTrip(LlmSuspensionKind kind)
    {
        // 触发时一次性 HUD 提示 + SMAPI error 日志（裁决 2）。文案已在主线程预解析。
        string message = kind == LlmSuspensionKind.Auth ? _breakerTexts.Auth : _breakerTexts.Rate;
        DialogueServices.Monitor?.Log($"[LivingNPCs] {message}", LogLevel.Error);
        LlmHudNotifier.Show(message);
    }

    private sealed class BreakerTexts
    {
        public string Auth { get; private init; } = string.Empty;

        public string Rate { get; private init; } = string.Empty;

        public static BreakerTexts EnglishDefaults()
        {
            return new BreakerTexts
            {
                Auth = "AI dialogue is paused: the LLM provider rejected the credentials twice (HTTP 401/403). Fix the API key in the mod config to resume.",
                Rate = "AI dialogue is paused for 10 minutes: the LLM provider reported repeated rate-limit errors (HTTP 429)."
            };
        }

        /// <summary>i18n 键归 WP15（裁决 2）；键缺失用英文兜底。主线程调用。</summary>
        public static BreakerTexts Resolve()
        {
            BreakerTexts defaults = EnglishDefaults();
            return new BreakerTexts
            {
                Auth = Util.GetConsoleString("dialogue.breaker.auth", null, defaults.Auth),
                Rate = Util.GetConsoleString("dialogue.breaker.rate", null, defaults.Rate)
            };
        }
    }
}
