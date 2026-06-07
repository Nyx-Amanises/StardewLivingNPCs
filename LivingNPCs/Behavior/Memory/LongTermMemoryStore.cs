using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class LongTermMemoryStore
{
    public const int MaxMemoriesPerNpc = 24;

    public static void Refresh(LivingNpcState state, int currentTotalDays)
    {
        state.LongTermMemories ??= new List<LongTermMemoryFact>();
        state.LongTermMemories = state.LongTermMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(NormalizeForStore)
            .Where(memory => !string.IsNullOrWhiteSpace(BuildKey(memory.Kind, memory.Subject, memory.Summary)))
            .GroupBy(
                memory => BuildKey(memory.Kind, memory.Subject, memory.Summary),
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
        int currentTimeOfDay,
        out LongTermMemoryFact? storedMemory)
    {
        storedMemory = null;
        state.LongTermMemories ??= new List<LongTermMemoryFact>();

        string normalizedKey = BuildKey(candidate.Kind, candidate.Subject, candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return false;
        }

        var existing = state.LongTermMemories.FirstOrDefault(memory =>
            BuildKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey);
        if (existing != null)
        {
            existing.Kind = NormalizeKind(candidate.Kind);
            existing.Subject = candidate.Subject.Trim();
            if (candidate.Importance >= existing.Importance || existing.Summary.Length < candidate.Summary.Trim().Length)
            {
                existing.Summary = candidate.Summary.Trim();
            }

            existing.Importance = System.Math.Max(existing.Importance, candidate.Importance);
            existing.Tags = BehaviorValueNormalizer.NormalizeMemoryTags(
                existing.Tags.Concat(candidate.Tags),
                existing.Subject,
                existing.Summary);
            existing.LastUpdatedTotalDays = currentTotalDays;
            existing.LastUpdatedTimeOfDay = currentTimeOfDay;
            existing.TimesReinforced += 1;
            storedMemory = existing;
            return true;
        }

        state.LongTermMemories.Add(new LongTermMemoryFact
        {
            Kind = NormalizeKind(candidate.Kind),
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

        state.LongTermMemories = state.LongTermMemories
            .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
            .ThenByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .Take(MaxMemoriesPerNpc)
            .ToList();

        storedMemory = state.LongTermMemories.LastOrDefault(memory =>
            BuildKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey);
        return true;
    }

    public static LongTermMemoryFact NormalizeForStore(LongTermMemoryFact memory)
    {
        memory.Kind = NormalizeKind(memory.Kind);
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

    public static int GetRetentionScore(LongTermMemoryFact memory)
    {
        return GetRetentionScore(memory, StardewValley.Game1.Date.TotalDays);
    }

    public static int GetRetentionScore(LongTermMemoryFact memory, int currentTotalDays)
    {
        int score = memory.Importance;
        score += System.Math.Min(24, memory.TimesReinforced * 4);
        score += System.Math.Min(12, memory.RecallCount);
        score += GetKindBonus(memory.Kind);
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

    public static int GetKindBonus(string kind)
    {
        return NormalizeKind(kind) switch
        {
            "boundary" => 18,
            "promise" => 16,
            "relationship" => 12,
            "preference" => 8,
            _ => 0
        };
    }

    public static string BuildKey(string kind, string subject, string summary)
    {
        return BehaviorValueNormalizer.BuildLongTermMemoryKey(kind, subject, summary);
    }

    public static string NormalizeKind(string kind)
    {
        return BehaviorValueNormalizer.NormalizeLongTermMemoryKind(kind);
    }

    private static LongTermMemoryFact MergeGroup(IEnumerable<LongTermMemoryFact> group)
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
