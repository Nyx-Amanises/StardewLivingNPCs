using LivingNPCs.Dialogue.GameHooks;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.GameHooks;

/// <summary>
/// F9 修复验证：婚后家务缓冲的日界清理——前一日未被婚后生成消费的播报行
///（婚后频率抽签不过、被动开关关闭等）跨日后自动丢弃，陈旧家务不进生成参考。
/// </summary>
[Collection("LlmLayer")]
public sealed class MarriageChoreBufferDayBoundaryTests : System.IDisposable
{
    private int day = 10;

    public MarriageChoreBufferDayBoundaryTests()
    {
        MarriageChoreBuffer.Clear();
        MarriageChoreBuffer.DayStampForTests = () => this.day;
    }

    public void Dispose()
    {
        MarriageChoreBuffer.DayStampForTests = null;
        MarriageChoreBuffer.Clear();
    }

    [Fact]
    public void Same_Day_Lines_Are_Kept_And_Drained()
    {
        MarriageChoreBuffer.Add("watered the pet bowl");
        MarriageChoreBuffer.Add("fed the animals");

        Assert.Equal(2, MarriageChoreBuffer.Count);
        Assert.Equal(new[] { "watered the pet bowl", "fed the animals" }, MarriageChoreBuffer.Drain());
        Assert.Equal(0, MarriageChoreBuffer.Count);
    }

    [Fact]
    public void Previous_Day_Leftovers_Are_Discarded_On_Read()
    {
        MarriageChoreBuffer.Add("yesterday's chores");
        this.day++;

        Assert.Equal(0, MarriageChoreBuffer.Count);
        Assert.Empty(MarriageChoreBuffer.Drain());
    }

    [Fact]
    public void Previous_Day_Leftovers_Are_Discarded_Before_New_Adds()
    {
        MarriageChoreBuffer.Add("yesterday's chores");
        this.day++;
        MarriageChoreBuffer.Add("today's chores");

        Assert.Equal(new[] { "today's chores" }, MarriageChoreBuffer.Drain());
    }
}
