using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        if (!this.CanUse)
        {
            return null;
        }

        string prompt = this.BuildPrompt(npc, trigger);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, this.config.AiPlannerTimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var payload = new
        {
            model = this.config.AiPlannerModel,
            temperature = 0.2,
            max_tokens = 120,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You choose tiny, safe Stardew Valley NPC behavior intents. Return only JSON."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, this.config.AiPlannerEndpoint);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
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
                this.monitor.Log($"AI planner request failed: {(int)response.StatusCode} {response.ReasonPhrase}", LogLevel.Warn);
                return null;
            }

            string content = this.ExtractAssistantContent(body);
            return this.ParseIntent(npc.Name, content);
        }
        catch (OperationCanceledException)
        {
            this.monitor.Log("AI planner timed out. Falling back to rule-based behavior.", LogLevel.Trace);
            return null;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"AI planner failed: {ex.Message}", LogLevel.Warn);
            return null;
        }
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
        string nearby = string.Join(", ", world.NearbyNpcNames);

        var prompt = new StringBuilder();
        prompt.AppendLine($"NPC: {npc.displayName} ({npc.Name})");
        prompt.AppendLine($"Profile source: {disposition.SourceLabel}");
        prompt.AppendLine($"Disposition: {disposition.PromptLabel}");
        if (disposition.HasProfileContext)
        {
            prompt.AppendLine($"Profile context: {disposition.BackgroundPrompt} {disposition.DialoguePrompt}");
        }

        prompt.AppendLine($"Trigger: {trigger}");
        prompt.AppendLine($"Location: {world.LocationDisplayName} ({world.LocationName})");
        prompt.AppendLine($"Date: year {Game1.year}, {world.Season} {world.DayOfMonth}");
        prompt.AppendLine($"Time: {world.TimeOfDay}");
        prompt.AppendLine($"Scene context: {world.PromptLabel}");
        prompt.AppendLine($"Distance to farmer in tiles: {Math.Round(Vector2.Distance(npc.Tile, Game1.player.Tile), 1)}");
        prompt.AppendLine($"Nearby NPCs: {(string.IsNullOrWhiteSpace(nearby) ? "none" : nearby)}");
        prompt.AppendLine();
        prompt.AppendLine($"Allowed intents: {string.Join(", ", allowed)}");
        prompt.AppendLine();
        prompt.AppendLine("Choose one tiny behavior that makes the NPC feel alive without disrupting schedules.");
        prompt.AppendLine("Return exactly this JSON shape:");
        prompt.AppendLine("{\"intent\":\"FacePlayer|Emote|ApproachPlayer|Pause|LookAround|StepAway\",\"reason\":\"short in-world reason\",\"emoteId\":16}");
        return prompt.ToString();
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
