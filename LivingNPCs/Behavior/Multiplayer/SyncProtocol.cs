using System.Collections.Generic;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>
/// 多人同步协议（v1，主机权威）：farmhand 用自己的 API Key 本地生成对话，完成的交换经
/// ModMessage 上报主机，由主机走现有 RecordExchange 管道统一入账（NPC 心智唯一写者）；
/// 主机把每 NPC 的"心智视图"（状态 + 近期行为记忆）回发给 farmhand 作只读镜像，
/// 供 farmhand 本地组装行为上下文与记忆手册。所有消息限定本 mod 的 UniqueID。
/// </summary>
internal static class SyncProtocol
{
    /// <summary>消息负载布局版本；不兼容时接收方忽略并告警一次（正常捆绑发布两端同版）。</summary>
    public const int Version = 1;

    public const string TypeProtocolHello = "ProtocolHello";
    public const string TypeProtocolHelloAck = "ProtocolHelloAck";
    public const string TypeExchangeReport = "ExchangeReport";
    public const string TypeExchangeAck = "ExchangeAck";
    public const string TypeNpcView = "NpcView";
    public const string TypeSnapshotRequest = "SnapshotRequest";
    public const string TypeSnapshotComplete = "SnapshotComplete";

    public static bool IsCompatible(int schemaVersion)
    {
        return schemaVersion == Version;
    }
}

/// <summary>所有协议 DTO 的共同版本字段；消息负载只允许纯数据成员。</summary>
internal interface ISyncMessage
{
    int SchemaVersion { get; set; }
}

/// <summary>主机 → farmhand：加入时公布主机协议版本。</summary>
internal sealed class ProtocolHelloMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public int ProtocolVersion { get; set; } = SyncProtocol.Version;
}

/// <summary>farmhand → 主机：确认是否能启用主机权威同步。</summary>
internal sealed class ProtocolHelloAckMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public int ProtocolVersion { get; set; } = SyncProtocol.Version;
    public bool Compatible { get; set; }
}

/// <summary>farmhand → 主机：一次完成的 AI 交换（玩家台词 / NPC 回复 / 元数据分析 JSON）。</summary>
internal sealed class ExchangeReportMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string NpcName { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string PlayerText { get; set; } = string.Empty;
    public string NpcResponse { get; set; } = string.Empty;
    public string AnalysisJson { get; set; } = string.Empty;
    public int TotalDays { get; set; } = -1;
    public int TimeOfDay { get; set; }
}

/// <summary>
/// 主机 → 上报的 farmhand：入账结果里属于"上报玩家自己"的部分。好感增量由 farmhand
/// 对自己的 Farmer 应用（好感是每玩家数据，主机代应用会记到主机头上）；随境跟话在
/// farmhand 屏幕上显示。
/// </summary>
internal sealed class ExchangeAckMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string NpcName { get; set; } = string.Empty;
    public int FriendshipDelta { get; set; }
    public string AmbientText { get; set; } = string.Empty;
    public int AmbientDelayMinutes { get; set; }
}

/// <summary>主机 → farmhand：单个 NPC 的心智视图（状态克隆 + 近期行为记忆），整体替换镜像。</summary>
internal sealed class NpcMindViewMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string NpcName { get; set; } = string.Empty;
    public LivingNpcState? State { get; set; }
    public List<BehaviorMemoryEntry> Entries { get; set; } = new();
}

/// <summary>farmhand → 主机：请求全量心智快照（读档重同步 / 打开记忆手册）。</summary>
internal sealed class SnapshotRequestMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>主机 → farmhand：全量快照发送完毕；NpcNames 是完整名单，镜像据此修剪陈旧条目。</summary>
internal sealed class SnapshotCompleteMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public List<string> NpcNames { get; set; } = new();
}

/// <summary>本进程玩家在多人拓扑里的角色。</summary>
internal enum MultiplayerRole
{
    /// <summary>主机玩家（含单机）：心智账本的唯一写者。</summary>
    HostPlayer,

    /// <summary>分屏副屏玩家：与主机同进程共享静态状态；v1 禁用其 AI 对话入口。</summary>
    SplitScreenSecondary,

    /// <summary>远程 farmhand：本地生成、上报主机入账、持有只读镜像。</summary>
    RemoteFarmhand
}

internal static class MultiplayerRoles
{
    /// <summary>纯判定（单测覆盖）：主玩家 → 主机；非主玩家在主机电脑上 → 分屏副屏；否则远程帮工。</summary>
    public static MultiplayerRole Decide(bool isMainPlayer, bool isOnHostComputer)
    {
        if (isMainPlayer)
        {
            return MultiplayerRole.HostPlayer;
        }

        return isOnHostComputer ? MultiplayerRole.SplitScreenSecondary : MultiplayerRole.RemoteFarmhand;
    }
}

/// <summary>farmhand 对主机协议的握手状态；只有 Compatible 会启用同步路径。</summary>
internal enum ProtocolHandshakeState
{
    AwaitingHost,
    Compatible,
    Incompatible
}
