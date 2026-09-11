using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using LivingNPCs.Dialogue.Content;
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
    private static readonly Dictionary<string, (string PromptKey, string Fallback)> SynthesizableEvents = new(StringComparer.Ordinal)
    {
        ["cc_Bus"] = ("cc_Bus_Repaired", "The farmer helped restore bus service to Calico Desert."),
        ["cc_Boulder"] = ("cc_Boulder_Removed", "The farmer helped clear the mountain boulder."),
        ["cc_Bridge"] = ("cc_Bridge", "The farmer helped repair the bridge to the quarry."),
        ["cc_Complete"] = ("cc_Complete", "The farmer helped restore the Community Center."),
        ["cc_Greenhouse"] = ("cc_Greenhouse", "The farmer restored the old greenhouse on the farm."),
        ["cc_Minecart"] = ("cc_Minecart", "The farmer helped get the minecarts running again."),
        ["wonIceFishing"] = ("wonIceFishing", "The farmer won the ice fishing contest."),
        ["wonGrange"] = ("wonGrange", "The farmer won the grange display at the Stardew Valley Fair."),
        ["wonEggHunt"] = ("wonEggHunt", "The farmer won the Egg Festival hunt.")
    };

    private static readonly Regex TemplateTokens = new(@"\{\{\s*([\w.\-]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>采样并格式化为提示词历史行（时间正序）。</summary>
    public static List<string> Sample(
        StardewEventHistory history,
        IReadOnlyList<KeyValuePair<string, int>> activeDialogueEvents,
        StardewTime now,
        string npcDisplayName,
        string currentConversationId,
        Func<string, string?>? getPrompt = null)
    {
        getPrompt ??= LookupPrompt;
        var entries = new List<(StardewTime Time, string Text)>();

        foreach (var entry in history.ConversationHistory)
        {
            string id = entry.Item2.ConversationElements.FirstOrDefault()?.Id ?? string.Empty;
            if (!string.IsNullOrEmpty(currentConversationId) && id == currentConversationId)
            {
                // 当前进行中的会话不重复入历史（§4.15）。
                continue;
            }

            string transcript = JoinConversation(entry.Item2.ConversationElements, npcDisplayName, getPrompt);
            if (transcript.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyConversationFormat", entry.Item1, transcript, npcDisplayName, string.Empty, npcDisplayName, null, getPrompt)));
            }
        }

        foreach (var entry in history.DialogueHistory)
        {
            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyDialogueFormat", entry.Item1, text, npcDisplayName, string.Empty, npcDisplayName, null, getPrompt)));
            }
        }

        foreach (var entry in history.EventHistory)
        {
            if (RsvAiPolicy.ContainsBlockedReference(entry.Item2.EventName)
                || RsvAiPolicy.IsBlockedDialogueKey(entry.Item2.EventName))
            {
                continue;
            }

            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyEventFormat", entry.Item1, text, npcDisplayName, entry.Item2.EventName, npcDisplayName, entry.Item2.Listeners, getPrompt)));
            }
        }

        foreach (var entry in history.OverheardHistory)
        {
            if (RsvAiPolicy.IsBlockedNpcName(entry.Item2.SpeakerName))
            {
                continue;
            }

            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                entries.Add((entry.Item1, Format("historyOverheardFormat", entry.Item1, text, entry.Item2.SpeakerName, string.Empty, npcDisplayName, null, getPrompt)));
            }
        }

        foreach (var entry in history.ThirdPartyHistory)
        {
            if (RsvAiPolicy.IsBlockedNpcName(entry.Item2.SpeakerName)
                || RsvAiPolicy.ContainsBlockedReference(entry.Item2.EventName)
                || RsvAiPolicy.IsBlockedDialogueKey(entry.Item2.EventName))
            {
                continue;
            }

            string text = JoinLines(entry.Item2.Dialogues);
            if (text.Length > 0)
            {
                // historyThirdPartyFestival is only an event-name suffix, never a complete record.
                entries.Add((entry.Item1, Format("historyThirdPartyFormat", entry.Item1, text, entry.Item2.SpeakerName, entry.Item2.EventName, npcDisplayName, null, getPrompt)));
            }
        }

        foreach (var synthesized in SynthesizeActiveEvents(activeDialogueEvents, now, getPrompt))
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
        StardewTime now,
        Func<string, string?>? getPrompt = null)
    {
        getPrompt ??= LookupPrompt;
        var result = new List<(StardewTime, string)>();
        foreach (var pair in activeDialogueEvents)
        {
            if (!SynthesizableEvents.TryGetValue(pair.Key, out var definition))
            {
                continue;
            }

            int days = pair.Value;
            if (days >= 112 && days % 112 != 0)
            {
                continue;
            }

            StardewTime when = now.AddDays(-days);
            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["days"] = days.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["when"] = FormatTime(when)
            };
            // Preserve custom legacy keys, then use the localized assets that actually exist.
            string text = RenderTemplate("historyActiveEvent_" + pair.Key, tokens, getPrompt)
                ?? RenderTemplate(definition.PromptKey, tokens, getPrompt)
                ?? definition.Fallback;
            result.Add((when, WithTime(text, tokens["when"])));
        }

        return result;
    }

    private static string JoinConversation(List<ConversationElement> elements, string npcDisplayName, Func<string, string?> getPrompt)
    {
        string farmerLabel = getPrompt("generalFarmerLabel") ?? "Farmer";
        var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
            elements,
            element => element.Text,
            element => element.IsPlayerLine);
        return string.Join(
            " / ",
            cleaned
                .Where(element => !string.IsNullOrWhiteSpace(element.Text)
                    && !RsvAiPolicy.ContainsBlockedReference(element.Text))
                .Select(element => $"{(element.IsPlayerLine ? farmerLabel : npcDisplayName)}: {element.Text.Trim()}"));
    }

    private static string JoinLines(List<HistoryLine> lines)
    {
        return string.Join(
            " / ",
            lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text)
                    && !RsvAiPolicy.ContainsBlockedReference(line.Text))
                .Select(line => line.Text.Trim()));
    }

    private static string Format(
        string key,
        StardewTime time,
        string text,
        string speaker,
        string eventName,
        string observer,
        IReadOnlyList<string>? listeners,
        Func<string, string?> getPrompt)
    {
        string when = FormatTime(time);
        string[] allowedListeners = (listeners ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && !RsvAiPolicy.IsBlockedNpcName(name)
                && !RsvAiPolicy.ContainsBlockedReference(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string listenerText = string.Join(", ", allowedListeners);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["when"] = when,
            ["text"] = text,
            ["speaker"] = speaker,
            ["observer"] = observer,
            ["eventName"] = eventName,
            ["listeners"] = listenerText,
            // Text/event aliases have stable meanings across older content packs.
            ["builder"] = text,
            ["totalDialogue"] = text,
            ["allListeners"] = listenerText.Length > 0
                ? listenerText
                : key == "historyDialogueFormat" ? getPrompt("generalFarmerLabel") ?? "Farmer" : string.Empty,
            ["festivalName"] = eventName
        };
        if (key is not "historyThirdPartyFormat" and not "historyOverheardFormat")
        {
            tokens["npcName"] = speaker;
            tokens["name"] = speaker;
        }
        // Legacy witness templates used Name/npcName for opposite roles in their base and
        // gender variants. Leave those ambiguous aliases unresolved so they take the complete
        // fact fallback; custom witness templates can use explicit observer/speaker instead.
        string eventContext = string.IsNullOrWhiteSpace(eventName)
            ? string.Empty
            : RenderTemplate("historyThirdPartyFestival", tokens, getPrompt) ?? $" ({eventName})";
        if (!string.IsNullOrWhiteSpace(eventName) && !eventContext.Contains(eventName, StringComparison.Ordinal))
        {
            eventContext = $" ({eventName})";
        }

        tokens["festivalNameString"] = eventContext;
        tokens["listenerContext"] = listenerText.Length == 0
            ? string.Empty
            : RenderTemplate("historyListeners", tokens, getPrompt) ?? $" ({listenerText})";

        string? formatted = RenderTemplate(key, tokens, getPrompt);
        // A localized/custom template must retain the evidence, not replace it with boilerplate.
        // Inspect the template's parameters before substitution so literal {{...}} in dialogue
        // remains ordinary data rather than being mistaken for an unresolved template parameter.
        if (!string.IsNullOrWhiteSpace(formatted)
            && new[] { text, speaker, eventName, observer }.Concat(allowedListeners)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .All(value => formatted.Contains(value, StringComparison.Ordinal)))
        {
            return WithTime(formatted, when);
        }

        // No persistence is changed: only the sampled prompt falls back to a complete fact line.
        string observerLabel = !string.IsNullOrWhiteSpace(observer) && observer != speaker ? $"{observer} ← " : string.Empty;
        string speakerLabel = string.IsNullOrWhiteSpace(speaker) ? string.Empty : $"{speaker}: ";
        string eventLabel = string.IsNullOrWhiteSpace(eventName) ? string.Empty : $" ({eventName})";
        string listenerLabel = listenerText.Length == 0 ? string.Empty : $" [{listenerText}]";
        return $"[{when}] {observerLabel}{speakerLabel}{text}{eventLabel}{listenerLabel}";
    }

    private static string? RenderTemplate(string key, IReadOnlyDictionary<string, string> tokens, Func<string, string?> getPrompt)
    {
        string? template = getPrompt(key);
        if (string.IsNullOrWhiteSpace(template) || template == key
            || TemplateTokens.Matches(template).Cast<Match>().Any(match => !tokens.ContainsKey(match.Groups[1].Value)))
        {
            return null;
        }

        return PromptTable.ReplaceTokens(template, tokens);
    }

    private static string FormatTime(StardewTime time) =>
        $"Y{time.year} {time.season} {time.dayOfMonth} {GameStateSnapshot.ClockText(time.timeOfDay)}";

    private static string WithTime(string text, string when) =>
        text.Contains(when, StringComparison.Ordinal) ? text : $"[{when}] {text}";

    private static string? LookupPrompt(string key) => Util.GetString(key, returnNull: true);
}
