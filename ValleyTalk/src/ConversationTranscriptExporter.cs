using System;
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
        builder.AppendLine($"- 存档：{Constants.SaveFolderName}");
        builder.AppendLine($"- 导出时间：第 {Game1.Date.TotalDays} 天 {Game1.timeOfDay:0000}");
        builder.AppendLine();

        var conversations = history.ConversationHistory
            .OrderBy(entry => entry.Item1)
            .ToList();

        if (conversations.Count == 0)
        {
            builder.AppendLine("暂无 AI 聊天记录。");
            return builder.ToString();
        }

        for (int i = 0; i < conversations.Count; i++)
        {
            var entry = conversations[i];
            var time = entry.Item1;
            builder.AppendLine($"## 对话 {i + 1}：第 {time.year} 年 {FormatSeason(time.season)} {time.dayOfMonth} 日 {time.timeOfDay:0000}");
            builder.AppendLine();

            foreach (var line in entry.Item2.ConversationElements)
            {
                string speaker = line.IsPlayerLine ? "玩家" : npcName;
                builder.AppendLine($"- **{speaker}：** {SanitizeTranscriptText(line.Text)}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
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

    private static string FormatSeason(StardewValley.Season season)
    {
        return season switch
        {
            StardewValley.Season.Spring => "春",
            StardewValley.Season.Summer => "夏",
            StardewValley.Season.Fall => "秋",
            StardewValley.Season.Winter => "冬",
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
}
