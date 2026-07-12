using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LivingNPCs.Dialogue.Engine;

internal readonly record struct SchedulePurposeResult(
    SchedulePurposeKind Kind,
    bool Confirmed,
    string Source);

/// <summary>
/// 将游戏已经选择的日程地点/行为翻译为“为什么去那里”。地点与行为来自运行时日程；
/// 固定节日名称只用于解释明确的游戏日程标记，不用 Wiki 表格覆盖模组后的真实日程。
/// </summary>
internal static class SchedulePurposeResolver
{
    private static readonly Regex TokenSeparator = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static SchedulePurposeResult Resolve(
        string destination,
        string behavior,
        string message,
        int seasonIndex,
        int dayOfMonth)
    {
        HashSet<string> behaviorTokens = Tokens(behavior);
        HashSet<string> messageTokens = Tokens(message);

        if (seasonIndex == 0
            && dayOfMonth is >= 15 and <= 17
            && destination.Equals("Desert", StringComparison.OrdinalIgnoreCase)
            && (behaviorTokens.Contains("desertfestival") || messageTokens.Contains("desertfestival")))
        {
            return new(SchedulePurposeKind.AttendDesertFestival, true, "schedule-festival-marker");
        }

        if (behaviorTokens.Count == 0)
        {
            return default;
        }

        if (behaviorTokens.Contains("inspect") && behaviorTokens.Contains("keg"))
            return Confirmed(SchedulePurposeKind.InspectKegs);
        if (behaviorTokens.Contains("phone"))
            return Confirmed(SchedulePurposeKind.UsePhone);
        if (ContainsAny(behaviorTokens, "read", "book", "newspaper"))
            return Confirmed(SchedulePurposeKind.Read);
        if (behaviorTokens.Contains("fish"))
            return Confirmed(SchedulePurposeKind.Fish);
        if (ContainsAny(behaviorTokens, "drink", "wine", "beer"))
            return Confirmed(SchedulePurposeKind.Drink);
        if (ContainsAny(behaviorTokens, "exercise", "aerobic", "stretch", "yoga"))
            return Confirmed(SchedulePurposeKind.Exercise);
        if (ContainsAny(behaviorTokens, "music", "guitar", "flute", "piano"))
            return Confirmed(SchedulePurposeKind.PlayMusic);
        if (ContainsAny(behaviorTokens, "sleep", "bed"))
            return Confirmed(SchedulePurposeKind.Sleep);
        if (ContainsAny(behaviorTokens, "meditate", "concentrate"))
            return Confirmed(SchedulePurposeKind.Meditate);
        if (ContainsAny(behaviorTokens, "work", "job", "farm", "stocking", "clipboard", "craft"))
            return Confirmed(SchedulePurposeKind.Work);

        return default;
    }

    private static SchedulePurposeResult Confirmed(SchedulePurposeKind kind)
        => new(kind, true, "schedule-behavior");

    private static HashSet<string> Tokens(string value)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in TokenSeparator.Split(value.ToLowerInvariant()))
        {
            string normalized = Regex.Replace(token, @"\d+$", string.Empty);
            if (normalized.Length > 0)
            {
                tokens.Add(normalized);
            }
        }

        return tokens;
    }

    private static bool ContainsAny(IReadOnlySet<string> tokens, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (tokens.Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }
}