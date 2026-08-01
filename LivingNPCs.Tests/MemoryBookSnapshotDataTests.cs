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
        original.LongTermMemories[0].Summary = "mutated";
        original.PlayerPreferenceMemories[0].Summary = "mutated";
        original.SharedExperiences[0].Summary = "mutated";
        original.HelpRequests[0].Summary = "mutated";
        original.Conflicts[0].Summary = "mutated";

        Assert.Equal("A warm, creative friendship.", npc.RelationshipImpression);
        Assert.Equal("The farmer likes sculpture.", Assert.Single(npc.LongTermMemories).Summary);
        Assert.Equal("Prefers tea.", Assert.Single(npc.PlayerPreferenceMemories).Summary);
        Assert.Equal("Watched the river together.", Assert.Single(npc.SharedExperiences).Summary);
        Assert.Equal("Bring driftwood.", Assert.Single(npc.HelpRequests).Summary);
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

        BookSnapshotMessage original = MemoryBookData.CreateBookSnapshot(
            request.RequestId,
            capturedTotalDays: 42,
            new[] { BuildRichState("Leah") });
        BookSnapshotMessage? restored = JsonConvert.DeserializeObject<BookSnapshotMessage>(
            JsonConvert.SerializeObject(original));

        Assert.NotNull(restored);
        Assert.Equal(SyncProtocol.Version, restored!.SchemaVersion);
        Assert.Equal("book-abc", restored.RequestId);
        Assert.Equal(42, restored.CapturedTotalDays);
        BookNpcSnapshot npc = Assert.Single(restored.Npcs);
        Assert.Equal("Grateful", npc.CurrentEmotion);
        Assert.Equal("promise", Assert.Single(npc.LongTermMemories).Kind);
        Assert.Equal("Recovering", Assert.Single(npc.Conflicts).Status);
        Assert.Equal("Fulfilled", Assert.Single(npc.HelpRequests).Status);
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
        snapshot.Npcs[0].LongTermMemories[0].Summary = "mutated message";

        Assert.True(source.TryGetState("Leah", out LivingNpcState? first));
        Assert.Equal(64, first!.RelationshipTrust);
        Assert.Equal("The farmer likes sculpture.", Assert.Single(first.LongTermMemories).Summary);

        first.RelationshipTrust = 2;
        first.LongTermMemories[0].Summary = "mutated export";
        Assert.True(source.TryGetState("Leah", out LivingNpcState? second));
        Assert.Equal(64, second!.RelationshipTrust);
        Assert.Equal("The farmer likes sculpture.", Assert.Single(second.LongTermMemories).Summary);

        List<LivingNpcState> exported = source.ExportStates();
        exported[0].RelationshipImpression = "mutated list";
        Assert.True(source.TryGetState("Leah", out LivingNpcState? third));
        Assert.Equal("A warm, creative friendship.", third!.RelationshipImpression);
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
            Summary = "Watched the river together.",
            LocationName = "Forest",
            LocationLabel = "Cindersap Forest",
            CreatedTotalDays = 35,
            LastUpdatedTotalDays = 37
        });
        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            Summary = "Bring driftwood.",
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
