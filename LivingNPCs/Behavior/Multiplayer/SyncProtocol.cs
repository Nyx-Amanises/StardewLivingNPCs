using System.Collections.Generic;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>
/// 多人同步协议（v1，主机权威）：farmhand 用自己的 API Key 本地生成对话，完成的交换经
/// ModMessage 上报主机，由主机走现有 RecordExchange 管道统一入账（NPC 心智唯一写者）；
/// 主机把每 NPC 的关系视图（预组装行为摘要 + 手册只读状态）回发给 farmhand，
/// 供 farmhand 本地组装行为上下文。所有消息限定本 mod 的 UniqueID。
/// </summary>
internal static class SyncProtocol
{
    /// <summary>消息负载布局版本；不兼容时接收方忽略并告警一次（正常捆绑发布两端同版）。</summary>
    public const int Version = 1;

    public const string TypeProtocolHello = "ProtocolHello";
    public const string TypeProtocolHelloAck = "ProtocolHelloAck";
    public const string TypeExchangeReport = "ExchangeReport";
    public const string TypeExchangeAck = "ExchangeAck";
    public const string TypeNpcRelationshipView = "NpcRelationshipView";
    public const string TypeRelationshipViewSetComplete = "RelationshipViewSetComplete";
    public const string TypeRelationshipViewsCleared = "RelationshipViewsCleared";
    public const string TypeQuestProjection = "QuestProjection";
    public const string TypeItemDeliveryRequest = "ItemDeliveryRequest";
    public const string TypeItemDeliveryResult = "ItemDeliveryResult";
    public const string TypeQuestRewardClaimed = "QuestRewardClaimed";
    public const string TypeBookSnapshotRequest = "BookSnapshotRequest";
    public const string TypeBookSnapshot = "BookSnapshot";

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
    public string ReportId { get; set; } = string.Empty;
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
    public string ReportId { get; set; } = string.Empty;
    public string NpcName { get; set; } = string.Empty;
    public int FriendshipDelta { get; set; }
    public int MoneyGrant { get; set; }
    public List<ItemGrant> ItemGrants { get; set; } = new();
    public string AmbientText { get; set; } = string.Empty;
    public int AmbientDelayMinutes { get; set; }
    public List<string> FeedbackMessages { get; set; } = new();
}

/// <summary>主机裁决、farmhand 本地兑现的纯数据物品授予。</summary>
internal sealed class ItemGrant
{
    public string ItemId { get; set; } = string.Empty;
    public int Stack { get; set; } = 1;
    public int Quality { get; set; }
    public string HudMessage { get; set; } = string.Empty;
}

/// <summary>
/// 主机 → farmhand：单 NPC 关系视图。行为摘要可直接注入提示词；BookState 是纯数据的
/// 手册只读源，不在 farmhand 上进入 BehaviorMemory 或参与任何账本写入。
/// </summary>
internal sealed class NpcRelationshipViewMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string NpcName { get; set; } = string.Empty;
    public string BehaviorContextSummary { get; set; } = string.Empty;
    public LivingNpcState? BookState { get; set; }
    public int UpdatedTotalDays { get; set; } = -1;
    public int UpdatedTimeOfDay { get; set; }
}

/// <summary>主机 → 新加入 farmhand：初始关系视图集合结束，并给出权威 NPC 名单。</summary>
internal sealed class RelationshipViewSetCompleteMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public List<string> NpcNames { get; set; } = new();
}

/// <summary>主机手动清空心智账本时清除 farmhand 的全部只读关系视图。</summary>
internal sealed class RelationshipViewsClearedMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
}

/// <summary>主机 → 单个 farmhand：该玩家当前应显示的 LivingNPCs 求助任务完整投影。</summary>
internal sealed class QuestProjectionMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public List<HelpRequestQuestProjection> Quests { get; set; } = new();
}

/// <summary>求助任务栏使用的纯数据投影；不携带 Quest、Farmer 或其他游戏对象。</summary>
internal sealed class HelpRequestQuestProjection
{
    public string NpcName { get; set; } = string.Empty;
    public string NpcDisplayName { get; set; } = string.Empty;
    public string QuestLogId { get; set; } = string.Empty;
    public string Type { get; set; } = "item_request";
    public string Summary { get; set; } = string.Empty;
    public List<HelpRequestQuestStepProjection> Steps { get; set; } = new();
    public int CurrentStepIndex { get; set; }
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public int DueTotalDays { get; set; } = -1;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int RewardMoney { get; set; }
    public bool MoneyRewardClaimable { get; set; }
}

internal sealed class HelpRequestQuestStepProjection
{
    public string Type { get; set; } = "item_request";
    public string Summary { get; set; } = string.Empty;
    public string RequestedItemId { get; set; } = string.Empty;
    public string RequestedItemLabel { get; set; } = string.Empty;
    public string QuestionTopic { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
}

/// <summary>farmhand → 主机：玩家尝试把当前手持物交给指定 NPC 的求助任务。</summary>
internal sealed class ItemDeliveryRequestMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public string QuestLogId { get; set; } = string.Empty;
    public string NpcName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public int ItemQuality { get; set; }
}

/// <summary>主机 → farmhand：物品交付的权威判定及本地生成答复所需的纯数据。</summary>
internal sealed class ItemDeliveryResultMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public string QuestLogId { get; set; } = string.Empty;
    public string NpcName { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public int ItemQuality { get; set; }
    public int TasteScore { get; set; }
    public bool Accepted { get; set; }
    public bool RequestFulfilled { get; set; }
    public int FriendshipDelta { get; set; }
    public List<ItemGrant> ItemGrants { get; set; } = new();
    public string PromptContext { get; set; } = string.Empty;
    public string HudMessage { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
}

/// <summary>farmhand → 主机：玩家已从本地代理任务领取主机授权的金钱奖励。</summary>
internal sealed class QuestRewardClaimedMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string QuestLogId { get; set; } = string.Empty;
}

/// <summary>本进程玩家在多人拓扑里的角色。</summary>
internal enum MultiplayerRole
{
    /// <summary>主机玩家（含单机）：心智账本的唯一写者。</summary>
    HostPlayer,

    /// <summary>分屏副屏玩家：与主机同进程共享静态状态；v1 禁用其 AI 对话入口。</summary>
    SplitScreenSecondary,

    /// <summary>远程 farmhand：本地生成、上报主机入账、持有只读关系视图。</summary>
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
