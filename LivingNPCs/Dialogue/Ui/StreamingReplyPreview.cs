using System;
using System.Text;

using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Dialogue.Ui;

/// <summary>
/// 流式回复的实时预览缓冲：worker 线程逐 token 追加，"思考中"窗（原版 DialogueBox）
/// 每帧经 <see cref="TryGetPreview"/> 取可见文本替代点点点动画——玩家在原版对话框里
/// 看着字一点点冒出来；定稿后走与非流式完全相同的原版呈现路径。
/// 引擎重试时经 OnToken 发送 <see cref="StreamingControlTokens.RetryReset"/>，此处按契约清空重来。
/// </summary>
internal static class StreamingReplyPreview
{
    /// <summary>预览只展示开头这么多字符（思考窗容量有限；完整文本随后由正式对话框分页呈现）。</summary>
    internal const int MaxPreviewChars = 150;

    private static readonly object Gate = new();
    private static readonly StringBuilder Raw = new();
    private static bool active;
    private static int cachedRawLength = -1;
    private static string cachedVisible = string.Empty;

    /// <summary>开始一次流式会话的预览（主线程，随思考窗 Start 调用）。</summary>
    public static void Begin()
    {
        lock (Gate)
        {
            active = true;
            Raw.Clear();
            cachedRawLength = -1;
            cachedVisible = string.Empty;
        }
    }

    /// <summary>结束并清空（完成呈现前 / 取消 / 失败时调用；任意线程安全）。</summary>
    public static void End()
    {
        lock (Gate)
        {
            active = false;
            Raw.Clear();
            cachedRawLength = -1;
            cachedVisible = string.Empty;
        }
    }

    /// <summary>worker 线程追加一个原始增量；重试控制记号会清空已积累的预览。</summary>
    public static void Append(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        lock (Gate)
        {
            if (!active)
            {
                return;
            }

            if (string.Equals(delta, StreamingControlTokens.RetryReset, StringComparison.Ordinal))
            {
                Raw.Clear();
                cachedRawLength = -1;
                cachedVisible = string.Empty;
                return;
            }

            Raw.Append(delta);
        }
    }

    /// <summary>
    /// 主线程每帧调用；false = 还没有可显示的内容（思考窗回落到点点点）。
    /// 可见文本经 <see cref="StreamingDialoguePreview.ExtractVisibleText"/> 过滤隐藏元数据，
    /// 超出 <see cref="MaxPreviewChars"/> 截断加省略号；按原始长度缓存避免每帧重解析。
    /// </summary>
    public static bool TryGetPreview(out string text)
    {
        string snapshot;
        lock (Gate)
        {
            if (!active || Raw.Length == 0)
            {
                text = string.Empty;
                return false;
            }

            if (Raw.Length == cachedRawLength)
            {
                text = cachedVisible;
                return text.Length > 0;
            }

            snapshot = Raw.ToString();
        }

        string visible = StreamingDialoguePreview.ExtractVisibleText(snapshot)?.Trim() ?? string.Empty;
        if (visible.Length > MaxPreviewChars)
        {
            visible = visible[..MaxPreviewChars] + "…";
        }

        lock (Gate)
        {
            cachedRawLength = snapshot.Length;
            cachedVisible = visible;
        }

        text = visible;
        return visible.Length > 0;
    }
}
