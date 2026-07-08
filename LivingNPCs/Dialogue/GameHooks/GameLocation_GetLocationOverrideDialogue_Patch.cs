using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>P13（§4.2.13）双重角色：触发键按下时从地点覆盖台词入口发起输入（与 P1 先到先得）；
/// 否则被动门控通过时返回生成占位串（上游构造对白后经 P11 改道）。</summary>
[HarmonyPatch(typeof(GameLocation), nameof(GameLocation.GetLocationOverrideDialogue))]
internal static class GameLocation_GetLocationOverrideDialogue_Patch
{
    public static bool Prefix(NPC character, ref string __result)
    {
        try
        {
            if (character == null)
            {
                return true;
            }

            // 前半：主动输入。
            if (DialoguePatchHelpers.IsTriggerKeyDown()
                && !character.IsInvisible
                && !character.isSleeping.Value
                && Game1.player?.CanMove == true
                && PatchGuards.AllowInteractiveInput(character))
            {
                DialoguePatchHelpers.BeginTypedConversation(character);
                __result = string.Empty;
                return false;
            }

            // 后半：被动占位。
            if (!PatchGuards.AllowPassiveGeneration(character, GenerationFrequencyKind.General, checkNetwork: false))
            {
                return true;
            }

            __result = EngineConstants.DialogueGenerationTag;
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"GameLocation.GetLocationOverrideDialogue patch failed: {ex}", LogLevel.Warn);
            return true;
        }
    }
}
