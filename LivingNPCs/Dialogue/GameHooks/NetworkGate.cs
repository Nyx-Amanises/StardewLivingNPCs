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
    private static volatile bool cachedAvailable = true;

    internal static void ResetForTests()
    {
        InstantProbeForTests = null;
        BackgroundProbeForTests = null;
        LastBackgroundProbe = null;
        probeInFlight = 0;
        cachedAvailable = true;
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

        // 首查失败：立即判不可用（调用方放行原版），后台探测刷新缓存标志。
        if (cachedAvailable)
        {
            cachedAvailable = false;
        }

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
