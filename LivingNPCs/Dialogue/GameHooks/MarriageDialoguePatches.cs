using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// P5 的"并入下次婚后台词"缓冲（§4.2.5/§4.2.14）：晨间家务播报原文暂存，
/// 由 P14 消费后并入下一次婚后生成的参考台词。
/// </summary>
internal static class MarriageChoreBuffer
{
    private static readonly object gate = new();
    private static readonly List<string> lines = new();

    public static int Count
    {
        get
        {
            lock (gate)
            {
                return lines.Count;
            }
        }
    }

    public static void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (gate)
        {
            lines.Add(text);
        }
    }

    /// <summary>取走全部缓冲行并清空。</summary>
    public static List<string> Drain()
    {
        lock (gate)
        {
            var drained = new List<string>(lines);
            lines.Clear();
            return drained;
        }
    }

    internal static void Clear()
    {
        lock (gate)
        {
            lines.Clear();
        }
    }
}

/// <summary>P5（§4.2.5）：配偶家务台词过滤。未启用放行原版（修正旧实现的已知副作用）；
/// 启用时复刻原版效果，晨间播报键改为解析原文暂存，不单独弹出。</summary>
[HarmonyPatch(typeof(NPC), nameof(NPC.addMarriageDialogue))]
internal static class NPC_AddMarriageDialogue_Patch
{
    /// <summary>晨间家务播报键（§4.2.5 逐字清单）。</summary>
    internal static readonly HashSet<string> MorningChoreKeys = new(StringComparer.Ordinal)
    {
        "NPC.cs.4463",
        "NPC.cs.4462",
        "NPC.cs.4470",
        "NPC.cs.4474",
        "NPC.cs.4481",
        "MultiplePetBowls_watered"
    };

    public static bool Prefix(NPC __instance, string dialogue_file, string dialogue_key, bool gendered, string[] substitutions)
    {
        try
        {
            if (!PatchGuards.IsEnabledFor(__instance))
            {
                return true;
            }

            if (!MorningChoreKeys.Contains(dialogue_key ?? string.Empty))
            {
                // 复刻原版效果：标记该说婚后台词并追加引用。
                __instance.shouldSayMarriageDialogue.Value = true;
                __instance.currentMarriageDialogue.Add(
                    new MarriageDialogueReference(dialogue_file, dialogue_key, gendered, substitutions));
                return false;
            }

            string text = ResolveLocalizedText(__instance, dialogue_file, dialogue_key ?? string.Empty, gendered, substitutions);
            MarriageChoreBuffer.Add(text);
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.patchFailed",
                    new { patch = "NPC.addMarriageDialogue", error = ex },
                    $"NPC.addMarriageDialogue patch failed: {ex}"),
                LogLevel.Warn);
            return true;
        }
    }

    /// <summary>解析本地化原文（键 = 文件:键；gendered 走 LoadStringByGender），失败返回空（静默跳过）。</summary>
    internal static string ResolveLocalizedText(NPC npc, string dialogueFile, string dialogueKey, bool gendered, string[]? substitutions)
    {
        try
        {
            string lookup = $"{dialogueFile}:{dialogueKey}";
            object[] args = substitutions ?? Array.Empty<string>();
            return gendered
                ? Game1.LoadStringByGender(npc.Gender, lookup, args)
                : Game1.content.LoadString(lookup, args);
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>P14（§4.2.14 + 裁决 2）：婚后台词引用一律改异步占位（不再同步阻塞生成）；
/// 消费 P5 缓冲拼出参考台词，经 P11 的占位改道走统一异步管线。</summary>
[HarmonyPatch(typeof(MarriageDialogueReference), nameof(MarriageDialogueReference.GetDialogue))]
internal static class MarriageDialogueReference_GetDialogue_Patch
{
    public static bool Prefix(MarriageDialogueReference __instance, NPC n, ref GameDialogue __result)
    {
        try
        {
            if (GenerationRequests.SchedulerBusy)
            {
                return true;
            }

            if (!PatchGuards.AllowPassiveGeneration(n, GenerationFrequencyKind.Marriage, checkNetwork: false))
            {
                return true;
            }

            string reference = BuildReferenceText(__instance, n);
            __result = new GameDialogue(n, __instance.DialogueKey, DialoguePatchHelpers.BuildPlaceholderText(reference));
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.patchFailed",
                    new { patch = "MarriageDialogueReference.GetDialogue", error = ex },
                    $"MarriageDialogueReference.GetDialogue patch failed: {ex}"),
                LogLevel.Warn);
            return true;
        }
    }

    /// <summary>缓冲非空时把当前引用自身原文也解析追加（失败跳过），空格连接并清空缓冲。</summary>
    internal static string BuildReferenceText(MarriageDialogueReference reference, NPC npc)
    {
        if (MarriageChoreBuffer.Count == 0)
        {
            return string.Empty;
        }

        var buffered = MarriageChoreBuffer.Drain();
        string own = NPC_AddMarriageDialogue_Patch.ResolveLocalizedText(
            npc, reference.DialogueFile, reference.DialogueKey, reference.IsGendered, reference.Substitutions);
        if (!string.IsNullOrWhiteSpace(own))
        {
            buffered.Add(own);
        }

        return string.Join(" ", buffered.Where(line => !string.IsNullOrWhiteSpace(line)));
    }
}
