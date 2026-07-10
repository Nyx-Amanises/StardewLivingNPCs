using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

using LivingNPCs.Dialogue.Engine;
using GameDialogue = StardewValley.Dialogue;
namespace LivingNPCs.Dialogue.Ui;

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
        bool ownedByLivingNPCs =
            ThinkingDialogueController.IsThinkingBox(dialogueBox)
            || NativeDialogueTextInputController.IsInputBox(dialogueBox);

        ClearDialogueState(speaker, dialogueBox);
        if (ownedByLivingNPCs)
        {
            DialogueServices.Monitor?.Log(I18n.Get("log.dialogue.staleUiCleared"), LogLevel.Trace);
        }
        else
        {
            string npcLabel = speaker.displayName ?? speaker.Name;
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.staleDrawSkipped",
                    new { npc = npcLabel },
                    $"LivingNPCs skipped a stale dialogue draw for {npcLabel} because the NPC dialogue stack was empty."),
                LogLevel.Warn);
        }

        return false;
    }

    public static void RemoveDialogue(NPC npc, GameDialogue dialogue)
    {
        if (dialogue == null)
        {
            return;
        }

        RemoveDialogues(npc, candidate => ReferenceEquals(candidate, dialogue));
    }

    public static void RemoveDialogues(NPC npc, Predicate<GameDialogue> predicate)
    {
        var stack = npc?.CurrentDialogue;
        if (stack == null || stack.Count == 0 || predicate == null)
        {
            return;
        }

        var kept = new List<GameDialogue>();
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

        DialogueServices.Monitor?.Log(I18n.Get("log.dialogue.playerControlReleased"), LogLevel.Trace);
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

}
