using System;
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
        /// <summary>事件内 NPC 可交互距离：与 DialoguePatchHelpers.TryFindNpcForInteraction 的抓取阈值一致（F10）。</summary>
        internal const float MaxEventInteractTileDistance = 1.5f;

        public static bool Prefix(Event __instance, ref bool __result, Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
        {
            try
            {
                if (!DialoguePatchHelpers.IsTriggerKeyDown() || who?.CanMove != true || __instance?.actors == null)
                {
                    return true;
                }

                bool allowSleeping = DialogueServices.Config?.AllowWakeSleepingNpc == true;
                var clickedTile = new Vector2(tileLocation.X, tileLocation.Y);
                var npc = __instance.actors
                    .FirstOrDefault(actor =>
                        actor != null
                        && NpcInteractionEligibility.IsEligible(actor)
                        && !actor.IsInvisible
                        && (allowSleeping || !actor.isSleeping.Value)
                        && Vector2.Distance(actor.Tile, clickedTile) <= MaxEventInteractTileDistance
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
            catch (Exception ex)
            {
                // 兜底口径与其余 prefix 对齐（F10）：补丁异常绝不打断事件交互，放行原方法。
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.patchFailed",
                        new { patch = "Event.checkAction", error = ex },
                        $"Event.checkAction patch failed: {ex}"),
                    LogLevel.Warn);
                return true;
            }
        }
    }
}
