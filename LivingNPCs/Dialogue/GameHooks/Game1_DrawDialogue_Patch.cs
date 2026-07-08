using System;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Ui;
using GameDialogue = StardewValley.Dialogue;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// P11（§4.2.11 + 裁决 1 方案 B）：对话真正显示前的集中拦截。
/// 跳过标记吞显（配合 P4 静默送礼）；生成占位改道异步生成（思考窗接管显示）；
/// 其余真台词交给 <see cref="DisplayedDialogueRecorder"/> 记录（日常/事件/旁听）。
/// </summary>
[HarmonyPatch(typeof(Game1), nameof(Game1.DrawDialogue), new[] { typeof(GameDialogue) })]
internal static class Game1_DrawDialogue_Patch
{
    public static bool Prefix(ref GameDialogue dialogue)
    {
        try
        {
            var lines = dialogue?.dialogues;
            if (dialogue == null || lines == null || lines.Count == 0)
            {
                return true;
            }

            string firstLine = lines[0]?.Text ?? string.Empty;
            if (firstLine.StartsWith(EngineConstants.DialogueSkipTag, StringComparison.Ordinal))
            {
                return false;
            }

            NPC speaker = dialogue.speaker;
            if (speaker == null)
            {
                return true;
            }

            // UI 壳（思考中 / 输入框）与引擎错误兜底（...）：照常显示、不改道不记录。
            if (ThinkingDialogueController.IsThinkingDialogue(dialogue)
                || string.Equals(dialogue.TranslationKey, EngineConstants.KeyInput, StringComparison.Ordinal)
                || string.Equals(dialogue.TranslationKey, EngineConstants.KeyError, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(firstLine, EngineConstants.DialogueGenerationTag, StringComparison.Ordinal))
            {
                GameDialogue? replacement = InterceptPlaceholder(speaker, dialogue);
                if (replacement == null)
                {
                    // 改道异步生成：本次不显示，思考窗 → 生成结果接管（§4.5）。
                    return false;
                }

                // Android 断网：改画 ... 等待对白。
                dialogue = replacement;
                return true;
            }

            DisplayedDialogueRecorder.Record(speaker, dialogue);
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"Game1.DrawDialogue patch failed: {ex}", LogLevel.Warn);
            return true;
        }
    }

    /// <summary>占位改道（§4.2.2 第 4 步）：弹掉占位；断网压 ... 对白；否则以 # 后续行为参考发起基础生成并清栈。</summary>
    private static GameDialogue? InterceptPlaceholder(NPC speaker, GameDialogue placeholder)
    {
        DialogueUiStateGuard.RemoveDialogue(speaker, placeholder);

        if (AndroidPlatform.IsAndroid && !NetworkGate.IsAvailableForGeneration())
        {
            var wait = new GameDialogue(speaker, placeholder.TranslationKey, EngineConstants.FallbackLine);
            speaker.CurrentDialogue.Push(wait);
            return wait;
        }

        string reference = DialoguePatchHelpers.JoinDialogueLines(placeholder.dialogues.Skip(1));
        string dialogueKey = DialoguePatchHelpers.ResolvePlaceholderKey(placeholder);
        GenerationRequests.Enqueue(speaker, GenerationRequests.BuildScheduled(speaker, dialogueKey, reference));
        Game1.currentSpeaker = speaker;
        speaker.CurrentDialogue.Clear();
        return null;
    }
}

/// <summary>P12（§4.2.12）：空对白栈残留 UI 守卫，转调搬运件。
/// 目标为 Game1 的私有实例方法 drawDialogueBox()（无参重载；§3 表注"静态"与实际不符，以程序集为准）。</summary>
[HarmonyPatch(typeof(Game1), "drawDialogueBox", new Type[0])]
internal static class Game1_DrawDialogueBox_Patch
{
    public static bool Prefix()
    {
        try
        {
            return DialogueUiStateGuard.TrySkipEmptySpeakerDraw();
        }
        catch
        {
            return true;
        }
    }
}
