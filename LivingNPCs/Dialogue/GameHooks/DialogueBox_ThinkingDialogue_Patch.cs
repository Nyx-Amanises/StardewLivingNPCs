using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley.Menus;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
namespace LivingNPCs.Dialogue.GameHooks;

[HarmonyPatch(typeof(DialogueBox), nameof(DialogueBox.getCurrentString))]
internal static class DialogueBox_GetCurrentString_ThinkingDialogue_Patch
{
    public static bool Prefix(DialogueBox __instance, ref string __result)
    {
        if (NativeDialogueTextInputController.TryGetDisplayText(__instance, out string inputText))
        {
            __result = inputText;
            return false;
        }

        if (ThinkingDialogueController.TryGetThinkingText(__instance, out string text))
        {
            __result = text;
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(DialogueBox), nameof(DialogueBox.draw), new[] { typeof(SpriteBatch) })]
internal static class DialogueBox_Draw_NativeTextInput_Patch
{
    public static void Postfix(DialogueBox __instance, SpriteBatch b)
    {
        NativeDialogueTextInputController.DrawInputText(__instance, b);
    }
}

[HarmonyPatch(typeof(DialogueBox), nameof(DialogueBox.receiveLeftClick), new[] { typeof(int), typeof(int), typeof(bool) })]
internal static class DialogueBox_ReceiveLeftClick_ThinkingDialogue_Patch
{
    public static bool Prefix(DialogueBox __instance)
    {
        if (NativeDialogueTextInputController.IsInputBox(__instance))
        {
            return false;
        }

        return !ThinkingDialogueController.IsThinkingBox(__instance);
    }
}

[HarmonyPatch(typeof(DialogueBox), nameof(DialogueBox.receiveKeyPress), new[] { typeof(Keys) })]
internal static class DialogueBox_ReceiveKeyPress_ThinkingDialogue_Patch
{
    public static bool Prefix(DialogueBox __instance, Keys key)
    {
        if (NativeDialogueTextInputController.IsInputBox(__instance))
        {
            NativeDialogueTextInputController.HandleSpecialKey(key);
            return false;
        }

        if (ThinkingDialogueController.IsThinkingBox(__instance))
        {
            // Let the player bail out of a slow or stuck generation by pressing Esc.
            if (key == Keys.Escape)
            {
                AsyncBuilder.Instance.CancelActiveGeneration();
            }

            // Keep the thinking box otherwise non-interactive.
            return false;
        }

        return true;
    }
}

/// <summary>
/// 手柄取消入口：键盘 Esc 之外，B 键在"思考中"窗取消生成、在输入框取消输入会话。
/// 目标必须写声明该方法的 IClickableMenu：DialogueBox 未覆写 receiveGamePadButton，
/// Harmony 特性寻址不沿继承链上溯，写 DialogueBox 会在运行时 "Undefined target method"
/// 并让整个 PatchAll 中止。补的是基类实现，因此前置守卫必须先确认实例确属本 mod 的
/// 两类对话框，其余菜单一律放行；自带覆写的菜单（如流式窗）不经基实现、天然不受影响。
/// </summary>
[HarmonyPatch(typeof(IClickableMenu), nameof(IClickableMenu.receiveGamePadButton))]
internal static class DialogueBox_ReceiveGamePadButton_ThinkingDialogue_Patch
{
    public static bool Prefix(IClickableMenu __instance, Buttons b)
    {
        if (__instance is not DialogueBox box)
        {
            return true;
        }

        if (NativeDialogueTextInputController.IsInputBox(box))
        {
            if (b == Buttons.B)
            {
                NativeDialogueTextInputController.HandleSpecialKey(Keys.Escape);
            }

            return false;
        }

        if (ThinkingDialogueController.IsThinkingBox(box))
        {
            if (b == Buttons.B)
            {
                AsyncBuilder.Instance.CancelActiveGeneration();
            }

            return false;
        }

        return true;
    }
}
