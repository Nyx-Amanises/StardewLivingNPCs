using LivingNPCs.Behavior.Multiplayer;
using Newtonsoft.Json;

namespace LivingNPCs.Tests.Multiplayer;

internal sealed record SentModMessage(long FromPlayerId, long ToPlayerId, string Type, object Payload);

/// <summary>两个或多个测试端点共用的同步内存网络；每次投递都经过 JSON 往返。</summary>
internal sealed class InMemoryModMessageNetwork
{
    private readonly Dictionary<long, InMemoryModMessageBus> endpoints = new();

    public List<SentModMessage> Sent { get; } = new();

    public InMemoryModMessageBus AddEndpoint(long playerId, bool isHost, string modId = "Yuki.LivingNPCs")
    {
        var endpoint = new InMemoryModMessageBus(this, playerId, isHost, modId);
        this.endpoints.Add(playerId, endpoint);
        return endpoint;
    }

    public IReadOnlyList<ModMessagePeer> GetPeers(long localPlayerId)
    {
        return this.endpoints.Values
            .Where(endpoint => endpoint.PlayerId != localPlayerId)
            .Select(endpoint => new ModMessagePeer(
                endpoint.PlayerId,
                endpoint.IsHost,
                IsSplitScreen: false,
                HasMod: true))
            .ToList();
    }

    public void Route<TMessage>(InMemoryModMessageBus sender, TMessage message, string type, IReadOnlyCollection<long>? targets)
    {
        IEnumerable<InMemoryModMessageBus> recipients = targets == null
            ? this.endpoints.Values.Where(endpoint => endpoint.PlayerId != sender.PlayerId)
            : targets.Select(playerId => this.endpoints[playerId]);

        foreach (InMemoryModMessageBus recipient in recipients.ToList())
        {
            string json = JsonConvert.SerializeObject(message);
            object payload = JsonConvert.DeserializeObject(json, message!.GetType())
                ?? throw new InvalidOperationException("The in-memory bus could not deserialize its payload.");
            this.Sent.Add(new SentModMessage(sender.PlayerId, recipient.PlayerId, type, payload));
            recipient.Deliver(ReceivedModMessage.FromPayload(sender.PlayerId, sender.ModId, type, payload));
        }
    }
}

internal sealed class InMemoryModMessageBus : IModMessageBus
{
    private readonly InMemoryModMessageNetwork network;

    public InMemoryModMessageBus(InMemoryModMessageNetwork network, long playerId, bool isHost, string modId)
    {
        this.network = network;
        this.PlayerId = playerId;
        this.IsHost = isHost;
        this.ModId = modId;
    }

    public long PlayerId { get; }
    public bool IsHost { get; }
    public string ModId { get; }

    public event Action<ReceivedModMessage>? MessageReceived;

    public IReadOnlyList<ModMessagePeer> GetConnectedPeers()
    {
        return this.network.GetPeers(this.PlayerId);
    }

    public void Send<TMessage>(TMessage message, string messageType, IReadOnlyCollection<long>? playerIds = null)
    {
        this.network.Route(this, message, messageType, playerIds);
    }

    public void Deliver(ReceivedModMessage message)
    {
        this.MessageReceived?.Invoke(message);
    }
}

internal sealed class FakeMultiplayerRuntimeContext : IMultiplayerRuntimeContext
{
    public bool IsMultiplayer { get; set; } = true;
    public bool IsMainPlayer { get; set; }
    public bool IsOnHostComputer { get; set; }
    public long? HostPlayerId { get; set; }
    public string LocalPlayerName { get; set; } = "Farmer";
    public int TotalDays { get; set; } = 100;
    public int TimeOfDay { get; set; } = 1200;
}
