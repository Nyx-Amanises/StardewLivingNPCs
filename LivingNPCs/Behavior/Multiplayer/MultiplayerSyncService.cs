using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
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

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly string modUniqueId;

    /// <summary>主机侧收到交换上报后的入账入口（engine 晚绑定：入队主线程统一消费）。</summary>
    public Action<long, ExchangeReportMessage>? RemoteExchangeReceived { get; set; }

    /// <summary>farmhand 快照到位后从镜像开书（engine 晚绑定）。</summary>
    public Action? OpenBookFromMirror { get; set; }

    private bool bookRequestPending;
    private long bookRequestDeadlineTicks;
    private bool bookSnapshotArrived;

    private readonly Dictionary<long, long> lastSnapshotSentTicksByPeer = new();
    private bool loggedIncompatibleVersion;

    public MultiplayerSyncService(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        BehaviorFeedbackService feedback,
        string modUniqueId)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.feedback = feedback;
        this.modUniqueId = modUniqueId;
    }

    private bool SyncEnabled => this.config.EnableMultiplayerSync;

    /// <summary>连接中的主机端是否装了本 mod（farmhand 视角；每次现查，连接列表极小）。</summary>
    public bool HostHasMod
    {
        get
        {
            try
            {
                IMultiplayerPeer? host = this.helper.Multiplayer
                    .GetConnectedPlayers()
                    .FirstOrDefault(peer => peer.IsHost);
                return host?.GetMod(this.modUniqueId) != null;
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
        && MultiplayerRoles.Current == MultiplayerRole.RemoteFarmhand
        && this.HostHasMod;

    /// <summary>
    /// 行为上下文是否抑制机会注入（礼物/求助机会与出游可用性）：这些段落引导模型发起
    /// 世界动作，而世界动作在主机权威下只属于主机玩家自己的会话（决策 3 的推广）。
    /// </summary>
    public bool SuppressLocalOpportunities => this.UseHostAuthority;

    // ---- SMAPI 事件（经 BehaviorEngine SafeRun 转发） ----

    public void OnModMessageReceived(ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.modUniqueId || !this.SyncEnabled)
        {
            return;
        }

        switch (e.Type)
        {
            case SyncProtocol.TypeExchangeReport:
            {
                if (!Context.IsMainPlayer)
                {
                    return;
                }

                var message = e.ReadAs<ExchangeReportMessage>();
                if (!this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.RemoteExchangeReceived?.Invoke(e.FromPlayerID, message);
                return;
            }

            case SyncProtocol.TypeExchangeAck:
            {
                if (Context.IsMainPlayer || !this.IsFromHost(e.FromPlayerID))
                {
                    return;
                }

                var message = e.ReadAs<ExchangeAckMessage>();
                if (!this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.ApplyExchangeAck(message);
                return;
            }

            case SyncProtocol.TypeNpcView:
            {
                if (Context.IsMainPlayer || !this.IsFromHost(e.FromPlayerID))
                {
                    return;
                }

                var message = e.ReadAs<NpcMindViewMessage>();
                if (!this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.memory.ImportNpcView(message, this.config.MaxMemoryEntriesPerNpc);
                return;
            }

            case SyncProtocol.TypeSnapshotRequest:
            {
                if (!Context.IsMainPlayer)
                {
                    return;
                }

                var message = e.ReadAs<SnapshotRequestMessage>();
                if (!this.CheckVersion(message.SchemaVersion, e.Type))
                {
                    return;
                }

                this.SendFullSnapshotToPeer(e.FromPlayerID);
                return;
            }

            case SyncProtocol.TypeSnapshotComplete:
            {
                if (Context.IsMainPlayer || !this.IsFromHost(e.FromPlayerID))
                {
                    return;
                }

                var message = e.ReadAs<SnapshotCompleteMessage>();
                if (!this.CheckVersion(message.SchemaVersion, e.Type))
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

    /// <summary>主机：farmhand 连入且装了本 mod 时推送全量快照（分屏本地对端共享内存，跳过）。</summary>
    public void OnPeerConnected(PeerConnectedEventArgs e)
    {
        if (!Context.IsMainPlayer || !this.SyncEnabled || e.Peer.IsSplitScreen)
        {
            return;
        }

        if (e.Peer.GetMod(this.modUniqueId) == null)
        {
            return;
        }

        this.SendFullSnapshotToPeer(e.Peer.PlayerID);
    }

    public void OnPeerDisconnected(PeerDisconnectedEventArgs e)
    {
        this.lastSnapshotSentTicksByPeer.Remove(e.Peer.PlayerID);
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
        this.ResetBookRequest();
        this.lastSnapshotSentTicksByPeer.Clear();
    }

    /// <summary>farmhand 开书流程的超时/到位巡查（BehaviorEngine 的 UpdateTicked 驱动）。</summary>
    public void OnUpdateTicked()
    {
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
        long? hostId = TryGetHostPlayerId();
        if (hostId == null)
        {
            return false;
        }

        this.helper.Multiplayer.SendMessage(
            new ExchangeReportMessage
            {
                NpcName = npcName,
                PlayerText = playerText,
                NpcResponse = npcResponse,
                AnalysisJson = analysisJson
            },
            SyncProtocol.TypeExchangeReport,
            new[] { this.modUniqueId },
            new[] { hostId.Value });
        if (this.config.Debug)
        {
            this.monitor.Log(I18n.Get("log.mp.exchangeForwarded", new { npc = npcName }), LogLevel.Debug);
        }

        return true;
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
        if (!Context.IsMainPlayer || !this.SyncEnabled)
        {
            return;
        }

        this.helper.Multiplayer.SendMessage(
            message,
            SyncProtocol.TypeExchangeAck,
            new[] { this.modUniqueId },
            new[] { toPlayerId });
    }

    /// <summary>主机在任一交换入账后刷新该 NPC 的镜像（无远程对端时零开销）。</summary>
    public void BroadcastNpcView(string npcName)
    {
        if (!Context.IsMainPlayer || !this.SyncEnabled)
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

        this.helper.Multiplayer.SendMessage(view, SyncProtocol.TypeNpcView, new[] { this.modUniqueId }, targets);
    }

    /// <summary>主机全量广播（日开始账本结算后 / 手动清记忆后）。</summary>
    public void BroadcastFullSnapshot()
    {
        if (!Context.IsMainPlayer || !this.SyncEnabled)
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
            this.helper.Multiplayer.SendMessage(view, SyncProtocol.TypeNpcView, new[] { this.modUniqueId }, targets);
        }

        this.helper.Multiplayer.SendMessage(
            new SnapshotCompleteMessage { NpcNames = views.Select(view => view.NpcName).ToList() },
            SyncProtocol.TypeSnapshotComplete,
            new[] { this.modUniqueId },
            targets);
        if (this.config.Debug)
        {
            this.monitor.Log(
                I18n.Get("log.mp.snapshotSent", new { count = views.Count, player = string.Join(",", targets) }),
                LogLevel.Debug);
        }
    }

    // ---- 杂项 ----

    private void SendToHost(SnapshotRequestMessage message, string type)
    {
        long? hostId = TryGetHostPlayerId();
        if (hostId == null)
        {
            return;
        }

        this.helper.Multiplayer.SendMessage(message, type, new[] { this.modUniqueId }, new[] { hostId.Value });
    }

    private static long? TryGetHostPlayerId()
    {
        try
        {
            return Game1.MasterPlayer?.UniqueMultiplayerID;
        }
        catch
        {
            return null;
        }
    }

    private bool IsFromHost(long fromPlayerId)
    {
        return TryGetHostPlayerId() == fromPlayerId;
    }

    private long[] GetRemotePeerIdsWithMod()
    {
        try
        {
            return this.helper.Multiplayer
                .GetConnectedPlayers()
                .Where(peer => !peer.IsHost && !peer.IsSplitScreen && peer.GetMod(this.modUniqueId) != null)
                .Select(peer => peer.PlayerID)
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
