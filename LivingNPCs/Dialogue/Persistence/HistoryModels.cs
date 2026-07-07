using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 每 NPC 历史的持久化模型（WP14 §5.2 新契约）。四个数组的元素形态沿用旧格式的
/// {"Item1": 时间, "Item2": 载荷}（Tuple 序列化产物），使迁移解析器与新数据读取共用一套代码；
/// 新增顶层 schemaVersion / npcName；台词只存纯文本字段，绝不序列化游戏对象（§3.1.1 Listeners 事故）。
/// 修剪语义按 WP10 §4.15 的契约实现：容量上限、112 天年龄上限（事件除外）、当日条目保护、幂等，
/// 被修剪的会话按时间正序返回给调用方归档；何时修剪（策略）由 WP10 决定。
/// </summary>
internal sealed class StardewEventHistory
{
    public const int MaxEventEntries = 40;
    public const int MaxOverheardEntries = 40;
    public const int MaxDialogueEntries = 60;
    public const int MaxConversationEntries = 30;
    public const int MaxAgeDays = 112;

    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("npcName")]
    public string NpcName { get; set; } = string.Empty;

    [JsonProperty("EventHistory")]
    public List<Tuple<StardewTime, DialogueEventHistory>> EventHistory { get; set; } = new();

    [JsonProperty("OverheardHistory")]
    public List<Tuple<StardewTime, OverheardHistory>> OverheardHistory { get; set; } = new();

    [JsonProperty("DialogueHistory")]
    public List<Tuple<StardewTime, DialogueHistory>> DialogueHistory { get; set; } = new();

    [JsonProperty("ConversationHistory")]
    public List<Tuple<StardewTime, ConversationHistory>> ConversationHistory { get; set; } = new();

    /// <summary>第三方目击记录不持久化（WP10 §4.14），仅当日在内存中参与采样。</summary>
    [JsonIgnore]
    public List<Tuple<StardewTime, ThirdPartyHistory>> ThirdPartyHistory { get; set; } = new();

    public void Add(StardewTime time, ConversationHistory history)
    {
        if (history != null)
        {
            this.ConversationHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, DialogueHistory history)
    {
        if (history != null)
        {
            this.DialogueHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, DialogueEventHistory history)
    {
        if (history != null)
        {
            this.EventHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, OverheardHistory history)
    {
        if (history != null)
        {
            this.OverheardHistory.Add(Tuple.Create(time, history));
        }
    }

    public void Add(StardewTime time, ThirdPartyHistory history)
    {
        if (history != null)
        {
            this.ThirdPartyHistory.Add(Tuple.Create(time, history));
        }
    }

    public bool Any()
    {
        return this.ConversationHistory.Count > 0
            || this.DialogueHistory.Count > 0
            || this.EventHistory.Count > 0
            || this.OverheardHistory.Count > 0;
    }

    /// <summary>回档保护：删除时间戳晚于当前游戏时间的条目，返回删除条数。</summary>
    public int RemoveAfter(StardewTime now)
    {
        int removed = 0;
        removed += this.EventHistory.RemoveAll(entry => entry.Item1.CompareTo(now) > 0);
        removed += this.OverheardHistory.RemoveAll(entry => entry.Item1.CompareTo(now) > 0);
        removed += this.DialogueHistory.RemoveAll(entry => entry.Item1.CompareTo(now) > 0);
        removed += this.ConversationHistory.RemoveAll(entry => entry.Item1.CompareTo(now) > 0);
        return removed;
    }

    /// <summary>
    /// 幂等修剪。返回被修剪掉的会话（时间正序），供调用方交给转录导出器归档；
    /// 其余类型的修剪结果直接丢弃。
    /// </summary>
    public List<Tuple<StardewTime, ConversationHistory>> Prune(StardewTime now)
    {
        int today = now.ToAbsoluteDays();
        int oldestAllowedDay = today - MaxAgeDays;

        this.EventHistory = PruneList(this.EventHistory, today, maxEntries: MaxEventEntries, oldestAllowedDay: null, out _);
        this.OverheardHistory = PruneList(this.OverheardHistory, today, MaxOverheardEntries, oldestAllowedDay, out _);
        this.DialogueHistory = PruneList(this.DialogueHistory, today, MaxDialogueEntries, oldestAllowedDay, out _);
        this.ConversationHistory = PruneList(this.ConversationHistory, today, MaxConversationEntries, oldestAllowedDay, out var droppedConversations);
        return droppedConversations;
    }

    private static List<Tuple<StardewTime, T>> PruneList<T>(
        List<Tuple<StardewTime, T>> entries,
        int today,
        int maxEntries,
        int? oldestAllowedDay,
        out List<Tuple<StardewTime, T>> dropped)
    {
        var ordered = entries.OrderBy(entry => entry.Item1).ToList();
        dropped = new List<Tuple<StardewTime, T>>();

        if (oldestAllowedDay.HasValue)
        {
            dropped.AddRange(ordered.Where(entry => entry.Item1.ToAbsoluteDays() < oldestAllowedDay.Value));
            ordered = ordered.Where(entry => entry.Item1.ToAbsoluteDays() >= oldestAllowedDay.Value).ToList();
        }

        int overflow = ordered.Count - maxEntries;
        if (overflow > 0)
        {
            // 当日条目受保护：只在非当日条目中按最老优先淘汰（进行中的会话每轮以当日
            // 时间戳重写，不得被修剪归档）。
            var keep = new List<Tuple<StardewTime, T>>(ordered.Count);
            foreach (var entry in ordered)
            {
                if (overflow > 0 && entry.Item1.ToAbsoluteDays() != today)
                {
                    dropped.Add(entry);
                    overflow--;
                }
                else
                {
                    keep.Add(entry);
                }
            }

            ordered = keep;
        }

        dropped = dropped.OrderBy(entry => entry.Item1).ToList();
        return ordered;
    }
}

/// <summary>一次展示的台词行组（原版或生成台词）。</summary>
internal sealed class DialogueHistory
{
    public DialogueHistory()
    {
    }

    public DialogueHistory(List<HistoryLine> dialogues)
    {
        this.Dialogues = dialogues ?? new List<HistoryLine>();
    }

    [JsonProperty("Dialogues")]
    public List<HistoryLine> Dialogues { get; set; } = new();
}

/// <summary>事件/节日中的台词 + 在场者 + 事件名。在场者只存内部名，不存游戏对象。</summary>
internal sealed class DialogueEventHistory
{
    public DialogueEventHistory()
    {
    }

    public DialogueEventHistory(List<string>? listenerNames, List<HistoryLine>? dialogues, string eventName)
    {
        this.Listeners = listenerNames ?? new List<string>();
        this.Dialogues = dialogues ?? new List<HistoryLine>();
        this.EventName = eventName ?? string.Empty;
    }

    [JsonProperty("Dialogues")]
    public List<HistoryLine> Dialogues { get; set; } = new();

    [JsonProperty("Listeners")]
    public List<string> Listeners { get; set; } = new();

    [JsonProperty("EventName")]
    public string EventName { get; set; } = string.Empty;
}

/// <summary>旁听记录：说话者 + 台词。旧格式的两个字段是小写开头（§3.1.1），按原样保留。</summary>
internal sealed class OverheardHistory
{
    public OverheardHistory()
    {
    }

    public OverheardHistory(string speakerName, List<HistoryLine>? dialogues)
    {
        this.SpeakerName = speakerName ?? string.Empty;
        this.Dialogues = dialogues ?? new List<HistoryLine>();
    }

    [JsonProperty("name")]
    public string SpeakerName { get; set; } = string.Empty;

    [JsonProperty("dialogues")]
    public List<HistoryLine> Dialogues { get; set; } = new();
}

/// <summary>完整会话（元素列表，含玩家行标志；以首元素 GUID 为会话 ID）。</summary>
internal sealed class ConversationHistory
{
    public ConversationHistory()
    {
    }

    public ConversationHistory(List<ConversationElement> conversationElements)
    {
        this.ConversationElements = conversationElements ?? new List<ConversationElement>();
    }

    [JsonProperty("ConversationElements")]
    public List<ConversationElement> ConversationElements { get; set; } = new();
}

/// <summary>会话元素。Id 写出但读入时不还原（每次反序列化重新生成），读取方不得依赖它。</summary>
internal sealed class ConversationElement
{
    public ConversationElement()
    {
    }

    public ConversationElement(string text, bool isPlayerLine)
    {
        this.Text = text;
        this.IsPlayerLine = isPlayerLine;
    }

    [JsonProperty("Id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonProperty("Text")]
    public string Text { get; set; } = string.Empty;

    [JsonProperty("IsPlayerLine")]
    public bool IsPlayerLine { get; set; }
}

/// <summary>台词行：只有 Text 承载数据（旧格式的 SideEffects/HasText 读取时忽略）。</summary>
internal sealed class HistoryLine
{
    public HistoryLine()
    {
    }

    public HistoryLine(string text)
    {
        this.Text = text ?? string.Empty;
    }

    [JsonProperty("Text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>第三方目击记录：无可序列化桶，只入内存（当日采样用）。</summary>
internal sealed class ThirdPartyHistory
{
    public ThirdPartyHistory(string speakerName, List<HistoryLine>? dialogues, string eventName)
    {
        this.SpeakerName = speakerName ?? string.Empty;
        this.Dialogues = dialogues ?? new List<HistoryLine>();
        this.EventName = eventName ?? string.Empty;
    }

    public string SpeakerName { get; }
    public List<HistoryLine> Dialogues { get; }
    public string EventName { get; }
}
