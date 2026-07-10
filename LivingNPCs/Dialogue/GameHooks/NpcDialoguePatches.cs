using System;
using System.Collections.Generic;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>P1（§4.2.1）：按住触发键点击 NPC 时发起自由输入。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.checkAction))]
internal static class NPC_CheckAction_Patch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("ApryllForever.PolyamorySweetKiss", "Digus.CustomKissingMod")]
    public static bool Prefix(NPC __instance, Farmer who, GameLocation l, ref bool __result)
    {
        try
        {
            // 先清理上次生成异常残留的"思考中"占位对白。
            ThinkingDialogueController.RemoveStale(__instance);

            if (!DialoguePatchHelpers.IsTriggerKeyDown())
            {
                return true;
            }

            bool sleepBlocked = __instance.isSleeping.Value
                && DialogueServices.Config?.AllowWakeSleepingNpc != true;
            if (__instance.IsInvisible || sleepBlocked || who?.CanMove != true)
            {
                DialogueServices.Monitor?.Log(
                    I18n.Get(
                        "log.dialogue.typedBlocked",
                        new
                        {
                            npc = __instance.Name,
                            invisible = __instance.IsInvisible,
                            sleepBlocked,
                            farmerCanMove = who?.CanMove
                        }),
                    LogLevel.Trace);
                return true;
            }

            if (!PatchGuards.AllowInteractiveInput(__instance))
            {
                return true;
            }

            DialoguePatchHelpers.BeginTypedConversation(__instance);
            // 置 false 并跳过原方法可阻止原版对话弹出（§4.2.1 实测语义）。
            __result = false;
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.patchFailed",
                    new { patch = "NPC.checkAction", error = ex },
                    $"NPC.checkAction patch failed: {ex}"),
                LogLevel.Warn);
            return true;
        }
    }
}

/// <summary>P2（裁决 1 方案 B 后的残余职责）：清栈守卫——弹掉思考控制器已不认账的占位对白。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.CurrentDialogue), MethodType.Getter)]
internal static class NPC_CurrentDialogue_Patch
{
    public static void Postfix(NPC __instance, Stack<GameDialogue> __result)
    {
        try
        {
            if (__result == null || __result.Count == 0)
            {
                return;
            }

            ThinkingDialogueController.TryDiscardInactiveTop(__instance, __result);
        }
        catch
        {
            // 守卫失败不影响原返回值。
        }
    }
}

/// <summary>P3（§4.2.3）：新产生的常规台词替换为生成占位（原文以 # 携带作参考）。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.checkForNewCurrentDialogue))]
internal static class NPC_CheckForNewCurrentDialogue_Patch
{
    public static void Postfix(NPC __instance, int heartLevel, bool noPreface, bool __result)
    {
        try
        {
            if (!__result || GenerationRequests.SchedulerBusy)
            {
                return;
            }

            if (!PatchGuards.AllowPassiveGeneration(__instance, GenerationFrequencyKind.General, checkNetwork: true))
            {
                return;
            }

            var stack = __instance.CurrentDialogue;
            if (stack == null || stack.Count == 0)
            {
                return;
            }

            GameDialogue top = stack.Peek();
            var lines = top?.dialogues;
            if (top == null || lines == null || lines.Count == 0)
            {
                return;
            }

            string firstLine = lines[0]?.Text ?? string.Empty;
            if (string.IsNullOrEmpty(firstLine) || string.Equals(firstLine, EngineConstants.DialogueGenerationTag, StringComparison.Ordinal))
            {
                return;
            }

            string original = DialoguePatchHelpers.JoinDialogueLines(lines);
            string key = BuildScheduledKey(top.temporaryDialogueKey, heartLevel, noPreface);
            stack.Pop();
            var replacement = new GameDialogue(__instance, key, DialoguePatchHelpers.BuildPlaceholderText(original))
            {
                removeOnNextMove = top.removeOnNextMove,
                temporaryDialogueKey = top.temporaryDialogueKey
            };
            stack.Push(replacement);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.patchFailed",
                    new { patch = "NPC.checkForNewCurrentDialogue", error = ex },
                    $"NPC.checkForNewCurrentDialogue patch failed: {ex}"),
                LogLevel.Warn);
        }
    }

    /// <summary>对话键构造（§4.2.3，纯函数）：temporaryDialogueKey 优先，否则 heart/default_{心数}。</summary>
    internal static string BuildScheduledKey(string? temporaryDialogueKey, int heartLevel, bool noPreface)
    {
        if (!string.IsNullOrEmpty(temporaryDialogueKey))
        {
            return temporaryDialogueKey;
        }

        return $"{(noPreface ? "default" : "heart")}_{heartLevel}";
    }
}
