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
/// P11（§4.2.11 + 裁决 1 方案 B）：对话真正显示前的集中拦截，两个补丁共享一套判定：
/// 跳过标记吞显（配合 P4 静默送礼）；生成占位改道异步生成（思考窗接管显示）；
/// 其余真台词交给 <see cref="DisplayedDialogueRecorder"/> 记录（日常/事件/旁听）。
/// </summary>
internal static class DialogueDisplayInterceptor
{
    /// <summary>引擎即将自行呈现的对白（GenerationScheduler.Draw 握手）：显示放行且不再记录
    /// （生成台词的历史由 EngineHistoryWriter 负责，重复记录会污染转录）。主线程读写。</summary>
    private static GameDialogue? enginePresented;

    public static void MarkEnginePresented(GameDialogue dialogue)
    {
        enginePresented = dialogue;
    }

    /// <summary>
    /// 显示前判定。返回 false = 抑制本次显示（跳过标记 / 已改道生成）；
    /// 返回 true = 照常显示（dialogue 可能已被替换为 Android 断网等待对白）。
    /// </summary>
    public static bool BeforeDisplay(ref GameDialogue dialogue)
    {
        if (dialogue != null && ReferenceEquals(dialogue, enginePresented))
        {
            enginePresented = null;
            return true;
        }

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

/// <summary>P11a：DrawDialogue(Dialogue) 重载（思考窗、DrawDialogue(NPC,String) 等路径汇入点）。</summary>
[HarmonyPatch(typeof(Game1), nameof(Game1.DrawDialogue), new[] { typeof(GameDialogue) })]
internal static class Game1_DrawDialogue_Patch
{
    public static bool Prefix(ref GameDialogue dialogue)
    {
        try
        {
            return DialogueDisplayInterceptor.BeforeDisplay(ref dialogue);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"Game1.DrawDialogue patch failed: {ex}", LogLevel.Warn);
            return true;
        }
    }
}

/// <summary>
/// P11b：drawDialogue(NPC) 重载。普通右键对话（NPC.checkAction）与送礼反应
/// （NPC.tryToReceiveActiveObject）都走这条路，它不经过 DrawDialogue(Dialogue) 而是
/// 直接 new DialogueBox(CurrentDialogue.Peek())——不拦这里，生成占位/跳过标记会原样画出
/// （0.2.0 冒烟 Bug：对话框显示 "$$$%%%"，$ 渲染成硬币图形）。
/// </summary>
[HarmonyPatch(typeof(Game1), nameof(Game1.drawDialogue), new[] { typeof(NPC) })]
internal static class Game1_drawDialogue_Npc_Patch
{
    public static bool Prefix(NPC speaker)
    {
        try
        {
            if (speaker?.CurrentDialogue == null || speaker.CurrentDialogue.Count == 0)
            {
                return true;
            }

            GameDialogue top = speaker.CurrentDialogue.Peek();
            GameDialogue replaced = top;
            bool display = DialogueDisplayInterceptor.BeforeDisplay(ref replaced);
            if (!display)
            {
                // 抑制显示时把被抑制的对白弹掉，避免残留栈顶反复触发
                // （改道生成路径已在 InterceptPlaceholder 里清栈，这里只处理跳过标记）。
                if (speaker.CurrentDialogue.Count > 0 && ReferenceEquals(speaker.CurrentDialogue.Peek(), top))
                {
                    speaker.CurrentDialogue.Pop();
                }

                return false;
            }

            // Android 断网替换对白已由 InterceptPlaceholder 压回栈顶，原方法 Peek 即得。
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"Game1.drawDialogue(NPC) patch failed: {ex}", LogLevel.Warn);
            return true;
        }
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
