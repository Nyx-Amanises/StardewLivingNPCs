using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Tests.Dialogue.Llm;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Tests.Multiplayer;

public sealed class ExchangeReportSyncTests
{
    private const string ModId = "Yuki.LivingNPCs";

    [Fact]
    public void FarmhandReportRoundTripsEveryRequiredFieldToHost()
    {
        (InMemoryModMessageNetwork network, MultiplayerSyncService host, MultiplayerSyncService farmhand, _) = CreateConnectedPair();
        ExchangeReportMessage? received = null;
        host.RemoteExchangeReceived = (_, message) => received = message;

        bool accepted = farmhand.TrySendExchangeReport("Emily", "How are you?", "Wonderful!", "{\"rapportDelta\":3}");

        Assert.True(accepted);
        Assert.NotNull(received);
        Assert.Equal(SyncProtocol.Version, received!.SchemaVersion);
        Assert.Equal("Emily", received.NpcName);
        Assert.Equal("Farmhand", received.PlayerName);
        Assert.Equal("How are you?", received.PlayerText);
        Assert.Equal("Wonderful!", received.NpcResponse);
        Assert.Equal("{\"rapportDelta\":3}", received.AnalysisJson);
        Assert.Equal(123, received.TotalDays);
        Assert.Equal(1640, received.TimeOfDay);
        Assert.True(Guid.TryParseExact(received.ReportId, "N", out _));
        Assert.Single(network.Sent, message => message.Type == SyncProtocol.TypeExchangeReport);
    }

    [Fact]
    public void HostDeduplicatesPendingReportAndReplaysCompletedAckWithoutReapplying()
    {
        (InMemoryModMessageNetwork network, MultiplayerSyncService host, InMemoryModMessageBus farmhandBus) =
            CreateHostWithCompatibleRawFarmhand();
        int callbacks = 0;
        host.RemoteExchangeReceived = (playerId, message) =>
        {
            Assert.Equal(2, playerId);
            Assert.Equal("report-1", message.ReportId);
            callbacks++;
        };
        var report = new ExchangeReportMessage
        {
            ReportId = "report-1",
            NpcName = "Emily",
            PlayerName = "Farmhand",
            PlayerText = "hello",
            NpcResponse = "hi",
            AnalysisJson = "{}"
        };

        farmhandBus.Send(report, SyncProtocol.TypeExchangeReport, new[] { 1L });
        farmhandBus.Send(report, SyncProtocol.TypeExchangeReport, new[] { 1L });

        Assert.Equal(1, callbacks);
        Assert.DoesNotContain(network.Sent, message => message.Type == SyncProtocol.TypeExchangeAck);

        var acknowledgement = new ExchangeAckMessage
        {
            ReportId = report.ReportId,
            NpcName = report.NpcName,
            FriendshipDelta = 4,
            FeedbackMessages = new List<string> { "applied once" }
        };
        host.SendExchangeAck(2, acknowledgement);
        farmhandBus.Send(report, SyncProtocol.TypeExchangeReport, new[] { 1L });

        Assert.Equal(1, callbacks);
        ExchangeAckMessage[] acknowledgements = network.Sent
            .Where(message => message.Type == SyncProtocol.TypeExchangeAck)
            .Select(message => Assert.IsType<ExchangeAckMessage>(message.Payload))
            .ToArray();
        Assert.Equal(2, acknowledgements.Length);
        Assert.All(acknowledgements, replayed =>
        {
            Assert.Equal("report-1", replayed.ReportId);
            Assert.Equal("Emily", replayed.NpcName);
            Assert.Equal(4, replayed.FriendshipDelta);
            Assert.Equal(new[] { "applied once" }, replayed.FeedbackMessages);
        });
        Assert.All(
            network.Sent.Where(message => message.Type == SyncProtocol.TypeExchangeAck),
            message => Assert.Equal(2, message.ToPlayerId));
    }

    [Fact]
    public void ItemDeliveryRequestAndResultRoundTripOnlyBetweenFarmhandAndHost()
    {
        (InMemoryModMessageNetwork network, MultiplayerSyncService host, MultiplayerSyncService farmhand, _) = CreateConnectedPair();
        network.AddEndpoint(3, isHost: false);
        network.Sent.Clear();
        long receivedFromPlayerId = -1;
        ItemDeliveryRequestMessage? receivedRequest = null;
        ItemDeliveryResultMessage? receivedResult = null;
        host.RemoteItemDeliveryReceived = (playerId, message) =>
        {
            receivedFromPlayerId = playerId;
            receivedRequest = message;
        };
        farmhand.ItemDeliveryResultReceived = message => receivedResult = message;

        bool sent = farmhand.TrySendItemDeliveryRequest(
            "quest-1",
            "Emily",
            "(O)80",
            "Quartz",
            itemQuality: 2);

        Assert.True(sent);
        Assert.Equal(2, receivedFromPlayerId);
        Assert.NotNull(receivedRequest);
        Assert.True(Guid.TryParseExact(receivedRequest!.RequestId, "N", out _));
        Assert.Equal("quest-1", receivedRequest.QuestLogId);
        Assert.Equal("Emily", receivedRequest.NpcName);
        Assert.Equal("(O)80", receivedRequest.ItemId);
        Assert.Equal("Quartz", receivedRequest.ItemLabel);
        Assert.Equal(2, receivedRequest.ItemQuality);
        SentModMessage requestWireMessage = Assert.Single(
            network.Sent,
            message => message.Type == SyncProtocol.TypeItemDeliveryRequest);
        Assert.Equal((2L, 1L), (requestWireMessage.FromPlayerId, requestWireMessage.ToPlayerId));

        host.SendItemDeliveryResult(
            2,
            new ItemDeliveryResultMessage
            {
                RequestId = receivedRequest.RequestId,
                QuestLogId = receivedRequest.QuestLogId,
                NpcName = receivedRequest.NpcName,
                ItemId = receivedRequest.ItemId,
                ItemLabel = receivedRequest.ItemLabel,
                ItemQuality = receivedRequest.ItemQuality,
                TasteScore = 4,
                Accepted = false,
                FailureReason = "test rejection"
            });

        Assert.NotNull(receivedResult);
        Assert.Equal(receivedRequest.RequestId, receivedResult!.RequestId);
        Assert.Equal("quest-1", receivedResult.QuestLogId);
        Assert.Equal(4, receivedResult.TasteScore);
        Assert.False(receivedResult.Accepted);
        SentModMessage resultWireMessage = Assert.Single(
            network.Sent,
            message => message.Type == SyncProtocol.TypeItemDeliveryResult);
        Assert.Equal((1L, 2L), (resultWireMessage.FromPlayerId, resultWireMessage.ToPlayerId));
        Assert.DoesNotContain(
            network.Sent,
            message => (message.Type is SyncProtocol.TypeItemDeliveryRequest or SyncProtocol.TypeItemDeliveryResult)
                && message.ToPlayerId == 3);
    }

    [Fact]
    public void QuestRewardClaimedRoundTripsOnlyFromFarmhandToHost()
    {
        (InMemoryModMessageNetwork network, MultiplayerSyncService host, MultiplayerSyncService farmhand, _) = CreateConnectedPair();
        network.AddEndpoint(3, isHost: false);
        network.Sent.Clear();
        long receivedFromPlayerId = -1;
        QuestRewardClaimedMessage? received = null;
        host.RemoteQuestRewardClaimed = (playerId, message) =>
        {
            receivedFromPlayerId = playerId;
            received = message;
        };

        bool sent = farmhand.TrySendQuestRewardClaimed("quest-reward-1");

        Assert.True(sent);
        Assert.Equal(2, receivedFromPlayerId);
        Assert.NotNull(received);
        Assert.Equal(SyncProtocol.Version, received!.SchemaVersion);
        Assert.Equal("quest-reward-1", received.QuestLogId);
        SentModMessage wireMessage = Assert.Single(
            network.Sent,
            message => message.Type == SyncProtocol.TypeQuestRewardClaimed);
        Assert.Equal((2L, 1L), (wireMessage.FromPlayerId, wireMessage.ToPlayerId));
        Assert.DoesNotContain(
            network.Sent,
            message => message.Type == SyncProtocol.TypeQuestRewardClaimed && message.ToPlayerId == 3);
    }

    [Fact]
    public void WorldActionMessagesIgnoreWrongSchemaAndItemResultFromNonHost()
    {
        (InMemoryModMessageNetwork network, MultiplayerSyncService host, MultiplayerSyncService farmhand, InMemoryModMessageBus farmhandBus) =
            CreateConnectedPair();
        InMemoryModMessageBus nonHostBus = network.AddEndpoint(3, isHost: false);
        int deliveryRequests = 0;
        int deliveryResults = 0;
        int rewardClaims = 0;
        host.RemoteItemDeliveryReceived = (_, _) => deliveryRequests++;
        host.RemoteQuestRewardClaimed = (_, _) => rewardClaims++;
        farmhand.ItemDeliveryResultReceived = _ => deliveryResults++;
        network.Sent.Clear();

        farmhandBus.Send(
            new ItemDeliveryRequestMessage
            {
                SchemaVersion = SyncProtocol.Version + 1,
                RequestId = "bad-request",
                QuestLogId = "quest-1"
            },
            SyncProtocol.TypeItemDeliveryRequest,
            new[] { 1L });
        farmhandBus.Send(
            new QuestRewardClaimedMessage
            {
                SchemaVersion = SyncProtocol.Version + 1,
                QuestLogId = "quest-1"
            },
            SyncProtocol.TypeQuestRewardClaimed,
            new[] { 1L });
        host.SendItemDeliveryResult(
            2,
            new ItemDeliveryResultMessage
            {
                SchemaVersion = SyncProtocol.Version + 1,
                RequestId = "bad-result",
                QuestLogId = "quest-1"
            });
        nonHostBus.Send(
            new ItemDeliveryResultMessage
            {
                RequestId = "rogue-result",
                QuestLogId = "quest-1"
            },
            SyncProtocol.TypeItemDeliveryResult,
            new[] { 2L });

        Assert.Equal(0, deliveryRequests);
        Assert.Equal(0, rewardClaims);
        Assert.Equal(0, deliveryResults);
    }

    [Fact]
    public void FailedReportsRetryInFifoOrder()
    {
        (_, MultiplayerSyncService host, MultiplayerSyncService farmhand, InMemoryModMessageBus farmhandBus) = CreateConnectedPair();
        var receivedPlayerLines = new List<string>();
        host.RemoteExchangeReceived = (_, message) => receivedPlayerLines.Add(message.PlayerText);
        farmhandBus.FailNextSendCount = 1;

        Assert.True(farmhand.TrySendExchangeReport("Emily", "first", "one", "{}"));
        Assert.True(farmhand.TrySendExchangeReport("Emily", "second", "two", "{}"));
        Assert.Equal(2, farmhand.PendingExchangeReportCount);
        Assert.Empty(receivedPlayerLines);

        for (int tick = 0; tick <= 60; tick++)
        {
            farmhand.OnUpdateTicked();
        }

        Assert.Equal(new[] { "first", "second" }, receivedPlayerLines);
        Assert.Equal(0, farmhand.PendingExchangeReportCount);
    }

    [Fact]
    public void SavingDiscardsFailedReportsWithTrace()
    {
        (_, _, MultiplayerSyncService farmhand, InMemoryModMessageBus farmhandBus, FakeMonitor monitor) = CreateConnectedPairWithMonitor();
        farmhandBus.FailNextSendCount = 1;
        Assert.True(farmhand.TrySendExchangeReport("Emily", "hello", "hi", "{}"));
        Assert.Equal(1, farmhand.PendingExchangeReportCount);

        farmhand.OnSaving();

        Assert.Equal(0, farmhand.PendingExchangeReportCount);
        Assert.Contains(
            monitor.Entries,
            entry => entry.Level == LogLevel.Trace
                && entry.Message.Contains("log.mp.exchangeRetriesDiscarded", StringComparison.Ordinal));
    }

    [Fact]
    public void FarmhandReportedAndHostLocalExchangeProduceEquivalentMindState()
    {
        (_, MultiplayerSyncService host, MultiplayerSyncService farmhand, _) = CreateConnectedPair();
        ExchangeReportMessage? report = null;
        host.RemoteExchangeReceived = (_, message) => report = message;
        const string analysisJson = """
            {
              "rapportDelta": 7,
              "memories": [
                { "kind": "fact", "summary": "the farmer keeps bees", "importance": 70, "playerPreference": false }
              ],
              "conflicts": [],
              "behaviorInfluences": []
            }
            """;
        Assert.True(farmhand.TrySendExchangeReport("Emily", "I keep bees.", "That sounds lovely.", analysisJson));
        Assert.NotNull(report);

        LivingNpcState localHostState = TestScenarios.TrustedState("Emily");
        LivingNpcState remoteHostState = localHostState.Clone();
        localHostState.LongTermMemories.Clear();
        remoteHostState.LongTermMemories.Clear();
        var localEntries = new List<BehaviorMemoryEntry>();
        var remoteEntries = new List<BehaviorMemoryEntry>();
        ExchangeApplicationService localApplication = CreateApplication(localEntries);
        ExchangeApplicationService remoteApplication = CreateApplication(remoteEntries);
        var npc = new NPC { Name = "Emily", displayName = "Emily" };
        ValleyTalkExchangeResult localResult = localApplication.Apply(
            npc,
            localHostState,
            report!.PlayerText,
            report.NpcResponse,
            report.AnalysisJson,
            maxEntriesPerNpc: 20,
            maxPendingHelpRequestsPerNpc: 1,
            helpRequestCooldownDays: 3,
            maxExtraFriendshipPerDay: 20,
            maxDialogueBehaviorInfluenceDays: 3);
        string remoteAnalysis = RemoteExchangePolicy.SanitizeAnalysisJson(report.AnalysisJson, out _);
        ValleyTalkExchangeResult remoteResult = remoteApplication.Apply(
            npc,
            remoteHostState,
            report.PlayerText,
            report.NpcResponse,
            remoteAnalysis,
            maxEntriesPerNpc: 20,
            maxPendingHelpRequestsPerNpc: 0,
            helpRequestCooldownDays: 3,
            maxExtraFriendshipPerDay: 20,
            maxDialogueBehaviorInfluenceDays: 3,
            allowHelpRequestProgress: false);

        Assert.Equal(localResult.LongTermMemoriesStored, remoteResult.LongTermMemoriesStored);
        Assert.Equal(localResult.AppliedFriendshipDelta, remoteResult.AppliedFriendshipDelta);
        Assert.Equal(
            JsonConvert.SerializeObject(localHostState),
            JsonConvert.SerializeObject(remoteHostState));
        Assert.Equal(
            JsonConvert.SerializeObject(localEntries),
            JsonConvert.SerializeObject(remoteEntries));
    }

    private static (InMemoryModMessageNetwork Network, MultiplayerSyncService Host, MultiplayerSyncService Farmhand, InMemoryModMessageBus FarmhandBus) CreateConnectedPair()
    {
        (InMemoryModMessageNetwork network, MultiplayerSyncService host, MultiplayerSyncService farmhand, InMemoryModMessageBus farmhandBus, _) = CreateConnectedPairWithMonitor();
        return (network, host, farmhand, farmhandBus);
    }

    private static (InMemoryModMessageNetwork Network, MultiplayerSyncService Host, MultiplayerSyncService Farmhand, InMemoryModMessageBus FarmhandBus, FakeMonitor FarmhandMonitor) CreateConnectedPairWithMonitor()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        MultiplayerSyncService host = CreateService(
            hostBus,
            new FakeMultiplayerRuntimeContext
            {
                IsMainPlayer = true,
                IsOnHostComputer = true,
                HostPlayerId = 1,
                LocalPlayerName = "Host"
            },
            out _);
        MultiplayerSyncService farmhand = CreateService(
            farmhandBus,
            new FakeMultiplayerRuntimeContext
            {
                IsMainPlayer = false,
                IsOnHostComputer = false,
                HostPlayerId = 1,
                LocalPlayerName = "Farmhand",
                TotalDays = 123,
                TimeOfDay = 1640
            },
            out FakeMonitor farmhandMonitor);
        host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        Assert.True(farmhand.UseHostAuthority);
        return (network, host, farmhand, farmhandBus, farmhandMonitor);
    }

    private static (InMemoryModMessageNetwork Network, MultiplayerSyncService Host, InMemoryModMessageBus FarmhandBus) CreateHostWithCompatibleRawFarmhand()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        MultiplayerSyncService host = CreateService(
            hostBus,
            new FakeMultiplayerRuntimeContext
            {
                IsMainPlayer = true,
                IsOnHostComputer = true,
                HostPlayerId = 1,
                LocalPlayerName = "Host"
            },
            out _);
        host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        farmhandBus.Send(
            new ProtocolHelloAckMessage { Compatible = true },
            SyncProtocol.TypeProtocolHelloAck,
            new[] { 1L });
        network.Sent.Clear();
        return (network, host, farmhandBus);
    }

    private static MultiplayerSyncService CreateService(
        IModMessageBus bus,
        IMultiplayerRuntimeContext runtime,
        out FakeMonitor monitor)
    {
        var config = new ModConfig
        {
            EnableMultiplayerSync = true,
            ShowHudMessages = false,
            Debug = false
        };
        monitor = new FakeMonitor();
        return new MultiplayerSyncService(
            bus,
            runtime,
            monitor,
            config,
            new NpcRelationshipViewStore(),
            new HelpRequestQuestProjectionStore(),
            new BehaviorFeedbackService(config, monitor),
            ModId);
    }

    private static ExchangeApplicationService CreateApplication(List<BehaviorMemoryEntry> entries)
    {
        BehaviorMemoryEntry CreateEntry(string npcName, string kind, string action, string reason)
        {
            return new BehaviorMemoryEntry
            {
                NpcName = npcName,
                Kind = kind,
                Action = action,
                Reason = reason,
                TotalDays = 123,
                TimeOfDay = 1640
            };
        }

        void AddEntry(BehaviorMemoryEntry entry, int _) => entries.Add(entry);

        var helpRequests = new HelpRequestMemoryService(
            (_, _, _) => { },
            (_, _) => { },
            (_, _, _, _) => { },
            CreateEntry,
            AddEntry,
            (_, fallback) => fallback);

        return new ExchangeApplicationService(
            helpRequests,
            (npc, kind, action, reason) => CreateEntry(npc.Name, kind, action, reason),
            AddEntry,
            (_, _) => false,
            (state, candidate) =>
            {
                state.LongTermMemories.Add(new LongTermMemoryFact
                {
                    Kind = candidate.Kind,
                    Subject = candidate.Subject,
                    Summary = candidate.Summary,
                    Tags = candidate.Tags.ToList(),
                    Importance = candidate.Importance,
                    CreatedTotalDays = 123,
                    CreatedTimeOfDay = 1640,
                    LastUpdatedTotalDays = 123,
                    LastUpdatedTimeOfDay = 1640
                });
                return true;
            },
            (_, _) => false,
            (_, _, _, _) => false,
            (_, _) => false,
            (_, _, _, _) => 0,
            (state, requested, maximum) =>
            {
                int applied = Math.Min(requested, maximum);
                state.AiFriendshipGainedToday += applied;
                state.LastAiFriendshipTotalDays = 123;
                return applied;
            },
            () => (123, 1640));
    }
}
