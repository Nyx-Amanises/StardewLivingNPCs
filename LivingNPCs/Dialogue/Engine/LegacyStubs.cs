using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

internal static class SldConstants
{
    // WP12-TODO: replace with the final dialogue key namespace.
    public const string DialogueKeyPrefix = "LivingNPCs_";
}

internal sealed class Character
{
    public Character(string name, NPC? stardewNpc = null)
    {
        this.Name = string.IsNullOrWhiteSpace(name) ? stardewNpc?.Name ?? string.Empty : name;
        this.StardewNpc = stardewNpc;
    }

    public string Name { get; }
    public NPC? StardewNpc { get; }
    public CharacterBio? Bio { get; init; }
}

internal sealed class CharacterBio
{
    public Dictionary<string, CharacterBioTrait> Traits { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class CharacterBioTrait
{
    public string Heading { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

internal sealed class DialogueContext
{
    public object? Accept { get; set; }
    public List<ConversationElement> ChatHistory { get; set; } = new();
    public string LivingNpcExtraPrompt { get; set; } = string.Empty;
    public int? Hearts { get; set; }
    public string Location { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public List<string> Weather { get; set; } = new();
    public string CurrentActivity { get; set; } = string.Empty;
    public string NextScheduleLocation { get; set; } = string.Empty;
    public int? MinutesUntilNextSchedule { get; set; }
}

internal sealed class ConversationElement
{
    public ConversationElement()
    {
    }

    public ConversationElement(string text, bool isPlayerLine)
    {
        this.Text = text;
        this.IsPlayerLine = isPlayerLine;
    }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool IsPlayerLine { get; set; }
}

internal sealed class DialogueBuilder
{
    public static DialogueBuilder Instance { get; } = new();

    private DialogueBuilder()
    {
    }

    public bool PatchNpc(NPC npc)
    {
        // WP10-TODO: route through IDialogueEngine once WP10/WP12 own the dialogue entrypoint.
        return false;
    }

    public void ClearContext()
    {
        // WP10-TODO: replace with the new dialogue session/context lifecycle.
    }

    public Character GetCharacter(NPC npc)
    {
        return new Character(npc?.Name ?? string.Empty, npc);
    }

    public Character GetCharacterByName(string npcName)
    {
        return new Character(npcName);
    }
}

internal static class Prompts
{
    public static bool IsLivingNpcThirdPartyOverride(string text)
    {
        // WP10-TODO: move this helper into the new prompt assembly path.
        return !string.IsNullOrWhiteSpace(text)
            && (text.Contains("LivingNPCs", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Active Companion Outing", StringComparison.OrdinalIgnoreCase));
    }
}


internal sealed class AsyncBuilder
{
    public static AsyncBuilder Instance { get; } = new();

    private AsyncBuilder()
    {
    }

    public void CancelActiveGeneration()
    {
        // WP10-TODO: cancellation moves to IDialogueEngine generation tokens.
    }
}

internal static class TextInputManager
{
    public static void RequestTextInput(string prompt, NPC npc)
    {
        // WP12-TODO: wire typed dialogue input to the new game hook entrypoint.
    }
}
internal sealed class EventHistoryReader
{
    public static EventHistoryReader Instance { get; } = new();

    public StardewEventHistory GetEventHistory(string npcName)
    {
        // WP14-TODO: wire to the new IDialogueHistory persistence store.
        return new StardewEventHistory();
    }
}

internal sealed class StardewEventHistory
{
    public const int MaxConversationEntries = 20;
    public const int MaxAgeDays = 112;

    public List<Tuple<StardewTime, ConversationHistory>> ConversationHistory { get; set; } = new();
    public List<Tuple<StardewTime, DialogueHistory>> DialogueHistory { get; set; } = new();
    public List<Tuple<StardewTime, DialogueEventHistory>> EventHistory { get; set; } = new();

    public void Add(StardewTime time, ConversationHistory history)
    {
        if (history != null)
        {
            this.ConversationHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, DialogueHistory history)
    {
        if (history != null)
        {
            this.DialogueHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, DialogueEventHistory history)
    {
        if (history != null)
        {
            this.EventHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, ThirdPartyHistory history)
    {
        // WP14-TODO: third-party history has no persistence bucket in the new store yet.
    }

    public bool Any()
    {
        return this.ConversationHistory.Count > 0
            || this.DialogueHistory.Count > 0
            || this.EventHistory.Count > 0;
    }

    public List<Tuple<StardewTime, ConversationHistory>> Prune(StardewTime now)
    {
        int today = now.ToAbsoluteDays();
        int oldestAllowedDay = today - MaxAgeDays;
        this.DialogueHistory = this.DialogueHistory
            .Where(entry => entry.Item1.ToAbsoluteDays() >= oldestAllowedDay)
            .OrderBy(entry => entry.Item1)
            .ToList();

        var ordered = this.ConversationHistory
            .OrderBy(entry => entry.Item1)
            .ToList();
        var prunable = ordered
            .Where(entry => entry.Item1.ToAbsoluteDays() != today)
            .ToList();
        int overflow = ordered.Count - MaxConversationEntries;
        var dropped = overflow > 0
            ? prunable.Take(overflow).ToList()
            : new List<Tuple<StardewTime, ConversationHistory>>();
        if (dropped.Count > 0)
        {
            var droppedSet = new HashSet<Tuple<StardewTime, ConversationHistory>>(dropped);
            ordered = ordered.Where(entry => !droppedSet.Contains(entry)).ToList();
        }

        this.ConversationHistory = ordered;
        return dropped;
    }
}

internal sealed class ConversationHistory
{
    public ConversationHistory()
    {
    }

    public ConversationHistory(List<ConversationElement> conversationElements)
    {
        this.ConversationElements = conversationElements ?? new List<ConversationElement>();
    }

    public List<ConversationElement> ConversationElements { get; set; } = new();
}

internal sealed class DialogueHistory
{
    public DialogueHistory()
    {
    }

    public DialogueHistory(List<DialogueLine> dialogues)
    {
        this.Dialogues = dialogues ?? new List<DialogueLine>();
    }

    public List<DialogueLine> Dialogues { get; set; } = new();
}

internal sealed class DialogueEventHistory
{
    public DialogueEventHistory(List<NPC>? npcs, List<DialogueLine>? dialogues, string eventId)
    {
        this.EventId = eventId ?? string.Empty;
    }

    public string EventId { get; }
}

internal sealed class ThirdPartyHistory
{
    public ThirdPartyHistory(object? source, List<DialogueLine>? dialogues, string eventId)
    {
    }
}

internal readonly record struct StardewTime(int year, Season season, int dayOfMonth, int timeOfDay) : IComparable<StardewTime>
{
    private const int DaysPerSeason = 28;
    private const int SeasonsPerYear = 4;
    private const int DaysPerYear = DaysPerSeason * SeasonsPerYear;

    public int ToAbsoluteDays()
    {
        return (this.year * DaysPerYear) + (SeasonIndex(this.season) * DaysPerSeason) + this.dayOfMonth;
    }

    public StardewTime AddDays(int days)
    {
        int zeroBasedDay = Math.Max(0, (this.year * DaysPerYear) + (SeasonIndex(this.season) * DaysPerSeason) + (this.dayOfMonth - 1) + days);
        int newYear = zeroBasedDay / DaysPerYear;
        int dayOfYear = zeroBasedDay % DaysPerYear;
        int newSeasonIndex = dayOfYear / DaysPerSeason;
        int newDay = (dayOfYear % DaysPerSeason) + 1;
        return new StardewTime(newYear, SeasonFromIndex(newSeasonIndex), newDay, this.timeOfDay);
    }

    public int CompareTo(StardewTime other)
    {
        int dayComparison = this.ToAbsoluteDays().CompareTo(other.ToAbsoluteDays());
        return dayComparison != 0 ? dayComparison : this.timeOfDay.CompareTo(other.timeOfDay);
    }

    private static int SeasonIndex(Season season)
    {
        return season switch
        {
            Season.Spring => 0,
            Season.Summer => 1,
            Season.Fall => 2,
            Season.Winter => 3,
            _ => 0
        };
    }

    private static Season SeasonFromIndex(int index)
    {
        return index switch
        {
            1 => Season.Summer,
            2 => Season.Fall,
            3 => Season.Winter,
            _ => Season.Spring
        };
    }
}
