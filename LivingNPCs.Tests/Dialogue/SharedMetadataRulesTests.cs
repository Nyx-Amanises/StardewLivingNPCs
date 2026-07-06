using System.Linq;
using LivingNpcs.Shared;
using LivingNPCs.Dialogue.Engine;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

/// <summary>
/// Pins the shared metadata value domains on the ValleyTalk (sender) side, through the public
/// parse path. Both mods compile the same LivingNpcMetadataRules source, so these tests guard
/// against someone re-introducing a local normalizer that drifts from the shared rules — exactly
/// what had happened before the rules were unified (walk_together and companion_walk were passed
/// through by ValleyTalk but silently dropped by LivingNPCs).
/// </summary>
public sealed class SharedMetadataRulesTests
{
    [Fact]
    public void EscortActionFoldsIntoCompanionOutingAndWalkTogetherIsDropped()
    {
        var analysis = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"actions":[{"type":"escort_to_location","targetLocation":"Beach","travelConsent":"accepted_now"},{"type":"walk_together","targetLocation":"Town","travelConsent":"accepted_now"}]}
        """);

        var action = Assert.Single(analysis.Actions);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("Beach", action.TargetLocation);
    }

    [Fact]
    public void CompanionWalkInfluenceIsDropped()
    {
        var analysis = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"behaviorInfluences":[{"type":"companion_walk","summary":"wants to walk"},{"type":"stay_near","summary":"wants to stay close"}]}
        """);

        var influence = Assert.Single(analysis.BehaviorInfluences);
        Assert.Equal("stay_near", influence.Type);
    }

    [Fact]
    public void NumericFieldsAreClampedToTheSharedRanges()
    {
        var analysis = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"rapportDelta":99,"emotionImpact":{"emotion":"HAPPY","intensityDelta":500,"repairDelta":-5},"actions":[{"type":"give_money","amount":99999,"delayMinutes":999}]}
        """);

        Assert.Equal(LivingNpcMetadataRules.MaxRapportDelta, analysis.RapportDelta);
        Assert.Equal("Happy", analysis.EmotionImpact.Emotion);
        Assert.Equal(LivingNpcMetadataRules.MaxEmotionIntensityDelta, analysis.EmotionImpact.IntensityDelta);
        Assert.Equal(0, analysis.EmotionImpact.RepairDelta);
        var action = Assert.Single(analysis.Actions);
        Assert.Equal(LivingNpcMetadataRules.MaxMoneyAmount, action.Amount);
        Assert.Equal(LivingNpcMetadataRules.MaxActionDelayMinutes, action.DelayMinutes);
    }

    [Fact]
    public void ListCapsMatchTheSharedConstants()
    {
        string memories = string.Join(",", Enumerable.Range(0, 10)
            .Select(i => $$"""{"kind":"fact","summary":"memory {{i}}","importance":50}"""));
        var analysis = ConversationAnalysis.Parse($$"""
        !LIVINGNPCS_META {"memories":[{{memories}}]}
        """);

        Assert.Equal(LivingNpcMetadataRules.MaxMemories, analysis.Memories.Count);
    }

    [Fact]
    public void SharedConstantsKeepTheirContractValues()
    {
        // The bridge JSON contract depends on these exact values; changing one changes what the
        // other mod accepts, so a change here must be deliberate on both sides.
        Assert.Equal(4, LivingNpcMetadataRules.MaxMemories);
        Assert.Equal(1, LivingNpcMetadataRules.MaxWorldActions);
        Assert.Equal(2, LivingNpcMetadataRules.MaxBehaviorInfluences);
        Assert.Equal(1, LivingNpcMetadataRules.MaxHelpRequests);
        Assert.Equal(3, LivingNpcMetadataRules.MaxHelpRequestSteps);
        Assert.Equal(2, LivingNpcMetadataRules.MaxHelpRequestUpdates);
        Assert.Equal(2, LivingNpcMetadataRules.MaxConflicts);
        Assert.Equal(6, LivingNpcMetadataRules.MaxPreferenceTags);
        Assert.Equal(30, LivingNpcMetadataRules.MaxRapportDelta);
        Assert.Equal(250, LivingNpcMetadataRules.MaxMoneyAmount);
        Assert.Equal(600, LivingNpcMetadataRules.MaxOutingDurationMinutes);
        Assert.Equal(7, LivingNpcMetadataRules.MaxHelpRequestDueDays);
    }
}
