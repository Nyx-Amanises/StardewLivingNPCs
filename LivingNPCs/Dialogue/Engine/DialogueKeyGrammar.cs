using System;
using System.Collections.Generic;
using System.Linq;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>婚姻小动作台词（WP10 §4.1）。</summary>
internal enum SpouseAction
{
    None,
    Patio,
    FunLeave,
    JobLeave,
    FunReturn,
    JobReturn,
    SpouseRoom
}

/// <summary>随机情境台词（WP10 §4.1）。</summary>
internal enum RandomAction
{
    None,
    Rainy,
    Indoor,
    Outdoor,
    OneKid,
    TwoKids,
    Good,
    Neutral,
    Bad
}

/// <summary>
/// 对话键按 <c>_</c> 分段解析出的上下文（WP10 §5.4）。样本选择与触发解析共用；
/// 全部为游戏数据格式，标识符精确保留。
/// </summary>
internal sealed class DialogueKeyFacts
{
    public bool IsMarriageKey { get; set; }
    public bool IsBirthdayKey { get; set; }
    public string Season { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string LocationPrefix { get; set; } = string.Empty;
    public string SpecialContextKey { get; set; } = string.Empty;
    public int? Hearts { get; set; }
    public int? Weekday { get; set; }
    public int? DayOfMonth { get; set; }
    public int? Year { get; set; }
    public bool IsGiftAccept { get; set; }
    public string GiftItem { get; set; } = string.Empty;
    public RandomAction RandomAction { get; set; }
    public SpouseAction SpouseAction { get; set; }
    /// <summary>Rainy/Indoor 后可跟的时间段（Day/Night 或钟表值）。</summary>
    public string TimeSlot { get; set; } = string.Empty;
    public string InlawName { get; set; } = string.Empty;
    public string ResortTag { get; set; } = string.Empty;
    /// <summary>配偶名（键本身不携带；由当前上下文侧填入参与差异分）。</summary>
    public string SpouseName { get; set; } = string.Empty;
    /// <summary>时间桶键（当前上下文侧填入，参与差异分）。</summary>
    public string TimeBucket { get; set; } = string.Empty;
    public bool IsGift => this.IsGiftAccept;
}

/// <summary>
/// 对话键文法（WP10 §5.4）。样本入库与触发解析（Scheduled 键首段的
/// RandomAction/SpouseAction 识别）共用。
/// </summary>
internal static class DialogueKeyGrammar
{
    private static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

    private static readonly string[] Weekdays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    private static readonly string[] LocationPrefixes =
    {
        "Beach", "Desert", "Railroad", "Saloon", "SeedShop", "JojaMart"
    };

    /// <summary>特殊上下文键集合（§5.4；多段键按最长优先贪心匹配；含 _Day/_Night 的除外）。</summary>
    private static readonly string[] SpecialContextKeys =
    {
        "cc_Boulder", "cc_Bridge", "cc_Bus", "cc_Greenhouse", "cc_Minecart", "cc_Complete",
        "movieTheater", "pamHouseUpgrade", "pamHouseUpgradeAnonymous", "jojaMartStruckByLightning",
        "babyBoy", "babyGirl", "wedding", "event_postweddingreception",
        "luauBest", "luauShorts", "luauPoisoned",
        "Characters_MovieInvite_Invited", "DumpsterDiveComment", "SpouseStardrop",
        "FlowerDance_Accept_Spouse", "FlowerDance_Accept", "FlowerDance_Decline",
        "GreenRain", "GreenRainFinished", "GreenRain_2", "Rainy"
    };

    private static readonly string[] ResortTags = { "Resort_Entering", "Resort_Leaving", "Resort" };

    public static DialogueKeyFacts Parse(string dialogueKey)
    {
        var facts = new DialogueKeyFacts();
        if (string.IsNullOrWhiteSpace(dialogueKey))
        {
            return facts;
        }

        // GUID 开头 → ChatID（取前两段），不再做其他解析。
        string[] segments = dialogueKey.Split('_');
        if (Guid.TryParse(segments[0], out _))
        {
            facts.ChatId = string.Join("_", segments.Take(2));
            return facts;
        }

        int i = 0;
        bool sawNumber = false;
        while (i < segments.Length)
        {
            // 多段特殊键 / Resort 标签：最长优先贪心匹配。
            if (TryMatchJoined(segments, i, ResortTags, out string resort, out int usedResort))
            {
                facts.ResortTag = resort;
                i += usedResort;
                continue;
            }

            if (TryMatchJoined(segments, i, SpecialContextKeys, out string special, out int used))
            {
                // 含 _Day/_Night 的组合不算特殊键（如 Rainy_Day）。
                bool dayNightSuffix = i + used < segments.Length
                    && (segments[i + used] is "Day" or "Night");
                if (!dayNightSuffix || special.Contains('_'))
                {
                    // "Rainy" 同时是 RandomAction 名；作为独立段落时按 RandomAction 处理。
                    if (!Enum.TryParse(special, ignoreCase: true, out RandomAction _) || special.Contains('_'))
                    {
                        facts.SpecialContextKey = special;
                        i += used;
                        continue;
                    }
                }
            }

            string segment = segments[i];
            if (segment.Length == 0)
            {
                i++;
                continue;
            }

            if (i == 0 && segment == "M")
            {
                facts.IsMarriageKey = true;
                i++;
                continue;
            }

            if (segment == "B" && (i == 0 || (i == 1 && facts.IsMarriageKey)))
            {
                facts.IsBirthdayKey = true;
                i++;
                continue;
            }

            if (Seasons.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                facts.Season = segment.ToLowerInvariant();
                i++;
                continue;
            }

            if (segment == "Accept" && i + 1 < segments.Length)
            {
                facts.IsGiftAccept = true;
                facts.GiftItem = segments[i + 1];
                i += 2;
                continue;
            }

            if (segment.Equals("inlaw", StringComparison.OrdinalIgnoreCase) && i + 1 < segments.Length)
            {
                facts.InlawName = segments[i + 1];
                i += 2;
                continue;
            }

            if (TryParseLocation(segment, facts))
            {
                i++;
                continue;
            }

            if (TryParseWeekday(segment, facts))
            {
                i++;
                continue;
            }

            if (!char.IsDigit(segment[0])
                && Enum.TryParse(segment, ignoreCase: true, out SpouseAction spouseAction)
                && spouseAction != SpouseAction.None)
            {
                facts.SpouseAction = spouseAction;
                i++;
                continue;
            }

            if (!char.IsDigit(segment[0])
                && Enum.TryParse(segment, ignoreCase: true, out RandomAction randomAction)
                && randomAction != RandomAction.None)
            {
                facts.RandomAction = randomAction;
                i++;
                // Rainy/Indoor 后可跟时间段。
                if ((randomAction is RandomAction.Rainy or RandomAction.Indoor)
                    && i < segments.Length
                    && IsTimeSlot(segments[i]))
                {
                    facts.TimeSlot = segments[i];
                    i++;
                }

                continue;
            }

            if (int.TryParse(segment, out int number))
            {
                // 第一个数字 → 季节日；后续数字 → 年份。
                if (!sawNumber && number >= 1 && number <= 28)
                {
                    facts.DayOfMonth = number;
                }
                else
                {
                    facts.Year = number;
                }

                sawNumber = true;
                i++;
                continue;
            }

            i++;
        }

        return facts;
    }

    private static bool TryMatchJoined(string[] segments, int start, string[] candidates, out string matched, out int usedSegments)
    {
        foreach (string candidate in candidates.OrderByDescending(key => key.Length))
        {
            string[] parts = candidate.Split('_');
            if (start + parts.Length > segments.Length)
            {
                continue;
            }

            bool match = true;
            for (int j = 0; j < parts.Length; j++)
            {
                if (!string.Equals(segments[start + j], parts[j], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                matched = candidate;
                usedSegments = parts.Length;
                return true;
            }
        }

        matched = string.Empty;
        usedSegments = 0;
        return false;
    }

    private static bool TryParseLocation(string segment, DialogueKeyFacts facts)
    {
        foreach (string prefix in LocationPrefixes)
        {
            if (!segment.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string suffix = segment[prefix.Length..];
            if (suffix.Length == 0)
            {
                facts.LocationPrefix = prefix;
                return true;
            }

            if (int.TryParse(suffix, out int hearts))
            {
                facts.LocationPrefix = prefix;
                facts.Hearts = hearts;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseWeekday(string segment, DialogueKeyFacts facts)
    {
        for (int day = 0; day < Weekdays.Length; day++)
        {
            if (!segment.StartsWith(Weekdays[day], StringComparison.Ordinal))
            {
                continue;
            }

            string suffix = segment[Weekdays[day].Length..];
            if (suffix.Length == 0)
            {
                facts.Weekday = day;
                return true;
            }

            if (int.TryParse(suffix, out int hearts))
            {
                facts.Weekday = day;
                facts.Hearts = hearts;
                return true;
            }
        }

        return false;
    }

    private static bool IsTimeSlot(string segment)
    {
        return segment is "Day" or "Night" || (int.TryParse(segment, out int time) && time > 28);
    }
}
