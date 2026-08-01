using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Multiplayer;

public sealed class BookSnapshotSyncTests
{
    private const string ModId = "Yuki.LivingNPCs";

    [Fact]
    public void CompatibleHandshakeEnablesBookAuthorityAndRoundTripsSnapshot()
    {
        var pair = CreatePair();
        BookSnapshotRequestMessage? providedRequest = null;
        BookSnapshotMessage? received = null;
        pair.Host.BookSnapshotProvider = request =>
        {
            providedRequest = request;
            return BuildSnapshot(request.RequestId, "Emily");
        };
        pair.Farmhand.BookSnapshotReceived = snapshot => received = snapshot;

        Assert.False(pair.Farmhand.UseHostAuthorityForBook);
        CompleteHandshake(pair);
        Assert.True(pair.Farmhand.UseHostAuthorityForBook);
        pair.Network.Sent.Clear();

        bool handled = pair.Farmhand.TryBeginRemoteBookOpen();

        Assert.True(handled);
        Assert.NotNull(providedRequest);
        Assert.False(string.IsNullOrWhiteSpace(providedRequest!.RequestId));
        Assert.NotNull(received);
        Assert.Equal(providedRequest.RequestId, received!.RequestId);
        Assert.Equal("Emily", Assert.Single(received.Npcs).NpcName);
        Assert.Equal(
            new[] { SyncProtocol.TypeBookSnapshotRequest, SyncProtocol.TypeBookSnapshot },
            pair.Network.Sent.Select(message => message.Type));
        Assert.All(
            pair.Network.Sent.Select(message => Assert.IsAssignableFrom<ISyncMessage>(message.Payload)),
            message => Assert.Equal(SyncProtocol.Version, message.SchemaVersion));
    }

    [Fact]
    public void ThreeEndpointResponseIsSentOnlyToRequester()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus firstBus = network.AddEndpoint(2, isHost: false);
        InMemoryModMessageBus secondBus = network.AddEndpoint(3, isHost: false);
        MultiplayerSyncService host = CreateService(hostBus, isMainPlayer: true, hostPlayerId: 1);
        MultiplayerSyncService first = CreateService(firstBus, isMainPlayer: false, hostPlayerId: 1);
        MultiplayerSyncService second = CreateService(secondBus, isMainPlayer: false, hostPlayerId: 1);
        int firstReceived = 0;
        int secondReceived = 0;
        host.BookSnapshotProvider = request => BuildSnapshot(request.RequestId, "Haley");
        first.BookSnapshotReceived = _ => firstReceived++;
        second.BookSnapshotReceived = _ => secondReceived++;
        host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        host.OnPeerConnected(new ModMessagePeer(3, IsHost: false, IsSplitScreen: false, HasMod: true));
        network.Sent.Clear();

        Assert.True(first.TryBeginRemoteBookOpen());

        Assert.Equal(1, firstReceived);
        Assert.Equal(0, secondReceived);
        SentModMessage response = Assert.Single(
            network.Sent,
            message => message.Type == SyncProtocol.TypeBookSnapshot);
        Assert.Equal(1, response.FromPlayerId);
        Assert.Equal(2, response.ToPlayerId);
    }

    [Fact]
    public void UnhandshakenAndWrongVersionRequestsAreIgnoredByHost()
    {
        var pair = CreatePair();
        int providerCalls = 0;
        pair.Host.BookSnapshotProvider = request =>
        {
            providerCalls++;
            return BuildSnapshot(request.RequestId, "Emily");
        };

        pair.FarmhandBus.Send(
            new BookSnapshotRequestMessage { RequestId = "before-handshake" },
            SyncProtocol.TypeBookSnapshotRequest,
            new[] { 1L });
        Assert.Equal(0, providerCalls);

        CompleteHandshake(pair);
        pair.Network.Sent.Clear();
        pair.FarmhandBus.Send(
            new BookSnapshotRequestMessage
            {
                SchemaVersion = SyncProtocol.Version + 1,
                RequestId = "future-schema"
            },
            SyncProtocol.TypeBookSnapshotRequest,
            new[] { 1L });

        Assert.Equal(0, providerCalls);
        Assert.DoesNotContain(pair.Network.Sent, message => message.Type == SyncProtocol.TypeBookSnapshot);
    }

    [Fact]
    public void WrongVersionAndWrongSenderResponsesAreIgnoredUntilHostSendsMatchingResponse()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus requesterBus = network.AddEndpoint(2, isHost: false);
        InMemoryModMessageBus otherFarmhandBus = network.AddEndpoint(3, isHost: false);
        MultiplayerSyncService host = CreateService(hostBus, isMainPlayer: true, hostPlayerId: 1);
        MultiplayerSyncService requester = CreateService(requesterBus, isMainPlayer: false, hostPlayerId: 1);
        CreateService(otherFarmhandBus, isMainPlayer: false, hostPlayerId: 1);
        host.BookSnapshotProvider = _ => null;
        int received = 0;
        requester.BookSnapshotReceived = _ => received++;
        host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        host.OnPeerConnected(new ModMessagePeer(3, IsHost: false, IsSplitScreen: false, HasMod: true));
        network.Sent.Clear();
        Assert.True(requester.TryBeginRemoteBookOpen());
        string requestId = GetOnlyRequest(network).RequestId;

        hostBus.Send(
            new BookSnapshotMessage
            {
                SchemaVersion = SyncProtocol.Version + 1,
                RequestId = requestId
            },
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });
        otherFarmhandBus.Send(
            BuildSnapshot(requestId, "Wrong sender"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });

        Assert.Equal(0, received);

        hostBus.Send(
            BuildSnapshot(requestId, "Emily"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });

        Assert.Equal(1, received);
    }

    [Fact]
    public void SinglePlayerAndHostNeverSendBookSnapshotRequests()
    {
        var singleNetwork = new InMemoryModMessageNetwork();
        InMemoryModMessageBus singleBus = singleNetwork.AddEndpoint(1, isHost: true);
        singleNetwork.AddEndpoint(2, isHost: false);
        MultiplayerSyncService singlePlayer = CreateService(
            singleBus,
            isMainPlayer: true,
            hostPlayerId: 1,
            isMultiplayer: false);

        var hostNetwork = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = hostNetwork.AddEndpoint(1, isHost: true);
        hostNetwork.AddEndpoint(2, isHost: false);
        MultiplayerSyncService host = CreateService(hostBus, isMainPlayer: true, hostPlayerId: 1);

        Assert.False(singlePlayer.TryBeginRemoteBookOpen());
        Assert.False(host.TryBeginRemoteBookOpen());
        Assert.Empty(singleNetwork.Sent);
        Assert.Empty(hostNetwork.Sent);
    }

    [Fact]
    public void ReenteringWhileRequestIsPendingDoesNotSendDuplicate()
    {
        var pair = CreatePair();
        pair.Host.BookSnapshotProvider = _ => null;
        CompleteHandshake(pair);
        pair.Network.Sent.Clear();

        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());
        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());

        Assert.Single(
            pair.Network.Sent,
            message => message.Type == SyncProtocol.TypeBookSnapshotRequest);
    }

    [Fact]
    public void SuccessfulResponsePreventsTimeout()
    {
        var clock = new TestClock();
        var pair = CreatePair(clock);
        int received = 0;
        int timedOut = 0;
        pair.Host.BookSnapshotProvider = request => BuildSnapshot(request.RequestId, "Emily");
        pair.Farmhand.BookSnapshotReceived = _ => received++;
        pair.Farmhand.BookSnapshotTimedOut = () => timedOut++;
        CompleteHandshake(pair);
        pair.Network.Sent.Clear();

        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());
        clock.Advance(10_000);
        pair.Farmhand.OnUpdateTicked();

        Assert.Equal(1, received);
        Assert.Equal(0, timedOut);
    }

    [Fact]
    public void PendingRequestTimesOutExactlyOnceAfterFiveSeconds()
    {
        var clock = new TestClock();
        var pair = CreatePair(clock);
        int timedOut = 0;
        pair.Host.BookSnapshotProvider = _ => null;
        pair.Farmhand.BookSnapshotTimedOut = () => timedOut++;
        CompleteHandshake(pair);
        pair.Network.Sent.Clear();

        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());
        clock.Advance(4_999);
        pair.Farmhand.OnUpdateTicked();
        Assert.Equal(0, timedOut);

        clock.Advance(1);
        pair.Farmhand.OnUpdateTicked();
        pair.Farmhand.OnUpdateTicked();
        clock.Advance(10_000);
        pair.Farmhand.OnUpdateTicked();

        Assert.Equal(1, timedOut);
    }

    [Fact]
    public void WrongIdDuplicateAndLateResponsesNeverApply()
    {
        var clock = new TestClock();
        var pair = CreatePair(clock);
        int received = 0;
        int timedOut = 0;
        pair.Host.BookSnapshotProvider = _ => null;
        pair.Farmhand.BookSnapshotReceived = _ => received++;
        pair.Farmhand.BookSnapshotTimedOut = () => timedOut++;
        CompleteHandshake(pair);
        pair.Network.Sent.Clear();
        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());
        string firstRequestId = GetOnlyRequest(pair.Network).RequestId;

        pair.HostBus.Send(
            BuildSnapshot("wrong-id", "Ignored"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });
        Assert.Equal(0, received);

        pair.HostBus.Send(
            BuildSnapshot(firstRequestId, "Emily"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });
        pair.HostBus.Send(
            BuildSnapshot(firstRequestId, "Duplicate"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });
        Assert.Equal(1, received);

        pair.Network.Sent.Clear();
        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());
        string secondRequestId = GetOnlyRequest(pair.Network).RequestId;
        clock.Advance(5_000);
        pair.Farmhand.OnUpdateTicked();
        Assert.Equal(1, timedOut);

        pair.HostBus.Send(
            BuildSnapshot(secondRequestId, "Late"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });

        Assert.Equal(1, received);
        Assert.Equal(1, timedOut);
    }

    [Theory]
    [InlineData(PendingClearAction.ReturnedToTitle)]
    [InlineData(PendingClearAction.HostDisconnected)]
    [InlineData(PendingClearAction.ExplicitCancel)]
    public void LifecycleAndExplicitCancellationClearPendingRequest(PendingClearAction action)
    {
        var clock = new TestClock();
        var pair = CreatePair(clock);
        int received = 0;
        int timedOut = 0;
        pair.Host.BookSnapshotProvider = _ => null;
        pair.Farmhand.BookSnapshotReceived = _ => received++;
        pair.Farmhand.BookSnapshotTimedOut = () => timedOut++;
        CompleteHandshake(pair);
        pair.Network.Sent.Clear();
        Assert.True(pair.Farmhand.TryBeginRemoteBookOpen());
        string requestId = GetOnlyRequest(pair.Network).RequestId;

        switch (action)
        {
            case PendingClearAction.ReturnedToTitle:
                pair.Farmhand.OnReturnedToTitle();
                break;
            case PendingClearAction.HostDisconnected:
                pair.Farmhand.OnPeerDisconnected(1);
                break;
            case PendingClearAction.ExplicitCancel:
                pair.Farmhand.CancelPendingBookSnapshot();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }

        int timeoutCallbacksAfterClear = timedOut;

        pair.HostBus.Send(
            BuildSnapshot(requestId, "Late"),
            SyncProtocol.TypeBookSnapshot,
            new[] { 2L });
        clock.Advance(10_000);
        pair.Farmhand.OnUpdateTicked();

        Assert.Equal(0, received);
        Assert.Equal(timeoutCallbacksAfterClear, timedOut);
    }

    [Fact]
    public void SendFailureDoesNotEscapeAndStillEndsInTimeout()
    {
        var clock = new TestClock();
        var pair = CreatePair(clock);
        int timedOut = 0;
        pair.Farmhand.BookSnapshotTimedOut = () => timedOut++;
        CompleteHandshake(pair);
        pair.Network.Sent.Clear();
        pair.FarmhandBus.FailNextSendCount = 1;

        bool handled = pair.Farmhand.TryBeginRemoteBookOpen();
        clock.Advance(5_000);
        pair.Farmhand.OnUpdateTicked();
        pair.Farmhand.OnUpdateTicked();

        Assert.True(handled);
        Assert.Equal(1, timedOut);
        Assert.DoesNotContain(pair.Network.Sent, message => message.Type == SyncProtocol.TypeBookSnapshotRequest);
    }

    private static ConnectedPair CreatePair(TestClock? clock = null)
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        MultiplayerSyncService host = CreateService(
            hostBus,
            isMainPlayer: true,
            hostPlayerId: 1,
            nowMilliseconds: clock == null ? null : clock.Read);
        MultiplayerSyncService farmhand = CreateService(
            farmhandBus,
            isMainPlayer: false,
            hostPlayerId: 1,
            nowMilliseconds: clock == null ? null : clock.Read);
        return new ConnectedPair(network, hostBus, farmhandBus, host, farmhand);
    }

    private static void CompleteHandshake(ConnectedPair pair)
    {
        pair.Host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
    }

    private static MultiplayerSyncService CreateService(
        IModMessageBus bus,
        bool isMainPlayer,
        long hostPlayerId,
        bool isMultiplayer = true,
        Func<long>? nowMilliseconds = null)
    {
        var config = new ModConfig { EnableMultiplayerSync = true, ShowHudMessages = false };
        var monitor = new FakeMonitor();
        return new MultiplayerSyncService(
            bus,
            new FakeMultiplayerRuntimeContext
            {
                IsMultiplayer = isMultiplayer,
                IsMainPlayer = isMainPlayer,
                IsOnHostComputer = isMainPlayer,
                HostPlayerId = hostPlayerId
            },
            monitor,
            config,
            new NpcRelationshipViewStore(),
            new HelpRequestQuestProjectionStore(),
            new BehaviorFeedbackService(config, monitor),
            ModId,
            nowMilliseconds: nowMilliseconds);
    }

    private static BookSnapshotRequestMessage GetOnlyRequest(InMemoryModMessageNetwork network)
    {
        SentModMessage request = Assert.Single(
            network.Sent,
            message => message.Type == SyncProtocol.TypeBookSnapshotRequest);
        return Assert.IsType<BookSnapshotRequestMessage>(request.Payload);
    }

    private static BookSnapshotMessage BuildSnapshot(string requestId, string npcName)
    {
        return new BookSnapshotMessage
        {
            RequestId = requestId,
            CapturedTotalDays = 100,
            Npcs =
            [
                new BookNpcSnapshot
                {
                    NpcName = npcName,
                    CurrentEmotion = "Happy",
                    RelationshipTrust = 55
                }
            ]
        };
    }

    public enum PendingClearAction
    {
        ReturnedToTitle,
        HostDisconnected,
        ExplicitCancel
    }

    private sealed record ConnectedPair(
        InMemoryModMessageNetwork Network,
        InMemoryModMessageBus HostBus,
        InMemoryModMessageBus FarmhandBus,
        MultiplayerSyncService Host,
        MultiplayerSyncService Farmhand);

    private sealed class TestClock
    {
        private long milliseconds = 10_000;

        public long Read()
        {
            return this.milliseconds;
        }

        public void Advance(long delta)
        {
            this.milliseconds += delta;
        }
    }
}
