using System;
using System.Collections.Generic;
using System.Linq;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 提示词内历史采样（WP10 §4.15）：四类记录 + 第三方目击 + 活动事件合成，按时间合并后
/// 取最近 20 条，再从最新往回累计 4000 字符预算，输出按时间正序。纯逻辑、可单测。
/// </summary>
internal static class HistorySampler
{
    public const int MaxEntries = 20;
    public const int CharacterBudget = 4000;

    /// <summary>可合成为历史条目的活动事件键（§4.15）。</summary>
    private static readonly string[] SynthesizableEventKeys =
    {
        "cc_Bus", "cc_Boulder", "cc_Bridge", "cc_Complete", "cc_Greenhouse", "cc_Minecart",
        "wonIceFishing", "wonGrange", "wonEggHunt"
    };

    /// <summary>采样并格式化为提示词历史行（时间正序）。</summary>
    public static List<string> Sample(
        StardewEventHistory history,
        IReadOnlyList<KeyValuePair<string, int>> activeDialogueEvents,
        StardewTime now,
        string npcDisplayName,
        string currentConversationId)
    {
        var entries = new List<(StardewTime Time, string Text)>();

        foreach (var entry in history.ConversationHistory)
        {
            string id = entry.Item2.ConversationElements.FirstOrDefault()?.Id ?? string.Empty;
            if (!string.IsNullOrEmpty(currentConversationId) && id == currentConversationId)
            {
                // 当前进行中的会话不重复入历史（§4.15）。
                continue;
            }

            string transcript = JoinConversation(entry.Item2.ConversationElements, npcDisplayName);
            if (transcript.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyConversationFormat", entry.Item1, transcript, string.Empty, string.Empty)));
            }
        }

        foreach (var entry in history.DialogueHistory)
        {
            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyDialogueFormat", entry.Item1, text, string.Empty, string.Empty)));
            }
        }

        foreach (var entry in history.EventHistory)
        {
            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyEventFormat", entry.Item1, text, string.Empty, entry.Item2.EventName)));
            }
        }

        foreach (var entry in history.OverheardHistory)
        {
            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyOverheardFormat", entry.Item1, text, entry.Item2.SpeakerName, string.Empty)));
            }
        }

        foreach (var entry in history.ThirdPartyHistory)
        {
            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                string key = string.IsNullOrWhiteSpace(entry.Item2.EventName)
                    ? "historyThirdPartyFormat"
                    : "historyThirdPartyFestival";
                entries.Add((entry.Item1, Format(key, entry.Item1, text, entry.Item2.SpeakerName, entry.Item2.EventName)));
            }
        }

        foreach (var synthesized in SynthesizeActiveEvents(activeDialogueEvents, now))
        {
            entries.Add(synthesized);
        }

        // 最近 20 条 → 从最新往回累计预算 → 时间正序输出。
        var newestFirst = entries
            .OrderByDescending(entry => entry.Time)
            .Take(MaxEntries)
            .ToList();

        var kept = new List<(StardewTime Time, string Text)>();
        int budget = CharacterBudget;
        foreach (var entry in newestFirst)
        {
            if (entry.Text.Length > budget)
            {
                break;
            }

            budget -= entry.Text.Length;
            kept.Add(entry);
        }

        return kept
            .OrderBy(entry => entry.Time)
            .Select(entry => entry.Text)
            .ToList();
    }

    /// <summary>活动事件合成（键限定集合；天数 &lt;112 或为 112 整倍数；时间戳 = 今天 − 天数）。</summary>
    internal static List<(StardewTime Time, string Text)> SynthesizeActiveEvents(
        IReadOnlyList<KeyValuePair<string, int>> activeDialogueEvents,
        StardewTime now)
    {
        var result = new List<(StardewTime, string)>();
        foreach (var pair in activeDialogueEvents)
        {
            if (!SynthesizableEventKeys.Contains(pair.Key, StringComparer.Ordinal))
            {
                continue;
            }

            int days = pair.Value;
            if (days >= 112 && days % 112 != 0)
            {
                continue;
            }

            StardewTime when = now.AddDays(-days);
            // 键族 historyActiveEvent_<eventKey> 属 WP20 提示词表（经 Util 的提示词回退可达）。
            string textKey = "historyActiveEvent_" + pair.Key;
            string text = Util.GetString(textKey, new { days });
            result.Add((when, text));
        }

        return result;
    }

    private static string JoinConversation(List<ConversationElement> elements, string npcDisplayName)
    {
        string farmerLabel = Util.GetString("generalFarmerLabel");
        return string.Join(
            " / ",
            elements
                .Where(element => !string.IsNullOrWhiteSpace(element.Text))
                .Select(element => $"{(element.IsPlayerLine ? farmerLabel : npcDisplayName)}: {element.Text.Trim()}"));
    }

    private static string JoinLines(List<HistoryLine> lines)
    {
        return string.Join(
            " / ",
            lines.Where(line => !string.IsNullOrWhiteSpace(line.Text)).Select(line => line.Text.Trim()));
    }

    private static string Format(string key, StardewTime time, string text, string speaker, string eventName)
    {
        string when = $"Y{time.year} {time.season} {time.dayOfMonth} {GameStateSnapshot.ClockText(time.timeOfDay)}";
        string formatted = Util.GetString(key, new { when, text, speaker, eventName }, returnNull: true);
        if (!string.IsNullOrWhiteSpace(formatted) && formatted != key)
        {
            return formatted;
        }

        // WP20 文案缺失时的兜底格式，保证历史仍然可用。
        string label = string.IsNullOrWhiteSpace(speaker) ? string.Empty : $"{speaker}: ";
        string suffix = string.IsNullOrWhiteSpace(eventName) ? string.Empty : $" ({eventName})";
        return $"[{when}] {label}{text}{suffix}";
    }
}
