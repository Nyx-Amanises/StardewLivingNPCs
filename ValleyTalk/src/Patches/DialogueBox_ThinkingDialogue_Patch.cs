using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley.Menus;

namespace ValleyTalk;

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
