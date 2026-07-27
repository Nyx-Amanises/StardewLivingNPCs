using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>
/// 多人同步运行时（v1 主机权威，见 <see cref="SyncProtocol"/>）。主机侧：接收 farmhand 的
/// 交换上报（转交 <see cref="BehaviorEngine"/> 主线程队列入账）、回发入账确认、按 NPC 推送
/// 心智视图、应答全量快照请求；farmhand 侧：上报交换、应用确认（好感/随境跟话）、维护
/// 只读镜像、驱动记忆手册的"请求快照 → 异步开书 → 超时降级"流程。所有回调都由
/// BehaviorEngine 的 SafeRun 包裹调用，事件均在主线程触发。
/// 主机未装本 mod（或本开关关闭）时 farmhand 自动回退旧行为：本地临时入账、不上报。
/// </summary>
internal sealed class MultiplayerSyncService
{
    private const long BookSyncTimeoutMs = 5_000;
    private const long SnapshotSendThrottleMs = 1_000;
    private const int ExchangeRetryIntervalTicks = 60;

    private readonly IModMessageBus bus;
    private readonly IMultiplayerRuntimeContext runtime;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly string modUniqueId;
    private readonly Action<int, int> notifyProtocolMismatch;

    /// <summary>主机侧收到交换上报后的入账入口（engine 晚绑定：入队主线程统一消费）。</summary>
    public Action<long, ExchangeReportMessage>? RemoteExchangeReceived { get; set; }

    /// <summary>farmhand 快照到位后从镜像开书（engine 晚绑定）。</summary>
    public Action? OpenBookFromMirror { get; set; }

    private bool bookRequestPending;
    private long bookRequestDeadlineTicks;
    private bool bookSnapshotArrived;

    private readonly Dictionary<long, long> lastSnapshotSentTicksByPeer = new();
    private readonly HashSet<long> compatiblePeerIds = new();
    private readonly Queue<ExchangeReportMessage> pendingExchangeReports = new();
    private bool loggedIncompatibleVersion;
    private bool protocolMismatchNotified;
    private ProtocolHandshakeState handshakeState = ProtocolHandshakeState.AwaitingHost;
    private int exchangeRetryTicksRemaining;

    public MultiplayerSyncService(
        IModMessageBus bus,
        IMultiplayerRuntimeContext runtime,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        BehaviorFeedbackService feedback,
        string modUniqueId,
        Action<int, int>? notifyProtocolMismatch = null)
    {
        this.bus = bus;
        this.runtime = runtime;
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.feedback = feedback;
        this.modUniqueId = modUniqueId;
        this.notifyProtocolMismatch = notifyProtocolMismatch
            ?? ((hostVersion, localVersion) => this.feedback.Show(I18n.Get(
                "hud.mp.protocolMismatch",
                new { host = hostVersion, local = localVersion })));
        this.bus.MessageReceived += this.OnModMessageReceived;
    }

    private bool SyncEnabled => this.config.EnableMultiplayerSync;

    /// <summary>连接中的主机端是否装了本 mod（farmhand 视角；每次现查，连接列表极小）。</summary>
    public bool HostHasMod
    {
        get
        {
            try
            {
                ModMessagePeer? host = this.bus
                    .GetConnectedPeers()
                    .FirstOrDefault(peer => peer.IsHost);
                return host?.HasMod == true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// farmhand 是否处于主机权威模式：远程帮工 + 两端开关 + 主机装了本 mod。
    /// 为真时交换上报主机、镜像只读、机会注入抑制；为假时回退旧的本地临时行为。
    /// </summary>
    public bool UseHostAuthority => this.SyncEnabled
        && this.runtime.IsMultiplayer
        && MultiplayerRoles.Decide(this.runtime.IsMainPlayer, this.runtime.IsOnHostComputer) == MultiplayerRole.RemoteFarmhand
        && this.HostHasMod
        && this.handshakeState == ProtocolHandshakeState.Compatible;

    /// <summary>
    /// 行为上下文是否抑制机会注入（礼物/求助机会与出游可用性）：这些段落引导模型发起
    /// 世界动作，而世界动作在主机权威下只属于主机玩家自己的会话（决策 3 的推广）。
    /// </summary>
    public bool SuppressLocalOpportunities => this.UseHostAuthority;

    public int PendingExchangeReportCount => this.pendingExchangeReports.Count;

    // ---- SMAPI 事件（经 BehaviorEngine SafeRun 转发） ----

    private void OnModMessageReceived(ReceivedModMessage e)
    {
        if (e.FromModId != this.modUniqueId || !this.SyncEnabled || !this.runtime.IsMultiplayer)
        {
            return;
        }

        switch (e.Type)
        {
            case SyncProtocol.TypeProtocolHello:
            {
                if (this.runtime.IsMainPlayer || !this.IsFromHost(e.FromPlayerId))
                {
                    return;
                }

                if (this.TryRead(e, out ProtocolHelloMessage message))
                {
                    this.ApplyProtocolHello(message);
                }

                return;
            }

            case SyncProtocol.TypeProtocolHelloAck:
            {
                if (!this.runtime.IsMainPlayer || !this.TryRead(e, out ProtocolHelloAckMessage message))
                {
                    return;
                }

                this.ApplyProtocolHelloAck(e.FromPlayerId, message);
                return;
            }

            case SyncProtocol.TypeExchangeReport:
            {
                if (!this.runtime.IsMainPlayer || !this.compatiblePeerIds.Contains(e.FromPlayerId))
                {
                    return;
                }

                if (!this.TryRead(e, out ExchangeReportMessage message)
                    || !this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.RemoteExchangeReceived?.Invoke(e.FromPlayerId, message);
                return;
            }

            case SyncProtocol.TypeExchangeAck:
            {
                if (this.runtime.IsMainPlayer || !this.IsFromHost(e.FromPlayerId) || !this.UseHostAuthority)
                {
                    return;
                }

                if (!this.TryRead(e, out ExchangeAckMessage message)
                    || !this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.ApplyExchangeAck(message);
                return;
            }

            case SyncProtocol.TypeNpcView:
            {
                if (this.runtime.IsMainPlayer || !this.IsFromHost(e.FromPlayerId) || !this.UseHostAuthority)
                {
                    return;
                }

                if (!this.TryRead(e, out NpcMindViewMessage message)
                    || !this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.memory.ImportNpcView(message, this.config.MaxMemoryEntriesPerNpc);
                return;
            }

            case SyncProtocol.TypeSnapshotRequest:
            {
                if (!this.runtime.IsMainPlayer || !this.compatiblePeerIds.Contains(e.FromPlayerId))
                {
                    return;
                }

                if (!this.TryRead(e, out SnapshotRequestMessage message)
                    || !this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.SendFullSnapshotToPeer(e.FromPlayerId);
                return;
            }

            case SyncProtocol.TypeSnapshotComplete:
            {
                if (this.runtime.IsMainPlayer || !this.IsFromHost(e.FromPlayerId) || !this.UseHostAuthority)
                {
                    return;
                }

                if (!this.TryRead(e, out SnapshotCompleteMessage message)
                    || !this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                var keep = new HashSet<string>(
                    message.NpcNames ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
                this.memory.RetainOnlyNpcs(keep);
                this.bookSnapshotArrived = true;
                if (this.config.Debug)
                {
                    this.monitor.Log(
                        I18n.Get("log.mp.snapshotApplied", new { count = keep.Count }),
                        LogLevel.Debug);
                }

                return;
            }
        }
    }

    /// <summary>主机：farmhand 连入且装了本 mod 时先公布协议版本。</summary>
    public void OnPeerConnected(ModMessagePeer peer)
    {
        if (!this.runtime.IsMultiplayer
            || !this.runtime.IsMainPlayer
            || !this.SyncEnabled
            || peer.IsSplitScreen
            || !peer.HasMod)
        {
            return;
        }

        this.SendProtocolHello(peer.PlayerId);
    }

    public void OnPeerDisconnected(long playerId)
    {
        this.lastSnapshotSentTicksByPeer.Remove(playerId);
        this.compatiblePeerIds.Remove(playerId);
        if (!this.runtime.IsMainPlayer && this.runtime.HostPlayerId == playerId)
        {
            this.handshakeState = ProtocolHandshakeState.AwaitingHost;
        }
    }

    /// <summary>读档后：farmhand 主动请求一次全量快照（与主机 PeerConnected 推送互为兜底）。</summary>
    public void OnSaveLoaded()
    {
        this.ResetBookRequest();
        this.lastSnapshotSentTicksByPeer.Clear();
        if (this.UseHostAuthority)
        {
            this.SendToHost(new SnapshotRequestMessage { Reason = "resync" }, SyncProtocol.TypeSnapshotRequest);
        }
    }

    public void OnReturnedToTitle()
    {
        this.DiscardPendingExchangeReports("returned to title");
        this.ResetBookRequest();
        this.lastSnapshotSentTicksByPeer.Clear();
        this.compatiblePeerIds.Clear();
        this.handshakeState = ProtocolHandshakeState.AwaitingHost;
        this.protocolMismatchNotified = false;
        this.loggedIncompatibleVersion = false;
    }

    /// <summary>farmhand 开书流程的超时/到位巡查（BehaviorEngine 的 UpdateTicked 驱动）。</summary>
    public void OnUpdateTicked()
    {
        this.RetryPendingExchangeReports();
        if (!this.bookRequestPending)
        {
            return;
        }

        FarmhandBookAction action = RemoteExchangePolicy.PlanBookOpen(
            this.bookSnapshotArrived,
            Environment.TickCount64 >= this.bookRequestDeadlineTicks,
            this.memory.GetTrackedStates().Any());
        switch (action)
        {
            case FarmhandBookAction.Wait:
                return;

            case FarmhandBookAction.OpenFresh:
                this.ResetBookRequest();
                this.OpenBookFromMirror?.Invoke();
                return;

            case FarmhandBookAction.OpenStaleWithNotice:
                this.ResetBookRequest();
                this.feedback.Show(I18n.Get("book.hud.syncStale"));
                this.OpenBookFromMirror?.Invoke();
                return;

            default:
                this.ResetBookRequest();
                this.feedback.Show(I18n.Get("book.hud.syncTimeout"));
                return;
        }
    }

    // ---- farmhand 侧动作 ----

    /// <summary>farmhand 上报一次交换；发送成功返回 true（失败时调用方回退本地入账）。</summary>
    public bool TrySendExchangeReport(string npcName, string playerText, string npcResponse, string analysisJson)
    {
        if (!this.UseHostAuthority)
        {
            return false;
        }

        var message = new ExchangeReportMessage
        {
            NpcName = npcName,
            PlayerName = this.runtime.LocalPlayerName,
            PlayerText = playerText,
            NpcResponse = npcResponse,
            AnalysisJson = analysisJson,
            TotalDays = this.runtime.TotalDays,
            TimeOfDay = this.runtime.TimeOfDay
        };

        // 旧报告未发出时保持 FIFO，不让后来的交换越过它。
        if (this.pendingExchangeReports.Count > 0)
        {
            this.pendingExchangeReports.Enqueue(message);
            return true;
        }

        if (!this.TrySendExchangeReportCore(message, out Exception? error))
        {
            this.pendingExchangeReports.Enqueue(message);
            this.exchangeRetryTicksRemaining = ExchangeRetryIntervalTicks;
            this.monitor.Log(
                I18n.Get(
                    "log.mp.exchangeQueuedForRetry",
                    new { npc = npcName, error = error?.GetType().Name ?? "unknown" }),
                LogLevel.Trace);
        }

        return true;
    }

    /// <summary>存档边界不持久化补偿队列；v1 明确丢弃并留 Trace。</summary>
    public void OnSaving()
    {
        this.DiscardPendingExchangeReports("saving");
    }

    /// <summary>
    /// farmhand 记忆手册入口：主机权威下改为"请求快照 → 异步开书"。返回 true 表示已接管
    /// （由 OnUpdateTicked 在快照到位或超时后开书）；false 表示走本地直开。
    /// </summary>
    public bool TryBeginRemoteBookOpen()
    {
        if (!this.UseHostAuthority)
        {
            return false;
        }

        if (this.bookRequestPending)
        {
            return true;
        }

        this.bookRequestPending = true;
        this.bookSnapshotArrived = false;
        this.bookRequestDeadlineTicks = Environment.TickCount64 + BookSyncTimeoutMs;
        this.SendToHost(new SnapshotRequestMessage { Reason = "book" }, SyncProtocol.TypeSnapshotRequest);
        this.feedback.Show(I18n.Get("book.hud.syncing"));
        return true;
    }

    private void ApplyExchangeAck(ExchangeAckMessage message)
    {
        NPC? npc = Game1.getCharacterFromName(message.NpcName);
        if (npc == null)
        {
            return;
        }

        if (message.FriendshipDelta > 0 && Game1.player != null)
        {
            Game1.player.changeFriendship(message.FriendshipDelta, npc);
        }

        if (!string.IsNullOrWhiteSpace(message.AmbientText) && this.config.EnableDialogueFollowUps)
        {
            this.feedback.QueueAmbientRemark(npc, message.AmbientText, message.AmbientDelayMinutes);
        }

        if (this.config.Debug)
        {
            this.monitor.Log(
                I18n.Get("log.mp.ackApplied", new { npc = message.NpcName, friendship = message.FriendshipDelta }),
                LogLevel.Debug);
        }
    }

    // ---- 主机侧动作 ----

    /// <summary>主机把结果确认发回上报的 farmhand。</summary>
    public void SendExchangeAck(long toPlayerId, ExchangeAckMessage message)
    {
        if (!this.runtime.IsMultiplayer
            || !this.runtime.IsMainPlayer
            || !this.SyncEnabled
            || !this.compatiblePeerIds.Contains(toPlayerId))
        {
            return;
        }

        this.bus.Send(
            message,
            SyncProtocol.TypeExchangeAck,
            new[] { toPlayerId });
    }

    /// <summary>主机在任一交换入账后刷新该 NPC 的镜像（无远程对端时零开销）。</summary>
    public void BroadcastNpcView(string npcName)
    {
        if (!this.runtime.IsMultiplayer || !this.runtime.IsMainPlayer || !this.SyncEnabled)
        {
            return;
        }

        long[] targets = this.GetRemotePeerIdsWithMod();
        if (targets.Length == 0)
        {
            return;
        }

        NpcMindViewMessage? view = this.memory.ExportNpcView(npcName);
        if (view == null)
        {
            return;
        }

        this.bus.Send(view, SyncProtocol.TypeNpcView, targets);
    }

    /// <summary>主机全量广播（日开始账本结算后 / 手动清记忆后）。</summary>
    public void BroadcastFullSnapshot()
    {
        if (!this.runtime.IsMultiplayer || !this.runtime.IsMainPlayer || !this.SyncEnabled)
        {
            return;
        }

        long[] targets = this.GetRemotePeerIdsWithMod();
        if (targets.Length == 0)
        {
            return;
        }

        this.SendFullSnapshotCore(targets);
    }

    private void SendFullSnapshotToPeer(long playerId)
    {
        long now = Environment.TickCount64;
        if (this.lastSnapshotSentTicksByPeer.TryGetValue(playerId, out long last)
            && now - last < SnapshotSendThrottleMs)
        {
            return;
        }

        this.lastSnapshotSentTicksByPeer[playerId] = now;
        this.SendFullSnapshotCore(new[] { playerId });
    }

    private void SendFullSnapshotCore(long[] targets)
    {
        List<NpcMindViewMessage> views = this.memory.ExportAllNpcViews();
        foreach (NpcMindViewMessage view in views)
        {
            this.bus.Send(view, SyncProtocol.TypeNpcView, targets);
        }

        this.bus.Send(
            new SnapshotCompleteMessage { NpcNames = views.Select(view => view.NpcName).ToList() },
            SyncProtocol.TypeSnapshotComplete,
            targets);
        if (this.config.Debug)
        {
            this.monitor.Log(
                I18n.Get("log.mp.snapshotSent", new { count = views.Count, player = string.Join(",", targets) }),
                LogLevel.Debug);
        }
    }

    // ---- 杂项 ----

    private bool TrySendExchangeReportCore(ExchangeReportMessage message, out Exception? error)
    {
        long? hostId = this.TryGetHostPlayerId();
        if (!this.UseHostAuthority || hostId == null)
        {
            error = null;
            return false;
        }

        try
        {
            this.bus.Send(message, SyncProtocol.TypeExchangeReport, new[] { hostId.Value });
            error = null;
            if (this.config.Debug)
            {
                this.monitor.Log(I18n.Get("log.mp.exchangeForwarded", new { npc = message.NpcName }), LogLevel.Debug);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private void RetryPendingExchangeReports()
    {
        if (this.pendingExchangeReports.Count == 0 || !this.UseHostAuthority)
        {
            return;
        }

        if (this.exchangeRetryTicksRemaining > 0)
        {
            this.exchangeRetryTicksRemaining--;
            return;
        }

        int attemptsRemaining = this.pendingExchangeReports.Count;
        while (attemptsRemaining-- > 0 && this.pendingExchangeReports.TryPeek(out ExchangeReportMessage? message))
        {
            if (!this.TrySendExchangeReportCore(message, out _))
            {
                this.exchangeRetryTicksRemaining = ExchangeRetryIntervalTicks;
                return;
            }

            this.pendingExchangeReports.Dequeue();
        }
    }

    private void DiscardPendingExchangeReports(string reason)
    {
        int count = this.pendingExchangeReports.Count;
        if (count == 0)
        {
            return;
        }

        this.pendingExchangeReports.Clear();
        this.exchangeRetryTicksRemaining = 0;
        this.monitor.Log(
            I18n.Get("log.mp.exchangeRetriesDiscarded", new { count, reason }),
            LogLevel.Trace);
    }

    private void SendProtocolHello(long playerId)
    {
        if (!this.runtime.IsMultiplayer || !this.runtime.IsMainPlayer || !this.SyncEnabled)
        {
            return;
        }

        this.bus.Send(
            new ProtocolHelloMessage(),
            SyncProtocol.TypeProtocolHello,
            new[] { playerId });
    }

    private void ApplyProtocolHello(ProtocolHelloMessage message)
    {
        bool compatible = SyncProtocol.IsCompatible(message.SchemaVersion)
            && SyncProtocol.IsCompatible(message.ProtocolVersion);
        this.handshakeState = compatible
            ? ProtocolHandshakeState.Compatible
            : ProtocolHandshakeState.Incompatible;

        long? hostId = this.TryGetHostPlayerId();
        if (hostId != null)
        {
            this.bus.Send(
                new ProtocolHelloAckMessage { Compatible = compatible },
                SyncProtocol.TypeProtocolHelloAck,
                new[] { hostId.Value });
        }

        if (compatible || this.protocolMismatchNotified)
        {
            return;
        }

        this.protocolMismatchNotified = true;
        this.notifyProtocolMismatch(message.ProtocolVersion, SyncProtocol.Version);
        this.monitor.Log(
            I18n.Get(
                "log.mp.protocolMismatch",
                new { host = message.ProtocolVersion, local = SyncProtocol.Version }),
            LogLevel.Warn);
    }

    private void ApplyProtocolHelloAck(long playerId, ProtocolHelloAckMessage message)
    {
        bool compatible = message.Compatible
            && SyncProtocol.IsCompatible(message.SchemaVersion)
            && SyncProtocol.IsCompatible(message.ProtocolVersion);
        if (!compatible)
        {
            this.compatiblePeerIds.Remove(playerId);
            return;
        }

        this.compatiblePeerIds.Add(playerId);
        this.SendFullSnapshotToPeer(playerId);
    }

    private bool TryRead<TMessage>(ReceivedModMessage received, out TMessage message)
        where TMessage : class
    {
        try
        {
            message = received.ReadAs<TMessage>();
            return message != null;
        }
        catch (Exception ex)
        {
            message = null!;
            this.monitor.Log(
                I18n.Get(
                    "log.mp.messageRejected",
                    new { type = received.Type, reason = ex.GetType().Name }),
                LogLevel.Warn);
            return false;
        }
    }

    private void SendToHost(SnapshotRequestMessage message, string type)
    {
        long? hostId = this.TryGetHostPlayerId();
        if (!this.runtime.IsMultiplayer || hostId == null)
        {
            return;
        }

        this.bus.Send(message, type, new[] { hostId.Value });
    }

    private long? TryGetHostPlayerId()
    {
        return this.runtime.IsMultiplayer ? this.runtime.HostPlayerId : null;
    }

    private bool IsFromHost(long fromPlayerId)
    {
        return this.TryGetHostPlayerId() == fromPlayerId;
    }

    private long[] GetRemotePeerIdsWithMod()
    {
        try
        {
            return this.bus
                .GetConnectedPeers()
                .Where(peer => !peer.IsHost
                    && !peer.IsSplitScreen
                    && peer.HasMod
                    && this.compatiblePeerIds.Contains(peer.PlayerId))
                .Select(peer => peer.PlayerId)
                .ToArray();
        }
        catch
        {
            return Array.Empty<long>();
        }
    }

    private bool CheckVersion(int schemaVersion, string messageType)
    {
        if (SyncProtocol.IsCompatible(schemaVersion))
        {
            return true;
        }

        if (!this.loggedIncompatibleVersion)
        {
            this.loggedIncompatibleVersion = true;
            this.monitor.Log(
                I18n.Get(
                    "log.mp.messageRejected",
                    new { type = messageType, reason = $"schema version {schemaVersion} != {SyncProtocol.Version}" }),
                LogLevel.Warn);
        }

        return false;
    }

    private void ResetBookRequest()
    {
        this.bookRequestPending = false;
        this.bookSnapshotArrived = false;
        this.bookRequestDeadlineTicks = 0;
    }
}
