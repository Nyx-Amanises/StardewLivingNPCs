using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class CommunityPropagationTests
{
    [Fact]
    public void PrivateImpressionCannotBeRetold()
    {
        var impression = Impression("Private");

        Assert.False(CommunityPropagationRules.CanRetell(impression, TestScenarios.Today));
    }

    [Fact]
    public void RetellingKeepsVisibilityButLowersSourceConfidenceAndAddsDistortion()
    {
        var impression = Impression("Personal");
        var reaction = new CommunityReactionCue(
            "Balanced",
            "balanced",
            "自然",
            38,
            "balanced retelling"
        );

        var retelling = CommunityPropagationRules.BuildRetelling(impression, reaction, "Haley");

        Assert.Equal("CloseCircle", retelling.Source);
        Assert.Equal("Personal", retelling.Visibility);
        Assert.True(retelling.Confidence < impression.Confidence);
        Assert.True(retelling.TransmissionDepth > impression.TransmissionDepth);
        Assert.True(retelling.DistortionLevel > impression.DistortionLevel);
        Assert.Equal(1, CommunityPropagationRules.GetDailyRetellingTargetLimit(reaction, impression));
    }

    [Fact]
    public void CommunitySourceConfidenceDegradesByDistanceFromOriginalEvent()
    {
        Assert.True(CommunityPropagationRules.GetInitialConfidence("Witnessed")
            > CommunityPropagationRules.GetInitialConfidence("CloseCircle"));
        Assert.True(CommunityPropagationRules.GetInitialConfidence("CloseCircle")
            > CommunityPropagationRules.GetInitialConfidence("PublicRumor"));
    }

    private static CommunityImpressionFact Impression(string visibility)
    {
        return new CommunityImpressionFact
        {
            SubjectNpcName = "Emily",
            SubjectDisplayName = "Emily",
            Kind = "relationship_trend",
            Summary = "The farmer and Emily have been talking warmly.",
            Source = "Witnessed",
            Visibility = visibility,
            Confidence = CommunityPropagationRules.GetInitialConfidence("Witnessed"),
            Importance = 80,
            TransmissionDepth = 0,
            DistortionLevel = 0,
            LastUpdatedTotalDays = TestScenarios.Today,
            LastUpdatedTimeOfDay = 1000,
            LastSharedTotalDays = TestScenarios.Today - 1,
            ExpiresTotalDays = TestScenarios.Today + 10
        };
    }
}
