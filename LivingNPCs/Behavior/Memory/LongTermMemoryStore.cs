using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal static class LongTermMemoryStore
{
    public const int MaxMemoriesPerNpc = 24;

    // 被 24 条上限挤掉的记忆先进 backlog 等待 LLM 压缩成"关系印象"，而不是直接丢弃。
    // backlog 自身也有上限兜底：模型持续失败时按重要度丢最低的，等价于旧行为、只是更晚发生。
    public const int MaxImpressionBacklog = 48;
    public const int MaxImpressionBatch = 16;

    public static void Refresh(LivingNpcState state, int currentTotalDays)
    {
        state.LongTermMemories ??= new List<LongTermMemoryFact>();
        ApplyCapacity(
            state,
            state.LongTermMemories
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
                .ToList());
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
        if (string.IsNullOrWhiteSpace(normalizedKey) || string.IsNullOrWhiteSpace(candidate.Summary))
        {
            return false;
        }

        var existingVersions = state.LongTermMemories.Where(memory =>
                BuildKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey)
            .ToArray();
        // An evicted revision remains evidence in the impression queue. A late older update
        // must not return to live recall and hide the newer queued fact from impression refresh.
        var latestKnown = (state.ImpressionInFlight ?? new List<LongTermMemoryFact>())
            .Concat(state.ImpressionBacklog ?? new List<LongTermMemoryFact>())
            .Concat(existingVersions)
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary)
                && BuildKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey)
            .OrderBy(RevisionDay)
            .ThenBy(RevisionTime)
            .LastOrDefault();
        if (latestKnown != null && IsNewerAt(RevisionDay(latestKnown), RevisionTime(latestKnown),
                currentTotalDays, currentTimeOfDay))
        {
            return false;
        }

        var fact = existingVersions.Length > 0
            ? MergeGroup(existingVersions.Select(NormalizeForStore))
            : latestKnown != null
                ? NormalizeForStore(LivingNpcState.CloneLongTermMemoryFact(latestKnown))
                : new LongTermMemoryFact
                {
                    CreatedTotalDays = currentTotalDays,
                    CreatedTimeOfDay = currentTimeOfDay
                };
        if (latestKnown != null && !ReferenceEquals(fact, latestKnown))
        {
            // Queued/in-flight versions are snapshots, not extra reinforcements to sum again.
            fact.Importance = System.Math.Max(fact.Importance, latestKnown.Importance);
            fact.TimesReinforced = System.Math.Max(fact.TimesReinforced, latestKnown.TimesReinforced);
            fact.RecallCount = System.Math.Max(fact.RecallCount, latestKnown.RecallCount);
            KeepEarlierCreation(fact, latestKnown);
            KeepLaterRecall(fact, latestKnown);
        }

        fact.Kind = NormalizeKind(candidate.Kind);
        fact.Subject = candidate.Subject.Trim();
        // Importance is salience, not revision authority. Several corrections may share one
        // game minute, so equal timestamps keep the latest accepted call's complete revision.
        fact.Summary = candidate.Summary.Trim();
        fact.Tags = BehaviorValueNormalizer.NormalizeMemoryTags(candidate.Tags, fact.Subject, fact.Summary);
        fact.Importance = System.Math.Max(fact.Importance, candidate.Importance);
        fact.LastUpdatedTotalDays = currentTotalDays;
        fact.LastUpdatedTimeOfDay = currentTimeOfDay;
        fact.TimesReinforced += 1;

        state.LongTermMemories.RemoveAll(memory => existingVersions.Contains(memory) && !ReferenceEquals(memory, fact));
        // Retire only superseded snapshots of this exact identity. Never mutate an in-flight
        // batch: its result must still acknowledge the evidence that was actually submitted.
        state.ImpressionBacklog?.RemoveAll(memory => memory != null
            && BuildKey(memory.Kind, memory.Subject, memory.Summary) == normalizedKey);
        if (existingVersions.Length > 0)
        {
            storedMemory = fact;
            return true;
        }

        state.LongTermMemories.Add(fact);

        ApplyCapacity(
            state,
            state.LongTermMemories
                .OrderByDescending(memory => GetRetentionScore(memory, currentTotalDays))
                .ThenByDescending(memory => memory.LastUpdatedTotalDays)
                .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
                .ToList());

        // The capacity cap may have moved the new memory straight into the impression backlog; it
        // is still retained (queued for LLM compression), so hand the caller the stored instance
        // instead of a null lookup miss — nickname-state updates must not silently skip it.
        storedMemory = fact;
        return true;
    }

    /// <summary>
    /// Applies the per-NPC capacity to an already-ordered memory list; entries that fall off the
    /// end are moved into the impression backlog (for later LLM compression) instead of dropped.
    /// </summary>
    public static void ApplyCapacity(LivingNpcState state, List<LongTermMemoryFact> orderedMemories)
    {
        if (orderedMemories.Count > MaxMemoriesPerNpc)
        {
            AddToImpressionBacklog(state, orderedMemories.Skip(MaxMemoriesPerNpc));
            orderedMemories = orderedMemories.Take(MaxMemoriesPerNpc).ToList();
        }

        state.LongTermMemories = orderedMemories;
    }

    public static void AddToImpressionBacklog(LivingNpcState state, IEnumerable<LongTermMemoryFact> evicted)
    {
        state.ImpressionBacklog ??= new List<LongTermMemoryFact>();
        state.ImpressionInFlight ??= new List<LongTermMemoryFact>();
        foreach (var memory in evicted)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.Summary))
            {
                continue;
            }

            string key = BuildKey(memory.Kind, memory.Subject, memory.Summary);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // A live fact may have changed since an older version was captured for generation.
            // Subject identity alone would discard that newer version when it is evicted.
            string contentKey = MemoryImpressionSources.GetKey(memory);
            var queued = state.ImpressionBacklog.FirstOrDefault(existing => string.Equals(
                MemoryImpressionSources.GetKey(existing), contentKey, System.StringComparison.Ordinal));
            if (queued != null)
            {
                // A -> B -> A is still a new state even though the final wording equals the
                // first A. Keep its latest timestamp and queue order so B cannot win later.
                if (IsNewerAt(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay,
                        queued.LastUpdatedTotalDays, queued.LastUpdatedTimeOfDay)
                    || (memory.LastUpdatedTotalDays == queued.LastUpdatedTotalDays
                        && memory.LastUpdatedTimeOfDay == queued.LastUpdatedTimeOfDay))
                {
                    state.ImpressionBacklog.Remove(queued);
                    state.ImpressionBacklog.Add(memory);
                }

                continue;
            }

            var inFlight = state.ImpressionInFlight.FirstOrDefault(existing => string.Equals(
                    MemoryImpressionSources.GetKey(existing),
                    contentKey,
                    System.StringComparison.Ordinal));
            if (inFlight == null
                || IsNewerAt(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay,
                    inFlight.LastUpdatedTotalDays, inFlight.LastUpdatedTimeOfDay)
                || state.ImpressionBacklog.Any(queuedMemory => string.Equals(
                    MemoryImpressionSources.GetIdentity(queuedMemory),
                    MemoryImpressionSources.GetIdentity(memory),
                    System.StringComparison.Ordinal)))
            {
                state.ImpressionBacklog.Add(memory);
            }
        }

        TrimImpressionQueue(state.ImpressionBacklog, MaxImpressionBacklog);
    }

    public static List<LongTermMemoryFact> NormalizeImpressionQueue(List<LongTermMemoryFact> queue, int maxEntries)
    {
        var normalized = (queue ?? new List<LongTermMemoryFact>())
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(NormalizeForStore)
            .ToList();
        TrimImpressionQueue(normalized, maxEntries);
        return normalized;
    }

    private static void TrimImpressionQueue(List<LongTermMemoryFact> queue, int maxEntries)
    {
        while (queue.Count > maxEntries)
        {
            LongTermMemoryFact weakest = queue
                .OrderBy(memory => memory.Importance)
                .ThenBy(memory => memory.CreatedTotalDays)
                .First();
            queue.Remove(weakest);
        }
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
            .Distinct()
            .OrderBy(memory => memory.LastUpdatedTotalDays)
            .ThenBy(memory => memory.LastUpdatedTimeOfDay)
            .ToList();
        // Stored list order breaks game-clock ties. Preserve one whole semantic revision;
        // merging an older summary or tags would attach stale content to the newest date.
        var primary = memories[^1];
        foreach (var memory in memories.Take(memories.Count - 1))
        {
            primary.Importance = System.Math.Max(primary.Importance, memory.Importance);
            primary.TimesReinforced += memory.TimesReinforced;
            primary.RecallCount += memory.RecallCount;
            KeepEarlierCreation(primary, memory);
            KeepLaterRecall(primary, memory);
        }

        return NormalizeForStore(primary);
    }

    private static int RevisionDay(LongTermMemoryFact memory) =>
        memory.LastUpdatedTotalDays >= 0 ? memory.LastUpdatedTotalDays : memory.CreatedTotalDays;

    private static int RevisionTime(LongTermMemoryFact memory) =>
        memory.LastUpdatedTotalDays >= 0 ? memory.LastUpdatedTimeOfDay : memory.CreatedTimeOfDay;

    private static void KeepEarlierCreation(LongTermMemoryFact primary, LongTermMemoryFact memory)
    {
        if (memory.CreatedTotalDays >= 0
            && (primary.CreatedTotalDays < 0 || IsNewerAt(primary.CreatedTotalDays, primary.CreatedTimeOfDay,
                memory.CreatedTotalDays, memory.CreatedTimeOfDay)))
        {
            primary.CreatedTotalDays = memory.CreatedTotalDays;
            primary.CreatedTimeOfDay = memory.CreatedTimeOfDay;
        }
    }

    private static void KeepLaterRecall(LongTermMemoryFact primary, LongTermMemoryFact memory)
    {
        if (IsNewerAt(memory.LastRecalledTotalDays, memory.LastRecalledTimeOfDay,
                primary.LastRecalledTotalDays, primary.LastRecalledTimeOfDay))
        {
            primary.LastRecalledTotalDays = memory.LastRecalledTotalDays;
            primary.LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay;
        }
    }

    private static int GetMemoryAge(int totalDays, int currentTotalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - totalDays);
    }

    private static bool IsNewerAt(int candidateTotalDays, int candidateTimeOfDay, int currentTotalDays, int currentTimeOfDay)
    {
        return candidateTotalDays > currentTotalDays
            || (candidateTotalDays == currentTotalDays && candidateTimeOfDay > currentTimeOfDay);
    }
}
