using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ValleyTalk;

internal static class LivingNpcActionDecisionPass
{
    private const int MaxCompactContextCharacters = 7000;

    public static async Task<ConversationAnalysis> TrySupplementAsync(
        Character character,
        DialogueContext context,
        ConversationAnalysis analysis,
        IReadOnlyList<string> parsedLines)
    {
        if (ModEntry.Config?.EnableLivingNpcActionDecisionPass != true
            || character == null
            || context == null
            || analysis == null
            || analysis.HasWorldActionOrHelpMetadata
            || string.IsNullOrWhiteSpace(context.LivingNpcExtraPrompt)
            || parsedLines == null
            || parsedLines.Count == 0)
        {
            return analysis ?? ConversationAnalysis.Empty;
        }

        string visibleNpcReply = parsedLines[0] ?? string.Empty;
        string playerText = context.ChatHistory?.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        if (!LooksRelevant(playerText, visibleNpcReply, context.LivingNpcExtraPrompt))
        {
            return analysis;
        }

        string compactPrompt = BuildPrompt(character, context, playerText, visibleNpcReply);
        int timeoutSeconds = Math.Clamp(ModEntry.Config.LivingNpcActionDecisionTimeoutSeconds, 2, Math.Max(2, ModEntry.Config.QueryTimeout));
        LlmResponse response;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var task = Llm.Instance.RunInference(
                "You are a strict classifier for Stardew Valley mod action metadata. Return only compact JSON metadata; never write dialogue.",
                string.Empty,
                $"NPC: {character.Name} ({character.StardewNpc?.displayName ?? character.Name})",
                compactPrompt,
                "!LIVINGNPCS_META ",
                n_predict: 512,
                allowRetry: false);
            response = await task.WaitAsync(cts.Token);
        }
        catch (Exception ex)
        {
            if (ModEntry.Config.Debug)
            {
                ModEntry.SMonitor.Log($"LivingNPCs action decision pass failed for {character.Name}: {ex.Message}", StardewModdingAPI.LogLevel.Debug);
            }

            return analysis;
        }

        TokenUsage usage = response.Usage.HasAnyTokens
            ? response.Usage
            : TokenUsage.Estimate(compactPrompt, response.Text ?? response.ErrorMessage ?? string.Empty);
        TokenUsageTracker.Instance.Record(
            character.Name,
            usage,
            ModEntry.Config.Provider,
            ModEntry.Config.ModelName,
            response.IsSuccess ? "action-decision" : "action-decision-failed");

        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
        {
            return analysis;
        }

        string parseText = response.Text.Contains("!LIVINGNPCS_META", StringComparison.Ordinal)
            ? response.Text
            : $"!LIVINGNPCS_META {response.Text}";
        ConversationAnalysis supplemental = ConversationAnalysis.Parse(parseText);
        bool changed = analysis.MergeSupplementalActionMetadata(supplemental);
        if (changed && ModEntry.Config.Debug)
        {
            ModEntry.SMonitor.Log(
                $"LivingNPCs action decision pass supplemented metadata for {character.Name}: actions={analysis.Actions.Count}, helpRequests={analysis.HelpRequests.Count}, helpUpdates={analysis.HelpRequestUpdates.Count}.",
                StardewModdingAPI.LogLevel.Debug);
        }

        return analysis;
    }

    private static string BuildPrompt(Character character, DialogueContext context, string playerText, string visibleNpcReply)
    {
        string compactContext = BuildCompactLivingNpcContext(context.LivingNpcExtraPrompt);
        var prompt = new StringBuilder();
        prompt.AppendLine("Decide whether the visible NPC reply clearly commits to a LivingNPCs world action or help-request metadata that the main dialogue JSON omitted.");
        prompt.AppendLine("Use the compact context as constraints, not as text to quote. Do not invent actions, items, destinations, or rewards.");
        prompt.AppendLine();
        prompt.AppendLine("Current facts:");
        prompt.AppendLine($"- NPC: {character.StardewNpc?.displayName ?? character.Name} ({character.Name}).");
        prompt.AppendLine($"- Location: {context.Location ?? "unknown"}; time: {context.TimeOfDay ?? "unknown"}; hearts: {(context.Hearts?.ToString() ?? "unknown")}.");
        if (!string.IsNullOrWhiteSpace(context.CurrentActivity))
        {
            prompt.AppendLine($"- Current visible activity: {context.CurrentActivity}.");
        }
        if (!string.IsNullOrWhiteSpace(context.NextScheduleLocation))
        {
            prompt.AppendLine($"- Next schedule destination: {context.NextScheduleLocation}; minutes until next schedule: {(context.MinutesUntilNextSchedule?.ToString() ?? "unknown")}.");
        }
        prompt.AppendLine();
        prompt.AppendLine("Compact LivingNPCs context:");
        prompt.AppendLine(compactContext);
        prompt.AppendLine();
        prompt.AppendLine("Conversation turn:");
        prompt.AppendLine($"- Farmer: {CleanForPrompt(playerText)}");
        prompt.AppendLine($"- NPC visible reply: {CleanForPrompt(visibleNpcReply)}");
        prompt.AppendLine();
        prompt.AppendLine("Return exactly one line beginning with !LIVINGNPCS_META followed by compact JSON using this schema:");
        prompt.AppendLine("{\"actions\":[{\"type\":\"give_small_gift|give_meaningful_gift|give_money|companion_outing|festival_interaction|assist_quest\",\"amount\":0,\"durationMinutes\":0,\"delayMinutes\":0,\"targetLocation\":\"\",\"travelConsent\":\"accepted_now|accepted_later|declined|tentative|none\",\"questHint\":\"\",\"itemId\":\"\",\"itemLabel\":\"\",\"reason\":\"short evidence from visible reply\"}],\"helpRequests\":[{\"type\":\"item_request\",\"summary\":\"short concrete ask\",\"requiresAcceptance\":true,\"requestedItemId\":\"\",\"requestedItemLabel\":\"\",\"questionTopic\":\"\",\"dueInDays\":1,\"reason\":\"short evidence\",\"followUpPotential\":\"none|deeper_relationship\"}],\"helpRequestUpdates\":[{\"summary\":\"matching existing request\",\"status\":\"accepted|declined|advanced|fulfilled\",\"resolution\":\"short result\"}]}");
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- If there is no clear visible commitment, return empty arrays for all three fields.");
        prompt.AppendLine("- For travel, include companion_outing only when the NPC visibly agrees to go now; set travelConsent=accepted_now and targetLocation to a supported destination from context or visible text. Later/maybe/refusal means no action.");
        prompt.AppendLine("- For brief escort/show-the-way use durationMinutes 20; for a real stay together use 60 or more.");
        prompt.AppendLine("- For gifts, include a gift action only when the NPC visibly gives something now. Do not convert later mail/return gift promises into immediate gift actions.");
        prompt.AppendLine("- For helpRequests, only item_request is allowed, and requestedItemId must come from the currently reasonable item list in context. If no listed item fits, return no help request.");
        prompt.AppendLine("- For helpRequestUpdates, use only when the farmer clearly accepts, declines, advances, or fulfills an existing request.");
        prompt.AppendLine("- Output no markdown, no explanation, and no visible dialogue.");
        return prompt.ToString();
    }

    private static string BuildCompactLivingNpcContext(string fullContext)
    {
        if (string.IsNullOrWhiteSpace(fullContext))
        {
            return "<none>";
        }

        return LivingNpcContextCompressor.BuildBriefContext(
            fullContext,
            maxLines: 100,
            fallbackLines: 30,
            maxCharacters: MaxCompactContextCharacters);
    }


    private static bool LooksRelevant(string playerText, string visibleNpcReply, string context)
    {
        string combined = $"{playerText}\n{visibleNpcReply}\n{context}";
        return ContainsAny(
            combined,
            "companion_outing",
            "helpRequests",
            "helpRequestUpdates",
            "Gift Opportunity",
            "Help Request Opportunity",
            "Help-request fit",
            "带我",
            "带你",
            "一起去",
            "陪我",
            "陪你",
            "去海",
            "去农场",
            "走吧",
            "帮忙",
            "帮我",
            "需要",
            "找一个",
            "带一个",
            "给你",
            "送你",
            "小礼物",
            "回礼",
            "谢礼",
            "钱",
            "任务",
            "with me",
            "together",
            "come with",
            "show me",
            "help",
            "bring me",
            "find me",
            "gift",
            "return gift",
            "quest");
    }

    private static string CleanForPrompt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        string cleaned = Regex.Replace(value.Replace("\r", " ").Replace("\n", " "), "\\s+", " ").Trim();
        return cleaned.Length <= 1200 ? cleaned : cleaned[..1200] + "...";
    }

    private static bool ContainsAny(string text, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}