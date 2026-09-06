using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class MemoryImpressionTests
{
    private const int Today = TestScenarios.Today;

    [Fact]
    public void ApplyCapacityMovesEvictedMemoriesToBacklog()
    {
        var state = TestScenarios.TrustedState();
        var ordered = new List<LongTermMemoryFact>();
        for (int i = 0; i < LongTermMemoryStore.MaxMemoriesPerNpc + 6; i++)
        {
            ordered.Add(TestScenarios.Memory($"Ordered memory number {i}.", importance: 90 - i));
        }

        LongTermMemoryStore.ApplyCapacity(state, ordered.ToList());

        Assert.Equal(LongTermMemoryStore.MaxMemoriesPerNpc, state.LongTermMemories.Count);
        Assert.Equal(6, state.ImpressionBacklog.Count);
        Assert.All(
            state.ImpressionBacklog,
            memory => Assert.DoesNotContain(state.LongTermMemories, kept => kept.Summary == memory.Summary));
        Assert.Contains(state.ImpressionBacklog, memory => memory.Summary.Contains($"number {LongTermMemoryStore.MaxMemoriesPerNpc}."));
    }

    [Fact]
    public void BacklogDoesNotQueueDuplicates()
    {
        var state = TestScenarios.TrustedState();
        var memory = TestScenarios.Memory("The farmer helped fix the fence.");

        LongTermMemoryStore.AddToImpressionBacklog(state, new[] { memory });
        LongTermMemoryStore.AddToImpressionBacklog(state, new[] { memory });

        Assert.Single(state.ImpressionBacklog);
    }

    [Fact]
    public void BacklogDoesNotQueueEntriesAlreadyInFlight()
    {
        var state = TestScenarios.TrustedState();
        var memory = TestScenarios.Memory("The farmer helped fix the fence.");
        state.ImpressionInFlight.Add(memory);

        LongTermMemoryStore.AddToImpressionBacklog(state, new[] { memory });

        Assert.Empty(state.ImpressionBacklog);
    }

    [Fact]
    public void BacklogOverflowDropsLowestImportanceOnly()
    {
        var state = TestScenarios.TrustedState();
        var evicted = new List<LongTermMemoryFact>();
        for (int i = 0; i < LongTermMemoryStore.MaxImpressionBacklog + 3; i++)
        {
            evicted.Add(TestScenarios.Memory($"Backlog memory number {i}.", importance: 10 + i));
        }

        LongTermMemoryStore.AddToImpressionBacklog(state, evicted);

        Assert.Equal(LongTermMemoryStore.MaxImpressionBacklog, state.ImpressionBacklog.Count);
        // The three lowest-importance entries (numbers 0-2) are the ones sacrificed.
        Assert.DoesNotContain(state.ImpressionBacklog, memory => memory.Summary.Contains("number 0."));
        Assert.DoesNotContain(state.ImpressionBacklog, memory => memory.Summary.Contains("number 2."));
        Assert.Contains(state.ImpressionBacklog, memory => memory.Summary.Contains("number 3."));
    }

    [Fact]
    public void ShouldRequestPreservesEightEntryThresholdForOrdinaryBacklog()
    {
        var state = TestScenarios.TrustedState();
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        // Low-importance archive entries still use the original eight-entry threshold.
        for (int i = 0; i < MemoryImpressionService.TriggerBacklogCount - 1; i++)
        {
            state.ImpressionBacklog.Add(TestScenarios.Memory($"Fresh memory {i}."));
        }

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        state.ImpressionBacklog.Add(TestScenarios.Memory("Eighth fresh memory."));

        Assert.True(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Theory]
    [InlineData(27, false)]
    [InlineData(28, true)]
    public void ShouldRequestUsesCreationAgeForSmallBacklog(int age, bool expected)
    {
        var state = TestScenarios.TrustedState();
        var stale = TestScenarios.Memory("An old memory from last season.");
        stale.CreatedTotalDays = Today - age;
        stale.LastUpdatedTotalDays = Today;
        state.ImpressionBacklog.Add(stale);

        Assert.Equal(expected, MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void ShouldRequestBacksOffAfterFailure()
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < MemoryImpressionService.TriggerBacklogCount; i++)
        {
            state.ImpressionBacklog.Add(TestScenarios.Memory($"Queued memory {i}."));
        }

        state.LastImpressionFailureTotalDays = Today - 1;
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        state.LastImpressionFailureTotalDays = Today - MemoryImpressionService.FailureCooldownDays;
        Assert.True(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void ApplyResultClearsInFlightAndStoresImpression()
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 5; i++)
        {
            state.ImpressionInFlight.Add(TestScenarios.Memory($"Compressed memory {i}."));
        }

        state.ImpressionRequestId = "impression-Emily-test";
        state.ImpressionRequestTotalDays = Today - 1;
        state.ImpressionRequestAttempts = 1;
        state.LastImpressionFailureTotalDays = Today - 10;
        state.ImpressionContextInFlight = "The captured relationship state.";

        MemoryImpressionService.ApplyResult(state, " They have known each other for two years. ", Today);

        Assert.Equal("They have known each other for two years.", state.RelationshipImpression);
        Assert.Equal(Today, state.RelationshipImpressionUpdatedTotalDays);
        Assert.Equal(5, state.RelationshipImpressionMemoryCount);
        Assert.Equal(5, state.RelationshipImpressionSourceKeys.Count);
        Assert.Equal("The captured relationship state.", state.RelationshipImpressionContext);
        Assert.Empty(state.ImpressionInFlight);
        Assert.Empty(state.ImpressionContextInFlight);
        Assert.Equal(string.Empty, state.ImpressionRequestId);
        Assert.Equal(-1, state.ImpressionRequestTotalDays);
        Assert.Equal(0, state.ImpressionRequestAttempts);
        Assert.Equal(-1, state.LastImpressionFailureTotalDays);
    }

    [Fact]
    public void FailRequestReturnsInFlightBatchToBacklogWithoutLoss()
    {
        var state = TestScenarios.TrustedState();
        state.ImpressionBacklog.Add(TestScenarios.Memory("Newly evicted memory."));
        for (int i = 0; i < 3; i++)
        {
            state.ImpressionInFlight.Add(TestScenarios.Memory($"In-flight memory {i}."));
        }

        state.ImpressionRequestId = "impression-Emily-test";
        state.ImpressionRequestAttempts = MemoryImpressionService.MaxAttempts;

        MemoryImpressionService.FailRequest(state, Today);

        Assert.Equal(4, state.ImpressionBacklog.Count);
        Assert.Empty(state.ImpressionInFlight);
        Assert.Equal(string.Empty, state.ImpressionRequestId);
        Assert.Equal(Today, state.LastImpressionFailureTotalDays);
        // The failed batch is queued ahead of newer evictions so it compresses first next time.
        Assert.Contains("In-flight memory", state.ImpressionBacklog[0].Summary);
        Assert.Contains(state.ImpressionBacklog, memory => memory.Summary == "Newly evicted memory.");
    }

    [Fact]
    public void StateCloneCopiesImpressionFields()
    {
        var state = TestScenarios.TrustedState();
        state.RelationshipImpression = "They are old friends.";
        state.RelationshipImpressionUpdatedTotalDays = Today - 2;
        state.RelationshipImpressionMemoryCount = 12;
        state.RelationshipImpressionSourceKeys.Add("covered-source");
        state.RelationshipImpressionContext = "The last successful relationship state.";
        state.ImpressionBacklog.Add(TestScenarios.Memory("Queued memory."));
        state.ImpressionInFlight.Add(TestScenarios.Memory("Requested memory."));
        state.ImpressionContextInFlight = "The pending relationship state.";
        state.ImpressionRequestId = "impression-Emily-test";
        state.ImpressionRequestTotalDays = Today;
        state.ImpressionRequestAttempts = 2;
        state.LastImpressionFailureTotalDays = Today - 5;

        var clone = state.Clone();

        Assert.Equal(state.RelationshipImpression, clone.RelationshipImpression);
        Assert.Equal(state.RelationshipImpressionUpdatedTotalDays, clone.RelationshipImpressionUpdatedTotalDays);
        Assert.Equal(state.RelationshipImpressionMemoryCount, clone.RelationshipImpressionMemoryCount);
        Assert.Equal(state.RelationshipImpressionSourceKeys, clone.RelationshipImpressionSourceKeys);
        Assert.Equal(state.RelationshipImpressionContext, clone.RelationshipImpressionContext);
        Assert.Single(clone.ImpressionBacklog);
        Assert.Single(clone.ImpressionInFlight);
        Assert.Equal(state.ImpressionContextInFlight, clone.ImpressionContextInFlight);
        Assert.Equal(state.ImpressionRequestId, clone.ImpressionRequestId);
        Assert.Equal(state.ImpressionRequestTotalDays, clone.ImpressionRequestTotalDays);
        Assert.Equal(state.ImpressionRequestAttempts, clone.ImpressionRequestAttempts);
        Assert.Equal(state.LastImpressionFailureTotalDays, clone.LastImpressionFailureTotalDays);
        Assert.NotSame(state.RelationshipImpressionSourceKeys, clone.RelationshipImpressionSourceKeys);
        Assert.NotSame(state.ImpressionBacklog[0], clone.ImpressionBacklog[0]);
        Assert.NotSame(state.ImpressionInFlight[0], clone.ImpressionInFlight[0]);

        clone.RelationshipImpressionSourceKeys.Add("later-source");
        clone.ImpressionInFlight[0].Summary = "Changed after saving.";
        Assert.Single(state.RelationshipImpressionSourceKeys);
        Assert.Equal("Requested memory.", state.ImpressionInFlight[0].Summary);
    }
}
