using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json; 
using ValleyTalk;

namespace ValleyTalk;

internal class StardewEventHistory
{
    // Retention caps. Prompt assembly only ever consumes the ~20 most recent entries across all
    // four lists combined (Character.EventHistorySample), so these caps are invisible to dialogue
    // generation; they exist to keep save data bounded. Conversations are the largest entries and
    // the only ones surfaced in exported transcripts, so pruned conversations are returned to the
    // caller for archiving instead of being silently dropped.
    internal const int MaxEventEntries = 40;
    internal const int MaxOverheardEntries = 40;
    internal const int MaxDialogueEntries = 60;
    internal const int MaxConversationEntries = 30;
    // One in-game year; matches the "a long time ago" boundary in StardewTime.SinceDescription.
    internal const int MaxAgeDays = 112;

    private List<Tuple<StardewTime,IHistory>> _eventHistory = new();
    private List<Tuple<StardewTime,IHistory>> _overheardHistory = new();
    private List<Tuple<StardewTime,IHistory>> _dialogueHistory = new();
    private List<Tuple<StardewTime,IHistory>> _conversationHistory = new();

    public List<Tuple<StardewTime, DialogueEventHistory>> EventHistory
    {
        get
        {
            return _eventHistory.Select(x => new Tuple<StardewTime, DialogueEventHistory>(x.Item1, (DialogueEventHistory)x.Item2)).ToList();
        }
        set
        {
            _eventHistory = value.Select(x => new Tuple<StardewTime, IHistory>(x.Item1, x.Item2)).ToList();
        }
    }

    public List<Tuple<StardewTime, DialogueEventOverheard>> OverheardHistory
    {
        get
        {
            return _overheardHistory.Select(x => new Tuple<StardewTime, DialogueEventOverheard>(x.Item1, (DialogueEventOverheard)x.Item2)).ToList();
        }
        set
        {
            _overheardHistory = value.Select(x => new Tuple<StardewTime, IHistory>(x.Item1, x.Item2)).ToList();
        }
    }

    public List<Tuple<StardewTime, DialogueHistory>> DialogueHistory
    {
        get
        {
            return _dialogueHistory.Select(x => new Tuple<StardewTime, DialogueHistory>(x.Item1, (DialogueHistory)x.Item2)).ToList();
        }
        set
        {
            _dialogueHistory = value.Select(x => new Tuple<StardewTime, IHistory>(x.Item1, x.Item2)).ToList();
        }
    }

    public List<Tuple<StardewTime, ConversationHistory>> ConversationHistory
    {
        get
        {
            return _conversationHistory.Select(x => new Tuple<StardewTime, ConversationHistory>(x.Item1, (ConversationHistory)x.Item2)).ToList();
        }
        set
        {
            _conversationHistory = value.Select(x => new Tuple<StardewTime, IHistory>(x.Item1, x.Item2)).ToList();
        }
    }

    public void ClearConversationHistory()
    {
        _conversationHistory.Clear();
        _dialogueHistory.Clear();
        _overheardHistory.Clear();
    }

    [JsonIgnore]
    public IEnumerable<Tuple<StardewTime, IHistory>> AllTypes => 
        _eventHistory.AsEnumerable<Tuple<StardewTime, IHistory>>()
                .Concat(_overheardHistory)
                .Concat(_dialogueHistory)
                .Concat(_conversationHistory);

    internal void Add(StardewTime time, IHistory theEvent)
    {
        switch(theEvent.GetType().Name)
        {
            case "DialogueEventHistory":
                _eventHistory.Add(new(time,(DialogueEventHistory)theEvent));
                break;
            case "DialogueEventOverheard":
                _overheardHistory.Add(new(time,(DialogueEventOverheard)theEvent));
                break;
            case "DialogueHistory":
                _dialogueHistory.Add(new(time,(DialogueHistory)theEvent));
                break;
            case "ConversationHistory":
                var chEvent = theEvent as ConversationHistory;
                if (_conversationHistory.Any(x => ((ConversationHistory)x.Item2).Id == chEvent.Id))
                {
                    // If the conversation already exists, update it
                    _conversationHistory.RemoveAll(x => ((ConversationHistory)x.Item2).Id == chEvent.Id);
                }
                _conversationHistory.Add(new(time,chEvent));
                break;
            default:
                // ThirdPartyHistory (and any future type) has no persistable bucket: it holds live
                // object references that cannot round-trip through save data. Dropping the entry is
                // safer than throwing from inside the dialogue-recording pipeline.
                ModEntry.SMonitor?.Log($"Ignoring non-persistable history entry of type {theEvent.GetType().Name}.", StardewModdingAPI.LogLevel.Trace);
                break;
        }
    }

    internal bool Any()
    {
        return _eventHistory.Any() || _overheardHistory.Any() || _dialogueHistory.Any() || _conversationHistory.Any();
    }

    internal Tuple<StardewTime,IHistory> Last()
    {
        var lastEvent = _eventHistory.LastOrDefault();
        var lastOverheard = _overheardHistory.LastOrDefault();
        var lastDialogue = _dialogueHistory.LastOrDefault();
        var lastConversation = _conversationHistory.LastOrDefault();
        // Return the item with the latest time in Item1 of the tuple
        var lastEventTime = lastEvent?.Item1 ?? new StardewTime();
        var lastOverheardTime = lastOverheard?.Item1 ?? new StardewTime();
        var lastDialogueTime = lastDialogue?.Item1 ?? new StardewTime();
        var lastConversationTime = lastConversation?.Item1 ?? new StardewTime();
        var lastDlg = lastDialogueTime.CompareTo(lastConversationTime) > 0 ? (lastDialogue,lastDialogueTime) : (lastConversation,lastConversationTime);
        if (lastEventTime.CompareTo(lastOverheardTime) > 0)
        {
            return lastEventTime.CompareTo(lastDlg.Item2) > 0 ? lastEvent : lastDlg.Item1;
        }
        else
        {
            return lastOverheardTime.CompareTo(lastDlg.Item2) > 0 ? lastOverheard : lastDlg.Item1;
        }
    }

    internal void RemoveAfter(StardewTime timeNow)
    {
        if (_eventHistory.Any(x => x.Item1.After(timeNow)))
        {
            _eventHistory.RemoveAll(x => x.Item1.After(timeNow));
        }
        if (_overheardHistory.Any(x => x.Item1.After(timeNow)))
        {
            _overheardHistory.RemoveAll(x => x.Item1.After(timeNow));
        }
        if (_dialogueHistory.Any(x => x.Item1.After(timeNow)))
        {
            _dialogueHistory.RemoveAll(x => x.Item1.After(timeNow));
        }
        if (_conversationHistory.Any(x => x.Item1.After(timeNow)))
        {
            _conversationHistory.RemoveAll(x => x.Item1.After(timeNow));
        }
    }

    /// <summary>
    /// Trim each history list to its retention cap (and age limit where applicable) so save data
    /// stays bounded. Returns the dropped conversation entries, oldest first, so the caller can
    /// archive them into the exported transcript before they disappear; dropped entries from the
    /// other lists are discarded (they never surface in transcripts).
    /// Calling this on an already-pruned history is a no-op that returns an empty list.
    /// </summary>
    internal List<Tuple<StardewTime, ConversationHistory>> Prune(StardewTime now)
    {
        PruneList(_eventHistory, MaxEventEntries, now, applyAgeLimit: false);
        PruneList(_overheardHistory, MaxOverheardEntries, now, applyAgeLimit: true);
        PruneList(_dialogueHistory, MaxDialogueEntries, now, applyAgeLimit: true);
        return PruneList(_conversationHistory, MaxConversationEntries, now, applyAgeLimit: true, protectToday: true)
            .Select(x => new Tuple<StardewTime, ConversationHistory>(x.Item1, (ConversationHistory)x.Item2))
            .ToList();
    }

    private static List<Tuple<StardewTime, IHistory>> PruneList(
        List<Tuple<StardewTime, IHistory>> list,
        int maxEntries,
        StardewTime now,
        bool applyAgeLimit,
        bool protectToday = false)
    {
        var dropped = new List<Tuple<StardewTime, IHistory>>();
        if (list.Count == 0)
        {
            return dropped;
        }

        // Entries protected from pruning: an in-progress conversation is re-added with today's
        // timestamp on every turn, so dropping a same-day entry could archive a live conversation.
        bool IsProtected(Tuple<StardewTime, IHistory> entry) => protectToday && entry.Item1.DaysSince(now) < 1;

        var ordered = list.OrderBy(x => x.Item1).ToList();
        var retained = new List<Tuple<StardewTime, IHistory>>(ordered.Count);
        foreach (var entry in ordered)
        {
            if (applyAgeLimit && !IsProtected(entry) && entry.Item1.DaysSince(now) > MaxAgeDays)
            {
                dropped.Add(entry);
            }
            else
            {
                retained.Add(entry);
            }
        }

        int excess = retained.Count - maxEntries;
        for (int i = 0; i < retained.Count && excess > 0;)
        {
            if (IsProtected(retained[i]))
            {
                i++;
                continue;
            }

            dropped.Add(retained[i]);
            retained.RemoveAt(i);
            excess--;
        }

        list.Clear();
        list.AddRange(retained);
        return dropped;
    }

    internal void RemoveDialogueOverlapping(List<ConversationElement> chatHistory)    {
        foreach (var chat in chatHistory)
        {
            _dialogueHistory.RemoveAll(x => ((DialogueHistory)x.Item2).Dialogues.Any(z => z.Text.Equals(chat.Text)));
        }
    }

    internal void RemoveOverheardOverlapping(string name, List<StardewValley.DialogueLine> overheardDialogue)
    {
        foreach (var dialogue in overheardDialogue)
        {
            _overheardHistory.RemoveAll(x => ((DialogueEventOverheard)x.Item2).dialogues.Any(z => z.Text.Equals(dialogue.Text)) && ((DialogueEventOverheard)x.Item2).name.Equals(name));
        }
    }
}
