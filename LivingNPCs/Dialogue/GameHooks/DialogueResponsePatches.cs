using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>P9（§4.2.9）：接管本 mod 生成的应答选项组。任一选项键不带 SLD_ 前缀即放行，
/// 保证原版问答/事件选择完全不受影响。裁决 6：省略 finishedLastDialogue 私有字段反射。</summary>
[HarmonyPatch(typeof(GameDialogue), nameof(GameDialogue.chooseResponse))]
internal static class Dialogue_ChooseResponse_Patch
{
    public static bool Prefix(GameDialogue __instance, Response response, ref bool __result)
    {
        try
        {
            NPC speaker = __instance.speaker;
            var engine = DialogueEngineHost.Instance;
            if (speaker == null || response == null || engine == null)
            {
                return true;
            }

            if (!PatchGuards.IsEnabledFor(speaker))
            {
                return true;
            }

            Response[] options = __instance.getResponseOptions();
            if (options == null || options.Length == 0)
            {
                return true;
            }

            foreach (Response option in options)
            {
                if (option?.responseKey?.StartsWith(EngineConstants.KeyPrefix, StringComparison.Ordinal) != true)
                {
                    return true;
                }
            }

            string chosenKey = response.responseKey ?? string.Empty;

            // 静默：记一条空玩家台词，对话正常关闭。
            if (string.Equals(chosenKey, EngineConstants.KeySilent, StringComparison.Ordinal))
            {
                engine.History.AppendToConversationContext(
                    speaker.Name, new ConversationTurn(string.Empty, true, NewTurnId()));
                __result = true;
                return false;
            }

            // 清理尾部"请回应"引导行。引擎生成的 NPC 原始台词已由 FinishTrigger 写入
            // recentContext；不能再把 Stardew 拆屏后的展示行写入，否则同一句会以完整行和
            // 页面行两种形式重复进入后续提示词。
            string respondPrompt = Util.GetString("outputRespond", returnNull: true) ?? "Respond:";
            RemoveTrailingRespondLine(__instance.dialogues, respondPrompt);

            string dialogueKey = string.IsNullOrWhiteSpace(__instance.TranslationKey)
                ? "default"
                : __instance.TranslationKey;

            // 自己输入：转输入请求队列，关闭当前对话交给输入框。
            if (string.Equals(chosenKey, EngineConstants.KeyTypedResponse, StringComparison.Ordinal))
            {
                string prompt = Util.GetString("uiYourResponse", returnNull: true) ?? "Your response";
                TypedInputRequestQueue.Submit(
                    prompt, speaker, dialogueKey, engine.History.PeekConversationContext(speaker.Name));
                __result = true;
                return false;
            }

            // 普通文本选项：以聊天历史 + 选项文本（玩家台词）发起会话生成。
            var playerTurn = new ConversationTurn(response.responseText ?? string.Empty, true, NewTurnId());
            var request = GenerationRequests.BuildConversation(speaker, dialogueKey, new[] { playerTurn });
            GenerationRequests.Enqueue(speaker, request);
            __result = true;
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.patchFailed",
                    new { patch = "Dialogue.chooseResponse", error = ex },
                    $"Dialogue.chooseResponse patch failed: {ex}"),
                LogLevel.Warn);
            return true;
        }
    }

    /// <summary>对白最后一行等于"请回应"引导行时移除（WP10 在生成对白尾部追加的行）。</summary>
    internal static void RemoveTrailingRespondLine(List<DialogueLine>? lines, string respondPrompt)
    {
        if (lines == null || lines.Count == 0 || string.IsNullOrWhiteSpace(respondPrompt))
        {
            return;
        }

        string? last = lines[^1]?.Text;
        if (last != null && string.Equals(last.Trim(), respondPrompt.Trim(), StringComparison.Ordinal))
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static string NewTurnId()
    {
        return Guid.NewGuid().ToString("N");
    }
}

/// <summary>P10（§4.2.10）：雨天台词占位（静态目标，无 __instance）。</summary>
[HarmonyPatch(typeof(GameDialogue), nameof(GameDialogue.TryGetDialogue), new[] { typeof(NPC), typeof(string) })]
internal static class Dialogue_TryGetDialogue_Patch
{
    private const string RainyKeyPrefix = @"Characters\Dialogue\rainy:";

    public static bool Prefix(NPC speaker, string translationKey, ref GameDialogue __result)
    {
        try
        {
            if (translationKey?.StartsWith(RainyKeyPrefix, StringComparison.Ordinal) != true)
            {
                return true;
            }

            if (!PatchGuards.AllowPassiveGeneration(speaker, GenerationFrequencyKind.General, checkNetwork: false))
            {
                return true;
            }

            __result = new GameDialogue(speaker, translationKey, EngineConstants.DialogueGenerationTag);
            return false;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.patchFailed",
                    new { patch = "Dialogue.TryGetDialogue", error = ex },
                    $"Dialogue.TryGetDialogue patch failed: {ex}"),
                LogLevel.Warn);
            return true;
        }
    }
}
