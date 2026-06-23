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
    private const int MaxRoutingTimeoutSeconds = 30;

    // Single-slot cache of the most recent raw (pre-boundary) routing decision. A multi-turn
    // conversation keeps the same key, so it only pays the router round-trip once; the per-turn
    // deterministic boundaries are re-applied to a clone every turn, so dynamic cues
    // (gift/travel/help) still take effect. The key changes when a new conversation starts
    // (ClearContext resets the history), which naturally evicts the previous decision.
    private static readonly object CacheGate = new();
    private static string cachedConversationKey;
    private static ContextRoutingPlan cachedRawPlan;

    private static readonly string[] TopicShiftInquiryFragments =
    [
        "为什么",
        "怎么回事",
        "怎么会",
        "是什么",
        "发生了什么",
        "跟我讲讲",
        "告诉我",
        "why",
        "how",
        "what happened",
        "tell me about",
        "explain"
    ];

    private static readonly string[] WorldLoreTopicFragments =
    [
        "这个世界",
        "星露谷",
        "鹈鹕镇",
        "社区中心",
        "joja",
        "祝尼魔",
        "矮人",
        "暗影",
        "法师",
        "魔法",
        "战争",
        "传说",
        "历史",
        "矿洞",
        "沙漠",
        "姜岛",
        "巴士",
        "温室",
        "world",
        "stardew",
        "pelican town",
        "community center",
        "junimo",
        "dwarf",
        "shadow",
        "wizard",
        "magic",
        "war",
        "lore",
        "history",
        "mine",
        "desert",
        "ginger island"
    ];

    private static readonly string[] FarmTopicFragments =
    [
        "我的农场",
        "农场",
        "作物",
        "种子",
        "种了",
        "菜地",
        "动物",
        "鸡舍",
        "畜棚",
        "果树",
        "洒水器",
        "farm",
        "crop",
        "seed",
        "animal",
        "coop",
        "barn",
        "greenhouse",
        "sprinkler"
    ];

    private static readonly string[] HistoryTopicFragments =
    [
        "还记得",
        "记不记得",
        "之前",
        "上次",
        "昨天",
        "那天",
        "我们说过",
        "我答应",
        "你答应",
        "remember",
        "before",
        "last time",
        "yesterday",
        "promised"
    ];

    private static readonly string[] NpcProfileTopicFragments =
    [
        "你妈妈",
        "你的妈妈",
        "家里",
        "童年",
        "梦想",
        "喜欢什么",
        "讨厌什么",
        "潘姆",
        "文森特",
        "贾斯",
        "your mother",
        "your family",
        "childhood",
        "dream",
        "pam",
        "vincent",
        "jas"
    ];

    private static readonly string[] LocationTopicFragments =
    [
        "这里",
        "这个地方",
        "海边",
        "海滩",
        "森林",
        "山上",
        "镇上",
        "图书馆",
        "博物馆",
        "this place",
        "here",
        "beach",
        "forest",
        "mountain",
        "town",
        "library",
        "museum"
    ];

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

        string conversationKey = BuildConversationKey(character, context);
        string cacheBypassReason = string.Empty;
        if (conversationKey != null && TryReuseCachedPlan(conversationKey, context, out ContextRoutingPlan cachedPlan, out cacheBypassReason))
        {
            if (ModEntry.Config.Debug)
            {
                ModEntry.SMonitor.Log($"Semantic context routing reused cached plan for {character.Name}: {cachedPlan.DebugLabel()}", StardewModdingAPI.LogLevel.Debug);
            }

            ExportLog(character.Name, context, "cached", 0, 0, "reused-conversation-plan", cachedPlan.DebugLabel(), string.Empty, string.Empty, string.Empty);
            return cachedPlan;
        }

        if (!string.IsNullOrWhiteSpace(cacheBypassReason) && ModEntry.Config.Debug)
        {
            ModEntry.SMonitor.Log($"Semantic context routing refreshed cached plan for {character.Name}: {cacheBypassReason}.", StardewModdingAPI.LogLevel.Debug);
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
                LlmThinking.RoutingSystemPrompt(),
                string.Empty,
                $"NPC: {character.Name} ({character.StardewNpc?.displayName ?? character.Name})",
                prompt,
                string.Empty,
                n_predict: 384,
                allowRetry: false,
                disableThinking: true);
            response = await task.WaitAsync(cts.Token);
        }
        catch (Exception ex)
        {
            routeWatch.Stop();
            string outcome = ex is OperationCanceledException || ex is TimeoutException ? "timeout-brief" : "failed-brief";
            if (ModEntry.Config.Debug)
            {
                ModEntry.SMonitor.Log($"Semantic context routing {outcome} for {character.Name} after {routeWatch.ElapsedMilliseconds}ms: {ex.Message}; using conservative brief context.", StardewModdingAPI.LogLevel.Debug);
            }

            var fallback = BuildFallbackPlan(outcome, context, routeWatch.ElapsedMilliseconds, timeoutSeconds, conservative: true);
            ExportLog(character.Name, context, outcome, routeWatch.ElapsedMilliseconds, timeoutSeconds, ex.GetType().Name, fallback.DebugLabel(), prompt, string.Empty, ex.Message);
            return fallback;
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
            string outcome = "response-failed-brief";
            var fallback = BuildFallbackPlan(outcome, context, routeWatch.ElapsedMilliseconds, timeoutSeconds, conservative: true);
            ExportLog(character.Name, context, outcome, routeWatch.ElapsedMilliseconds, timeoutSeconds, "empty-or-unsuccessful-response", fallback.DebugLabel(), prompt, response.Text, response.ErrorMessage);
            return fallback;
        }

        if (!TryParsePlan(response.Text, out ContextRoutingPlan plan, out string parseDetail) || plan == null)
        {
            // Two different failures land here. "Low confidence" means the model produced a usable
            // decision but flagged itself unsure — trust that signal and fall back to full context.
            // "Couldn't parse" means we got nothing usable (typically a weak model that can't emit
            // valid JSON); fall back to the conservative brief plan so we still save tokens instead of
            // paying the router round-trip and then sending the full context anyway.
            bool isLowConfidence = parseDetail.StartsWith("low-confidence", StringComparison.Ordinal);
            string outcome = isLowConfidence ? "low-confidence-full" : "parse-failed-brief";
            if (ModEntry.Config.Debug)
            {
                ModEntry.SMonitor.Log($"Semantic context routing fell back for {character.Name} ({outcome}). Reason: {parseDetail}. Output: {response.Text}", StardewModdingAPI.LogLevel.Debug);
            }

            var fallback = BuildFallbackPlan(outcome, context, routeWatch.ElapsedMilliseconds, timeoutSeconds, conservative: !isLowConfidence);
            ExportLog(character.Name, context, outcome, routeWatch.ElapsedMilliseconds, timeoutSeconds, parseDetail, fallback.DebugLabel(), prompt, response.Text, response.ErrorMessage);
            return fallback;
        }

        // Cache the raw (pre-boundary) decision so the rest of this conversation can reuse it
        // without another router round-trip.
        if (conversationKey != null)
        {
            StoreCachedPlan(conversationKey, plan.Clone());
        }

        // BuildPlanAsync is the single place that applies the per-turn deterministic boundaries;
        // dependency closure is applied once on the consumer side (Prompts constructor).
        ApplyDeterministicBoundaries(plan, context);
        plan.WithRoutingDiagnostics("success", routeWatch.ElapsedMilliseconds, timeoutSeconds);
        if (ModEntry.Config.Debug)
        {
            ModEntry.SMonitor.Log($"Semantic context routing for {character.Name}: {plan.DebugLabel()}", StardewModdingAPI.LogLevel.Debug);
        }

        ExportLog(character.Name, context, "success", routeWatch.ElapsedMilliseconds, timeoutSeconds, parseDetail, plan.DebugLabel(), prompt, response.Text, response.ErrorMessage);
        return plan;
    }

    private static string BuildConversationKey(Character character, DialogueContext context)
    {
        // Only cache within a real, ongoing conversation: the first visible line's id is stable for
        // the whole exchange and changes when a new conversation starts. Without history (gifts,
        // scheduled one-shots) there is no stable key, so we route fresh rather than risk reusing an
        // unrelated decision.
        var history = context.ChatHistory;
        if (history == null || history.Count == 0)
        {
            return null;
        }

        return $"{character.Name}|{history[0].Id}";
    }

    private static bool TryReuseCachedPlan(string conversationKey, DialogueContext context, out ContextRoutingPlan plan, out string bypassReason)
    {
        bypassReason = string.Empty;
        ContextRoutingPlan raw = null;
        lock (CacheGate)
        {
            if (string.Equals(cachedConversationKey, conversationKey, StringComparison.Ordinal) && cachedRawPlan != null)
            {
                raw = cachedRawPlan.Clone();
            }
        }

        if (raw == null)
        {
            plan = null;
            return false;
        }

        if (ShouldRefreshCachedPlanForTopicShift(context, raw, out bypassReason))
        {
            plan = null;
            return false;
        }

        ApplyDeterministicBoundaries(raw, context);
        raw.WithRoutingDiagnostics("cached", 0, 0);
        plan = raw;
        return true;
    }

    internal static bool ShouldRefreshCachedPlanForTopicShift(DialogueContext context, ContextRoutingPlan cachedRawPlan, out string reason)
    {
        reason = string.Empty;
        if (context?.ChatHistory == null || context.ChatHistory.Count < 3 || cachedRawPlan == null)
        {
            return false;
        }

        string latestPlayerText = context.ChatHistory.LastOrDefault(line => line.IsPlayerLine)?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(latestPlayerText))
        {
            return false;
        }

        var requiredModules = new HashSet<ContextModule>();
        if (LooksLikeWorldLoreShift(latestPlayerText))
        {
            requiredModules.Add(ContextModule.World);
            requiredModules.Add(ContextModule.GameState);
            requiredModules.Add(ContextModule.EventHistory);
        }

        if (ContainsAny(latestPlayerText, FarmTopicFragments))
        {
            requiredModules.Add(ContextModule.Farm);
            requiredModules.Add(ContextModule.GameState);
        }

        if (ContainsAny(latestPlayerText, HistoryTopicFragments))
        {
            requiredModules.Add(ContextModule.EventHistory);
            requiredModules.Add(ContextModule.RecentEvents);
            requiredModules.Add(ContextModule.Relationship);
        }

        if (ContainsAny(latestPlayerText, NpcProfileTopicFragments))
        {
            requiredModules.Add(ContextModule.NpcProfile);
            requiredModules.Add(ContextModule.Relationship);
        }

        if (LooksLikeLocationTopicShift(latestPlayerText))
        {
            requiredModules.Add(ContextModule.Location);
            requiredModules.Add(ContextModule.World);
        }

        var missingFullModules = requiredModules
            .Where(module => cachedRawPlan.Get(module) != ContextDetail.Full)
            .OrderBy(module => module.ToString())
            .ToArray();
        if (missingFullModules.Length == 0)
        {
            return false;
        }

        reason = $"topic-shift requires fresh routing for {string.Join(", ", missingFullModules.Select(module => module.ToString()))}";
        return true;
    }

    private static bool LooksLikeWorldLoreShift(string text)
    {
        return ContainsAny(text, WorldLoreTopicFragments)
            && (ContainsAny(text, TopicShiftInquiryFragments)
                || ContainsAny(text, "传说", "历史", "lore", "history"));
    }

    private static bool LooksLikeLocationTopicShift(string text)
    {
        return ContainsAny(text, LocationTopicFragments)
            && (ContainsAny(text, TopicShiftInquiryFragments)
                || ContainsAny(text, "这里", "这个地方", "this place", "here"));
    }

    private static void StoreCachedPlan(string conversationKey, ContextRoutingPlan rawPlan)
    {
        lock (CacheGate)
        {
            cachedConversationKey = conversationKey;
            cachedRawPlan = rawPlan;
        }
    }

    private static bool TryParsePlan(string text, out ContextRoutingPlan plan, out string parseDetail)
    {
        plan = null;
        parseDetail = string.Empty;
        string jsonText = ExtractJsonObject(text);
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            parseDetail = "no-json-object";
            return false;
        }

        try
        {
            var json = JObject.Parse(jsonText);
            double confidence = json.Value<double?>("confidence") ?? 0;
            if (confidence < MinimumConfidence)
            {
                parseDetail = $"low-confidence={confidence:0.###}";
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

            plan = parsed;
            parseDetail = $"confidence={confidence:0.###}";
            return true;
        }
        catch (Exception ex)
        {
            parseDetail = $"json-parse-error={ex.Message}";
            return false;
        }
    }

    private static ContextRoutingPlan BuildFallbackPlan(
        string outcome,
        DialogueContext context,
        long routeMilliseconds,
        int timeoutSeconds,
        bool conservative)
    {
        if (!conservative)
        {
            return ContextRoutingPlan.Full().WithRoutingDiagnostics(outcome, routeMilliseconds, timeoutSeconds);
        }

        // Start from the conservative brief plan, then re-apply the same per-turn deterministic
        // boundaries the success path uses, so dynamic cues (gift/help/outing/location) still pull in
        // the context they need even when routing itself failed. Without this, a routing failure on a
        // weak/slow model would pay the router round-trip and then send full context anyway — the worst
        // of both worlds. Brief still emits trimmed versions of the core modules, not none.
        var plan = ContextRoutingPlan.ConservativeBrief();
        ApplyDeterministicBoundaries(plan, context);
        return plan.WithRoutingDiagnostics(outcome, routeMilliseconds, timeoutSeconds);
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
        if (ConversationCues.ContainsAny(playerText, ConversationCues.LocationPromotion))
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
        prompt.AppendLine("Return only JSON with keys world,npcProfile,gameState,sampleDialogue,eventHistory,dateTime,weather,nearbyNpcs,relationship,farm,location,trinkets,recentEvents,specialDates,gift,livingNpc,spouseAction,preoccupation,currentConversation,confidence.");
        prompt.AppendLine("Each module value must be none, brief, or full. confidence is 0-1.");
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

    private static void ExportLog(
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
        ContextRoutingLogExporter.Append(
            npcName,
            context,
            outcome,
            routeMilliseconds,
            timeoutSeconds,
            parseDetail,
            planLabel,
            routerPrompt,
            rawOutput,
            errorMessage);
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
