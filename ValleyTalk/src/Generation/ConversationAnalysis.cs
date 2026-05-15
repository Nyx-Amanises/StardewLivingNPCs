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

    [JsonProperty("commitments")]
    public List<ConversationCommitmentCandidate> Commitments { get; set; } = new();

    [JsonProperty("conflicts")]
    public List<ConversationConflictCandidate> Conflicts { get; set; } = new();

    public bool HasContent => this.RapportDelta > 0
        || this.Memories.Count > 0
        || this.EndConversation
        || this.AmbientFollowUp.HasContent
        || this.EmotionImpact.HasContent
        || this.Actions.Count > 0
        || this.Commitments.Count > 0
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
            var json = JObject.Parse(remainder[objectStart..]);
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
                    action.TargetLocation = action.TargetLocation?.Trim() ?? string.Empty;
                    action.QuestHint = action.QuestHint?.Trim() ?? string.Empty;
                    return action;
                })
                .Where(action => action.Type != "none")
                .Take(1)
                .ToList();
            analysis.Commitments = analysis.Commitments
                .Where(commitment => commitment != null && !string.IsNullOrWhiteSpace(commitment.Summary))
                .Select(commitment =>
                {
                    commitment.Type = NormalizeCommitmentType(commitment.Type);
                    commitment.Summary = commitment.Summary.Trim();
                    commitment.DueInDays = Math.Clamp(commitment.DueInDays, 0, 28);
                    commitment.TimeOfDay = NormalizeTimeOfDay(commitment.TimeOfDay);
                    commitment.Location = commitment.Location?.Trim() ?? string.Empty;
                    commitment.LocationLabel = commitment.LocationLabel?.Trim() ?? string.Empty;
                    return commitment;
                })
                .Where(commitment => commitment.Type != "none")
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

    private static string NormalizeCommitmentType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "meet_again" => "meet_again",
            "go_together" => "go_together",
            "help_task" => "help_task",
            _ => "none"
        };
    }

    private static string NormalizeEmotion(string emotion)
    {
        return emotion?.Trim().ToLowerInvariant() switch
        {
            "happy" => "Happy",
            "calm" => "Calm",
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

    private static int NormalizeTimeOfDay(int value)
    {
        if (value <= 0)
        {
            return 900;
        }

        int hours = Math.Clamp(value / 100, 6, 26);
        int minutes = Math.Clamp(value % 100, 0, 50);
        minutes -= minutes % 10;
        return (hours * 100) + minutes;
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

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonProperty("targetLocation")]
    public string TargetLocation { get; set; } = string.Empty;

    [JsonProperty("questHint")]
    public string QuestHint { get; set; } = string.Empty;
}

internal sealed class ConversationCommitmentCandidate
{
    [JsonProperty("type")]
    public string Type { get; set; } = "none";

    [JsonProperty("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonProperty("dueInDays")]
    public int DueInDays { get; set; }

    [JsonProperty("timeOfDay")]
    public int TimeOfDay { get; set; } = 900;

    [JsonProperty("location")]
    public string Location { get; set; } = string.Empty;

    [JsonProperty("locationLabel")]
    public string LocationLabel { get; set; } = string.Empty;
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
