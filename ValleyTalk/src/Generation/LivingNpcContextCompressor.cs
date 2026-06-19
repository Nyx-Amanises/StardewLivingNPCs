using System;
using System.Collections.Generic;
using System.Linq;

namespace ValleyTalk;

public static class LivingNpcContextCompressor
{
    private const int DefaultMaxBriefLines = 80;
    private const int DefaultFallbackLines = 12;
    private const int DefaultMaxBriefCharacters = 6000;

    public static string BuildBriefContext(
        string fullContext,
        int maxLines = DefaultMaxBriefLines,
        int fallbackLines = DefaultFallbackLines,
        int maxCharacters = DefaultMaxBriefCharacters)
    {
        if (string.IsNullOrWhiteSpace(fullContext))
        {
            return string.Empty;
        }

        var lines = fullContext
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();
        var selected = new List<string>();
        bool inCriticalSection = false;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                inCriticalSection = IsCriticalSectionHeading(line);
            }

            if (inCriticalSection || ShouldKeepBriefContextLine(line))
            {
                selected.Add(line);
            }
        }

        if (selected.Count == 0)
        {
            selected = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(Math.Max(1, fallbackLines))
                .ToList();
        }

        if (maxLines > 0 && selected.Count > maxLines)
        {
            selected = selected.Take(maxLines).ToList();
        }

        string compact = string.Join("\n", selected);
        if (maxCharacters > 0 && compact.Length > maxCharacters)
        {
            compact = compact[..maxCharacters].TrimEnd() + "\n<truncated>";
        }

        return compact;
    }

    public static bool ShouldKeepBriefContextLine(string line)
    {
        return ContainsAny(
            line,
            "## LivingNPCs",
            "## Active Companion Outing",
            "Rules:",
            "Conversation stance",
            "Current state:",
            "Mood:",
            "emotion:",
            "Familiarity",
            "trust",
            "Relationship trust",
            "Recent gift",
            "Gift memory",
            "Gift Opportunity",
            "Shared small gift IDs",
            "personalized small gift IDs",
            "Reciprocal Gift Mail",
            "Birthday Gift Mail",
            "return gift",
            "Help requests",
            "Help-request",
            "Help Request Opportunity",
            "currently reasonable item requests",
            "allowed help request type",
            "request relationship tier",
            "request depth",
            "readiness",
            "fit:",
            "Active help request",
            "Recently fulfilled help request",
            "Unfinished help request",
            "helpRequests",
            "helpRequestUpdates",
            "Conflict",
            "Personal memory",
            "Last interaction",
            "Shared experiences",
            "Phase:",
            "Shared activity",
            "destination",
            "targetLocation",
            "travelConsent",
            "plans to remain",
            "companion_outing",
            "Active Companion",
            "schedule",
            "World knowledge",
            "Scene:",
            "location:",
            "time:",
            "farmer is",
            "Do not announce travel",
            "求助",
            "帮忙",
            "出游",
            "带路",
            "一起去",
            "陪",
            "礼物",
            "回礼",
            "谢礼",
            "冲突",
            "信任",
            "熟悉",
            "地点",
            "日程");
    }

    private static bool IsCriticalSectionHeading(string line)
    {
        return ContainsAny(
            line,
            "## LivingNPCs Gift Opportunity",
            "## LivingNPCs Help Request Opportunity",
            "## Active Companion Outing",
            "## LivingNPCs Help Request Gift Response",
            "## LivingNPCs Reciprocal Gift Mail",
            "## LivingNPCs Birthday Gift Mail");
    }

    private static bool ContainsAny(string text, params string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return fragments.Any(fragment => text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }
}
