using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for removing the "Town" last-resort from
/// <see cref="TravelLocationRules.Normalize"/>: a blank travel target must stay blank so the
/// downstream guards and fallbacks can fire — the companion-outing "a supported outing
/// destination is required" rejection, <see cref="DialogueBehaviorInfluenceStore.Store"/>'s
/// current-location fallback, and the festival anchor's current-map fallback. Call sites that
/// want a "Town" default (NormalizeForStore's legacy-data path) pass "Town" explicitly.
/// </summary>
public sealed class TravelLocationEmptyTargetTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("", "   ")]
    [InlineData("   ", "   ")]
    public void BlankValueWithBlankFallbackStaysBlank(string value, string fallback)
    {
        Assert.Equal(string.Empty, TravelLocationRules.Normalize(value, fallback));
    }

    [Fact]
    public void BlankValueUsesNonBlankFallback()
    {
        Assert.Equal("Saloon", TravelLocationRules.Normalize("", "Saloon"));

        // Explicit "Town" fallback call sites (DialogueBehaviorInfluenceStore.NormalizeForStore)
        // keep their semantics.
        Assert.Equal("Town", TravelLocationRules.Normalize("", "Town"));
    }

    [Fact]
    public void BlankLocationIsNotAPublicOutingTarget()
    {
        // Previously Normalize("", "") returned "Town", which made both of these true and let a
        // companion_outing action without a targetLocation slip past CompanionOutingRuntime's
        // destination guard and start an unintended outing to Town.
        Assert.False(TravelLocationRules.IsKnownPublicOutingTarget(string.Empty));
        Assert.False(TravelLocationRules.IsKnownPublicOutingTarget("   "));
    }

    [Fact]
    public void NonBlankValuesKeepTheirExistingSemantics()
    {
        Assert.Equal("Town", TravelLocationRules.Normalize("Pelican Town", string.Empty));
        Assert.Equal("Farm", TravelLocationRules.Normalize("农农场", string.Empty));
        Assert.Equal("UnknownPlace", TravelLocationRules.Normalize("UnknownPlace", string.Empty));
    }

    [Fact]
    public void ParserKeepsBlankInfluenceTargetBlank()
    {
        const string json = """
        {
          "behaviorInfluences": [
            { "type": "visit_location", "summary": "wants to meet the farmer there again", "durationDays": 2 }
          ]
        }
        """;

        var analysis = ValleyTalkExchangeParser.Parse(json);

        var influence = Assert.Single(analysis.BehaviorInfluences);
        Assert.Equal("visit_location", influence.Type);
        Assert.Equal(string.Empty, influence.TargetLocation);
    }

    [Fact]
    public void StoreFallsBackToCurrentLocationForBlankInfluenceTarget()
    {
        var state = new LivingNpcState { NpcName = "Penny" };
        var candidate = new ValleyTalkBehaviorInfluenceCandidate
        {
            Type = "visit_location",
            Summary = "wants to meet the farmer here again",
            TargetLocation = string.Empty
        };

        bool stored = DialogueBehaviorInfluenceStore.Store(
            state,
            candidate,
            fallbackLocation: "Saloon",
            maxDialogueBehaviorInfluenceDays: 3,
            currentTotalDays: TestScenarios.Today,
            currentTimeOfDay: 1200,
            out var storedInfluence);

        Assert.True(stored);
        Assert.NotNull(storedInfluence);

        // Before the fix the parser had already turned the blank target into "Town", so the
        // conversation's actual location passed here could never win.
        Assert.Equal("Saloon", storedInfluence!.TargetLocation);
    }

    [Fact]
    public void NormalizeForStoreKeepsExplicitTownDefaultForLegacyBlankData()
    {
        var influence = new DialogueBehaviorInfluenceFact
        {
            Type = "visit_location",
            Summary = "legacy influence with no location",
            TargetLocation = string.Empty,
            Status = "Active",
            ExpiresTotalDays = TestScenarios.Today + 2
        };

        var normalized = DialogueBehaviorInfluenceStore.NormalizeForStore(influence, TestScenarios.Today);

        Assert.Equal("Town", normalized.TargetLocation);
    }
}
