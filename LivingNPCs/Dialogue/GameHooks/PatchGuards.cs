using System;
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
        if (npc == null || !EngineReady)
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
        if (npc != null && NotifyIfConnectionUnconfigured())
        {
            return false;
        }

        return IsEnabledFor(npc);
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
