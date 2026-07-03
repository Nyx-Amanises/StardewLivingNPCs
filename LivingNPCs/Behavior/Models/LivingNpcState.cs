using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class LivingNpcState
{
    /// <summary>Claimed gift-mail facts older than this are dropped when the state is clamped.</summary>
    private const int ClaimedGiftMailRetentionDays = 14;

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
    public int LastDailyHelpRequestOpportunityRollTotalDays { get; set; } = -1;
    public int DailyHelpRequestOpportunityTotalDays { get; set; } = -1;
    public int PendingReciprocalGiftDueTotalDays { get; set; } = -1;
    public string PendingReciprocalGiftSourceGiftName { get; set; } = string.Empty;
    public string PendingReciprocalGiftReason { get; set; } = string.Empty;
    public List<NpcGiftMailFact> GiftMails { get; set; } = new();
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

    public bool HasUnresolvedConflict => this.Conflicts.Any(conflict => conflict.Status is "Active" or "Recovering");

    public int HighestUnresolvedConflictSeverity => this.Conflicts
        .Where(conflict => conflict.Status is "Active" or "Recovering")
        .Select(conflict => conflict.Severity)
        .DefaultIfEmpty(0)
        .Max();

    public IEnumerable<DialogueBehaviorInfluenceFact> ActiveDialogueBehaviorInfluences =>
        this.DialogueBehaviorInfluences.Where(influence =>
            influence.Status == "Active"
            && influence.ExpiresTotalDays >= Game1.Date.TotalDays
            && influence.TriggerCount < System.Math.Max(1, influence.MaxTriggers));

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
                if (request.RewardMoneyGranted
                    || request.Status != "Fulfilled")
                {
                    request.RewardMoneyClaimQueued = false;
                    request.RewardMoneyQuestPosted = false;
                }
                else if (request.RewardMoneyQuestPosted)
                {
                    request.RewardMoneyClaimQueued = true;
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
            // Live question_requests are legacy data with no completion path (current builds only
            // create item requests): left alone they can only expire and punish the player, so
            // drop them; the next quest-log sync removes their proxy entries.
            .Where(request => !(request.Type == "question_request" && request.Status is "Offered" or "Pending"))
            .OrderBy(BehaviorMemory.HelpRequestRetentionRank)
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

        if (this.LastDailyHelpRequestOpportunityRollTotalDays != Game1.Date.TotalDays)
        {
            this.DailyHelpRequestOpportunityTotalDays = -1;
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
                mail.NpcName = mail.NpcName?.Trim() ?? string.Empty;
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
                    "help_request_reward" => "help_request_reward",
                    "birthday" => "birthday",
                    _ => "daily"
                };
                mail.Reason = mail.Reason?.Trim() ?? string.Empty;
                mail.SourceGiftName = mail.SourceGiftName?.Trim() ?? string.Empty;
                mail.Tier = mail.Tier == "meaningful" ? "meaningful" : "small";
                mail.DueTotalDays = mail.DueTotalDays < 0
                    ? Game1.Date.TotalDays + 1
                    : mail.DueTotalDays;
                if (!mail.Claimed)
                {
                    mail.ClaimedTotalDays = -1;
                    mail.ClaimedTimeOfDay = 0;
                }

                mail.GeneratedBody = mail.GeneratedBody?.Trim() ?? string.Empty;
                mail.GenerationStatus = (mail.GenerationStatus ?? "none").Trim().ToLowerInvariant() switch
                {
                    "pending" => "pending",
                    "ready" => mail.GeneratedBody.Length > 0 ? "ready" : "none",
                    "failed" => "failed",
                    _ => "none"
                };
                mail.GenerationAttempts = System.Math.Max(0, mail.GenerationAttempts);

                return mail;
            })
            .Where(mail => !mail.Claimed
                || mail.ClaimedTotalDays < 0
                || Game1.Date.TotalDays - mail.ClaimedTotalDays <= ClaimedGiftMailRetentionDays)
            .OrderBy(GiftMailRetentionRank)
            .ThenByDescending(mail => mail.CreatedTotalDays)
            .ThenByDescending(mail => mail.CreatedTimeOfDay)
            .Take(12)
            .ToList();
    }

    /// <summary>
    /// Priority when the gift-mail list is clamped to its cap. Delivered-but-unclaimed mails map to a
    /// real letter already sitting in the mailbox/tomorrow queue — dropping their fact would orphan
    /// that letter (blank mail, attached item lost), so they are kept first. Undelivered mails are
    /// kept next (dropping one silently cancels a planned gift). Claimed mails are history only.
    /// </summary>
    private static int GiftMailRetentionRank(NpcGiftMailFact mail)
    {
        if (mail.Claimed)
        {
            return 2;
        }

        return mail.QueuedForDelivery ? 0 : 1;
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
                    RewardMoneyClaimQueued = request.RewardMoneyClaimQueued,
                    RewardMoneyQuestPosted = request.RewardMoneyQuestPosted,
                    RewardGiftGiven = request.RewardGiftGiven,
                    SpecialFollowUpPlanned = request.SpecialFollowUpPlanned,
                    TimesReinforced = request.TimesReinforced
                })
                .ToList(),
            GiftMails = this.GiftMails
                .Select(mail => new NpcGiftMailFact
                {
                    MailKey = mail.MailKey,
                    NpcName = mail.NpcName,
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
                    QueuedForDelivery = mail.QueuedForDelivery,
                    Claimed = mail.Claimed,
                    ClaimedTotalDays = mail.ClaimedTotalDays,
                    ClaimedTimeOfDay = mail.ClaimedTimeOfDay,
                    GeneratedBody = mail.GeneratedBody,
                    GenerationStatus = mail.GenerationStatus,
                    GenerationAttempts = mail.GenerationAttempts
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
            LastDailyHelpRequestOpportunityRollTotalDays = this.LastDailyHelpRequestOpportunityRollTotalDays,
            DailyHelpRequestOpportunityTotalDays = this.DailyHelpRequestOpportunityTotalDays,
            PendingReciprocalGiftDueTotalDays = this.PendingReciprocalGiftDueTotalDays,
            PendingReciprocalGiftSourceGiftName = this.PendingReciprocalGiftSourceGiftName,
            PendingReciprocalGiftReason = this.PendingReciprocalGiftReason,
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

    internal IEnumerable<LongTermMemoryFact> GetTopLongTermMemories(int count)
    {
        return this.LongTermMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    internal IEnumerable<PlayerPreferenceFact> GetTopPlayerPreferences(int count)
    {
        return this.PlayerPreferenceMemories
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    internal IEnumerable<CommunityImpressionFact> GetTopCommunityImpressions(int count)
    {
        return this.CommunityImpressions
            .OrderByDescending(CommunityImpressionStore.GetRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(count);
    }

    internal IEnumerable<SharedExperienceFact> GetTopSharedExperiences(int count)
    {
        return this.SharedExperiences
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(count);
    }

    internal IEnumerable<NpcHelpRequestFact> GetTopHelpRequests(int count)
    {
        return this.HelpRequests
            .OrderBy(request => BehaviorMemory.HelpRequestStatusOrder(request.Status))
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(count);
    }

    internal IEnumerable<NpcConflictFact> GetTopConflicts(int count)
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
