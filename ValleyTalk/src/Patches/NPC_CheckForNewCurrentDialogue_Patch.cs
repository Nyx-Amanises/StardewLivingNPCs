using HarmonyLib;
using StardewValley;
using System.Linq;

namespace ValleyTalk
{
    [HarmonyPatch(typeof(NPC), nameof(NPC.checkForNewCurrentDialogue))]
    public class NPC_CheckForNewCurrentDialogue_Patch
    {
        public static bool Prefix(ref NPC __instance, ref bool __result, int heartLevel, bool noPreface)
        {
            ModEntry.SMonitor.Log($"NPC {__instance.Name} checking for new dialogue at heart level {heartLevel}", StardewModdingAPI.LogLevel.Trace);
            return true;
        }

        public static void Postfix(ref NPC __instance, ref bool __result, int heartLevel, bool noPreface)
        {
            if (!__result || !DialogueBuilder.Instance.PatchPassiveNpc(__instance, ModEntry.Config.GeneralFrequency, true))
            {
                return;
            }

            if (!NetworkAvailabilityChecker.IsNetworkAvailableWithRetry())
            {
                ModEntry.SMonitor.Log($"Network not available, skipping AI new dialogue check for {__instance.Name}", StardewModdingAPI.LogLevel.Trace);
                return;
            }

            if (AsyncBuilder.Instance.AwaitingGeneration && AsyncBuilder.Instance.SpeakingNpc == __instance)
            {
                return;
            }

            var currentDialogue = __instance.CurrentDialogue;
            if (currentDialogue == null || currentDialogue.Count == 0)
            {
                return;
            }

            var vanillaDialogue = currentDialogue.Peek();
            if (vanillaDialogue?.dialogues == null || vanillaDialogue.dialogues.Count == 0)
            {
                return;
            }

            if (vanillaDialogue.dialogues.First().Text == SldConstants.DialogueGenerationTag)
            {
                return;
            }

            string originalLine = string.Join(
                " ",
                vanillaDialogue.dialogues
                    .Select(line => line.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
            );
            string generatedDialogueText = string.IsNullOrWhiteSpace(originalLine)
                ? SldConstants.DialogueGenerationTag
                : $"{SldConstants.DialogueGenerationTag}#{originalLine}";
            string dialogueKey = !string.IsNullOrWhiteSpace(vanillaDialogue.temporaryDialogueKey)
                ? vanillaDialogue.temporaryDialogueKey
                : $"{(noPreface ? "default" : "heart")}_{heartLevel}";

            currentDialogue.Pop();
            currentDialogue.Push(new Dialogue(__instance, dialogueKey, generatedDialogueText)
            {
                removeOnNextMove = vanillaDialogue.removeOnNextMove,
                temporaryDialogueKey = vanillaDialogue.temporaryDialogueKey
            });
            ModEntry.SMonitor.Log($"NPC {__instance.Name} replaced normal right-click dialogue with AI generation placeholder.", StardewModdingAPI.LogLevel.Trace);
        }
    }
}
