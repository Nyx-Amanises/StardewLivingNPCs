using HarmonyLib;
using StardewValley;
using StardewModdingAPI;

namespace ValleyTalk
{
    /// <summary>
    /// Patch for NPC.checkAction to allow initiating a conversation with typed dialogue
    /// </summary>
    [HarmonyPatch(typeof(NPC), nameof(NPC.checkAction))]
    public class NPC_CheckAction_Patch
    {
        /// <summary>
        /// Prefix method for NPC.checkAction
        /// </summary>
        public static bool Prefix(ref NPC __instance, ref bool __result, Farmer who, GameLocation l)
        {
            // Check if the configured key is being held down while clicking the NPC.
            var triggerKey = ModEntry.Config.InitiateTypedDialogueKey;
            bool wasTriggerKeyDown = triggerKey != SButton.None && ModEntry.SHelper.Input.IsDown(triggerKey);

            // Check for cases when we should not allow initiating typed dialogue
            if (
                __instance.IsInvisible ||
                __instance.isSleeping.Value ||
                !who.CanMove ||
                !wasTriggerKeyDown ||
                !DialogueBuilder.Instance.PatchNpc(__instance)
                )
            {
                return true;
            }

            DialogueBuilder.Instance.ClearContext();
            var character = DialogueBuilder.Instance.GetCharacter(__instance);  
            var prompt = Util.GetString(character, "uiStartConversation", new { Name = __instance.displayName }, returnNull: true) ?? "你想说什么？";
            // Show text entry dialog for the player to type their dialogue
            TextInputManager.RequestTextInput
            (
                prompt,
                __instance
            );
            __result = false;
            return false; // Prevent the original method from executing
        }
    }
}
