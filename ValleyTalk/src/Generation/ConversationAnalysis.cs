using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ValleyTalk;

internal sealed class ConversationAnalysis
{
    private static readonly HashSet<string> AllowedPlayerPreferenceTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "food",
        "drink",
        "flower",
        "mineral",
        "forage",
        "nature",
        "sweet",
        "comfort",
        "practical",
        "scholarly",
        "adventurous",
        "magical",
        "artistic",
        "refined",
        "work",
        "active",
        "fishing",
        "mining",
        "farming",
        "morning",
        "night"
    };

    public static readonly ConversationAnalysis Empty = new();

    [JsonProperty("rapportDelta")]
    public int RapportDelta { get; set; }

    [JsonProperty("memories")]
    public List<ConversationMemoryCandidate> Memories { get; set; } = new();

    [JsonProperty("endConversation")]
    public bool EndConversation { get; set; }

    [JsonProperty("ambientFollowUp")]
    public ConversationAmbientFollowUp AmbientFollowUp { get; set; } = new();

    [JsonProperty("emotionImpact")]
    public ConversationEmotionImpact EmotionImpact { get; set; } = new();

    [JsonProperty("actions")]
    public List<ConversationWorldActionRequest> Actions { get; set; } = new();

    [JsonProperty("behaviorInfluences")]
    public List<ConversationBehaviorInfluenceCandidate> BehaviorInfluences { get; set; } = new();

    [JsonProperty("helpRequests")]
    public List<ConversationHelpRequestCandidate> HelpRequests { get; set; } = new();

    [JsonProperty("helpRequestUpdates")]
    public List<ConversationHelpRequestUpdateCandidate> HelpRequestUpdates { get; set; } = new();

    [JsonProperty("conflicts")]
    public List<ConversationConflictCandidate> Conflicts { get; set; } = new();

    public bool HasContent => this.RapportDelta > 0
        || this.Memories.Count > 0
        || this.EndConversation
        || this.AmbientFollowUp.HasContent
        || this.EmotionImpact.HasContent
        || this.Actions.Count > 0
        || this.BehaviorInfluences.Count > 0
        || this.HelpRequests.Count > 0
        || this.HelpRequestUpdates.Count > 0
        || this.Conflicts.Count > 0;

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this);
    }

    public static ConversationAnalysis Parse(string text)
    {
        const string marker = "!LIVINGNPCS_META";
        if (string.IsNullOrWhiteSpace(text))
        {
            return Empty;
        }

        int markerIndex = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return Empty;
        }

        string remainder = text[(markerIndex + marker.Length)..].Trim();
        int objectStart = remainder.IndexOf('{');
        if (objectStart < 0)
        {
            return Empty;
        }

        try
        {
            string jsonText = ExtractBalancedJsonObject(remainder[objectStart..]);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                return Empty;
            }

            var json = JObject.Parse(jsonText);
            var analysis = json.ToObject<ConversationAnalysis>() ?? new ConversationAnalysis();
            analysis.RapportDelta = Math.Clamp(analysis.RapportDelta, 0, 30);
            analysis.Memories = analysis.Memories
                .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
                .Select(memory =>
                {
                    memory.Kind = NormalizeKind(memory.Kind);
                    memory.Summary = memory.Summary.Trim();
                    memory.Importance = Math.Clamp(memory.Importance, 0, 100);
                    memory.PlayerPreferenceKind = NormalizePlayerPreferenceKind(memory.PlayerPreferenceKind);
                    memory.Subject = memory.Subject?.Trim() ?? string.Empty;
                    memory.Tags = NormalizePlayerPreferenceTags(memory.Tags);
                    memory.PlayerPreference = memory.PlayerPreference && memory.PlayerPreferenceKind != "none";
                    return memory;
                })
                .Take(4)
                .ToList();
            analysis.AmbientFollowUp ??= new ConversationAmbientFollowUp();
            analysis.AmbientFollowUp.Text = analysis.AmbientFollowUp.Text?.Trim() ?? string.Empty;
            analysis.AmbientFollowUp.DelayMinutes = Math.Clamp(analysis.AmbientFollowUp.DelayMinutes, 0, 120);
            analysis.EmotionImpact ??= new ConversationEmotionImpact();
            analysis.EmotionImpact.Emotion = NormalizeEmotion(analysis.EmotionImpact.Emotion);
            analysis.EmotionImpact.IntensityDelta = Math.Clamp(analysis.EmotionImpact.IntensityDelta, -100, 100);
            analysis.EmotionImpact.RepairDelta = Math.Clamp(analysis.EmotionImpact.RepairDelta, 0, 100);
            analysis.EmotionImpact.Reason = analysis.EmotionImpact.Reason?.Trim() ?? string.Empty;
            analysis.Actions = analysis.Actions
                .Where(action => action != null)
                .Select(action =>
                {
                    action.Type = NormalizeActionType(action.Type);
                    action.Reason = action.Reason?.Trim() ?? string.Empty;
                    action.Amount = Math.Clamp(action.Amount, 0, 250);
                    action.TileCount = Math.Clamp(action.TileCount, 0, 12);
                    action.DurationMinutes = Math.Clamp(action.DurationMinutes, 0, 20);
                    action.DelayMinutes = Math.Clamp(action.DelayMinutes, 0, 20);
                    action.TargetLocation = action.TargetLocation?.Trim() ?? string.Empty;
                    action.QuestHint = action.QuestHint?.Trim() ?? string.Empty;
                    action.ItemId = action.ItemId?.Trim() ?? string.Empty;
                    action.ItemLabel = action.ItemLabel?.Trim() ?? string.Empty;
                    return action;
                })
                .Where(action => action.Type != "none")
                .Take(1)
                .ToList();
            analysis.BehaviorInfluences = analysis.BehaviorInfluences
                .Where(influence => influence != null && !string.IsNullOrWhiteSpace(influence.Summary))
                .Select(influence =>
                {
                    influence.Type = NormalizeBehaviorInfluenceType(influence.Type);
                    influence.Summary = influence.Summary.Trim();
                    influence.TargetLocation = influence.TargetLocation?.Trim() ?? string.Empty;
                    influence.TargetLocationLabel = influence.TargetLocationLabel?.Trim() ?? string.Empty;
                    influence.DurationDays = Math.Clamp(influence.DurationDays, 0, 7);
                    influence.Intensity = Math.Clamp(influence.Intensity, 0, 100);
                    influence.MaxTriggers = Math.Clamp(influence.MaxTriggers, 0, 4);
                    return influence;
                })
                .Where(influence => influence.Type != "none")
                .Take(2)
                .ToList();
            analysis.HelpRequests = analysis.HelpRequests
                .Where(request => request != null && !string.IsNullOrWhiteSpace(request.Summary))
                .Select(request =>
                {
                    request.Type = NormalizeHelpRequestType(request.Type);
                    request.Summary = request.Summary.Trim();
                    request.RequestedItemId = request.RequestedItemId?.Trim() ?? string.Empty;
                    request.RequestedItemLabel = request.RequestedItemLabel?.Trim() ?? string.Empty;
                    request.QuestionTopic = request.QuestionTopic?.Trim() ?? string.Empty;
                    request.DueInDays = Math.Clamp(request.DueInDays, 1, 7);
                    request.Reason = request.Reason?.Trim() ?? string.Empty;
                    request.FollowUpPotential = NormalizeHelpRequestFollowUpPotential(request.FollowUpPotential);
                    request.Steps = (request.Steps ?? new List<ConversationHelpRequestStepCandidate>())
                        .Where(step => step != null)
                        .Select(step =>
                        {
                            step.Type = NormalizeHelpRequestType(step.Type);
                            step.Summary = step.Summary?.Trim() ?? string.Empty;
                            step.RequestedItemId = step.RequestedItemId?.Trim() ?? string.Empty;
                            step.RequestedItemLabel = step.RequestedItemLabel?.Trim() ?? string.Empty;
                            step.QuestionTopic = step.QuestionTopic?.Trim() ?? string.Empty;
                            return step;
                        })
                        .Where(step => step.Type != "none" && !string.IsNullOrWhiteSpace(step.Summary))
                        .Take(3)
                        .ToList();
                    return request;
                })
                .Where(request => request.Type != "none")
                .Take(1)
                .ToList();
            analysis.HelpRequestUpdates = analysis.HelpRequestUpdates
                .Where(update => update != null && !string.IsNullOrWhiteSpace(update.Summary))
                .Select(update =>
                {
                    update.Summary = update.Summary.Trim();
                    update.Status = NormalizeHelpRequestUpdateStatus(update.Status);
                    update.Resolution = update.Resolution?.Trim() ?? string.Empty;
                    return update;
                })
                .Where(update => update.Status != "none")
                .Take(2)
                .ToList();
            analysis.Conflicts = analysis.Conflicts
                .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary))
                .Select(conflict =>
                {
                    conflict.CauseKind = NormalizeConflictCauseKind(conflict.CauseKind);
                    conflict.Summary = conflict.Summary.Trim();
                    conflict.Severity = Math.Clamp(conflict.Severity, 0, 100);
                    return conflict;
                })
                .Where(conflict => conflict.Severity > 0)
                .Take(2)
                .ToList();
            return analysis;
        }
        catch
        {
            return Empty;
        }
    }

    private static string ExtractBalancedJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '{')
        {
            return string.Empty;
        }

        int depth = 0;
        bool inString = false;
        bool escaping = false;
        for (int i = 0; i < text.Length; i++)
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
                    return text[..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static string NormalizeKind(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "preference" => "preference",
            "promise" => "promise",
            "boundary" => "boundary",
            "relationship" => "relationship",
            _ => "fact"
        };
    }

    private static string NormalizeActionType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "give_small_gift" => "give_small_gift",
            "give_meaningful_gift" => "give_meaningful_gift",
            "give_money" => "give_money",
            "water_nearby_crops" => "water_nearby_crops",
            "walk_together" => "walk_together",
            "escort_to_location" => "escort_to_location",
            "festival_interaction" => "festival_interaction",
            "assist_quest" => "assist_quest",
            _ => "none"
        };
    }

    private static string NormalizeBehaviorInfluenceType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "companion_walk" => "companion_walk",
            "walk_together" => "companion_walk",
            "visit_location" => "visit_location",
            "go_to_location" => "visit_location",
            "comforted" => "comforted",
            "reassured" => "comforted",
            "offended" => "offended",
            "hurt" => "offended",
            "give_space" => "give_space",
            "needs_space" => "give_space",
            "stay_near" => "stay_near",
            "approach" => "stay_near",
            "pause_to_talk" => "pause_to_talk",
            "stop_to_talk" => "pause_to_talk",
            _ => "none"
        };
    }

    private static string NormalizeHelpRequestType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "item_request" => "item_request",
            "question_request" => "question_request",
            _ => "none"
        };
    }

    private static string NormalizeHelpRequestUpdateStatus(string status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "accepted" => "accepted",
            "fulfilled" => "fulfilled",
            "advanced" => "advanced",
            "declined" => "declined",
            _ => "none"
        };
    }

    private static string NormalizeHelpRequestFollowUpPotential(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "deeper_relationship" => "deeper_relationship",
            _ => "none"
        };
    }

    private static string NormalizeEmotion(string emotion)
    {
        return emotion?.Trim().ToLowerInvariant() switch
        {
            "happy" => "Happy",
            "calm" => "Calm",
            "jealous" => "Jealous",
            "worried" => "Worried",
            "grateful" => "Grateful",
            "disappointed" => "Disappointed",
            "uneasy" => "Uneasy",
            "upset" => "Upset",
            "angry" => "Angry",
            "sad" => "Sad",
            _ => "none"
        };
    }

    private static string NormalizeConflictCauseKind(string causeKind)
    {
        return causeKind?.Trim().ToLowerInvariant() switch
        {
            "dialogue" => "dialogue",
            "gift" => "gift",
            "boundary" => "boundary",
            "promise" => "promise",
            _ => "dialogue"
        };
    }

    private static string NormalizePlayerPreferenceKind(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "liked_item_category" => "liked_item_category",
            "disliked_item" => "disliked_item",
            "habit" => "habit",
            "value" => "value",
            "goal" => "goal",
            _ => "none"
        };
    }

    private static List<string> NormalizePlayerPreferenceTags(IEnumerable<string> tags)
    {
        if (tags == null)
        {
            return new List<string>();
        }

        return tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => AllowedPlayerPreferenceTags.Contains(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList()
            ?? new List<string>();
    }
}

internal sealed class ConversationMemoryCandidate
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = "fact";

    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("importance")]
    public int Importance { get; set; }

    [JsonProperty("playerPreference")]
    public bool PlayerPreference { get; set; }

    [JsonProperty("playerPreferenceKind")]
    public string PlayerPreferenceKind { get; set; } = "none";

    [JsonProperty("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();
}

internal sealed class ConversationAmbientFollowUp
{
    [JsonProperty("text")]
    public string Text { get; set; } = string.Empty;

    [JsonProperty("delayMinutes")]
    public int DelayMinutes { get; set; }

    public bool HasContent => !string.IsNullOrWhiteSpace(this.Text);
}

internal sealed class ConversationEmotionImpact
{
    [JsonProperty("emotion")]
    public string Emotion { get; set; } = "none";

    [JsonProperty("intensityDelta")]
    public int IntensityDelta { get; set; }

    [JsonProperty("apology")]
    public bool Apology { get; set; }

    [JsonProperty("repairDelta")]
    public int RepairDelta { get; set; }

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;

    public bool HasContent => this.Emotion != "none"
        || this.IntensityDelta != 0
        || this.Apology
        || this.RepairDelta > 0;
}

internal sealed class ConversationWorldActionRequest
{
    [JsonProperty("type")]
    public string Type { get; set; } = "none";

    [JsonProperty("amount")]
    public int Amount { get; set; }

    [JsonProperty("tileCount")]
    public int TileCount { get; set; }

    [JsonProperty("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonProperty("delayMinutes")]
    public int DelayMinutes { get; set; }

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonProperty("targetLocation")]
    public string TargetLocation { get; set; } = string.Empty;

    [JsonProperty("questHint")]
    public string QuestHint { get; set; } = string.Empty;

    [JsonProperty("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonProperty("itemLabel")]
    public string ItemLabel { get; set; } = string.Empty;
}

internal sealed class ConversationBehaviorInfluenceCandidate
{
    [JsonProperty("type")]
    public string Type { get; set; } = "none";

    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("targetLocation")]
    public string TargetLocation { get; set; } = string.Empty;

    [JsonProperty("targetLocationLabel")]
    public string TargetLocationLabel { get; set; } = string.Empty;

    [JsonProperty("durationDays")]
    public int DurationDays { get; set; }

    [JsonProperty("intensity")]
    public int Intensity { get; set; }

    [JsonProperty("maxTriggers")]
    public int MaxTriggers { get; set; }
}

internal sealed class ConversationHelpRequestCandidate
{
    [JsonProperty("type")]
    public string Type { get; set; } = "none";

    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("requiresAcceptance")]
    public bool RequiresAcceptance { get; set; } = true;

    [JsonProperty("steps")]
    public List<ConversationHelpRequestStepCandidate> Steps { get; set; } = new();

    [JsonProperty("requestedItemId")]
    public string RequestedItemId { get; set; } = string.Empty;

    [JsonProperty("requestedItemLabel")]
    public string RequestedItemLabel { get; set; } = string.Empty;

    [JsonProperty("questionTopic")]
    public string QuestionTopic { get; set; } = string.Empty;

    [JsonProperty("dueInDays")]
    public int DueInDays { get; set; } = 3;

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonProperty("followUpPotential")]
    public string FollowUpPotential { get; set; } = "none";
}

internal sealed class ConversationHelpRequestStepCandidate
{
    [JsonProperty("type")]
    public string Type { get; set; } = "none";

    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("requestedItemId")]
    public string RequestedItemId { get; set; } = string.Empty;

    [JsonProperty("requestedItemLabel")]
    public string RequestedItemLabel { get; set; } = string.Empty;

    [JsonProperty("questionTopic")]
    public string QuestionTopic { get; set; } = string.Empty;
}

internal sealed class ConversationHelpRequestUpdateCandidate
{
    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("status")]
    public string Status { get; set; } = "none";

    [JsonProperty("resolution")]
    public string Resolution { get; set; } = string.Empty;
}

internal sealed class ConversationConflictCandidate
{
    [JsonProperty("causeKind")]
    public string CauseKind { get; set; } = "dialogue";

    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("severity")]
    public int Severity { get; set; }
}
