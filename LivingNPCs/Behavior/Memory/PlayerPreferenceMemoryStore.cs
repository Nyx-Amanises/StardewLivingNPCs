using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class PlayerPreferenceMemoryStore
{
    public const int MaxMemoriesPerNpc = 24;

    public static void Refresh(LivingNpcState state, int currentTotalDays)
    {
        state.PlayerPreferenceMemories ??= new List<PlayerPreferenceFact>();
        state.PlayerPreferenceMemories = state.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(NormalizeForStore)
            .Where(memory => memory.PreferenceKind != "none"
                && !string.IsNullOrWhiteSpace(BuildKey(memory.PreferenceKind, memory.Subject, memory.Summary)))
            .GroupBy(
                memory => BuildKey(memory.PreferenceKind, memory.Subject, memory.Summary),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(MergeGroup)
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxMemoriesPerNpc)
            .ToList();
    }

    public static bool Store(
        LivingNpcState state,
        ValleyTalkMemoryCandidate candidate,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        state.PlayerPreferenceMemories ??= new List<PlayerPreferenceFact>();

        string normalizedKey = BuildKey(candidate.PlayerPreferenceKind, candidate.Subject, candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        var existing = state.PlayerPreferenceMemories.FirstOrDefault(memory =>
            BuildKey(memory.PreferenceKind, memory.Subject, memory.Summary) == normalizedKey);
        if (existing != null)
        {
            existing.PreferenceKind = NormalizeKind(candidate.PlayerPreferenceKind);
            existing.Subject = candidate.Subject.Trim();
            existing.Summary = candidate.Summary.Trim();
            existing.Importance = System.Math.Max(existing.Importance, candidate.Importance);
            existing.Tags = BehaviorValueNormalizer.NormalizeMemoryTags(
                existing.Tags.Concat(candidate.Tags),
                existing.Subject,
                existing.Summary);
            existing.LastUpdatedTotalDays = currentTotalDays;
            existing.LastUpdatedTimeOfDay = currentTimeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact
        {
            PreferenceKind = NormalizeKind(candidate.PlayerPreferenceKind),
            Subject = candidate.Subject.Trim(),
            Summary = candidate.Summary.Trim(),
            Tags = BehaviorValueNormalizer.NormalizeMemoryTags(candidate.Tags, candidate.Subject, candidate.Summary),
            Importance = candidate.Importance,
            CreatedTotalDays = currentTotalDays,
            CreatedTimeOfDay = currentTimeOfDay,
            LastUpdatedTotalDays = currentTotalDays,
            LastUpdatedTimeOfDay = currentTimeOfDay,
            TimesReinforced = 1
        });

        state.PlayerPreferenceMemories = state.PlayerPreferenceMemories
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxMemoriesPerNpc)
            .ToList();
        return true;
    }

    public static PlayerPreferenceFact NormalizeForStore(PlayerPreferenceFact memory)
    {
        memory.PreferenceKind = NormalizeKind(memory.PreferenceKind);
        memory.Subject = memory.Subject?.Trim() ?? string.Empty;
        memory.Summary = memory.Summary.Trim();
        memory.Tags = BehaviorValueNormalizer.NormalizeMemoryTags(memory.Tags, memory.Subject, memory.Summary);
        memory.Importance = System.Math.Clamp(memory.Importance, 0, 100);
        memory.TimesReinforced = System.Math.Max(0, memory.TimesReinforced);
        memory.RecallCount = System.Math.Max(0, memory.RecallCount);
        if (memory.LastUpdatedTotalDays < 0)
        {
            memory.LastUpdatedTotalDays = memory.CreatedTotalDays;
            memory.LastUpdatedTimeOfDay = memory.CreatedTimeOfDay;
        }

        return memory;
    }

    public static int GetRetentionScore(PlayerPreferenceFact memory)
    {
        return GetRetentionScore(memory, StardewValley.Game1.Date.TotalDays);
    }

    public static int GetRetentionScore(PlayerPreferenceFact memory, int currentTotalDays)
    {
        int score = memory.Importance;
        score += System.Math.Min(24, memory.TimesReinforced * 4);
        score += System.Math.Min(12, memory.RecallCount);
        score += memory.PreferenceKind switch
        {
            "goal" => 14,
            "value" => 12,
            "habit" => 10,
            "liked_item_category" => 8,
            "disliked_item" => 8,
            _ => 0
        };
        score += GetMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays) switch
        {
            0 => 12,
            1 => 9,
            <= 7 => 6,
            <= 28 => 3,
            >= 112 => -12,
            >= 56 => -6,
            _ => 0
        };
        return score;
    }

    public static string BuildKey(string kind, string subject, string summary)
    {
        return BehaviorValueNormalizer.BuildPlayerPreferenceKey(kind, subject, summary);
    }

    public static string NormalizeKind(string kind)
    {
        return BehaviorValueNormalizer.NormalizePlayerPreferenceKind(kind);
    }

    private static PlayerPreferenceFact MergeGroup(IEnumerable<PlayerPreferenceFact> group)
    {
        var memories = group
            .OrderByDescending(GetRetentionScore)
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .ToList();
        var primary = memories[0];
        foreach (var memory in memories.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(primary.Subject) && !string.IsNullOrWhiteSpace(memory.Subject))
            {
                primary.Subject = memory.Subject;
            }

            if (memory.Importance > primary.Importance || memory.Summary.Length > primary.Summary.Length)
            {
                primary.Summary = memory.Summary;
            }

            primary.Importance = System.Math.Max(primary.Importance, memory.Importance);
            primary.Tags = BehaviorValueNormalizer.NormalizeMemoryTags(
                primary.Tags.Concat(memory.Tags),
                primary.Subject,
                primary.Summary,
                memory.Subject,
                memory.Summary);
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

        return NormalizeForStore(primary);
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
