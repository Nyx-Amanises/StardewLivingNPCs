using LivingNPCs.Behavior;
using StardewValley;

namespace LivingNPCs.Tests;

public sealed class HistoricalContextRecallTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentInputRecallsOlderFactsOutsideTheFormerTopFour(bool concise)
    {
        var state = HistoricalState();
        var experience = state.SharedExperiences[^1];
        var request = state.HelpRequests[^1];
        var conflict = state.Conflicts[^1];
        experience.Summary = "We repaired the telescope together.";
        request.Summary = "Delivered a lens for the telescope.";
        conflict.Summary = "The telescope misunderstanding was resolved.";

        string prompt = BuildPrompt(state, "telescope", concise);

        AssertOccursOnce(experience.Summary, prompt);
        AssertOccursOnce(request.Summary, prompt);
        AssertOccursOnce(conflict.Summary, prompt);
        Assert.DoesNotContain(state.SharedExperiences[0].Summary, prompt);
        Assert.DoesNotContain(state.HelpRequests[0].Summary, prompt);
        Assert.DoesNotContain(state.Conflicts[0].Summary, prompt);
        Assert.Contains("status Fulfilled", prompt);
        Assert.Contains("resolved conflict", prompt);
        Assert.DoesNotContain("Active help request:", prompt);
        Assert.Equal(6, state.SharedExperiences.Count);
        Assert.Equal(6, state.HelpRequests.Count);
        Assert.Equal(6, state.Conflicts.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("qxvz9842")]
    public void EmptyOrUnmatchedInputKeepsTwoFactsInTheExistingStoreOrder(string? query)
    {
        var state = HistoricalState();

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, query);

        Assert.Equal(state.GetTopSharedExperiences(2), plan.SharedExperiences);
        Assert.Equal(state.GetTopHelpRequests(2), plan.HelpRequests);
        Assert.Equal(state.GetTopConflicts(2), plan.Conflicts);
    }

    [Fact]
    public void MatchingHistoryIsBoundedAndEqualScoresKeepTheOriginalOrder()
    {
        var state = HistoricalState();
        foreach (var fact in state.SharedExperiences) fact.Summary += " telescope";
        foreach (var fact in state.HelpRequests) fact.Summary += " telescope";
        foreach (var fact in state.Conflicts) fact.Summary += " telescope";

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Equal(state.GetTopSharedExperiences(2), plan.SharedExperiences);
        Assert.Equal(state.GetTopHelpRequests(2), plan.HelpRequests);
        Assert.Equal(state.GetTopConflicts(2), plan.Conflicts);
    }

    [Fact]
    public void CurrentObligationsBeyondFourSurviveAnUnrelatedQueryAndRenderOnce()
    {
        var state = ActiveState();
        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        string prompt = string.Join("\n", BehaviorPromptContextBuilder.BuildDurableStoreLines(
                state, TestScenarios.Today, priorityCuesProvided: true, plan)
            .Concat(BehaviorPromptContextBuilder.BuildPriorityPromptContext(
                Npc(), state, TestScenarios.World(), MemoryRecallPlan.Empty,
                Array.Empty<CommunityImpressionSelection>(), Expression(), TestScenarios.Today, plan)));

        Assert.Equal(6, plan.BehaviorInfluences.Count);
        Assert.Equal(6, plan.HelpRequests.Count);
        Assert.Equal(6, plan.Conflicts.Count);
        foreach (var fact in state.DialogueBehaviorInfluences) AssertOccursOnce(fact.Summary, prompt);
        foreach (var fact in state.HelpRequests) AssertOccursOnce(fact.Summary, prompt);
        foreach (var fact in state.Conflicts) AssertOccursOnce(fact.Summary, prompt);
        Assert.Contains("status Offered", prompt);
        Assert.Contains("status Pending", prompt);
        Assert.Contains("repair stage NeedsGesture", prompt);
        Assert.Contains("pleasant line alone cannot erase serious hurt", prompt);
    }

    [Fact]
    public void ExpiredAndSpentBehaviorInfluencesAreNotReintroducedByTopicMatches()
    {
        var state = TestScenarios.TrustedState();
        state.DialogueBehaviorInfluences.AddRange(new[]
        {
            new DialogueBehaviorInfluenceFact
            {
                Summary = "telescope expired", Status = "Active", ExpiresTotalDays = TestScenarios.Today - 1
            },
            new DialogueBehaviorInfluenceFact
            {
                Summary = "telescope spent", Status = "Spent", ExpiresTotalDays = TestScenarios.Today + 1
            },
            new DialogueBehaviorInfluenceFact
            {
                Summary = "telescope used", Status = "Active", ExpiresTotalDays = TestScenarios.Today + 1,
                TriggerCount = 1, MaxTriggers = 1
            }
        });

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Empty(plan.BehaviorInfluences);
        Assert.Equal(3, state.DialogueBehaviorInfluences.Count);
    }

    [Fact]
    public void ActiveMultiStepRequestKeepsTheCurrentItemAndLifecycleWithoutTopicOverlap()
    {
        var state = TestScenarios.TrustedState();
        var request = new NpcHelpRequestFact
        {
            Summary = "Bring supplies in the agreed order.", Status = "Pending", CurrentStepIndex = 1,
            DueTotalDays = TestScenarios.Today + 1,
            Steps = new List<NpcHelpRequestStepFact>
            {
                new() { Summary = "The completed mineral.", RequestedItemLabel = "Quartz", RequestedItemId = "(O)80", Status = "Fulfilled" },
                new() { Summary = "The remaining timber.", RequestedItemLabel = "Wood", RequestedItemId = "(O)388", Status = "Pending" }
            }
        };
        state.HelpRequests.Add(request);
        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        string prompt = string.Join("\n", BehaviorPromptContextBuilder.BuildPriorityPromptContext(
            Npc(), state, TestScenarios.World(), MemoryRecallPlan.Empty,
            Array.Empty<CommunityImpressionSelection>(), Expression(), TestScenarios.Today, plan));

        Assert.Same(request, Assert.Single(plan.HelpRequests));
        Assert.Contains("step 2/2", prompt);
        Assert.Contains("The remaining timber.", prompt);
        Assert.Contains("Wood (O)388", prompt);
        Assert.Contains("due tomorrow", prompt);
        Assert.Contains("the farmer accepted this ask", prompt);
        Assert.Equal(2, request.Steps.Count);
        Assert.Equal("Fulfilled", request.Steps[0].Status);
    }

    [Fact]
    public void HistorySearchIncludesLocationsAndCompletedStepsButKeepsTheStoredFactsIntact()
    {
        var state = HistoricalState();
        var experience = state.SharedExperiences[^1];
        experience.LocationName = "Observatory";
        experience.LocationLabel = "Observatory";
        var request = state.HelpRequests[^1];
        request.Steps.Add(new NpcHelpRequestStepFact
        {
            Summary = "Delivered to the Observatory.", RequestedItemLabel = "Quartz",
            RequestedItemId = "(O)80", Status = "Fulfilled"
        });

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "Observatory");

        Assert.Same(experience, Assert.Single(plan.SharedExperiences));
        Assert.Same(request, Assert.Single(plan.HelpRequests));
        Assert.Equal("Fulfilled", request.Status);
        Assert.Equal("(O)80", request.Steps[0].RequestedItemId);
    }

    [Fact]
    public void QueuedUnclaimedRewardRemainsEvenWhenItsCompletedRequestIsUnrelated()
    {
        var state = HistoricalState();
        var owed = state.HelpRequests[^1];
        owed.RewardMoney = 200;
        owed.RewardMoneyClaimQueued = true;
        state.HelpRequests[0].Summary = "An unrelated telescope errand.";

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Contains(owed, plan.HelpRequests);
        Assert.Contains(state.HelpRequests[0], plan.HelpRequests);
        Assert.Equal(2, plan.HelpRequests.Count);
        Assert.Null(plan.ActiveHelpRequest);
        Assert.False(owed.RewardMoneyGranted);
    }

    [Fact]
    public void DueContinuityCuesRemainPinnedAndOnlyTheRenderedCueObjectsAreMarked()
    {
        var state = DueCueState();
        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "qxvz9842");

        string prompt = BuildPrompt(state, "qxvz9842");

        foreach (string summary in new[]
        {
            plan.RecentlyFulfilledHelpRequest!.Summary, plan.ExpiredHelpRequest!.Summary,
            plan.SharedExperienceFollowUp!.Summary, plan.RecentlyResolvedConflict!.Summary
        }) AssertOccursOnce(summary, prompt);
        Assert.Equal(TestScenarios.Today, plan.RecentlyFulfilledHelpRequest.LastMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, plan.ExpiredHelpRequest.LastMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, plan.SharedExperienceFollowUp.FollowUpShownTotalDays);
        Assert.Equal(TestScenarios.Today, plan.RecentlyResolvedConflict.RecoveryMentionedTotalDays);
        Assert.Equal(-1, state.HelpRequests[1].LastMentionedTotalDays);
        Assert.Equal(-1, state.HelpRequests[3].LastMentionedTotalDays);
        Assert.Equal(-1, state.SharedExperiences[1].FollowUpShownTotalDays);
        Assert.Equal(-1, state.Conflicts[1].RecoveryMentionedTotalDays);
    }

    [Fact]
    public void MatchingPinnedCueDoesNotBringAlongUnrelatedOptionalHistory()
    {
        var state = HistoricalState();
        var due = state.SharedExperiences[^1];
        due.Summary = "We repaired the telescope.";
        due.LastUpdatedTotalDays = TestScenarios.Today - 1;
        due.FollowUpEligibleTotalDays = TestScenarios.Today;
        due.FollowUpShownTotalDays = -1;

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Same(due, Assert.Single(plan.SharedExperiences));
        Assert.Same(due, plan.SharedExperienceFollowUp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservationalAndConciseRenderingDoNotConsumeOneShotCues(bool concise)
    {
        var state = DueCueState();

        string first = BuildPrompt(state, "qxvz9842", concise, markFollowUpCues: false);
        string second = BuildPrompt(state, "qxvz9842", concise, markFollowUpCues: false);

        Assert.Equal(first, second);
        Assert.All(state.HelpRequests, request => Assert.Equal(-1, request.LastMentionedTotalDays));
        Assert.All(state.SharedExperiences, experience => Assert.Equal(-1, experience.FollowUpShownTotalDays));
        Assert.All(state.Conflicts, conflict => Assert.Equal(-1, conflict.RecoveryMentionedTotalDays));
        Assert.Contains(state.SharedExperiences[0].Summary, first);
        Assert.Contains(state.HelpRequests[0].Summary, first);
        Assert.Contains(state.Conflicts[0].Summary, first);
    }

    [Fact]
    public void ConciseRenderingKeepsEveryActiveBehaviorAndConflict()
    {
        var state = ActiveState();
        // Item-candidate construction depends on the game; request detail retention is exercised
        // separately through the durable/priority renderers above.
        state.HelpRequests.Clear();

        string prompt = BuildPrompt(state, "telescope", concise: true);

        foreach (var fact in state.DialogueBehaviorInfluences) AssertOccursOnce(fact.Summary, prompt);
        foreach (var fact in state.Conflicts) AssertOccursOnce(fact.Summary, prompt);
    }

    [Fact]
    public void SelectionDoesNotRewriteStoresOrAdvanceOtherRecallCounters()
    {
        var state = HistoricalState();
        var experiences = state.SharedExperiences.ToArray();
        var requests = state.HelpRequests.ToArray();
        var conflicts = state.Conflicts.ToArray();
        var memory = TestScenarios.Memory("The farmer returned a library book.");
        memory.RecallCount = 3;
        state.LongTermMemories.Add(memory);
        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact { Summary = "Quiet mornings.", RecallCount = 4 });
        state.CommunityImpressions.Add(new CommunityImpressionFact { Summary = "A neighbor waved.", RecallCount = 5 });

        _ = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Equal(experiences, state.SharedExperiences);
        Assert.Equal(requests, state.HelpRequests);
        Assert.Equal(conflicts, state.Conflicts);
        Assert.Equal(3, memory.RecallCount);
        Assert.Equal(4, state.PlayerPreferenceMemories[0].RecallCount);
        Assert.Equal(5, state.CommunityImpressions[0].RecallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockedHighRankedHistoryCannotCrowdOutAllowedMatches(bool concise)
    {
        var state = HistoricalState();
        for (int i = 0; i < 2; i++)
        {
            state.SharedExperiences[i].Summary = $"Torts telescope history {i}.";
            state.HelpRequests[i].Summary = $"Torts telescope request {i}.";
            state.Conflicts[i].Summary = $"Torts telescope disagreement {i}.";
        }
        state.SharedExperiences[^1].Summary = "An allowed telescope outing.";
        state.HelpRequests[^1].Summary = "An allowed telescope errand.";
        state.Conflicts[^1].Summary = "An allowed telescope repair.";

        string prompt = BuildPrompt(state, "telescope", concise);

        Assert.False(RsvAiPolicy.ContainsBlockedReference(prompt));
        Assert.Contains(state.SharedExperiences[^1].Summary, prompt);
        Assert.Contains(state.HelpRequests[^1].Summary, prompt);
        Assert.Contains(state.Conflicts[^1].Summary, prompt);
        Assert.Equal(6, state.SharedExperiences.Count);
        Assert.Equal(6, state.HelpRequests.Count);
        Assert.Equal(6, state.Conflicts.Count);
    }

    [Theory]
    [InlineData(nameof(NpcHelpRequestStepFact.Type))]
    [InlineData(nameof(NpcHelpRequestStepFact.Summary))]
    [InlineData(nameof(NpcHelpRequestStepFact.RequestedItemId))]
    [InlineData(nameof(NpcHelpRequestStepFact.RequestedItemLabel))]
    [InlineData(nameof(NpcHelpRequestStepFact.QuestionTopic))]
    [InlineData(nameof(NpcHelpRequestStepFact.Status))]
    [InlineData(nameof(NpcHelpRequestStepFact.Resolution))]
    public void BlockedContentInAnyStoredStepExcludesTheWholeRequestBeforeSelection(string field)
    {
        var state = HistoricalState();
        var blocked = state.HelpRequests[0];
        blocked.Summary = "The telescope item request.";
        blocked.Steps.Add(new NpcHelpRequestStepFact { Summary = "The earlier step.", Status = "Fulfilled" });
        blocked.Steps.Add(new NpcHelpRequestStepFact { Summary = "The final step.", Status = "Fulfilled" });
        typeof(NpcHelpRequestStepFact).GetProperty(field)!.SetValue(blocked.Steps[0], "Torts");
        blocked.CurrentStepIndex = 1;
        state.HelpRequests[^1].Summary = "The allowed telescope request.";

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Same(state.HelpRequests[^1], Assert.Single(plan.HelpRequests));
        Assert.Equal(2, blocked.Steps.Count);
    }

    [Fact]
    public void BlockedDueCuesAreNeitherRenderedNorConsumedAndAllowedNeighborsSurvive()
    {
        var state = DueCueState();
        state.HelpRequests[0].FollowUpPotential = "Torts follow-up";
        state.HelpRequests[2].FailureReaction = "Torts noticed";
        state.SharedExperiences[0].LocationName = "Ridge";
        state.Conflicts[0].Summary = "Torts disagreement";
        state.Conflicts.Add(new NpcConflictFact
        {
            Summary = "Torts active disagreement", Status = "Active", RequiresComplexRepair = true, Severity = 80
        });
        state.DialogueBehaviorInfluences.Add(new DialogueBehaviorInfluenceFact
        {
            Summary = "telescope tendency", Status = "Active", TargetLocation = "Ridge",
            ExpiresTotalDays = TestScenarios.Today + 1
        });

        string prompt = BuildPrompt(state, "Torts telescope");

        Assert.False(RsvAiPolicy.ContainsBlockedReference(prompt));
        Assert.DoesNotContain("Complex conflict repair:", prompt);
        Assert.DoesNotContain("telescope tendency", prompt);
        Assert.Equal(-1, state.HelpRequests[0].LastMentionedTotalDays);
        Assert.Equal(-1, state.HelpRequests[2].LastMentionedTotalDays);
        Assert.Equal(-1, state.SharedExperiences[0].FollowUpShownTotalDays);
        Assert.Equal(-1, state.Conflicts[0].RecoveryMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, state.HelpRequests[1].LastMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, state.HelpRequests[3].LastMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, state.SharedExperiences[1].FollowUpShownTotalDays);
        Assert.Equal(TestScenarios.Today, state.Conflicts[1].RecoveryMentionedTotalDays);
        Assert.Contains(state.HelpRequests[1].Summary, prompt);
        Assert.Contains(state.HelpRequests[3].Summary, prompt);
        Assert.Contains(state.SharedExperiences[1].Summary, prompt);
        Assert.Contains(state.Conflicts[1].Summary, prompt);
    }

    [Fact]
    public void SuppressedFactsDoNotPretendTheUnderlyingStoreIsEmpty()
    {
        var state = TestScenarios.TrustedState();
        state.SharedExperiences.Add(new SharedExperienceFact { Summary = "A Torts outing." });

        string prompt = string.Join("\n", BehaviorPromptContextBuilder.BuildDurableStoreLines(state, TestScenarios.Today));

        Assert.DoesNotContain(PromptFragments.Context.StoreLabelSharedExperiences, prompt);
        Assert.False(RsvAiPolicy.ContainsBlockedReference(prompt));
        Assert.Single(state.SharedExperiences);
    }

    [Fact]
    public void BlockedNpcCannotProduceAHistoricalRecallPlan()
    {
        var state = HistoricalState();
        state.NpcName = "Torts";

        var plan = HistoricalContextRecallPlan.Build(state, TestScenarios.Today, "telescope");

        Assert.Same(HistoricalContextRecallPlan.Empty, plan);
    }

    private static LivingNpcState HistoricalState()
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 6; i++)
        {
            state.SharedExperiences.Add(new SharedExperienceFact
            {
                Summary = $"Past experience number {i}.", Type = "companion_outing", LocationLabel = "Town",
                Importance = 80 - i, CreatedTotalDays = TestScenarios.Today - 20 - i,
                LastUpdatedTotalDays = TestScenarios.Today - 20 - i, FollowUpShownTotalDays = TestScenarios.Today - 10
            });
            state.HelpRequests.Add(new NpcHelpRequestFact
            {
                Summary = $"Past errand number {i}.", Status = "Fulfilled", DueTotalDays = TestScenarios.Today - 20 + i,
                FulfilledTotalDays = TestScenarios.Today - 10 - i, LastMentionedTotalDays = TestScenarios.Today - 5
            });
            state.Conflicts.Add(new NpcConflictFact
            {
                Summary = $"Past disagreement number {i}.", Status = "Resolved", RepairStage = "Resolved",
                LastUpdatedTotalDays = TestScenarios.Today - 20 - i, ResolvedTotalDays = TestScenarios.Today - 20 - i,
                RecoveryMentionedTotalDays = TestScenarios.Today - 10
            });
        }
        return state;
    }

    private static LivingNpcState ActiveState()
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 6; i++)
        {
            state.DialogueBehaviorInfluences.Add(new DialogueBehaviorInfluenceFact
            {
                Summary = $"Current influence number {i}.", Type = "give_space", TargetLocationLabel = "Town",
                Status = "Active", ExpiresTotalDays = TestScenarios.Today + 1
            });
            state.HelpRequests.Add(new NpcHelpRequestFact
            {
                Summary = $"Current errand number {i}.", Status = i % 2 == 0 ? "Offered" : "Pending",
                RequestedItemLabel = $"Supply{i}", RequestedItemId = $"(O){388 + i}", DueTotalDays = TestScenarios.Today + 1
            });
            state.Conflicts.Add(new NpcConflictFact
            {
                Summary = $"Current disagreement number {i}.", Status = i % 2 == 0 ? "Active" : "Recovering",
                Severity = 30 + i, RequiresComplexRepair = true, RepairStage = "NeedsGesture"
            });
        }
        return state;
    }

    private static LivingNpcState DueCueState()
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 2; i++)
        {
            state.HelpRequests.Add(new NpcHelpRequestFact
            {
                Summary = $"Recently completed errand {i}.", Status = "Fulfilled",
                FulfilledTotalDays = TestScenarios.Today - 1, DueTotalDays = TestScenarios.Today - 1
            });
            state.SharedExperiences.Add(new SharedExperienceFact
            {
                Summary = $"Recent shared outing {i}.", Type = "companion_outing", LocationLabel = "Beach",
                CreatedTotalDays = TestScenarios.Today - 1, LastUpdatedTotalDays = TestScenarios.Today - 1,
                FollowUpEligibleTotalDays = TestScenarios.Today
            });
            state.Conflicts.Add(new NpcConflictFact
            {
                Summary = $"Recently resolved disagreement {i}.", Status = "Resolved", RepairStage = "Resolved",
                ResolvedTotalDays = TestScenarios.Today - 1
            });
        }
        for (int i = 0; i < 2; i++)
        {
            state.HelpRequests.Add(new NpcHelpRequestFact
            {
                Summary = $"Recently expired errand {i}.", Status = "Expired", DueTotalDays = TestScenarios.Today - 1
            });
        }
        return state;
    }

    private static string BuildPrompt(
        LivingNpcState state, string? query, bool concise = false, bool markFollowUpCues = true)
    {
        var disposition = new NpcDispositionProfile("careful", "debug", 0, 0, 16, "reason");
        if (concise)
        {
            return BehaviorPromptContextBuilder.BuildConcisePromptContext(
                Npc(), Array.Empty<BehaviorMemoryEntry>(), state, TestScenarios.World(), disposition,
                Expression(), MemoryRecallPlan.Empty, HistoricalContextRecallPlan.Build(state, TestScenarios.Today, query),
                maxPendingHelpRequestsPerNpc: 0, helpRequestCooldownDays: 3, currentTotalDays: TestScenarios.Today);
        }
        return BehaviorPromptContextBuilder.BuildPromptContext(
            Npc(), Array.Empty<BehaviorMemoryEntry>(), state, TestScenarios.World(), disposition,
            Expression(), MemoryRecallPlan.Empty, Array.Empty<CommunityImpressionSelection>(),
            maxPendingHelpRequestsPerNpc: 0, helpRequestCooldownDays: 3,
            currentTotalDays: TestScenarios.Today, currentTimeOfDay: 1200,
            currentPlayerText: query, markFollowUpCues: markFollowUpCues);
    }

    private static NPC Npc() => new() { Name = "Emily", displayName = "Emily" };

    private static EmotionalExpressionCue Expression() => new(
        "test", "quiet", "debug", "conflict style", "repair style", "reply style", 50, 50, 1, 1, 1, 0);

    private static void AssertOccursOnce(string value, string prompt) =>
        Assert.Equal(1, prompt.Split(value, StringSplitOptions.None).Length - 1);
}
