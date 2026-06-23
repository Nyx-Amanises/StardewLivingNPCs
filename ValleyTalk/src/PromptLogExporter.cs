using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StardewModdingAPI;
using StardewValley;

namespace ValleyTalk;

internal static class PromptLogExporter
{
    private const string RootFolderName = "prompt_logs";

    public static void Append(
        string npcName,
        DialogueContext context,
        string systemPrompt,
        string gameConstantContext,
        string npcConstantContext,
        string corePrompt,
        string instructions,
        string command,
        string responseStart,
        LlmResponse response,
        IReadOnlyList<string> parsedLines,
        int attempt,
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
                BuildEntry(
                    npcName,
                    context,
                    systemPrompt,
                    gameConstantContext,
                    npcConstantContext,
                    corePrompt,
                    instructions,
                    command,
                    responseStart,
                    response,
                    parsedLines,
                    attempt,
                    outcome),
                Encoding.UTF8);
        }
        catch (Exception ex)
        {
            ModEntry.SMonitor?.Log($"Failed to export prompt log for {npcName}: {ex.Message}", StardewModdingAPI.LogLevel.Warn);
        }
    }

    private static string BuildEntry(
        string npcName,
        DialogueContext context,
        string systemPrompt,
        string gameConstantContext,
        string npcConstantContext,
        string corePrompt,
        string instructions,
        string command,
        string responseStart,
        LlmResponse response,
        IReadOnlyList<string> parsedLines,
        int attempt,
        string outcome)
    {
        string generatedPrompt = string.Concat(corePrompt, instructions, command);
        string userPrompt = string.Concat(gameConstantContext, npcConstantContext, generatedPrompt);
        string combinedPrompt = string.Concat(systemPrompt, gameConstantContext, npcConstantContext, generatedPrompt, responseStart);
        var builder = new StringBuilder();
        var time = Game1.Date;

        builder.AppendLine($"## {npcName} - Year {time.Year}, {FormatSeason(time.Season)} {time.DayOfMonth} {Game1.timeOfDay:0000} - attempt {attempt}");
        builder.AppendLine();
        builder.AppendLine($"- Provider/model: `{ModEntry.Config.Provider}/{ModEntry.Config.ModelName}`");
        builder.AppendLine($"- Outcome: `{outcome}`");
        builder.AppendLine($"- Success: `{response?.IsSuccess == true}`");
        builder.AppendLine($"- Prompt chars: `{combinedPrompt.Length}`");
        builder.AppendLine($"- Response chars: `{(response?.Text ?? string.Empty).Length}`");
        builder.AppendLine($"- Location/time: `{context?.Location ?? "unknown"}` / `{context?.TimeOfDay ?? "unknown"}`");
        builder.AppendLine();

        builder.AppendLine("### LLM Input Parts");
        builder.AppendLine();
        AppendPromptSection(builder, "System Prompt", systemPrompt);
        AppendPromptSection(builder, "Game Constant Context", gameConstantContext);
        AppendPromptSection(builder, "NPC Constant Context", npcConstantContext);
        AppendPromptSection(builder, "Core Prompt", corePrompt);
        AppendPromptSection(builder, "Instructions", instructions);
        AppendPromptSection(builder, "Command", command);
        AppendPromptSection(builder, "Response Start", responseStart);

        builder.AppendLine("### Chat Messages");
        builder.AppendLine();
        builder.AppendLine("These are the system and user messages used by OpenAI-compatible chat providers.");
        builder.AppendLine();
        AppendPromptSection(builder, "System Message", systemPrompt);
        AppendPromptSection(builder, "User Message", userPrompt);

        builder.AppendLine("### Combined Prompt");
        builder.AppendLine();
        builder.AppendLine("This is the same concatenation used for token estimation. Providers may still send the system prompt and user prompt as separate chat messages.");
        builder.AppendLine();
        AppendFence(builder, combinedPrompt, "text");
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

    private static void AppendPromptSection(StringBuilder builder, string title, string text)
    {
        builder.AppendLine($"#### {title}");
        builder.AppendLine();
        AppendFence(builder, text, "text");
        builder.AppendLine();
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
