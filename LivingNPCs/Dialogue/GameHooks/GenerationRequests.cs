using System;
using System.Collections.Generic;
using StardewValley;

using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Dialogue.GameHooks;

/// <summary>
/// 补丁 → 引擎的请求装配（WP12 §6）：快照在主线程采集（触发时刻），
/// 行为系统上下文（WP16 方向反转）经注入的委托在同一时刻装入 BehaviorContext。
/// </summary>
internal static class GenerationRequests
{
    /// <summary>行为上下文注入点（对话触发）：WP16/ModEntry 装配；null = 无行为系统。</summary>
    public static Func<NPC, string>? ConversationContextProvider { get; set; }

    /// <summary>行为上下文注入点（送礼触发）：(npc, itemId, displayName, taste) → 上下文。</summary>
    public static Func<NPC, string, string, int, string>? GiftContextProvider { get; set; }

    /// <summary>单飞行判定（P3/P4/P14 的"已有生成在途"检查）。</summary>
    public static bool SchedulerBusy => AsyncBuilder.Instance.Scheduler?.IsBusy == true;

    /// <summary>入队一次生成；调度器未装配或在途时丢弃（调度器自行记警告）。</summary>
    public static bool Enqueue(NPC npc, GenerationRequest request)
    {
        return AsyncBuilder.Instance.Scheduler?.Enqueue(npc, request) == true;
    }

    /// <summary>被动占位改道的基础生成（P11 拦截占位后调用；婚后键同走此路，靠键解析）。</summary>
    public static GenerationRequest BuildScheduled(NPC npc, string dialogueKey, string originalLine)
    {
        return new GenerationRequest
        {
            NpcName = npc.Name,
            Trigger = GenerationTrigger.Scheduled,
            DialogueKey = dialogueKey ?? string.Empty,
            OriginalLine = originalLine ?? string.Empty,
            BehaviorContext = SafeConversationContext(npc),
            Snapshot = GameStateSnapshotCollector.Collect(npc)
        };
    }

    /// <summary>玩家会话生成（输入框提交 / P9 文本选项）。</summary>
    public static GenerationRequest BuildConversation(NPC npc, string dialogueKey, IReadOnlyList<ConversationTurn> conversation)
    {
        return new GenerationRequest
        {
            NpcName = npc.Name,
            Trigger = GenerationTrigger.Conversation,
            DialogueKey = dialogueKey ?? string.Empty,
            Conversation = conversation,
            BehaviorContext = SafeConversationContext(npc),
            Snapshot = GameStateSnapshotCollector.Collect(npc)
        };
    }

    /// <summary>送礼反应生成（P4：礼物与口味原样透传）。</summary>
    public static GenerationRequest BuildGift(NPC npc, StardewValley.Object gift, int taste)
    {
        string itemId = gift?.QualifiedItemId ?? string.Empty;
        string displayName = gift?.DisplayName ?? string.Empty;
        return new GenerationRequest
        {
            NpcName = npc.Name,
            Trigger = GenerationTrigger.Gift,
            GiftItemId = itemId,
            GiftTaste = taste,
            BehaviorContext = SafeGiftContext(npc, itemId, displayName, taste),
            Snapshot = GameStateSnapshotCollector.Collect(npc)
        };
    }

    private static string SafeConversationContext(NPC npc)
    {
        try
        {
            return ConversationContextProvider?.Invoke(npc) ?? string.Empty;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.behaviorContextFailed", new { npc = npc.Name, error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
            return string.Empty;
        }
    }

    private static string SafeGiftContext(NPC npc, string itemId, string displayName, int taste)
    {
        try
        {
            return GiftContextProvider?.Invoke(npc, itemId, displayName, taste) ?? string.Empty;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.giftContextFailed", new { npc = npc.Name, error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
            return string.Empty;
        }
    }
}
