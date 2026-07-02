using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using ValleyTalk;

namespace ValleyTalk;

public class EventHistoryReader
{
    public static EventHistoryReader Instance { get; } = new EventHistoryReader();

    private static readonly Dictionary<string, string> _conversionCache = new();

    private EventHistoryReader()
    {
        if (!Context.IsMainPlayer)
        {
            _multiplayerFilename = $"multiplayer/{Constants.SaveFolderName}.json";
            _fileEventHistories = ModEntry.SHelper.Data.ReadJsonFile<Dictionary<string, StardewEventHistory>>(_multiplayerFilename) ?? new();
            ModEntry.SHelper.Events.GameLoop.Saving += OnSavingFile;
        }
        else
        {
            _saveCache = new();
            ModEntry.SHelper.Events.GameLoop.Saving += OnSavingGameData;
        }
    }

    private void OnSavingFile(object sender, SavingEventArgs e)
    {
        ModEntry.SHelper.Data.WriteJsonFile(_multiplayerFilename, _fileEventHistories);
    }

    private void OnSavingGameData(object sender, SavingEventArgs e)
    {
        foreach (var kvp in _saveCache)
        {
            ModEntry.SHelper.Data.WriteSaveData(kvp.Key, kvp.Value);
        }
        _saveCache.Clear();
    }

    private Dictionary<string, StardewEventHistory> _fileEventHistories = new();
    private Dictionary<string, StardewEventHistory> _saveCache;
    private readonly string _multiplayerFilename;

    internal void ClearSessionCache()
    {
        _saveCache?.Clear();
        if (!Context.IsMainPlayer)
        {
            _fileEventHistories.Clear();
        }
    }

    internal StardewEventHistory GetEventHistory(string name)
    {
        if (Context.IsMainPlayer)
        {
            var history = LoadFromSaveFile(name);
            PruneAndArchive(name, history);
            return history;
        }
        else
        {
            if (_fileEventHistories.TryGetValue(name, out var history))
            {
                PruneAndArchive(name, history);
                return history;
            }

            return new StardewEventHistory();
        }
    }

    /// <summary>
    /// Trim the history to its retention caps and archive any pruned conversations into the
    /// exported transcript. Idempotent: pruning an already-pruned history drops nothing, and the
    /// transcript archive skips conversations at or before its watermark, so re-loading the same
    /// unpruned save blob (the main-player path deserializes it fresh on every call) is safe.
    /// </summary>
    private void PruneAndArchive(string name, StardewEventHistory history)
    {
        var dropped = history.Prune(new StardewTime(Game1.Date, Game1.timeOfDay));
        if (dropped.Count == 0)
        {
            return;
        }

        ConversationTranscriptExporter.ArchivePrunedConversations(name, dropped);
        if (Context.IsMainPlayer)
        {
            // Queue the pruned history so the next save persists the smaller blob even if the
            // player never talks to this NPC again this session.
            _saveCache[$"EventHistory_{GetSaveName(name)}"] = history;
        }
        // Farmhand path: the history object is the same instance stored in _fileEventHistories,
        // so the pruned state is written out with the next file save automatically.
    }
            
    private static StardewEventHistory LoadFromSaveFile(string name)
    {
        {
            var saveName = GetSaveName(name);
            var eventKey = $"EventHistory_{saveName}";
            try
            {
                var history = ModEntry.SHelper.Data.ReadSaveData<StardewEventHistory>(eventKey);
                if (history != null)
                {
                    // Remove anything from the history that happens after the current game time
                    history.RemoveAfter(new StardewTime(Game1.Date,Game1.timeOfDay));
                    return history;
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Error loading event history for {name} from save file. {ex}", LogLevel.Error);
            }
        }

        return new StardewEventHistory();
    }

    internal void UpdateEventHistory(string name, StardewEventHistory eventHistory)
    {
        // Keep long play sessions bounded too: history grows on every recorded line, and the save
        // is only pruned again at the next load.
        PruneAndArchive(name, eventHistory);

        if (Context.IsMainPlayer)
        {
            var saveName = GetSaveName(name);
            var eventKey = $"EventHistory_{saveName}";
            _saveCache[eventKey] = eventHistory;
        }
        else
        {
            _fileEventHistories[name] = eventHistory;
        }

        ConversationTranscriptExporter.Export(name, eventHistory);
    }

    internal bool ClearEventHistory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var emptyHistory = new StardewEventHistory();
        bool hadHistory;
        if (Context.IsMainPlayer)
        {
            var saveName = GetSaveName(name);
            var eventKey = $"EventHistory_{saveName}";
            hadHistory = (_saveCache.TryGetValue(eventKey, out var cachedHistory) && cachedHistory.Any())
                || LoadFromSaveFile(name).Any();
            _saveCache[eventKey] = emptyHistory;
            ModEntry.SHelper.Data.WriteSaveData(eventKey, emptyHistory);
        }
        else
        {
            hadHistory = _fileEventHistories.Remove(name);
            ModEntry.SHelper.Data.WriteJsonFile(_multiplayerFilename, _fileEventHistories);
        }

        // Wipe the transcript archive along with the live section: "forget" means the whole file.
        ConversationTranscriptExporter.ResetTranscript(name);
        return hadHistory;
    }

    private static string GetSaveName(string name)
    {
        string saveName;
        if (_conversionCache.TryGetValue(name, out saveName))
        {
            return saveName;
        }
        // Create a list of characters that are valid - letters, numbers, underscores, periods, or hyphens
        const string validCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.";
        saveName = name;
        // Strip out any characters that are not valid
        foreach (var ch in name.Distinct())
        {
            if (!validCharacters.Contains(ch))
            {
                saveName = saveName.Replace(ch.ToString(), string.Empty);
            }
        }

        // If the name is empty (no valid characters), take a hexadecimal representation of the string bytes
        if (string.IsNullOrEmpty(saveName))
        {
            saveName = BitConverter.ToString(name.Select(ch => (byte)ch).ToArray()).Replace("-", "");
        }

        // If the name is too long, truncate it to 50 characters
        if (saveName.Length > 50)
        {
            saveName = saveName.Substring(0, 50);
        }
        _conversionCache[name] = saveName;
        return saveName;
    }
}
