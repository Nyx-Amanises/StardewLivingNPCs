using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class CommunityImpressionStore
{
    public const int MaxImpressionsPerNpc = 16;

    public static IReadOnlyList<CommunityImpressionFact> GetRetellable(
        LivingNpcState state,
        int maxCount,
        int currentTotalDays)
    {
        return state.CommunityImpressions
            .Where(memory => memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= currentTotalDays)
            .Where(memory => GetFreshnessStage(memory, currentTotalDays) is "fresh" or "settled")
            .Where(memory => memory.Confidence >= 35)
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenBy(memory => memory.LastSharedTotalDays < 0 ? -1 : memory.LastSharedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .Take(System.Math.Max(0, maxCount))
            .ToList();
    }

    public static void MarkShared(CommunityImpressionFact memory, int currentTotalDays, int currentTimeOfDay)
    {
        memory.LastSharedTotalDays = currentTotalDays;
        memory.LastSharedTimeOfDay = currentTimeOfDay;
        memory.ShareCount += 1;
    }

    public static bool Store(
        LivingNpcState state,
        string subjectNpcName,
        string subjectDisplayName,
        string kind,
        string summary,
        string source,
        string visibility,
        int transmissionDepth,
        int distortionLevel,
        string? heardFromNpcName,
        string? circleKey,
        int importance,
        int currentTotalDays,
        int currentTimeOfDay,
        out bool created,
        out string normalizedKind)
    {
        created = false;
        string normalizedKindValue = NormalizeKind(kind);
        normalizedKind = normalizedKindValue;
        if (string.IsNullOrWhiteSpace(subjectNpcName) || string.IsNullOrWhiteSpace(summary))
        {
            return false;
        }

        state.CommunityImpressions ??= new List<CommunityImpressionFact>();

        string normalizedSummary = BehaviorValueNormalizer.NormalizeMemorySummary(summary);
        string normalizedSource = NormalizeSource(source);
        string normalizedVisibility = NormalizeVisibility(visibility);
        int confidence = CommunityPropagationRules.GetInitialConfidence(normalizedSource);
        int normalizedDepth = System.Math.Clamp(transmissionDepth, 0, 8);
        int normalizedDistortion = System.Math.Clamp(distortionLevel, 0, 100);
        string normalizedKey = BuildKey(subjectNpcName, normalizedKindValue, summary);

        var existing = state.CommunityImpressions.FirstOrDefault(memory =>
            BuildKey(memory.SubjectNpcName, memory.Kind, memory.Summary) == normalizedKey
            || (string.Equals(memory.SubjectNpcName, subjectNpcName, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(memory.Kind, normalizedKindValue, System.StringComparison.OrdinalIgnoreCase)
                && BehaviorValueNormalizer.NormalizeMemorySummary(memory.Summary) == normalizedSummary));

        if (existing != null)
        {
            existing.SubjectDisplayName = subjectDisplayName;
            existing.Source = normalizedSource switch
            {
                "Witnessed" => "Witnessed",
                "CloseCircle" when NormalizeSource(existing.Source) == "PublicRumor" => "CloseCircle",
                _ => NormalizeSource(existing.Source)
            };
            existing.Visibility = GetMoreRestrictiveVisibility(existing.Visibility, normalizedVisibility);
            existing.Confidence = System.Math.Max(existing.Confidence, confidence);
            existing.TransmissionDepth = System.Math.Min(existing.TransmissionDepth, normalizedDepth);
            existing.DistortionLevel = System.Math.Min(existing.DistortionLevel, normalizedDistortion);
            existing.HeardFromNpcName = string.IsNullOrWhiteSpace(existing.HeardFromNpcName)
                ? heardFromNpcName?.Trim() ?? string.Empty
                : existing.HeardFromNpcName;
            existing.CircleKey = string.IsNullOrWhiteSpace(existing.CircleKey)
                ? circleKey?.Trim() ?? string.Empty
                : existing.CircleKey;
            existing.Importance = System.Math.Min(100, System.Math.Max(existing.Importance, importance) + 3);
            existing.LastUpdatedTotalDays = currentTotalDays;
            existing.LastUpdatedTimeOfDay = currentTimeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        state.CommunityImpressions.Add(new CommunityImpressionFact
        {
            SubjectNpcName = subjectNpcName.Trim(),
            SubjectDisplayName = subjectDisplayName.Trim(),
            Kind = normalizedKindValue,
            Summary = summary.Trim(),
            Source = normalizedSource,
            Visibility = normalizedVisibility,
            Confidence = confidence,
            TransmissionDepth = normalizedDepth,
            DistortionLevel = normalizedDistortion,
            HeardFromNpcName = heardFromNpcName?.Trim() ?? string.Empty,
            CircleKey = circleKey?.Trim() ?? string.Empty,
            Importance = System.Math.Clamp(importance, 0, 100),
            CreatedTotalDays = currentTotalDays,
            CreatedTimeOfDay = currentTimeOfDay,
            LastUpdatedTotalDays = currentTotalDays,
            LastUpdatedTimeOfDay = currentTimeOfDay,
            ExpiresTotalDays = DetermineExpiry(
                normalizedSource,
                normalizedVisibility,
                normalizedDepth,
                currentTotalDays
            ),
            TimesReinforced = 1
        });
        state.CommunityImpressions = state.CommunityImpressions
            .OrderByDescending(memory => memory.Importance)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxImpressionsPerNpc)
            .ToList();

        created = true;
        return true;
    }

    public static void Refresh(LivingNpcState state, int currentTotalDays)
    {
        state.CommunityImpressions ??= new List<CommunityImpressionFact>();
        state.CommunityImpressions = state.CommunityImpressions
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.SubjectNpcName)
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && (memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= currentTotalDays))
            .Select(memory => NormalizeForStore(memory, currentTotalDays))
            .GroupBy(
                memory => BuildKey(memory.SubjectNpcName, memory.Kind, memory.Summary),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(memory => MergeGroup(memory, currentTotalDays))
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxImpressionsPerNpc)
            .ToList();
    }

    public static void Fade(LivingNpcState state, int currentTotalDays)
    {
        foreach (var memory in state.CommunityImpressions)
        {
            int age = GetMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays);
            if (age <= 0)
            {
                continue;
            }

            int sourceDecay = NormalizeSource(memory.Source) switch
            {
                "Witnessed" => 1,
                "CloseCircle" => 3,
                _ => 5
            };
            int distortionDecay = memory.TransmissionDepth + (memory.DistortionLevel / 25);
            int decay = System.Math.Max(1, sourceDecay + distortionDecay);
            memory.Confidence = System.Math.Max(0, memory.Confidence - decay);
            memory.Importance = System.Math.Max(0, memory.Importance - System.Math.Max(1, decay / 2));
        }

        state.CommunityImpressions = state.CommunityImpressions
            .Where(memory => memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= currentTotalDays)
            .Where(memory => memory.Confidence >= 18)
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxImpressionsPerNpc)
            .ToList();
    }

    public static CommunityImpressionFact NormalizeForStore(CommunityImpressionFact memory)
    {
        return NormalizeForStore(memory, StardewValley.Game1.Date.TotalDays);
    }

    public static CommunityImpressionFact NormalizeForStore(CommunityImpressionFact memory, int currentTotalDays)
    {
        memory.SubjectNpcName = memory.SubjectNpcName?.Trim() ?? string.Empty;
        memory.SubjectDisplayName = string.IsNullOrWhiteSpace(memory.SubjectDisplayName)
            ? memory.SubjectNpcName
            : memory.SubjectDisplayName.Trim();
        memory.Kind = NormalizeKind(memory.Kind);
        memory.Summary = memory.Summary.Trim();
        memory.Source = NormalizeSource(memory.Source);
        memory.Visibility = NormalizeVisibility(memory.Visibility);
        memory.Confidence = System.Math.Clamp(memory.Confidence, 0, 100);
        memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
        memory.TransmissionDepth = System.Math.Clamp(memory.TransmissionDepth, 0, 8);
        memory.DistortionLevel = System.Math.Clamp(memory.DistortionLevel, 0, 100);
        memory.HeardFromNpcName = memory.HeardFromNpcName?.Trim() ?? string.Empty;
        memory.CircleKey = memory.CircleKey?.Trim() ?? string.Empty;
        memory.ShareCount = System.Math.Max(0, memory.ShareCount);
        if (memory.ExpiresTotalDays < 0)
        {
            memory.ExpiresTotalDays = DetermineExpiry(
                memory.Source,
                memory.Visibility,
                memory.TransmissionDepth,
                memory.LastUpdatedTotalDays >= 0 ? memory.LastUpdatedTotalDays : currentTotalDays
            );
        }

        memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
        memory.RecallCount = System.Math.Max(0, memory.RecallCount);
        if (memory.LastUpdatedTotalDays < 0)
        {
            memory.LastUpdatedTotalDays = memory.CreatedTotalDays;
            memory.LastUpdatedTimeOfDay = memory.CreatedTimeOfDay;
        }

        return memory;
    }

    public static int GetRetentionScore(CommunityImpressionFact memory)
    {
        return GetRetentionScore(memory, StardewValley.Game1.Date.TotalDays);
    }

    public static int GetRetentionScore(CommunityImpressionFact memory, int currentTotalDays)
    {
        int age = GetMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays);
        int freshness = age switch
        {
            0 => 20,
            1 => 16,
            <= 3 => 12,
            <= 7 => 6,
            <= 14 => 2,
            _ => -12
        };

        return memory.Importance
            + (memory.Confidence / 5)
            + freshness
            + (NormalizeSource(memory.Source) switch
            {
                "Witnessed" => 8,
                "CloseCircle" => 3,
                _ => 0
            })
            - (memory.TransmissionDepth * 3)
            - (memory.DistortionLevel / 10)
            + System.Math.Min(memory.TimesReinforced * 3, 15)
            - System.Math.Min(memory.RecallCount * 2, 12);
    }

    public static int DetermineExpiry(string source, string visibility, int transmissionDepth, int baseTotalDays)
    {
        int baseLifetime = NormalizeSource(source) switch
        {
            "Witnessed" => 14,
            "CloseCircle" => 10,
            _ => 6
        };
        int visibilityAdjustment = NormalizeVisibility(visibility) switch
        {
            "Private" => -2,
            "Personal" => -1,
            _ => 0
        };
        int depthPenalty = System.Math.Min(4, System.Math.Max(0, transmissionDepth));
        return baseTotalDays + System.Math.Max(3, baseLifetime + visibilityAdjustment - depthPenalty);
    }

    public static string GetMoreRestrictiveVisibility(string first, string second)
    {
        int firstRank = GetVisibilityRank(first);
        int secondRank = GetVisibilityRank(second);
        return firstRank >= secondRank
            ? NormalizeVisibility(first)
            : NormalizeVisibility(second);
    }

    public static string GetFreshnessStage(CommunityImpressionFact memory, int currentTotalDays)
    {
        int age = memory.LastUpdatedTotalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - memory.LastUpdatedTotalDays);
        int remaining = memory.ExpiresTotalDays < 0
            ? int.MaxValue
            : memory.ExpiresTotalDays - currentTotalDays;
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

    public static string BuildKey(string subjectNpcName, string kind, string summary)
    {
        return BehaviorValueNormalizer.BuildCommunityImpressionKey(subjectNpcName, kind, summary);
    }

    public static string NormalizeKind(string kind)
    {
        return BehaviorValueNormalizer.NormalizeCommunityImpressionKind(kind);
    }

    public static string NormalizeSource(string source)
    {
        return BehaviorValueNormalizer.NormalizeCommunityImpressionSource(source);
    }

    public static string NormalizeVisibility(string visibility)
    {
        return BehaviorValueNormalizer.NormalizeCommunityImpressionVisibility(visibility);
    }

    private static CommunityImpressionFact MergeGroup(
        IEnumerable<CommunityImpressionFact> group,
        int currentTotalDays)
    {
        var memories = group
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .ToList();
        var primary = memories[0];
        foreach (var memory in memories.Skip(1))
        {
            if (memory.Source == "Witnessed")
            {
                primary.Source = "Witnessed";
            }
            else if (primary.Source != "Witnessed" && memory.Source == "CloseCircle")
            {
                primary.Source = "CloseCircle";
            }

            if (memory.Importance > primary.Importance || memory.Summary.Length > primary.Summary.Length)
            {
                primary.Summary = memory.Summary;
            }

            if (string.IsNullOrWhiteSpace(primary.SubjectDisplayName) && !string.IsNullOrWhiteSpace(memory.SubjectDisplayName))
            {
                primary.SubjectDisplayName = memory.SubjectDisplayName;
            }

            primary.Confidence = System.Math.Max(primary.Confidence, memory.Confidence);
            primary.Importance = System.Math.Max(primary.Importance, memory.Importance);
            primary.Visibility = GetMoreRestrictiveVisibility(primary.Visibility, memory.Visibility);
            primary.TransmissionDepth = System.Math.Min(primary.TransmissionDepth, memory.TransmissionDepth);
            primary.DistortionLevel = System.Math.Min(primary.DistortionLevel, memory.DistortionLevel);
            if (string.IsNullOrWhiteSpace(primary.HeardFromNpcName))
            {
                primary.HeardFromNpcName = memory.HeardFromNpcName;
            }

            if (string.IsNullOrWhiteSpace(primary.CircleKey))
            {
                primary.CircleKey = memory.CircleKey;
            }

            primary.ShareCount += memory.ShareCount;
            if (IsNewerAt(memory.LastSharedTotalDays, memory.LastSharedTimeOfDay, primary.LastSharedTotalDays, primary.LastSharedTimeOfDay))
            {
                primary.LastSharedTotalDays = memory.LastSharedTotalDays;
                primary.LastSharedTimeOfDay = memory.LastSharedTimeOfDay;
            }

            primary.ExpiresTotalDays = System.Math.Max(primary.ExpiresTotalDays, memory.ExpiresTotalDays);
            primary.TimesReinforced += memory.TimesReinforced;
            primary.RecallCount += memory.RecallCount;
            if (IsOlderCreatedAt(memory.CreatedTotalDays, primary.CreatedTotalDays))
            {
                primary.CreatedTotalDays = memory.CreatedTotalDays;
                primary.CreatedTimeOfDay = memory.CreatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay, primary.LastUpdatedTotalDays, primary.LastUpdatedTimeOfDay))
            {
                primary.LastUpdatedTotalDays = memory.LastUpdatedTotalDays;
                primary.LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay;
            }

            if (IsNewerAt(memory.LastRecalledTotalDays, memory.LastRecalledTimeOfDay, primary.LastRecalledTotalDays, primary.LastRecalledTimeOfDay))
            {
                primary.LastRecalledTotalDays = memory.LastRecalledTotalDays;
                primary.LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay;
            }
        }

        return NormalizeForStore(primary, currentTotalDays);
    }

    private static int GetVisibilityRank(string visibility)
    {
        return NormalizeVisibility(visibility) switch
        {
            "Private" => 2,
            "Personal" => 1,
            _ => 0
        };
    }

    private static int GetMemoryAge(int totalDays, int currentTotalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - totalDays);
    }

    private static bool IsOlderCreatedAt(int candidateTotalDays, int currentTotalDays)
    {
        return candidateTotalDays >= 0 && (currentTotalDays < 0 || candidateTotalDays < currentTotalDays);
    }

    private static bool IsNewerAt(int candidateTotalDays, int candidateTimeOfDay, int currentTotalDays, int currentTimeOfDay)
    {
        return candidateTotalDays > currentTotalDays
            || (candidateTotalDays == currentTotalDays && candidateTimeOfDay > currentTimeOfDay);
    }
}
