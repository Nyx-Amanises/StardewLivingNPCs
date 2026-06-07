using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class ConflictRepairTests
{
    [Fact]
    public void SeriousConflictRequiresApologyGestureTimeAndConversationBeforeResolution()
    {
        var memory = new BehaviorMemory();
        var state = TestScenarios.TrustedState();
        var conflict = TestScenarios.SeriousConflict();
        state.Conflicts.Add(conflict);

        memory.ApplyConflictRepairForTesting(
            state,
            repairDelta: 100,
            apology: true,
            specificRepairTalk: false,
            currentTotalDays: TestScenarios.Today
        );

        Assert.True(conflict.ApologyReceived);
        Assert.Equal("NeedsGesture", conflict.RepairStage);
        Assert.NotEqual("Resolved", conflict.Status);
        Assert.True(conflict.Severity >= 30);

        memory.MarkRepairGiftReceivedForTesting(
            state,
            "Amethyst",
            currentTotalDays: TestScenarios.Today + 1
        );

        Assert.True(conflict.MeaningfulGiftReceived);
        Assert.Equal("NeedsTime", conflict.RepairStage);
        Assert.NotEqual("Resolved", conflict.Status);

        memory.DecayEmotionAndConflictsForTesting(
            state,
            emotionDailyDecay: 0,
            conflictDailyDecay: 100,
            currentTotalDays: TestScenarios.Today + 2
        );

        Assert.Equal("NeedsTime", conflict.RepairStage);
        Assert.NotEqual("Resolved", conflict.Status);
        Assert.True(conflict.Severity >= 20);

        memory.ApplyConflictRepairForTesting(
            state,
            repairDelta: 0,
            apology: false,
            specificRepairTalk: true,
            currentTotalDays: TestScenarios.Today + ConflictRepairService.GetComplexRepairDelayDays(80)
        );

        Assert.True(conflict.SpecificRepairTalkReceived);
        Assert.Equal("ReadyToResolve", conflict.RepairStage);
        Assert.NotEqual("Resolved", conflict.Status);
        Assert.True(conflict.Severity > 0);
    }
}
