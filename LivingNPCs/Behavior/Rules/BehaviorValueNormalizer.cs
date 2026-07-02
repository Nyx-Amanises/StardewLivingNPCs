using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LivingNpcs.Shared;

namespace LivingNPCs.Behavior;

/// <summary>
/// Normalization for values stored by LivingNPCs. The value domains shared with the ValleyTalk
/// metadata bridge live in <see cref="LivingNpcMetadataRules"/> (compiled into both mods); the
/// members here either delegate to it or cover LivingNPCs-only concepts (community impressions,
/// shared experiences, keyword-inferred tags, dedup keys).
/// </summary>
internal static class BehaviorValueNormalizer
{
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
        return LivingNpcMetadataRules.NormalizeLongTermMemoryKind(kind);
    }

    public static string NormalizePlayerPreferenceKind(string kind)
    {
        return LivingNpcMetadataRules.NormalizePlayerPreferenceKind(kind);
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
        return LivingNpcMetadataRules.NormalizeWorldActionType(type);
    }

    public static string NormalizeTravelConsent(string consent)
    {
        return LivingNpcMetadataRules.NormalizeTravelConsent(consent);
    }

    public static string NormalizeDialogueBehaviorInfluenceType(string type)
    {
        return LivingNpcMetadataRules.NormalizeBehaviorInfluenceType(type);
    }

    public static string NormalizeEmotion(string emotion)
    {
        return LivingNpcMetadataRules.NormalizeEmotion(emotion);
    }

    public static string NormalizeConflictCauseKind(string causeKind)
    {
        return LivingNpcMetadataRules.NormalizeConflictCauseKind(causeKind);
    }

    public static string NormalizeHelpRequestType(string type)
    {
        return LivingNpcMetadataRules.NormalizeHelpRequestType(type);
    }

    public static string NormalizeHelpRequestUpdateStatus(string status)
    {
        return LivingNpcMetadataRules.NormalizeHelpRequestUpdateStatus(status);
    }

    public static string NormalizeHelpRequestFollowUpPotential(string value)
    {
        return LivingNpcMetadataRules.NormalizeHelpRequestFollowUpPotential(value);
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
        return LivingNpcMetadataRules.NormalizePlayerPreferenceTags(tags);
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
