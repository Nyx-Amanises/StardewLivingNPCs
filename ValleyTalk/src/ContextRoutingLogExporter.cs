using System;
using System.IO;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewValley;

namespace ValleyTalk;

internal static class ContextRoutingLogExporter
{
    private const string RootFolderName = "context_routing_logs";

    public static void Append(
        string npcName,
        DialogueContext context,
        string outcome,
        long routeMilliseconds,
        int timeoutSeconds,
        string parseDetail,
        string planLabel,
        string routerPrompt,
        string rawOutput,
        string errorMessage)
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
                BuildEntry(npcName, context, outcome, routeMilliseconds, timeoutSeconds, parseDetail, planLabel, routerPrompt, rawOutput, errorMessage),
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor?.Log($"Failed to export context routing log for {npcName}: {ex.Message}", LogLevel.Warn);
        }
    }

    private static string BuildEntry(
        string npcName,
        DialogueContext context,
        string outcome,
        long routeMilliseconds,
        int timeoutSeconds,
        string parseDetail,
        string planLabel,
        string routerPrompt,
        string rawOutput,
        string errorMessage)
    {
        var builder = new StringBuilder();
        var time = Game1.Date;
        builder.AppendLine($"## {npcName} - Year {time.Year}, {FormatSeason(time.Season)} {time.DayOfMonth} {Game1.timeOfDay:0000}");
        builder.AppendLine();
        builder.AppendLine($"- Provider/model: `{ModEntry.Config.Provider}/{ModEntry.Config.ModelName}`");
        builder.AppendLine($"- Outcome: `{outcome}`");
        builder.AppendLine($"- Route time: `{routeMilliseconds}ms`");
        builder.AppendLine($"- Timeout: `{timeoutSeconds}s`");
        builder.AppendLine($"- Parse detail: `{(string.IsNullOrWhiteSpace(parseDetail) ? "none" : parseDetail)}`");
        builder.AppendLine($"- Final plan: `{(string.IsNullOrWhiteSpace(planLabel) ? "none" : planLabel)}`");
        builder.AppendLine($"- Location/time: `{context?.Location ?? "unknown"}` / `{context?.TimeOfDay ?? "unknown"}`");
        builder.AppendLine($"- Gift response: `{context?.Accept != null}`");
        builder.AppendLine($"- LivingNPC context present: `{!string.IsNullOrWhiteSpace(context?.LivingNpcExtraPrompt)}`");
        builder.AppendLine();

        builder.AppendLine("### Router Prompt");
        builder.AppendLine();
        AppendFence(builder, routerPrompt, "text");
        builder.AppendLine();

        builder.AppendLine("### Raw Router Output");
        builder.AppendLine();
        AppendFence(builder, rawOutput, "text");

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            builder.AppendLine();
            builder.AppendLine("### Error Message");
            builder.AppendLine();
            AppendFence(builder, errorMessage, "text");
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
        var builder = new StringBuilder((value ?? string.Empty).Length);
        foreach (char ch in value ?? string.Empty)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.Length == 0 ? "unknown-npc" : builder.ToString();
    }
}