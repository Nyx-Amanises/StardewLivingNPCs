using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Menus;

namespace ValleyTalk;

internal static class ThinkingDialogueController
{
    private static bool active;
    private static NPC activeNpc;
    private static string npcName;
    private static string baseText;
    private static DialogueBox activeBox;
    private static string ThinkingDialogueKey => $"{SldConstants.DialogueKeyPrefix}Thinking";

    public static void Start(NPC npc)
    {
        if (npc == null)
        {
            return;
        }

        Close();
        RemoveStale(npc);

        active = true;
        activeNpc = npc;
        npcName = npc.Name;
        string thinkingText = Util.GetString("uiThinking", new { Name = npc.displayName }, returnNull: true)
            ?? $"{npc.displayName} is thinking";
        baseText = thinkingText;
        activeBox = null;

        var dialogue = new Dialogue(npc, ThinkingDialogueKey, $"{baseText}...")
        {
            removeOnNextMove = true,
            temporaryDialogueKey = ThinkingDialogueKey
        };
        npc.CurrentDialogue.Push(dialogue);
        Game1.DrawDialogue(dialogue);
        activeBox = Game1.activeClickableMenu as DialogueBox;
    }

    public static void Close()
    {
        if (Game1.activeClickableMenu is DialogueBox dialogueBox && IsThinkingBox(dialogueBox))
        {
            Game1.exitActiveMenu();
        }

        RemoveStale(activeNpc);
        Clear();
    }

    public static void RemoveStale(NPC npc)
    {
        if (npc == null)
        {
            return;
        }

        Remove(npc, IsThinkingDialogue);
    }

    public static bool TryDiscardInactiveTop(NPC npc, Stack<Dialogue> dialogues)
    {
        if (dialogues == null || dialogues.Count == 0)
        {
            return false;
        }

        if (!IsThinkingDialogue(dialogues.Peek()) || IsActiveFor(npc))
        {
            return false;
        }

        dialogues.Pop();
        return true;
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

    private static bool IsActiveFor(NPC npc)
    {
        return active
            && npc != null
            && (ReferenceEquals(activeNpc, npc) || string.Equals(npc.Name, npcName, StringComparison.Ordinal));
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
        activeNpc = null;
        npcName = null;
        baseText = null;
        activeBox = null;
    }

    private static void Remove(NPC npc, Dialogue dialogue)
    {
        if (dialogue == null)
        {
            return;
        }

        Remove(npc, candidate => ReferenceEquals(candidate, dialogue));
    }

    private static void Remove(NPC npc, Predicate<Dialogue> predicate)
    {
        var stack = npc?.CurrentDialogue;
        if (stack == null || stack.Count == 0 || predicate == null)
        {
            return;
        }

        var kept = new List<Dialogue>();
        while (stack.Count > 0)
        {
            var dialogue = stack.Pop();
            if (!predicate(dialogue))
            {
                kept.Add(dialogue);
            }
        }

        for (int i = kept.Count - 1; i >= 0; i--)
        {
            stack.Push(kept[i]);
        }
    }

    internal static bool IsThinkingDialogue(Dialogue dialogue)
    {
        if (dialogue == null)
        {
            return false;
        }

        if (string.Equals(dialogue.temporaryDialogueKey, ThinkingDialogueKey, StringComparison.Ordinal))
        {
            return true;
        }

        return dialogue.dialogues?.Any(line =>
            !string.IsNullOrWhiteSpace(line?.Text)
            && (
                line.Text.Contains("正在思考", StringComparison.Ordinal)
                || line.Text.Contains("is thinking", StringComparison.OrdinalIgnoreCase)
            )) == true;
    }
}
