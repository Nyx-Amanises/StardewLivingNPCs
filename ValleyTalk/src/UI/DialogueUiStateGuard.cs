using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace ValleyTalk;

internal static class DialogueUiStateGuard
{
    public static bool HasEmptyDialogueStack(NPC npc)
    {
        if (npc == null)
        {
            return false;
        }

        var stack = npc.CurrentDialogue;
        return stack == null || stack.Count == 0;
    }

    public static void ClearDialogueState(NPC speaker, DialogueBox dialogueBox = null)
    {
        if (dialogueBox == null || ReferenceEquals(Game1.activeClickableMenu, dialogueBox))
        {
            if (Game1.activeClickableMenu is DialogueBox || dialogueBox != null)
            {
                Game1.activeClickableMenu = null;
            }
        }

        if (speaker == null || SpeakersMatch(Game1.currentSpeaker, speaker))
        {
            Game1.currentSpeaker = null;
        }

        Game1.dialogueUp = false;
        Game1.dialogueTyping = false;
        Game1.dialogueButtonShrinking = false;
        Game1.currentDialogueCharacterIndex = 0;
        ReleasePlayerControl();
    }

    public static bool TrySkipEmptySpeakerDraw()
    {
        NPC speaker = Game1.currentSpeaker;
        if (speaker == null)
        {
            return true;
        }

        if (!HasEmptyDialogueStack(speaker))
        {
            return true;
        }

        var dialogueBox = Game1.activeClickableMenu as DialogueBox;
        bool ownedByValleyTalk =
            ThinkingDialogueController.IsThinkingBox(dialogueBox)
            || NativeDialogueTextInputController.IsInputBox(dialogueBox);

        ClearDialogueState(speaker, dialogueBox);
        if (ownedByValleyTalk)
        {
            LogBilingual(
                "ValleyTalk cleared a stale temporary dialogue UI before Stardew could draw an empty NPC dialogue stack.",
                "ValleyTalk 已在原版绘制空 NPC 对白栈前清理残留临时对白界面。",
                LogLevel.Trace);
        }
        else
        {
            LogBilingual(
                $"ValleyTalk skipped a stale dialogue draw for {speaker.Name} because the NPC dialogue stack was empty.",
                $"ValleyTalk 已跳过 {speaker.displayName ?? speaker.Name} 的残留对白绘制，因为 NPC 对白栈为空。",
                LogLevel.Warn);
        }

        return false;
    }

    public static void RemoveDialogue(NPC npc, Dialogue dialogue)
    {
        if (dialogue == null)
        {
            return;
        }

        RemoveDialogues(npc, candidate => ReferenceEquals(candidate, dialogue));
    }

    public static void RemoveDialogues(NPC npc, Predicate<Dialogue> predicate)
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

    private static void ReleasePlayerControl()
    {
        if (Game1.player == null || Game1.eventUp || Game1.currentLocation?.currentEvent != null)
        {
            return;
        }

        Game1.player.CanMove = true;
        Game1.player.freezePause = 0;
        Game1.player.movementPause = 0;
        Game1.player.noMovementPause = 0;

        LogBilingual(
            "ValleyTalk released player control after closing a temporary dialogue UI.",
            "ValleyTalk 已在关闭临时对白界面后恢复玩家控制。",
            LogLevel.Trace);
    }

    private static bool SpeakersMatch(NPC currentSpeaker, NPC expectedSpeaker)
    {
        return ReferenceEquals(currentSpeaker, expectedSpeaker)
            || (
                currentSpeaker != null
                && expectedSpeaker != null
                && string.Equals(currentSpeaker.Name, expectedSpeaker.Name, StringComparison.Ordinal)
            );
    }

    private static void LogBilingual(string english, string chinese, LogLevel level)
    {
        ModEntry.SMonitor?.Log(english, level);
        ModEntry.SMonitor?.Log(chinese, level);
    }
}
