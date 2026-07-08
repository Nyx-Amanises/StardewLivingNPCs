using System;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>P4（§4.2.4）：送礼反应改走异步生成；原版拿到跳过标记对白，P11 吞掉显示，
/// 好感与每日送礼计数仍由原版逻辑处理。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.GetGiftReaction))]
internal static class NPC_GetGiftReaction_Patch
{
    public static bool Prefix(NPC __instance, Farmer giver, StardewValley.Object gift, int taste, ref GameDialogue __result)
    {
        try
        {
            if (GenerationRequests.SchedulerBusy)
            {
                return true;
            }

            if (!PatchGuards.AllowGiftGeneration(__instance))
            {
                return true;
            }

            var request = GenerationRequests.BuildGift(__instance, gift, taste);
            if (!GenerationRequests.Enqueue(__instance, request))
            {
                return true;
            }

            var skip = new GameDialogue(__instance, null, EngineConstants.DialogueSkipTag);
            skip.exitCurrentDialogue();
            __result = skip;
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"NPC.GetGiftReaction patch failed: {ex}", LogLevel.Warn);
            return true;
        }
    }
}

/// <summary>P6（§4.2.6）：临时台词（度假村等）替换为生成占位。压入同键占位后仍放行原方法——
/// 原版见栈顶键相同会跳过自己的压栈，等效替换；异常时抑制原方法（保守吞错）。</summary>
[HarmonyPatch(typeof(NPC), "_PushTemporaryDialogue")]
internal static class NPC_PushTemporaryDialogue_Patch
{
    public static bool Prefix(NPC __instance, ref string translationKey)
    {
        try
        {
            if (!PatchGuards.AllowPassiveGeneration(__instance, GenerationFrequencyKind.General, checkNetwork: true))
            {
                return true;
            }

            translationKey = RemapResortMarriageKey(translationKey);

            var stack = __instance.CurrentDialogue;
            if (stack.Count > 0 && string.Equals(stack.Peek().temporaryDialogueKey, translationKey, StringComparison.Ordinal))
            {
                return true;
            }

            string original;
            try
            {
                original = Game1.content.LoadString(translationKey);
            }
            catch
            {
                original = string.Empty;
            }

            var dialogue = new GameDialogue(__instance, translationKey, DialoguePatchHelpers.BuildPlaceholderText(original))
            {
                removeOnNextMove = true,
                temporaryDialogueKey = translationKey
            };
            stack.Push(dialogue);
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"NPC._PushTemporaryDialogue patch failed: {ex}", LogLevel.Warn);
            return false;
        }
    }

    /// <summary>已婚 NPC 度假村台词键重映射：存在 Resort_Marriage + 原键后缀的资源时改用之。</summary>
    private static string RemapResortMarriageKey(string translationKey)
    {
        if (string.IsNullOrEmpty(translationKey) || !translationKey.StartsWith("Resort", StringComparison.Ordinal))
        {
            return translationKey;
        }

        string candidate = "Resort_Marriage" + translationKey["Resort".Length..];
        try
        {
            return Game1.content.LoadStringReturnNullIfNotFound(candidate) != null ? candidate : translationKey;
        }
        catch
        {
            return translationKey;
        }
    }
}

/// <summary>P7（§4.2.7）：婚后特定键台词（funReturn_/jobReturn_）占位。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.tryToGetMarriageSpecificDialogue))]
internal static class NPC_TryToGetMarriageSpecificDialogue_Patch
{
    public static bool Prefix(NPC __instance, string dialogueKey, ref GameDialogue __result)
    {
        try
        {
            bool isMarriageReturnKey = dialogueKey != null
                && (dialogueKey.StartsWith("funReturn_", StringComparison.Ordinal)
                    || dialogueKey.StartsWith("jobReturn_", StringComparison.Ordinal));
            if (!isMarriageReturnKey)
            {
                return true;
            }

            if (!PatchGuards.AllowPassiveGeneration(__instance, GenerationFrequencyKind.Marriage, checkNetwork: true))
            {
                return true;
            }

            __result = new GameDialogue(__instance, dialogueKey, EngineConstants.DialogueGenerationTag);
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"NPC.tryToGetMarriageSpecificDialogue patch failed: {ex}", LogLevel.Warn);
            return true;
        }
    }
}

/// <summary>P8（§4.2.8）：常规检索台词整体占位（键 = preface_heartLevel）。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.tryToRetrieveDialogue))]
internal static class NPC_TryToRetrieveDialogue_Patch
{
    public static bool Prefix(NPC __instance, string preface, int heartLevel, string appendToEnd, ref GameDialogue __result)
    {
        try
        {
            if (!PatchGuards.AllowPassiveGeneration(__instance, GenerationFrequencyKind.General, checkNetwork: true))
            {
                return true;
            }

            __result = new GameDialogue(__instance, $"{preface}_{heartLevel}", EngineConstants.DialogueGenerationTag);
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"NPC.tryToRetrieveDialogue patch failed: {ex}", LogLevel.Warn);
            return true;
        }
    }
}
