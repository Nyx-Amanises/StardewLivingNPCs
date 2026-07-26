using System;
using LivingNPCs.Dialogue.GameHooks;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.GameHooks;

/// <summary>
/// F2 修复矩阵：占位改道拦截器对入队结果的处置（纯函数）。
/// 入队成功才允许清栈改道；调度器忙时有原文则还原原版台词、无原文仅抑制显示——
/// 两条忙路径都绝不清空对白栈、不设置 currentSpeaker（副作用映射见 InterceptPlaceholder）。
/// </summary>
public sealed class PlaceholderInterceptOutcomeTests
{
    [Theory]
    [InlineData(true, true, nameof(PlaceholderInterceptOutcome.Rerouted))]
    [InlineData(true, false, nameof(PlaceholderInterceptOutcome.Rerouted))]
    [InlineData(false, true, nameof(PlaceholderInterceptOutcome.RestoredOriginal))]
    [InlineData(false, false, nameof(PlaceholderInterceptOutcome.SuppressedWithoutReroute))]
    public void Busy_Enqueue_Never_Reroutes_And_Prefers_Restoring_Vanilla_Text(
        bool enqueued, bool hasReferenceText, string expectedName)
    {
        // 参数用枚举名传递：内部枚举类型不能出现在公共测试方法签名里（CS0051）。
        var expected = Enum.Parse<PlaceholderInterceptOutcome>(expectedName);
        Assert.Equal(expected, DialogueDisplayInterceptor.ResolveInterceptOutcome(enqueued, hasReferenceText));
    }
}
