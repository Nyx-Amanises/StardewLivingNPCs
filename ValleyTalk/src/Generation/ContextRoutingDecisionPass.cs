using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ValleyTalk;

internal static class ContextRoutingDecisionPass
{
    private const double MinimumConfidence = 0.55;
    private const int MaxRoutingTimeoutSeconds = 8;

    private static readonly Dictionary<string, ContextModule> ModuleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["world"] = ContextModule.World,
        ["npcProfile"] = ContextModule.NpcProfile,
        ["gameState"] = ContextModule.GameState,
        ["sampleDialogue"] = ContextModule.SampleDialogue,
        ["eventHistory"] = ContextModule.EventHistory,
        ["dateTime"] = ContextModule.DateTime,
        ["weather"] = ContextModule.Weather,
        ["nearbyNpcs"] = ContextModule.NearbyNpcs,
        ["relationship"] = ContextModule.Relationship,
        ["farm"] = ContextModule.Farm,
        ["location"] = ContextModule.Location,
        ["trinkets"] = ContextModule.Trinkets,
        ["recentEvents"] = ContextModule.RecentEvents,
        ["specialDates"] = ContextModule.SpecialDates,
        ["gift"] = ContextModule.Gift,
        ["livingNpc"] = ContextModule.LivingNpc,
        ["spouseAction"] = ContextModule.SpouseAction,
        ["preoccupation"] = ContextModule.Preoccupation,
        ["currentConversation"] = ContextModule.CurrentConversation
    };

    public static async Task<ContextRoutingPlan> BuildPlanAsync(Character character, DialogueContext context)
    {
        if (ModEntry.Config?.EnableSemanticContextRouting != true || character == null || context == null)
        {
            return ContextRoutingPlan.Full().WithRoutingDiagnostics("disabled-full", 0, 0);
        }

        string prompt = BuildRouterPrompt(character, context);
        LlmResponse response;
        int timeoutSeconds = Math.Clamp(
            ModEntry.Config.SemanticContextRoutingTimeoutSeconds,
            2,
            Math.Min(MaxRoutingTimeoutSeconds, Math.Max(2, ModEntry.Config.QueryTimeout)));
        var routeWatch = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            var task = Llm.Instance.RunInference(
                "You are a fast semantic router for a Stardew Valley dialogue prompt. Return only compact JSON.",
                string.Empty,
                $"NPC: {character.Name} ({character.StardewNpc?.displayName ?? character.Name})",
                prompt,
                string.Empty,
                n_predict: 384,
                allowRetry: false);
            response = await task.WaitAsync(cts.Token);
        }
        catch (Exception ex)
        {
            routeWatch.Stop();
            string outcome = ex is OperationCanceledException || ex is TimeoutException ? "timeout-full" : "failed-full";
            if (ModEntry.Config.Debug)
            {
                ModEntry.SMonitor.Log($"Semantic context routing {outcome} for {character.Name} after {routeWatch.ElapsedMilliseconds}ms: {ex.Message}; using full context.", StardewModdingAPI.LogLevel.Debug);
            }
            return ContextRoutingPlan.Full().WithRoutingDiagnostics(outcome, routeWatch.ElapsedMilliseconds, timeoutSeconds);
        }

        routeWatch.Stop();
        TokenUsage usage = response.Usage.HasAnyTokens
            ? response.Usage
            : TokenUsage.Estimate(prompt, response.Text ?? response.ErrorMessage ?? string.Empty);
        TokenUsageTracker.Instance.Record(
            character.Name,
            usage,
            ModEntry.Config.Provider,
            ModEntry.Config.ModelName,
            response.IsSuccess ? "context-routing" : "context-routing-failed");

        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
        {
            return ContextRoutingPlan.Full().WithRoutingDiagnostics("response-failed-full", routeWatch.ElapsedMilliseconds, timeoutSeconds);
        }

        if (!TryParsePlan(response.Text, context, out ContextRoutingPlan plan) || plan == null)
        {
            if (ModEntry.Config.Debug)
            {
                ModEntry.SMonitor.Log($"Semantic context routing returned unparseable output for {character.Name}; using full context. Output: {response.Text}", StardewModdingAPI.LogLevel.Debug);
            }
            return ContextRoutingPlan.Full().WithRoutingDiagnostics("parse-failed-full", routeWatch.ElapsedMilliseconds, timeoutSeconds);
        }

        ApplyDeterministicBoundaries(plan, context);
        plan.ApplyDependencies();
        plan.WithRoutingDiagnostics("success", routeWatch.ElapsedMilliseconds, timeoutSeconds);
        if (ModEntry.Config.Debug)
        {
            ModEntry.SMonitor.Log($"Semantic context routing for {character.Name}: {plan.DebugLabel()}", StardewModdingAPI.LogLevel.Debug);
        }
        return plan;
    }

    private static bool TryParsePlan(string text, DialogueContext context, out ContextRoutingPlan plan)
    {
        plan = null;
        string jsonText = ExtractJsonObject(text);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return false;
        }

        try
        {
            var json = JObject.Parse(jsonText);
            double confidence = json.Value<double?>("confidence") ?? 0;
            if (confidence < MinimumConfidence)
            {
                return false;
            }

            var parsed = ContextRoutingPlan.ConservativeBrief();
            foreach (var pair in ModuleKeys)
            {
                string raw = json.Value<string>(pair.Key);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                parsed.Set(pair.Value, ParseDetail(raw));
            }

            ApplyDeterministicBoundaries(parsed, context);
            plan = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyDeterministicBoundaries(ContextRoutingPlan plan, DialogueContext context)
    {
        plan.ApplyHardBoundaries();
        if (context.Accept != null)
        {
            plan.Promote(ContextModule.Gift, ContextDetail.Full);
            plan.Promote(ContextModule.LivingNpc, ContextDetail.Full);
            plan.Promote(ContextModule.Relationship, ContextDetail.Full);
        }

        if (!string.IsNullOrWhiteSpace(context.LivingNpcExtraPrompt))
        {
            if (ContainsAny(
                    context.LivingNpcExtraPrompt,
                    "Gift Opportunity",
                    "Help Request Opportunity",
                    "Active Companion Outing",
                    "Help-request fit",
                    "currently reasonable item requests"))
            {
                plan.Promote(ContextModule.LivingNpc, ContextDetail.Full);
                plan.Promote(ContextModule.Location, ContextDetail.Full);
            }
        }

        string playerText = context.ChatHistory?.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        if (ContainsAny(playerText, "去哪", "去哪里", "一起去", "带我", "带你", "陪我", "陪你", "where", "go", "come with", "show me"))
        {
            plan.Promote(ContextModule.Location, ContextDetail.Full);
            plan.Promote(ContextModule.LivingNpc, ContextDetail.Full);
        }
    }

    private static ContextDetail ParseDetail(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "full" => ContextDetail.Full,
            "brief" => ContextDetail.Brief,
            "none" => ContextDetail.None,
            _ => ContextDetail.Brief
        };
    }

    private static string BuildRouterPrompt(Character character, DialogueContext context)
    {
        string playerText = context.ChatHistory?.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        string recentHistory = FormatRecentHistory(context);
        var prompt = new StringBuilder();
        prompt.AppendLine("Choose which context modules are needed for the next NPC reply. The dialogue may be Chinese or English; judge meaning, not keywords.");
        prompt.AppendLine("Prefer brief unless full context is needed. Never omit safety/core modules by trying to be clever; code will apply hard dependencies after your decision.");
        prompt.AppendLine();
        prompt.AppendLine("Facts:");
        prompt.AppendLine($"- NPC: {character.StardewNpc?.displayName ?? character.Name} ({character.Name}); hearts: {(context.Hearts?.ToString() ?? "unknown")}.");
        prompt.AppendLine($"- Location: {context.Location ?? "unknown"}; time: {context.TimeOfDay ?? "unknown"}; weather: {string.Join(", ", context.Weather ?? new List<string>())}.");
        prompt.AppendLine($"- Gift response: {(context.Accept != null ? "yes" : "no")}; LivingNPC context present: {!string.IsNullOrWhiteSpace(context.LivingNpcExtraPrompt)}.");
        if (!string.IsNullOrWhiteSpace(context.CurrentActivity))
        {
            prompt.AppendLine($"- Current activity: {context.CurrentActivity}.");
        }
        if (!string.IsNullOrWhiteSpace(context.NextScheduleLocation))
        {
            prompt.AppendLine($"- Next schedule: {context.NextScheduleLocation} in {(context.MinutesUntilNextSchedule?.ToString() ?? "unknown")} minutes.");
        }
        prompt.AppendLine();
        prompt.AppendLine("Recent conversation:");
        prompt.AppendLine(recentHistory);
        prompt.AppendLine();
        prompt.AppendLine($"Farmer latest text: {Clean(playerText)}");
        prompt.AppendLine();
        prompt.AppendLine("Module meanings:");
        prompt.AppendLine("- world: Stardew/world/SVE lore and world progress.");
        prompt.AppendLine("- npcProfile: biography, personality, relationships.");
        prompt.AppendLine("- gameState/recentEvents/eventHistory: farm/world achievements and older conversation/event memory.");
        prompt.AppendLine("- location/weather/dateTime/nearbyNpcs: current scene, schedule, nearby people.");
        prompt.AppendLine("- relationship/farm/gift/livingNpc: friendship, spouse/farm details, gift reaction, LivingNPCs memory/actions/help/outing/conflict.");
        prompt.AppendLine("- sampleDialogue/currentConversation: style examples and visible chat history.");
        prompt.AppendLine();
        prompt.AppendLine("Return only JSON with keys world,npcProfile,gameState,sampleDialogue,eventHistory,dateTime,weather,nearbyNpcs,relationship,farm,location,trinkets,recentEvents,specialDates,gift,livingNpc,spouseAction,preoccupation,currentConversation,confidence,reason.");
        prompt.AppendLine("Each module value must be none, brief, or full. confidence is 0-1. reason is short.");
        prompt.AppendLine("Use full for location/livingNpc/gift when the farmer may be asking to go somewhere, asking/offering help, giving/receiving items, handling conflict, nickname, mood, outing, or concrete world action.");
        prompt.AppendLine("Use full for world only when the reply needs specific lore/progress. Use sampleDialogue none unless style is likely fragile or this is an unfamiliar/custom NPC.");
        return prompt.ToString();
    }

    private static string FormatRecentHistory(DialogueContext context)
    {
        if (context.ChatHistory == null || context.ChatHistory.Count == 0)
        {
            return "<none>";
        }

        return string.Join(
            "\n",
            context.ChatHistory
                .TakeLast(4)
                .Select(line => $"- {(line.IsPlayerLine ? "Farmer" : "NPC")}: {Clean(line.Text)}"));
    }

    private static string ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return text[start..(end + 1)];
    }

    private static string Clean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        string cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return cleaned.Length <= 800 ? cleaned : cleaned[..800] + "...";
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