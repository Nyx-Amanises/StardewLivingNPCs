using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LivingNPCs.Behavior;

internal static class BehaviorValueNormalizer
{
    private static readonly HashSet<string> AllowedPlayerPreferenceTags = new(System.StringComparer.OrdinalIgnoreCase)
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

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> MemoryKeywordTags =
        new Dictionary<string, IReadOnlyCollection<string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["farm"] = ["farming", "work", "nature"],
            ["农场"] = ["farming", "work", "nature"],
            ["crop"] = ["farming", "work"],
            ["作物"] = ["farming", "work"],
            ["mine"] = ["mining", "adventurous", "mineral"],
            ["mines"] = ["mining", "adventurous", "mineral"],
            ["矿"] = ["mining", "adventurous", "mineral"],
            ["fish"] = ["fishing", "nature"],
            ["fishing"] = ["fishing", "nature"],
            ["钓鱼"] = ["fishing", "nature"],
            ["beach"] = ["fishing", "nature"],
            ["海边"] = ["fishing", "nature"],
            ["海滩"] = ["fishing", "nature"],
            ["flower"] = ["flower", "nature", "artistic"],
            ["花"] = ["flower", "nature", "artistic"],
            ["food"] = ["food", "comfort"],
            ["吃"] = ["food", "comfort"],
            ["料理"] = ["food", "comfort"],
            ["coffee"] = ["drink", "comfort", "work"],
            ["咖啡"] = ["drink", "comfort", "work"],
            ["book"] = ["scholarly"],
            ["library"] = ["scholarly"],
            ["书"] = ["scholarly"],
            ["图书馆"] = ["scholarly"],
            ["magic"] = ["magical"],
            ["魔法"] = ["magical"],
            ["art"] = ["artistic"],
            ["画"] = ["artistic"],
            ["艺术"] = ["artistic"],
            ["morning"] = ["morning"],
            ["早"] = ["morning"],
            ["night"] = ["night"],
            ["晚"] = ["night"]
        };

    public static string NormalizeLongTermMemoryKind(string kind)
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

    public static string NormalizePlayerPreferenceKind(string kind)
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

    public static string NormalizeCommunityImpressionKind(string kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            "helped" => "helped",
            "shared_experience" => "shared_experience",
            "relationship_trend" => "relationship_trend",
            "romantic_attention" => "romantic_attention",
            _ => "community_fact"
        };
    }

    public static string NormalizeCommunityImpressionSource(string source)
    {
        return source?.Trim() switch
        {
            "Witnessed" => "Witnessed",
            "CloseCircle" => "CloseCircle",
            "Heard" => "CloseCircle",
            "PublicRumor" => "PublicRumor",
            _ => "PublicRumor"
        };
    }

    public static string NormalizeCommunityImpressionVisibility(string visibility)
    {
        return visibility?.Trim() switch
        {
            "Private" => "Private",
            "Personal" => "Personal",
            _ => "Public"
        };
    }

    public static string NormalizeWorldActionType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "give_small_gift" => "give_small_gift",
            "give_meaningful_gift" => "give_meaningful_gift",
            "give_money" => "give_money",
            "water_nearby_crops" => "water_nearby_crops",
            "companion_outing" => "companion_outing",
            "escort_to_location" => "companion_outing",
            "festival_interaction" => "festival_interaction",
            "assist_quest" => "assist_quest",
            _ => "none"
        };
    }

    public static string NormalizeTravelConsent(string consent)
    {
        return consent?.Trim().ToLowerInvariant() switch
        {
            "accepted_now" => "accepted_now",
            "accepted_later" => "accepted_later",
            "deferred" => "accepted_later",
            "declined" => "declined",
            "rejected" => "declined",
            "tentative" => "tentative",
            "maybe" => "tentative",
            "none" => "none",
            _ => string.Empty
        };
    }

    public static string NormalizeDialogueBehaviorInfluenceType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
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

    public static string NormalizeEmotion(string emotion)
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

    public static string NormalizeConflictCauseKind(string causeKind)
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

    public static string NormalizeHelpRequestType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "item_request" => "item_request",
            "question_request" => "question_request",
            _ => "none"
        };
    }

    public static string NormalizeHelpRequestUpdateStatus(string status)
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

    public static string NormalizeHelpRequestFollowUpPotential(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "deeper_relationship" => "deeper_relationship",
            _ => "none"
        };
    }

    public static string NormalizeSharedExperienceType(string type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "help_request" => "help_request",
            "companion_outing" => "companion_outing",
            _ => "none"
        };
    }

    public static string NormalizeMemorySummary(string summary)
    {
        return Regex.Replace(summary ?? string.Empty, @"\s+", " ").Trim().ToLowerInvariant();
    }

    public static List<string> NormalizePlayerPreferenceTags(IEnumerable<string>? tags)
    {
        return tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => AllowedPlayerPreferenceTags.Contains(tag))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList()
            ?? new List<string>();
    }

    public static List<string> NormalizeMemoryTags(IEnumerable<string>? tags, params string?[] texts)
    {
        var allTags = new List<string>();
        if (tags != null)
        {
            allTags.AddRange(tags);
        }

        foreach (string? text in texts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var pair in MemoryKeywordTags)
            {
                if (text.Contains(pair.Key, System.StringComparison.OrdinalIgnoreCase))
                {
                    allTags.AddRange(pair.Value);
                }
            }
        }

        return NormalizePlayerPreferenceTags(allTags);
    }

    public static void AddInferredTags(string text, ISet<string> tags)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var pair in MemoryKeywordTags)
        {
            if (!text.Contains(pair.Key, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (string tag in pair.Value)
            {
                tags.Add(tag);
            }
        }
    }

    public static string NormalizePlayerPreferenceKey(ValleyTalkMemoryCandidate candidate)
    {
        return BuildPlayerPreferenceKey(candidate.PlayerPreferenceKind, candidate.Subject, candidate.Summary);
    }

    public static string BuildPlayerPreferenceKey(string kind, string subject, string summary)
    {
        string normalizedKind = NormalizePlayerPreferenceKind(kind);
        string normalizedSubject = NormalizeMemorySummary(subject);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string identity = string.IsNullOrWhiteSpace(normalizedSubject)
            ? normalizedSummary
            : normalizedSubject;
        return normalizedKind == "none" || string.IsNullOrWhiteSpace(identity)
            ? string.Empty
            : $"{normalizedKind}:{identity}";
    }

    public static string BuildLongTermMemoryKey(string kind, string subject, string summary)
    {
        string normalizedKind = NormalizeLongTermMemoryKind(kind);
        string normalizedSubject = NormalizeMemorySummary(subject);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string identity = string.IsNullOrWhiteSpace(normalizedSubject)
            ? normalizedSummary
            : normalizedSubject;
        return string.IsNullOrWhiteSpace(identity)
            ? string.Empty
            : $"{normalizedKind}:{identity}";
    }

    public static string BuildCommunityImpressionKey(string subjectNpcName, string kind, string summary)
    {
        string normalizedSubject = NormalizeMemorySummary(subjectNpcName);
        string normalizedKind = NormalizeCommunityImpressionKind(kind);
        string normalizedSummary = NormalizeMemorySummary(summary);
        return string.IsNullOrWhiteSpace(normalizedSubject) || string.IsNullOrWhiteSpace(normalizedSummary)
            ? string.Empty
            : $"{normalizedSubject}:{normalizedKind}:{normalizedSummary}";
    }

    public static string BuildDialogueBehaviorInfluenceKey(string type, string summary, string targetLocation)
    {
        string normalizedType = NormalizeDialogueBehaviorInfluenceType(type);
        string normalizedSummary = NormalizeMemorySummary(summary);
        string normalizedLocation = TravelLocationRules.Normalize(targetLocation, string.Empty);
        return normalizedType == "none" || string.IsNullOrWhiteSpace(normalizedSummary)
            ? string.Empty
            : $"{normalizedType}:{normalizedSummary}:{normalizedLocation}";
    }
}
