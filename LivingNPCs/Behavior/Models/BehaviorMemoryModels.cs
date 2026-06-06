using System.Collections.Generic;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemorySaveData
{
    public Dictionary<string, List<BehaviorMemoryEntry>> EntriesByNpc { get; set; } = new();
    public Dictionary<string, LivingNpcState> StatesByNpc { get; set; } = new();
    public int LastStateDecayTotalDays { get; set; } = -1;
}

internal sealed class BehaviorMemoryEntry
{
    public string NpcName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Behavior";
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Season { get; set; } = string.Empty;
    public int Day { get; set; }
    public int TimeOfDay { get; set; }
    public int TotalDays { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string LocationDisplayName { get; set; } = string.Empty;
}

internal sealed record GiftMemoryDetails(
    string ItemId,
    string ItemName,
    string TasteLabel,
    string TastePromptLabel,
    int TasteScore
);

internal sealed record MemoryRecallContext(
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> Tokens
);

internal sealed record LongTermMemorySelection(
    LongTermMemoryFact Memory,
    int Score,
    string Reason
);

internal sealed record PlayerPreferenceSelection(
    PlayerPreferenceFact Memory,
    int Score,
    string Reason
);

internal sealed record CommunityImpressionSelection(
    CommunityImpressionFact Memory,
    int Score,
    string Reason
);

internal sealed record MemoryRecallPlan(
    MemoryRecallContext Context,
    IReadOnlyList<LongTermMemorySelection> LongTermMemories,
    IReadOnlyList<PlayerPreferenceSelection> PlayerPreferences
)
{
    public static MemoryRecallPlan Empty { get; } = new(
        new MemoryRecallContext(
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)),
        System.Array.Empty<LongTermMemorySelection>(),
        System.Array.Empty<PlayerPreferenceSelection>());
}

internal sealed class ValleyTalkExchangeAnalysis
{
    public int RapportDelta { get; set; }
    public bool EndConversation { get; set; }
    public ValleyTalkAmbientFollowUp AmbientFollowUp { get; set; } = new();
    public ValleyTalkEmotionImpact EmotionImpact { get; set; } = new();
    public List<ValleyTalkWorldActionRequest> Actions { get; set; } = new();
    public List<ValleyTalkBehaviorInfluenceCandidate> BehaviorInfluences { get; set; } = new();
    public List<ValleyTalkHelpRequestCandidate> HelpRequests { get; set; } = new();
    public List<ValleyTalkHelpRequestUpdateCandidate> HelpRequestUpdates { get; set; } = new();
    public List<ValleyTalkConflictCandidate> Conflicts { get; set; } = new();
    public List<ValleyTalkMemoryCandidate> Memories { get; set; } = new();
}

internal sealed class ValleyTalkMemoryCandidate
{
    public string Kind { get; set; } = "fact";
    public string Summary { get; set; } = string.Empty;
    public int Importance { get; set; }
    public bool PlayerPreference { get; set; }
    public string PlayerPreferenceKind { get; set; } = "none";
    public string Subject { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

internal sealed class LongTermMemoryFact
{
    public string Kind { get; set; } = "fact";
    public string Subject { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; }
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastRecalledTotalDays { get; set; } = -1;
    public int LastRecalledTimeOfDay { get; set; }
    public int RecallCount { get; set; }
    public int TimesReinforced { get; set; }
}

internal sealed class PlayerPreferenceFact
{
    public string PreferenceKind { get; set; } = "none";
    public string Subject { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int Importance { get; set; }
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastRecalledTotalDays { get; set; } = -1;
    public int LastRecalledTimeOfDay { get; set; }
    public int RecallCount { get; set; }
    public int TimesReinforced { get; set; }
}

internal sealed class CommunityImpressionFact
{
    public string SubjectNpcName { get; set; } = string.Empty;
    public string SubjectDisplayName { get; set; } = string.Empty;
    public string Kind { get; set; } = "relationship_trend";
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = "CloseCircle";
    public string Visibility { get; set; } = "Public";
    public int Confidence { get; set; }
    public int Importance { get; set; }
    public int TransmissionDepth { get; set; }
    public int DistortionLevel { get; set; }
    public string HeardFromNpcName { get; set; } = string.Empty;
    public string CircleKey { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastRecalledTotalDays { get; set; } = -1;
    public int LastRecalledTimeOfDay { get; set; }
    public int LastSharedTotalDays { get; set; } = -1;
    public int LastSharedTimeOfDay { get; set; }
    public int ShareCount { get; set; }
    public int ExpiresTotalDays { get; set; } = -1;
    public int RecallCount { get; set; }
    public int TimesReinforced { get; set; }

    public string FreshnessStage
    {
        get
        {
            int age = this.LastUpdatedTotalDays < 0
                ? int.MaxValue
                : System.Math.Max(0, Game1.Date.TotalDays - this.LastUpdatedTotalDays);
            int remaining = this.ExpiresTotalDays < 0
                ? int.MaxValue
                : this.ExpiresTotalDays - Game1.Date.TotalDays;
            if (remaining < 0)
            {
                return "expired";
            }

            if (age <= 1)
            {
                return "fresh";
            }

            if (age <= 5 && remaining >= 2)
            {
                return "settled";
            }

            return "fading";
        }
    }

    public string PromptLabel => this.Source switch
    {
        "Witnessed" => $"directly witnessed, {this.FreshnessStage} ({this.Visibility.ToLowerInvariant()}): {this.Summary}",
        "CloseCircle" => $"heard through a close connection after {this.TransmissionDepth} retelling(s), {this.FreshnessStage} ({this.Visibility.ToLowerInvariant()}): {this.Summary}",
        _ => $"picked up as a faint public impression after {this.TransmissionDepth} retelling(s), {this.FreshnessStage} ({this.Visibility.ToLowerInvariant()}): {this.Summary}"
    };
}

internal sealed class SharedExperienceFact
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int Importance { get; set; }
    public int TimesReinforced { get; set; }
    public int FollowUpEligibleTotalDays { get; set; } = -1;
    public int FollowUpShownTotalDays { get; set; } = -1;
    public int FollowUpShownTimeOfDay { get; set; }

    public string PromptLabel =>
        $"{this.Type} at {this.LocationLabel}; shared on total day {this.CreatedTotalDays}; summary: {this.Summary}";
}

internal sealed class DialogueBehaviorInfluenceFact
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = string.Empty;
    public string TargetLocationLabel { get; set; } = string.Empty;
    public int Intensity { get; set; }
    public string Status { get; set; } = "Active";
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int ExpiresTotalDays { get; set; } = -1;
    public int LastTriggeredTotalDays { get; set; } = -1;
    public int LastTriggeredTimeOfDay { get; set; }
    public int TriggerCount { get; set; }
    public int MaxTriggers { get; set; } = 1;
    public int TimesReinforced { get; set; }

    public string PromptLabel =>
        $"{this.Type}, intensity {this.Intensity}/100, target {this.TargetLocationLabel}, status {this.Status}, expires total day {this.ExpiresTotalDays}; summary: {this.Summary}";
}

internal sealed class NpcHelpRequestFact
{
    public string NpcDisplayName { get; set; } = string.Empty;
    public string QuestLogId { get; set; } = string.Empty;
    public string Type { get; set; } = "item_request";
    public string Summary { get; set; } = string.Empty;
    public List<NpcHelpRequestStepFact> Steps { get; set; } = new();
    public int CurrentStepIndex { get; set; }
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public int DueTotalDays { get; set; } = -1;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Resolution { get; set; } = string.Empty;
    public string FollowUpPotential { get; set; } = "none";
    public string FailureReaction { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int AcceptedTotalDays { get; set; } = -1;
    public int AcceptedTimeOfDay { get; set; }
    public int DeclinedTotalDays { get; set; } = -1;
    public int DeclinedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int LastMentionedTotalDays { get; set; } = -1;
    public int LastMentionedTimeOfDay { get; set; }
    public int FulfilledTotalDays { get; set; } = -1;
    public int FulfilledTimeOfDay { get; set; }
    public int FollowUpEligibleTotalDays { get; set; } = -1;
    public int FollowUpShownTotalDays { get; set; } = -1;
    public int FollowUpShownTimeOfDay { get; set; }
    public int RewardFriendship { get; set; }
    public bool RewardGranted { get; set; }
    public int RewardMoney { get; set; }
    public bool RewardMoneyGranted { get; set; }
    public bool RewardMoneyByMail { get; set; }
    public string RewardMoneyMailKey { get; set; } = string.Empty;
    public int RewardMoneyMailTotalDays { get; set; } = -1;
    public bool RewardGiftGiven { get; set; }
    public bool SpecialFollowUpPlanned { get; set; }
    public int TimesReinforced { get; set; }

    public string PromptLabel =>
        $"{this.Type}, due on total day {this.DueTotalDays}, status {this.Status}, step {System.Math.Min(this.CurrentStepIndex + 1, System.Math.Max(1, this.Steps.Count))}/{System.Math.Max(1, this.Steps.Count)}; current step: {this.CurrentStepPromptLabel}; summary: {this.Summary}";

    public string FulfilledPromptLabel =>
        $"{this.Type} was fulfilled on total day {this.FulfilledTotalDays}; summary: {this.Summary}; follow-up potential: {this.FollowUpPotential}";

    public string CurrentStepPromptLabel
    {
        get
        {
            var step = this.Steps.Count == 0
                ? null
                : this.Steps[System.Math.Clamp(this.CurrentStepIndex, 0, this.Steps.Count - 1)];
            if (step == null)
            {
                return this.Type == "item_request"
                    ? $"bring {this.RequestedItemLabel} {this.RequestedItemId}".Trim()
                    : this.QuestionTopic;
            }

            return step.PromptLabel;
        }
    }
}

internal sealed class NpcGiftMailFact
{
    public string MailKey { get; set; } = string.Empty;
    public string NpcDisplayName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public string Motive { get; set; } = "daily";
    public string Reason { get; set; } = string.Empty;
    public string SourceGiftName { get; set; } = string.Empty;
    public string Tier { get; set; } = "small";
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int DueTotalDays { get; set; } = -1;
    public bool QueuedForDelivery { get; set; }
}

internal sealed class NpcHelpRequestStepFact
{
    public string Type { get; set; } = "item_request";
    public string Summary { get; set; } = string.Empty;
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string Resolution { get; set; } = string.Empty;
    public int CompletedTotalDays { get; set; } = -1;
    public int CompletedTimeOfDay { get; set; }

    public string PromptLabel => this.Type == "item_request"
        ? $"item step: {this.Summary}; needs {this.RequestedItemLabel} {this.RequestedItemId}; status {this.Status}"
        : $"conversation step: {this.Summary}; topic {this.QuestionTopic}; status {this.Status}";
}

internal sealed class NpcConflictFact
{
    public string CauseKind { get; set; } = "dialogue";
    public string Summary { get; set; } = string.Empty;
    public int Severity { get; set; }
    public int PeakSeverity { get; set; }
    public string Status { get; set; } = "Active";
    public int CreatedTotalDays { get; set; } = -1;
    public int CreatedTimeOfDay { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int LastUpdatedTimeOfDay { get; set; }
    public int ResolvedTotalDays { get; set; } = -1;
    public int ResolvedTimeOfDay { get; set; }
    public int RecoveryMentionedTotalDays { get; set; } = -1;
    public int RecoveryMentionedTimeOfDay { get; set; }
    public int RepairScore { get; set; }
    public int ApologyCount { get; set; }
    public bool RequiresComplexRepair { get; set; }
    public string RepairStage { get; set; } = "Simple";
    public bool ApologyReceived { get; set; }
    public bool MeaningfulGiftReceived { get; set; }
    public bool SpecificRepairTalkReceived { get; set; }
    public int MinimumRepairTotalDays { get; set; } = -1;
    public string LastRepairGiftName { get; set; } = string.Empty;
    public bool RepairGrowthGranted { get; set; }
    public int TimesReinforced { get; set; }

    public string PromptLabel => $"{this.Status.ToLowerInvariant()} conflict, severity {this.Severity}/100, cause {this.CauseKind}, repair stage {this.RepairStage}: {this.Summary}";
    public string ResolvedPromptLabel => $"resolved conflict from total day {this.CreatedTotalDays}, cause {this.CauseKind}: {this.Summary}";
}

internal sealed class ValleyTalkAmbientFollowUp
{
    public string Text { get; set; } = string.Empty;
    public int DelayMinutes { get; set; }
}

internal sealed class ValleyTalkEmotionImpact
{
    public string Emotion { get; set; } = "none";
    public int IntensityDelta { get; set; }
    public bool Apology { get; set; }
    public int RepairDelta { get; set; }
    public string Reason { get; set; } = string.Empty;

    public bool HasEffect => this.Emotion != "none"
        || this.IntensityDelta != 0
        || this.Apology
        || this.RepairDelta > 0;
}

internal sealed class ValleyTalkWorldActionRequest
{
    public string Type { get; set; } = "none";
    public int Amount { get; set; }
    public int TileCount { get; set; }
    public int DurationMinutes { get; set; }
    public int DelayMinutes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = string.Empty;
    public string QuestHint { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
}

internal sealed class ValleyTalkBehaviorInfluenceCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public string TargetLocation { get; set; } = string.Empty;
    public string TargetLocationLabel { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public int Intensity { get; set; }
    public int MaxTriggers { get; set; }
}

internal sealed class ValleyTalkHelpRequestCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public bool RequiresAcceptance { get; set; } = true;
    public List<ValleyTalkHelpRequestStepCandidate> Steps { get; set; } = new();
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public int DueInDays { get; set; } = 3;
    public string Reason { get; set; } = string.Empty;
    public string FollowUpPotential { get; set; } = "none";
}

internal sealed class ValleyTalkHelpRequestStepCandidate
{
    public string Type { get; set; } = "none";
    public string Summary { get; set; } = string.Empty;
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
}

internal sealed class ValleyTalkHelpRequestUpdateCandidate
{
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "none";
    public string Resolution { get; set; } = string.Empty;
}

internal sealed class ValleyTalkConflictCandidate
{
    public string CauseKind { get; set; } = "dialogue";
    public string Summary { get; set; } = string.Empty;
    public int Severity { get; set; }
}

internal sealed record ValleyTalkExchangeResult(
    int LongTermMemoriesStored,
    int PlayerPreferencesStored,
    int HelpRequestsStored,
    int HelpRequestsUpdated,
    int ConflictsStored,
    int BehaviorInfluencesStored,
    int ConflictsResolved,
    bool EmotionChanged,
    int AppliedFriendshipDelta,
    int RequestedFriendshipDelta,
    bool EndConversation,
    string AmbientFollowUpText,
    int AmbientFollowUpDelayMinutes,
    IReadOnlyList<ValleyTalkWorldActionRequest> Actions,
    IReadOnlyList<NpcHelpRequestFact> FulfilledHelpRequests
)
{
    public bool HasEffect => this.LongTermMemoriesStored > 0
        || this.PlayerPreferencesStored > 0
        || this.HelpRequestsStored > 0
        || this.HelpRequestsUpdated > 0
        || this.ConflictsStored > 0
        || this.BehaviorInfluencesStored > 0
        || this.ConflictsResolved > 0
        || this.EmotionChanged
        || this.AppliedFriendshipDelta > 0
        || !string.IsNullOrWhiteSpace(this.AmbientFollowUpText)
        || this.Actions.Count > 0
        || this.FulfilledHelpRequests.Count > 0;
}
