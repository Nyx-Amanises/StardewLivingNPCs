using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Ui;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>P1/P3/P11/P13 共用的小件：触发键查询、输入会话发起、占位文本与键解析。</summary>
internal static class DialoguePatchHelpers
{
    /// <summary>配置触发键当前是否按下（补丁内即时查询，§4.3 不订 ButtonPressed）。</summary>
    public static bool IsTriggerKeyDown()
    {
        var triggerKey = DialogueServices.Config?.InitiateTypedDialogueKey ?? SButton.None;
        if (triggerKey == SButton.None)
        {
            return false;
        }

        return DialogueServices.Helper?.Input.IsDown(triggerKey) == true;
    }

    /// <summary>主动搭话共通动作（P1/P13 前半）：清空上次会话上下文并提交输入请求。</summary>
    public static void BeginTypedConversation(NPC npc)
    {
        DialogueEngineHost.Instance?.History.ClearContext();
        string prompt = Util.GetString("uiStartConversation", new { Name = npc.displayName }, returnNull: true)
            ?? "What do you want to say?";
        TypedInputRequestQueue.Submit(prompt, npc, dialogueKey: string.Empty, conversation: null);
    }

    /// <summary>占位对白文本：占位标记，原文非空则以 # 携带（§4.2.3）。</summary>
    public static string BuildPlaceholderText(string original)
    {
        return string.IsNullOrWhiteSpace(original)
            ? EngineConstants.DialogueGenerationTag
            : $"{EngineConstants.DialogueGenerationTag}#{original}";
    }

    /// <summary>对白全部非空行用空格连接（占位改写的"原版参考台词"）。</summary>
    public static string JoinDialogueLines(IEnumerable<DialogueLine?>? lines)
    {
        if (lines == null)
        {
            return string.Empty;
        }

        return string.Join(" ", lines
            .Select(line => line?.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    /// <summary>
    /// 占位对白 → 生成请求对话键：temporaryDialogueKey 优先，其次 TranslationKey，
    /// 均空用 default（P3/P7/P8/P10 构造的键经此透传给引擎的键文法）。
    /// </summary>
    public static string ResolvePlaceholderKey(GameDialogue placeholder)
    {
        return ResolvePlaceholderKey(placeholder.temporaryDialogueKey, placeholder.TranslationKey);
    }

    internal static string ResolvePlaceholderKey(string? temporaryDialogueKey, string? translationKey)
    {
        if (!string.IsNullOrWhiteSpace(temporaryDialogueKey))
        {
            return temporaryDialogueKey;
        }

        if (!string.IsNullOrWhiteSpace(translationKey))
        {
            return translationKey;
        }

        return "default";
    }
}
