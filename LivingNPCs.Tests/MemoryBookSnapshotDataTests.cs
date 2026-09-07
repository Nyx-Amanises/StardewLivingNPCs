using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Behavior.Ui;
using Newtonsoft.Json;

namespace LivingNPCs.Tests;

public sealed class MemoryBookSnapshotDataTests
{
    private static string Echo(string key, object? tokens = null)
    {
        return tokens == null ? key : $"{key}{JsonConvert.SerializeObject(tokens)}";
    }

    [Fact]
    public void CreateSnapshotDeepCopiesOnlyBookDataAndSkipsEmptyStates()
    {
        LivingNpcState original = BuildRichState("Leah");
        var empty = new LivingNpcState { NpcName = "Empty" };

        BookSnapshotMessage snapshot = MemoryBookData.CreateBookSnapshot(
            "request-1",
            capturedTotalDays: 42,
            new[] { original, empty });

        BookNpcSnapshot npc = Assert.Single(snapshot.Npcs);
        Assert.Equal("Leah", npc.NpcName);
        Assert.Equal("request-1", snapshot.RequestId);
        Assert.Equal(42, snapshot.CapturedTotalDays);
        Assert.Equal(SyncProtocol.Version, snapshot.SchemaVersion);

        original.RelationshipImpression = "mutated";
        original.LastGiftItemId = "mutated";
        original.LongTermMemories[0].Summary = "mutated";
        original.PlayerPreferenceMemories[0].Summary = "mutated";
        original.SharedExperiences[0].Summary = "mutated";
        original.SharedExperiences[0].Key = "mutated";
        original.SharedExperiences[0].Type = "mutated";
        original.HelpRequests[0].Summary = "mutated";
        original.HelpRequests[0].Type = "mutated";
        original.HelpRequests[0].RequestedItemId = "mutated";
        original.HelpRequests[0].RequestedItemLabel = "mutated";
        original.HelpRequests[0].QuestionTopic = "mutated";
        original.Conflicts[0].Summary = "mutated";

        Assert.Equal("A warm, creative friendship.", npc.RelationshipImpression);
        Assert.Equal("(O)426", npc.LastGiftItemId);
        Assert.Equal("The farmer likes sculpture.", Assert.Single(npc.LongTermMemories).Summary);
        Assert.Equal("Prefers tea.", Assert.Single(npc.PlayerPreferenceMemories).Summary);
        Assert.Equal("Watched the river together.", Assert.Single(npc.SharedExperiences).Summary);
        Assert.Equal("companion_outing:forest", npc.SharedExperiences[0].Key);
        Assert.Equal("companion_outing", npc.SharedExperiences[0].Type);
        Assert.Equal("Bring driftwood.", Assert.Single(npc.HelpRequests).Summary);
        Assert.Equal("item_request", npc.HelpRequests[0].Type);
        Assert.Equal("(O)169", npc.HelpRequests[0].RequestedItemId);
        Assert.Equal("Driftwood", npc.HelpRequests[0].RequestedItemLabel);
        Assert.Equal("sculpture materials", npc.HelpRequests[0].QuestionTopic);
        Assert.Equal("A promise was forgotten.", Assert.Single(npc.Conflicts).Summary);

        string json = JsonConvert.SerializeObject(snapshot);
        Assert.DoesNotContain(nameof(LivingNpcState), json, StringComparison.Ordinal);
        Assert.DoesNotContain("ConversationHistory", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveDialogueBehaviorInfluences", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Mood", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestAndSnapshotRoundTripThroughJson()
    {
        var request = new BookSnapshotRequestMessage { RequestId = "book-abc" };
        BookSnapshotRequestMessage? restoredRequest = JsonConvert.DeserializeObject<BookSnapshotRequestMessage>(
            JsonConvert.SerializeObject(request));

        Assert.NotNull(restoredRequest);
        Assert.Equal(SyncProtocol.Version, restoredRequest!.SchemaVersion);
        Assert.Equal("book-abc", restoredRequest.RequestId);

        LivingNpcState originalState = BuildRichState("Leah");
        LivingNpcState clonedState = originalState.Clone();
        Assert.Equal(originalState.LastGiftItemId, clonedState.LastGiftItemId);
        BookSnapshotMessage original = MemoryBookData.CreateBookSnapshot(
            request.RequestId,
            capturedTotalDays: 42,
            new[] { clonedState });
        BookSnapshotMessage? restored = JsonConvert.DeserializeObject<BookSnapshotMessage>(
            JsonConvert.SerializeObject(original));

        Assert.NotNull(restored);
        Assert.Equal(SyncProtocol.Version, restored!.SchemaVersion);
        Assert.Equal("book-abc", restored.RequestId);
        Assert.Equal(42, restored.CapturedTotalDays);
        BookNpcSnapshot npc = Assert.Single(restored.Npcs);
        Assert.Equal("(O)426", npc.LastGiftItemId);
        Assert.Equal("(O)426", MemoryBookData.BuildStateFromSnapshot(npc).LastGiftItemId);
        Assert.Equal("Grateful", npc.CurrentEmotion);
        Assert.Equal("promise", Assert.Single(npc.LongTermMemories).Kind);
        Assert.Equal("Recovering", Assert.Single(npc.Conflicts).Status);
        Assert.Equal("Fulfilled", Assert.Single(npc.HelpRequests).Status);
        BookSharedExperienceSnapshot experience = Assert.Single(npc.SharedExperiences);
        Assert.Equal("companion_outing:forest", experience.Key);
        Assert.Equal("companion_outing", experience.Type);
        Assert.Equal("item_request", npc.HelpRequests[0].Type);
        Assert.Equal("(O)169", npc.HelpRequests[0].RequestedItemId);
        Assert.Equal("Driftwood", npc.HelpRequests[0].RequestedItemLabel);
        Assert.Equal("sculpture materials", npc.HelpRequests[0].QuestionTopic);
    }

    [Fact]
    public void OlderSnapshotWithoutMomentFormattingFieldsStillMaterializes()
    {
        const string json = @"{
            ""RequestId"": ""older-book"",
            ""CapturedTotalDays"": 42,
            ""Npcs"": [{
                ""NpcName"": ""Leah"",
                ""LastGiftName"": ""Goat Cheese"",
                ""LastGiftTotalDays"": 39,
                ""SharedExperiences"": [{
                    ""Summary"": ""Watched the river together."",
                    ""LocationName"": ""Forest"",
                    ""LocationLabel"": ""Cindersap Forest"",
                    ""CreatedTotalDays"": 35,
                    ""LastUpdatedTotalDays"": 37
                }],
                ""HelpRequests"": [{
                    ""Summary"": ""Bring driftwood."",
                    ""Status"": ""Fulfilled"",
                    ""CreatedTotalDays"": 34
                }]
            }]
        }";

        BookSnapshotMessage? restored = JsonConvert.DeserializeObject<BookSnapshotMessage>(json);

        Assert.NotNull(restored);
        BookNpcSnapshot npc = Assert.Single(restored!.Npcs);
        Assert.Equal(string.Empty, npc.LastGiftItemId);
        BookSharedExperienceSnapshot experience = Assert.Single(npc.SharedExperiences);
        Assert.Equal(string.Empty, experience.Key);
        Assert.Equal(string.Empty, experience.Type);
        BookHelpRequestSnapshot request = Assert.Single(npc.HelpRequests);
        Assert.Equal("item_request", request.Type);
        Assert.Equal(string.Empty, request.RequestedItemId);
        Assert.Equal(string.Empty, request.RequestedItemLabel);
        Assert.Equal(string.Empty, request.QuestionTopic);

        LivingNpcState state = MemoryBookData.BuildStateFromSnapshot(npc);
        Assert.Equal(string.Empty, state.LastGiftItemId);
        Assert.Equal("Goat Cheese", state.LastGiftName);
        Assert.Equal(39, state.LastGiftTotalDays);
        SharedExperienceFact oldExperience = Assert.Single(state.SharedExperiences);
        Assert.Equal("Watched the river together.", oldExperience.Summary);
        Assert.Equal("Forest", oldExperience.LocationName);
        Assert.Equal(string.Empty, oldExperience.Key);
        Assert.Equal(string.Empty, oldExperience.Type);
        NpcHelpRequestFact oldRequest = Assert.Single(state.HelpRequests);
        Assert.Equal("Bring driftwood.", oldRequest.Summary);
        Assert.Equal("Fulfilled", oldRequest.Status);
        Assert.Equal("item_request", oldRequest.Type);
        Assert.Equal(string.Empty, oldRequest.RequestedItemId);
        Assert.Equal(string.Empty, oldRequest.RequestedItemLabel);
        Assert.Equal(string.Empty, oldRequest.QuestionTopic);
    }

    [Fact]
    public void SnapshotSourceMatchesLocalRosterRelationshipMemoryAndMomentPages()
    {
        LivingNpcState state = BuildRichState("Leah");
        BookSnapshotMessage snapshot = MemoryBookData.CreateBookSnapshot(
            "request-equivalence",
            capturedTotalDays: 42,
            new[] { state });
        MemoryBookSnapshotSource source = MemoryBookData.BuildSourceFromSnapshot(
            snapshot,
            name => $"local:{name}",
            _ => 7,
            Echo);

        List<MemoryBookNpcSummary> expectedRoster = MemoryBookData.BuildRoster(
            new[] { state },
            name => $"local:{name}",
            _ => 7,
            nowTotalDays: 42,
            Echo);
        Assert.Equal(expectedRoster, source.Roster);
        Assert.Equal(42, source.CapturedTotalDays);
        Assert.True(source.TryGetState("leah", out LivingNpcState? restored));
        Assert.NotNull(restored);
        AssertMomentFormattingFields(restored!);

        Assert.Equal(
            MemoryBookData.BuildRelationshipCard(state, "local:Leah", 7, 42, Echo),
            MemoryBookData.BuildRelationshipCard(restored!, "local:Leah", 7, 42, Echo));
        Assert.Equal(
            MemoryBookData.BuildMemoryLines(state, 42, Echo),
            MemoryBookData.BuildMemoryLines(restored!, 42, Echo));
        Assert.Equal(
            MemoryBookData.BuildMomentLines(state, 42, Echo),
            MemoryBookData.BuildMomentLines(restored!, 42, Echo));
    }

    [Fact]
    public void SnapshotSourceClonesMessageInputAndEveryStateExport()
    {
        BookSnapshotMessage snapshot = MemoryBookData.CreateBookSnapshot(
            "request-clone",
            capturedTotalDays: 42,
            new[] { BuildRichState("Leah") });
        MemoryBookSnapshotSource source = MemoryBookData.BuildSourceFromSnapshot(
            snapshot,
            name => name,
            _ => 3,
            Echo);

        snapshot.Npcs[0].RelationshipTrust = 1;
        snapshot.Npcs[0].LastGiftItemId = "mutated message";
        snapshot.Npcs[0].LongTermMemories[0].Summary = "mutated message";
        snapshot.Npcs[0].SharedExperiences[0].Key = "mutated message";
        snapshot.Npcs[0].SharedExperiences[0].Type = "mutated message";
        snapshot.Npcs[0].HelpRequests[0].Type = "mutated message";
        snapshot.Npcs[0].HelpRequests[0].RequestedItemId = "mutated message";
        snapshot.Npcs[0].HelpRequests[0].RequestedItemLabel = "mutated message";
        snapshot.Npcs[0].HelpRequests[0].QuestionTopic = "mutated message";

        Assert.True(source.TryGetState("Leah", out LivingNpcState? first));
        Assert.Equal(64, first!.RelationshipTrust);
        Assert.Equal("The farmer likes sculpture.", Assert.Single(first.LongTermMemories).Summary);
        AssertMomentFormattingFields(first);

        first.RelationshipTrust = 2;
        first.LastGiftItemId = "mutated export";
        first.LongTermMemories[0].Summary = "mutated export";
        first.SharedExperiences[0].Key = "mutated export";
        first.SharedExperiences[0].Type = "mutated export";
        first.HelpRequests[0].Type = "mutated export";
        first.HelpRequests[0].RequestedItemId = "mutated export";
        first.HelpRequests[0].RequestedItemLabel = "mutated export";
        first.HelpRequests[0].QuestionTopic = "mutated export";
        Assert.True(source.TryGetState("Leah", out LivingNpcState? second));
        Assert.Equal(64, second!.RelationshipTrust);
        Assert.Equal("The farmer likes sculpture.", Assert.Single(second.LongTermMemories).Summary);
        AssertMomentFormattingFields(second);

        List<LivingNpcState> exported = source.ExportStates();
        exported[0].RelationshipImpression = "mutated list";
        exported[0].LastGiftItemId = "mutated list";
        exported[0].SharedExperiences[0].Key = "mutated list";
        exported[0].SharedExperiences[0].Type = "mutated list";
        exported[0].HelpRequests[0].Type = "mutated list";
        exported[0].HelpRequests[0].RequestedItemId = "mutated list";
        exported[0].HelpRequests[0].RequestedItemLabel = "mutated list";
        exported[0].HelpRequests[0].QuestionTopic = "mutated list";
        Assert.True(source.TryGetState("Leah", out LivingNpcState? third));
        Assert.Equal("A warm, creative friendship.", third!.RelationshipImpression);
        AssertMomentFormattingFields(third);
    }

    private static void AssertMomentFormattingFields(LivingNpcState state)
    {
        Assert.Equal("(O)426", state.LastGiftItemId);
        SharedExperienceFact experience = Assert.Single(state.SharedExperiences);
        Assert.Equal("companion_outing:forest", experience.Key);
        Assert.Equal("companion_outing", experience.Type);
        NpcHelpRequestFact request = Assert.Single(state.HelpRequests);
        Assert.Equal("item_request", request.Type);
        Assert.Equal("(O)169", request.RequestedItemId);
        Assert.Equal("Driftwood", request.RequestedItemLabel);
        Assert.Equal("sculpture materials", request.QuestionTopic);
    }

    private static LivingNpcState BuildRichState(string npcName)
    {
        var state = new LivingNpcState
        {
            NpcName = npcName,
            CurrentEmotion = "Grateful",
            EmotionIntensity = 72,
            InteractionComfortTier = "Trusted",
            RelationshipTrust = 64,
            FarmerNickname = "Artist",
            ConsecutiveConversationDays = 4,
            LastConversationTotalDays = 41,
            RelationshipImpression = "A warm, creative friendship.",
            RelationshipImpressionUpdatedTotalDays = 40,
            LastGiftItemId = "(O)426",
            LastGiftName = "Goat Cheese",
            LastGiftTotalDays = 39,
            LastUpdatedTotalDays = 41
        };
        state.LongTermMemories.Add(new LongTermMemoryFact
        {
            Kind = "promise",
            Summary = "The farmer likes sculpture.",
            Importance = 82,
            LastUpdatedTotalDays = 38,
            TimesReinforced = 3
        });
        state.PlayerPreferenceMemories.Add(new PlayerPreferenceFact
        {
            Summary = "Prefers tea.",
            Importance = 55
        });
        state.SharedExperiences.Add(new SharedExperienceFact
        {
            Key = "companion_outing:forest",
            Type = "companion_outing",
            Summary = "Watched the river together.",
            LocationName = "Forest",
            LocationLabel = "Cindersap Forest",
            CreatedTotalDays = 35,
            LastUpdatedTotalDays = 37
        });
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Type = "item_request",
            Summary = "Bring driftwood.",
            RequestedItemId = "(O)169",
            RequestedItemLabel = "Driftwood",
            QuestionTopic = "sculpture materials",
            Status = "Fulfilled",
            CreatedTotalDays = 34
        });
        state.Conflicts.Add(new NpcConflictFact
        {
            CauseKind = "dialogue",
            Summary = "A promise was forgotten.",
            Severity = 25,
            Status = "Recovering"
        });
        return state;
    }
}
