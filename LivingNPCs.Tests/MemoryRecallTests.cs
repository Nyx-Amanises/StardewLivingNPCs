using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class MemoryRecallTests
{
    [Fact]
    public void LocationChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer loved quiet fishing at the beach.",
            importance: 52,
            tags: ["fishing", "nature"]));

        var plan = Recall(state, TestScenarios.World("Beach", "Beach"));

        Assert.Contains("beach", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimeOfDayChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer promised to check in during a late night festival.",
            importance: 52,
            kind: "promise",
            tags: "night"));

        var plan = Recall(state, TestScenarios.World(timeOfDay: 1900));

        Assert.Contains("night", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LastGiftChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.LastGiftName = "Coffee";
        state.LastGiftTaste = "liked";
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer once brought coffee after a tiring morning.",
            importance: 50,
            tags: ["drink", "comfort"]));

        var plan = Recall(state, TestScenarios.World());

        Assert.Contains("coffee", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingHelpRequestChangesLongTermMemoryRanking()
    {
        var state = TestScenarios.TrustedState();
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Type = "item_request",
            Summary = "Bring quartz for the library display.",
            Status = "Pending",
            DueTotalDays = TestScenarios.Today + 1
        });
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer helped with ordinary farm chores.",
            importance: 65,
            tags: "farming"));
        state.LongTermMemories.Add(TestScenarios.Memory(
            "The farmer promised to bring quartz for the library display.",
            importance: 48,
            kind: "promise",
            subject: "library quartz",
            tags: ["scholarly", "mineral"]));

        var plan = Recall(state, TestScenarios.World("ArchaeologyHouse", "Library"));

        Assert.Contains("quartz", plan.LongTermMemories[0].Memory.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryRecallPlan Recall(LivingNpcState state, WorldContextSnapshot world)
    {
        return new BehaviorMemory().BuildMemoryRecallPlanForTesting(
            state,
            world,
            Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 2,
            preferenceCount: 0,
            currentTotalDays: TestScenarios.Today
        );
    }
}
