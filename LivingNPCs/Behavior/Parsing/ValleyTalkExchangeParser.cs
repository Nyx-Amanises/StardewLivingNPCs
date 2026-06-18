using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LivingNPCs.Behavior;

internal static class ValleyTalkExchangeParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ValleyTalkExchangeAnalysis Parse(string analysisJson)
    {
        if (string.IsNullOrWhiteSpace(analysisJson))
        {
            return new ValleyTalkExchangeAnalysis();
        }

        try
        {
            var analysis = JsonSerializer.Deserialize<ValleyTalkExchangeAnalysis>(
                analysisJson,
                SerializerOptions
            ) ?? new ValleyTalkExchangeAnalysis();

            analysis.RapportDelta = System.Math.Clamp(analysis.RapportDelta, 0, 30);
            analysis.AmbientFollowUp ??= new ValleyTalkAmbientFollowUp();
            analysis.AmbientFollowUp.Text = analysis.AmbientFollowUp.Text?.Trim() ?? string.Empty;
            analysis.AmbientFollowUp.DelayMinutes = System.Math.Clamp(analysis.AmbientFollowUp.DelayMinutes, 0, 120);
            analysis.EmotionImpact ??= new ValleyTalkEmotionImpact();
            analysis.EmotionImpact.Emotion = BehaviorValueNormalizer.NormalizeEmotion(analysis.EmotionImpact.Emotion);
            analysis.EmotionImpact.IntensityDelta = System.Math.Clamp(analysis.EmotionImpact.IntensityDelta, -100, 100);
            analysis.EmotionImpact.RepairDelta = System.Math.Clamp(analysis.EmotionImpact.RepairDelta, 0, 100);
            analysis.EmotionImpact.Reason = analysis.EmotionImpact.Reason?.Trim() ?? string.Empty;
            analysis.Memories = analysis.Memories
                .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
                .Select(memory =>
                {
                    memory.Kind = BehaviorValueNormalizer.NormalizeLongTermMemoryKind(memory.Kind);
                    memory.Summary = memory.Summary.Trim();
                    memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
                    memory.PlayerPreferenceKind = BehaviorValueNormalizer.NormalizePlayerPreferenceKind(memory.PlayerPreferenceKind);
                    memory.Subject = memory.Subject?.Trim() ?? string.Empty;
                    memory.Tags = BehaviorValueNormalizer.NormalizeMemoryTags(memory.Tags, memory.Subject, memory.Summary);
                    memory.PlayerPreference = memory.PlayerPreference && memory.PlayerPreferenceKind != "none";
                    return memory;
                })
                .Take(4)
                .ToList();
            analysis.Actions = analysis.Actions
                .Where(action => action != null)
                .Select(action =>
                {
                    action.Type = BehaviorValueNormalizer.NormalizeWorldActionType(action.Type);
                    action.Reason = action.Reason?.Trim() ?? string.Empty;
                    action.Amount = System.Math.Clamp(action.Amount, 0, 250);
                    action.TileCount = System.Math.Clamp(action.TileCount, 0, 12);
                    action.DurationMinutes = action.Type == "companion_outing"
                        ? System.Math.Clamp(
                            action.DurationMinutes <= 0
                                ? CompanionOutingRules.MinimumStayMinutes
                                : action.DurationMinutes,
                            CompanionOutingRules.MinimumStayMinutes,
                            600
                        )
                        : System.Math.Clamp(action.DurationMinutes, 0, 20);
                    action.DelayMinutes = System.Math.Clamp(action.DelayMinutes, 0, 20);
                    action.TargetLocation = action.TargetLocation?.Trim() ?? string.Empty;
                    action.TravelConsent = BehaviorValueNormalizer.NormalizeTravelConsent(action.TravelConsent);
                    action.QuestHint = action.QuestHint?.Trim() ?? string.Empty;
                    action.ItemId = action.ItemId?.Trim() ?? string.Empty;
                    action.ItemLabel = action.ItemLabel?.Trim() ?? string.Empty;
                    return action;
                })
                .Where(action => action.Type != "none")
                .Take(1)
                .ToList();
            analysis.BehaviorInfluences = analysis.BehaviorInfluences
                .Where(influence => influence != null && !string.IsNullOrWhiteSpace(influence.Summary))
                .Select(influence =>
                {
                    influence.Type = BehaviorValueNormalizer.NormalizeDialogueBehaviorInfluenceType(influence.Type);
                    influence.Summary = influence.Summary.Trim();
                    influence.TargetLocation = TravelLocationRules.Normalize(influence.TargetLocation, string.Empty);
                    influence.TargetLocationLabel = influence.TargetLocationLabel?.Trim() ?? string.Empty;
                    influence.DurationDays = System.Math.Clamp(influence.DurationDays, 0, 7);
                    influence.Intensity = System.Math.Clamp(influence.Intensity, 0, 100);
                    influence.MaxTriggers = System.Math.Clamp(influence.MaxTriggers, 0, 4);
                    return influence;
                })
                .Where(influence => influence.Type != "none")
                .Take(2)
                .ToList();
            analysis.HelpRequests = analysis.HelpRequests
                .Where(request => request != null && !string.IsNullOrWhiteSpace(request.Summary))
                .Select(request =>
                {
                    request.Type = BehaviorValueNormalizer.NormalizeHelpRequestType(request.Type);
                    request.Summary = request.Summary.Trim();
                    request.RequestedItemId = request.RequestedItemId?.Trim() ?? string.Empty;
                    request.RequestedItemLabel = request.RequestedItemLabel?.Trim() ?? string.Empty;
                    request.QuestionTopic = request.QuestionTopic?.Trim() ?? string.Empty;
                    request.DueInDays = System.Math.Clamp(request.DueInDays, 1, 7);
                    request.Reason = request.Reason?.Trim() ?? string.Empty;
                    request.FollowUpPotential = BehaviorValueNormalizer.NormalizeHelpRequestFollowUpPotential(request.FollowUpPotential);
                    request.Steps = (request.Steps ?? new List<ValleyTalkHelpRequestStepCandidate>())
                        .Where(step => step != null)
                        .Select(step =>
                        {
                            step.Type = BehaviorValueNormalizer.NormalizeHelpRequestType(step.Type);
                            step.Summary = step.Summary?.Trim() ?? string.Empty;
                            step.RequestedItemId = step.RequestedItemId?.Trim() ?? string.Empty;
                            step.RequestedItemLabel = step.RequestedItemLabel?.Trim() ?? string.Empty;
                            step.QuestionTopic = step.QuestionTopic?.Trim() ?? string.Empty;
                            return step;
                        })
                        .Where(step => step.Type != "none" && !string.IsNullOrWhiteSpace(step.Summary))
                        .Take(3)
                        .ToList();
                    return request;
                })
                .Where(request => request.Type != "none")
                .Take(1)
                .ToList();
            analysis.HelpRequestUpdates = analysis.HelpRequestUpdates
                .Where(update => update != null && !string.IsNullOrWhiteSpace(update.Summary))
                .Select(update =>
                {
                    update.Summary = update.Summary.Trim();
                    update.Status = BehaviorValueNormalizer.NormalizeHelpRequestUpdateStatus(update.Status);
                    update.Resolution = update.Resolution?.Trim() ?? string.Empty;
                    return update;
                })
                .Where(update => update.Status != "none")
                .Take(2)
                .ToList();
            analysis.Conflicts = analysis.Conflicts
                .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary))
                .Select(conflict =>
                {
                    conflict.CauseKind = BehaviorValueNormalizer.NormalizeConflictCauseKind(conflict.CauseKind);
                    conflict.Summary = conflict.Summary.Trim();
                    conflict.Severity = System.Math.Clamp(conflict.Severity, 0, 100);
                    return conflict;
                })
                .Where(conflict => conflict.Severity > 0)
                .Take(2)
                .ToList();
            return analysis;
        }
        catch
        {
            return new ValleyTalkExchangeAnalysis();
        }
    }
}
