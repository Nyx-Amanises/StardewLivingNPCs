using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class LivingNpcState
{
    public string NpcName { get; set; } = string.Empty;
    public string Mood { get; set; } = "Neutral";
    public string CurrentEmotion { get; set; } = "Calm";
    public int EmotionIntensity { get; set; }
    public string LastEmotionReason { get; set; } = "none";
    public int LastEmotionUpdatedTotalDays { get; set; } = -1;
    public int LastEmotionUpdatedTimeOfDay { get; set; }
    public int Attention { get; set; } = 35;
    public int Openness { get; set; } = 50;
    public int Familiarity { get; set; }
    public int FamiliarityGainedToday { get; set; }
    public int LastFamiliarityGainTotalDays { get; set; } = -1;
    public int ConversationsToday { get; set; }
    public int ConsecutiveConversationDays { get; set; }
    public int LastConversationTotalDays { get; set; } = -1;
    public int LastConversationTimeOfDay { get; set; }
    public int LastConversationGapDays { get; set; } = -1;
    public string InteractionRhythm { get; set; } = "New";
    public string InteractionComfortTier { get; set; } = "Distant";
    public int DailyConversationComfortLimit { get; set; } = 2;
    public int RepeatedConversationPressure { get; set; }
    public int LastFriendshipHearts { get; set; }
    public string LastGiftName { get; set; } = string.Empty;
    public string LastGiftTaste { get; set; } = string.Empty;
    public int LastGiftTotalDays { get; set; } = -1;
    public int LastGiftTimeOfDay { get; set; }
    public int GiftsToday { get; set; }
    public string LastEventContext { get; set; } = string.Empty;
    public int LastEventTotalDays { get; set; } = -1;
    public int LastEventTimeOfDay { get; set; }
    public List<LongTermMemoryFact> LongTermMemories { get; set; } = new();
    public List<PlayerPreferenceFact> PlayerPreferenceMemories { get; set; } = new();
    public List<CommunityImpressionFact> CommunityImpressions { get; set; } = new();
    public List<SharedExperienceFact> SharedExperiences { get; set; } = new();
    public List<DialogueBehaviorInfluenceFact> DialogueBehaviorInfluences { get; set; } = new();
    public List<NpcHelpRequestFact> HelpRequests { get; set; } = new();
    public List<NpcConflictFact> Conflicts { get; set; } = new();
    public bool RelationshipTrustInitialized { get; set; }
    public int RelationshipTrust { get; set; } = 20;
    public int LastRelationshipTrustUpdatedTotalDays { get; set; } = -1;
    public int LastRelationshipTrustUpdatedTimeOfDay { get; set; }
    public int AiFriendshipGainedToday { get; set; }
    public int LastAiFriendshipTotalDays { get; set; } = -1;
    public int LastAiSmallGiftTotalDays { get; set; } = -1;
    public int LastAiMeaningfulGiftTotalDays { get; set; } = -1;
    public int LastAiMoneyGiftTotalDays { get; set; } = -1;
    public List<string> RecentAiGiftItemIds { get; set; } = new();
    public int LastDailyGiftOpportunityRollTotalDays { get; set; } = -1;
    public int DailyGiftOpportunityTotalDays { get; set; } = -1;
    public int DailyGiftOpportunityChancePercent { get; set; }
    public string DailyGiftOpportunityReason { get; set; } = string.Empty;
    public int PendingReciprocalGiftDueTotalDays { get; set; } = -1;
    public string PendingReciprocalGiftSourceGiftName { get; set; } = string.Empty;
    public string PendingReciprocalGiftReason { get; set; } = string.Empty;
    public List<NpcGiftMailFact> GiftMails { get; set; } = new();
    public int LastAiFarmHelpTotalDays { get; set; } = -1;
    public int LastAiWalkTogetherTotalDays { get; set; } = -1;
    public int LastHelpRequestTotalDays { get; set; } = -1;
    public int LastHelpRequestTimeOfDay { get; set; }
    public string LastSceneContext { get; set; } = "none";
    public string LastSceneInfluence { get; set; } = "none";
    public string LastSceneInfluenceReason { get; set; } = "none";
    public string CurrentInclination { get; set; } = "Neutral";
    public string LastInteraction { get; set; } = "none yet";
    public string FarmerNickname { get; set; } = string.Empty;
    public string FarmerNicknameStatus { get; set; } = string.Empty;
    public int FarmerNicknameTotalDays { get; set; } = -1;
    public int FarmerNicknameTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; }
    public int LastUpdatedTimeOfDay { get; set; }

    public string MoodLabel => this.Mood switch
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

    public string EmotionLabel => this.CurrentEmotion switch
    {
        "Happy" => $"开心（{this.EmotionIntensity}）",
        "Jealous" => $"吃醋（{this.EmotionIntensity}）",
        "Worried" => $"担心（{this.EmotionIntensity}）",
        "Grateful" => $"感激（{this.EmotionIntensity}）",
        "Disappointed" => $"失望（{this.EmotionIntensity}）",
        "Uneasy" => $"有些不自在（{this.EmotionIntensity}）",
        "Upset" => $"不悦（{this.EmotionIntensity}）",
        "Angry" => $"生气（{this.EmotionIntensity}）",
        "Sad" => $"伤心（{this.EmotionIntensity}）",
        _ => $"平静（{this.EmotionIntensity}）"
    };

    public string EmotionPromptLabel => this.CurrentEmotion switch
    {
        "Happy" => $"happy, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Jealous" => $"jealous, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Worried" => $"worried, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Grateful" => $"grateful, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Disappointed" => $"disappointed, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Uneasy" => $"uneasy, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Upset" => $"upset, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Angry" => $"angry, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        "Sad" => $"sad, intensity {this.EmotionIntensity}/100; latest reason: {this.LastEmotionReason}",
        _ => $"calm, intensity {this.EmotionIntensity}/100"
    };

    public bool HasUnresolvedConflict => this.Conflicts.Any(conflict => conflict.Status is "Active" or "Recovering");

    public int HighestUnresolvedConflictSeverity => this.Conflicts
        .Where(conflict => conflict.Status is "Active" or "Recovering")
        .Select(conflict => conflict.Severity)
        .DefaultIfEmpty(0)
        .Max();

    public string FamiliarityLabel => this.Familiarity switch
    {
        >= 75 => "亲近",
        >= 45 => "熟悉",
        >= 18 => "眼熟",
        _ => "刚认识"
    };

    public string FamiliarityPromptLabel => this.Familiarity switch
    {
        >= 75 => "close and comfortable",
        >= 45 => "familiar",
        >= 18 => "recognizes the farmer",
        _ => "new or barely familiar"
    };

    public string AttentionLabel => this.Attention switch
    {
        >= 75 => "高",
        >= 45 => "中",
        _ => "低"
    };

    public string InclinationLabel => this.CurrentInclination switch
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

    public string InteractionRhythmLabel => this.InteractionRhythm switch
    {
        "AfterLongGap" => $"隔了 {this.LastConversationGapDays} 天才再次聊天",
        "AtComfortLimit" => $"今天第 {this.ConversationsToday} 次聊天，接近日常舒适上限",
        "BuildingRoutine" => $"连续 {this.ConsecutiveConversationDays} 天打招呼",
        "CheckedInAgain" => $"今天第 {this.ConversationsToday} 次聊天",
        "ComfortableRepeat" => $"今天第 {this.ConversationsToday} 次聊天，关系足够熟所以仍然自然",
        "CrowdedToday" => $"今天已经聊了 {this.ConversationsToday} 次，有点频繁",
        "DailyRoutine" => $"连续 {this.ConsecutiveConversationDays} 天聊天，像日常习惯",
        "FirstConversation" => "第一次记录到对话",
        "FreshToday" => "今天第一次聊天",
        "LongQuietGap" => $"已经 {this.LastConversationGapDays} 天没有聊天",
        "NoConversationToday" => this.LastConversationGapDays <= 1
            ? "今天还没聊天，昨天聊过"
            : $"今天还没聊天，上次聊天在 {this.LastConversationGapDays} 天前",
        "PoliteRepeat" => $"今天第 {this.ConversationsToday} 次聊天，关系还不深所以会更客气",
        _ => "暂无稳定节奏"
    };

    public string InteractionComfortTierLabel => this.InteractionComfortTier switch
    {
        "Intimate" => $"非常亲近（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        "Trusted" => $"亲近（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        "Friendly" => $"友好（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        "Familiar" => $"熟悉（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）",
        _ => $"不熟（{this.LastFriendshipHearts} 心，日常舒适上限 {this.DailyConversationComfortLimit} 次）"
    };

    public string InteractionComfortTierPromptLabel => this.InteractionComfortTier switch
    {
        "Intimate" => $"very close; {this.LastFriendshipHearts} hearts; up to {this.DailyConversationComfortLimit} short conversations today can feel normal",
        "Trusted" => $"trusted; {this.LastFriendshipHearts} hearts; up to {this.DailyConversationComfortLimit} short conversations today can still feel natural",
        "Friendly" => $"friendly; {this.LastFriendshipHearts} hearts; repeated conversations are acceptable but should not feel endlessly eager",
        "Familiar" => $"familiar; {this.LastFriendshipHearts} hearts; repeated conversations should stay polite and modest",
        _ => $"distant; {this.LastFriendshipHearts} hearts; repeated conversations should feel more cautious or formal"
    };

    public string InteractionRhythmPromptLabel => this.InteractionRhythm switch
    {
        "AfterLongGap" => $"they are speaking again after {this.LastConversationGapDays} days without a recorded conversation",
        "AtComfortLimit" => $"this is conversation {this.ConversationsToday} today, around the normal comfort limit for this relationship",
        "BuildingRoutine" => $"the farmer has checked in for {this.ConsecutiveConversationDays} consecutive days",
        "CheckedInAgain" => $"this is conversation {this.ConversationsToday} with the farmer today",
        "ComfortableRepeat" => $"this is conversation {this.ConversationsToday} today, but the relationship is close enough that it can still feel natural",
        "CrowdedToday" => $"the farmer has already spoken with them {this.ConversationsToday} times today, so the attention may feel repetitive",
        "DailyRoutine" => $"the farmer has spoken with them for {this.ConsecutiveConversationDays} consecutive days, forming a familiar daily rhythm",
        "FirstConversation" => "this is the first recorded LivingNPCs conversation with the farmer",
        "FreshToday" => "this is the first recorded conversation with the farmer today",
        "LongQuietGap" => $"there has been no recorded conversation with the farmer for {this.LastConversationGapDays} days",
        "NoConversationToday" => this.LastConversationGapDays <= 1
            ? "there has been no recorded conversation with the farmer today, but they spoke yesterday"
            : $"there has been no recorded conversation with the farmer today; the last one was {this.LastConversationGapDays} days ago",
        "PoliteRepeat" => $"this is conversation {this.ConversationsToday} today, and the relationship is not close enough for repeated chats to feel fully casual",
        _ => "no stable interaction rhythm yet"
    };

    public string LastSceneInfluenceLabel => this.LastSceneInfluence switch
    {
        "none" => "暂无",
        _ => this.LastSceneInfluence
    };

    public string LastGiftLabel => string.IsNullOrWhiteSpace(this.LastGiftName)
        ? "暂无"
        : $"{this.LastGiftName}（{this.LastGiftTaste}，第 {this.LastGiftTotalDays} 天 {this.LastGiftTimeOfDay}，今天第 {this.GiftsToday} 次礼物记录）";

    public string LastGiftPromptLabel => string.IsNullOrWhiteSpace(this.LastGiftName)
        ? "no recent LivingNPCs gift memory"
        : $"last recorded gift: the farmer offered {this.LastGiftName}; gift taste: {this.LastGiftTaste}; gifts recorded today: {this.GiftsToday}";

    public string LastEventLabel => string.IsNullOrWhiteSpace(this.LastEventContext)
        ? "暂无"
        : $"{this.LastEventContext}（第 {this.LastEventTotalDays} 天 {this.LastEventTimeOfDay}）";

    public string LastEventPromptLabel => string.IsNullOrWhiteSpace(this.LastEventContext)
        ? "no recent LivingNPCs event memory"
        : $"last recorded event context: {this.LastEventContext}";

    public string LongTermMemoryPromptLabel
    {
        get
        {
            var memories = this.GetTopLongTermMemories(4).ToList();
            return memories.Count == 0
                ? "no durable personal memory has been recorded"
                : string.Join("; ", memories.Select(memory => memory.Summary));
        }
    }

    public string LongTermMemoryDebugLabel
    {
        get
        {
            var memories = this.GetTopLongTermMemories(4).ToList();
            return memories.Count == 0
                ? "暂无"
                : string.Join("；", memories.Select(memory => $"{memory.Summary}（重要度 {memory.Importance}）"));
        }
    }

    public string PlayerPreferencePromptLabel
    {
        get
        {
            var preferences = this.GetTopPlayerPreferences(6).ToList();
            return preferences.Count == 0
                ? "no durable farmer preference memory has been recorded"
                : string.Join("; ", preferences.Select(memory => memory.Summary));
        }
    }

    public string PlayerPreferenceDebugLabel
    {
        get
        {
            var preferences = this.GetTopPlayerPreferences(6).ToList();
            return preferences.Count == 0
                ? "暂无"
                : string.Join("；", preferences.Select(memory => $"{memory.Summary}（{memory.PreferenceKind}，重要度 {memory.Importance}）"));
        }
    }

    public string CommunityImpressionPromptLabel
    {
        get
        {
            var memories = this.GetTopCommunityImpressions(4).ToList();
            return memories.Count == 0
                ? "no community impression about the farmer has been recorded"
                : string.Join("; ", memories.Select(memory => memory.PromptLabel));
        }
    }

    public string CommunityImpressionDebugLabel
    {
        get
        {
            var memories = this.GetTopCommunityImpressions(4).ToList();
            return memories.Count == 0
                ? "暂无"
                : string.Join("；", memories.Select(memory =>
                    $"{memory.Summary}（{memory.Source}/{memory.Visibility}，{memory.FreshnessStage}，转述 {memory.TransmissionDepth} 次，失真 {memory.DistortionLevel}，重要度 {memory.Importance}）"));
        }
    }

    public string SharedExperiencePromptLabel
    {
        get
        {
            var experiences = this.GetTopSharedExperiences(4).ToList();
            return experiences.Count == 0
                ? "no durable shared experiences are recorded"
                : string.Join("; ", experiences.Select(experience => experience.PromptLabel));
        }
    }

    public string SharedExperienceDebugLabel
    {
        get
        {
            var experiences = this.GetTopSharedExperiences(4).ToList();
            return experiences.Count == 0
                ? "暂无"
                : string.Join("；", experiences.Select(experience =>
                    $"{experience.Summary}（{experience.Type}，{experience.LocationLabel}，第 {experience.CreatedTotalDays} 天）"));
        }
    }

    public IEnumerable<DialogueBehaviorInfluenceFact> ActiveDialogueBehaviorInfluences =>
        this.DialogueBehaviorInfluences.Where(influence =>
            influence.Status == "Active"
            && influence.ExpiresTotalDays >= Game1.Date.TotalDays
            && influence.TriggerCount < System.Math.Max(1, influence.MaxTriggers));

    public string DialogueBehaviorInfluencePromptLabel
    {
        get
        {
            var influences = this.ActiveDialogueBehaviorInfluences.Take(4).ToList();
            return influences.Count == 0
                ? "no active conversation-driven behavior tendency"
                : string.Join("; ", influences.Select(influence => influence.PromptLabel));
        }
    }

    public string DialogueBehaviorInfluenceDebugLabel
    {
        get
        {
            var influences = DialogueBehaviorInfluenceStore.OrderForDisplay(this.DialogueBehaviorInfluences)
                .Take(4)
                .ToList();
            return influences.Count == 0
                ? "暂无"
                : string.Join("；", influences.Select(influence =>
                    $"{influence.Summary}（{influence.Type}，{influence.Status}，触发 {influence.TriggerCount}/{influence.MaxTriggers}，到第 {influence.ExpiresTotalDays} 天）"));
        }
    }

    public string HelpRequestPromptLabel
    {
        get
        {
            var requests = this.GetTopHelpRequests(4).ToList();
            return requests.Count == 0
                ? "no durable help requests are recorded"
                : string.Join("; ", requests.Select(request => request.PromptLabel));
        }
    }

    public string HelpRequestDebugLabel
    {
        get
        {
            var requests = this.GetTopHelpRequests(4).ToList();
            return requests.Count == 0
                ? "暂无"
                : string.Join("；", requests.Select(request =>
                    $"{request.Summary}（{request.Type}，截止第 {request.DueTotalDays} 天，{request.Status}，原因：{request.Reason}，当前步骤：{request.CurrentStepPromptLabel}）"));
        }
    }

    public string RelationshipTrustPromptLabel => this.RelationshipTrust switch
    {
        >= 80 => $"deep interpersonal trust ({this.RelationshipTrust}/100)",
        >= 60 => $"steady interpersonal trust ({this.RelationshipTrust}/100)",
        >= 35 => $"tentative interpersonal trust ({this.RelationshipTrust}/100)",
        _ => $"low interpersonal trust ({this.RelationshipTrust}/100)"
    };

    public string RelationshipTrustDebugLabel => $"{this.RelationshipTrust}/100";

    public string SecretSharingPromptLabel => this.RelationshipTrust switch
    {
        >= 80 => "deep trust; private hopes, fears, and history may surface naturally when the scene truly supports it",
        >= 60 => "steady trust; some vulnerable personal details may be shared when relevant",
        >= 35 => "limited trust; light personal details are fine, but deeper secrets should stay mostly guarded",
        _ => "low trust; keep disclosures surface-level and avoid volunteering private secrets"
    };

    public string ConflictPromptLabel
    {
        get
        {
            var conflicts = this.GetTopConflicts(4).ToList();
            return conflicts.Count == 0
                ? "no durable conflict memory has been recorded"
                : string.Join("; ", conflicts.Select(conflict => conflict.PromptLabel));
        }
    }

    public string ConflictDebugLabel
    {
        get
        {
            var conflicts = this.GetTopConflicts(4).ToList();
            return conflicts.Count == 0
                ? "暂无"
                : string.Join("；", conflicts.Select(conflict =>
                    $"{conflict.Summary}（{conflict.CauseKind}，严重度 {conflict.Severity}，{conflict.Status}，修复阶段 {conflict.RepairStage}）"));
        }
    }

    public string FarmerNicknamePromptLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.FarmerNickname))
            {
                return "no personal name preference has been recorded";
            }

            return this.FarmerNicknameStatus switch
            {
                "Accepted" => $"the farmer asked to be called {this.FarmerNickname}, and this NPC accepted; when choosing to address the farmer, use that name instead of @ or the save-file name, and never combine both names in one reply",
                "Rejected" => $"the farmer asked to be called {this.FarmerNickname}, but this NPC did not accept; do not use that name unless the relationship later changes",
                _ => $"the farmer asked to be called {this.FarmerNickname}; acceptance is unclear, so the NPC may decide whether to use it based on personality and relationship"
            };
        }
    }

    public string FarmerNicknameLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(this.FarmerNickname))
            {
                return "暂无";
            }

            return this.FarmerNicknameStatus switch
            {
                "Accepted" => $"已接受称呼“{this.FarmerNickname}”",
                "Rejected" => $"未接受称呼“{this.FarmerNickname}”",
                _ => $"玩家请求称呼“{this.FarmerNickname}”，是否接受尚不明确"
            };
        }
    }

    public string LastInteractionLabel => this.LastInteraction switch
    {
        "none yet" => "暂无",
        "passive nearby reaction" => "附近发生了一次自然反应",
        "small behavior near the farmer" => "刚在玩家附近做过小动作",
        "the farmer started a conversation" => "玩家刚主动开始对话",
        "the farmer caused interpersonal friction" => "玩家刚造成了一次关系摩擦",
        "the farmer helped repair a conflict" => "玩家刚缓和了一次冲突",
        _ when this.LastInteraction.StartsWith("the farmer helped with a personal request", System.StringComparison.Ordinal) => "玩家刚帮忙完成了一次主动求助",
        _ when this.LastInteraction.StartsWith("the farmer declined a personal help request", System.StringComparison.Ordinal) => "玩家刚拒绝了一次主动求助",
        _ when this.LastInteraction.StartsWith("a personal help request went unanswered", System.StringComparison.Ordinal) => "一次主动求助没有得到回应",
        "time passed" => "时间过去，状态回落",
        _ => this.LastInteraction
    };

    public static int ClampScore(int value)
    {
        return System.Math.Clamp(value, 0, 100);
    }

    public static int MoveToward(int value, int target, int amount)
    {
        if (amount <= 0 || value == target)
        {
            return value;
        }

        return value < target
            ? System.Math.Min(value + amount, target)
            : System.Math.Max(value - amount, target);
    }

    public void Clamp()
    {
        this.Attention = ClampScore(this.Attention);
        this.Openness = ClampScore(this.Openness);
        this.Familiarity = ClampScore(this.Familiarity);
        this.RelationshipTrust = ClampScore(this.RelationshipTrust);
        this.CurrentEmotion = BehaviorMemory.NormalizeEmotion(this.CurrentEmotion);
        if (this.CurrentEmotion == "none")
        {
            this.CurrentEmotion = "Calm";
        }

        this.EmotionIntensity = ClampScore(this.EmotionIntensity);
        this.FamiliarityGainedToday = System.Math.Clamp(this.FamiliarityGainedToday, 0, 100);
        this.ConversationsToday = System.Math.Max(0, this.ConversationsToday);
        this.ConsecutiveConversationDays = System.Math.Max(0, this.ConsecutiveConversationDays);
        this.LastConversationGapDays = this.LastConversationGapDays < -1 ? -1 : this.LastConversationGapDays;
        this.DailyConversationComfortLimit = System.Math.Clamp(this.DailyConversationComfortLimit <= 0 ? 2 : this.DailyConversationComfortLimit, 1, 8);
        this.RepeatedConversationPressure = System.Math.Clamp(this.RepeatedConversationPressure, 0, 100);
        this.LastFriendshipHearts = System.Math.Clamp(this.LastFriendshipHearts, 0, 14);
        this.GiftsToday = System.Math.Max(0, this.GiftsToday);
        this.LongTermMemories ??= new List<LongTermMemoryFact>();
        this.LongTermMemories = this.LongTermMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(LongTermMemoryStore.NormalizeForStore)
            .OrderByDescending(LongTermMemoryStore.GetRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(LongTermMemoryStore.MaxMemoriesPerNpc)
            .ToList();
        this.PlayerPreferenceMemories ??= new List<PlayerPreferenceFact>();
        this.PlayerPreferenceMemories = this.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(PlayerPreferenceMemoryStore.NormalizeForStore)
            .Where(memory => memory.PreferenceKind != "none")
            .OrderByDescending(PlayerPreferenceMemoryStore.GetRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(PlayerPreferenceMemoryStore.MaxMemoriesPerNpc)
            .ToList();
        this.CommunityImpressions ??= new List<CommunityImpressionFact>();
        this.CommunityImpressions = this.CommunityImpressions
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.SubjectNpcName)
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && (memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= Game1.Date.TotalDays))
            .Select(memory => CommunityImpressionStore.NormalizeForStore(memory))
            .OrderByDescending(CommunityImpressionStore.GetRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(CommunityImpressionStore.MaxImpressionsPerNpc)
            .ToList();
        this.HelpRequests ??= new List<NpcHelpRequestFact>();
        this.HelpRequests = this.HelpRequests
            .Where(request => request != null && !string.IsNullOrWhiteSpace(request.Summary))
            .Select(request =>
            {
                request.Type = BehaviorMemory.NormalizeHelpRequestType(request.Type);
                request.NpcDisplayName = request.NpcDisplayName?.Trim() ?? string.Empty;
                request.QuestLogId = string.IsNullOrWhiteSpace(request.QuestLogId)
                    ? System.Guid.NewGuid().ToString("N")
                    : request.QuestLogId.Trim();
                request.Summary = request.Summary.Trim();
                request.RequestedItemId = request.RequestedItemId?.Trim() ?? string.Empty;
                request.RequestedItemLabel = request.RequestedItemLabel?.Trim() ?? string.Empty;
                request.QuestionTopic = request.QuestionTopic?.Trim() ?? string.Empty;
                request.Reason = request.Reason?.Trim() ?? string.Empty;
                request.FollowUpPotential = BehaviorMemory.NormalizeHelpRequestFollowUpPotential(request.FollowUpPotential);
                request.FailureReaction = request.FailureReaction?.Trim() ?? string.Empty;
                request.Steps ??= new List<NpcHelpRequestStepFact>();
                request.Steps = request.Steps
                    .Where(step => step != null)
                    .Select(step =>
                    {
                        step.Type = BehaviorMemory.NormalizeHelpRequestType(step.Type);
                        step.Summary = step.Summary?.Trim() ?? string.Empty;
                        step.RequestedItemId = step.RequestedItemId?.Trim() ?? string.Empty;
                        step.RequestedItemLabel = step.RequestedItemLabel?.Trim() ?? string.Empty;
                        step.QuestionTopic = step.QuestionTopic?.Trim() ?? string.Empty;
                        step.Status = step.Status == "Fulfilled" ? "Fulfilled" : "Pending";
                        step.Resolution = step.Resolution?.Trim() ?? string.Empty;
                        return step;
                    })
                    .Where(step => step.Type != "none" && !string.IsNullOrWhiteSpace(step.Summary))
                    .Take(3)
                    .ToList();
                request.Status = request.Status switch
                {
                    "Offered" => "Offered",
                    "Fulfilled" => "Fulfilled",
                    "Expired" => "Expired",
                    "Declined" => "Declined",
                    _ => "Pending"
                };
                request.CurrentStepIndex = System.Math.Clamp(request.CurrentStepIndex, 0, System.Math.Max(0, request.Steps.Count - 1));
                if (request.Steps.Count == 0)
                {
                    request.Steps.Add(new NpcHelpRequestStepFact
                    {
                        Type = request.Type,
                        Summary = request.Summary,
                        RequestedItemId = request.RequestedItemId,
                        RequestedItemLabel = request.RequestedItemLabel,
                        QuestionTopic = request.QuestionTopic,
                        Status = request.Status == "Fulfilled" ? "Fulfilled" : "Pending",
                        Resolution = request.Resolution,
                        CompletedTotalDays = request.FulfilledTotalDays,
                        CompletedTimeOfDay = request.FulfilledTimeOfDay
                    });
                }

                var currentStep = request.Steps[request.CurrentStepIndex];
                request.Type = currentStep.Type;
                request.RequestedItemId = currentStep.RequestedItemId;
                request.RequestedItemLabel = currentStep.RequestedItemLabel;
                request.QuestionTopic = currentStep.QuestionTopic;
                request.TimesReinforced = System.Math.Max(0, request.TimesReinforced);
                request.RewardFriendship = request.RewardFriendship <= 0
                    ? 50
                    : System.Math.Clamp(request.RewardFriendship, 0, 100);
                request.RewardMoney = request.RewardMoney <= 0
                    ? HelpRequestMemoryRules.DetermineMoneyReward(request.Steps)
                    : System.Math.Clamp(request.RewardMoney, 200, 10000);
                request.RewardMoneyMailKey = request.RewardMoneyMailKey?.Trim() ?? string.Empty;
                if (!request.RewardMoneyByMail)
                {
                    request.RewardMoneyMailTotalDays = -1;
                }

                if (!request.SpecialFollowUpPlanned
                    && request.Status == "Fulfilled"
                    && request.FollowUpEligibleTotalDays > 0
                    && request.FollowUpShownTotalDays < 0)
                {
                    request.SpecialFollowUpPlanned = true;
                }

                return request;
            })
            .Where(request => request.Type != "none")
            .OrderBy(request => BehaviorMemory.HelpRequestStatusOrder(request.Status))
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        this.SharedExperiences ??= new List<SharedExperienceFact>();
        this.SharedExperiences = this.SharedExperiences
            .Where(experience => experience != null && !string.IsNullOrWhiteSpace(experience.Summary))
            .Select(experience =>
            {
                experience.Type = BehaviorMemory.NormalizeSharedExperienceType(experience.Type);
                experience.Summary = experience.Summary.Trim();
                experience.LocationName = experience.LocationName?.Trim() ?? string.Empty;
                experience.LocationLabel = string.IsNullOrWhiteSpace(experience.LocationLabel)
                    ? experience.LocationName
                    : experience.LocationLabel.Trim();
                experience.Key = string.IsNullOrWhiteSpace(experience.Key)
                    ? $"{experience.Type}:{experience.Summary}:{experience.LocationName}"
                    : experience.Key;
                experience.Importance = ClampScore(experience.Importance);
                experience.TimesReinforced = System.Math.Max(0, experience.TimesReinforced);
                return experience;
            })
            .Where(experience => experience.Type != "none")
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(12)
            .ToList();
        DialogueBehaviorInfluenceStore.Refresh(this, Game1.Date.TotalDays);
        this.Conflicts ??= new List<NpcConflictFact>();
        this.Conflicts = this.Conflicts
            .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary))
            .Select(conflict =>
            {
                conflict.CauseKind = BehaviorMemory.NormalizeConflictCauseKind(conflict.CauseKind);
                conflict.Summary = conflict.Summary.Trim();
                conflict.Severity = ClampScore(conflict.Severity);
                conflict.PeakSeverity = System.Math.Max(conflict.Severity, ClampScore(conflict.PeakSeverity));
                conflict.Status = conflict.Status switch
                {
                    "Resolved" => "Resolved",
                    "Recovering" => "Recovering",
                    _ => conflict.Severity <= 0 ? "Resolved" : "Active"
                };
                conflict.RepairScore = ClampScore(conflict.RepairScore);
                conflict.ApologyCount = System.Math.Max(0, conflict.ApologyCount);
                conflict.RepairStage = conflict.RepairStage switch
                {
                    "NeedsApology" => "NeedsApology",
                    "NeedsGesture" => "NeedsGesture",
                    "NeedsTime" => "NeedsTime",
                    "NeedsConversation" => "NeedsConversation",
                    "ReadyToResolve" => "ReadyToResolve",
                    "Resolved" => "Resolved",
                    _ => conflict.RequiresComplexRepair ? "NeedsApology" : "Simple"
                };
                if (conflict.RequiresComplexRepair && conflict.MinimumRepairTotalDays < 0)
                {
                    conflict.MinimumRepairTotalDays = conflict.CreatedTotalDays + ConflictRepairService.GetComplexRepairDelayDays(conflict.PeakSeverity);
                }
                conflict.TimesReinforced = System.Math.Max(0, conflict.TimesReinforced);
                if (conflict.Status == "Resolved" && conflict.ResolvedTotalDays < 0)
                {
                    conflict.ResolvedTotalDays = conflict.LastUpdatedTotalDays;
                    conflict.ResolvedTimeOfDay = conflict.LastUpdatedTimeOfDay;
                }

                return conflict;
            })
            .OrderBy(conflict => BehaviorMemory.ConflictStatusOrder(conflict.Status))
            .ThenByDescending(conflict => conflict.Severity)
            .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        this.AiFriendshipGainedToday = System.Math.Clamp(this.AiFriendshipGainedToday, 0, 30);
        if (string.IsNullOrWhiteSpace(this.Mood))
        {
            this.Mood = "Neutral";
        }

        if (string.IsNullOrWhiteSpace(this.CurrentInclination))
        {
            this.CurrentInclination = "Neutral";
        }

        if (string.IsNullOrWhiteSpace(this.LastInteraction))
        {
            this.LastInteraction = "none yet";
        }

        if (string.IsNullOrWhiteSpace(this.LastSceneContext))
        {
            this.LastSceneContext = "none";
        }

        if (string.IsNullOrWhiteSpace(this.LastSceneInfluence))
        {
            this.LastSceneInfluence = "none";
        }

        if (string.IsNullOrWhiteSpace(this.LastSceneInfluenceReason))
        {
            this.LastSceneInfluenceReason = "none";
        }

        if (string.IsNullOrWhiteSpace(this.LastEmotionReason))
        {
            this.LastEmotionReason = "none";
        }

        if (string.IsNullOrWhiteSpace(this.FarmerNicknameStatus) && !string.IsNullOrWhiteSpace(this.FarmerNickname))
        {
            this.FarmerNicknameStatus = "Requested";
        }

        if (string.IsNullOrWhiteSpace(this.InteractionRhythm))
        {
            this.InteractionRhythm = "New";
        }

        if (string.IsNullOrWhiteSpace(this.InteractionComfortTier))
        {
            this.InteractionComfortTier = "Distant";
        }

        if (this.LastGiftTotalDays != Game1.Date.TotalDays)
        {
            this.GiftsToday = 0;
        }

        if (this.LastAiFriendshipTotalDays != Game1.Date.TotalDays)
        {
            this.AiFriendshipGainedToday = 0;
        }

        this.RecentAiGiftItemIds ??= new List<string>();
        this.RecentAiGiftItemIds = this.RecentAiGiftItemIds
            .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
            .Select(itemId => itemId.Trim())
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (this.LastDailyGiftOpportunityRollTotalDays != Game1.Date.TotalDays)
        {
            this.DailyGiftOpportunityTotalDays = -1;
            this.DailyGiftOpportunityChancePercent = 0;
            this.DailyGiftOpportunityReason = string.Empty;
        }

        this.PendingReciprocalGiftDueTotalDays = -1;
        this.PendingReciprocalGiftSourceGiftName = string.Empty;
        this.PendingReciprocalGiftReason = string.Empty;

        this.GiftMails ??= new List<NpcGiftMailFact>();
        this.GiftMails = this.GiftMails
            .Where(mail => mail != null
                && !string.IsNullOrWhiteSpace(mail.ItemId)
                && !string.IsNullOrWhiteSpace(mail.ItemLabel))
            .Select(mail =>
            {
                mail.MailKey = string.IsNullOrWhiteSpace(mail.MailKey)
                    ? System.Guid.NewGuid().ToString("N")
                    : mail.MailKey.Trim();
                mail.NpcDisplayName = mail.NpcDisplayName?.Trim() ?? string.Empty;
                mail.ItemId = mail.ItemId?.Trim() ?? string.Empty;
                mail.ItemLabel = mail.ItemLabel?.Trim() ?? string.Empty;
                mail.Motive = mail.Motive switch
                {
                    "reciprocal" => "reciprocal",
                    "inventory_full" => "inventory_full",
                    "meaningful" => "meaningful",
                    "thanks" => "thanks",
                    "preference" => "preference",
                    _ => "daily"
                };
                mail.Reason = mail.Reason?.Trim() ?? string.Empty;
                mail.SourceGiftName = mail.SourceGiftName?.Trim() ?? string.Empty;
                mail.Tier = mail.Tier == "meaningful" ? "meaningful" : "small";
                mail.DueTotalDays = mail.DueTotalDays < 0
                    ? Game1.Date.TotalDays + 1
                    : mail.DueTotalDays;
                return mail;
            })
            .OrderBy(mail => mail.QueuedForDelivery)
            .ThenBy(mail => mail.DueTotalDays)
            .ThenByDescending(mail => mail.CreatedTotalDays)
            .ThenByDescending(mail => mail.CreatedTimeOfDay)
            .Take(12)
            .ToList();
    }

    public LivingNpcState Clone()
    {
        return new LivingNpcState
        {
            NpcName = this.NpcName,
            Mood = this.Mood,
            CurrentEmotion = this.CurrentEmotion,
            EmotionIntensity = this.EmotionIntensity,
            LastEmotionReason = this.LastEmotionReason,
            LastEmotionUpdatedTotalDays = this.LastEmotionUpdatedTotalDays,
            LastEmotionUpdatedTimeOfDay = this.LastEmotionUpdatedTimeOfDay,
            Attention = this.Attention,
            Openness = this.Openness,
            Familiarity = this.Familiarity,
            FamiliarityGainedToday = this.FamiliarityGainedToday,
            LastFamiliarityGainTotalDays = this.LastFamiliarityGainTotalDays,
            ConversationsToday = this.ConversationsToday,
            ConsecutiveConversationDays = this.ConsecutiveConversationDays,
            LastConversationTotalDays = this.LastConversationTotalDays,
            LastConversationTimeOfDay = this.LastConversationTimeOfDay,
            LastConversationGapDays = this.LastConversationGapDays,
            InteractionRhythm = this.InteractionRhythm,
            InteractionComfortTier = this.InteractionComfortTier,
            DailyConversationComfortLimit = this.DailyConversationComfortLimit,
            RepeatedConversationPressure = this.RepeatedConversationPressure,
            LastFriendshipHearts = this.LastFriendshipHearts,
            LastGiftName = this.LastGiftName,
            LastGiftTaste = this.LastGiftTaste,
            LastGiftTotalDays = this.LastGiftTotalDays,
            LastGiftTimeOfDay = this.LastGiftTimeOfDay,
            GiftsToday = this.GiftsToday,
            LastEventContext = this.LastEventContext,
            LastEventTotalDays = this.LastEventTotalDays,
            LastEventTimeOfDay = this.LastEventTimeOfDay,
            LongTermMemories = this.LongTermMemories
                .Select(memory => new LongTermMemoryFact
                {
                    Kind = memory.Kind,
                    Subject = memory.Subject,
                    Summary = memory.Summary,
                    Tags = memory.Tags.ToList(),
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
                    LastRecalledTotalDays = memory.LastRecalledTotalDays,
                    LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
                    RecallCount = memory.RecallCount,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            PlayerPreferenceMemories = this.PlayerPreferenceMemories
                .Select(memory => new PlayerPreferenceFact
                {
                    PreferenceKind = memory.PreferenceKind,
                    Subject = memory.Subject,
                    Summary = memory.Summary,
                    Tags = memory.Tags.ToList(),
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
                    LastRecalledTotalDays = memory.LastRecalledTotalDays,
                    LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
                    RecallCount = memory.RecallCount,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            CommunityImpressions = this.CommunityImpressions
                .Select(memory => new CommunityImpressionFact
                {
                    SubjectNpcName = memory.SubjectNpcName,
                    SubjectDisplayName = memory.SubjectDisplayName,
                    Kind = memory.Kind,
                    Summary = memory.Summary,
                    Source = memory.Source,
                    Visibility = memory.Visibility,
                    Confidence = memory.Confidence,
                    TransmissionDepth = memory.TransmissionDepth,
                    DistortionLevel = memory.DistortionLevel,
                    HeardFromNpcName = memory.HeardFromNpcName,
                    CircleKey = memory.CircleKey,
                    Importance = memory.Importance,
                    CreatedTotalDays = memory.CreatedTotalDays,
                    CreatedTimeOfDay = memory.CreatedTimeOfDay,
                    LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
                    LastRecalledTotalDays = memory.LastRecalledTotalDays,
                    LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
                    LastSharedTotalDays = memory.LastSharedTotalDays,
                    LastSharedTimeOfDay = memory.LastSharedTimeOfDay,
                    ShareCount = memory.ShareCount,
                    ExpiresTotalDays = memory.ExpiresTotalDays,
                    RecallCount = memory.RecallCount,
                    TimesReinforced = memory.TimesReinforced
                })
                .ToList(),
            SharedExperiences = this.SharedExperiences
                .Select(experience => new SharedExperienceFact
                {
                    Key = experience.Key,
                    Type = experience.Type,
                    Summary = experience.Summary,
                    LocationName = experience.LocationName,
                    LocationLabel = experience.LocationLabel,
                    CreatedTotalDays = experience.CreatedTotalDays,
                    CreatedTimeOfDay = experience.CreatedTimeOfDay,
                    LastUpdatedTotalDays = experience.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = experience.LastUpdatedTimeOfDay,
                    Importance = experience.Importance,
                    TimesReinforced = experience.TimesReinforced,
                    FollowUpEligibleTotalDays = experience.FollowUpEligibleTotalDays,
                    FollowUpShownTotalDays = experience.FollowUpShownTotalDays,
                    FollowUpShownTimeOfDay = experience.FollowUpShownTimeOfDay
                })
                .ToList(),
            DialogueBehaviorInfluences = this.DialogueBehaviorInfluences
                .Select(influence => new DialogueBehaviorInfluenceFact
                {
                    Type = influence.Type,
                    Summary = influence.Summary,
                    TargetLocation = influence.TargetLocation,
                    TargetLocationLabel = influence.TargetLocationLabel,
                    Intensity = influence.Intensity,
                    Status = influence.Status,
                    CreatedTotalDays = influence.CreatedTotalDays,
                    CreatedTimeOfDay = influence.CreatedTimeOfDay,
                    LastUpdatedTotalDays = influence.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = influence.LastUpdatedTimeOfDay,
                    ExpiresTotalDays = influence.ExpiresTotalDays,
                    LastTriggeredTotalDays = influence.LastTriggeredTotalDays,
                    LastTriggeredTimeOfDay = influence.LastTriggeredTimeOfDay,
                    TriggerCount = influence.TriggerCount,
                    MaxTriggers = influence.MaxTriggers,
                    TimesReinforced = influence.TimesReinforced
                })
                .ToList(),
            HelpRequests = this.HelpRequests
                .Select(request => new NpcHelpRequestFact
                {
                    NpcDisplayName = request.NpcDisplayName,
                    QuestLogId = request.QuestLogId,
                    Type = request.Type,
                    Summary = request.Summary,
                    Steps = request.Steps
                        .Select(step => new NpcHelpRequestStepFact
                        {
                            Type = step.Type,
                            Summary = step.Summary,
                            RequestedItemId = step.RequestedItemId,
                            RequestedItemLabel = step.RequestedItemLabel,
                            QuestionTopic = step.QuestionTopic,
                            Status = step.Status,
                            Resolution = step.Resolution,
                            CompletedTotalDays = step.CompletedTotalDays,
                            CompletedTimeOfDay = step.CompletedTimeOfDay
                        })
                        .ToList(),
                    CurrentStepIndex = request.CurrentStepIndex,
                    RequestedItemId = request.RequestedItemId,
                    RequestedItemLabel = request.RequestedItemLabel,
                    QuestionTopic = request.QuestionTopic,
                    DueTotalDays = request.DueTotalDays,
                    Reason = request.Reason,
                    Status = request.Status,
                    Resolution = request.Resolution,
                    FollowUpPotential = request.FollowUpPotential,
                    FailureReaction = request.FailureReaction,
                    CreatedTotalDays = request.CreatedTotalDays,
                    CreatedTimeOfDay = request.CreatedTimeOfDay,
                    AcceptedTotalDays = request.AcceptedTotalDays,
                    AcceptedTimeOfDay = request.AcceptedTimeOfDay,
                    DeclinedTotalDays = request.DeclinedTotalDays,
                    DeclinedTimeOfDay = request.DeclinedTimeOfDay,
                    LastUpdatedTotalDays = request.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = request.LastUpdatedTimeOfDay,
                    LastMentionedTotalDays = request.LastMentionedTotalDays,
                    LastMentionedTimeOfDay = request.LastMentionedTimeOfDay,
                    FulfilledTotalDays = request.FulfilledTotalDays,
                    FulfilledTimeOfDay = request.FulfilledTimeOfDay,
                    FollowUpEligibleTotalDays = request.FollowUpEligibleTotalDays,
                    FollowUpShownTotalDays = request.FollowUpShownTotalDays,
                    FollowUpShownTimeOfDay = request.FollowUpShownTimeOfDay,
                    RewardFriendship = request.RewardFriendship,
                    RewardGranted = request.RewardGranted,
                    RewardMoney = request.RewardMoney,
                    RewardMoneyGranted = request.RewardMoneyGranted,
                    RewardMoneyByMail = request.RewardMoneyByMail,
                    RewardMoneyMailKey = request.RewardMoneyMailKey,
                    RewardMoneyMailTotalDays = request.RewardMoneyMailTotalDays,
                    RewardGiftGiven = request.RewardGiftGiven,
                    SpecialFollowUpPlanned = request.SpecialFollowUpPlanned,
                    TimesReinforced = request.TimesReinforced
                })
                .ToList(),
            GiftMails = this.GiftMails
                .Select(mail => new NpcGiftMailFact
                {
                    MailKey = mail.MailKey,
                    NpcDisplayName = mail.NpcDisplayName,
                    ItemId = mail.ItemId,
                    ItemLabel = mail.ItemLabel,
                    Motive = mail.Motive,
                    Reason = mail.Reason,
                    SourceGiftName = mail.SourceGiftName,
                    Tier = mail.Tier,
                    CreatedTotalDays = mail.CreatedTotalDays,
                    CreatedTimeOfDay = mail.CreatedTimeOfDay,
                    DueTotalDays = mail.DueTotalDays,
                    QueuedForDelivery = mail.QueuedForDelivery
                })
                .ToList(),
            Conflicts = this.Conflicts
                .Select(conflict => new NpcConflictFact
                {
                    CauseKind = conflict.CauseKind,
                    Summary = conflict.Summary,
                    Severity = conflict.Severity,
                    PeakSeverity = conflict.PeakSeverity,
                    Status = conflict.Status,
                    CreatedTotalDays = conflict.CreatedTotalDays,
                    CreatedTimeOfDay = conflict.CreatedTimeOfDay,
                    LastUpdatedTotalDays = conflict.LastUpdatedTotalDays,
                    LastUpdatedTimeOfDay = conflict.LastUpdatedTimeOfDay,
                    ResolvedTotalDays = conflict.ResolvedTotalDays,
                    ResolvedTimeOfDay = conflict.ResolvedTimeOfDay,
                    RecoveryMentionedTotalDays = conflict.RecoveryMentionedTotalDays,
                    RecoveryMentionedTimeOfDay = conflict.RecoveryMentionedTimeOfDay,
                    RepairScore = conflict.RepairScore,
                    ApologyCount = conflict.ApologyCount,
                    RequiresComplexRepair = conflict.RequiresComplexRepair,
                    RepairStage = conflict.RepairStage,
                    ApologyReceived = conflict.ApologyReceived,
                    MeaningfulGiftReceived = conflict.MeaningfulGiftReceived,
                    SpecificRepairTalkReceived = conflict.SpecificRepairTalkReceived,
                    MinimumRepairTotalDays = conflict.MinimumRepairTotalDays,
                    LastRepairGiftName = conflict.LastRepairGiftName,
                    RepairGrowthGranted = conflict.RepairGrowthGranted,
                    TimesReinforced = conflict.TimesReinforced
                })
                .ToList(),
            AiFriendshipGainedToday = this.AiFriendshipGainedToday,
            RelationshipTrustInitialized = this.RelationshipTrustInitialized,
            RelationshipTrust = this.RelationshipTrust,
            LastRelationshipTrustUpdatedTotalDays = this.LastRelationshipTrustUpdatedTotalDays,
            LastRelationshipTrustUpdatedTimeOfDay = this.LastRelationshipTrustUpdatedTimeOfDay,
            LastAiFriendshipTotalDays = this.LastAiFriendshipTotalDays,
            LastAiSmallGiftTotalDays = this.LastAiSmallGiftTotalDays,
            LastAiMeaningfulGiftTotalDays = this.LastAiMeaningfulGiftTotalDays,
            LastAiMoneyGiftTotalDays = this.LastAiMoneyGiftTotalDays,
            RecentAiGiftItemIds = this.RecentAiGiftItemIds.ToList(),
            LastDailyGiftOpportunityRollTotalDays = this.LastDailyGiftOpportunityRollTotalDays,
            DailyGiftOpportunityTotalDays = this.DailyGiftOpportunityTotalDays,
            DailyGiftOpportunityChancePercent = this.DailyGiftOpportunityChancePercent,
            DailyGiftOpportunityReason = this.DailyGiftOpportunityReason,
            PendingReciprocalGiftDueTotalDays = this.PendingReciprocalGiftDueTotalDays,
            PendingReciprocalGiftSourceGiftName = this.PendingReciprocalGiftSourceGiftName,
            PendingReciprocalGiftReason = this.PendingReciprocalGiftReason,
            LastAiFarmHelpTotalDays = this.LastAiFarmHelpTotalDays,
            LastAiWalkTogetherTotalDays = this.LastAiWalkTogetherTotalDays,
            LastHelpRequestTotalDays = this.LastHelpRequestTotalDays,
            LastHelpRequestTimeOfDay = this.LastHelpRequestTimeOfDay,
            LastSceneContext = this.LastSceneContext,
            LastSceneInfluence = this.LastSceneInfluence,
            LastSceneInfluenceReason = this.LastSceneInfluenceReason,
            CurrentInclination = this.CurrentInclination,
            LastInteraction = this.LastInteraction,
            FarmerNickname = this.FarmerNickname,
            FarmerNicknameStatus = this.FarmerNicknameStatus,
            FarmerNicknameTotalDays = this.FarmerNicknameTotalDays,
            FarmerNicknameTimeOfDay = this.FarmerNicknameTimeOfDay,
            LastUpdatedTotalDays = this.LastUpdatedTotalDays,
            LastUpdatedTimeOfDay = this.LastUpdatedTimeOfDay
        };
    }

    private IEnumerable<LongTermMemoryFact> GetTopLongTermMemories(int count)
    {
        return this.LongTermMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<PlayerPreferenceFact> GetTopPlayerPreferences(int count)
    {
        return this.PlayerPreferenceMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<CommunityImpressionFact> GetTopCommunityImpressions(int count)
    {
        return this.CommunityImpressions
            .OrderByDescending(CommunityImpressionStore.GetRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<SharedExperienceFact> GetTopSharedExperiences(int count)
    {
        return this.SharedExperiences
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(count);
    }

    private IEnumerable<NpcHelpRequestFact> GetTopHelpRequests(int count)
    {
        return this.HelpRequests
            .OrderBy(request => BehaviorMemory.HelpRequestStatusOrder(request.Status))
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(count);
    }

    private IEnumerable<NpcConflictFact> GetTopConflicts(int count)
    {
        return this.Conflicts
            .OrderBy(conflict => conflict.Status switch
            {
                "Active" => 0,
                "Recovering" => 1,
                "Resolved" => 2,
                _ => 3
            })
            .ThenByDescending(conflict => conflict.Severity)
            .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
            .Take(count);
    }
}
