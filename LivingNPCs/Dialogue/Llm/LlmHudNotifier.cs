using System;
using System.Collections.Concurrent;
using StardewValley;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 后台线程安全的 HUD 提示通道：消息先进并发队列，由主线程的 UpdateTicked 泵取出后
/// 才调用 Game1.addHUDMessage（游戏消息列表非线程安全，不能在后台线程直接加）。
/// EnsurePump 必须在主线程调用（ReplaceClient 时机天然满足）。
/// </summary>
internal static class LlmHudNotifier
{
    private static readonly ConcurrentQueue<string> Pending = new();
    private static bool _pumpSubscribed;

    /// <summary>测试注入：设置后消息直接交给该回调，不进队列。</summary>
    internal static Action<string>? SinkForTests;

    /// <summary>任意线程可调用。</summary>
    public static void Show(string message)
    {
        Action<string>? sink = SinkForTests;
        if (sink != null)
        {
            sink(message);
            return;
        }

        Pending.Enqueue(message);
    }

    /// <summary>主线程调用；无 SMAPI 事件环境（单元测试）时静默跳过。</summary>
    public static void EnsurePump()
    {
        if (_pumpSubscribed)
        {
            return;
        }

        var events = DialogueServices.Helper?.Events;
        if (events == null)
        {
            return;
        }

        events.GameLoop.UpdateTicked += (_, _) =>
        {
            while (Pending.TryDequeue(out string? message))
            {
                try
                {
                    Game1.addHUDMessage(HUDMessage.ForCornerTextbox(message));
                }
                catch
                {
                    // HUD 不可用（标题界面等）时丢弃提示；error 日志已另行记录。
                }
            }
        };
        _pumpSubscribed = true;
    }
}
