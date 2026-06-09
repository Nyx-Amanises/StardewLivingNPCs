using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class DialogueBehaviorInfluenceStore
{
    public const int MaxInfluencesPerNpc = 12;

    public static bool Store(
        LivingNpcState state,
        ValleyTalkBehaviorInfluenceCandidate candidate,
        string fallbackLocation,
        int maxDialogueBehaviorInfluenceDays,
        int currentTotalDays,
        int currentTimeOfDay,
        out DialogueBehaviorInfluenceFact? storedInfluence)
    {
        storedInfluence = null;

        string type = NormalizeType(candidate.Type);
        if (type == "none" || string.IsNullOrWhiteSpace(candidate.Summary))
        {
            return false;
        }

        state.DialogueBehaviorInfluences ??= new List<DialogueBehaviorInfluenceFact>();

        string targetLocation = TravelLocationRules.Normalize(candidate.TargetLocation, fallbackLocation);
        string targetLabel = string.IsNullOrWhiteSpace(candidate.TargetLocationLabel)
            ? TravelLocationRules.GetLabel(targetLocation)
            : candidate.TargetLocationLabel.Trim();
        int durationDays = candidate.DurationDays <= 0
            ? GetDefaultDurationDays(type)
            : candidate.DurationDays;
        durationDays = System.Math.Clamp(
            durationDays,
            0,
            System.Math.Clamp(maxDialogueBehaviorInfluenceDays, 1, 7)
        );
        int maxTriggers = candidate.MaxTriggers <= 0
            ? GetDefaultMaxTriggers(type)
            : candidate.MaxTriggers;
        string normalizedKey = BuildKey(type, candidate.Summary, targetLocation);

        var existing = state.DialogueBehaviorInfluences.FirstOrDefault(influence =>
            BuildKey(influence.Type, influence.Summary, influence.TargetLocation) == normalizedKey
            && influence.Status == "Active");
        if (existing != null)
        {
            existing.Summary = candidate.Summary.Trim();
            existing.TargetLocation = targetLocation;
            existing.TargetLocationLabel = targetLabel;
            existing.Intensity = System.Math.Max(existing.Intensity, candidate.Intensity);
            existing.ExpiresTotalDays = System.Math.Max(existing.ExpiresTotalDays, currentTotalDays + durationDays);
            existing.MaxTriggers = System.Math.Max(existing.MaxTriggers, maxTriggers);
            existing.LastUpdatedTotalDays = currentTotalDays;
            existing.LastUpdatedTimeOfDay = currentTimeOfDay;
            existing.TimesReinforced += 1;
            storedInfluence = existing;
            return true;
        }

        var influence = new DialogueBehaviorInfluenceFact
        {
            Type = type,
            Summary = candidate.Summary.Trim(),
            TargetLocation = targetLocation,
            TargetLocationLabel = targetLabel,
            Intensity = System.Math.Clamp(candidate.Intensity <= 0 ? GetDefaultIntensity(type) : candidate.Intensity, 1, 100),
            CreatedTotalDays = currentTotalDays,
            CreatedTimeOfDay = currentTimeOfDay,
            LastUpdatedTotalDays = currentTotalDays,
            LastUpdatedTimeOfDay = currentTimeOfDay,
            ExpiresTotalDays = currentTotalDays + durationDays,
            MaxTriggers = System.Math.Clamp(maxTriggers, 1, 4),
            Status = "Active",
            TimesReinforced = 1
        };

        state.DialogueBehaviorInfluences.Add(influence);
        SortAndTrim(state);
        storedInfluence = influence;
        return true;
    }

    public static void Refresh(LivingNpcState state, int currentTotalDays)
    {
        state.DialogueBehaviorInfluences ??= new List<DialogueBehaviorInfluenceFact>();
        state.DialogueBehaviorInfluences = state.DialogueBehaviorInfluences
            .Where(influence => influence != null && !string.IsNullOrWhiteSpace(influence.Summary))
            .Select(influence => NormalizeForStore(influence, currentTotalDays))
            .Where(influence => influence.Type != "none")
            .OrderBy(influence => StatusOrder(influence.Status))
            .ThenBy(influence => influence.ExpiresTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTimeOfDay)
            .Take(MaxInfluencesPerNpc)
            .ToList();
    }

    public static IEnumerable<DialogueBehaviorInfluenceFact> OrderForDisplay(IEnumerable<DialogueBehaviorInfluenceFact> influences)
    {
        return influences
            .OrderBy(influence => StatusOrder(influence.Status))
            .ThenBy(influence => influence.ExpiresTotalDays);
    }

    public static DialogueBehaviorInfluenceFact NormalizeForStore(DialogueBehaviorInfluenceFact influence, int currentTotalDays)
    {
        influence.Type = NormalizeType(influence.Type);
        influence.Summary = influence.Summary.Trim();
        influence.TargetLocation = TravelLocationRules.Normalize(influence.TargetLocation, "Town");
        influence.TargetLocationLabel = string.IsNullOrWhiteSpace(influence.TargetLocationLabel)
            ? influence.TargetLocation
            : influence.TargetLocationLabel.Trim();
        influence.Intensity = LivingNpcState.ClampScore(influence.Intensity);
        influence.Status = NormalizeStatus(influence.Status);
        if (influence.Status == "Active" && influence.ExpiresTotalDays < currentTotalDays)
        {
            influence.Status = "Expired";
        }

        influence.TriggerCount = System.Math.Max(0, influence.TriggerCount);
        influence.MaxTriggers = System.Math.Clamp(influence.MaxTriggers <= 0 ? 1 : influence.MaxTriggers, 1, 4);
        influence.TimesReinforced = System.Math.Max(0, influence.TimesReinforced);
        return influence;
    }

    public static string NormalizeType(string type)
    {
        return BehaviorValueNormalizer.NormalizeDialogueBehaviorInfluenceType(type);
    }

    public static int StatusOrder(string status)
    {
        return status switch
        {
            "Active" => 0,
            "Spent" => 1,
            "Expired" => 2,
            _ => 3
        };
    }

    public static string BuildKey(string type, string summary, string targetLocation)
    {
        return BehaviorValueNormalizer.BuildDialogueBehaviorInfluenceKey(type, summary, targetLocation);
    }

    private static void SortAndTrim(LivingNpcState state)
    {
        state.DialogueBehaviorInfluences = state.DialogueBehaviorInfluences
            .OrderBy(influence => StatusOrder(influence.Status))
            .ThenBy(influence => influence.ExpiresTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTotalDays)
            .ThenByDescending(influence => influence.LastUpdatedTimeOfDay)
            .Take(MaxInfluencesPerNpc)
            .ToList();
    }

    private static string NormalizeStatus(string status)
    {
        return status switch
        {
            "Spent" => "Spent",
            "Expired" => "Expired",
            _ => "Active"
        };
    }

    private static int GetDefaultDurationDays(string type)
    {
        return NormalizeType(type) switch
        {
            "pause_to_talk" => 1,
            "visit_location" => 3,
            "comforted" => 2,
            "offended" => 3,
            "give_space" => 2,
            "stay_near" => 1,
            _ => 1
        };
    }

    private static int GetDefaultMaxTriggers(string type)
    {
        return NormalizeType(type) switch
        {
            "pause_to_talk" => 1,
            "visit_location" => 2,
            "comforted" => 2,
            "offended" => 2,
            "give_space" => 2,
            "stay_near" => 2,
            _ => 1
        };
    }

    private static int GetDefaultIntensity(string type)
    {
        return NormalizeType(type) switch
        {
            "visit_location" => 45,
            "comforted" => 55,
            "offended" => 70,
            "give_space" => 60,
            "stay_near" => 55,
            "pause_to_talk" => 45,
            _ => 40
        };
    }
}
