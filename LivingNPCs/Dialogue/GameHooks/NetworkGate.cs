using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// Android 网络门控（WP12 §4.6.2 非阻塞版）：桌面恒真；"跳过连接检查"配置绕过；
/// Android 先做即时接口查询，失败**立即**返回不可用（本次放行原版台词、绝不阻塞
/// 游戏线程），同时后台跑一次 WP11 的重试探测（每秒 × 5）刷新缓存标志供下次使用。
/// </summary>
internal static class NetworkGate
{
    /// <summary>测试注入：即时探针（null 走 NetworkInterface.GetIsNetworkAvailable）。</summary>
    internal static Func<bool>? InstantProbeForTests;

    /// <summary>测试注入：后台重试探测（null 走 NetworkAvailability.EnsureAvailableForGenerationAsync）。</summary>
    internal static Func<Task<bool>>? BackgroundProbeForTests;

    /// <summary>测试用：最近一次后台探测任务（等待其完成以断言缓存刷新）。</summary>
    internal static Task? LastBackgroundProbe;

    private static int probeInFlight;

    /// <summary>最近一次后台重试探测（HTTP 实测）是否确认网络可用；初始 false（尚未验证过）。</summary>
    private static volatile bool cachedAvailable;

    internal static void ResetForTests()
    {
        InstantProbeForTests = null;
        BackgroundProbeForTests = null;
        LastBackgroundProbe = null;
        probeInFlight = 0;
        cachedAvailable = false;
    }

    public static bool IsAvailableForGeneration()
    {
        if (!AndroidPlatform.IsAndroid)
        {
            return true;
        }

        if (DialogueServices.Config?.SuppressConnectionCheck == true)
        {
            return true;
        }

        if (ProbeInstant())
        {
            cachedAvailable = true;
            return true;
        }

        // 即时接口查询失败但后台重试探测此前确认过可用：判为 Android 误报（F7 自愈），
        // 本次放行，同时踢一次后台复核刷新缓存；复核失败会把缓存翻回 false 恢复拦截。
        if (cachedAvailable)
        {
            BeginBackgroundProbe();
            return true;
        }

        // 从未验证成功（或复核已失败）：立即判不可用（调用方放行原版、绝不阻塞游戏线程），
        // 后台探测刷新缓存标志供下次使用。
        BeginBackgroundProbe();
        return false;
    }

    private static bool ProbeInstant()
    {
        Func<bool>? probe = InstantProbeForTests;
        if (probe != null)
        {
            return probe();
        }

        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            return false;
        }
    }

    private static void BeginBackgroundProbe()
    {
        if (Interlocked.CompareExchange(ref probeInFlight, 1, 0) != 0)
        {
            return;
        }

        LastBackgroundProbe = Task.Run(async () =>
        {
            try
            {
                Func<Task<bool>>? probe = BackgroundProbeForTests;
                cachedAvailable = probe != null
                    ? await probe().ConfigureAwait(false)
                    : await NetworkAvailability.EnsureAvailableForGenerationAsync().ConfigureAwait(false);
            }
            catch
            {
                cachedAvailable = false;
            }
            finally
            {
                Interlocked.Exchange(ref probeInFlight, 0);
            }
        });
    }
}
