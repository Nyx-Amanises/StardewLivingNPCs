using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using StardewValley;
using Xunit.Abstractions;

namespace LivingNPCs.Tests;

public sealed class BehaviorPromptCompressionTests
{
    private readonly ITestOutputHelper output;

    public BehaviorPromptCompressionTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData("disabled", "help requests are disabled")]
    [InlineData("relationship", "the relationship is not close enough yet")]
    [InlineData("emotion", "their current emotion is too strained")]
    [InlineData("conflict", "unresolved conflict makes asking for help feel wrong")]
    [InlineData("cooldown", "a recent help request is still too fresh")]
    public void BlockedRequestsKeepTheReasonWithoutBuildingInapplicableCandidates(string blocker, string reason)
    {
        var state = TestScenarios.TrustedState();
        // A previously scheduled opportunity must not override the current readiness result.
        state.DailyHelpRequestOpportunityTotalDays = TestScenarios.Today;
        state.HelpRequests.Add(new NpcHelpRequestFact { Summary = "An old favor.", Status = "Fulfilled" });
        if (blocker == "emotion")
        {
            state.CurrentEmotion = "Angry";
        }
        else if (blocker == "conflict")
        {
            state.Conflicts.Add(TestScenarios.SeriousConflict());
        }
        else if (blocker == "cooldown")
        {
            state.LastHelpRequestTotalDays = TestScenarios.Today - 1;
        }

        HelpRequestReadinessResult readiness = HelpRequestReadinessRules.Evaluate(
            state, blocker == "relationship" ? 1 : 6, blocker == "disabled" ? 0 : 1, 3, TestScenarios.Today);
        Assert.False(readiness.Allowed);

        string line = Assert.Single(BehaviorPromptContextBuilder.BuildHelpRequestContextLines(
            state, readiness, () => throw new InvalidOperationException("Blocked requests must not select item candidates.")));

        Assert.Contains(reason, line);
        Assert.Contains("should not open a new help request now", line);
        Assert.Contains("even if the farmer offers to help", line);
        Assert.Contains("never name, accept or commit to a new favor", line);
        Assert.DoesNotContain("Help-request fit", line);
        Assert.DoesNotContain("Help-request lifecycle", line);
    }

    [Theory]
    [InlineData("Offered")]
    [InlineData("Pending")]
    public void ActiveRequestsKeepTheLifecycleAndFitEvenWhenNewRequestsAreBlocked(string status)
    {
        var state = TestScenarios.TrustedState();
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Summary = "Bring quartz.", Status = status, RequestedItemId = "(O)80", RequestedItemLabel = "Quartz"
        });
        var readiness = HelpRequestReadinessRules.Evaluate(state, 6, 1, 3, TestScenarios.Today);
        Assert.False(readiness.Allowed);
        const string fit = "currently reasonable item requests: Quartz (O)80; request depth: one step";
        int selections = 0;

        string[] lines = BehaviorPromptContextBuilder.BuildHelpRequestContextLines(state, readiness, () =>
        {
            selections++;
            return fit;
        }).ToArray();

        Assert.Equal(1, selections);
        Assert.Equal(3, lines.Length);
        Assert.Equal(PromptFragments.Context.HelpRequestLifecycleLine, lines[0]);
        Assert.Contains("only Pending is a task", lines[0]);
        Assert.Contains(readiness.Reason, lines[1]);
        Assert.Equal(PromptFragments.Context.HelpRequestFitLine(fit), lines[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllowedRequestsKeepTheCompleteFitInBothContextModes(bool concise)
    {
        var state = TestScenarios.TrustedState();
        var readiness = HelpRequestReadinessRules.Evaluate(state, 6, 1, 3, TestScenarios.Today);
        Assert.True(readiness.Allowed);
        const string fit = "theme study; currently reasonable item requests: Quartz (O)80, Wood (O)388; "
            + "allowed help request type: item_request only; request relationship tier: friendly; "
            + "request depth: two steps; world-stage constraint: early year; exact spoken order";

        string[] lines = BehaviorPromptContextBuilder.BuildHelpRequestContextLines(
            state, readiness, () => fit, concise).ToArray();

        Assert.Equal(3, lines.Length);
        Assert.Contains("Offered = asked but not accepted", lines[0]);
        Assert.Contains("may naturally ask for one modest favor now", lines[1]);
        Assert.Contains(readiness.Reason, lines[1]);
        Assert.Contains("do not withdraw it or answer for them", lines[1]);
        Assert.Equal(PromptFragments.Context.HelpRequestFitLine(fit), lines[2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullContextKeepsCharacterAndSceneFactsWithoutRepeatingProfileLabels(bool hasState)
    {
        var npc = new NPC { Name = "Emily", displayName = "Emily" };
        var disposition = new NpcDispositionProfile(
            "distinct temperament", "debug", 0, 0, 16, "reason",
            SourceLabel: "distinct profile source",
            BackgroundPrompt: "distinct family background",
            DialoguePrompt: "distinct dialogue cue");
        var expression = new EmotionalExpressionCue(
            "test", "distinct expression style", "debug", "conflict cue", "repair cue", "reply cue",
            50, 50, 1, 1, 1, 0);
        WorldContextSnapshot world = TestScenarios.World();
        LivingNpcState? state = hasState ? TestScenarios.TrustedState() : null;

        string prompt = BehaviorPromptContextBuilder.BuildPromptContext(
            npc, Array.Empty<BehaviorMemoryEntry>(), state, world, disposition, expression,
            MemoryRecallPlan.Empty, Array.Empty<CommunityImpressionSelection>(),
            maxPendingHelpRequestsPerNpc: 0, helpRequestCooldownDays: 3,
            currentTotalDays: TestScenarios.Today, currentTimeOfDay: 1200);

        Assert.Contains(PromptFragments.Context.StanceHeading, prompt);
        foreach (string fact in new[]
        {
            disposition.PromptLabel, disposition.SourceLabel, expression.PromptLabel,
            disposition.BackgroundPrompt, disposition.DialoguePrompt
        })
        {
            Assert.Equal(1, prompt.Split(fact, StringSplitOptions.None).Length - 1);
        }

        Assert.Contains(PromptFragments.Context.SceneLine(world), prompt);
        Assert.Contains(PromptFragments.Context.WorldKnowledgeLine(world.ProgressionKnowledge.PromptLabel), prompt);
        if (state != null)
        {
            Assert.Contains(PromptFragments.Context.MoodLine(state), prompt);
            Assert.Contains(PromptFragments.Context.EmotionLine(state), prompt);
            Assert.Contains(PromptFragments.Context.FamiliarityLine(state), prompt);
            Assert.Contains(PromptFragments.Context.TrustLine(state), prompt);
            Assert.Contains("help requests are disabled", prompt);
        }
    }

    [Fact]
    public void FullContextKeepsDetailedMemoriesOnceWithTheirGuidanceAndPrivacyBoundaries()
    {
        string prompt = BuildRichPromptFixture();

        foreach (string fact in new[]
        {
            "Coffee", "the library reading", "the wind is getting cold",
            "The farmer promised to return the borrowed book.", "The farmer prefers quiet mornings.",
            "The farmer helped Pam carry groceries.", "The farmer mocked the lesson.",
            "The farmer repaired the garden fence.", "walked along the shore together",
            "wants to visit the library", "Bring a geode for the classroom."
        })
        {
            AssertOccursOnce(fact, prompt);
        }

        AssertOccursOnce("conflict expression cue", prompt);
        AssertOccursOnce("repair expression cue", prompt);
        AssertOccursOnce("repeated conversation pressure: 24/100", prompt);
        Assert.Contains("gifts recorded today: 2", prompt);
        Assert.Contains("severity 80/100", prompt);
        Assert.Contains("repair stage NeedsApology", prompt);
        Assert.Contains("pleasant line alone cannot erase serious hurt", prompt);
        Assert.Contains("fulfillment", prompt);
        Assert.Contains("(O)535", prompt);
        Assert.Contains("do not reveal knowledge the NPC would not plausibly have", prompt);
        Assert.Contains("keep indirect reports tentative", prompt);
        Assert.Contains("without formally reciting the memory or treating it as a future plan", prompt);
        Assert.Contains("body language and follow-through", prompt);
        Assert.Contains("still refuse during events, sleep, severe conflict", prompt);

        this.output.WriteLine("Rich behavior fixture: {0} characters; brief: {1} characters.",
            prompt.Length, LivingNpcContextCompressor.BuildBriefContext(prompt).Length);
    }

    [Theory]
    [InlineData("Offered")]
    [InlineData("Pending")]
    public void PriorityRequestRetainsCurrentStepAndOtherStoredRequests(string status)
    {
        var state = TestScenarios.TrustedState();
        var active = new NpcHelpRequestFact
        {
            Type = "item_request", Status = status, Summary = "Bring two classroom supplies.",
            DueTotalDays = TestScenarios.Today + 1, CurrentStepIndex = 1,
            Steps = new List<NpcHelpRequestStepFact>
            {
                new() { Summary = "The first supply.", RequestedItemId = "(O)80", RequestedItemLabel = "Quartz", Status = "Fulfilled" },
                new() { Summary = "The remaining supply.", RequestedItemId = "(O)388", RequestedItemLabel = "Wood", Status = "Pending" }
            }
        };
        state.HelpRequests.Add(active);
        state.HelpRequests.Add(new NpcHelpRequestFact { Summary = "An older completed errand.", Status = "Fulfilled" });

        string prompt = string.Join("\n", BehaviorPromptContextBuilder.BuildDurableStoreLines(
                state, TestScenarios.Today, priorityCuesProvided: true)
            .Concat(BehaviorPromptContextBuilder.BuildPriorityPromptContext(
                FixtureNpc(), state, TestScenarios.World(), MemoryRecallPlan.Empty,
                Array.Empty<CommunityImpressionSelection>(), FixtureExpression(), TestScenarios.Today)));

        AssertOccursOnce(active.Summary, prompt);
        AssertOccursOnce("An older completed errand.", prompt);
        Assert.Contains("due tomorrow", prompt);
        Assert.Contains("step 2/2", prompt);
        Assert.Contains("The remaining supply.", prompt);
        Assert.Contains("Wood (O)388", prompt);
        Assert.Contains(status == "Offered"
            ? "do not treat it as an active task until accepted"
            : "the farmer accepted this ask; it is now an active personal task", prompt);
        string emptyStores = Assert.Single(prompt.Split('\n'), line => line.Contains("Nothing recorded yet for:"));
        Assert.DoesNotContain(PromptFragments.Context.StoreLabelHelpRequests, emptyStores);
    }

    [Fact]
    public void CompletedFollowUpsStopRepeatingWhileTheirUnderlyingFactsRemain()
    {
        var state = RichState();
        string first = BuildFixturePrompt(state);
        string second = BuildFixturePrompt(state);

        Assert.Contains("Recently resolved conflict:", first);
        Assert.Contains("Recently fulfilled help request:", first);
        Assert.Contains("without formally reciting the memory or treating it as a future plan", first);
        Assert.DoesNotContain("Recently resolved conflict:", second);
        Assert.DoesNotContain("Recently fulfilled help request:", second);
        Assert.DoesNotContain("without formally reciting the memory or treating it as a future plan", second);
        AssertOccursOnce("The farmer repaired the garden fence.", second);
        AssertOccursOnce("Bring a geode for the classroom.", second);
        AssertOccursOnce("walked along the shore together", second);
        Assert.Equal(TestScenarios.Today, state.Conflicts.Single(conflict => conflict.Status == "Resolved").RecoveryMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, state.HelpRequests.Single().LastMentionedTotalDays);
        Assert.Equal(TestScenarios.Today, state.SharedExperiences.Single().FollowUpShownTotalDays);
    }

    [Fact]
    public void BriefContextKeepsTheMergedConflictAndBehaviorCues()
    {
        string brief = LivingNpcContextCompressor.BuildBriefContext(BuildRichPromptFixture());

        Assert.Contains("careful temperament", brief);
        Assert.Contains("Last interaction rhythm:", brief);
        Assert.Contains("The farmer mocked the lesson.", brief);
        Assert.Contains("conflict expression cue", brief);
        Assert.Contains("pleasant line alone cannot erase serious hurt", brief);
        Assert.Contains("wants to visit the library", brief);
        Assert.Contains("body language and follow-through", brief);
        Assert.Contains("walked along the shore together", brief);
        Assert.Contains("still refuse during events, sleep, severe conflict", brief);
    }

    [Fact]
    public void LowFamiliarityDoesNotEraseEstablishedFriendshipOrLowTrustDisclosureLimits()
    {
        var state = TestScenarios.TrustedState();
        state.Familiarity = 0;
        state.LastFriendshipHearts = 10;
        state.InteractionComfortTier = "Intimate";
        state.RelationshipTrust = 20;

        string prompt = BuildFixturePrompt(state);

        Assert.Contains("10 hearts", prompt);
        Assert.Contains("limited familiarity recorded", prompt);
        Assert.Contains("current friendship and explicit relationship status take precedence", prompt);
        Assert.DoesNotContain("new or barely familiar", prompt);
        Assert.Contains("current friendship and recorded familiarity guide warmth", prompt);
        Assert.Contains("Relationship pacing (last recorded interaction)", prompt);
        AssertOccursOnce("low interpersonal trust (20/100)", prompt);
        Assert.Contains("keep disclosures surface-level", prompt);
        Assert.Contains("Avoid sudden emotional intimacy", prompt);
        Assert.Contains("shared outings and private visits may be accepted", prompt);
    }

    [Fact]
    public void ScenePressureKeepsItsCauseWithoutRestatingAlreadyAppliedMood()
    {
        var state = TestScenarios.TrustedState();
        state.Mood = "Uneasy";
        state.CurrentInclination = "Measured";
        state.LastSceneInfluenceReason = "the wind is getting cold";

        string prompt = BuildFixturePrompt(state);

        AssertOccursOnce("Mood: Uneasy", prompt);
        AssertOccursOnce("response inclination: Measured", prompt);
        AssertOccursOnce("the wind is getting cold", prompt);
        Assert.DoesNotContain("suggested mood:", prompt);
        Assert.Contains("current scene pressure tint tone and pacing", prompt);
    }

    [Fact]
    public void PriorityConflictOutsideTopStoreEntriesDoesNotDisplaceOtherFacts()
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 4; i++)
        {
            state.Conflicts.Add(new NpcConflictFact
            {
                Summary = $"Minor unresolved issue {i}.", Status = "Active", Severity = 10 + i
            });
        }

        state.Conflicts.Add(new NpcConflictFact
        {
            Summary = "An older serious issue is still recovering.", Status = "Recovering", Severity = 80
        });
        string prompt = BuildFixturePrompt(state);

        foreach (NpcConflictFact conflict in state.Conflicts)
        {
            AssertOccursOnce(conflict.Summary, prompt);
        }

        Assert.Contains("severity 80/100", prompt);
        Assert.Contains("if severe, a friendly invitation may be refused", prompt);
    }

    // Returns a real builder result for offline before/after size measurements. It deliberately
    // creates fresh state on each call because full-context generation consumes follow-up cues.
    internal static string BuildRichPromptFixture() => BuildFixturePrompt(RichState());

    private static string BuildFixturePrompt(LivingNpcState state)
    {
        var world = TestScenarios.World(promptLabel: "quiet public company") with
        {
            StateInfluence = new WorldStateInfluence("Uneasy", "Measured", 40, 0, 0, "the wind is getting cold", "debug"),
            NearbyNpcNames = new[] { "Penny" }
        };
        var recall = new MemoryRecallPlan(MemoryRecallPlan.Empty.Context,
            state.LongTermMemories.Select(memory => new LongTermMemorySelection(memory, 80, "fixture")).ToArray(),
            state.PlayerPreferenceMemories.Select(memory => new PlayerPreferenceSelection(memory, 80, "fixture")).ToArray());
        var community = state.CommunityImpressions
            .Select(memory => new CommunityImpressionSelection(memory, 80, "fixture")).ToArray();

        return BehaviorPromptContextBuilder.BuildPromptContext(
            FixtureNpc(), Array.Empty<BehaviorMemoryEntry>(), state, world,
            new NpcDispositionProfile("careful temperament", "debug", 0, 0, 16, "reason",
                SourceLabel: "fixture profile", BackgroundPrompt: "a creative family background", DialoguePrompt: "gentle phrasing"),
            FixtureExpression(), recall, community, maxPendingHelpRequestsPerNpc: 0, helpRequestCooldownDays: 3,
            currentTotalDays: TestScenarios.Today, currentTimeOfDay: 1200);
    }

    private static LivingNpcState RichState()
    {
        var state = TestScenarios.TrustedState();
        state.LastFriendshipHearts = 6;
        state.LastGiftName = "Coffee";
        state.LastGiftTaste = "liked";
        state.LastGiftTotalDays = TestScenarios.Today;
        state.GiftsToday = 2;
        state.LastEventContext = "the library reading";
        state.LastEventTotalDays = TestScenarios.Today;
        state.LastSceneInfluenceReason = "the wind is getting cold";
        state.InteractionRhythm = "DailyRoutine";
        state.ConsecutiveConversationDays = 7;
        state.ConversationsToday = 3;
        state.RepeatedConversationPressure = 24;
        state.LongTermMemories.Add(TestScenarios.Memory("The farmer promised to return the borrowed book.", importance: 80));
        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact { Summary = "The farmer prefers quiet mornings.", Importance = 75 });
        state.CommunityImpressions.Add(new CommunityImpressionFact
        {
            SubjectNpcName = "Pam", Summary = "The farmer helped Pam carry groceries.", Source = "CloseCircle",
            Visibility = "Personal", TransmissionDepth = 1, LastUpdatedTotalDays = TestScenarios.Today, ExpiresTotalDays = TestScenarios.Today + 4
        });
        var conflict = TestScenarios.SeriousConflict();
        conflict.Summary = "The farmer mocked the lesson.";
        state.Conflicts.Add(conflict);
        state.Conflicts.Add(new NpcConflictFact
        {
            Summary = "The farmer repaired the garden fence.", Status = "Resolved", Severity = 0,
            RepairStage = "Resolved", CreatedTotalDays = TestScenarios.Today - 2, ResolvedTotalDays = TestScenarios.Today - 1
        });
        state.SharedExperiences.Add(new SharedExperienceFact
        {
            Type = "companion_outing", Summary = "walked along the shore together", LocationLabel = "Beach",
            CreatedTotalDays = TestScenarios.Today - 2, LastUpdatedTotalDays = TestScenarios.Today - 1,
            FollowUpEligibleTotalDays = TestScenarios.Today
        });
        state.DialogueBehaviorInfluences.Add(new DialogueBehaviorInfluenceFact
        {
            Type = "visit_location", Summary = "wants to visit the library", TargetLocationLabel = "Library",
            Status = "Active", Intensity = 55, ExpiresTotalDays = TestScenarios.Today + 2
        });
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Type = "item_request", Summary = "Bring a geode for the classroom.", Status = "Fulfilled",
            RequestedItemId = "(O)535", RequestedItemLabel = "Geode", FulfilledTotalDays = TestScenarios.Today - 1,
            DueTotalDays = TestScenarios.Today - 1, FollowUpPotential = "a later discussion of the fulfillment"
        });
        return state;
    }

    private static NPC FixtureNpc() => new() { Name = "Emily", displayName = "Emily" };

    private static EmotionalExpressionCue FixtureExpression() => new(
        "fixture", "soft-spoken style", "debug", "conflict expression cue", "repair expression cue", "reply expression cue",
        50, 50, 1, 1, 1, 0);

    private static void AssertOccursOnce(string fact, string prompt)
    {
        Assert.Equal(1, prompt.Split(fact, StringSplitOptions.None).Length - 1);
    }
}
