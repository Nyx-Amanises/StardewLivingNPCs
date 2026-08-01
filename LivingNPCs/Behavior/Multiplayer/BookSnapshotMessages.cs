using System.Collections.Generic;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>farmhand → 主机：请求一次只用于记忆手册的只读快照。</summary>
internal sealed class BookSnapshotRequestMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
}

/// <summary>
/// 主机 → farmhand：一次记忆手册只读快照。负载只含手册名册、关系、记忆与经历页所需的
/// 纯数据；对话历史明确不随消息发送，farmhand 始终读取自己的本地历史。
/// </summary>
internal sealed class BookSnapshotMessage : ISyncMessage
{
    public int SchemaVersion { get; set; } = SyncProtocol.Version;
    public string RequestId { get; set; } = string.Empty;
    public int CapturedTotalDays { get; set; } = -1;
    public List<BookNpcSnapshot> Npcs { get; set; } = new();
}

/// <summary>单个 NPC 的手册只读数据；不携带 LivingNpcState 或任何游戏对象。</summary>
internal sealed class BookNpcSnapshot
{
    public string NpcName { get; set; } = string.Empty;
    public string CurrentEmotion { get; set; } = "Calm";
    public int EmotionIntensity { get; set; }
    public string InteractionComfortTier { get; set; } = "Distant";
    public int RelationshipTrust { get; set; } = 20;
    public string FarmerNickname { get; set; } = string.Empty;
    public int ConsecutiveConversationDays { get; set; }
    public int LastConversationTotalDays { get; set; } = -1;
    public string RelationshipImpression { get; set; } = string.Empty;
    public int RelationshipImpressionUpdatedTotalDays { get; set; } = -1;
    public string LastGiftName { get; set; } = string.Empty;
    public int LastGiftTotalDays { get; set; } = -1;
    public int LastUpdatedTotalDays { get; set; } = -1;
    public List<BookLongTermMemorySnapshot> LongTermMemories { get; set; } = new();
    public List<BookPlayerPreferenceSnapshot> PlayerPreferenceMemories { get; set; } = new();
    public List<BookSharedExperienceSnapshot> SharedExperiences { get; set; } = new();
    public List<BookHelpRequestSnapshot> HelpRequests { get; set; } = new();
    public List<BookConflictSnapshot> Conflicts { get; set; } = new();
}

internal sealed class BookLongTermMemorySnapshot
{
    public string Kind { get; set; } = "fact";
    public string Summary { get; set; } = string.Empty;
    public int Importance { get; set; }
    public int LastUpdatedTotalDays { get; set; } = -1;
    public int TimesReinforced { get; set; }
}

internal sealed class BookPlayerPreferenceSnapshot
{
    public string Summary { get; set; } = string.Empty;
    public int Importance { get; set; }
}

internal sealed class BookSharedExperienceSnapshot
{
    public string Summary { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public int CreatedTotalDays { get; set; } = -1;
    public int LastUpdatedTotalDays { get; set; } = -1;
}

internal sealed class BookHelpRequestSnapshot
{
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int CreatedTotalDays { get; set; } = -1;
}

internal sealed class BookConflictSnapshot
{
    public string CauseKind { get; set; } = "dialogue";
    public string Summary { get; set; } = string.Empty;
    public int Severity { get; set; }
    public string Status { get; set; } = "Active";
}
