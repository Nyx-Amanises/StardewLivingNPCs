using System;
using System.Collections.Generic;
using StardewValley;

using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Dialogue.Ui;

/// <summary>
/// 调度器 ↔ 流式对话窗之间的窄接口：AppendToken 线程安全（可从流式回调线程直呼），
/// Complete/Close 只在主线程调用。测试经 GenerationScheduler 的工厂注入假实现。
/// </summary>
internal interface IStreamingDialoguePresenter
{
    void AppendToken(string token);

    void Complete(
        GenerationResult result,
        IReadOnlyList<StreamingResponseOption> options,
        Action<StreamingResponseOption> onResponseSelected,
        Action onFinished);

    void Close();
}

/// <summary>
/// 生产实现：包装 <see cref="StreamingDialogueWindow"/> 并管理 activeClickableMenu 生命周期。
/// Open/Close 仅主线程；Close 只在窗仍是当前菜单时退出，避免踩掉玩家已打开的其他菜单。
/// </summary>
internal sealed class StreamingDialogueWindowPresenter : IStreamingDialoguePresenter
{
    private readonly StreamingDialogueWindow window;

    public StreamingDialogueWindowPresenter(NPC npc, Action onCancelRequested)
    {
        this.window = new StreamingDialogueWindow(npc) { OnCancelRequested = onCancelRequested };
    }

    /// <summary>主线程调用：把窗设为当前菜单。</summary>
    public void Open()
    {
        Game1.activeClickableMenu = this.window;
    }

    public void AppendToken(string token)
    {
        this.window.AppendToken(token);
    }

    public void Complete(
        GenerationResult result,
        IReadOnlyList<StreamingResponseOption> options,
        Action<StreamingResponseOption> onResponseSelected,
        Action onFinished)
    {
        this.window.Complete(result, options, onResponseSelected, onFinished);
    }

    public void Close()
    {
        if (ReferenceEquals(Game1.activeClickableMenu, this.window))
        {
            Game1.exitActiveMenu();
        }
    }
}
