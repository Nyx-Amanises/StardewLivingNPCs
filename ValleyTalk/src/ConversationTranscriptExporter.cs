using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewValley;

namespace ValleyTalk;

internal static class ConversationTranscriptExporter
{
    private const string RootFolderName = "conversation_logs";

    public static string CurrentSaveFolderPath => Path.Combine(
        ModEntry.SHelper.DirectoryPath,
        RootFolderName,
        string.IsNullOrWhiteSpace(Constants.SaveFolderName) ? "unknown-save" : Constants.SaveFolderName
    );

    public static void ExportAllKnownHistories()
    {
        if (!Context.IsWorldReady)
        {
            return;
        }

        foreach (string npcName in Game1.characterData.Keys.OrderBy(name => name))
        {
            var history = EventHistoryReader.Instance.GetEventHistory(npcName);
            Export(npcName, history);
        }
    }

    public static void Export(string npcName, StardewEventHistory history)
    {
        if (string.IsNullOrWhiteSpace(npcName) || history == null || ModEntry.SHelper == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CurrentSaveFolderPath);
            string filePath = Path.Combine(CurrentSaveFolderPath, $"{GetSafeFileName(npcName)}.md");
            File.WriteAllText(filePath, BuildMarkdown(npcName, history), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor?.Log($"Failed to export conversation transcript for {npcName}: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
        }
    }

    private static string BuildMarkdown(string npcName, StardewEventHistory history)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {npcName}");
        builder.AppendLine();
        builder.AppendLine(T("transcriptSave", "- Save: {{save}}", new { save = Constants.SaveFolderName }));
        builder.AppendLine(T("transcriptExportTime", "- Export time: Day {{day}} {{time}}", new { day = Game1.Date.TotalDays, time = Game1.timeOfDay.ToString("0000") }));
        builder.AppendLine();

        var transcriptEntries = BuildTranscriptEntries(history)
            .OrderBy(entry => entry.Time)
            .ToList();

        if (transcriptEntries.Count == 0)
        {
            builder.AppendLine(T("transcriptNoConversations", "No AI conversation records yet."));
            return builder.ToString();
        }

        for (int i = 0; i < transcriptEntries.Count; i++)
        {
            var entry = transcriptEntries[i];
            var time = entry.Time;
            builder.AppendLine(T(
                "transcriptConversationHeading",
                "## Conversation {{index}}: Year {{year}}, {{season}} {{day}} at {{time}}",
                new
                {
                    index = i + 1,
                    year = time.year,
                    season = FormatSeason(time.season),
                    day = time.dayOfMonth,
                    time = time.timeOfDay.ToString("0000")
                }));
            builder.AppendLine();

            foreach (var line in entry.Lines)
            {
                string speaker = line.IsPlayerLine ? T("transcriptPlayer", "Player") : npcName;
                builder.AppendLine(T(
                    "transcriptLine",
                    "- **{{speaker}}:** {{text}}",
                    new { speaker, text = SanitizeTranscriptText(line.Text) }));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IEnumerable<TranscriptEntry> BuildTranscriptEntries(StardewEventHistory history)
    {
        foreach (var entry in history.DialogueHistory)
        {
            var lines = entry.Item2.Dialogues
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new TranscriptLine(false, line.Text))
                .ToList();
            if (lines.Count > 0)
            {
                yield return new TranscriptEntry(entry.Item1, lines);
            }
        }

        foreach (var entry in history.ConversationHistory)
        {
            var lines = entry.Item2.ConversationElements
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new TranscriptLine(line.IsPlayerLine, line.Text))
                .ToList();
            if (lines.Count > 0)
            {
                yield return new TranscriptEntry(entry.Item1, lines);
            }
        }
    }

    private static string SanitizeTranscriptText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = text.Replace("skip#", string.Empty, StringComparison.Ordinal);
        int responseIndex = cleaned.IndexOf("#$q ", StringComparison.Ordinal);
        if (responseIndex >= 0)
        {
            cleaned = cleaned[..responseIndex];
        }

        cleaned = cleaned.Replace("#$b#", " / ", StringComparison.Ordinal);
        cleaned = cleaned.Replace("#$e#", " / ", StringComparison.Ordinal);
        cleaned = cleaned.Replace("\r", " ", StringComparison.Ordinal);
        cleaned = cleaned.Replace("\n", " ", StringComparison.Ordinal);
        return cleaned.Trim();
    }

    private static string T(string key, string fallback, object tokens = null)
    {
        string result = Util.GetString(key, tokens, returnNull: true);
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }

    private static string FormatSeason(StardewValley.Season season)
    {
        return season switch
        {
            StardewValley.Season.Spring => T("transcriptSeasonSpring", "Spring"),
            StardewValley.Season.Summer => T("transcriptSeasonSummer", "Summer"),
            StardewValley.Season.Fall => T("transcriptSeasonFall", "Fall"),
            StardewValley.Season.Winter => T("transcriptSeasonWinter", "Winter"),
            _ => season.ToString()
        };
    }

    private static string GetSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.Length == 0 ? "unknown-npc" : builder.ToString();
    }

    private sealed class TranscriptEntry
    {
        public TranscriptEntry(StardewTime time, List<TranscriptLine> lines)
        {
            this.Time = time;
            this.Lines = lines;
        }

        public StardewTime Time { get; }
        public List<TranscriptLine> Lines { get; }
    }

    private sealed class TranscriptLine
    {
        public TranscriptLine(bool isPlayerLine, string text)
        {
            this.IsPlayerLine = isPlayerLine;
            this.Text = text;
        }

        public bool IsPlayerLine { get; }
        public string Text { get; }
    }
}
