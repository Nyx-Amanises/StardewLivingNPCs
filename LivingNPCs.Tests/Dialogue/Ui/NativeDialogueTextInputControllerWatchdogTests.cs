using LivingNPCs.Dialogue.Ui;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Ui;

/// <summary>
/// F1 修复的判定矩阵：原生输入会话的看门狗（宿主对话框被外力关闭时废弃会话、归还键盘）
/// 与废弃后全局对话状态的清理边界（仅清本会话残留，绝不碰他人打开的菜单）。
/// 两个判定均为纯函数；ForceClose 的无会话路径不触任何游戏静态，可直接调用验证幂等。
/// </summary>
public sealed class NativeDialogueTextInputControllerWatchdogTests
{
    [Theory]
    [InlineData(false, false, false)] // 无会话：宿主框在不在都不动作
    [InlineData(false, true, false)]  // 无会话：即使有输入框形态的菜单也不认领
    [InlineData(true, true, false)]   // 会话活跃且宿主框仍在（正常打字中）：保持
    [InlineData(true, false, true)]   // 会话活跃但宿主框已不在（外力关闭/被其他菜单顶替）：废弃
    public void Watchdog_Abandons_Only_When_Active_Session_Lost_Its_Host_Box(
        bool sessionActive, bool hostBoxAlive, bool expected)
    {
        Assert.Equal(
            expected,
            NativeDialogueTextInputController.ShouldAbandonSession(sessionActive, hostBoxAlive));
    }

    [Theory]
    [InlineData(true, true, true, true)]    // 无菜单 + 说话人仍指向本 NPC + 栈已空：确为本会话残留，清理
    [InlineData(false, true, true, false)]  // 有别的菜单开着（ShippingMenu/事件对话框等）：绝不碰
    [InlineData(true, false, true, false)]  // 说话人已不是本 NPC（closeDialogue 已清理或他人接管）：不动
    [InlineData(true, true, false, false)]  // NPC 栈里还有别的对白待显示：不动
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    public void Abandoned_State_Cleared_Only_For_Own_Residue(
        bool menuIsNull, bool currentSpeakerIsOwnNpc, bool ownNpcStackEmpty, bool expected)
    {
        Assert.Equal(
            expected,
            NativeDialogueTextInputController.ShouldClearAbandonedDialogueState(
                menuIsNull, currentSpeakerIsOwnNpc, ownNpcStackEmpty));
    }

    [Fact]
    public void ForceClose_Without_Active_Session_Is_A_Safe_Idempotent_NoOp()
    {
        // 存档重置路径在多数情况下无残留会话；必须可反复调用且不触发任何清理副作用。
        NativeDialogueTextInputController.ForceClose();
        NativeDialogueTextInputController.ForceClose();

        Assert.False(NativeDialogueTextInputController.IsInputBox(null));
    }
}
