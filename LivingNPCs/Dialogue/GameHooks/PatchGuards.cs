using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>频率档位（WP12 §4.1：常规 / 婚后 / 送礼，0–4，4 = 总是）。</summary>
internal enum GenerationFrequencyKind
{
    General,
    Marriage,
    Gift
}

/// <summary>
/// §4.1 通用跳过条件的集中实现：总开关、运行时闸、共存禁用、LLM 可用性、
/// 每 NPC 启用链（转发 WP10 EnableGate）、三档频率抽签（常规档带当日记忆）、
/// 被动门控、Android 网络门控。全部补丁只调用这里，不各自散写。
/// </summary>
internal static class PatchGuards
{
    /// <summary>测试注入的引擎取用点；null 走 DialogueEngineHost.Instance。</summary>
    internal static Func<DialogueEngine?>? EngineResolverForTests;

    /// <summary>测试注入的 LLM 可用性；null 走 LlmClientHost.Instance.CanGenerate。</summary>
    internal static Func<bool>? CanGenerateResolverForTests;

    private static DialogueEngine? Engine => EngineResolverForTests != null
        ? EngineResolverForTests()
        : DialogueEngineHost.Instance;

    private static bool ClientCanGenerate => CanGenerateResolverForTests?.Invoke()
        ?? LlmClientHost.Instance.CanGenerate;

    /// <summary>
    /// 引擎整体可用（总门控）：引擎已装配、总开关开、WP15 运行时闸未落、
    /// 未因旧 mod 共存禁用、WP11 客户端可生成（初始化成功且未熔断）。
    /// </summary>
    public static bool EngineReady
    {
        get
        {
            if (Engine == null)
            {
                return false;
            }

            if (DialogueServices.Config?.EnableDialogueEngine != true)
            {
                return false;
            }

            if (DialogueEngineGate.RuntimeDisabled || EnableGate.EngineDisabledByCoexistence)
            {
                return false;
            }

            return ClientCanGenerate;
        }
    }

    /// <summary>每 NPC 启用判定（引擎就绪 + WP10 启用链：黑名单/禁用表/未授权内容包）。</summary>
    public static bool IsEnabledFor(NPC? npc)
    {
        // 少数响应/婚后台词补丁直接调用 IsEnabledFor，而不经过主动、被动或送礼门。
        // 因此分屏副屏的 v1 禁用必须在公共启用链再次静默兜底，避免已有 AI 对话继续
        // 进入进程级静态输入/调度状态。主动入口仍由 AllowInteractiveInput 负责提示。
        if (npc == null || IsSplitScreenSecondaryBlocked(notifyPlayer: false) || !EngineReady)
        {
            return false;
        }

        return Engine!.IsEnabledFor(npc);
    }

    /// <summary>测试注入的"连接未配置"判定；null 走 LlmClientHost.Instance.ConnectionLooksIncomplete。</summary>
    internal static Func<bool>? ConnectionUnconfiguredResolverForTests;

    private static long lastUnconfiguredHintTicks = long.MinValue;
    private const long UnconfiguredHintThrottleMs = 10_000;

    private static bool ConnectionUnconfigured => ConnectionUnconfiguredResolverForTests?.Invoke()
        ?? LlmClientHost.Instance.ConnectionLooksIncomplete;

    /// <summary>主动搭话路径（P1 / P13 前半 / P15）：就绪 + 启用，不抽签（§4.2.1）。</summary>
    public static bool AllowInteractiveInput(NPC? npc)
    {
        if (IsSplitScreenSecondaryBlocked(notifyPlayer: npc != null))
        {
            return false;
        }

        if (npc != null && NotifyIfConnectionUnconfigured())
        {
            return false;
        }

        return IsEnabledFor(npc);
    }

    /// <summary>已提示过"分屏不支持"的副屏 ScreenId（每屏一次性）。</summary>
    private static readonly HashSet<int> splitScreenNoticeShownForScreens = new();

    /// <summary>测试注入的分屏状态 (IsSplitScreen, ScreenId)；null 走 SMAPI Context。</summary>
    internal static Func<(bool IsSplitScreen, int ScreenId)>? SplitScreenStateResolverForTests;

    /// <summary>测试注入的分屏提示出口；null 时直接显示在当前分屏的 HUD。</summary>
    internal static Action<string>? SplitScreenNoticeSinkForTests;

    /// <summary>
    /// 多人 v1 不支持分屏副屏的 AI 对话：引擎的调度器/输入队列/思考窗全是进程级静态，
    /// 未做 PerScreen 化，副屏触发会与主屏互相踩状态。副屏一律回退原版对话；
    /// 主动搭话时给一次性 HUD 说明（被动路径静默）。PerScreen 化留 v2。
    /// </summary>
    internal static bool IsSplitScreenSecondaryBlocked(bool notifyPlayer)
    {
        bool isSplitScreen;
        int screenId;
        try
        {
            (isSplitScreen, screenId) = SplitScreenStateResolverForTests?.Invoke()
                ?? (Context.IsSplitScreen, Context.ScreenId);
        }
        catch
        {
            // 无游戏环境（单元测试/极早期）读分屏状态会抛：按非分屏放行。
            return false;
        }

        if (!isSplitScreen || screenId == 0)
        {
            return false;
        }

        if (notifyPlayer && splitScreenNoticeShownForScreens.Add(screenId))
        {
            string message = Util.GetConsoleString(
                "dialogue.hud.splitScreenUnsupported",
                null,
                "LivingNPCs: AI dialogue is unavailable for secondary split-screen players in this version; vanilla dialogue is used instead.");
            Action<string>? sink = SplitScreenNoticeSinkForTests;
            if (sink != null)
            {
                sink(message);
            }
            else
            {
                try
                {
                    // 必须在当前副屏的输入回调内直接显示；走全局异步 HUD 队列可能被主屏 tick 抢先消费。
                    Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message));
                }
                catch
                {
                    // HUD 失败不影响门禁裁决。
                }
            }
        }

        return true;
    }

    /// <summary>测试复位：清除分屏一次性提示记录与注入。</summary>
    internal static void ResetSplitScreenNoticeForTests()
    {
        splitScreenNoticeShownForScreens.Clear();
        SplitScreenStateResolverForTests = null;
        SplitScreenNoticeSinkForTests = null;
    }

    /// <summary>
    /// 玩家按住触发键主动搭话、引擎开着但连接必填项缺失时：给一次游戏内配置引导（10 秒节流），
    /// 并让入口回退原版对话，替代旧行为里的静默"..."。
    /// </summary>
    private static bool NotifyIfConnectionUnconfigured()
    {
        if (DialogueServices.Config?.EnableDialogueEngine != true)
        {
            return false;
        }

        if (DialogueEngineGate.RuntimeDisabled || EnableGate.EngineDisabledByCoexistence)
        {
            return false;
        }

        if (!ConnectionUnconfigured)
        {
            return false;
        }

        long now = Environment.TickCount64;
        if (lastUnconfiguredHintTicks == long.MinValue || now - lastUnconfiguredHintTicks >= UnconfiguredHintThrottleMs)
        {
            lastUnconfiguredHintTicks = now;
            LlmHudNotifier.Show(Util.GetConsoleString(
                "dialogue.hud.notConfigured",
                null,
                "LivingNPCs: the AI dialogue engine is not configured yet. Open Generic Mod Config Menu -> LivingNPCs -> AI Dialogue Engine and fill in the connection settings."));
        }

        return true;
    }

    /// <summary>测试复位：清除未配置提示节流与注入。</summary>
    internal static void ResetUnconfiguredHintForTests()
    {
        lastUnconfiguredHintTicks = long.MinValue;
        ConnectionUnconfiguredResolverForTests = null;
    }

    /// <summary>
    /// 被动类补丁门（P2/P3/P6/P7/P8/P10/P13 后半/P14）：
    /// "常规右键也生成 AI"配置开 + NPC 启用 + 频率抽签 + 可选网络门控。
    /// </summary>
    public static bool AllowPassiveGeneration(NPC? npc, GenerationFrequencyKind kind, bool checkNetwork)
    {
        if (IsSplitScreenSecondaryBlocked(notifyPlayer: false))
        {
            return false;
        }

        if (DialogueServices.Config?.GenerateAiForNormalRightClick != true)
        {
            return false;
        }

        if (!IsEnabledFor(npc))
        {
            return false;
        }

        if (!PassFrequency(npc!.Name, kind))
        {
            return false;
        }

        return !checkNetwork || NetworkGate.IsAvailableForGeneration();
    }

    /// <summary>送礼生成门（P4）：不受被动门控约束；启用 + 送礼频率（不带记忆）+ 网络。</summary>
    public static bool AllowGiftGeneration(NPC? npc)
    {
        if (IsSplitScreenSecondaryBlocked(notifyPlayer: false))
        {
            return false;
        }

        if (!IsEnabledFor(npc))
        {
            return false;
        }

        if (!PassFrequency(npc!.Name, GenerationFrequencyKind.Gift))
        {
            return false;
        }

        return NetworkGate.IsAvailableForGeneration();
    }

    /// <summary>频率抽签（引擎概率门承接；常规档带当日记忆变体）。</summary>
    public static bool PassFrequency(string npcName, GenerationFrequencyKind kind)
    {
        var engine = Engine;
        if (engine == null)
        {
            return false;
        }

        (int probability, bool retainDaily) = ResolveFrequency(DialogueServices.Config, kind);
        return engine.Gate.PassProbability(npcName, probability, retainDaily);
    }

    /// <summary>
    /// 档位映射（纯函数，单测覆盖）：常规=带当日记忆；婚后/送礼=每次独立抽（§4.2.4/§4.2.7）。
    /// </summary>
    internal static (int Probability, bool RetainDaily) ResolveFrequency(DialogueConfig? config, GenerationFrequencyKind kind)
    {
        config ??= new DialogueConfig();
        return kind switch
        {
            GenerationFrequencyKind.Marriage => (config.MarriageFrequency, false),
            GenerationFrequencyKind.Gift => (config.GiftFrequency, false),
            _ => (config.GeneralFrequency, true)
        };
    }
}
