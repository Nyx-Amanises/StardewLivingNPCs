using LivingNPCs.Behavior;
using StardewValley;

namespace LivingNPCs.Tests;

public sealed class BehaviorPromptCompressionTests
{
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
}
