using System;
using System.IO;
using System.Text;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
namespace LivingNPCs.Dialogue.Diagnostics;

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
        if (DialogueServices.Config?.ExportAiResponseLogs != true || DialogueServices.Helper == null)
        {
            return;
        }

        try
        {
            string saveFolder = string.IsNullOrWhiteSpace(Constants.SaveFolderName)
                ? "unknown-save"
                : Constants.SaveFolderName;
            string directory = Path.Combine(DialogueServices.Helper.DirectoryPath, RootFolderName, saveFolder);
            string filePath = Path.Combine(directory, $"{GetSafeFileName(npcName)}.md");
            DiagnosticMarkdownLogWriter.Append(
                filePath,
                recordedAt => BuildEntry(npcName, context, outcome, routeMilliseconds, timeoutSeconds, parseDetail, planLabel, routerPrompt, rawOutput, errorMessage, recordedAt));
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.exportFailed",
                    new { what = "context routing log", npc = npcName, error = ex.Message },
                    $"Failed to export context routing log for {npcName}: {ex.Message}"),
                LogLevel.Warn);
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
        string errorMessage,
        DateTimeOffset recordedAt)
    {
        var builder = new StringBuilder();
        var time = Game1.Date;
        builder.AppendLine($"## {npcName} - Year {time.Year}, {FormatSeason(time.Season)} {time.DayOfMonth} {Game1.timeOfDay:0000}");
        builder.AppendLine();
        builder.AppendLine($"- Wall-clock time: `{DiagnosticMarkdownLogWriter.FormatWallClockTimestamp(recordedAt)}`");
        builder.AppendLine($"- Provider/model: `{DialogueServices.Config.Provider}/{DialogueServices.Config.ModelName}`");
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
        // Fence length adapts to the content so raw LLM output cannot break out (see the writer).
        DiagnosticMarkdownLogWriter.AppendFencedBlock(builder, text, language);
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
        return DiagnosticMarkdownLogWriter.GetSafeFileName(value);
    }
}
