using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

/// <summary>
/// Chinese debug-display labels computed from a NPC's persisted <see cref="LivingNpcState"/>,
/// used by the in-game debug summary and markdown reports. English prompt wording lives in
/// <see cref="PromptFragments"/>; this class only formats state for human debugging.
/// </summary>
internal static class StateDebugLabels
{
    public static string Mood(LivingNpcState state) => state.Mood switch
    {
        "Aware" => "注意到周围",
        "Attentive" => "专注",
        "Calm" => "放松",
        "Careful" => "谨慎",
        "Chilly" => "有些怕冷",
        "Comfortable" => "自在",
        "CrowdedButWarm" => "亲近但有点频繁",
        "Curious" => "好奇",
        "Delighted" => "非常高兴",
        "Engaged" => "投入",
        "EventAware" => "留意活动气氛",
        "Expressive" => "情绪外露",
        "Familiar" => "熟悉",
        "Focused" => "专注于事务",
        "Fresh" => "精神不错",
        "Guarded" => "警觉",
        "Hurried" => "匆忙",
        "Overloaded" => "有点应接不暇",
        "Pleased" => "高兴",
        "Polite" => "礼貌克制",
        "Public" => "留意公共场合",
        "Quiet" => "安静",
        "Sociable" => "有社交兴致",
        "Surprised" => "有些意外",
        "Upset" => "不太高兴",
        "Awkward" => "有些尴尬",
        "GiftAware" => "注意到礼物",
        "Warm" => "温和",
        _ => "普通"
    };

    public static string Emotion(LivingNpcState state) => state.CurrentEmotion switch
    {
        "Happy" => $"开心（{state.EmotionIntensity}）",
        "Jealous" => $"吃醋（{state.EmotionIntensity}）",
        "Worried" => $"担心（{state.EmotionIntensity}）",
        "Grateful" => $"感激（{state.EmotionIntensity}）",
        "Disappointed" => $"失望（{state.EmotionIntensity}）",
        "Uneasy" => $"有些不自在（{state.EmotionIntensity}）",
        "Upset" => $"不悦（{state.EmotionIntensity}）",
        "Angry" => $"生气（{state.EmotionIntensity}）",
        "Sad" => $"伤心（{state.EmotionIntensity}）",
        _ => $"平静（{state.EmotionIntensity}）"
    };

    public static string Familiarity(LivingNpcState state) => state.Familiarity switch
    {
        >= 75 => "亲近",
        >= 45 => "熟悉",
        >= 18 => "眼熟",
        _ => "刚认识"
    };

    public static string Attention(LivingNpcState state) => state.Attention switch
    {
        >= 75 => "高",
        >= 45 => "中",
        _ => "低"
    };

    public static string Inclination(LivingNpcState state) => state.CurrentInclination switch
    {
        "Acknowledging" => "会简单回应",
        "Aware" => "注意到玩家",
        "Businesslike" => "偏事务性回应",
        "Careful" => "谨慎回应",
        "Comfortable" => "自在回应",
        "Focused" => "保持专注",
        "GentleBoundary" => "温和地保留空间",
        "Measured" => "礼貌但有分寸",
        "NeedsSpace" => "需要一点空间",
        "OpenToTalk" => "愿意继续回应",
        "Public" => "顾及周围的人",
        "Quiet" => "安静回应",
        "Reacting" => "正在反应",
        "Reconnecting" => "重新熟悉",
        "Reserved" => "保守回应",
        "Sheltering" => "想避开天气",
        _ => "普通"
    };

    public static string InteractionRhythm(LivingNpcState state) => state.InteractionRhythm switch
    {
        "AfterLongGap" => $"隔了 {state.LastConversationGapDays} 天才再次聊天",
        "AtComfortLimit" => $"今天第 {state.ConversationsToday} 次聊天，接近日常舒适上限",
        "BuildingRoutine" => $"连续 {state.ConsecutiveConversationDays} 天打招呼",
        "CheckedInAgain" => $"今天第 {state.ConversationsToday} 次聊天",
        "ComfortableRepeat" => $"今天第 {state.ConversationsToday} 次聊天，关系足够熟所以仍然自然",
        "CrowdedToday" => $"今天已经聊了 {state.ConversationsToday} 次，有点频繁",
        "DailyRoutine" => $"连续 {state.ConsecutiveConversationDays} 天聊天，像日常习惯",
        "FirstConversation" => "第一次记录到对话",
        "FreshToday" => "今天第一次聊天",
        "LongQuietGap" => $"已经 {state.LastConversationGapDays} 天没有聊天",
        "NoConversationToday" => state.LastConversationGapDays <= 1
            ? "今天还没聊天，昨天聊过"
            : $"今天还没聊天，上次聊天在 {state.LastConversationGapDays} 天前",
        "PoliteRepeat" => $"今天第 {state.ConversationsToday} 次聊天，关系还不深所以会更客气",
        _ => "暂无稳定节奏"
    };

    public static string InteractionComfortTier(LivingNpcState state) => state.InteractionComfortTier switch
    {
        "Intimate" => $"非常亲近（{state.LastFriendshipHearts} 心，日常舒适上限 {state.DailyConversationComfortLimit} 次）",
        "Trusted" => $"亲近（{state.LastFriendshipHearts} 心，日常舒适上限 {state.DailyConversationComfortLimit} 次）",
        "Friendly" => $"友好（{state.LastFriendshipHearts} 心，日常舒适上限 {state.DailyConversationComfortLimit} 次）",
        "Familiar" => $"熟悉（{state.LastFriendshipHearts} 心，日常舒适上限 {state.DailyConversationComfortLimit} 次）",
        _ => $"不熟（{state.LastFriendshipHearts} 心，日常舒适上限 {state.DailyConversationComfortLimit} 次）"
    };

    public static string LastSceneInfluence(LivingNpcState state) => state.LastSceneInfluence switch
    {
        "none" => "暂无",
        _ => state.LastSceneInfluence
    };

    public static string LastGift(LivingNpcState state) => string.IsNullOrWhiteSpace(state.LastGiftName)
        ? "暂无"
        : $"{state.LastGiftName}（{state.LastGiftTaste}，第 {state.LastGiftTotalDays} 天 {state.LastGiftTimeOfDay}，今天第 {state.GiftsToday} 次礼物记录）";

    public static string LastEvent(LivingNpcState state) => string.IsNullOrWhiteSpace(state.LastEventContext)
        ? "暂无"
        : $"{state.LastEventContext}（第 {state.LastEventTotalDays} 天 {state.LastEventTimeOfDay}）";

    public static string LongTermMemories(LivingNpcState state)
    {
        var memories = state.GetTopLongTermMemories(4).ToList();
        return memories.Count == 0
            ? "暂无"
            : string.Join("；", memories.Select(memory => $"{memory.Summary}（重要度 {memory.Importance}）"));
    }

    public static string RelationshipImpression(LivingNpcState state)
    {
        string queueSuffix = $"（累计压缩 {state.RelationshipImpressionMemoryCount} 条，待压缩 {state.ImpressionBacklog.Count} 条，压缩中 {state.ImpressionInFlight.Count} 条）";
        return string.IsNullOrWhiteSpace(state.RelationshipImpression)
            ? $"尚未生成{queueSuffix}"
            : $"{state.RelationshipImpression}{queueSuffix}";
    }

    public static string PlayerPreferences(LivingNpcState state)
    {
        var preferences = state.GetTopPlayerPreferences(6).ToList();
        return preferences.Count == 0
            ? "暂无"
            : string.Join("；", preferences.Select(memory => $"{memory.Summary}（{memory.PreferenceKind}，重要度 {memory.Importance}）"));
    }

    public static string CommunityImpressions(LivingNpcState state)
    {
        var memories = state.GetTopCommunityImpressions(4).ToList();
        return memories.Count == 0
            ? "暂无"
            : string.Join("；", memories.Select(memory =>
                $"{memory.Summary}（{memory.Source}/{memory.Visibility}，{memory.FreshnessStage}，转述 {memory.TransmissionDepth} 次，失真 {memory.DistortionLevel}，重要度 {memory.Importance}）"));
    }

    public static string SharedExperiences(LivingNpcState state)
    {
        var experiences = state.GetTopSharedExperiences(4).ToList();
        return experiences.Count == 0
            ? "暂无"
            : string.Join("；", experiences.Select(experience =>
                $"{experience.Summary}（{experience.Type}，{experience.LocationLabel}，第 {experience.CreatedTotalDays} 天）"));
    }

    public static string DialogueBehaviorInfluences(LivingNpcState state)
    {
        var influences = DialogueBehaviorInfluenceStore.OrderForDisplay(state.DialogueBehaviorInfluences)
            .Take(4)
            .ToList();
        return influences.Count == 0
            ? "暂无"
            : string.Join("；", influences.Select(influence =>
                $"{influence.Summary}（{influence.Type}，{influence.Status}，触发 {influence.TriggerCount}/{influence.MaxTriggers}，到第 {influence.ExpiresTotalDays} 天）"));
    }

    public static string HelpRequests(LivingNpcState state)
    {
        var requests = state.GetTopHelpRequests(4).ToList();
        return requests.Count == 0
            ? "暂无"
            : string.Join("；", requests.Select(request =>
                $"{request.Summary}（{request.Type}，截止第 {request.DueTotalDays} 天，{request.Status}，原因：{request.Reason}，当前步骤：{PromptFragments.Facts.HelpRequestCurrentStep(request)}）"));
    }

    public static string RelationshipTrust(LivingNpcState state) => $"{state.RelationshipTrust}/100";

    public static string Conflicts(LivingNpcState state)
    {
        var conflicts = state.GetTopConflicts(4).ToList();
        return conflicts.Count == 0
            ? "暂无"
            : string.Join("；", conflicts.Select(conflict =>
                $"{conflict.Summary}（{conflict.CauseKind}，严重度 {conflict.Severity}，{conflict.Status}，修复阶段 {conflict.RepairStage}）"));
    }

    public static string FarmerNickname(LivingNpcState state)
    {
        if (string.IsNullOrWhiteSpace(state.FarmerNickname))
        {
            return "暂无";
        }

        return state.FarmerNicknameStatus switch
        {
            "Accepted" => $"已接受称呼“{state.FarmerNickname}”",
            "Rejected" => $"未接受称呼“{state.FarmerNickname}”",
            _ => $"玩家请求称呼“{state.FarmerNickname}”，是否接受尚不明确"
        };
    }

    public static string LastInteraction(LivingNpcState state) => state.LastInteraction switch
    {
        "none yet" => "暂无",
        "passive nearby reaction" => "附近发生了一次自然反应",
        "small behavior near the farmer" => "刚在玩家附近做过小动作",
        "the farmer started a conversation" => "玩家刚主动开始对话",
        "the farmer caused interpersonal friction" => "玩家刚造成了一次关系摩擦",
        "the farmer helped repair a conflict" => "玩家刚缓和了一次冲突",
        _ when state.LastInteraction.StartsWith("the farmer helped with a personal request", System.StringComparison.Ordinal) => "玩家刚帮忙完成了一次主动求助",
        _ when state.LastInteraction.StartsWith("the farmer declined a personal help request", System.StringComparison.Ordinal) => "玩家刚拒绝了一次主动求助",
        _ when state.LastInteraction.StartsWith("a personal help request went unanswered", System.StringComparison.Ordinal) => "一次主动求助没有得到回应",
        "time passed" => "时间过去，状态回落",
        _ => state.LastInteraction
    };
}
