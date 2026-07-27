using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>联机服务可见的对端信息；不把 SMAPI peer 对象放进协议或测试。</summary>
internal sealed record ModMessagePeer(
    long PlayerId,
    bool IsHost,
    bool IsSplitScreen,
    bool HasMod);

/// <summary>
/// 一条已接收的 ModMessage。生产端延迟调用 SMAPI 的 ReadAs，测试端直接携带经过
/// 序列化往返的纯数据负载；联机业务因此不依赖 SMAPI 事件参数。
/// </summary>
internal sealed class ReceivedModMessage
{
    private readonly ModMessageReceivedEventArgs? smapiEvent;
    private readonly object? payload;

    private ReceivedModMessage(
        long fromPlayerId,
        string fromModId,
        string type,
        ModMessageReceivedEventArgs? smapiEvent,
        object? payload)
    {
        this.FromPlayerId = fromPlayerId;
        this.FromModId = fromModId;
        this.Type = type;
        this.smapiEvent = smapiEvent;
        this.payload = payload;
    }

    public long FromPlayerId { get; }
    public string FromModId { get; }
    public string Type { get; }

    public T ReadAs<T>()
        where T : notnull
    {
        if (this.smapiEvent != null)
        {
            return this.smapiEvent.ReadAs<T>();
        }

        return this.payload is T value
            ? value
            : throw new InvalidOperationException($"Message payload is not {typeof(T).FullName}.");
    }

    public static ReceivedModMessage FromSmapi(ModMessageReceivedEventArgs e)
    {
        return new ReceivedModMessage(e.FromPlayerID, e.FromModID, e.Type, e, null);
    }

    public static ReceivedModMessage FromPayload(long fromPlayerId, string fromModId, string type, object payload)
    {
        return new ReceivedModMessage(fromPlayerId, fromModId, type, null, payload);
    }
}

/// <summary>
/// LivingNPCs 的 ModMessage 传输边界。生产实现只在这里接触 <see cref="IMultiplayerHelper"/>；
/// 联机业务与测试都只面向该抽象。
/// </summary>
internal interface IModMessageBus
{
    event Action<ReceivedModMessage>? MessageReceived;

    IReadOnlyList<ModMessagePeer> GetConnectedPeers();

    void Send<TMessage>(TMessage message, string messageType, IReadOnlyCollection<long>? playerIds = null);
}

/// <summary>SMAPI ModMessage 的生产适配器。</summary>
internal sealed class SmapiModMessageBus : IModMessageBus
{
    private readonly IMultiplayerHelper multiplayer;
    private readonly string modUniqueId;

    public SmapiModMessageBus(IMultiplayerHelper multiplayer, string modUniqueId)
    {
        this.multiplayer = multiplayer;
        this.modUniqueId = modUniqueId;
    }

    public event Action<ReceivedModMessage>? MessageReceived;

    public IReadOnlyList<ModMessagePeer> GetConnectedPeers()
    {
        return this.multiplayer
            .GetConnectedPlayers()
            .Select(this.DescribePeer)
            .ToList();
    }

    public ModMessagePeer DescribePeer(IMultiplayerPeer peer)
    {
        return new ModMessagePeer(
            peer.PlayerID,
            peer.IsHost,
            peer.IsSplitScreen,
            peer.GetMod(this.modUniqueId) != null);
    }

    public void Send<TMessage>(TMessage message, string messageType, IReadOnlyCollection<long>? playerIds = null)
    {
        this.multiplayer.SendMessage(
            message,
            messageType,
            new[] { this.modUniqueId },
            playerIds?.ToArray());
    }

    /// <summary>由 BehaviorEngine 的 SMAPI 主线程事件转入抽象总线。</summary>
    public void Receive(ModMessageReceivedEventArgs e)
    {
        this.MessageReceived?.Invoke(ReceivedModMessage.FromSmapi(e));
    }
}
