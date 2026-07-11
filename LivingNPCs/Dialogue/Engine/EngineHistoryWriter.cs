using System;
using System.Collections.Generic;
using System.Linq;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 引擎侧历史写入语义（WP10 §4.14）：四类记录写入 + 去重 + 应答提示行剔除 + 会话续接缓存 +
/// 清除接口。持久化本体在 WP14（DialogueHistoryStore），本类只负责语义。
/// </summary>
internal sealed class EngineHistoryWriter
{
    private readonly DialogueHistoryStore store;
    private readonly object gate = new();

    /// <summary>
    /// 玩家可读聊天记录的即时导出通道。每次完整 AI 会话写入历史后调用；测试可替换。
    /// </summary>
    internal Action<string, StardewEventHistory>? TranscriptSink { get; set; }
        = (npcName, _) => ConversationTranscriptExporter.QueueExport(npcName);

    /// <summary>会话续接缓存：NPC 名 → 最近上下文（连续对话间按元素 GUID 去重合并）。</summary>
    private readonly Dictionary<string, List<ConversationTurn>> recentContext = new(StringComparer.OrdinalIgnoreCase);

    public EngineHistoryWriter(DialogueHistoryStore store)
    {
        this.store = store;
    }

    // ---- 写入（WP12 在台词实际展示后回调；引擎在会话收尾时调用） ----

    /// <summary>一次展示的台词行组。与最后一条 DialogueHistory 完全一致则跳过。</summary>
    public void AddDialogueLine(string npcName, IReadOnlyList<string> lines, StardewTime now)
    {
        var cleaned = StripSyntheticResponseLines(lines);
        if (cleaned.Count == 0)
        {
            return;
        }

        var history = this.store.GetHistory(npcName);
        var last = history.DialogueHistory.OrderBy(entry => entry.Item1).LastOrDefault();
        if (last != null && SameTexts(last.Item2.Dialogues.Select(line => line.Text), cleaned))
        {
            return;
        }

        this.store.Append(npcName, new ExchangeRecord
        {
            Kind = ExchangeKind.DialogueLines,
            Time = now,
            Lines = cleaned.Select(text => new ExchangeLine(text, false)).ToList()
        });
        this.store.PruneAndArchive(npcName);
    }

    /// <summary>事件/节日台词；同时给每个在场者写第三方目击记录（仅内存，§4.14）。</summary>
    public void AddEventLine(string npcName, string eventName, IReadOnlyList<string> lines, IReadOnlyList<string> listenerNames, StardewTime now)
    {
        var cleaned = StripSyntheticResponseLines(lines);
        if (cleaned.Count == 0)
        {
            return;
        }

        var history = this.store.GetHistory(npcName);
        var last = history.DialogueHistory.OrderBy(entry => entry.Item1).LastOrDefault();
        if (last != null && SameTexts(last.Item2.Dialogues.Select(line => line.Text), cleaned))
        {
            return;
        }

        this.store.Append(npcName, new ExchangeRecord
        {
            Kind = ExchangeKind.EventLines,
            Time = now,
            EventName = eventName ?? string.Empty,
            ListenerNames = listenerNames?.ToList() ?? new List<string>(),
            Lines = cleaned.Select(text => new ExchangeLine(text, false)).ToList()
        });

        foreach (string listener in listenerNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(listener) || string.Equals(listener, npcName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            this.store.GetHistory(listener).Add(now, new ThirdPartyHistory(
                npcName,
                cleaned.Select(text => new HistoryLine(text)).ToList(),
                eventName ?? string.Empty));
        }

        this.store.PruneAndArchive(npcName);
    }

    /// <summary>旁听记录；先删除同说话人重叠文本的旧条目（§4.14）。</summary>
    public void AddOverheardLine(string npcName, string speakerName, IReadOnlyList<string> lines, StardewTime now)
    {
        var cleaned = StripSyntheticResponseLines(lines);
        if (cleaned.Count == 0)
        {
            return;
        }

        var history = this.store.GetHistory(npcName);
        int removed = history.OverheardHistory.RemoveAll(entry =>
            string.Equals(entry.Item2.SpeakerName, speakerName, StringComparison.OrdinalIgnoreCase)
            && entry.Item2.Dialogues.Any(line => cleaned.Contains(line.Text, StringComparer.Ordinal)));
        if (removed > 0)
        {
            this.store.MarkDirty(npcName);
        }

        this.store.Append(npcName, new ExchangeRecord
        {
            Kind = ExchangeKind.Overheard,
            Time = now,
            SpeakerName = speakerName ?? string.Empty,
            Lines = cleaned.Select(text => new ExchangeLine(text, false)).ToList()
        });
        this.store.PruneAndArchive(npcName);
    }

    /// <summary>每轮会话后整体 upsert（同 ID 覆盖）；删除文本与之重叠的 DialogueHistory 条目。</summary>
    public void UpsertConversation(string npcName, IReadOnlyList<ConversationTurn> turns, StardewTime now)
    {
        if (turns == null || turns.Count == 0)
        {
            return;
        }

        var texts = turns.Select(turn => turn.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToHashSet(StringComparer.Ordinal);
        var history = this.store.GetHistory(npcName);
        int removed = history.DialogueHistory.RemoveAll(entry =>
            entry.Item2.Dialogues.Any(line => texts.Contains(line.Text)));
        if (removed > 0)
        {
            this.store.MarkDirty(npcName);
        }

        this.store.Append(npcName, new ExchangeRecord
        {
            Kind = ExchangeKind.Conversation,
            Time = now,
            ConversationId = turns[0].Id,
            Lines = turns.Select(turn => new ExchangeLine(turn.Text, turn.IsPlayerLine)).ToList()
        });
        this.store.PruneAndArchive(npcName);
        this.TranscriptSink?.Invoke(npcName, this.store.GetHistory(npcName));
    }

    // ---- 查询 ----

    /// <summary>"刚刚说过话"：最后一条历史属于三类台词记录之一且时间戳为刚才（同刻近邻）。</summary>
    public bool JustSpoke(string npcName, StardewTime now)
    {
        var history = this.store.GetHistory(npcName);
        var candidates = new List<StardewTime>();
        if (history.DialogueHistory.Count > 0)
        {
            candidates.Add(history.DialogueHistory.Max(entry => entry.Item1));
        }

        if (history.EventHistory.Count > 0)
        {
            candidates.Add(history.EventHistory.Max(entry => entry.Item1));
        }

        if (history.ConversationHistory.Count > 0)
        {
            candidates.Add(history.ConversationHistory.Max(entry => entry.Item1));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        StardewTime latest = candidates.Max();
        return latest.ToAbsoluteDays() == now.ToAbsoluteDays()
            && Math.Abs(latest.timeOfDay - now.timeOfDay) <= 10;
    }

    // ---- 会话续接缓存（§4.14） ----

    /// <summary>合并请求携带的会话与缓存上下文（按元素 GUID 去重），并刷新缓存。</summary>
    public List<ConversationTurn> MergeConversationContext(string npcName, IReadOnlyList<ConversationTurn> incoming)
    {
        lock (this.gate)
        {
            this.recentContext.TryGetValue(npcName, out var cached);
            var merged = new List<ConversationTurn>(cached ?? Enumerable.Empty<ConversationTurn>());
            var seen = new HashSet<string>(merged.Select(turn => turn.Id), StringComparer.Ordinal);
            foreach (var turn in incoming ?? Array.Empty<ConversationTurn>())
            {
                if (seen.Add(turn.Id))
                {
                    merged.Add(turn);
                }
            }

            this.recentContext[npcName] = merged;
            return new List<ConversationTurn>(merged);
        }
    }

    /// <summary>NPC 新台词追加进会话缓存。</summary>
    public void AppendToConversationContext(string npcName, ConversationTurn turn)
    {
        lock (this.gate)
        {
            if (!this.recentContext.TryGetValue(npcName, out var cached))
            {
                cached = new List<ConversationTurn>();
                this.recentContext[npcName] = cached;
            }

            if (cached.All(existing => existing.Id != turn.Id))
            {
                cached.Add(turn);
            }
        }
    }

    public IReadOnlyList<ConversationTurn> PeekConversationContext(string npcName)
    {
        lock (this.gate)
        {
            return this.recentContext.TryGetValue(npcName, out var cached)
                ? new List<ConversationTurn>(cached)
                : new List<ConversationTurn>();
        }
    }

    /// <summary>存档切换、清除历史、事件结束（WP12 调 ClearContext）时清空。</summary>
    public void ClearContext()
    {
        lock (this.gate)
        {
            this.recentContext.Clear();
        }
    }

    // ---- 清除（供控制台命令回显） ----

    public bool Forget(string npcName)
    {
        this.ClearContextFor(npcName);
        bool cleared = this.store.Forget(npcName);
        Diagnostics.ConversationTranscriptExporter.ResetTranscript(npcName);
        return cleared;
    }

    public bool ForgetAll()
    {
        this.ClearContext();
        return this.store.ForgetAll();
    }

    private void ClearContextFor(string npcName)
    {
        lock (this.gate)
        {
            this.recentContext.Remove(npcName);
        }
    }

    // ---- 内部 ----

    /// <summary>剔除合成的应答提示行（以 Respond: 或本地化 outputRespond 文案开头，§4.14）。</summary>
    internal static List<string> StripSyntheticResponseLines(IReadOnlyList<string> lines)
    {
        string localizedRespond = Util.GetString("outputRespond", returnNull: true) ?? string.Empty;
        return (lines ?? Array.Empty<string>())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.TrimStart().StartsWith("Respond:", StringComparison.OrdinalIgnoreCase))
            .Where(line => string.IsNullOrWhiteSpace(localizedRespond)
                || !line.TrimStart().StartsWith(localizedRespond, StringComparison.Ordinal))
            .ToList();
    }

    private static bool SameTexts(IEnumerable<string> left, IReadOnlyList<string> right)
    {
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }
}
