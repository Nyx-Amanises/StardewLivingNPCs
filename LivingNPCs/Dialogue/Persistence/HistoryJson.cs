using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StardewValley;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 历史 JSON 的容错解析（WP14 §4.4，迁移与日常读取共用）。四个数组独立解析、坏元素丢弃不连坐；
/// 兼容旧格式（无 schemaVersion 顶层、Listeners 序列化 NPC 对象、更老的 chatHistory 形态）与
/// 新格式（§5.2）。整体解析失败由调用方处理（迁移记失败键、日常读取当空历史）。
/// </summary>
internal static class HistoryJson
{
    /// <summary>逐条容错地把一个历史 JSON 对象解析为模型；root 为 null 或非对象返回空历史。</summary>
    public static StardewEventHistory Parse(JObject? root, string npcName)
    {
        var history = new StardewEventHistory
        {
            NpcName = !string.IsNullOrWhiteSpace(npcName)
                ? npcName
                : root?.Value<string>("npcName") ?? string.Empty
        };

        if (root == null)
        {
            return history;
        }

        foreach (var entry in ParseEntries(root["EventHistory"], ParseEventPayload))
        {
            history.EventHistory.Add(entry);
        }

        foreach (var entry in ParseEntries(root["OverheardHistory"], ParseOverheardPayload))
        {
            history.OverheardHistory.Add(entry);
        }

        foreach (var entry in ParseEntries(root["DialogueHistory"], ParseDialoguePayload))
        {
            history.DialogueHistory.Add(entry);
        }

        foreach (var entry in ParseEntries(root["ConversationHistory"], ParseConversationPayload))
        {
            history.ConversationHistory.Add(entry);
        }

        return history;
    }

    private static IEnumerable<Tuple<StardewTime, T>> ParseEntries<T>(JToken? array, Func<JObject, T?> parsePayload)
        where T : class
    {
        if (array is not JArray items)
        {
            yield break;
        }

        foreach (var item in items)
        {
            Tuple<StardewTime, T>? entry = null;
            try
            {
                if (item is JObject pair
                    && TryParseTime(pair["Item1"], out var time)
                    && pair["Item2"] is JObject payloadToken)
                {
                    var payload = parsePayload(payloadToken);
                    if (payload != null)
                    {
                        entry = Tuple.Create(time, payload);
                    }
                }
            }
            catch
            {
                // 坏元素丢弃不连坐。
            }

            if (entry != null)
            {
                yield return entry;
            }
        }
    }

    /// <summary>时间容错：季节字符串大小写不敏感；非法季节/负数日期的条目丢弃。</summary>
    internal static bool TryParseTime(JToken? token, out StardewTime time)
    {
        time = default;
        if (token is not JObject obj)
        {
            return false;
        }

        string? seasonText = obj.Value<string>("season");
        if (string.IsNullOrWhiteSpace(seasonText) || !Enum.TryParse(seasonText, ignoreCase: true, out Season season))
        {
            return false;
        }

        int year = obj.Value<int?>("year") ?? 0;
        int dayOfMonth = obj.Value<int?>("dayOfMonth") ?? 0;
        int timeOfDay = obj.Value<int?>("timeOfDay") ?? 0;
        if (year < 0 || dayOfMonth < 0 || timeOfDay < 0)
        {
            return false;
        }

        time = new StardewTime(year, season, dayOfMonth, timeOfDay);
        return true;
    }

    private static DialogueEventHistory? ParseEventPayload(JObject payload)
    {
        // Listeners 在旧存档里序列化的是完整 NPC 对象（实测全部为空数组），必须容忍任意内容：
        // 字符串按名字收，对象取 Name 字段，其余丢弃；解析异常时允许整条丢弃（由调用方捕获）。
        var listeners = new List<string>();
        if (payload["Listeners"] is JArray rawListeners)
        {
            foreach (var listener in rawListeners)
            {
                string? name = listener.Type == JTokenType.String
                    ? listener.Value<string>()
                    : (listener as JObject)?.Value<string>("Name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    listeners.Add(name);
                }
            }
        }

        return new DialogueEventHistory(
            listeners,
            ParseLines(payload["Dialogues"]),
            payload.Value<string>("EventName") ?? string.Empty);
    }

    private static OverheardHistory? ParseOverheardPayload(JObject payload)
    {
        return new OverheardHistory(
            payload.Value<string>("name") ?? string.Empty,
            ParseLines(payload["dialogues"]));
    }

    private static DialogueHistory? ParseDialoguePayload(JObject payload)
    {
        return new DialogueHistory(ParseLines(payload["Dialogues"]));
    }

    private static ConversationHistory? ParseConversationPayload(JObject payload)
    {
        if (payload["ConversationElements"] is JArray elements)
        {
            var parsed = new List<ConversationElement>();
            foreach (var element in elements)
            {
                if (element is JObject obj)
                {
                    string? text = obj.Value<string>("Text");
                    if (text != null)
                    {
                        // Id 不还原：每次反序列化重新生成，读取方不得依赖它（§3.1.1）。
                        parsed.Add(new ConversationElement(text, obj.Value<bool?>("IsPlayerLine") ?? false));
                    }
                }
            }

            if (parsed.Count > 0)
            {
                return new ConversationHistory(parsed);
            }
        }

        // 更老的替代形态：chatHistory 字符串数组，偶数下标为 NPC 台词、奇数下标为玩家台词。
        if (payload["chatHistory"] is JArray chatHistory)
        {
            var parsed = chatHistory
                .Where(item => item.Type == JTokenType.String)
                .Select((item, index) => new ConversationElement(item.Value<string>() ?? string.Empty, index % 2 == 1))
                .ToList();
            if (parsed.Count > 0)
            {
                return new ConversationHistory(parsed);
            }
        }

        return null;
    }

    /// <summary>DialogueLine 只取 Text；SideEffects/HasText 等未知字段忽略。</summary>
    private static List<HistoryLine> ParseLines(JToken? token)
    {
        var lines = new List<HistoryLine>();
        if (token is not JArray items)
        {
            return lines;
        }

        foreach (var item in items)
        {
            string? text = (item as JObject)?.Value<string>("Text");
            if (text != null)
            {
                lines.Add(new HistoryLine(text));
            }
        }

        return lines;
    }
}
