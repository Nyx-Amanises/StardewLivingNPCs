using System;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>Android 网络可用性检查（WP11 §4.4）。非 Android 平台两处挂载点均零开销跳过。</summary>
internal static class NetworkAvailability
{
    /// <summary>测试用探针注入；null 走 NetworkInterface.GetIsNetworkAvailable。</summary>
    internal static Func<bool>? ProbeForTests;

    /// <summary>预检重查间隔（测试可缩短）。</summary>
    internal static TimeSpan RecheckDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 挂载点 1：每次推理入口。Android 断网时立即抛 InvalidOperationException，
    /// 不发请求不重试，由重试循环外层捕获后按普通失败处理。
    /// </summary>
    public static void ThrowIfUnavailable()
    {
        if (!AndroidPlatform.IsAndroid)
        {
            return;
        }

        if (!IsNetworkAvailable())
        {
            throw new InvalidOperationException("Network not available");
        }
    }

    /// <summary>
    /// 挂载点 2：游戏钩子侧预检（WP12 发起生成前调用）。Android 下首查不可用则
    /// Warn 后每秒重查共 5 次；仍不可用再记 Warn 并返回 false（本次生成走原版对话）。
    /// </summary>
    public static async Task<bool> EnsureAvailableForGenerationAsync(CancellationToken ct = default)
    {
        if (!AndroidPlatform.IsAndroid)
        {
            return true;
        }

        if (IsNetworkAvailable())
        {
            return true;
        }

        DialogueServices.Monitor?.Log("[LivingNPCs] Network unavailable on Android; rechecking before AI dialogue generation.", LogLevel.Warn);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(RecheckDelay, ct).ConfigureAwait(false);
            if (IsNetworkAvailable())
            {
                return true;
            }
        }

        DialogueServices.Monitor?.Log("[LivingNPCs] Network still unavailable; skipping AI dialogue generation this time.", LogLevel.Warn);
        return false;
    }

    /// <summary>同步包装，给无法 async 的补丁点。</summary>
    public static bool EnsureAvailableForGeneration()
    {
        return EnsureAvailableForGenerationAsync().GetAwaiter().GetResult();
    }

    private static bool IsNetworkAvailable()
    {
        Func<bool>? probe = ProbeForTests;
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
}
