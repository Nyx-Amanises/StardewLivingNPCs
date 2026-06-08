using System;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class ConflictEmotionMemoryService
{
    public static bool ApplyDialogueEmotionImpact(
        LivingNpcState state,
        ValleyTalkEmotionImpact impact,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        if (!impact.HasEffect)
        {
            return false;
        }

        string reason = string.IsNullOrWhiteSpace(impact.Reason)
            ? "the latest conversation changed how they felt"
            : impact.Reason;
        if (impact.Emotion == "none")
        {
            if (impact.IntensityDelta < 0)
            {
                state.EmotionIntensity = LivingNpcState.ClampScore(state.EmotionIntensity + impact.IntensityDelta);
                if (state.EmotionIntensity == 0)
                {
                    state.CurrentEmotion = "Calm";
                }

                state.LastEmotionReason = reason;
                state.LastEmotionUpdatedTotalDays = currentTotalDays;
                state.LastEmotionUpdatedTimeOfDay = currentTimeOfDay;
                return true;
            }

            return false;
        }

        ApplyEmotion(state, impact.Emotion, impact.IntensityDelta, reason, currentTotalDays, currentTimeOfDay);
        return true;
    }

    public static void ApplyEmotion(
        LivingNpcState state,
        string emotion,
        int intensityDelta,
        string reason,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        string normalizedEmotion = BehaviorValueNormalizer.NormalizeEmotion(emotion);
        if (normalizedEmotion == "none")
        {
            return;
        }

        int baseIntensity = state.CurrentEmotion == normalizedEmotion
            ? state.EmotionIntensity
            : normalizedEmotion == "Calm"
                ? 0
                : Math.Max(10, state.EmotionIntensity / 2);
        state.CurrentEmotion = normalizedEmotion == "none" ? "Calm" : normalizedEmotion;
        state.EmotionIntensity = LivingNpcState.ClampScore(baseIntensity + intensityDelta);
        if (state.EmotionIntensity == 0)
        {
            state.CurrentEmotion = "Calm";
        }

        state.LastEmotionReason = string.IsNullOrWhiteSpace(reason)
            ? "the latest interaction changed how they felt"
            : reason.Trim();
        state.LastEmotionUpdatedTotalDays = currentTotalDays;
        state.LastEmotionUpdatedTimeOfDay = currentTimeOfDay;
    }

    public static void ApplyRelationshipTrustDelta(
        LivingNpcState state,
        int delta,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        if (delta == 0)
        {
            return;
        }

        state.RelationshipTrust = LivingNpcState.ClampScore(state.RelationshipTrust + delta);
        state.LastRelationshipTrustUpdatedTotalDays = currentTotalDays;
        state.LastRelationshipTrustUpdatedTimeOfDay = currentTimeOfDay;
    }

    public static bool StoreConflict(
        LivingNpcState state,
        ValleyTalkConflictCandidate candidate,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        string normalizedSummary = BehaviorValueNormalizer.NormalizeMemorySummary(candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedSummary) || candidate.Severity <= 0)
        {
            return false;
        }

        var existing = state.Conflicts.FirstOrDefault(conflict =>
            conflict.Status != "Resolved"
            && BehaviorValueNormalizer.NormalizeMemorySummary(conflict.Summary) == normalizedSummary);
        if (existing != null)
        {
            existing.CauseKind = candidate.CauseKind;
            existing.Severity = LivingNpcState.ClampScore(Math.Max(existing.Severity, candidate.Severity) + 8);
            existing.PeakSeverity = Math.Max(existing.PeakSeverity, existing.Severity);
            if (existing.PeakSeverity >= 60)
            {
                existing.RequiresComplexRepair = true;
                existing.MinimumRepairTotalDays = Math.Max(
                    existing.MinimumRepairTotalDays,
                    currentTotalDays + ConflictRepairService.GetComplexRepairDelayDays(state, existing.PeakSeverity)
                );
                existing.SpecificRepairTalkReceived = false;
                if (candidate.Severity >= 30)
                {
                    existing.MeaningfulGiftReceived = false;
                }
            }

            ConflictRepairService.RefreshStage(existing, currentTotalDays);
            existing.Status = ConflictRepairService.GetConflictStatus(existing.Severity);
            existing.LastUpdatedTotalDays = currentTotalDays;
            existing.LastUpdatedTimeOfDay = currentTimeOfDay;
            existing.TimesReinforced += 1;
            ApplyRelationshipTrustDelta(state, -Math.Max(2, ConflictRepairService.GetConflictTrustLoss(candidate.Severity) / 2), currentTotalDays, currentTimeOfDay);
            ApplyEmotionForConflict(state, existing, currentTotalDays, currentTimeOfDay);
            return true;
        }

        var conflict = new NpcConflictFact
        {
            CauseKind = candidate.CauseKind,
            Summary = candidate.Summary.Trim(),
            Severity = LivingNpcState.ClampScore(candidate.Severity),
            PeakSeverity = LivingNpcState.ClampScore(candidate.Severity),
            Status = ConflictRepairService.GetConflictStatus(candidate.Severity),
            CreatedTotalDays = currentTotalDays,
            CreatedTimeOfDay = currentTimeOfDay,
            LastUpdatedTotalDays = currentTotalDays,
            LastUpdatedTimeOfDay = currentTimeOfDay,
            RequiresComplexRepair = candidate.Severity >= 60,
            MinimumRepairTotalDays = candidate.Severity >= 60
                ? currentTotalDays + ConflictRepairService.GetComplexRepairDelayDays(state, candidate.Severity)
                : -1,
            TimesReinforced = 1
        };
        ConflictRepairService.RefreshStage(conflict, currentTotalDays);
        state.Conflicts.Add(conflict);
        state.Conflicts = state.Conflicts
            .OrderBy(conflictEntry => BehaviorMemory.ConflictStatusOrder(conflictEntry.Status))
            .ThenByDescending(conflictEntry => conflictEntry.Severity)
            .ThenByDescending(conflictEntry => conflictEntry.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        ApplyRelationshipTrustDelta(state, -ConflictRepairService.GetConflictTrustLoss(conflict.Severity), currentTotalDays, currentTimeOfDay);
        ApplyEmotionForConflict(state, conflict, currentTotalDays, currentTimeOfDay);
        return true;
    }

    public static int ApplyConflictRepair(
        LivingNpcState state,
        int repairDelta,
        bool apology,
        bool specificRepairTalk,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        var update = ConflictRepairService.ApplyRepair(
            state,
            repairDelta,
            apology,
            specificRepairTalk,
            emotionalStyle,
            currentTotalDays,
            currentTimeOfDay
        );
        ApplyConflictRepairUpdate(state, update, currentTotalDays, currentTimeOfDay);

        if (update.ResolvedCount > 0)
        {
            ApplyEmotion(state, "Calm", -state.EmotionIntensity, "an earlier conflict has been repaired", currentTotalDays, currentTimeOfDay);
        }

        return update.ResolvedCount;
    }

    public static void MarkRepairGiftReceived(
        LivingNpcState state,
        string giftName,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        ConflictRepairService.MarkRepairGiftReceived(state, giftName, currentTotalDays, currentTimeOfDay);
    }

    public static void DecayEmotionAndConflicts(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        EmotionalExpressionCue emotionalStyle,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        var update = ConflictRepairService.DecayEmotionAndConflicts(
            state,
            emotionDailyDecay,
            conflictDailyDecay,
            emotionalStyle,
            currentTotalDays,
            currentTimeOfDay
        );
        ApplyConflictRepairUpdate(state, update, currentTotalDays, currentTimeOfDay);
    }

    private static void ApplyConflictRepairUpdate(
        LivingNpcState state,
        ConflictRepairUpdate update,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        if (update.ComplexRepairGrowthAwards <= 0)
        {
            return;
        }

        ApplyRelationshipTrustDelta(state, update.ComplexRepairGrowthAwards * 8, currentTotalDays, currentTimeOfDay);
        state.Familiarity = LivingNpcState.ClampScore(state.Familiarity + (update.ComplexRepairGrowthAwards * 2));
    }

    private static void ApplyEmotionForConflict(
        LivingNpcState state,
        NpcConflictFact conflict,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        string emotion = conflict.CauseKind == "promise"
            ? "Disappointed"
            : conflict.Severity switch
        {
            >= 70 => "Angry",
            >= 35 => "Upset",
            _ => "Uneasy"
        };
        ApplyEmotion(state, emotion, conflict.Severity / 2, conflict.Summary, currentTotalDays, currentTimeOfDay);
    }
}
