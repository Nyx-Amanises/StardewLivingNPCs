using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ValleyTalk;

internal static class LivingNpcActionDecisionPass
{
    private const int MaxCompactContextCharacters = 7000;

    public static async Task<LivingNpcActionDecisionResult> TrySupplementAsync(
        Character character,
        DialogueContext context,
        ConversationAnalysis analysis,
        IReadOnlyList<string> parsedLines)
    {
        analysis ??= ConversationAnalysis.Empty;

        if (ModEntry.Config?.EnableLivingNpcActionDecisionPass != true)
        {
            return LivingNpcActionDecisionResult.Skipped(analysis, "disabled");
        }

        if (character == null || context == null)
        {
            return LivingNpcActionDecisionResult.Skipped(analysis, "missing character or context");
        }

        if (analysis.HasWorldActionOrHelpMetadata)
        {
            return LivingNpcActionDecisionResult.Skipped(analysis, "main response already had world-action/help metadata");
        }

        if (string.IsNullOrWhiteSpace(context.LivingNpcExtraPrompt))
        {
            return LivingNpcActionDecisionResult.Skipped(analysis, "missing LivingNPCs context");
        }

        if (parsedLines == null || parsedLines.Count == 0)
        {
            return LivingNpcActionDecisionResult.Skipped(analysis, "no parsed visible NPC reply");
        }

        string visibleNpcReply = parsedLines[0] ?? string.Empty;
        string playerText = context.ChatHistory?.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        if (!LooksRelevant(playerText, visibleNpcReply, context.LivingNpcExtraPrompt))
        {
            return LivingNpcActionDecisionResult.Skipped(analysis, "no action-relevant cue detected");
        }

        string compactPrompt = BuildPrompt(character, context, playerText, visibleNpcReply);
        int timeoutSeconds = Math.Clamp(ModEntry.Config.LivingNpcActionDecisionTimeoutSeconds, 2, Math.Max(2, ModEntry.Config.QueryTimeout));
        var diagnostics = new LivingNpcActionDecisionDiagnostics
        {
            WasRun = true,
            Outcome = "started",
            Prompt = compactPrompt,
            PromptCharacters = compactPrompt.Length,
            TimeoutSeconds = timeoutSeconds
        };

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
            diagnostics.Outcome = "request-failed";
            diagnostics.ErrorMessage = ex.Message;
            LogDiagnostics(character, diagnostics);
            return new LivingNpcActionDecisionResult(analysis, diagnostics);
        }

        diagnostics.ResponseSuccess = response.IsSuccess;
        diagnostics.RawResponse = response.Text ?? string.Empty;
        diagnostics.ResponseCharacters = diagnostics.RawResponse.Length;
        diagnostics.ErrorMessage = response.ErrorMessage ?? string.Empty;

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
            diagnostics.Outcome = response.IsSuccess ? "empty-response" : "model-failed";
            LogDiagnostics(character, diagnostics);
            return new LivingNpcActionDecisionResult(analysis, diagnostics);
        }

        string parseText = response.Text.Contains("!LIVINGNPCS_META", StringComparison.Ordinal)
            ? response.Text
            : $"!LIVINGNPCS_META {response.Text}";
        diagnostics.ParseText = parseText;

        ConversationAnalysis supplemental = ConversationAnalysis.Parse(parseText);
        bool addedTravelDecisionAction = TryAddActionFromTravelDecision(
            supplemental,
            response.Text,
            playerText,
            visibleNpcReply,
            out string travelDecisionDetail);
        diagnostics.TravelDecisionDetail = travelDecisionDetail;
        diagnostics.SupplementalJson = supplemental.ToJson();

        bool changed = analysis.MergeSupplementalActionMetadata(supplemental);
        diagnostics.Merged = changed;
        diagnostics.MergedJson = analysis.ToJson();
        diagnostics.Outcome = changed
            ? addedTravelDecisionAction ? "merged-travel-decision" : "merged-actions"
            : supplemental.HasWorldActionOrHelpMetadata ? "parsed-but-filtered" : "parsed-empty";

        LogDiagnostics(character, diagnostics);
        return new LivingNpcActionDecisionResult(analysis, diagnostics);
    }

    private static string BuildPrompt(Character character, DialogueContext context, string playerText, string visibleNpcReply)
    {
        string compactContext = BuildCompactLivingNpcContext(context.LivingNpcExtraPrompt);
        var prompt = new StringBuilder();
        prompt.AppendLine("Decide whether the visible NPC reply clearly commits to LivingNPCs world-action/help metadata that the main dialogue JSON omitted.");
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
        prompt.AppendLine("{\"travelDecision\":{\"isTravelReply\":false,\"consent\":\"accepted_now|accepted_later|declined|tentative|none\",\"targetLocation\":\"Farm|Town|Mountain|Beach|Forest|BusStop|Saloon|SeedShop|ArchaeologyHouse|Hospital\",\"delayMinutes\":0,\"durationMinutes\":0,\"reason\":\"short evidence from visible reply\"},\"actions\":[{\"type\":\"give_small_gift|give_meaningful_gift|give_money|companion_outing|festival_interaction|assist_quest\",\"amount\":0,\"durationMinutes\":0,\"delayMinutes\":0,\"targetLocation\":\"\",\"travelConsent\":\"accepted_now|accepted_later|declined|tentative|none\",\"questHint\":\"\",\"itemId\":\"\",\"itemLabel\":\"\",\"reason\":\"short evidence from visible reply\"}],\"helpRequests\":[{\"type\":\"item_request\",\"summary\":\"short concrete ask\",\"requiresAcceptance\":true,\"requestedItemId\":\"\",\"requestedItemLabel\":\"\",\"questionTopic\":\"\",\"dueInDays\":1,\"reason\":\"short evidence\",\"followUpPotential\":\"none|deeper_relationship\"}],\"helpRequestUpdates\":[{\"summary\":\"matching existing request\",\"status\":\"accepted|declined|advanced|fulfilled\",\"resolution\":\"short result\"}]}");
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Always fill travelDecision, even when actions is empty. This is a classifier, not dialogue.");
        prompt.AppendLine("- For travelDecision, accepted_now means the NPC agrees to go now. Short preparation still counts as accepted_now: wait five minutes, let me change clothes, get my coat, grab an umbrella, I will be right out.");
        prompt.AppendLine("- For travel, include companion_outing in actions when consent=accepted_now and targetLocation is supported. Use the farmer's invitation to infer targetLocation if the NPC reply says yes/now but omits the destination.");
        prompt.AppendLine("- Later/maybe/refusal means no action and travelDecision consent accepted_later/tentative/declined.");
        prompt.AppendLine("- For brief escort/show-the-way use durationMinutes 20; for a real stay together use 60 or more. Use delayMinutes 1-10 for short preparation.");
        prompt.AppendLine("- For gifts, include a gift action only when the NPC visibly gives something now. Do not convert later mail/return gift promises into immediate gift actions.");
        prompt.AppendLine("- For helpRequests, only item_request is allowed, and requestedItemId must come from the currently reasonable item list in context. If no listed item fits, return no help request.");
        prompt.AppendLine("- For helpRequestUpdates, use only when the farmer clearly accepts, declines, advances, or fulfills an existing request.");
        prompt.AppendLine("- Output no markdown, no explanation, and no visible dialogue.");
        return prompt.ToString();
    }

    private static bool TryAddActionFromTravelDecision(
        ConversationAnalysis supplemental,
        string responseText,
        string playerText,
        string visibleNpcReply,
        out string detail)
    {
        detail = string.Empty;
        if (supplemental == null)
        {
            detail = "no supplemental analysis";
            return false;
        }

        if (supplemental.Actions.Count > 0)
        {
            detail = "actions already contained a world action";
            return false;
        }

        JObject json;
        try
        {
            string jsonText = ExtractFirstJsonObject(responseText);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                detail = "no JSON object in decision response";
                return false;
            }

            json = JObject.Parse(jsonText);
        }
        catch (Exception ex)
        {
            detail = $"travelDecision parse failed: {ex.Message}";
            return false;
        }

        var decision = json["travelDecision"] as JObject
            ?? json["travel"] as JObject
            ?? json["outingDecision"] as JObject;
        if (decision == null)
        {
            detail = "no travelDecision object";
            return false;
        }

        bool isTravelReply = decision.Value<bool?>("isTravelReply")
            ?? decision.Value<bool?>("isTravel")
            ?? !string.IsNullOrWhiteSpace(decision.Value<string>("consent"));
        string consent = NormalizeTravelConsent(decision.Value<string>("consent") ?? decision.Value<string>("travelConsent"));
        if (!isTravelReply || consent != "accepted_now")
        {
            detail = $"travelDecision not accepted_now (isTravelReply={isTravelReply}, consent={consent})";
            return false;
        }

        string targetLocation = NormalizeTravelTarget(decision.Value<string>("targetLocation"));
        if (string.IsNullOrWhiteSpace(targetLocation))
        {
            targetLocation = InferTravelTargetFromText($"{playerText} {visibleNpcReply}");
        }

        if (string.IsNullOrWhiteSpace(targetLocation))
        {
            detail = "accepted_now travelDecision had no supported targetLocation";
            return false;
        }

        int delayMinutes = Math.Clamp(decision.Value<int?>("delayMinutes") ?? InferPreparationDelayMinutes(visibleNpcReply), 0, 20);
        int durationMinutes = Math.Clamp(decision.Value<int?>("durationMinutes") ?? 60, 0, 600);
        string reason = decision.Value<string>("reason")?.Trim();
        supplemental.Actions.Add(new ConversationWorldActionRequest
        {
            Type = "companion_outing",
            TargetLocation = targetLocation,
            TravelConsent = "accepted_now",
            DelayMinutes = delayMinutes,
            DurationMinutes = durationMinutes,
            Reason = string.IsNullOrWhiteSpace(reason)
                ? "action decision pass judged visible reply as accepting an immediate outing"
                : reason
        });
        detail = $"converted travelDecision to companion_outing target={targetLocation}, delay={delayMinutes}, duration={durationMinutes}";
        return true;
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
            "来农场",
            "来我的农场",
            "农场转转",
            "走吧",
            "出发",
            "等我",
            "等一下",
            "五分钟",
            "5分钟",
            "换件",
            "换衣服",
            "很快就出来",
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
            "wait",
            "five minutes",
            "change clothes",
            "right out",
            "help",
            "bring me",
            "find me",
            "gift",
            "return gift",
            "quest");
    }

    private static void LogDiagnostics(Character character, LivingNpcActionDecisionDiagnostics diagnostics)
    {
        if (ModEntry.Config?.Debug != true || diagnostics == null)
        {
            return;
        }

        ModEntry.SMonitor.Log(
            $"LivingNPCs action decision pass for {character?.Name ?? "unknown"}: outcome={diagnostics.Outcome}, success={diagnostics.ResponseSuccess}, merged={diagnostics.Merged}, detail={diagnostics.TravelDecisionDetail}, error={diagnostics.ErrorMessage}",
            StardewModdingAPI.LogLevel.Debug);
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

    private static string ExtractFirstJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        int start = text.IndexOf('{');
        if (start < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        bool inString = false;
        bool escaping = false;
        for (int i = start; i < text.Length; i++)
        {
            char ch = text[i];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static string NormalizeTravelConsent(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "accepted_now" => "accepted_now",
            "accepted" => "accepted_now",
            "now" => "accepted_now",
            "yes" => "accepted_now",
            "accepted_later" => "accepted_later",
            "deferred" => "accepted_later",
            "later" => "accepted_later",
            "declined" => "declined",
            "rejected" => "declined",
            "no" => "declined",
            "tentative" => "tentative",
            "maybe" => "tentative",
            "none" => "none",
            _ => string.Empty
        };
    }

    private static string NormalizeTravelTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "farm" or "农场" => "Farm",
            "town" or "鹈鹕镇" or "镇上" => "Town",
            "mountain" or "山" or "山上" => "Mountain",
            "beach" or "海滩" or "海边" => "Beach",
            "forest" or "森林" => "Forest",
            "busstop" or "bus stop" or "巴士站" => "BusStop",
            "saloon" or "酒吧" or "沙龙" => "Saloon",
            "seedshop" or "seed shop" or "皮埃尔" or "杂货店" => "SeedShop",
            "archaeologyhouse" or "museum" or "library" or "博物馆" or "图书馆" => "ArchaeologyHouse",
            "hospital" or "clinic" or "医院" or "诊所" => "Hospital",
            _ => normalized
        };
    }

    private static string InferTravelTargetFromText(string text)
    {
        if (ContainsAny(text, "农场", "farm")) return "Farm";
        if (ContainsAny(text, "海边", "海滩", "看海", "大海", "beach", "shore")) return "Beach";
        if (ContainsAny(text, "图书馆", "博物馆", "library", "museum")) return "ArchaeologyHouse";
        if (ContainsAny(text, "酒吧", "沙龙", "saloon")) return "Saloon";
        if (ContainsAny(text, "森林", "forest")) return "Forest";
        if (ContainsAny(text, "山上", "山地", "mountain")) return "Mountain";
        if (ContainsAny(text, "医院", "诊所", "hospital", "clinic")) return "Hospital";
        if (ContainsAny(text, "皮埃尔", "杂货店", "general store", "pierre")) return "SeedShop";
        if (ContainsAny(text, "巴士站", "bus stop")) return "BusStop";
        if (ContainsAny(text, "镇上", "鹈鹕镇", "town")) return "Town";
        return string.Empty;
    }

    private static int InferPreparationDelayMinutes(string text)
    {
        if (ContainsAny(text, "五分钟", "5分钟", "5 分钟", "five minutes")) return 5;
        if (ContainsAny(text, "等我", "稍等", "一会儿", "准备", "换件", "换衣服", "很快就出来", "wait", "change clothes", "right out")) return 10;
        return 0;
    }
}

internal sealed class LivingNpcActionDecisionResult
{
    public LivingNpcActionDecisionResult(ConversationAnalysis analysis, LivingNpcActionDecisionDiagnostics diagnostics)
    {
        this.Analysis = analysis ?? ConversationAnalysis.Empty;
        this.Diagnostics = diagnostics;
    }

    public ConversationAnalysis Analysis { get; }
    public LivingNpcActionDecisionDiagnostics Diagnostics { get; }

    public static LivingNpcActionDecisionResult Skipped(ConversationAnalysis analysis, string reason)
    {
        return new LivingNpcActionDecisionResult(
            analysis ?? ConversationAnalysis.Empty,
            new LivingNpcActionDecisionDiagnostics
            {
                WasRun = false,
                Outcome = "skipped",
                SkipReason = reason
            });
    }
}

internal sealed class LivingNpcActionDecisionDiagnostics
{
    public bool WasRun { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string SkipReason { get; set; } = string.Empty;
    public bool ResponseSuccess { get; set; }
    public bool Merged { get; set; }
    public int PromptCharacters { get; set; }
    public int ResponseCharacters { get; set; }
    public int TimeoutSeconds { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string TravelDecisionDetail { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string RawResponse { get; set; } = string.Empty;
    public string ParseText { get; set; } = string.Empty;
    public string SupplementalJson { get; set; } = string.Empty;
    public string MergedJson { get; set; } = string.Empty;

    public string ToSummaryJson()
    {
        return JsonConvert.SerializeObject(new
        {
            this.WasRun,
            this.Outcome,
            this.SkipReason,
            this.ResponseSuccess,
            this.Merged,
            this.PromptCharacters,
            this.ResponseCharacters,
            this.TimeoutSeconds,
            this.ErrorMessage,
            this.TravelDecisionDetail
        });
    }
}
