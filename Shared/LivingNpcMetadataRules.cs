#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNpcs.Shared;

/// <summary>
/// Single source of truth for the LivingNPCs hidden-metadata value domains: the enum whitelists,
/// numeric clamp ranges, and per-list caps that both sides of the mod bridge must agree on.
/// ValleyTalk normalizes what the model returned before serializing it across the SMAPI bridge;
/// LivingNPCs re-normalizes what arrives (defense in depth against other callers or version skew).
/// Both parsers previously carried private copies of these rules and had already drifted apart
/// (walk_together / companion_walk were accepted by one side and silently dropped by the other),
/// so the rules now live here and are compiled into BOTH assemblies via a csproj Compile link.
///
/// Keep this file dependency-free: no game types, no JSON library, nothing but the BCL, so it can
/// compile identically inside either mod. The canonical semantics are the receiver's (LivingNPCs):
/// it executes the actions and its tests pin these values.
///
/// Licensing: this file was authored for this project by the LivingNPCs author, who licenses it
/// under LGPL 2.1 as part of the ValleyTalk fork and under the LivingNPCs project license as part
/// of LivingNPCs.
/// </summary>
internal static class LivingNpcMetadataRules
{
    // --- per-list caps (Take(...) limits applied by both parsers) ---
    public const int MaxMemories = 4;
    public const int MaxWorldActions = 1;
    public const int MaxBehaviorInfluences = 2;
    public const int MaxHelpRequests = 1;
    public const int MaxHelpRequestSteps = 3;
    public const int MaxHelpRequestUpdates = 2;
    public const int MaxConflicts = 2;
    public const int MaxPreferenceTags = 6;

    // --- numeric ranges ---
    public const int MaxRapportDelta = 30;
    public const int MaxImportance = 100;
    public const int MaxAmbientFollowUpDelayMinutes = 120;
    public const int MaxEmotionIntensityDelta = 100;
    public const int MaxRepairDelta = 100;
    public const int MaxMoneyAmount = 250;
    public const int MaxActionDelayMinutes = 20;
    public const int MaxOutingDurationMinutes = 600;
    public const int MaxNonOutingDurationMinutes = 20;
    public const int MaxInfluenceDurationDays = 7;
    public const int MaxInfluenceIntensity = 100;
    public const int MaxInfluenceTriggers = 4;
    public const int MinHelpRequestDueDays = 1;
    public const int MaxHelpRequestDueDays = 7;
    public const int MaxConflictSeverity = 100;

    private static readonly HashSet<string> AllowedPlayerPreferenceTagSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "food",
        "drink",
        "flower",
        "mineral",
        "forage",
        "nature",
        "sweet",
        "comfort",
        "practical",
        "scholarly",
        "adventurous",
        "magical",
        "artistic",
        "refined",
        "work",
        "active",
        "fishing",
        "mining",
        "farming",
        "morning",
        "night"
    };

    public static int ClampRapportDelta(int value) => Math.Clamp(value, 0, MaxRapportDelta);

    public static int ClampImportance(int value) => Math.Clamp(value, 0, MaxImportance);

    public static int ClampAmbientFollowUpDelayMinutes(int value) => Math.Clamp(value, 0, MaxAmbientFollowUpDelayMinutes);

    public static int ClampEmotionIntensityDelta(int value) => Math.Clamp(value, -MaxEmotionIntensityDelta, MaxEmotionIntensityDelta);

    public static int ClampRepairDelta(int value) => Math.Clamp(value, 0, MaxRepairDelta);

    public static int ClampMoneyAmount(int value) => Math.Clamp(value, 0, MaxMoneyAmount);

    public static int ClampActionDelayMinutes(int value) => Math.Clamp(value, 0, MaxActionDelayMinutes);

    public static int ClampInfluenceDurationDays(int value) => Math.Clamp(value, 0, MaxInfluenceDurationDays);

    public static int ClampInfluenceIntensity(int value) => Math.Clamp(value, 0, MaxInfluenceIntensity);

    public static int ClampInfluenceMaxTriggers(int value) => Math.Clamp(value, 0, MaxInfluenceTriggers);

    public static int ClampHelpRequestDueDays(int value) => Math.Clamp(value, MinHelpRequestDueDays, MaxHelpRequestDueDays);

    public static int ClampConflictSeverity(int value) => Math.Clamp(value, 0, MaxConflictSeverity);

    /// <summary>
    /// Companion outings may run for hours; every other action treats durationMinutes as a short
    /// delay. LivingNPCs additionally applies its own outing stay rules on top of this range.
    /// </summary>
    public static int NormalizeActionDurationMinutes(string? actionType, int durationMinutes)
    {
        return string.Equals(actionType, "companion_outing", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(durationMinutes, 0, MaxOutingDurationMinutes)
            : Math.Clamp(durationMinutes, 0, MaxNonOutingDurationMinutes);
    }

    public static string NormalizeLongTermMemoryKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "preference" => "preference",
            "promise" => "promise",
            "boundary" => "boundary",
            "relationship" => "relationship",
            _ => "fact"
        };
    }

    public static string NormalizePlayerPreferenceKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "liked_item_category" => "liked_item_category",
            "disliked_item" => "disliked_item",
            "habit" => "habit",
            "value" => "value",
            "goal" => "goal",
            _ => "none"
        };
    }

    /// <summary>
    /// escort_to_location folds into companion_outing (same executor); walk_together is not a
    /// supported action and normalizes to none — both pinned by LivingNPCs.Tests.
    /// </summary>
    public static string NormalizeWorldActionType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "give_small_gift" => "give_small_gift",
            "give_meaningful_gift" => "give_meaningful_gift",
            "give_money" => "give_money",
            "companion_outing" => "companion_outing",
            "escort_to_location" => "companion_outing",
            "festival_interaction" => "festival_interaction",
            _ => "none"
        };
    }

    public static string NormalizeTravelConsent(string? consent)
    {
        return consent?.Trim().ToLowerInvariant() switch
        {
            "accepted_now" => "accepted_now",
            "accepted_later" => "accepted_later",
            "deferred" => "accepted_later",
            "declined" => "declined",
            "rejected" => "declined",
            "tentative" => "tentative",
            "maybe" => "tentative",
            "none" => "none",
            _ => string.Empty
        };
    }

    public static string NormalizeBehaviorInfluenceType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "visit_location" => "visit_location",
            "go_to_location" => "visit_location",
            "comforted" => "comforted",
            "reassured" => "comforted",
            "offended" => "offended",
            "hurt" => "offended",
            "give_space" => "give_space",
            "needs_space" => "give_space",
            "stay_near" => "stay_near",
            "approach" => "stay_near",
            "pause_to_talk" => "pause_to_talk",
            "stop_to_talk" => "pause_to_talk",
            _ => "none"
        };
    }

    public static string NormalizeEmotion(string? emotion)
    {
        return emotion?.Trim().ToLowerInvariant() switch
        {
            "happy" => "Happy",
            "calm" => "Calm",
            "jealous" => "Jealous",
            "worried" => "Worried",
            "grateful" => "Grateful",
            "disappointed" => "Disappointed",
            "uneasy" => "Uneasy",
            "upset" => "Upset",
            "angry" => "Angry",
            "sad" => "Sad",
            _ => "none"
        };
    }

    public static string NormalizeConflictCauseKind(string? causeKind)
    {
        return causeKind?.Trim().ToLowerInvariant() switch
        {
            "dialogue" => "dialogue",
            "gift" => "gift",
            "boundary" => "boundary",
            "promise" => "promise",
            _ => "dialogue"
        };
    }

    public static string NormalizeHelpRequestType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "item_request" => "item_request",
            "question_request" => "question_request",
            _ => "none"
        };
    }

    public static string NormalizeHelpRequestUpdateStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "accepted" => "accepted",
            "fulfilled" => "fulfilled",
            "advanced" => "advanced",
            "declined" => "declined",
            _ => "none"
        };
    }

    public static string NormalizeHelpRequestFollowUpPotential(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "deeper_relationship" => "deeper_relationship",
            _ => "none"
        };
    }

    public static List<string> NormalizePlayerPreferenceTags(IEnumerable<string?>? tags)
    {
        return tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!.Trim().ToLowerInvariant())
            .Where(tag => AllowedPlayerPreferenceTagSet.Contains(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPreferenceTags)
            .ToList()
            ?? new List<string>();
    }
}
