using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;

namespace ValleyTalk;

internal static class AiResponseLogExporter
{
    private const string RootFolderName = "ai_response_logs";

    public static void Append(
        string npcName,
        DialogueContext context,
        LlmResponse response,
        ConversationAnalysis analysis,
        IReadOnlyList<string> parsedLines,
        int attempt,
        int promptCharacters,
        string outcome)
    {
        if (ModEntry.Config?.ExportAiResponseLogs != true || ModEntry.SHelper == null)
        {
            return;
        }

        try
        {
            string saveFolder = string.IsNullOrWhiteSpace(Constants.SaveFolderName)
                ? "unknown-save"
                : Constants.SaveFolderName;
            string directory = Path.Combine(ModEntry.SHelper.DirectoryPath, RootFolderName, saveFolder);
            Directory.CreateDirectory(directory);

            string filePath = Path.Combine(directory, $"{GetSafeFileName(npcName)}.md");
            File.AppendAllText(
                filePath,
                BuildEntry(npcName, context, response, analysis, parsedLines, attempt, promptCharacters, outcome),
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor?.Log($"Failed to export AI response log for {npcName}: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
        }
    }

    private static string BuildEntry(
        string npcName,
        DialogueContext context,
        LlmResponse response,
        ConversationAnalysis analysis,
        IReadOnlyList<string> parsedLines,
        int attempt,
        int promptCharacters,
        string outcome)
    {
        var builder = new StringBuilder();
        var time = Game1.Date;
        builder.AppendLine($"## {npcName} - Year {time.Year}, {FormatSeason(time.Season)} {time.DayOfMonth} {Game1.timeOfDay:0000} - attempt {attempt}");
        builder.AppendLine();
        builder.AppendLine($"- Provider/model: `{ModEntry.Config.Provider}/{ModEntry.Config.ModelName}`");
        builder.AppendLine($"- Outcome: `{outcome}`");
        builder.AppendLine($"- Success: `{response?.IsSuccess == true}`");
        builder.AppendLine($"- Prompt chars: `{promptCharacters}`");
        builder.AppendLine($"- Response chars: `{(response?.Text ?? string.Empty).Length}`");
        builder.AppendLine($"- Location/time: `{context?.Location ?? "unknown"}` / `{context?.TimeOfDay ?? "unknown"}`");
        builder.AppendLine();

        builder.AppendLine("### Parsed Dialogue Lines");
        builder.AppendLine();
        if (parsedLines != null && parsedLines.Count > 0)
        {
            for (int i = 0; i < parsedLines.Count; i++)
            {
                builder.AppendLine($"{i + 1}. {parsedLines[i]}");
            }
        }
        else
        {
            builder.AppendLine("<none>");
        }
        builder.AppendLine();

        builder.AppendLine("### Parsed Metadata");
        builder.AppendLine();
        AppendFence(builder, PrettyJson(analysis?.ToJson() ?? ConversationAnalysis.Empty.ToJson()), "json");
        builder.AppendLine();

        builder.AppendLine("### Raw Model Output");
        builder.AppendLine();
        AppendFence(builder, response?.Text ?? string.Empty, "text");
        if (!string.IsNullOrWhiteSpace(response?.ErrorMessage))
        {
            builder.AppendLine();
            builder.AppendLine("### Error Message");
            builder.AppendLine();
            AppendFence(builder, response.ErrorMessage, "text");
        }
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendFence(StringBuilder builder, string text, string language)
    {
        builder.AppendLine($"~~~{language}");
        builder.AppendLine(string.IsNullOrWhiteSpace(text) ? "<empty>" : text.TrimEnd());
        builder.AppendLine("~~~");
    }

    private static string PrettyJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            return JToken.Parse(json).ToString(Formatting.Indented);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatSeason(StardewValley.Season season)
    {
        return season switch
        {
            StardewValley.Season.Spring => "Spring",
            StardewValley.Season.Summer => "Summer",
            StardewValley.Season.Fall => "Fall",
            StardewValley.Season.Winter => "Winter",
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
