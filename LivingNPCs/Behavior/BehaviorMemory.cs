using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class BehaviorMemory
{
    private readonly Dictionary<string, List<BehaviorMemoryEntry>> entriesByNpc = new();
    private readonly Dictionary<string, int> dailyCountsByNpc = new();

    public void Load(BehaviorMemorySaveData? saveData, int maxEntriesPerNpc)
    {
        this.entriesByNpc.Clear();
        this.dailyCountsByNpc.Clear();

        if (saveData?.EntriesByNpc == null)
        {
            return;
        }

        foreach (var pair in saveData.EntriesByNpc)
        {
            var entries = pair.Value
                .Where(entry => !string.IsNullOrWhiteSpace(entry.NpcName))
                .OrderBy(entry => entry.TotalDays)
                .ThenBy(entry => entry.TimeOfDay)
                .TakeLast(maxEntriesPerNpc)
                .ToList();

            if (entries.Count > 0)
            {
                this.entriesByNpc[pair.Key] = entries;
            }
        }

        this.RebuildDailyCounts();
    }

    public BehaviorMemorySaveData ToSaveData()
    {
        return new BehaviorMemorySaveData
        {
            EntriesByNpc = this.entriesByNpc.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList()
            )
        };
    }

    public void ResetDaily()
    {
        this.dailyCountsByNpc.Clear();
    }

    public bool HasDailyBudget(NPC npc, int maxPerDay)
    {
        return !this.dailyCountsByNpc.TryGetValue(npc.Name, out int count) || count < maxPerDay;
    }

    public BehaviorMemoryEntry Record(NPC npc, BehaviorIntent intent, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(npc, "Behavior", intent.Type.ToString(), intent.Reason);
        this.AddEntry(entry, maxEntriesPerNpc);
        this.dailyCountsByNpc[npc.Name] = this.dailyCountsByNpc.TryGetValue(npc.Name, out int count) ? count + 1 : 1;
        return entry;
    }

    public BehaviorMemoryEntry RecordConversationStart(NPC npc, int maxEntriesPerNpc)
    {
        var entry = this.CreateEntry(
            npc,
            "Conversation",
            "ConversationStarted",
            "the farmer approached and started a conversation"
        );

        this.AddEntry(entry, maxEntriesPerNpc);
        return entry;
    }

    private BehaviorMemoryEntry CreateEntry(NPC npc, string kind, string action, string reason)
    {
        return new BehaviorMemoryEntry
        {
            NpcName = npc.Name,
            Kind = kind,
            Action = action,
            Reason = reason,
            Year = Game1.year,
            Season = Game1.season.ToString(),
            Day = Game1.dayOfMonth,
            TimeOfDay = Game1.timeOfDay,
            TotalDays = Game1.Date.TotalDays,
            LocationName = npc.currentLocation?.Name ?? string.Empty,
            LocationDisplayName = npc.currentLocation?.DisplayName ?? string.Empty
        };
    }

    private void AddEntry(BehaviorMemoryEntry entry, int maxEntriesPerNpc)
    {
        if (!this.entriesByNpc.TryGetValue(entry.NpcName, out var entries))
        {
            entries = new List<BehaviorMemoryEntry>();
            this.entriesByNpc[entry.NpcName] = entries;
        }

        entries.Add(entry);
        while (entries.Count > maxEntriesPerNpc)
        {
            entries.RemoveAt(0);
        }
    }

    public string BuildPromptContext(NPC npc, int maxEntries)
    {
        if (!this.entriesByNpc.TryGetValue(npc.Name, out var entries) || entries.Count == 0)
        {
            return string.Empty;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine("## LivingNPCs behavior and interaction context");
        prompt.AppendLine($"{npc.displayName} has recent small in-world actions and player interaction moments tracked by LivingNPCs.");
        prompt.AppendLine("Use these as quiet scene context for the next reply. Do not mention LivingNPCs, prompts, mods, JSON, or AI systems.");
        prompt.AppendLine("If an entry is relevant, the character may naturally acknowledge it as something that just happened or affected the mood of the scene.");

        foreach (var entry in entries.TakeLast(maxEntries))
        {
            prompt.AppendLine($"- {this.FormatPromptEntry(entry)}");
        }

        prompt.AppendLine("Keep the next line in character and consistent with the current Stardew Valley scene.");
        return prompt.ToString();
    }

    public string BuildDebugSummary(NPC npc, int maxEntries)
    {
        if (!this.entriesByNpc.TryGetValue(npc.Name, out var entries) || entries.Count == 0)
        {
            return $"{npc.displayName} 还没有 LivingNPCs 行为记忆。";
        }

        var summary = new StringBuilder();
        summary.AppendLine($"{npc.displayName} 最近 {System.Math.Min(entries.Count, maxEntries)} 条 LivingNPCs 行为/互动记忆：");
        foreach (var entry in entries.TakeLast(maxEntries))
        {
            string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
            string locationSuffix = string.IsNullOrWhiteSpace(location) ? string.Empty : $" @ {location}";
            summary.AppendLine($"- 第 {entry.TotalDays} 天 {entry.TimeOfDay}{locationSuffix}: {this.FormatDebugKind(entry)} {entry.Action}; {entry.Reason}");
        }

        return summary.ToString().TrimEnd();
    }

    private string FormatPromptEntry(BehaviorMemoryEntry entry)
    {
        string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
        string locationSuffix = string.IsNullOrWhiteSpace(location) ? string.Empty : $" at {location}";
        string kind = string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase)
            ? "conversation"
            : "behavior";

        return $"{entry.Season} {entry.Day}, {entry.TimeOfDay}{locationSuffix}: {kind} - {entry.Action}; reason: {entry.Reason}";
    }

    private string FormatDebugKind(BehaviorMemoryEntry entry)
    {
        return string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase)
            ? "[对话]"
            : "[行为]";
    }

    private void RebuildDailyCounts()
    {
        foreach (var pair in this.entriesByNpc)
        {
            int count = pair.Value.Count(entry =>
                entry.TotalDays == Game1.Date.TotalDays
                && !string.Equals(entry.Kind, "Conversation", System.StringComparison.OrdinalIgnoreCase)
            );
            if (count > 0)
            {
                this.dailyCountsByNpc[pair.Key] = count;
            }
        }
    }
}

internal sealed class BehaviorMemorySaveData
{
    public Dictionary<string, List<BehaviorMemoryEntry>> EntriesByNpc { get; set; } = new();
}

internal sealed class BehaviorMemoryEntry
{
    public string NpcName { get; set; } = string.Empty;
    public string Kind { get; set; } = "Behavior";
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Season { get; set; } = string.Empty;
    public int Day { get; set; }
    public int TimeOfDay { get; set; }
    public int TotalDays { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string LocationDisplayName { get; set; } = string.Empty;
}
