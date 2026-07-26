using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

/// <summary>
/// F3 修复矩阵：生成结果的呈现许可（世界就绪 / 过日流程 / 前台菜单归属）与
/// 临时会话关闭后清理全局对话状态的共用三重守门。
/// </summary>
public sealed class GenerationSchedulerPresentGuardTests
{
    [Theory]
    // 常规呈现：世界就绪、非过日、前台是本次生成的思考窗（关窗后接替显示）。
    [InlineData(true, false, false, true, true)]
    // 常规呈现：世界就绪、非过日、无任何菜单（思考窗被外力关闭但世界状态干净）。
    [InlineData(true, false, true, false, true)]
    // 过日/结算流程中：一律丢弃。
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    // 世界未就绪（回标题/加载中）：一律丢弃。
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    // 前台是他人菜单（ShippingMenu、事件对话框、其他 mod 菜单）：丢弃，不得顶替。
    [InlineData(true, false, false, false, false)]
    public void Present_Allowed_Only_In_Safe_World_State(
        bool worldReady, bool dayTransition, bool menuIsNull, bool menuIsOwnThinkingBox, bool expected)
    {
        Assert.Equal(
            expected,
            GenerationScheduler.ShouldPresentResult(worldReady, dayTransition, menuIsNull, menuIsOwnThinkingBox));
    }

    [Theory]
    // 唯一允许清理全局对话状态的形态：无菜单 + 说话人仍指向本 NPC + 本 NPC 对白栈已空。
    [InlineData(true, true, true, true)]
    // 有任何菜单（事件对话框/结算菜单/他人 mod 菜单）在前台：绝不清理。
    [InlineData(false, true, true, false)]
    // 说话人已切换（closeDialogue 已清理或他人接管对话）：不动。
    [InlineData(true, false, true, false)]
    // 本 NPC 还有待显示的对白：不动。
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    public void Residual_Dialogue_State_Cleared_Only_For_Own_Leftovers(
        bool menuIsNull, bool currentSpeakerIsOwnNpc, bool ownNpcStackEmpty, bool expected)
    {
        Assert.Equal(
            expected,
            DialogueUiStateGuard.ShouldClearResidualDialogueState(menuIsNull, currentSpeakerIsOwnNpc, ownNpcStackEmpty));
    }
}
