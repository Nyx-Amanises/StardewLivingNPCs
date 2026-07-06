using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using xTile.Dimensions;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
namespace LivingNPCs.Dialogue.GameHooks
{
    [HarmonyPatch(typeof(Event), nameof(Event.checkAction))]
    public class Event_CheckAction_Patch
    {
        public static bool Prefix(Event __instance, ref bool __result, Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
        {
            var triggerKey = DialogueServices.Config.InitiateTypedDialogueKey;
            bool wasTriggerKeyDown = triggerKey != SButton.None && DialogueServices.Helper.Input.IsDown(triggerKey);
            if (!wasTriggerKeyDown || who?.CanMove != true || __instance?.actors == null)
            {
                return true;
            }

            var clickedTile = new Vector2(tileLocation.X, tileLocation.Y);
            var npc = __instance.actors
                .FirstOrDefault(actor =>
                    actor != null
                    && !actor.IsInvisible
                    && !actor.isSleeping.Value
                    && Vector2.Distance(actor.Tile, clickedTile) <= 1.25f
                );
            if (npc == null || !DialogueBuilder.Instance.PatchNpc(npc))
            {
                return true;
            }

            DialogueBuilder.Instance.ClearContext();
            var character = DialogueBuilder.Instance.GetCharacter(npc);
            var prompt = Util.GetString(
                character,
                "uiStartConversation",
                new { Name = npc.displayName },
                returnNull: true
            ) ?? "What do you want to say?";

            TextInputManager.RequestTextInput(prompt, npc);
            __result = true;
            return false;
        }
    }
}
