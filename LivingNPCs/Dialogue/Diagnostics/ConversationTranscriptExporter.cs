using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Diagnostics;

internal static class ConversationTranscriptExporter
{
    private const string RootFolderName = "conversation_logs";

    // Archive block markers. Everything between the two marker lines is append-only transcript
    // content preserved from pruned history; the section after the end marker is rebuilt from
    // in-memory history on every export. The markers are HTML comments so rendered markdown
    // still reads as one continuous log.
    private const string ArchiveMarkerPrefix = "<!-- valleytalk:archive ";
    private const string ArchiveEndMarker = "<!-- valleytalk:archive-end -->";

    // Export runs after every recorded conversation line, but only the live tail actually
    // changes; the archive above it is append-only and unbounded. Remember where the live
    // section starts (byte offset just past the archive-end marker line and its blank line)
    // and how many conversations the archive holds, so an ordinary export can truncate and
    // rewrite the bounded tail without re-reading or re-writing the whole file. Entries are
    // seeded by full writes and dropped whenever the file is rebuilt or fails validation.
    private static readonly object LiveSectionGate = new();
    private static readonly Dictionary<string, (long Offset, int ArchivedCount)> LiveSectionOffsets = new(StringComparer.OrdinalIgnoreCase);

    public static string CurrentSaveFolderPath => Path.Combine(
        DialogueServices.Helper.DirectoryPath,
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
            var history = DialogueHistoryStore.Instance.GetHistory(npcName);
            Export(npcName, history);
        }
    }

    public static void Export(string npcName, StardewEventHistory history)
    {
        if (string.IsNullOrWhiteSpace(npcName) || history == null || DialogueServices.Helper == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CurrentSaveFolderPath);
            string filePath = GetTranscriptPath(npcName);
            if (TryRewriteLiveSectionOnly(filePath, npcName, history))
            {
                return;
            }

            var archive = ReadArchiveBlock(filePath);
            WriteFullTranscript(filePath, npcName, history, archive);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.exportFailed",
                    new { what = "conversation transcript", npc = npcName, error = ex.Message },
                    $"Failed to export conversation transcript for {npcName}: {ex.Message}"),
                StardewModdingAPI.LogLevel.Warn);
        }
    }

    /// <summary>
    /// Reset the transcript to an empty file with no archive block. Used when the player asks the
    /// NPC to forget its history: the archive must be wiped along with the live section.
    /// </summary>
    public static void ResetTranscript(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName) || DialogueServices.Helper == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CurrentSaveFolderPath);
            WriteFullTranscript(GetTranscriptPath(npcName), npcName, new StardewEventHistory(), new ArchiveBlock());
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = $"reset the conversation transcript for {npcName}", error = ex.Message },
                    $"Failed to reset the conversation transcript for {npcName}: {ex.Message}"),
                StardewModdingAPI.LogLevel.Warn);
        }
    }

    /// <summary>
    /// Append conversations that are about to be pruned from history into the transcript's archive
    /// block, so the exported log keeps the full chat history even though save data stays bounded.
    /// The existing live section is preserved untouched; the next Export rebuilds it anyway.
    /// </summary>
    public static void ArchivePrunedConversations(string npcName, List<Tuple<StardewTime, ConversationHistory>> dropped)
    {
        if (string.IsNullOrWhiteSpace(npcName) || dropped == null || dropped.Count == 0 || DialogueServices.Helper == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(CurrentSaveFolderPath);
            string filePath = GetTranscriptPath(npcName);
            var archive = ReadArchiveBlock(filePath);

            var entries = dropped
                .Where(entry => entry?.Item1 != null && entry.Item2?.ConversationElements != null)
                .Select(entry => new TranscriptEntry(
                    entry.Item1,
                    BuildTranscriptLines(entry.Item2.ConversationElements
                        .Select(line => new TranscriptLine(line.IsPlayerLine, line.Text)))))
                .Where(entry => entry.Lines.Count > 0)
                .OrderBy(entry => entry.Time)
                .ToList();
            entries = RemoveRedundantPrefixEntries(entries);

            // Watermark dedup: the same save blob can be pruned repeatedly before the pruned
            // version is written back (the main-player path re-deserializes save data on every
            // GetEventHistory call), so skip conversations at or before the newest archived time.
            // Conversation ids are regenerated on deserialization, so time is the only stable key;
            // a same-minute tie straddling two prune batches could be skipped, which we accept.
            entries = entries
                .Where(entry => IsAfterWatermark(entry.Time, archive.LastDay, archive.LastTime))
                .ToList();
            if (entries.Count == 0)
            {
                return;
            }

            var sectionBuilder = new StringBuilder();
            int index = archive.Count;
            foreach (var entry in entries)
            {
                index++;
                AppendConversationSection(sectionBuilder, entry, index, npcName);
            }

            var newest = entries[entries.Count - 1].Time;
            var builder = new StringBuilder();
            AppendHeader(builder, npcName);
            builder.AppendLine(BuildArchiveMarker(index, newest.ToAbsoluteDays(), newest.timeOfDay));
            if (!string.IsNullOrWhiteSpace(archive.Content))
            {
                builder.AppendLine(archive.Content.TrimEnd());
                builder.AppendLine();
            }
            builder.Append(sectionBuilder);
            builder.AppendLine(ArchiveEndMarker);
            builder.AppendLine();
            if (!string.IsNullOrWhiteSpace(archive.LiveContent))
            {
                builder.AppendLine(archive.LiveContent.TrimEnd());
            }

            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
            // The archive grew, so the cached live-section offset is stale; the next Export does
            // one full pass and re-seeds it.
            InvalidateLiveSectionOffset(filePath);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = $"archive pruned conversations for {npcName}", error = ex.Message },
                    $"Failed to archive pruned conversations for {npcName}: {ex.Message}"),
                StardewModdingAPI.LogLevel.Warn);
        }
    }

    private static string GetTranscriptPath(string npcName)
    {
        return Path.Combine(CurrentSaveFolderPath, $"{GetSafeFileName(npcName)}.md");
    }

    /// <summary>
    /// Rebuild and write the whole transcript file (header, archive block, live section), then
    /// remember where the live section starts so subsequent exports can rewrite only the tail.
    /// </summary>
    private static void WriteFullTranscript(string filePath, string npcName, StardewEventHistory history, ArchiveBlock archive)
    {
        bool hasArchive = archive.HasMarkers && archive.Count > 0 && !string.IsNullOrWhiteSpace(archive.Content);
        var transcriptEntries = BuildTranscriptEntries(history)
            .OrderBy(entry => entry.Time)
            .ToList();

        var builder = new StringBuilder();
        AppendHeader(builder, npcName);

        if (!hasArchive && transcriptEntries.Count == 0)
        {
            builder.AppendLine(T("transcriptNoConversations", "No AI conversation records yet."));
            InvalidateLiveSectionOffset(filePath);
            File.WriteAllText(filePath, builder.ToString(), Encoding.UTF8);
            return;
        }

        builder.AppendLine(BuildArchiveMarker(
            hasArchive ? archive.Count : 0,
            hasArchive ? archive.LastDay : 0,
            hasArchive ? archive.LastTime : 0));
        if (hasArchive)
        {
            builder.AppendLine(archive.Content.TrimEnd());
            builder.AppendLine();
        }
        builder.AppendLine(ArchiveEndMarker);
        builder.AppendLine();

        int archivedCount = hasArchive ? archive.Count : 0;
        string liveSection = BuildLiveSection(npcName, transcriptEntries, archivedCount);
        File.WriteAllText(filePath, builder.ToString() + liveSection, Encoding.UTF8);

        long liveOffset = new FileInfo(filePath).Length - Encoding.UTF8.GetByteCount(liveSection);
        lock (LiveSectionGate)
        {
            LiveSectionOffsets[filePath] = (liveOffset, archivedCount);
        }
    }

    /// <summary>
    /// The per-line fast path: truncate the file at the remembered live-section offset and write a
    /// fresh live section, leaving the unbounded archive bytes above it untouched. Returns false
    /// (after invalidating the cached offset) whenever the file does not look like what we last
    /// wrote, in which case the caller falls back to a full rebuild.
    /// </summary>
    private static bool TryRewriteLiveSectionOnly(string filePath, string npcName, StardewEventHistory history)
    {
        long liveOffset;
        int archivedCount;
        lock (LiveSectionGate)
        {
            if (!LiveSectionOffsets.TryGetValue(filePath, out var cached))
            {
                return false;
            }

            (liveOffset, archivedCount) = cached;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (!LiveSectionOffsetIsValid(stream, liveOffset))
            {
                InvalidateLiveSectionOffset(filePath);
                return false;
            }

            var transcriptEntries = BuildTranscriptEntries(history)
                .OrderBy(entry => entry.Time)
                .ToList();
            string liveSection = BuildLiveSection(npcName, transcriptEntries, archivedCount);
            stream.SetLength(liveOffset);
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(liveSection);
            return true;
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.transcriptRewriteFailed", new { npc = npcName, error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
            InvalidateLiveSectionOffset(filePath);
            return false;
        }
    }

    /// <summary>
    /// The cached offset is only trusted when the bytes immediately before it are exactly the
    /// archive-end marker line plus the blank line we wrote last time. Anything else (external
    /// edit, different newline convention, truncated file) fails validation and forces a full
    /// rebuild, which can never lose data.
    /// </summary>
    private static bool LiveSectionOffsetIsValid(FileStream stream, long liveOffset)
    {
        byte[] expected = Encoding.UTF8.GetBytes(ArchiveEndMarker + Environment.NewLine + Environment.NewLine);
        if (liveOffset < expected.Length || stream.Length < liveOffset)
        {
            return false;
        }

        stream.Seek(liveOffset - expected.Length, SeekOrigin.Begin);
        byte[] actual = new byte[expected.Length];
        int read = 0;
        while (read < actual.Length)
        {
            int chunk = stream.Read(actual, read, actual.Length - read);
            if (chunk <= 0)
            {
                return false;
            }

            read += chunk;
        }

        return actual.AsSpan().SequenceEqual(expected);
    }

    private static void InvalidateLiveSectionOffset(string filePath)
    {
        lock (LiveSectionGate)
        {
            LiveSectionOffsets.Remove(filePath);
        }
    }

    /// <summary>
    /// The rebuilt-every-export tail: the export stamp plus every conversation still in memory.
    /// The stamp lives here rather than in the header so the bytes before the live section stay
    /// stable and the tail-only rewrite can work.
    /// </summary>
    private static string BuildLiveSection(string npcName, List<TranscriptEntry> transcriptEntries, int archivedCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine(T("transcriptExportTime", "- Export time: Day {{day}} {{time}}", new { day = Game1.Date.TotalDays, time = Game1.timeOfDay.ToString("0000") }));
        builder.AppendLine();
        for (int i = 0; i < transcriptEntries.Count; i++)
        {
            AppendConversationSection(builder, transcriptEntries[i], archivedCount + i + 1, npcName);
        }

        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder, string npcName)
    {
        builder.AppendLine($"# {npcName}");
        builder.AppendLine();
        builder.AppendLine(T("transcriptSave", "- Save: {{save}}", new { save = Constants.SaveFolderName }));
        builder.AppendLine();
    }

    private static void AppendConversationSection(StringBuilder builder, TranscriptEntry entry, int index, string npcName)
    {
        var time = entry.Time;
        builder.AppendLine(T(
            "transcriptConversationHeading",
            "## Conversation {{index}}: Year {{year}}, {{season}} {{day}} at {{time}}",
            new
            {
                index,
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

    internal static string BuildArchiveMarker(int count, int lastDay, int lastTime)
    {
        return $"{ArchiveMarkerPrefix}count={count} lastDay={lastDay} lastTime={lastTime} -->";
    }

    internal static bool TryParseArchiveMarker(string line, out int count, out int lastDay, out int lastTime)
    {
        count = 0;
        lastDay = 0;
        lastTime = 0;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.Trim();
        if (!trimmed.StartsWith(ArchiveMarkerPrefix, StringComparison.Ordinal)
            || !trimmed.EndsWith("-->", StringComparison.Ordinal))
        {
            return false;
        }

        string body = trimmed[ArchiveMarkerPrefix.Length..^3];
        foreach (string token in body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = token.IndexOf('=');
            if (separator <= 0 || !int.TryParse(token[(separator + 1)..], out int value))
            {
                return false;
            }

            switch (token[..separator])
            {
                case "count":
                    count = value;
                    break;
                case "lastDay":
                    lastDay = value;
                    break;
                case "lastTime":
                    lastTime = value;
                    break;
            }
        }

        return true;
    }

    internal static bool IsAfterWatermark(StardewTime time, int lastDay, int lastTime)
    {
        int day = time.ToAbsoluteDays();
        return day > lastDay || (day == lastDay && time.timeOfDay > lastTime);
    }

    internal static ArchiveBlock ExtractArchiveBlock(string[] lines)
    {
        var block = new ArchiveBlock();
        int startIndex = -1;
        int endIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (startIndex < 0)
            {
                if (TryParseArchiveMarker(lines[i], out int count, out int lastDay, out int lastTime))
                {
                    block.Count = count;
                    block.LastDay = lastDay;
                    block.LastTime = lastTime;
                    startIndex = i;
                }
            }
            else if (string.Equals(lines[i].Trim(), ArchiveEndMarker, StringComparison.Ordinal))
            {
                endIndex = i;
                break;
            }
        }

        if (startIndex < 0 || endIndex < 0)
        {
            // Legacy file without markers (or a truncated one): treat as having no archive. The
            // caller rebuilds the live section from history, so no conversations are lost.
            return new ArchiveBlock();
        }

        block.HasMarkers = true;
        block.Content = string.Join(Environment.NewLine, lines[(startIndex + 1)..endIndex]);
        block.LiveContent = endIndex + 1 < lines.Length
            ? string.Join(Environment.NewLine, lines[(endIndex + 1)..])
            : string.Empty;
        return block;
    }

    private static ArchiveBlock ReadArchiveBlock(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new ArchiveBlock();
            }

            return ExtractArchiveBlock(File.ReadAllLines(filePath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                I18n.Get("log.dialogue.transcriptArchiveReadFailed", new { path = filePath, error = ex.Message }),
                StardewModdingAPI.LogLevel.Trace);
            return new ArchiveBlock();
        }
    }

    internal sealed class ArchiveBlock
    {
        public int Count { get; set; }
        public int LastDay { get; set; }
        public int LastTime { get; set; }
        public string Content { get; set; } = string.Empty;
        public string LiveContent { get; set; } = string.Empty;
        public bool HasMarkers { get; set; }
    }

    private static IEnumerable<TranscriptEntry> BuildTranscriptEntries(StardewEventHistory history)
    {
        var conversationEntries = history.ConversationHistory
            .Select(entry => new TranscriptEntry(
                entry.Item1,
                BuildTranscriptLines(entry.Item2.ConversationElements
                    .Select(line => new TranscriptLine(line.IsPlayerLine, line.Text)))))
            .Where(entry => entry.Lines.Count > 0)
            .OrderBy(entry => entry.Time)
            .ToList();

        if (conversationEntries.Count > 0)
        {
            return RemoveRedundantPrefixEntries(conversationEntries);
        }

        return history.DialogueHistory
            .Select(entry => new TranscriptEntry(
                entry.Item1,
                BuildTranscriptLines(entry.Item2.Dialogues
                    .Select(line => new TranscriptLine(false, line.Text)))))
            .Where(entry => entry.Lines.Count > 0)
            .ToList();
    }

    private static List<TranscriptLine> BuildTranscriptLines(IEnumerable<TranscriptLine> lines)
    {
        var result = new List<TranscriptLine>();

        var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
            lines,
            line => line.Text,
            line => line.IsPlayerLine);
        foreach (var line in cleaned)
        {
            string text = SanitizeTranscriptText(line.Text);
            if (!IsExportableTranscriptText(text))
            {
                continue;
            }

            var normalized = new TranscriptLine(line.IsPlayerLine, text);
            if (result.Count > 0 && TranscriptLineEquals(result[result.Count - 1], normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static List<TranscriptEntry> RemoveRedundantPrefixEntries(List<TranscriptEntry> entries)
    {
        var result = new List<TranscriptEntry>();

        for (int i = 0; i < entries.Count; i++)
        {
            bool isRedundant = false;
            for (int j = i + 1; j < entries.Count; j++)
            {
                if (IsSameGameDay(entries[i].Time, entries[j].Time)
                    && IsStrictPrefix(entries[i].Lines, entries[j].Lines))
                {
                    isRedundant = true;
                    break;
                }
            }

            if (!isRedundant)
            {
                result.Add(entries[i]);
            }
        }

        return result;
    }

    private static bool IsStrictPrefix(List<TranscriptLine> prefix, List<TranscriptLine> full)
    {
        if (prefix.Count == 0 || prefix.Count >= full.Count)
        {
            return false;
        }

        for (int i = 0; i < prefix.Count; i++)
        {
            if (!TranscriptLineEquals(prefix[i], full[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TranscriptLineEquals(TranscriptLine left, TranscriptLine right)
    {
        return left.IsPlayerLine == right.IsPlayerLine
            && string.Equals(left.Text, right.Text, StringComparison.Ordinal);
    }

    private static bool IsSameGameDay(StardewTime left, StardewTime right)
    {
        return left.year == right.year
            && left.season == right.season
            && left.dayOfMonth == right.dayOfMonth;
    }

    private static bool IsExportableTranscriptText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Contains("正在思考", StringComparison.Ordinal)
            || text.Contains("is thinking", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var placeholders = new[]
        {
            T("uiStartConversation", "What do you want to say?"),
            T("uiYourResponse", "Your response"),
            T("uiTypeYourResponse", "Type your response"),
            T("outputRespond", "Respond"),
            "你想说什么？",
            "你的回复",
            "自由输入",
            "回应"
        };

        return !placeholders.Any(placeholder =>
            !string.IsNullOrWhiteSpace(placeholder)
            && string.Equals(text, placeholder, StringComparison.OrdinalIgnoreCase));
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
