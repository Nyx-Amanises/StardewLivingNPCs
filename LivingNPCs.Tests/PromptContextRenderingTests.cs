using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class PromptContextRenderingTests
{
    [Fact]
    public void EmptyDurableStoresCollapseIntoOneSummaryLine()
    {
        var state = new LivingNpcState { NpcName = "Emily" };

        var lines = BehaviorPromptContextBuilder.BuildDurableStoreLines(state, TestScenarios.Today);

        string summary = Assert.Single(lines);
        Assert.Contains("Nothing recorded yet for:", summary);
        Assert.Contains(PromptFragments.Context.StoreLabelLongTermMemories, summary);
        Assert.Contains(PromptFragments.Context.StoreLabelHelpRequests, summary);
        Assert.Contains(PromptFragments.Context.StoreLabelNickname, summary);
        Assert.Contains("do not invent", summary);
    }

    [Fact]
    public void PopulatedStoresKeepTheirOwnLinesAndLeaveTheSummaryToTheRest()
    {
        var state = new LivingNpcState
        {
            NpcName = "Emily",
            LastGiftName = "Coffee",
            LastGiftTaste = "liked"
        };
        state.LongTermMemories.Add(TestScenarios.Memory("The farmer promised to visit the library.", importance: 80));

        var lines = BehaviorPromptContextBuilder.BuildDurableStoreLines(state, TestScenarios.Today);

        Assert.Contains(lines, line => line.Contains("1 long-term memories tracked"));
        Assert.Contains(lines, line => line.Contains("Coffee"));
        string summary = Assert.Single(lines, line => line.Contains("Nothing recorded yet for:"));
        Assert.DoesNotContain(PromptFragments.Context.StoreLabelLongTermMemories, summary);
        Assert.DoesNotContain(PromptFragments.Context.StoreLabelGifts, summary);
        Assert.Contains(PromptFragments.Context.StoreLabelHelpRequests, summary);
        Assert.Contains(PromptFragments.Context.StoreLabelSharedExperiences, summary);
    }

    [Fact]
    public void ExpiredBehaviorTendencyCountsAsEmptyStore()
    {
        var state = new LivingNpcState { NpcName = "Emily" };
        state.DialogueBehaviorInfluences.Add(new DialogueBehaviorInfluenceFact
        {
            Type = "visit_location",
            Summary = "wants to see the beach",
            Status = "Active",
            Intensity = 50,
            ExpiresTotalDays = TestScenarios.Today - 1
        });

        var lines = BehaviorPromptContextBuilder.BuildDurableStoreLines(state, TestScenarios.Today);

        string summary = Assert.Single(lines);
        Assert.Contains(PromptFragments.Context.StoreLabelBehaviorTendencies, summary);
    }

    [Fact]
    public void GiftRestrictionSectionIsASingleLine()
    {
        string section = PromptFragments.GiftOpportunity.NoOpportunitySection();

        Assert.DoesNotContain("\n", section);
        Assert.Contains("give_small_gift", section);
        Assert.Contains("give_meaningful_gift", section);
        Assert.Contains("Gift Restriction", section);
    }
}
