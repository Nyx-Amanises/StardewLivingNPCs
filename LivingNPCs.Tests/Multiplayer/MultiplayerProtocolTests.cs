using System.Reflection;
using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Multiplayer;

public sealed class MultiplayerProtocolTests
{
    private const string ModId = "Yuki.LivingNPCs";

    [Fact]
    public void CompatiblePeersCompleteHandshakeBeforeHostAuthorityStarts()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        MultiplayerSyncService host = CreateService(hostBus, isMainPlayer: true, hostPlayerId: 1);
        MultiplayerSyncService farmhand = CreateService(farmhandBus, isMainPlayer: false, hostPlayerId: 1);

        Assert.False(farmhand.UseHostAuthority);

        host.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));

        Assert.True(farmhand.UseHostAuthority);
        Assert.Equal(
            new[]
            {
                SyncProtocol.TypeProtocolHello,
                SyncProtocol.TypeProtocolHelloAck,
                SyncProtocol.TypeSnapshotComplete
            },
            network.Sent.Select(message => message.Type));
        Assert.All(
            network.Sent.Select(message => Assert.IsAssignableFrom<ISyncMessage>(message.Payload)),
            message => Assert.Equal(SyncProtocol.Version, message.SchemaVersion));
    }

    [Fact]
    public void IncompatibleHostFallsBackAndNotifiesOnlyOnce()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        int notices = 0;
        MultiplayerSyncService farmhand = CreateService(
            farmhandBus,
            isMainPlayer: false,
            hostPlayerId: 1,
            notifyProtocolMismatch: (_, _) => notices++);
        var incompatible = new ProtocolHelloMessage
        {
            SchemaVersion = SyncProtocol.Version + 1,
            ProtocolVersion = SyncProtocol.Version + 1
        };

        hostBus.Send(incompatible, SyncProtocol.TypeProtocolHello, new[] { 2L });
        hostBus.Send(incompatible, SyncProtocol.TypeProtocolHello, new[] { 2L });

        Assert.False(farmhand.UseHostAuthority);
        Assert.Equal(1, notices);
        Assert.Equal(2, network.Sent.Count(message => message.Type == SyncProtocol.TypeProtocolHelloAck));
        Assert.All(
            network.Sent
                .Where(message => message.Type == SyncProtocol.TypeProtocolHelloAck)
                .Select(message => Assert.IsType<ProtocolHelloAckMessage>(message.Payload)),
            acknowledgement => Assert.False(acknowledgement.Compatible));
    }

    [Fact]
    public void SinglePlayerNeverSendsModMessages()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus localBus = network.AddEndpoint(1, isHost: true);
        network.AddEndpoint(2, isHost: false);
        MultiplayerSyncService service = CreateService(
            localBus,
            isMainPlayer: true,
            hostPlayerId: 1,
            isMultiplayer: false);

        service.OnPeerConnected(new ModMessagePeer(2, IsHost: false, IsSplitScreen: false, HasMod: true));
        service.OnSaveLoaded();
        service.BroadcastFullSnapshot();
        bool exchangeSent = service.TrySendExchangeReport("Emily", "hello", "hi", "{}");

        Assert.False(exchangeSent);
        Assert.Empty(network.Sent);
    }

    [Fact]
    public void UnknownTypeAndVersionAreIgnoredWithoutMutatingMirror()
    {
        var network = new InMemoryModMessageNetwork();
        InMemoryModMessageBus hostBus = network.AddEndpoint(1, isHost: true);
        InMemoryModMessageBus farmhandBus = network.AddEndpoint(2, isHost: false);
        BehaviorMemory farmhandMemory = new();
        MultiplayerSyncService farmhand = CreateService(
            farmhandBus,
            isMainPlayer: false,
            hostPlayerId: 1,
            memory: farmhandMemory);
        hostBus.Send(new ProtocolHelloMessage(), SyncProtocol.TypeProtocolHello, new[] { 2L });
        Assert.True(farmhand.UseHostAuthority);

        hostBus.Send(new ProtocolHelloMessage(), "FutureMessageType", new[] { 2L });
        hostBus.Send(
            new NpcMindViewMessage
            {
                SchemaVersion = SyncProtocol.Version + 1,
                NpcName = "Emily",
                State = TestScenarios.TrustedState("Emily")
            },
            SyncProtocol.TypeNpcView,
            new[] { 2L });

        Assert.Empty(farmhandMemory.GetTrackedStates());
    }

    [Fact]
    public void EveryProtocolMessageIsVersionedPureData()
    {
        Type[] messageTypes = typeof(ISyncMessage).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ISyncMessage).IsAssignableFrom(type))
            .ToArray();

        Assert.NotEmpty(messageTypes);
        foreach (Type messageType in messageTypes)
        {
            var message = Assert.IsAssignableFrom<ISyncMessage>(Activator.CreateInstance(messageType));
            Assert.Equal(SyncProtocol.Version, message.SchemaVersion);
            AssertPureDataGraph(messageType, new HashSet<Type>());
        }
    }

    private static MultiplayerSyncService CreateService(
        IModMessageBus bus,
        bool isMainPlayer,
        long hostPlayerId,
        bool isMultiplayer = true,
        BehaviorMemory? memory = null,
        Action<int, int>? notifyProtocolMismatch = null)
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
            memory ?? new BehaviorMemory(),
            new BehaviorFeedbackService(config, monitor),
            ModId,
            notifyProtocolMismatch);
    }

    private static void AssertPureDataGraph(Type type, HashSet<Type> visited)
    {
        Type actualType = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(actualType)
            || actualType.IsPrimitive
            || actualType.IsEnum
            || actualType == typeof(string)
            || actualType == typeof(decimal)
            || actualType == typeof(DateTime))
        {
            return;
        }

        Assert.DoesNotContain("StardewValley", actualType.FullName ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Xna.Framework", actualType.FullName ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("StardewModdingAPI", actualType.FullName ?? string.Empty, StringComparison.Ordinal);

        if (actualType.IsArray)
        {
            AssertPureDataGraph(actualType.GetElementType()!, visited);
            return;
        }

        if (actualType.IsGenericType)
        {
            foreach (Type argument in actualType.GetGenericArguments())
            {
                AssertPureDataGraph(argument, visited);
            }

            if (actualType.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                return;
            }
        }

        foreach (PropertyInfo property in actualType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            AssertPureDataGraph(property.PropertyType, visited);
        }

        foreach (FieldInfo field in actualType.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            AssertPureDataGraph(field.FieldType, visited);
        }
    }
}
