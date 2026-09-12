using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue.Engine;
using Newtonsoft.Json;

namespace LivingNPCs.Behavior;

/// <summary>Quote the selected records with their identity and revision time, not an inferred current status.</summary>
internal static class MemoryEvidenceFormatter
{
    public static string LongTermMemories(IReadOnlyList<LongTermMemorySelection> selections, int currentTotalDays) =>
        selections.Count == 0
            ? PromptFragments.Recall.EmptyLongTermRecall
            : string.Join("; ", selections.OrderBy(selection => selection.Memory.LastUpdatedTotalDays)
                .ThenBy(selection => selection.Memory.LastUpdatedTimeOfDay)
                .Select(selection =>
                {
                    LongTermMemoryFact memory = selection.Memory;
                    return $"[kind={memory.Kind}; subject={JsonConvert.SerializeObject(memory.Subject)}; {Updated(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay, currentTotalDays)}] {PromptDataBoundary.EscapeInline(memory.Summary)}";
                }));

    public static string PlayerPreferences(IReadOnlyList<PlayerPreferenceSelection> selections, int currentTotalDays) =>
        selections.Count == 0
            ? PromptFragments.Recall.EmptyPreferenceRecall
            : string.Join("; ", selections.OrderBy(selection => selection.Memory.LastUpdatedTotalDays)
                .ThenBy(selection => selection.Memory.LastUpdatedTimeOfDay)
                .Select(selection =>
                {
                    PlayerPreferenceFact memory = selection.Memory;
                    return $"[kind=preference; preferenceKind={memory.PreferenceKind}; subject={JsonConvert.SerializeObject(memory.Subject)}; {Updated(memory.LastUpdatedTotalDays, memory.LastUpdatedTimeOfDay, currentTotalDays)}] {PromptDataBoundary.EscapeInline(memory.Summary)}";
                }));

    private static string Updated(int day, int time, int currentTotalDays)
    {
        if (day < 0 || day > currentTotalDays)
        {
            return "record update time unknown";
        }

        string age = PromptFragments.Context.MemoryAge(Math.Max(0, currentTotalDays - day));
        string clock = time >= 600 && time <= 2600 && time % 100 < 60
            ? " at " + GameStateSnapshot.ClockText(time)
            : string.Empty;
        return $"record updated {age}{clock}";
    }
}
