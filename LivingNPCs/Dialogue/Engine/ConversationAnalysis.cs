using System;
using System.Collections.Generic;
using System.Linq;
using LivingNpcs.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

internal sealed class ConversationAnalysis
{
    public static ConversationAnalysis Empty => new();

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

    public bool HasWorldActionOrHelpMetadata => this.Actions.Count > 0
        || this.HelpRequests.Count > 0
        || this.HelpRequestUpdates.Count > 0;

    public bool MergeSupplementalActionMetadata(ConversationAnalysis supplemental)
    {
        if (supplemental == null)
        {
            return false;
        }

        bool changed = false;
        if (this.Actions.Count == 0 && supplemental.Actions.Count > 0)
        {
            this.Actions = supplemental.Actions;
            changed = true;
        }

        if (this.HelpRequests.Count == 0 && supplemental.HelpRequests.Count > 0)
        {
            this.HelpRequests = supplemental.HelpRequests;
            changed = true;
        }

        if (this.HelpRequestUpdates.Count == 0 && supplemental.HelpRequestUpdates.Count > 0)
        {
            this.HelpRequestUpdates = supplemental.HelpRequestUpdates;
            changed = true;
        }

        return changed;
    }

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
            LogParseFailure("no JSON object after marker", remainder);
            return Empty;
        }

        try
        {
            string jsonText = ExtractBalancedJsonObject(remainder[objectStart..]);
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                LogParseFailure("unbalanced JSON object after marker", remainder);
                return Empty;
            }

            var json = JObject.Parse(jsonText);
            var analysis = json.ToObject<ConversationAnalysis>() ?? new ConversationAnalysis();
            analysis.Memories ??= new List<ConversationMemoryCandidate>();
            analysis.Actions ??= new List<ConversationWorldActionRequest>();
            analysis.BehaviorInfluences ??= new List<ConversationBehaviorInfluenceCandidate>();
            analysis.HelpRequests ??= new List<ConversationHelpRequestCandidate>();
            analysis.HelpRequestUpdates ??= new List<ConversationHelpRequestUpdateCandidate>();
            analysis.Conflicts ??= new List<ConversationConflictCandidate>();
            analysis.RapportDelta = LivingNpcMetadataRules.ClampRapportDelta(analysis.RapportDelta);
            analysis.Memories = analysis.Memories
                .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
                .Select(memory =>
                {
                    memory.Kind = LivingNpcMetadataRules.NormalizeLongTermMemoryKind(memory.Kind);
                    memory.Summary = memory.Summary.Trim();
                    memory.Importance = LivingNpcMetadataRules.ClampImportance(memory.Importance);
                    memory.PlayerPreferenceKind = LivingNpcMetadataRules.NormalizePlayerPreferenceKind(memory.PlayerPreferenceKind);
                    memory.Subject = memory.Subject?.Trim() ?? string.Empty;
                    memory.Tags = LivingNpcMetadataRules.NormalizePlayerPreferenceTags(memory.Tags);
                    memory.PlayerPreference = memory.PlayerPreference && memory.PlayerPreferenceKind != "none";
                    return memory;
                })
                .Take(LivingNpcMetadataRules.MaxMemories)
                .ToList();
            analysis.AmbientFollowUp ??= new ConversationAmbientFollowUp();
            analysis.AmbientFollowUp.Text = analysis.AmbientFollowUp.Text?.Trim() ?? string.Empty;
            analysis.AmbientFollowUp.DelayMinutes = LivingNpcMetadataRules.ClampAmbientFollowUpDelayMinutes(analysis.AmbientFollowUp.DelayMinutes);
            analysis.EmotionImpact ??= new ConversationEmotionImpact();
            analysis.EmotionImpact.Emotion = LivingNpcMetadataRules.NormalizeEmotion(analysis.EmotionImpact.Emotion);
            analysis.EmotionImpact.IntensityDelta = LivingNpcMetadataRules.ClampEmotionIntensityDelta(analysis.EmotionImpact.IntensityDelta);
            analysis.EmotionImpact.RepairDelta = LivingNpcMetadataRules.ClampRepairDelta(analysis.EmotionImpact.RepairDelta);
            analysis.EmotionImpact.Reason = analysis.EmotionImpact.Reason?.Trim() ?? string.Empty;
            analysis.Actions = analysis.Actions
                .Where(action => action != null)
                .Select(action =>
                {
                    action.Type = LivingNpcMetadataRules.NormalizeWorldActionType(action.Type);
                    action.Reason = action.Reason?.Trim() ?? string.Empty;
                    action.Amount = LivingNpcMetadataRules.ClampMoneyAmount(action.Amount);
                    action.DurationMinutes = LivingNpcMetadataRules.NormalizeActionDurationMinutes(action.Type, action.DurationMinutes);
                    action.DelayMinutes = LivingNpcMetadataRules.ClampActionDelayMinutes(action.DelayMinutes);
                    action.TargetLocation = action.TargetLocation?.Trim() ?? string.Empty;
                    action.TravelConsent = LivingNpcMetadataRules.NormalizeTravelConsent(action.TravelConsent);
                    action.QuestHint = action.QuestHint?.Trim() ?? string.Empty;
                    action.ItemId = action.ItemId?.Trim() ?? string.Empty;
                    action.ItemLabel = action.ItemLabel?.Trim() ?? string.Empty;
                    return action;
                })
                .Where(action => action.Type != "none")
                .Take(LivingNpcMetadataRules.MaxWorldActions)
                .ToList();
            analysis.BehaviorInfluences = analysis.BehaviorInfluences
                .Where(influence => influence != null && !string.IsNullOrWhiteSpace(influence.Summary))
                .Select(influence =>
                {
                    influence.Type = LivingNpcMetadataRules.NormalizeBehaviorInfluenceType(influence.Type);
                    influence.Summary = influence.Summary.Trim();
                    influence.TargetLocation = influence.TargetLocation?.Trim() ?? string.Empty;
                    influence.TargetLocationLabel = influence.TargetLocationLabel?.Trim() ?? string.Empty;
                    influence.DurationDays = LivingNpcMetadataRules.ClampInfluenceDurationDays(influence.DurationDays);
                    influence.Intensity = LivingNpcMetadataRules.ClampInfluenceIntensity(influence.Intensity);
                    influence.MaxTriggers = LivingNpcMetadataRules.ClampInfluenceMaxTriggers(influence.MaxTriggers);
                    return influence;
                })
                .Where(influence => influence.Type != "none")
                .Take(LivingNpcMetadataRules.MaxBehaviorInfluences)
                .ToList();
            analysis.HelpRequests = analysis.HelpRequests
                .Where(request => request != null && !string.IsNullOrWhiteSpace(request.Summary))
                .Select(request =>
                {
                    request.Type = LivingNpcMetadataRules.NormalizeHelpRequestType(request.Type);
                    request.Summary = request.Summary.Trim();
                    request.RequestedItemId = request.RequestedItemId?.Trim() ?? string.Empty;
                    request.RequestedItemLabel = request.RequestedItemLabel?.Trim() ?? string.Empty;
                    request.QuestionTopic = request.QuestionTopic?.Trim() ?? string.Empty;
                    request.DueInDays = LivingNpcMetadataRules.ClampHelpRequestDueDays(request.DueInDays);
                    request.Reason = request.Reason?.Trim() ?? string.Empty;
                    request.FollowUpPotential = LivingNpcMetadataRules.NormalizeHelpRequestFollowUpPotential(request.FollowUpPotential);
                    request.Steps = (request.Steps ?? new List<ConversationHelpRequestStepCandidate>())
                        .Where(step => step != null)
                        .Select(step =>
                        {
                            step.Type = LivingNpcMetadataRules.NormalizeHelpRequestType(step.Type);
                            step.Summary = step.Summary?.Trim() ?? string.Empty;
                            step.RequestedItemId = step.RequestedItemId?.Trim() ?? string.Empty;
                            step.RequestedItemLabel = step.RequestedItemLabel?.Trim() ?? string.Empty;
                            step.QuestionTopic = step.QuestionTopic?.Trim() ?? string.Empty;
                            return step;
                        })
                        .Where(step => step.Type != "none" && !string.IsNullOrWhiteSpace(step.Summary))
                        .Take(LivingNpcMetadataRules.MaxHelpRequestSteps)
                        .ToList();
                    return request;
                })
                .Where(request => request.Type != "none")
                .Take(LivingNpcMetadataRules.MaxHelpRequests)
                .ToList();
            analysis.HelpRequestUpdates = analysis.HelpRequestUpdates
                .Where(update => update != null && !string.IsNullOrWhiteSpace(update.Summary))
                .Select(update =>
                {
                    update.Summary = update.Summary.Trim();
                    update.Status = LivingNpcMetadataRules.NormalizeHelpRequestUpdateStatus(update.Status);
                    update.Resolution = update.Resolution?.Trim() ?? string.Empty;
                    return update;
                })
                .Where(update => update.Status != "none")
                .Take(LivingNpcMetadataRules.MaxHelpRequestUpdates)
                .ToList();
            analysis.Conflicts = analysis.Conflicts
                .Where(conflict => conflict != null && !string.IsNullOrWhiteSpace(conflict.Summary))
                .Select(conflict =>
                {
                    conflict.CauseKind = LivingNpcMetadataRules.NormalizeConflictCauseKind(conflict.CauseKind);
                    conflict.Summary = conflict.Summary.Trim();
                    conflict.Severity = LivingNpcMetadataRules.ClampConflictSeverity(conflict.Severity);
                    return conflict;
                })
                .Where(conflict => conflict.Severity > 0)
                .Take(LivingNpcMetadataRules.MaxConflicts)
                .ToList();
            return analysis;
        }
        catch (Exception ex)
        {
            LogParseFailure($"JSON parse error: {ex.Message}", remainder);
            return Empty;
        }
    }

    /// <summary>
    /// The metadata marker was present but its payload could not be used. Without this log the
    /// failure is indistinguishable from "the model sent no metadata at all", which makes prompt
    /// regressions invisible. Trace level: always in the SMAPI log file, never console noise.
    /// SMonitor is null under unit tests, where staying silent is fine.
    /// </summary>
    private static void LogParseFailure(string reason, string snippet)
    {
        string cleaned = (snippet ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        if (cleaned.Length > 200)
        {
            cleaned = cleaned[..200] + "...";
        }

        DialogueServices.Monitor?.Log(
            $"LivingNPCs metadata block found but not parsed ({reason}). Snippet: {cleaned}",
            StardewModdingAPI.LogLevel.Trace);
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

    [JsonProperty("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonProperty("delayMinutes")]
    public int DelayMinutes { get; set; }

    [JsonProperty("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonProperty("targetLocation")]
    public string TargetLocation { get; set; } = string.Empty;

    [JsonProperty("travelConsent")]
    public string TravelConsent { get; set; } = string.Empty;

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
