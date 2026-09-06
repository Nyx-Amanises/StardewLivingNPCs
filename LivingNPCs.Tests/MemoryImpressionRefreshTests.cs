using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class MemoryImpressionRefreshTests
{
    private const int Today = TestScenarios.Today;

    [Fact]
    public void ThreeImportantInteractionsProduceFirstDraftBeforeAnyMemoryIsEvicted()
    {
        var state = StateWithImportantMemories(2);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        state.LongTermMemories.Add(TestScenarios.Memory("They helped prepare the town festival.", importance: 60));

        Assert.Equal(3, state.LongTermMemories.Count);
        Assert.Empty(state.ImpressionBacklog);
        Assert.True(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void FullPageOfMinorMemoriesOrCopiesOfOneFactCannotProduceFirstDraft()
    {
        var state = StateWithImportantMemories(0);
        for (int i = 0; i < LongTermMemoryStore.MaxMemoriesPerNpc; i++)
        {
            state.LongTermMemories.Add(TestScenarios.Memory($"An ordinary greeting {i}.", importance: 59));
        }

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        state.LongTermMemories.Clear();
        for (int i = 0; i < 3; i++)
        {
            state.LongTermMemories.Add(TestScenarios.Memory("They helped repair the fence.", importance: 80));
        }

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));
        Assert.Single(MemoryImpressionSources.GetImportantMemories(state));
    }

    [Fact]
    public void RelationshipPromisesAndBoundariesCountAtModerateImportance()
    {
        var state = StateWithImportantMemories(0);
        state.LongTermMemories.Add(TestScenarios.Memory("They want to know each other better.", importance: 40, kind: "relationship"));
        state.LongTermMemories.Add(TestScenarios.Memory("They promised to help with the class.", importance: 40, kind: "promise"));
        state.LongTermMemories.Add(TestScenarios.Memory("The farmer asked to avoid a painful subject.", importance: 39, kind: "boundary"));

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        state.LongTermMemories[2].Importance = 40;

        Assert.True(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void ImportantPreferenceSharedExperienceAndConflictCanProduceFirstDraftTogether()
    {
        var state = StateWithImportantMemories(0);
        state.PlayerPreferenceMemories.Add(ImportantPreference());
        state.SharedExperiences.Add(ImportantOuting());
        var conflict = TestScenarios.SeriousConflict();
        conflict.Severity = 2;
        conflict.PeakSeverity = 30;
        state.Conflicts.Add(conflict);

        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        Assert.Equal(3, state.ImpressionInFlight.Count);
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.Contains("tea", StringComparison.Ordinal));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.Contains("river", StringComparison.Ordinal));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.Contains(conflict.Summary, StringComparison.Ordinal));
        Assert.Single(state.PlayerPreferenceMemories);
        Assert.Single(state.SharedExperiences);
        Assert.Single(state.Conflicts);
    }

    [Fact]
    public void MinorPreferencesOutingsAndConflictsDoNotFillTheInitialThreshold()
    {
        var state = StateWithImportantMemories(2);
        var preference = ImportantPreference();
        preference.Importance = 59;
        state.PlayerPreferenceMemories.Add(preference);
        var outing = ImportantOuting();
        outing.Importance = 59;
        state.SharedExperiences.Add(outing);
        var conflict = TestScenarios.SeriousConflict();
        conflict.Severity = 29;
        conflict.PeakSeverity = 29;
        state.Conflicts.Add(conflict);

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void PreparingFirstDraftSnapshotsEvidenceAndKeepsTheOriginalMemoriesAvailable()
    {
        var state = StateWithImportantMemories();
        var originalMemories = state.LongTermMemories.ToArray();
        string originalContext = MemoryImpressionSources.GetRelationshipContext(state);

        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        Assert.Equal(originalMemories, state.LongTermMemories);
        Assert.Equal(3, state.ImpressionInFlight.Count);
        Assert.Equal(originalContext, state.ImpressionContextInFlight);
        Assert.True(MemoryImpressionService.HasInFlightRequest(state));
        Assert.Equal(Today, state.ImpressionRequestTotalDays);
        Assert.Empty(state.RelationshipImpressionSourceKeys);
        foreach (var original in originalMemories)
        {
            var snapshot = Assert.Single(state.ImpressionInFlight, memory => memory.Summary == original.Summary);
            Assert.NotSame(original, snapshot);
            Assert.NotSame(original.Tags, snapshot.Tags);
        }
    }

    [Fact]
    public void PendingGenerationCannotBeOverwrittenByAnotherRefresh()
    {
        var state = StateWithImportantMemories();
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        string requestId = state.ImpressionRequestId;
        string[] pendingKeys = state.ImpressionInFlight.Select(MemoryImpressionSources.GetKey).ToArray();
        state.LongTermMemories.Add(TestScenarios.Memory("They agreed to meet again.", importance: 80));

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
        Assert.False(MemoryImpressionService.PrepareRequest(state, Today + 1));
        Assert.Equal(requestId, state.ImpressionRequestId);
        Assert.Equal(pendingKeys, state.ImpressionInFlight.Select(MemoryImpressionSources.GetKey));
    }

    [Fact]
    public void NewImportantInteractionRefreshesOnTheNextDayWithoutRepeatingUnchangedWork()
    {
        var state = StateWithImportantMemories();
        CompleteImpression(state, Today);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 60));

        state.LongTermMemories.Add(TestScenarios.Memory("An ordinary greeting.", importance: 20));
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));

        state.LongTermMemories.Add(TestScenarios.Memory("The farmer returned to keep their promise.", importance: 80));
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));
        Assert.True(MemoryImpressionService.ShouldRequest(state, Today + 1));

        CompleteImpression(state, Today + 1);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 2));
    }

    [Fact]
    public void RecallingOrReinforcingTheSameFactsDoesNotRequestAnotherImpression()
    {
        var state = StateWithImportantMemories();
        state.PlayerPreferenceMemories.Add(ImportantPreference());
        CompleteImpression(state, Today - 1);

        foreach (var memory in state.LongTermMemories)
        {
            memory.RecallCount += 4;
            memory.TimesReinforced += 2;
            memory.LastUpdatedTotalDays = Today;
            memory.LastUpdatedTimeOfDay = 1800;
            memory.LastRecalledTotalDays = Today;
            memory.LastRecalledTimeOfDay = 1900;
            memory.Importance += 10;
        }

        var preference = state.PlayerPreferenceMemories[0];
        preference.RecallCount += 3;
        preference.TimesReinforced += 1;
        preference.LastUpdatedTotalDays = Today;
        preference.LastRecalledTotalDays = Today;

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void PreferenceReturningToAnEarlierValueIsANewChangeForTheSameSubject()
    {
        var state = StateWithImportantMemories(2);
        var preference = ImportantPreference();
        preference.Subject = "favorite drink";
        state.PlayerPreferenceMemories.Add(preference);
        string originalSummary = preference.Summary;
        CompleteImpression(state, Today - 1);

        preference.Summary = "The farmer now prefers coffee when they talk together.";
        preference.LastUpdatedTotalDays = Today;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary == preference.Summary);
        MemoryImpressionService.ApplyResult(state, "They now share coffee instead of tea.", Today);

        preference.Summary = originalSummary;
        preference.LastUpdatedTotalDays = Today + 1;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 1));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary == originalSummary);
        MemoryImpressionService.ApplyResult(state, "The farmer has returned to their preference for tea.", Today + 1);

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 2));
    }

    [Theory]
    [InlineData("comfort")]
    [InlineData("trust")]
    [InlineData("hearts")]
    [InlineData("nickname")]
    [InlineData("nickname status")]
    public void MeaningfulRelationshipChangesRefreshEvenWithoutNewFacts(string change)
    {
        var state = StateWithImportantMemories();
        state.FarmerNickname = "小刚";
        state.FarmerNicknameStatus = "Requested";
        CompleteImpression(state, Today - 1);
        switch (change)
        {
            case "comfort": state.InteractionComfortTier = "Close"; break;
            case "trust": state.RelationshipTrust = 74; break;
            case "hearts": state.LastFriendshipHearts += 1; break;
            case "nickname": state.FarmerNickname = "阿刚"; break;
            case "nickname status": state.FarmerNicknameStatus = "Accepted"; break;
        }

        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.NotEqual(state.RelationshipImpressionContext, state.ImpressionContextInFlight);
        Assert.NotEmpty(state.ImpressionInFlight);

        MemoryImpressionService.ApplyResult(state, "Their relationship has changed.", Today);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
    }

    [Fact]
    public void DailyMoodAndTrustChangesWithinTheSameTierDoNotRequestAnUpdate()
    {
        var state = StateWithImportantMemories();
        CompleteImpression(state, Today - 1);
        state.CurrentEmotion = "Grateful";
        state.EmotionIntensity = 90;
        state.RelationshipTrust = 83;
        state.Familiarity += 1;
        state.ConversationsToday += 2;
        state.LastUpdatedTotalDays = Today;

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));
    }

    [Fact]
    public void AnotherSharedOutingRefreshesButItsBookkeepingTimestampAloneDoesNot()
    {
        var state = StateWithImportantMemories(2);
        var outing = ImportantOuting();
        state.SharedExperiences.Add(outing);
        CompleteImpression(state, Today - 1);

        outing.LastUpdatedTotalDays = Today;
        outing.LastUpdatedTimeOfDay = 1600;
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        outing.TimesReinforced += 1;
        Assert.True(MemoryImpressionService.ShouldRequest(state, Today));

        CompleteImpression(state, Today);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
    }

    [Fact]
    public void ResolvingAnImportantConflictRefreshesEvenAfterItsSeverityHasCooled()
    {
        var state = StateWithImportantMemories(2);
        var conflict = TestScenarios.SeriousConflict();
        state.Conflicts.Add(conflict);
        CompleteImpression(state, Today - 1);

        conflict.Severity = 2;
        conflict.LastUpdatedTotalDays = Today;
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today));

        conflict.Status = "Resolved";
        conflict.ResolvedTotalDays = Today;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.Contains("Resolved", StringComparison.Ordinal));
    }

    [Fact]
    public void ImportantConflictBecomingActiveAgainAfterRecoveryRefreshesTheImpression()
    {
        var state = StateWithImportantMemories(2);
        var conflict = TestScenarios.SeriousConflict();
        conflict.CreatedTotalDays = Today - 2;
        conflict.LastUpdatedTotalDays = Today - 2;
        state.Conflicts.Add(conflict);
        CompleteImpression(state, Today - 1);

        conflict.Status = "Recovering";
        conflict.LastUpdatedTotalDays = Today;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.Contains("Recovering", StringComparison.Ordinal));
        MemoryImpressionService.ApplyResult(state, "They have begun to repair the disagreement.", Today);

        conflict.Status = "Active";
        conflict.LastUpdatedTotalDays = Today + 1;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 1));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.Contains("Active", StringComparison.Ordinal));
        MemoryImpressionService.ApplyResult(state, "The disagreement has become active again.", Today + 1);

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 2));
    }

    [Fact]
    public void SuccessfulResultAcknowledgesOnlyEvidenceAndContextCapturedBeforeGeneration()
    {
        var state = StateWithImportantMemories();
        string originalKey = MemoryImpressionSources.GetKey(state.LongTermMemories[0]);
        string originalContext = MemoryImpressionSources.GetRelationshipContext(state);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        state.LongTermMemories[0].Summary = "The farmer changed their mind about a future together.";
        string changedKey = MemoryImpressionSources.GetKey(state.LongTermMemories[0]);
        var newMemory = TestScenarios.Memory("The farmer apologized sincerely.", importance: 85);
        state.LongTermMemories.Add(newMemory);
        state.FarmerNickname = "小刚";
        state.FarmerNicknameStatus = "Accepted";

        MemoryImpressionService.ApplyResult(state, "An impression based on the earlier conversation.", Today);

        Assert.Contains(originalKey, state.RelationshipImpressionSourceKeys);
        Assert.DoesNotContain(changedKey, state.RelationshipImpressionSourceKeys);
        Assert.DoesNotContain(MemoryImpressionSources.GetKey(newMemory), state.RelationshipImpressionSourceKeys);
        Assert.Equal(originalContext, state.RelationshipImpressionContext);
        Assert.NotEqual(MemoryImpressionSources.GetRelationshipContext(state), state.RelationshipImpressionContext);
        Assert.Equal(4, state.LongTermMemories.Count);
        Assert.True(MemoryImpressionService.ShouldRequest(state, Today + 1));
    }

    [Fact]
    public void UpdatedMemoryEvictedDuringGenerationSurvivesTheOlderSnapshotBeingApplied()
    {
        var state = StateWithImportantMemories(2);
        var promise = TestScenarios.Memory("The farmer promised a library visit.", importance: 40, kind: "promise", subject: "library visit");
        state.LongTermMemories.Add(promise);
        string capturedKey = MemoryImpressionSources.GetKey(promise);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        promise.Summary = "The farmer postponed the library visit and promised to come tomorrow.";
        promise.LastUpdatedTotalDays = Today;
        string updatedKey = MemoryImpressionSources.GetKey(promise);
        var retained = state.LongTermMemories.Where(memory => !ReferenceEquals(memory, promise)).ToList();
        for (int i = 0; i < LongTermMemoryStore.MaxMemoriesPerNpc - 2; i++)
        {
            retained.Add(TestScenarios.Memory($"A newer ordinary fact {i}.", importance: 59));
        }

        // Both versions refer to the same promise, but only the old wording reached the model.
        LongTermMemoryStore.ApplyCapacity(state, retained.Append(promise).ToList());

        Assert.Equal(promise.Summary, Assert.Single(state.ImpressionBacklog).Summary);
        Assert.DoesNotContain(state.LongTermMemories, memory => ReferenceEquals(memory, promise));

        MemoryImpressionService.ApplyResult(state, "The farmer had promised a visit.", Today);

        Assert.Contains(capturedKey, state.RelationshipImpressionSourceKeys);
        Assert.DoesNotContain(updatedKey, state.RelationshipImpressionSourceKeys);
        Assert.Equal(promise.Summary, Assert.Single(state.ImpressionBacklog).Summary);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 1));
        Assert.Contains(state.ImpressionInFlight, memory => MemoryImpressionSources.GetKey(memory) == updatedKey);
    }

    [Fact]
    public void BatchLimitLeavesUnsentImportantMemoriesForTheNextRefresh()
    {
        var state = StateWithImportantMemories(23);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.Equal(LongTermMemoryStore.MaxImpressionBatch, state.ImpressionInFlight.Count);
        string[] unsentKeys = state.LongTermMemories.Select(MemoryImpressionSources.GetKey)
            .Except(state.ImpressionInFlight.Select(MemoryImpressionSources.GetKey)).ToArray();

        MemoryImpressionService.ApplyResult(state, "The first batch of memories.", Today);

        Assert.All(unsentKeys, key => Assert.DoesNotContain(key, state.RelationshipImpressionSourceKeys));
        Assert.Equal(23, state.LongTermMemories.Count);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 1));
        Assert.Equal(unsentKeys.OrderBy(key => key), state.ImpressionInFlight.Select(MemoryImpressionSources.GetKey).OrderBy(key => key));

        MemoryImpressionService.ApplyResult(state, "The remaining memories are included.", Today + 1);
        Assert.Equal(23, state.RelationshipImpressionMemoryCount);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 2));
    }

    [Fact]
    public void NewLiveMemoriesCannotStarveTheExistingArchiveBatch()
    {
        var state = StateWithImportantMemories(20);
        for (int i = 0; i < 8; i++)
        {
            state.ImpressionBacklog.Add(TestScenarios.Memory($"An archived interaction {i}.", importance: 20));
        }

        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        Assert.Equal(LongTermMemoryStore.MaxImpressionBatch, state.ImpressionInFlight.Count);
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary.StartsWith("An archived interaction", StringComparison.Ordinal));
        Assert.NotEmpty(state.ImpressionBacklog);
        Assert.Equal(20, state.LongTermMemories.Count);
    }

    [Fact]
    public void EvictingAlreadySummarizedMemoriesDoesNotSummarizeThemAgain()
    {
        var state = StateWithImportantMemories(8);
        CompleteImpression(state, Today);
        var coveredMemories = state.LongTermMemories.ToArray();
        state.LongTermMemories.Clear();
        LongTermMemoryStore.AddToImpressionBacklog(state, coveredMemories);

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 60));
    }

    [Fact]
    public void RelationshipChangeCanRefreshAfterAllUnderlyingMemoriesHaveBeenEvicted()
    {
        var state = StateWithImportantMemories();
        CompleteImpression(state, Today - 1);
        state.LongTermMemories.Clear();
        state.LastFriendshipHearts += 1;

        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.NotEmpty(state.ImpressionInFlight);
        Assert.Empty(state.LongTermMemories);

        MemoryImpressionService.ApplyResult(state, "They feel closer now.", Today);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
    }

    [Fact]
    public void FailedFirstDraftKeepsBothTheSnapshotAndLiveChangesUntilRetrySucceeds()
    {
        var state = StateWithImportantMemories();
        string capturedSummary = state.LongTermMemories[0].Summary;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        state.LongTermMemories[0].Summary = "The farmer followed through on a new promise.";
        state.LongTermMemories.Add(TestScenarios.Memory("They made plans to see each other again.", importance: 70));

        MemoryImpressionService.FailRequest(state, Today);

        Assert.Empty(state.RelationshipImpressionSourceKeys);
        Assert.Empty(state.RelationshipImpressionContext);
        Assert.Empty(state.ImpressionContextInFlight);
        Assert.False(MemoryImpressionService.HasInFlightRequest(state));
        Assert.Equal(4, state.LongTermMemories.Count);
        Assert.Contains(state.ImpressionBacklog, memory => memory.Summary == capturedSummary);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 2));
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 3));
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary == capturedSummary);
        Assert.Contains(state.ImpressionInFlight, memory => memory.Summary == state.LongTermMemories[0].Summary);
        Assert.Equal(state.ImpressionInFlight.Count, state.ImpressionInFlight.Select(MemoryImpressionSources.GetKey).Distinct().Count());

        MemoryImpressionService.ApplyResult(state, "Their first impression is finally ready.", Today + 3);
        Assert.Empty(state.ImpressionBacklog);
        Assert.Equal(-1, state.LastImpressionFailureTotalDays);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 4));
    }

    [Fact]
    public void FailedRefreshDoesNotAdvanceTheLastSuccessfulEvidenceOrRelationshipState()
    {
        var state = StateWithImportantMemories();
        CompleteImpression(state, Today - 1);
        string[] coveredKeys = state.RelationshipImpressionSourceKeys.ToArray();
        string coveredContext = state.RelationshipImpressionContext;
        state.LongTermMemories.Add(TestScenarios.Memory("They overcame a serious disagreement.", importance: 90));
        state.LastFriendshipHearts += 1;
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        MemoryImpressionService.FailRequest(state, Today);

        Assert.Equal(coveredKeys, state.RelationshipImpressionSourceKeys);
        Assert.Equal(coveredContext, state.RelationshipImpressionContext);
        Assert.Equal(Today - 1, state.RelationshipImpressionUpdatedTotalDays);
        Assert.Single(state.ImpressionBacklog);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 2));
        Assert.True(MemoryImpressionService.ShouldRequest(state, Today + 3));
    }

    [Fact]
    public void FailedOriginalVersionReturningAfterAnEvictedChangeUsesTheLatestQueuedOccurrence()
    {
        var state = StateWithImportantMemories(2);
        var original = TestScenarios.Memory("The farmer plans to visit the library.", importance: 40, kind: "promise", subject: "library visit");
        state.LongTermMemories.Add(original);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        MemoryImpressionService.FailRequest(state, Today);

        var changed = LivingNpcState.CloneLongTermMemoryFact(original);
        changed.Summary = "The farmer has cancelled the library visit.";
        changed.LastUpdatedTotalDays = Today + 1;
        EvictVersion(state, changed);

        var returned = LivingNpcState.CloneLongTermMemoryFact(original);
        returned.LastUpdatedTotalDays = Today + 2;
        EvictVersion(state, returned);

        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 3));

        var requested = Assert.Single(state.ImpressionInFlight, memory => memory.Subject == original.Subject);
        Assert.Equal(original.Summary, requested.Summary);
        Assert.Equal(Today + 2, requested.LastUpdatedTotalDays);
        Assert.DoesNotContain(state.ImpressionInFlight, memory => memory.Summary == changed.Summary);

        MemoryImpressionService.ApplyResult(state, "The farmer plans to visit after all.", Today + 3);
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 4));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningToInFlightContentDoesNotLeaveAnIntermediateEvictedVersionPending(bool sameTimestamp)
    {
        var state = StateWithImportantMemories(2);
        var original = TestScenarios.Memory("The farmer plans to visit the library.", importance: 40, kind: "promise", subject: "library visit");
        original.LastUpdatedTotalDays = Today;
        original.LastUpdatedTimeOfDay = 900;
        state.LongTermMemories.Add(original);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));

        var changed = LivingNpcState.CloneLongTermMemoryFact(original);
        changed.Summary = "The farmer has cancelled the library visit.";
        changed.LastUpdatedTimeOfDay = sameTimestamp ? 900 : 1000;
        EvictVersion(state, changed);

        var returned = LivingNpcState.CloneLongTermMemoryFact(original);
        returned.LastUpdatedTimeOfDay = sameTimestamp ? 900 : 1100;
        EvictVersion(state, returned);

        // Several interactions can share the same game clock minute; queue order still matters.
        Assert.True(MemoryImpressionService.HasInFlightRequest(state));
        Assert.Contains(state.ImpressionBacklog, memory => memory.Summary == returned.Summary);
        MemoryImpressionService.ApplyResult(state, "The farmer plans to visit the library.", Today);

        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 1));
        Assert.False(MemoryImpressionService.PrepareRequest(state, Today + 1));
        Assert.False(MemoryImpressionService.ShouldRequest(state, Today + 60));
    }

    private static void EvictVersion(LivingNpcState state, LongTermMemoryFact version)
    {
        var retained = state.LongTermMemories.Where(memory => memory.Subject != version.Subject).ToList();
        while (retained.Count < LongTermMemoryStore.MaxMemoriesPerNpc)
        {
            retained.Add(TestScenarios.Memory($"A retained ordinary fact {retained.Count}.", importance: 59));
        }

        LongTermMemoryStore.ApplyCapacity(state, retained.Append(version).ToList());
        Assert.DoesNotContain(state.LongTermMemories, memory => memory.Subject == version.Subject);
    }

    private static LivingNpcState StateWithImportantMemories(int count = 3)
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < count; i++)
        {
            state.LongTermMemories.Add(TestScenarios.Memory($"A meaningful interaction {i}.", importance: 60));
        }

        return state;
    }

    private static void CompleteImpression(LivingNpcState state, int day)
    {
        Assert.True(MemoryImpressionService.PrepareRequest(state, day));
        MemoryImpressionService.ApplyResult(state, "They have begun to trust each other.", day);
    }

    private static PlayerPreferenceFact ImportantPreference() => new()
    {
        PreferenceKind = "like",
        Subject = "tea",
        Summary = "The farmer prefers tea when they talk together.",
        Importance = 60,
        CreatedTotalDays = Today - 2,
        LastUpdatedTotalDays = Today - 2,
        TimesReinforced = 1
    };

    private static SharedExperienceFact ImportantOuting() => new()
    {
        Key = "outing:river",
        Type = "walk_together",
        Summary = "They watched the river together.",
        Importance = 60,
        CreatedTotalDays = Today - 2,
        LastUpdatedTotalDays = Today - 2,
        TimesReinforced = 1
    };
}
