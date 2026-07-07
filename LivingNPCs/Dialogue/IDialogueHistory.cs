using System.Collections.Generic;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Dialogue;

/// <summary>
/// 跨包契约（01 §2）：WP14 实现，WP10/WP16 消费。成员由 01 钉死，不得私自增删。
/// </summary>
internal interface IDialogueHistory
{
    void Append(string npcName, ExchangeRecord record);

    IReadOnlyList<ExchangeRecord> Recent(string npcName, int maxItems);
}

/// <summary>四类历史记录（WP10 §4.14）在契约面上的统一形态。</summary>
internal enum ExchangeKind
{
    /// <summary>完整会话：以 ConversationId（首元素 GUID）为会话 ID，同 ID 覆盖（upsert）。</summary>
    Conversation,

    /// <summary>一次展示的原版/生成台词行组。</summary>
    DialogueLines,

    /// <summary>事件/节日中的台词 + 在场者（EventName/ListenerNames）。</summary>
    EventLines,

    /// <summary>旁听的（说话人, 台词）（SpeakerName）。</summary>
    Overheard
}

/// <summary>
/// 一次交换记录。字段的最终裁定权在 WP10（01 §2：冲突以 WP10 为准）；本定义覆盖 WP10 §4.14
/// 四类记录所需的全部载荷，WP10 落地时如需扩展在此文件追加。
/// </summary>
internal sealed class ExchangeRecord
{
    public ExchangeKind Kind { get; set; } = ExchangeKind.Conversation;

    public StardewTime Time { get; set; }

    /// <summary>Conversation 专用：会话 ID；空则视为新会话。同会话每轮整体覆盖。</summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>Overheard 专用：说话者 NPC 内部名。</summary>
    public string SpeakerName { get; set; } = string.Empty;

    /// <summary>EventLines 专用：事件/节日名。</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>EventLines 专用：在场 NPC 内部名（不持有游戏对象）。</summary>
    public List<string> ListenerNames { get; set; } = new();

    public List<ExchangeLine> Lines { get; set; } = new();
}

internal sealed record ExchangeLine(string Text, bool IsPlayerLine);
