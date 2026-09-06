using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LivingNPCs.Behavior;

/// <summary>Stable evidence for a relationship impression, independent of recall counters and daily mood.</summary>
internal static class MemoryImpressionSources
{
    internal const int InitialMemoryCount = 3;
    internal const int ImportantMemoryThreshold = 60;
    internal const int MaxRememberedSourceKeys = 256;

    internal static List<LongTermMemoryFact> GetImportantMemories(LivingNpcState state)
    {
        var memories = DistinctEligible(state.LongTermMemories)
            .Where(IsImportant)
            .Select(LivingNpcState.CloneLongTermMemoryFact)
            .ToList();

        memories.AddRange(state.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary)
                && memory.Importance >= ImportantMemoryThreshold)
            .Select(memory => new LongTermMemoryFact
            {
                Kind = "preference",
                Subject = memory.Subject,
                Summary = memory.Summary,
                Importance = memory.Importance,
                CreatedTotalDays = memory.CreatedTotalDays,
                CreatedTimeOfDay = memory.CreatedTimeOfDay,
                LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
                LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay
            }));

        memories.AddRange(state.SharedExperiences
            .Where(experience => experience != null && !string.IsNullOrWhiteSpace(experience.Summary)
                && experience.Importance >= ImportantMemoryThreshold)
            .Select(experience => new LongTermMemoryFact
            {
                Kind = "relationship",
                Subject = $"shared:{experience.Key}",
                Summary = $"{experience.Summary} (recorded occurrences: {Math.Max(1, experience.TimesReinforced)})",
                Importance = experience.Importance,
                CreatedTotalDays = experience.LastUpdatedTotalDays >= 0 ? experience.LastUpdatedTotalDays : experience.CreatedTotalDays,
                CreatedTimeOfDay = experience.LastUpdatedTotalDays >= 0 ? experience.LastUpdatedTimeOfDay : experience.CreatedTimeOfDay,
                LastUpdatedTotalDays = experience.LastUpdatedTotalDays,
                LastUpdatedTimeOfDay = experience.LastUpdatedTimeOfDay
            }));

        memories.AddRange(state.Conflicts
            .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary)
                && Math.Max(conflict.Severity, conflict.PeakSeverity) >= 30)
            .Select(conflict => new LongTermMemoryFact
            {
                Kind = "relationship",
                Subject = $"conflict:{conflict.CauseKind}:{conflict.CreatedTotalDays}:{conflict.CreatedTimeOfDay}",
                Summary = $"Conflict: {conflict.Summary}; current status: {conflict.Status}.",
                Importance = Math.Max(ImportantMemoryThreshold, conflict.PeakSeverity),
                CreatedTotalDays = conflict.ResolvedTotalDays >= 0 ? conflict.ResolvedTotalDays : conflict.CreatedTotalDays,
                CreatedTimeOfDay = conflict.ResolvedTotalDays >= 0 ? conflict.ResolvedTimeOfDay : conflict.CreatedTimeOfDay,
                LastUpdatedTotalDays = conflict.LastUpdatedTotalDays,
                LastUpdatedTimeOfDay = conflict.LastUpdatedTimeOfDay
            }));

        return LatestVersions(memories)
            .OrderByDescending(memory => memory.LastUpdatedTotalDays)
            .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
            .ThenByDescending(memory => memory.Importance)
            .ToList();
    }

    internal static IEnumerable<LongTermMemoryFact> DistinctEligible(IEnumerable<LongTermMemoryFact> memories)
    {
        return memories
            .Where(IsEligible)
            .DistinctBy(GetKey, StringComparer.Ordinal);
    }

    private static bool IsEligible(LongTermMemoryFact memory) =>
        memory != null
        && !string.IsNullOrWhiteSpace(memory.Summary)
        && !RsvAiPolicy.ContainsBlockedReference(memory.Summary)
        && !RsvAiPolicy.ContainsBlockedReference(memory.Subject);

    internal static bool IsImportant(LongTermMemoryFact memory) =>
        memory.Importance >= ImportantMemoryThreshold
        || (memory.Importance >= 40 && memory.Kind is "relationship" or "promise" or "boundary");

    internal static IEnumerable<LongTermMemoryFact> LatestVersions(IEnumerable<LongTermMemoryFact> memories) =>
        memories.Where(IsEligible)
            .GroupBy(GetIdentity, StringComparer.Ordinal)
            .Select(group => group.OrderBy(memory => memory.LastUpdatedTotalDays)
                .ThenBy(memory => memory.LastUpdatedTimeOfDay)
                .Last());

    // A reminder, a recall, or the passage of a day does not make the same fact new evidence.
    // Content changes (including a conflict's status or a shared outing's occurrence count) do.
    internal static string GetKey(LongTermMemoryFact memory)
    {
        return $"{GetIdentity(memory)}:{Hash(memory.Summary?.Trim() ?? string.Empty)}";
    }

    internal static string GetIdentity(LongTermMemoryFact memory)
    {
        string subject = string.IsNullOrWhiteSpace(memory.Subject)
            ? memory.Summary?.Trim() ?? string.Empty
            : memory.Subject.Trim().ToLowerInvariant();
        return Hash($"{memory.Kind?.Trim().ToLowerInvariant()}\n{subject}");
    }

    // Keep the last summarized version of each source, not every value it has ever held:
    // a preference can change A -> B -> A and the final A still needs a fresh impression.
    internal static List<string> NormalizeCoveredKeys(IEnumerable<string> keys) =>
        keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Reverse()
            .DistinctBy(key => key.Split(':')[0], StringComparer.Ordinal)
            .Take(MaxRememberedSourceKeys)
            .Reverse()
            .ToList();

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string GetRelationshipContext(LivingNpcState state)
    {
        string trust = state.RelationshipTrust switch
        {
            >= 75 => "deep",
            >= 50 => "solid",
            >= 30 => "warming",
            _ => "early"
        };
        string nickname = string.IsNullOrWhiteSpace(state.FarmerNickname)
            || RsvAiPolicy.ContainsBlockedReference(state.FarmerNickname)
            ? "none"
            : $"{state.FarmerNickname.Trim()} ({state.FarmerNicknameStatus})";
        return $"Comfort: {state.InteractionComfortTier}; friendship hearts: {state.LastFriendshipHearts}; trust: {trust}; requested nickname: {nickname}.";
    }
}
