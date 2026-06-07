namespace LivingNPCs.Behavior;

internal static class CommunityPropagationRules
{
    public static int GetInitialConfidence(string source)
    {
        return CommunityImpressionStore.NormalizeSource(source) switch
        {
            "Witnessed" => 95,
            "CloseCircle" => 68,
            _ => 42
        };
    }

    public static bool CanRetell(CommunityImpressionFact impression, int currentTotalDays)
    {
        if (CommunityImpressionStore.NormalizeVisibility(impression.Visibility) == "Private")
        {
            return false;
        }

        string freshnessStage = CommunityImpressionStore.GetFreshnessStage(impression, currentTotalDays);
        if (freshnessStage is "fading" or "expired"
            || impression.Confidence < 35
            || impression.TransmissionDepth >= 3)
        {
            return false;
        }

        return impression.LastSharedTotalDays < currentTotalDays;
    }

    public static int GetDailyRetellingTargetLimit(CommunityReactionCue reaction, CommunityImpressionFact impression)
    {
        if (CommunityImpressionStore.NormalizeVisibility(impression.Visibility) == "Personal")
        {
            return 1;
        }

        return reaction.SharePropensity >= 60 ? 2 : 1;
    }

    public static int GetRetellingDistortionGain(CommunityReactionCue reaction, CommunityImpressionFact impression)
    {
        int baseGain = CommunityImpressionStore.NormalizeSource(impression.Source) switch
        {
            "Witnessed" => 8,
            "CloseCircle" => 12,
            _ => 16
        };

        return reaction.Key switch
        {
            "Expressive" => baseGain + 6,
            "Curious" => baseGain + 4,
            "Reserved" => System.Math.Max(4, baseGain - 4),
            "Measured" => System.Math.Max(4, baseGain - 3),
            _ => baseGain
        };
    }

    public static RetoldCommunityImpression BuildRetelling(
        CommunityImpressionFact impression,
        CommunityReactionCue reaction,
        string subjectDisplayName)
    {
        int depth = System.Math.Min(8, impression.TransmissionDepth + 1);
        int distortion = System.Math.Min(
            100,
            impression.DistortionLevel + GetRetellingDistortionGain(reaction, impression)
        );

        return new RetoldCommunityImpression(
            BuildRetoldSummary(subjectDisplayName, impression, depth, distortion),
            depth,
            distortion,
            "CloseCircle",
            CommunityImpressionStore.NormalizeVisibility(impression.Visibility),
            GetInitialConfidence("CloseCircle"),
            System.Math.Max(24, impression.Importance - 8)
        );
    }

    public static string BuildRetoldSummary(
        string subjectDisplayName,
        CommunityImpressionFact impression,
        int depth,
        int distortion)
    {
        string displayName = string.IsNullOrWhiteSpace(subjectDisplayName)
            ? impression.SubjectDisplayName
            : subjectDisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = impression.SubjectNpcName;
        }

        if (depth <= 1 && distortion < 20)
        {
            return impression.Summary;
        }

        return CommunityImpressionStore.NormalizeKind(impression.Kind) switch
        {
            "relationship_trend" when depth >= 3 || distortion >= 35 =>
                $"people have noticed the farmer and {displayName} talking more lately",
            "relationship_trend" =>
                $"the farmer seems to have been spending more time with {displayName} lately",
            "helped" when depth >= 3 || distortion >= 35 =>
                $"someone said the farmer did {displayName} a favor recently",
            "helped" =>
                $"the farmer may have helped {displayName} with something recently",
            "shared_experience" when depth >= 3 || distortion >= 35 =>
                $"the farmer and {displayName} seem to have had some sort of plan together recently",
            "shared_experience" =>
                $"the farmer and {displayName} seem to have spent some time together recently",
            _ =>
                $"there has been a little talk lately involving the farmer and {displayName}"
        };
    }

}

internal sealed record RetoldCommunityImpression(
    string Summary,
    int TransmissionDepth,
    int DistortionLevel,
    string Source,
    string Visibility,
    int Confidence,
    int Importance
);
