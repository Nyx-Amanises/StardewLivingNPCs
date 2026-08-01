using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Multiplayer;

public sealed class MultiplayerDebugStatusTests
{
    private const string ModId = "Yuki.LivingNPCs";

    [Fact]
    public void SinglePlayerReportsHostRoleWithoutHostAuthority()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus bus = network.AddEndpoint(1, isHost: true);
        MultiplayerSyncService service = CreateService(
            bus,
            isMainPlayer: true,
            hostPlayerId: 1,
            isMultiplayer: false);

        MultiplayerDebugStatus status = service.GetDebugStatus();

        Assert.False(status.IsMultiplayer);
        Assert.Equal(MultiplayerRole.HostPlayer, status.Role);
        Assert.Equal(SyncProtocol.Version, status.ProtocolVersion);
        Assert.Equal(ProtocolHandshakeState.AwaitingHost, status.HandshakeState);
        Assert.False(status.HostAuthorityActive);
        Assert.Equal(0, status.RelationshipViewCount);
        Assert.Equal(0, status.PendingExchangeReportCount);
        Assert.Equal(0, status.CompatiblePeerCount);
    }

    [Fact]
    public void MultiplayerHostReportsCompatiblePeerAfterHandshake()
    {
        ConnectedPair pair = CreatePair();

        MultiplayerDebugStatus before = pair.Host.GetDebugStatus();
        pair.Host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        MultiplayerDebugStatus after = pair.Host.GetDebugStatus();

        Assert.True(before.IsMultiplayer);
        Assert.Equal(MultiplayerRole.HostPlayer, before.Role);
        Assert.False(before.HostAuthorityActive);
        Assert.Equal(0, before.CompatiblePeerCount);
        Assert.Equal(1, after.CompatiblePeerCount);
        Assert.False(after.HostAuthorityActive);
    }

    [Fact]
    public void RemoteFarmhandTransitionsFromAwaitingHostToCompatibleAuthority()
    {
        ConnectedPair pair = CreatePair();

        MultiplayerDebugStatus before = pair.Farmhand.GetDebugStatus();
        pair.Host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        MultiplayerDebugStatus after = pair.Farmhand.GetDebugStatus();

        Assert.True(before.IsMultiplayer);
        Assert.Equal(MultiplayerRole.RemoteFarmhand, before.Role);
        Assert.Equal(ProtocolHandshakeState.AwaitingHost, before.HandshakeState);
        Assert.False(before.HostAuthorityActive);
        Assert.Equal(ProtocolHandshakeState.Compatible, after.HandshakeState);
        Assert.True(after.HostAuthorityActive);
        Assert.Equal(0, after.CompatiblePeerCount);
    }

    [Fact]
    public void SnapshotIncludesRelationshipViewsAndPendingExchangeQueue()
    {
        var relationshipViews = new NpcRelationshipViewStore();
        ConnectedPair pair = CreatePair(relationshipViews);
        pair.Host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        relationshipViews.Apply(new NpcRelationshipViewMessage
        {
            NpcName = "Emily",
            BehaviorContextSummary = "host view"
        });
        pair.FarmhandBus.FailNextSendCount = 1;

        Assert.True(pair.Farmhand.TrySendExchangeReport("Emily", "hello", "hi", "{}"));
        MultiplayerDebugStatus status = pair.Farmhand.GetDebugStatus();

        Assert.True(status.HostAuthorityActive);
        Assert.Equal(1, status.RelationshipViewCount);
        Assert.Equal(1, status.PendingExchangeReportCount);
    }

    [Fact]
    public void FormatterLabelsSinglePlayerWithoutAwaitingHost()
    {
        MultiplayerDebugStatus status = CreateStatus(
            isMultiplayer: false,
            role: MultiplayerRole.HostPlayer,
            handshakeState: ProtocolHandshakeState.AwaitingHost);

        string output = BehaviorDebugCommandHandler.FormatMultiplayerStatus(status, TranslateForTest);

        Assert.Contains("debug.multiplayer.role.singlePlayer", output, StringComparison.Ordinal);
        Assert.Contains("debug.multiplayer.sync.singlePlayer", output, StringComparison.Ordinal);
        Assert.DoesNotContain("debug.multiplayer.sync.awaitingHost", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatterReportsHostPeerCountInsteadOfHandshakeState()
    {
        MultiplayerDebugStatus status = CreateStatus(
            role: MultiplayerRole.HostPlayer,
            handshakeState: ProtocolHandshakeState.AwaitingHost,
            compatiblePeerCount: 3);

        string output = BehaviorDebugCommandHandler.FormatMultiplayerStatus(status, TranslateForTest);

        Assert.Contains("debug.multiplayer.sync.hosting[count=3]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("debug.multiplayer.sync.awaitingHost", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((int)ProtocolHandshakeState.AwaitingHost, false, "debug.multiplayer.sync.awaitingHost")]
    [InlineData((int)ProtocolHandshakeState.Compatible, true, "debug.multiplayer.sync.compatible")]
    [InlineData((int)ProtocolHandshakeState.Compatible, false, "debug.multiplayer.sync.compatibleInactive")]
    [InlineData((int)ProtocolHandshakeState.Incompatible, false, "debug.multiplayer.sync.incompatible")]
    public void FormatterReportsRemoteFarmhandHandshake(
        int handshakeStateValue,
        bool hostAuthorityActive,
        string expectedKey)
    {
        MultiplayerDebugStatus status = CreateStatus(
            role: MultiplayerRole.RemoteFarmhand,
            handshakeState: (ProtocolHandshakeState)handshakeStateValue,
            hostAuthorityActive: hostAuthorityActive);

        string output = BehaviorDebugCommandHandler.FormatMultiplayerStatus(status, TranslateForTest);

        Assert.Contains(expectedKey, output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatterMarksSplitScreenAiDialogueDisabled()
    {
        MultiplayerDebugStatus status = CreateStatus(
            role: MultiplayerRole.SplitScreenSecondary,
            handshakeState: ProtocolHandshakeState.AwaitingHost);

        string output = BehaviorDebugCommandHandler.FormatMultiplayerStatus(status, TranslateForTest);

        Assert.Contains("debug.multiplayer.role.splitScreenSecondary", output, StringComparison.Ordinal);
        Assert.Contains("debug.multiplayer.sync.splitScreenDisabled", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatterPinsProtocolAndQueueCountsInStableOrder()
    {
        MultiplayerDebugStatus status = CreateStatus(
            role: MultiplayerRole.RemoteFarmhand,
            handshakeState: ProtocolHandshakeState.Compatible,
            hostAuthorityActive: true,
            relationshipViewCount: 7,
            pendingExchangeReportCount: 2);

        string[] lines = BehaviorDebugCommandHandler
            .FormatMultiplayerStatus(status, TranslateForTest)
            .Split(Environment.NewLine, StringSplitOptions.None);

        Assert.Equal(new[]
        {
            "debug.multiplayer.heading",
            "debug.multiplayer.role[role=debug.multiplayer.role.remoteFarmhand]",
            $"debug.multiplayer.protocol[version={SyncProtocol.Version}]",
            "debug.multiplayer.syncStatus[status=debug.multiplayer.sync.compatible]",
            "debug.multiplayer.relationshipViews[count=7]",
            "debug.multiplayer.pendingExchanges[count=2]"
        }, lines);
    }

    [Fact]
    public void DebugCommandWritesMultiplayerSectionBeforeEvaluatingNpcSection()
    {
        MultiplayerDebugStatus status = CreateStatus();
        var sections = new List<string>();
        bool npcEvaluated = false;

        BehaviorDebugCommandHandler.WriteDebugCommandOutput(
            status,
            () =>
            {
                npcEvaluated = true;
                Assert.Single(sections);
                return "npc-section";
            },
            section => sections.Add(section),
            TranslateForTest);

        Assert.True(npcEvaluated);
        Assert.Equal(2, sections.Count);
        Assert.StartsWith("debug.multiplayer.heading", sections[0], StringComparison.Ordinal);
        Assert.Equal("npc-section", sections[1]);
    }

    private static ConnectedPair CreatePair(NpcRelationshipViewStore? farmhandViews = null)
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        MultiplayerSyncService host = CreateService(hostBus, isMainPlayer: true, hostPlayerId: 1);
        MultiplayerSyncService farmhand = CreateService(
            farmhandBus,
            isMainPlayer: false,
            hostPlayerId: 1,
            relationshipViews: farmhandViews);
        return new ConnectedPair(farmhandBus, host, farmhand);
    }

    private static MultiplayerSyncService CreateService(
        IModMessageBus bus,
        bool isMainPlayer,
        long hostPlayerId,
        bool isMultiplayer = true,
        NpcRelationshipViewStore? relationshipViews = null)
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
            relationshipViews ?? new NpcRelationshipViewStore(),
            new HelpRequestQuestProjectionStore(),
            new BehaviorFeedbackService(config, monitor),
            ModId);
    }

    private static MultiplayerDebugStatus CreateStatus(
        bool isMultiplayer = true,
        MultiplayerRole role = MultiplayerRole.HostPlayer,
        ProtocolHandshakeState handshakeState = ProtocolHandshakeState.AwaitingHost,
        bool hostAuthorityActive = false,
        int relationshipViewCount = 0,
        int pendingExchangeReportCount = 0,
        int compatiblePeerCount = 0)
    {
        return new MultiplayerDebugStatus(
            isMultiplayer,
            role,
            SyncProtocol.Version,
            handshakeState,
            hostAuthorityActive,
            relationshipViewCount,
            pendingExchangeReportCount,
            compatiblePeerCount);
    }

    private static string TranslateForTest(string key, object? tokens)
    {
        if (tokens == null)
        {
            return key;
        }

        string values = string.Join(",", tokens.GetType()
            .GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.GetValue(tokens)}"));
        return $"{key}[{values}]";
    }

    private sealed record ConnectedPair(
        InMemoryModMessageBus FarmhandBus,
        MultiplayerSyncService Host,
        MultiplayerSyncService Farmhand);
}
