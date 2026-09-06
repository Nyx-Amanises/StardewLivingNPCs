using LivingNPCs.Behavior;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests;

public sealed class MemoryImpressionPersistenceTests
{
    private const int Today = TestScenarios.Today;

    [Fact]
    public void SuccessfulEvidenceWatermarkSurvivesTheSaveCloneAndJsonRoundTrip()
    {
        var state = StateReadyForFirstDraft();
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        MemoryImpressionService.ApplyResult(state, "They have learned to rely on each other.", Today);

        LivingNpcState restored = RoundTripSave(state);

        Assert.Equal(state.RelationshipImpression, restored.RelationshipImpression);
        Assert.Equal(state.RelationshipImpressionUpdatedTotalDays, restored.RelationshipImpressionUpdatedTotalDays);
        Assert.Equal(state.RelationshipImpressionMemoryCount, restored.RelationshipImpressionMemoryCount);
        Assert.Equal(state.RelationshipImpressionSourceKeys, restored.RelationshipImpressionSourceKeys);
        Assert.Equal(state.RelationshipImpressionContext, restored.RelationshipImpressionContext);
        Assert.False(MemoryImpressionService.ShouldRequest(restored, Today + 1));

        restored.LongTermMemories.Add(TestScenarios.Memory("The farmer kept a difficult promise.", importance: 90));

        Assert.True(MemoryImpressionService.ShouldRequest(restored, Today + 1));
        Assert.Equal(3, state.LongTermMemories.Count);
    }

    [Fact]
    public void InFlightSnapshotSurvivesSaveReloadAndStillLeavesNewLiveChangesPending()
    {
        var state = StateReadyForFirstDraft();
        string capturedSummary = state.LongTermMemories[0].Summary;
        string capturedKey = MemoryImpressionSources.GetKey(state.LongTermMemories[0]);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        state.ImpressionRequestAttempts = 2;
        string capturedContext = state.ImpressionContextInFlight;
        state.LongTermMemories[0].Summary = "They made a different promise while the model was working.";
        string changedKey = MemoryImpressionSources.GetKey(state.LongTermMemories[0]);
        state.FarmerNickname = "小刚";
        state.FarmerNicknameStatus = "Accepted";

        LivingNpcState restored = RoundTripSave(state);

        Assert.True(MemoryImpressionService.HasInFlightRequest(restored));
        Assert.Equal(state.ImpressionRequestId, restored.ImpressionRequestId);
        Assert.Equal(Today, restored.ImpressionRequestTotalDays);
        Assert.Equal(2, restored.ImpressionRequestAttempts);
        Assert.Equal(capturedContext, restored.ImpressionContextInFlight);
        Assert.Contains(restored.ImpressionInFlight, memory => memory.Summary == capturedSummary);
        Assert.Contains(restored.LongTermMemories, memory => memory.Summary == state.LongTermMemories[0].Summary);
        Assert.False(MemoryImpressionService.PrepareRequest(restored, Today + 1));

        MemoryImpressionService.ApplyResult(restored, "Their relationship before the new promise.", Today + 1);

        Assert.Contains(capturedKey, restored.RelationshipImpressionSourceKeys);
        Assert.DoesNotContain(changedKey, restored.RelationshipImpressionSourceKeys);
        Assert.Equal(capturedContext, restored.RelationshipImpressionContext);
        Assert.True(MemoryImpressionService.ShouldRequest(restored, Today + 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldSaveWithoutWatermarkFieldsCanGenerateOnceAndThenStayUpToDate(bool hasOldImpression)
    {
        var state = StateReadyForFirstDraft();
        if (hasOldImpression)
        {
            state.RelationshipImpression = "They had an older relationship impression.";
            state.RelationshipImpressionUpdatedTotalDays = Today - 5;
            state.RelationshipImpressionMemoryCount = 8;
        }

        LivingNpcState restored = DeserializeLegacyState(state);

        Assert.Empty(restored.RelationshipImpressionSourceKeys);
        Assert.Empty(restored.RelationshipImpressionContext);
        Assert.Empty(restored.ImpressionContextInFlight);
        Assert.True(MemoryImpressionService.PrepareRequest(restored, Today));

        MemoryImpressionService.ApplyResult(restored, "Their current relationship impression.", Today);

        Assert.Equal(3, restored.RelationshipImpressionSourceKeys.Count);
        Assert.False(MemoryImpressionService.ShouldRequest(restored, Today + 1));
    }

    [Fact]
    public void OldSaveWithPendingArchiveBatchKeepsItsRetryStateWithoutNewContextFields()
    {
        var state = TestScenarios.TrustedState();
        state.ImpressionInFlight.Add(TestScenarios.Memory("An archived memory being summarized."));
        state.ImpressionRequestId = "impression-Emily-legacy";
        state.ImpressionRequestTotalDays = Today - 1;
        state.ImpressionRequestAttempts = 2;

        LivingNpcState restored = DeserializeLegacyState(state);

        Assert.True(MemoryImpressionService.HasInFlightRequest(restored));
        Assert.False(MemoryImpressionService.PrepareRequest(restored, Today));
        Assert.Equal(2, restored.ImpressionRequestAttempts);
        Assert.Equal("impression-Emily-legacy", restored.ImpressionRequestId);

        MemoryImpressionService.FailRequest(restored, Today);

        Assert.Equal("An archived memory being summarized.", Assert.Single(restored.ImpressionBacklog).Summary);
        Assert.Equal(Today, restored.LastImpressionFailureTotalDays);
        Assert.Empty(restored.RelationshipImpressionSourceKeys);
    }

    private static LivingNpcState RoundTripSave(LivingNpcState state)
    {
        var save = new BehaviorMemorySaveData
        {
            StatesByNpc = { [state.NpcName] = state.Clone() }
        };

        var restored = JsonConvert.DeserializeObject<BehaviorMemorySaveData>(JsonConvert.SerializeObject(save));
        Assert.NotNull(restored);
        return restored.StatesByNpc[state.NpcName];
    }

    private static LivingNpcState DeserializeLegacyState(LivingNpcState state)
    {
        JObject json = JObject.FromObject(state);
        json.Remove(nameof(LivingNpcState.RelationshipImpressionSourceKeys));
        json.Remove(nameof(LivingNpcState.RelationshipImpressionContext));
        json.Remove(nameof(LivingNpcState.ImpressionContextInFlight));

        var restored = json.ToObject<LivingNpcState>();
        Assert.NotNull(restored);
        return restored;
    }

    private static LivingNpcState StateReadyForFirstDraft()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory("The farmer helped at the workshop.", importance: 80));
        state.LongTermMemories.Add(TestScenarios.Memory("The farmer promised to visit the library.", importance: 60, kind: "promise"));
        state.LongTermMemories.Add(TestScenarios.Memory("They agreed to spend more time together.", importance: 60, kind: "relationship"));
        return state;
    }
}
