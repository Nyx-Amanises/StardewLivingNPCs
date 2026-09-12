using LivingNPCs.Behavior;
using Newtonsoft.Json;
using StardewValley;
using StardewValley.Network;

namespace LivingNPCs.Tests;

[CollectionDefinition("Memory revision game state", DisableParallelization = true)]
public sealed class MemoryRevisionGameStateCollection
{
}

[Collection("Memory revision game state")]
public sealed class MemoryRevisionTests
{
    private const int Today = TestScenarios.Today;
    private const string Subject = "library plan";
    private const string Original = "The farmer promised a long visit to browse the library's mineral collection.";
    private const string Correction = "The visit is cancelled.";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShorterLowerImportanceRevisionReplacesTextAndOldTagsTogether(bool preference)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate(Original, 95, "mineral"), Today - 1, 900));

        Assert.True(Store(state, preference, Candidate(Correction, 40, "comfort"), Today, 1000));

        var memory = OnlyMemory(state, preference);
        Assert.Equal(Correction, memory.Summary);
        Assert.Equal(95, memory.Importance);
        Assert.Contains("comfort", memory.Tags);
        Assert.DoesNotContain("mineral", memory.Tags);
        Assert.Equal(Today - 1, memory.CreatedTotalDays);
        Assert.Equal(900, memory.CreatedTimeOfDay);
        Assert.Equal(Today, memory.LastUpdatedTotalDays);
        Assert.Equal(1000, memory.LastUpdatedTimeOfDay);
        Assert.Equal(2, memory.TimesReinforced);
    }

    [Theory]
    [InlineData(false, -1, 1400)]
    [InlineData(false, 0, 900)]
    [InlineData(true, -1, 1400)]
    [InlineData(true, 0, 900)]
    public void EarlierRevisionCannotChangeTheLatestFactOrItsStatistics(bool preference, int dayOffset, int time)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate(Correction, 40, "comfort"), Today, 1000));
        string before = StoredJson(state);

        Assert.False(Store(state, preference, Candidate(Original, 100, "mineral"), Today + dayOffset, time));

        Assert.Equal(before, StoredJson(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeveralRevisionsInOneGameMinuteKeepTheLastAcceptedTextAndTags(bool preference)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate(Original, 95, "mineral"), Today, 900));
        Assert.True(Store(state, preference, Candidate(Correction, 40, "comfort"), Today, 900));
        Assert.Equal(Correction, OnlyMemory(state, preference).Summary);

        Assert.True(Store(state, preference, Candidate(Original, 40, "scholarly"), Today, 900));

        var memory = OnlyMemory(state, preference);
        Assert.Equal(Original, memory.Summary);
        Assert.Contains("scholarly", memory.Tags);
        Assert.DoesNotContain("comfort", memory.Tags);
        Assert.Equal(3, memory.TimesReinforced);
        Assert.Equal(900, memory.LastUpdatedTimeOfDay);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RefreshUsesNewestRevisionRegardlessOfSalienceLengthOrSavedOrder(bool preference, bool reverseOrder)
    {
        var state = TestScenarios.TrustedState();
        var older = Memory(Original, 95, Today - 1, 900, "mineral");
        older.CreatedTimeOfDay = 900;
        older.TimesReinforced = 2;
        older.RecallCount = 7;
        older.LastRecalledTotalDays = Today;
        older.LastRecalledTimeOfDay = 1200;
        var latest = Memory(Correction, 40, Today, 1000, "comfort");
        latest.CreatedTimeOfDay = 1100;
        latest.TimesReinforced = 3;
        latest.RecallCount = 1;
        foreach (var memory in reverseOrder ? new[] { latest, older } : new[] { older, latest })
        {
            AddVersion(state, preference, memory);
        }

        Refresh(state, preference);

        var merged = OnlyMemory(state, preference);
        Assert.Equal(Correction, merged.Summary);
        Assert.Contains("comfort", merged.Tags);
        Assert.DoesNotContain("mineral", merged.Tags);
        Assert.Equal(Today, merged.LastUpdatedTotalDays);
        Assert.Equal(1000, merged.LastUpdatedTimeOfDay);
        Assert.Equal(95, merged.Importance);
        Assert.Equal(5, merged.TimesReinforced);
        Assert.Equal(8, merged.RecallCount);
        Assert.Equal(Today - 10, merged.CreatedTotalDays);
        Assert.Equal(900, merged.CreatedTimeOfDay);
        Assert.Equal(Today, merged.LastRecalledTotalDays);
        Assert.Equal(1200, merged.LastRecalledTimeOfDay);
        string afterFirstRefresh = StoredJson(state);
        Refresh(state, preference);
        Assert.Equal(afterFirstRefresh, StoredJson(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshSameMinuteRevisionsUseLastStoredOccurrenceAndRemainStable(bool preference)
    {
        var state = TestScenarios.TrustedState();
        AddVersion(state, preference, Memory(Original, 95, Today, 900, "mineral"));
        AddVersion(state, preference, Memory(Correction, 70, Today, 900, "comfort"));
        AddVersion(state, preference, Memory(Original, 40, Today, 900, "scholarly"));

        Refresh(state, preference);

        var merged = OnlyMemory(state, preference);
        Assert.Equal(Original, merged.Summary);
        Assert.Contains("scholarly", merged.Tags);
        Assert.DoesNotContain("comfort", merged.Tags);
        Assert.Equal(3, merged.TimesReinforced);
        string before = StoredJson(state);
        Refresh(state, preference);
        Assert.Equal(before, StoredJson(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadResolvesSameMinuteRevisionsBeforeClampCanReorderThem(bool preference)
    {
        var state = TestScenarios.TrustedState();
        AddVersion(state, preference, Memory(Original, 40, Today, 900, "mineral"));
        AddVersion(state, preference, Memory(Correction, 95, Today, 900, "comfort"));

        LivingNpcState loaded = LoadState(state);
        Refresh(loaded, preference);

        var memory = OnlyMemory(loaded, preference);
        Assert.Equal(Correction, memory.Summary);
        Assert.Contains("comfort", memory.Tags);
        Assert.DoesNotContain("mineral", memory.Tags);
        Assert.Equal(2, memory.TimesReinforced);
        Assert.Equal(StoredJson(loaded), StoredJson(LoadState(loaded)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadMergesRevisionsBeforeCapacityCanDropTheLatestCorrection(bool preference)
    {
        var state = TestScenarios.TrustedState();
        AddVersion(state, preference, Memory(Original, 95, Today - 1, 900, "mineral"));
        for (int index = 0; index < LongTermMemoryStore.MaxMemoriesPerNpc - 1; index++)
        {
            AddVersion(state, preference, TestScenarios.Memory($"Independent plan {index}.", 80,
                "promise", $"independent-{index}"));
        }
        AddVersion(state, preference, Memory(Correction, 40, Today, 1000, "comfort"));

        LivingNpcState loaded = LoadState(state);
        Refresh(loaded, preference);

        var memories = preference
            ? loaded.PlayerPreferenceMemories.Select(memory => (memory.Subject, memory.Summary))
            : loaded.LongTermMemories.Select(memory => (memory.Subject, memory.Summary));
        Assert.Equal(LongTermMemoryStore.MaxMemoriesPerNpc, memories.Count());
        Assert.Equal(Correction, Assert.Single(memories, memory => memory.Subject == Subject).Summary);
        Assert.Empty(loaded.ImpressionBacklog);
        Assert.Equal(StoredJson(loaded), StoredJson(LoadState(loaded)));
    }

    [Fact]
    public void LoadKeepsLastSameMinuteItemPolarityBeforeClampSortsByImportance()
    {
        var state = TestScenarios.TrustedState();
        state.PlayerPreferenceMemories.Add(PreferenceRecord("disliked_item", "coffee",
            "Coffee used to be disliked.", 40, Today, 900, "mineral"));
        state.PlayerPreferenceMemories.Add(PreferenceRecord("liked_item_category", "coffee",
            "Coffee is a favorite again.", 95, Today, 900, "comfort"));

        LivingNpcState loaded = LoadState(state);
        PlayerPreferenceMemoryStore.Refresh(loaded, Today);

        var memory = Assert.Single(loaded.PlayerPreferenceMemories);
        Assert.Equal("liked_item_category", memory.PreferenceKind);
        Assert.Equal("Coffee is a favorite again.", memory.Summary);
        Assert.Contains("comfort", memory.Tags);
        Assert.DoesNotContain("mineral", memory.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyCreationDateStillPreventsAnOlderUpdateAndChoosesTheNewerLoadedFact(bool preference)
    {
        var state = TestScenarios.TrustedState();
        var legacy = Memory(Correction, 40, -1, 0, "comfort");
        legacy.CreatedTotalDays = Today;
        legacy.CreatedTimeOfDay = 1100;
        AddVersion(state, preference, legacy);
        string before = StoredJson(state);

        Assert.False(Store(state, preference, Candidate(Original, 100, "mineral"), Today, 1000));
        Assert.Equal(before, StoredJson(state));
        AddVersion(state, preference, Memory(Original, 100, Today - 1, 1400, "mineral"));

        Refresh(state, preference);

        var merged = OnlyMemory(state, preference);
        Assert.Equal(Correction, merged.Summary);
        Assert.Equal(Today, merged.LastUpdatedTotalDays);
        Assert.Equal(1100, merged.LastUpdatedTimeOfDay);
        Assert.DoesNotContain("mineral", merged.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoreCollapsesUnrefreshedDuplicateIdentitiesAroundTheLatestRevision(bool preference)
    {
        var state = TestScenarios.TrustedState();
        AddVersion(state, preference, Memory(Original, 95, Today - 2, 900, "mineral"));
        AddVersion(state, preference, Memory(Correction, 40, Today - 1, 1000, "comfort"));
        const string latest = "The visit is rescheduled.";

        Assert.True(Store(state, preference, Candidate(latest, 40, "practical"), Today, 1000));

        var stored = OnlyMemory(state, preference);
        Assert.Equal(latest, stored.Summary);
        Assert.Equal(3, stored.TimesReinforced);
        Assert.Contains("practical", stored.Tags);
        Assert.DoesNotContain("mineral", stored.Tags);
        Assert.DoesNotContain("comfort", stored.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimilarSubjectsLanguagesAndDifferentKindsDoNotMergeFacts(bool preference)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate(Original, 80), Today, 900));
        var translatedSubject = Candidate("A separately recorded plan.", 70);
        translatedSubject.Subject = "图书馆计划";
        Assert.True(Store(state, preference, translatedSubject, Today, 1000));
        var differentKind = Candidate("The farmer wants quiet at the library.", 60);
        differentKind.Kind = "boundary";
        differentKind.PlayerPreferenceKind = "habit";
        Assert.True(Store(state, preference, differentKind, Today, 1100));

        Refresh(state, preference);

        Assert.Equal(3, preference ? state.PlayerPreferenceMemories.Count : state.LongTermMemories.Count);
        var summaries = preference
            ? state.PlayerPreferenceMemories.Select(memory => memory.Summary)
            : state.LongTermMemories.Select(memory => memory.Summary);
        Assert.Contains(Original, summaries);
        Assert.Contains(translatedSubject.Summary, summaries);
        Assert.Contains(differentKind.Summary, summaries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyRevisionDoesNotEraseAnExistingFact(bool preference)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate(Correction, 50), Today, 900));
        string before = StoredJson(state);

        Assert.False(Store(state, preference, Candidate("  ", 100), Today, 1000));

        Assert.Equal(before, StoredJson(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevisionDuringImpressionKeepsTheSubmittedSnapshotAndRequestsTheCorrection(bool evict)
    {
        var state = StateReadyForImpression();
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        string submitted = JsonConvert.SerializeObject(state.ImpressionInFlight);
        string oldKey = MemoryImpressionSources.GetKey(OnlySubject(state.ImpressionInFlight));

        Assert.True(Store(state, false, Candidate(Correction, 40, "comfort"), Today, 900));
        var corrected = OnlySubject(state.LongTermMemories);
        string newKey = MemoryImpressionSources.GetKey(corrected);
        if (evict)
        {
            Evict(state, corrected);
        }
        Assert.Equal(submitted, JsonConvert.SerializeObject(state.ImpressionInFlight));

        MemoryImpressionService.ApplyResult(state, "The farmer had planned a visit.", Today);

        Assert.Contains(oldKey, state.RelationshipImpressionSourceKeys);
        Assert.DoesNotContain(newKey, state.RelationshipImpressionSourceKeys);
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today + 1));
        var requested = OnlySubject(state.ImpressionInFlight);
        Assert.Equal(Correction, requested.Summary);
        Assert.Contains("comfort", requested.Tags);
        Assert.DoesNotContain("mineral", requested.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderLongTermRevisionCannotReturnFromOutsideTheLiveStore(bool inFlight)
    {
        var state = TestScenarios.TrustedState();
        var latest = Memory(Correction, 40, Today, 1000, "comfort");
        (inFlight ? state.ImpressionInFlight : state.ImpressionBacklog).Add(latest);
        string before = StoredJson(state);

        Assert.False(LongTermMemoryStore.Store(state, Candidate(Original, 100, "mineral"), Today, 900, out var stored));

        Assert.Null(stored);
        Assert.Empty(state.LongTermMemories);
        Assert.Equal(before, StoredJson(state));
    }

    [Fact]
    public void NewRevisionOfAnArchivedFactKeepsItsSalienceButRetiresOnlyItsOldQueuedVersions()
    {
        var state = TestScenarios.TrustedState();
        var old = Memory(Original, 95, Today - 1, 900, "mineral");
        old.TimesReinforced = 4;
        old.RecallCount = 3;
        state.ImpressionBacklog.Add(old);
        var unrelated = TestScenarios.Memory("A different remembered event.", subject: "another fact");
        state.ImpressionBacklog.Add(unrelated);
        state.ImpressionInFlight.Add(LivingNpcState.CloneLongTermMemoryFact(old));
        string submitted = JsonConvert.SerializeObject(state.ImpressionInFlight);

        Assert.True(Store(state, false, Candidate(Correction, 40, "comfort"), Today, 900));

        var stored = OnlyMemory(state, false);
        Assert.Equal(Correction, stored.Summary);
        Assert.Equal(95, stored.Importance);
        Assert.Equal(5, stored.TimesReinforced);
        Assert.Equal(3, stored.RecallCount);
        Assert.Equal(old.CreatedTotalDays, stored.CreatedTotalDays);
        Assert.Same(unrelated, Assert.Single(state.ImpressionBacklog));
        Assert.Equal(submitted, JsonConvert.SerializeObject(state.ImpressionInFlight));
        Assert.Contains(MemoryImpressionSources.GetImportantMemories(state), memory => memory.Summary == Correction);
    }

    [Fact]
    public void ReturningToCapturedTextWithinOneMinuteDoesNotLeaveAnIntermediateQueuedRevision()
    {
        var state = StateReadyForImpression();
        Assert.True(MemoryImpressionService.PrepareRequest(state, Today));
        Assert.True(Store(state, false, Candidate(Correction, 40), Today, 900));
        Evict(state, OnlySubject(state.LongTermMemories));
        Assert.Equal(Correction, OnlySubject(state.ImpressionBacklog).Summary);

        Assert.True(Store(state, false, Candidate(Original, 40), Today, 900));
        Evict(state, OnlySubject(state.LongTermMemories));
        // The pending snapshot already contains the returned text, so it may need no new
        // queue entry. The intermediate cancellation must never become its successor.
        Assert.DoesNotContain(state.ImpressionBacklog, memory => memory.Subject == Subject && memory.Summary != Original);
        MemoryImpressionService.ApplyResult(state, "The farmer plans to visit.", Today);

        Assert.DoesNotContain(state.ImpressionBacklog, memory => memory.Subject == Subject);
        // Ignore the other retained facts: neither version of this source remains pending.
        Assert.DoesNotContain(state.ImpressionBacklog, memory => memory.Summary == Correction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedOldRevisionCreatesNeitherASecondStoreFactNorAHistoryEntry(bool preference)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate(Correction, 40), Today, 1000));
        var candidate = Candidate(Original, 100);
        candidate.PlayerPreference = preference;
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, candidate, entries, Today - 1, 1200);

        Assert.Equal(0, result.LongTermMemoriesStored);
        Assert.Equal(0, result.PlayerPreferencesStored);
        Assert.Empty(entries);
        Assert.Equal(Correction, OnlyMemory(state, preference).Summary);
        if (preference)
        {
            Assert.Empty(state.LongTermMemories);
        }
        else
        {
            Assert.Empty(state.PlayerPreferenceMemories);
        }
    }

    [Fact]
    public void UnrecognizedPreferenceKindStillUsesTheOrdinaryMemoryFallback()
    {
        var state = TestScenarios.TrustedState();
        var candidate = Candidate(Original, 80);
        candidate.PlayerPreference = true;
        candidate.PlayerPreferenceKind = "unknown_kind";
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, candidate, entries, Today, 900);

        Assert.Equal(1, result.LongTermMemoriesStored);
        Assert.Equal(0, result.PlayerPreferencesStored);
        Assert.Empty(state.PlayerPreferenceMemories);
        Assert.Equal(Original, Assert.Single(state.LongTermMemories).Summary);
        Assert.Equal("LongTermMemory", Assert.Single(entries).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("coffee")]
    [InlineData("unmatched_topic")]
    public void OppositeItemPreferenceReplacesTheOldFactBeforeRecallEvenWithoutAMatchingQuery(string? query)
    {
        var state = TestScenarios.TrustedState();
        var original = PreferenceCandidate("liked_item_category", " Evening   Coffee ",
            "The farmer likes sweet evening coffee.", 95, "sweet");
        var correction = PreferenceCandidate("disliked_item", "evening coffee",
            "The farmer no longer drinks evening coffee.", 40, "drink");
        Assert.True(PlayerPreferenceMemoryStore.Store(state, original, Today - 1, 900));

        Assert.True(PlayerPreferenceMemoryStore.Store(state, correction, Today, 1000));
        var plan = MemoryRecallService.BuildPlan(state, TestScenarios.World(), Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 0, preferenceCount: 4, currentTotalDays: Today, currentPlayerText: query);

        var stored = Assert.Single(state.PlayerPreferenceMemories);
        Assert.Equal("disliked_item", stored.PreferenceKind);
        Assert.Equal(correction.Subject, stored.Subject);
        Assert.Equal(correction.Summary, stored.Summary);
        Assert.Equal(95, stored.Importance);
        Assert.DoesNotContain("sweet", stored.Tags);
        Assert.Same(stored, Assert.Single(plan.PlayerPreferences).Memory);
        Assert.Empty(state.LongTermMemories);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LikeDislikeLikeRevisionsKeepTheLatestRealTypeIncludingWithinOneMinute(bool sameTime)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("liked_item_category", "coffee", "Coffee is a favorite.", 95, "sweet"), Today, 900));
        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("disliked_item", "coffee", "The farmer dislikes coffee now.", 40, "practical"),
            Today, sameTime ? 900 : 1000));
        Assert.Equal("disliked_item", Assert.Single(state.PlayerPreferenceMemories).PreferenceKind);
        const string latest = "The farmer likes coffee again.";

        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("liked_item_category", "coffee", latest, 40, "comfort"),
            Today, sameTime ? 900 : 1100));

        var memory = Assert.Single(state.PlayerPreferenceMemories);
        Assert.Equal("liked_item_category", memory.PreferenceKind);
        Assert.Equal(latest, memory.Summary);
        Assert.Equal(3, memory.TimesReinforced);
        Assert.Equal(sameTime ? 900 : 1100, memory.LastUpdatedTimeOfDay);
        Assert.Contains("comfort", memory.Tags);
        Assert.DoesNotContain("sweet", memory.Tags);
        Assert.DoesNotContain("practical", memory.Tags);
        PlayerPreferenceMemoryStore.Refresh(state, Today);
        Assert.Same(memory, Assert.Single(state.PlayerPreferenceMemories));
        Assert.Equal("liked_item_category", memory.PreferenceKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshOppositeItemTypesKeepsTheLatestWholeRevisionRegardlessOfSavedOrder(bool reverseOrder)
    {
        var state = TestScenarios.TrustedState();
        var old = PreferenceRecord("liked_item_category", "coffee", "Coffee was the farmer's very favorite drink.",
            95, Today - 1, 900, "sweet");
        var latest = PreferenceRecord("disliked_item", " Coffee ", "No more coffee.",
            40, Today, 1000, "practical");
        state.PlayerPreferenceMemories.AddRange(reverseOrder ? new[] { latest, old } : new[] { old, latest });

        PlayerPreferenceMemoryStore.Refresh(state, Today);

        var stored = Assert.Single(state.PlayerPreferenceMemories);
        Assert.Same(latest, stored);
        Assert.Equal("disliked_item", stored.PreferenceKind);
        Assert.Equal("No more coffee.", stored.Summary);
        Assert.Contains("practical", stored.Tags);
        Assert.DoesNotContain("sweet", stored.Tags);
        Assert.Equal(95, stored.Importance);
        Assert.Equal(Today, stored.LastUpdatedTotalDays);
        Assert.Equal(1000, stored.LastUpdatedTimeOfDay);
        Assert.Equal(Today - 1, stored.CreatedTotalDays);
        Assert.Equal(2, stored.TimesReinforced);
        string before = StoredJson(state);
        PlayerPreferenceMemoryStore.Refresh(state, Today);
        Assert.Equal(before, StoredJson(state));
    }

    [Fact]
    public void RefreshOppositeTypesInTheSameMinuteKeepsTheLastStoredOccurrence()
    {
        var state = TestScenarios.TrustedState();
        state.PlayerPreferenceMemories.Add(PreferenceRecord("liked_item_category", "coffee", "Coffee was a favorite.",
            95, Today, 900, "sweet"));
        state.PlayerPreferenceMemories.Add(PreferenceRecord("disliked_item", "coffee", "No more coffee.",
            60, Today, 900, "practical"));
        var returned = PreferenceRecord("liked_item_category", "coffee", "Coffee is welcome again.",
            40, Today, 900, "comfort");
        state.PlayerPreferenceMemories.Add(returned);

        PlayerPreferenceMemoryStore.Refresh(state, Today);

        Assert.Same(returned, Assert.Single(state.PlayerPreferenceMemories));
        Assert.Equal("liked_item_category", returned.PreferenceKind);
        Assert.Equal("Coffee is welcome again.", returned.Summary);
        Assert.Contains("comfort", returned.Tags);
        Assert.DoesNotContain("sweet", returned.Tags);
        Assert.DoesNotContain("practical", returned.Tags);
        Assert.Equal(3, returned.TimesReinforced);
    }

    [Theory]
    [InlineData("habit")]
    [InlineData("value")]
    [InlineData("goal")]
    public void ItemPolarityRevisionPreservesIndependentPreferenceKindsForTheSameSubject(string independentKind)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("liked_item_category", "coffee", "Coffee is a favorite.", 95), Today - 2, 900));
        var independent = PreferenceCandidate(independentKind, "coffee", "The farmer grows coffee for the community.", 70);
        Assert.True(PlayerPreferenceMemoryStore.Store(state, independent, Today - 1, 900));

        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("disliked_item", "coffee", "The farmer dislikes drinking coffee.", 40), Today, 900));
        PlayerPreferenceMemoryStore.Refresh(state, Today);
        var plan = MemoryRecallService.BuildPlan(state, TestScenarios.World(), Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 0, preferenceCount: 4, currentTotalDays: Today);

        Assert.Equal(2, state.PlayerPreferenceMemories.Count);
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == independentKind
            && memory.Summary == independent.Summary);
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "disliked_item");
        Assert.DoesNotContain(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "liked_item_category");
        Assert.Equal(2, plan.PlayerPreferences.Count);
    }

    [Theory]
    [InlineData("coffee", "咖啡")]
    [InlineData("coffee", "coffee cake")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    public void ItemPolarityDoesNotMergeDifferentAliasOnlyOrMissingSubjects(string oldSubject, string newSubject)
    {
        var state = TestScenarios.TrustedState();
        // Equal summaries also must not turn two absent subjects into an identified item.
        const string summary = "An item preference was recorded.";
        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("liked_item_category", oldSubject, summary, 95), Today - 1, 900));

        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("disliked_item", newSubject, summary, 40), Today, 900));
        PlayerPreferenceMemoryStore.Refresh(state, Today);

        Assert.Equal(2, state.PlayerPreferenceMemories.Count);
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "liked_item_category");
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "disliked_item");
    }

    [Theory]
    [InlineData(-1, 1400)]
    [InlineData(0, 900)]
    public void OlderOppositeTypeCannotReviveThePreviousPreferenceOrCreateAHistoryEntry(int dayOffset, int time)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(PlayerPreferenceMemoryStore.Store(state,
            PreferenceCandidate("disliked_item", "coffee", "The farmer no longer drinks coffee.", 40), Today, 1000));
        var old = PreferenceCandidate("liked_item_category", "coffee", "Coffee is a favorite.", 100);
        string before = StoredJson(state);

        Assert.False(PlayerPreferenceMemoryStore.Store(state, old, Today + dayOffset, time));
        var entries = new List<BehaviorMemoryEntry>();
        var result = ApplyExchange(state, old, entries, Today + dayOffset, time);

        Assert.Equal(before, StoredJson(state));
        Assert.Equal("disliked_item", Assert.Single(state.PlayerPreferenceMemories).PreferenceKind);
        Assert.Empty(state.LongTermMemories);
        Assert.Empty(entries);
        Assert.Equal(0, result.PlayerPreferencesStored);
        Assert.Equal(0, result.LongTermMemoriesStored);
    }

    [Fact]
    public void RevisionIdentityUnifiesOnlyExplicitItemPolarityWhilePublicKeysStayTypeSpecific()
    {
        Assert.Equal("liked_item_category:coffee", PlayerPreferenceMemoryStore.BuildKey("liked_item_category", "Coffee", "Liked."));
        Assert.Equal("disliked_item:coffee", PlayerPreferenceMemoryStore.BuildKey("disliked_item", "Coffee", "Disliked."));
        Assert.Equal(PlayerPreferenceMemoryStore.BuildRevisionKey("liked_item_category", " Coffee ", "Liked."),
            PlayerPreferenceMemoryStore.BuildRevisionKey("disliked_item", "coffee", "Disliked."));
        Assert.Equal(PlayerPreferenceMemoryStore.BuildKey("habit", "coffee", "A habit."),
            PlayerPreferenceMemoryStore.BuildRevisionKey("habit", "coffee", "A habit."));
        Assert.Equal(PlayerPreferenceMemoryStore.BuildKey("disliked_item", "", "An unspecified item."),
            PlayerPreferenceMemoryStore.BuildRevisionKey("disliked_item", "", "An unspecified item."));
    }

    [Theory]
    [InlineData(false, 60, 90)]
    [InlineData(false, 95, 40)]
    [InlineData(true, 60, 90)]
    [InlineData(true, 95, 40)]
    public void ExchangeBatchKeepsLastExactRevisionRegardlessOfImportance(bool preference, int oldImportance, int latestImportance)
    {
        var state = TestScenarios.TrustedState();
        var original = Candidate(Original, oldImportance, "mineral");
        original.Subject = " Library   Plan ";
        original.PlayerPreference = preference;
        var latest = Candidate(Correction, latestImportance, "comfort");
        latest.PlayerPreference = preference;
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, latest }, entries, Today, 900);

        var stored = OnlyMemory(state, preference);
        Assert.Equal(Correction, stored.Summary);
        Assert.Equal(Subject, stored.Subject);
        Assert.Equal(latestImportance, stored.Importance);
        Assert.Contains("comfort", stored.Tags);
        Assert.DoesNotContain("mineral", stored.Tags);
        Assert.Equal(1, stored.TimesReinforced);
        Assert.Equal(preference ? 0 : 1, result.LongTermMemoriesStored);
        Assert.Equal(preference ? 1 : 0, result.PlayerPreferencesStored);
        Assert.Equal(Correction, Assert.Single(entries).Reason);
        var recall = MemoryRecallService.BuildPlan(state, TestScenarios.World(), Array.Empty<BehaviorMemoryEntry>(),
            longTermCount: 4, preferenceCount: 4, currentTotalDays: Today);
        Assert.Equal(Correction, preference
            ? Assert.Single(recall.PlayerPreferences).Memory.Summary
            : Assert.Single(recall.LongTermMemories).Memory.Summary);
    }

    [Theory]
    [InlineData(false, 60, 90)]
    [InlineData(false, 95, 40)]
    [InlineData(true, 60, 90)]
    [InlineData(true, 95, 40)]
    public void ExchangeBatchKeepsLatestItemPolarityOnlyOnce(bool latestLiked, int oldImportance, int latestImportance)
    {
        var state = TestScenarios.TrustedState();
        var original = PreferenceCandidate(latestLiked ? "disliked_item" : "liked_item_category", " Evening   Coffee ",
            latestLiked ? "The farmer dislikes evening coffee." : "The farmer likes evening coffee.", oldImportance, "sweet");
        var latest = PreferenceCandidate(latestLiked ? "liked_item_category" : "disliked_item", "evening coffee",
            latestLiked ? "Evening coffee is a favorite again." : "The farmer no longer likes evening coffee.", latestImportance, "comfort");
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, latest }, entries, Today, 900);

        var stored = Assert.Single(state.PlayerPreferenceMemories);
        Assert.Equal(latest.PlayerPreferenceKind, stored.PreferenceKind);
        Assert.Equal(latest.Summary, stored.Summary);
        Assert.Equal(latestImportance, stored.Importance);
        Assert.Equal(1, stored.TimesReinforced);
        Assert.Contains("comfort", stored.Tags);
        Assert.DoesNotContain("sweet", stored.Tags);
        Assert.Empty(state.LongTermMemories);
        Assert.Equal(1, result.PlayerPreferencesStored);
        Assert.Equal(0, result.LongTermMemoriesStored);
        Assert.Equal(latest.Summary, Assert.Single(entries).Reason);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 39)]
    [InlineData(true, 0)]
    [InlineData(true, 39)]
    public void ExchangeBatchSuppressesEarlierRevisionWhenLatestFailsImportanceThreshold(bool preference, int latestImportance)
    {
        var state = TestScenarios.TrustedState();
        var original = Candidate(Original, 100);
        var latest = Candidate(Correction, latestImportance);
        var independent = Candidate("The farmer plans to visit the museum.", 60);
        independent.Subject = "museum plan";
        foreach (var candidate in new[] { original, independent, latest })
        {
            candidate.PlayerPreference = preference;
        }
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, independent, latest }, entries, Today, 900);

        Assert.Equal(independent.Summary, OnlyMemory(state, preference).Summary);
        Assert.Equal(independent.Summary, Assert.Single(entries).Reason);
        Assert.Equal(preference ? 0 : 1, result.LongTermMemoriesStored);
        Assert.Equal(preference ? 1 : 0, result.PlayerPreferencesStored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExchangeBatchBelowThresholdLeavesExistingMemoryAndHistoryUntouched(bool preference)
    {
        var state = TestScenarios.TrustedState();
        Assert.True(Store(state, preference, Candidate("The visit had already been rescheduled.", 75), Today - 1, 900));
        string before = StoredJson(state);
        var original = Candidate(Original, 100);
        original.PlayerPreference = preference;
        var latest = Candidate(Correction, 39);
        latest.PlayerPreference = preference;
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, latest }, entries, Today, 900);

        // Under-threshold metadata neither writes a new revision nor promotes an earlier
        // candidate from this batch; the existing durable record retains its prior state.
        Assert.Equal(before, StoredJson(state));
        Assert.Empty(entries);
        Assert.Equal(0, result.LongTermMemoriesStored);
        Assert.Equal(0, result.PlayerPreferencesStored);
    }

    [Fact]
    public void ExchangeBatchKeepsDifferentLongTermKindsSubjectsAndStoreRoutes()
    {
        var state = TestScenarios.TrustedState();
        var original = Candidate(Original, 80);
        var boundary = Candidate("The farmer needs quiet at the library.", 80);
        boundary.Kind = "boundary";
        var otherSubject = Candidate("The farmer promised to visit the museum.", 80);
        otherSubject.Subject = "museum plan";
        var preference = PreferenceCandidate("goal", Subject, "The farmer hopes to learn more about minerals.", 80);
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, boundary, otherSubject, preference }, entries, Today, 900);

        Assert.Equal(3, state.LongTermMemories.Count);
        Assert.Contains(state.LongTermMemories, memory => memory.Summary == original.Summary);
        Assert.Contains(state.LongTermMemories, memory => memory.Summary == boundary.Summary);
        Assert.Contains(state.LongTermMemories, memory => memory.Summary == otherSubject.Summary);
        Assert.Equal(preference.Summary, Assert.Single(state.PlayerPreferenceMemories).Summary);
        Assert.Equal(3, result.LongTermMemoriesStored);
        Assert.Equal(1, result.PlayerPreferencesStored);
        Assert.Equal(4, entries.Count);
    }

    [Fact]
    public void ExchangeBatchKeepsIndependentPreferenceKindsAndTranslatedSubjects()
    {
        var state = TestScenarios.TrustedState();
        var candidates = new[]
        {
            PreferenceCandidate("liked_item_category", "coffee", "The farmer likes coffee.", 95),
            PreferenceCandidate("habit", "coffee", "The farmer carries a coffee flask.", 80),
            PreferenceCandidate("value", "coffee", "The farmer values locally grown coffee.", 75),
            PreferenceCandidate("disliked_item", "咖啡", "The farmer dislikes this separately identified item.", 40)
        };
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, candidates, entries, Today, 900);

        Assert.Equal(4, state.PlayerPreferenceMemories.Count);
        Assert.All(candidates, candidate => Assert.Contains(state.PlayerPreferenceMemories,
            memory => memory.PreferenceKind == candidate.PlayerPreferenceKind && memory.Subject == candidate.Subject
                && memory.Summary == candidate.Summary));
        Assert.Equal(4, result.PlayerPreferencesStored);
        Assert.Empty(state.LongTermMemories);
        Assert.Equal(4, entries.Count);
    }

    [Fact]
    public void ExchangeBatchDoesNotTreatMissingItemSubjectsAsOneIdentity()
    {
        var state = TestScenarios.TrustedState();
        const string summary = "An item preference was recorded.";
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[]
        {
            PreferenceCandidate("liked_item_category", "", summary, 80),
            PreferenceCandidate("disliked_item", "", summary, 80)
        }, entries, Today, 900);

        Assert.Equal(2, state.PlayerPreferenceMemories.Count);
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "liked_item_category");
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "disliked_item");
        Assert.Equal(2, result.PlayerPreferencesStored);
        Assert.Equal(2, entries.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExchangeBatchKeepsDifferentSummaryIdentitiesWhenSubjectIsMissing(bool preference)
    {
        var state = TestScenarios.TrustedState();
        var original = Candidate(Original, 80);
        var latest = Candidate(Correction, 80);
        foreach (var candidate in new[] { original, latest })
        {
            candidate.Subject = "";
            candidate.PlayerPreference = preference;
        }
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, latest }, entries, Today, 900);

        Assert.Equal(2, preference ? state.PlayerPreferenceMemories.Count : state.LongTermMemories.Count);
        Assert.Equal(preference ? 0 : 2, result.LongTermMemoriesStored);
        Assert.Equal(preference ? 2 : 0, result.PlayerPreferencesStored);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ExchangeBatchInvalidPreferenceKindUsesOrdinaryRevisionIdentity()
    {
        var state = TestScenarios.TrustedState();
        var original = Candidate(Original, 60);
        var latest = Candidate(Correction, 90);
        latest.PlayerPreference = true;
        latest.PlayerPreferenceKind = "unknown_kind";
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { original, latest }, entries, Today, 900);

        Assert.Equal(Correction, Assert.Single(state.LongTermMemories).Summary);
        Assert.Empty(state.PlayerPreferenceMemories);
        Assert.Equal(1, result.LongTermMemoriesStored);
        Assert.Equal(0, result.PlayerPreferencesStored);
        Assert.Equal(Correction, Assert.Single(entries).Reason);
    }

    [Fact]
    public void ExchangeBatchEmptyCandidateDoesNotReplaceValidParsedRevision()
    {
        var state = TestScenarios.TrustedState();
        var entries = new List<BehaviorMemoryEntry>();

        var result = ApplyExchange(state, new[] { Candidate(Original, 60), Candidate("  ", 100) }, entries, Today, 900);

        Assert.Equal(Original, Assert.Single(state.LongTermMemories).Summary);
        Assert.Equal(1, result.LongTermMemoriesStored);
        Assert.Equal(Original, Assert.Single(entries).Reason);
    }

    private static ValleyTalkMemoryCandidate PreferenceCandidate(string kind, string subject, string summary,
        int importance, params string[] tags) => new()
    {
        Kind = "preference",
        PlayerPreference = true,
        PlayerPreferenceKind = kind,
        Subject = subject,
        Summary = summary,
        Importance = importance,
        Tags = tags.ToList()
    };

    private static PlayerPreferenceFact PreferenceRecord(string kind, string subject, string summary,
        int importance, int day, int time, params string[] tags) => new()
    {
        PreferenceKind = kind,
        Subject = subject,
        Summary = summary,
        Importance = importance,
        Tags = tags.ToList(),
        CreatedTotalDays = day,
        CreatedTimeOfDay = time,
        LastUpdatedTotalDays = day,
        LastUpdatedTimeOfDay = time,
        TimesReinforced = 1
    };

    private static ValleyTalkMemoryCandidate Candidate(string summary, int importance, params string[] tags) => new()
    {
        Kind = "promise",
        PlayerPreferenceKind = "goal",
        Subject = Subject,
        Summary = summary,
        Importance = importance,
        Tags = tags.ToList()
    };

    private static LongTermMemoryFact Memory(string summary, int importance, int day, int time, params string[] tags) => new()
    {
        Kind = "promise",
        Subject = Subject,
        Summary = summary,
        Importance = importance,
        Tags = tags.ToList(),
        CreatedTotalDays = Today - 10,
        CreatedTimeOfDay = 900,
        LastUpdatedTotalDays = day,
        LastUpdatedTimeOfDay = time,
        TimesReinforced = 1
    };

    private static bool Store(LivingNpcState state, bool preference, ValleyTalkMemoryCandidate candidate, int day, int time) =>
        preference ? PlayerPreferenceMemoryStore.Store(state, candidate, day, time)
            : LongTermMemoryStore.Store(state, candidate, day, time, out _);

    private static void Refresh(LivingNpcState state, bool preference)
    {
        if (preference)
        {
            PlayerPreferenceMemoryStore.Refresh(state, Today);
        }
        else
        {
            LongTermMemoryStore.Refresh(state, Today);
        }
    }

    private static void AddVersion(LivingNpcState state, bool preference, LongTermMemoryFact memory)
    {
        if (!preference)
        {
            state.LongTermMemories.Add(memory);
            return;
        }

        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact
        {
            PreferenceKind = "goal",
            Subject = memory.Subject,
            Summary = memory.Summary,
            Tags = memory.Tags.ToList(),
            Importance = memory.Importance,
            CreatedTotalDays = memory.CreatedTotalDays,
            CreatedTimeOfDay = memory.CreatedTimeOfDay,
            LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
            LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
            LastRecalledTotalDays = memory.LastRecalledTotalDays,
            LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
            TimesReinforced = memory.TimesReinforced,
            RecallCount = memory.RecallCount
        });
    }

    private static LongTermMemoryFact OnlyMemory(LivingNpcState state, bool preference)
    {
        if (!preference)
        {
            return Assert.Single(state.LongTermMemories);
        }

        var memory = Assert.Single(state.PlayerPreferenceMemories);
        return new LongTermMemoryFact
        {
            Kind = memory.PreferenceKind,
            Subject = memory.Subject,
            Summary = memory.Summary,
            Tags = memory.Tags.ToList(),
            Importance = memory.Importance,
            CreatedTotalDays = memory.CreatedTotalDays,
            CreatedTimeOfDay = memory.CreatedTimeOfDay,
            LastUpdatedTotalDays = memory.LastUpdatedTotalDays,
            LastUpdatedTimeOfDay = memory.LastUpdatedTimeOfDay,
            LastRecalledTotalDays = memory.LastRecalledTotalDays,
            LastRecalledTimeOfDay = memory.LastRecalledTimeOfDay,
            TimesReinforced = memory.TimesReinforced,
            RecallCount = memory.RecallCount
        };
    }

    private static string StoredJson(LivingNpcState state) => JsonConvert.SerializeObject(new
    {
        state.LongTermMemories,
        state.PlayerPreferenceMemories,
        state.ImpressionBacklog,
        state.ImpressionInFlight
    });

    private static LivingNpcState LoadState(LivingNpcState state)
    {
        var saveData = new BehaviorMemorySaveData { StatesByNpc = { [state.NpcName] = state } };
        var deserialized = JsonConvert.DeserializeObject<BehaviorMemorySaveData>(JsonConvert.SerializeObject(saveData));
        var memory = new BehaviorMemory();
        var originalWorldState = Game1.netWorldState;
        Game1.netWorldState = new(new NetWorldState());
        try
        {
            memory.Load(deserialized, maxEntriesPerNpc: 30);
            return memory.ToSaveData().StatesByNpc[state.NpcName];
        }
        finally
        {
            Game1.netWorldState = originalWorldState;
        }
    }

    private static LivingNpcState StateReadyForImpression()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory("A meaningful shared conversation.", importance: 60, subject: "conversation"));
        state.LongTermMemories.Add(TestScenarios.Memory("A meaningful shared event.", importance: 60, subject: "event"));
        Assert.True(Store(state, false, Candidate(Original, 95, "mineral"), Today, 900));
        return state;
    }

    private static LongTermMemoryFact OnlySubject(IEnumerable<LongTermMemoryFact> memories) =>
        Assert.Single(memories, memory => memory.Subject == Subject);

    private static void Evict(LivingNpcState state, LongTermMemoryFact memory)
    {
        var retained = state.LongTermMemories.Where(item => !ReferenceEquals(item, memory)).ToList();
        while (retained.Count < LongTermMemoryStore.MaxMemoriesPerNpc)
        {
            retained.Add(TestScenarios.Memory($"An important retained fact {retained.Count}.", importance: 100,
                kind: "boundary", subject: $"retained-{retained.Count}"));
        }
        LongTermMemoryStore.ApplyCapacity(state, retained.Append(memory).ToList());
        Assert.DoesNotContain(state.LongTermMemories, item => item.Subject == Subject);
    }

    private static ValleyTalkExchangeResult ApplyExchange(LivingNpcState state, ValleyTalkMemoryCandidate candidate,
        List<BehaviorMemoryEntry> entries, int day, int time) =>
        ApplyExchange(state, new[] { candidate }, entries, day, time);

    private static ValleyTalkExchangeResult ApplyExchange(LivingNpcState state, IReadOnlyList<ValleyTalkMemoryCandidate> candidates,
        List<BehaviorMemoryEntry> entries, int day, int time)
    {
        BehaviorMemoryEntry CreateEntry(string npcName, string kind, string action, string reason) => new()
        {
            NpcName = npcName,
            Kind = kind,
            Action = action,
            Reason = reason,
            TotalDays = day,
            TimeOfDay = time
        };
        void AddEntry(BehaviorMemoryEntry entry, int _) => entries.Add(entry);
        var helpRequests = new HelpRequestMemoryService(
            (_, _, _) => { }, (_, _) => { }, (_, _, _, _) => { }, CreateEntry, AddEntry, (_, fallback) => fallback);
        var application = new ExchangeApplicationService(
            helpRequests,
            (npc, kind, action, reason) => CreateEntry(npc.Name, kind, action, reason),
            AddEntry,
            (target, memory) => PlayerPreferenceMemoryStore.Store(target, memory, day, time),
            (target, memory) => LongTermMemoryStore.Store(target, memory, day, time, out _),
            (_, _) => false,
            (_, _, _, _) => false,
            (_, _) => false,
            (_, _, _, _) => 0,
            (_, _, _) => 0,
            () => (day, time));
        return application.Apply(new NPC { Name = state.NpcName, displayName = state.NpcName }, state,
            "Hello.", "Hello.", JsonConvert.SerializeObject(new { memories = candidates }),
            maxEntriesPerNpc: 30, maxPendingHelpRequestsPerNpc: 0, helpRequestCooldownDays: 2,
            maxExtraFriendshipPerDay: 0, maxDialogueBehaviorInfluenceDays: 3, allowHelpRequestProgress: false);
    }
}
