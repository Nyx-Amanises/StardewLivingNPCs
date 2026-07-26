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
    public void FactsRenderRelativeTimeInsteadOfInternalDayNumbers()
    {
        int today = TestScenarios.Today;

        string dueHelp = PromptFragments.Facts.HelpRequest(
            new NpcHelpRequestFact
            {
                Type = "item_request",
                Summary = "Bring quartz.",
                Status = "Pending",
                DueTotalDays = today + 1,
                RequestedItemId = "(O)80",
                RequestedItemLabel = "Quartz"
            },
            today);
        Assert.Contains("due tomorrow", dueHelp);
        Assert.DoesNotContain("total day", dueHelp);

        string shared = PromptFragments.Facts.SharedExperience(
            new SharedExperienceFact
            {
                Type = "companion_outing",
                Summary = "walked to the beach together",
                LocationLabel = "Beach",
                CreatedTotalDays = today - 3
            },
            today);
        Assert.Contains("shared 3 days ago", shared);
        Assert.DoesNotContain("total day", shared);

        string tendency = PromptFragments.Facts.DialogueBehaviorInfluence(
            new DialogueBehaviorInfluenceFact
            {
                Type = "visit_location",
                Summary = "wants to visit the beach",
                TargetLocationLabel = "Beach",
                Intensity = 50,
                Status = "Active",
                ExpiresTotalDays = today + 2
            },
            today);
        Assert.Contains("expires in 2 days", tendency);
        Assert.DoesNotContain("total day", tendency);

        string fulfilled = PromptFragments.Facts.HelpRequestFulfilled(
            new NpcHelpRequestFact
            {
                Type = "item_request",
                Summary = "Bring quartz.",
                Status = "Fulfilled",
                FulfilledTotalDays = today - 1
            },
            today);
        Assert.Contains("fulfilled yesterday", fulfilled);
        Assert.DoesNotContain("total day", fulfilled);

        string resolved = PromptFragments.Facts.ConflictResolved(
            new NpcConflictFact
            {
                Summary = "argued about the fence",
                CauseKind = "dialogue",
                CreatedTotalDays = today - 2
            },
            today);
        Assert.Contains("from 2 days ago", resolved);
        Assert.DoesNotContain("total day", resolved);
    }

    [Fact]
    public void CommunityImpressionFactUsesParameterizedFreshness()
    {
        var memory = new CommunityImpressionFact
        {
            SubjectNpcName = "Emily",
            Summary = "seen chatting warmly with the farmer",
            Source = "Witnessed",
            Visibility = "Public",
            LastUpdatedTotalDays = TestScenarios.Today,
            ExpiresTotalDays = TestScenarios.Today + 10
        };

        string rendered = PromptFragments.Facts.CommunityImpression(memory, TestScenarios.Today);

        Assert.Contains("directly witnessed", rendered);
        Assert.Contains("fresh", rendered);
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
