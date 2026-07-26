using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Llm;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class AiBehaviorClient
{
    private static readonly HttpClient HttpClient = new();

    private readonly ModConfig config;
    private readonly IMonitor monitor;

    public AiBehaviorClient(ModConfig config, IMonitor monitor)
    {
        this.config = config;
        this.monitor = monitor;
    }

    public bool CanUse =>
        this.config.EnableAiPlanner
        && !string.IsNullOrWhiteSpace(this.config.AiPlannerEndpoint)
        && !string.IsNullOrWhiteSpace(this.config.AiPlannerModel);

    public async Task<BehaviorIntent?> ChooseIntentAsync(NPC npc, BehaviorTrigger trigger, CancellationToken cancellationToken)
    {
        if (!this.CanUse || npc == null || RsvAiPolicy.IsBlockedNpc(npc))
        {
            return null;
        }

        string prompt = RsvAiPolicy.RemoveBlockedLines(this.BuildPrompt(npc, trigger));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, this.config.AiPlannerTimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        string payloadJson = BuildPayloadJson(
            this.config.AiPlannerModel,
            RsvAiPolicy.RemoveBlockedLines(PromptFragments.Planner.SystemMessage),
            prompt);

        using var request = new HttpRequestMessage(HttpMethod.Post, this.config.AiPlannerEndpoint);
        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(this.config.AiPlannerApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.config.AiPlannerApiKey);
        }

        try
        {
            using var response = await HttpClient.SendAsync(request, linked.Token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this.monitor.Log(I18n.Get("log.aiPlanner.requestFailed", new { status = (int)response.StatusCode, reason = response.ReasonPhrase }), LogLevel.Warn);
                return null;
            }

            string content = this.ExtractAssistantContent(body);
            return this.ParseIntent(npc.Name, content);
        }
        catch (OperationCanceledException)
        {
            this.monitor.Log(I18n.Get("log.aiPlanner.timeout"), LogLevel.Trace);
            return null;
        }
        catch (Exception ex)
        {
            this.monitor.Log(I18n.Get("log.aiPlanner.failed", new { error = ex.Message }), LogLevel.Warn);
            return null;
        }
    }

    /// <summary>
    /// 组 chat/completions 请求体。端点与模型名都来自用户配置（OpenAI 兼容形态），
    /// 按模型名走同一推理模型判定：gpt-5/o 系拒绝 max_tokens 与非默认 temperature，
    /// 改发 max_completion_tokens 并省略 temperature；其余模型保持原字段不变。
    /// </summary>
    internal static string BuildPayloadJson(string model, string systemContent, string userContent)
    {
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            [LlmThinking.OpenAiMaxTokensFieldName(model)] = 120,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemContent },
                new { role = "user", content = userContent }
            }
        };

        if (!LlmThinking.IsOpenAiReasoningModel(model))
        {
            payload["temperature"] = 0.2;
        }

        return JsonSerializer.Serialize(payload);
    }

    private string BuildPrompt(NPC npc, BehaviorTrigger trigger)
    {
        var allowed = new List<string>();
        if (this.config.AllowFacePlayer)
        {
            allowed.Add("FacePlayer");
            allowed.Add("Pause");
            allowed.Add("LookAround");
        }

        if (this.config.AllowEmotes)
        {
            allowed.Add("Emote");
        }

        if (this.config.AllowApproachPlayer && trigger == BehaviorTrigger.Manual)
        {
            allowed.Add("ApproachPlayer");
            allowed.Add("StepAway");
        }

        var world = WorldContext.For(npc);
        var disposition = NpcDisposition.For(npc);
        return PromptFragments.Planner.UserPrompt(
            npc,
            trigger,
            allowed,
            world,
            disposition,
            Game1.year,
            Vector2.Distance(npc.Tile, Game1.player.Tile));
    }

    private string ExtractAssistantContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var first = choices[0];
        if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        if (first.TryGetProperty("text", out var text))
        {
            return text.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private BehaviorIntent? ParseIntent(string npcName, string rawContent)
    {
        string json = this.StripJsonFence(rawContent);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("intent", out var intentProperty))
        {
            return null;
        }

        string? intentText = intentProperty.GetString();
        if (!Enum.TryParse(intentText, ignoreCase: true, out BehaviorIntentType type))
        {
            return null;
        }

        if (type == BehaviorIntentType.FacePlayer && !this.config.AllowFacePlayer)
        {
            return null;
        }

        if (type == BehaviorIntentType.Emote && !this.config.AllowEmotes)
        {
            return null;
        }

        if (type == BehaviorIntentType.ApproachPlayer && !this.config.AllowApproachPlayer)
        {
            return null;
        }

        if (type == BehaviorIntentType.StepAway && !this.config.AllowApproachPlayer)
        {
            return null;
        }

        if ((type == BehaviorIntentType.Pause || type == BehaviorIntentType.LookAround) && !this.config.AllowFacePlayer)
        {
            return null;
        }

        string reason = root.TryGetProperty("reason", out var reasonProperty)
            ? reasonProperty.GetString() ?? "they responded to the moment"
            : "they responded to the moment";

        int emoteId = root.TryGetProperty("emoteId", out var emoteProperty) && emoteProperty.TryGetInt32(out int parsedEmote)
            ? parsedEmote
            : 16;

        return new BehaviorIntent(type, npcName, this.TrimReason(reason), emoteId);
    }

    private string StripJsonFence(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        int firstNewLine = text.IndexOf('\n');
        int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return text;
        }

        return text[(firstNewLine + 1)..lastFence].Trim();
    }

    private string TrimReason(string reason)
    {
        reason = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return reason.Length <= 140 ? reason : reason[..140];
    }
}
