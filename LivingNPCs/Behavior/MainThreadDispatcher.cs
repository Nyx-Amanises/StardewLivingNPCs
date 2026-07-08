using System;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace LivingNPCs.Behavior;

/// <summary>
/// 后台线程 → 游戏主线程的轻量交接（WP16 §4.2）：注册一次性的 UpdateTicked，
/// 把回调推迟到下一游戏 tick 执行。无 SMAPI 环境（单元测试）时同步执行。
/// </summary>
internal static class MainThreadDispatcher
{
    public static void Post(Action action)
    {
        var events = LivingNPCs.Dialogue.DialogueServices.Helper?.Events.GameLoop;
        if (events == null)
        {
            Run(action);
            return;
        }

        void Handler(object? sender, UpdateTickedEventArgs e)
        {
            events.UpdateTicked -= Handler;
            Run(action);
        }

        events.UpdateTicked += Handler;
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            LivingNPCs.Dialogue.DialogueServices.Monitor?.Log($"Main-thread dispatch failed: {ex}", LogLevel.Warn);
        }
    }
}
