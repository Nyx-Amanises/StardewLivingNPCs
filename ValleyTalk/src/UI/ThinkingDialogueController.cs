using System;
using StardewValley;
using StardewValley.Menus;

namespace ValleyTalk;

internal static class ThinkingDialogueController
{
    private static bool active;
    private static string npcName;
    private static string baseText;
    private static DialogueBox activeBox;

    public static void Start(NPC npc)
    {
        if (npc == null)
        {
            return;
        }

        active = true;
        npcName = npc.Name;
        baseText = $"{npc.displayName}正在思考";
        activeBox = null;

        var dialogue = new Dialogue(npc, $"{SldConstants.DialogueKeyPrefix}Thinking", $"{baseText}...");
        npc.CurrentDialogue.Push(dialogue);
        Game1.DrawDialogue(dialogue);
        npc.CurrentDialogue.TryPop(out _);
        activeBox = Game1.activeClickableMenu as DialogueBox;
    }

    public static void Close()
    {
        if (Game1.activeClickableMenu is DialogueBox dialogueBox && IsThinkingBox(dialogueBox))
        {
            Game1.exitActiveMenu();
        }

        Clear();
    }

    public static bool IsThinkingBox(DialogueBox dialogueBox)
    {
        if (!active || dialogueBox == null)
        {
            return false;
        }

        if (activeBox != null && !ReferenceEquals(activeBox, dialogueBox))
        {
            return false;
        }

        return string.Equals(dialogueBox.characterDialogue?.speaker?.Name, npcName, StringComparison.Ordinal);
    }

    public static bool TryGetThinkingText(DialogueBox dialogueBox, out string text)
    {
        text = null;
        if (!IsThinkingBox(dialogueBox))
        {
            return false;
        }

        int dotCount = 1 + (int)((Game1.currentGameTime?.TotalGameTime.TotalMilliseconds ?? 0d) / 450d % 3d);
        text = baseText + new string('.', dotCount);
        return true;
    }

    private static void Clear()
    {
        active = false;
        npcName = null;
        baseText = null;
        activeBox = null;
    }
}
