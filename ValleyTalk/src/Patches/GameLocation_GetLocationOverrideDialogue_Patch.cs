using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace ValleyTalk
{
    [HarmonyPatch(typeof(GameLocation), nameof(GameLocation.GetLocationOverrideDialogue))]
    public class GameLocation_GetLocationOverrideDialogue_Patch
    {
        public static bool Prefix(ref GameLocation __instance, ref string __result, NPC character)
        {
            ModEntry.SMonitor.Log($"GameLocation.GetLocationOverrideDialogue called for {character?.Name} in {__instance.Name}", StardewModdingAPI.LogLevel.Trace);
            if (character == null)
            {
                return true;
            }

            var triggerKey = ModEntry.Config.InitiateTypedDialogueKey;
            bool wasTriggerKeyDown = triggerKey != SButton.None && ModEntry.SHelper.Input.IsDown(triggerKey);
            if (wasTriggerKeyDown
                && !character.IsInvisible
                && !character.isSleeping.Value
                && Game1.player?.CanMove == true
                && DialogueBuilder.Instance.PatchNpc(character))
            {
                DialogueBuilder.Instance.ClearContext();
                var valleyTalkCharacter = DialogueBuilder.Instance.GetCharacter(character);
                var prompt = Util.GetString(
                    valleyTalkCharacter,
                    "uiStartConversation",
                    new { Name = character.displayName },
                    returnNull: true
                ) ?? "What do you want to say?";
                TextInputManager.RequestTextInput(prompt, character);
                __result = string.Empty;
                return false;
            }

            if (!DialogueBuilder.Instance.PatchPassiveNpc(character, ModEntry.Config.GeneralFrequency, true))
            {
                return true;
            }
            __result = SldConstants.DialogueGenerationTag;
            return false;
        }
    }
}
