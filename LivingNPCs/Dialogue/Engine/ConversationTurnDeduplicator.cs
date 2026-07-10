using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Removes legacy conversation pollution where one generated NPC line is followed by the
/// same text after Stardew has expanded page breaks, portrait commands, and the farmer name.
/// </summary>
internal static class ConversationTurnDeduplicator
{
    private static readonly Regex PageBreakPattern = new(@"#\$(?:b|e)#", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DollarCommandPattern = new(@"\$[A-Za-z0-9_]+", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static List<T> CollapseExpandedNpcPages<T>(
        IEnumerable<T> source,
        Func<T, string> getText,
        Func<T, bool> isPlayerLine)
    {
        var lines = source?.ToList() ?? new List<T>();
        var result = new List<T>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            T current = lines[i];
            result.Add(current);
            if (isPlayerLine(current))
            {
                continue;
            }

            string[] pages = PageBreakPattern
                .Split(getText(current) ?? string.Empty)
                .Select(Normalize)
                .Where(page => page.Length > 0)
                .ToArray();
            if (pages.Length < 2 || i + pages.Length >= lines.Count)
            {
                continue;
            }

            bool expandedCopy = true;
            for (int pageIndex = 0; pageIndex < pages.Length; pageIndex++)
            {
                T candidate = lines[i + 1 + pageIndex];
                if (isPlayerLine(candidate) || !MatchesPage(pages[pageIndex], Normalize(getText(candidate))))
                {
                    expandedCopy = false;
                    break;
                }
            }

            if (expandedCopy)
            {
                i += pages.Length;
            }
        }

        return result;
    }

    private static string Normalize(string text)
    {
        string normalized = (text ?? string.Empty)
            .Replace("skip#", string.Empty, StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        normalized = DollarCommandPattern.Replace(normalized, string.Empty);
        normalized = WhitespacePattern.Replace(normalized, " ");
        return normalized.Trim();
    }

    private static bool MatchesPage(string originalPage, string displayedPage)
    {
        if (!originalPage.Contains('@'))
        {
            return string.Equals(originalPage, displayedPage, StringComparison.Ordinal);
        }

        string pattern = "^" + Regex.Escape(originalPage).Replace("@", ".+?") + "$";
        return Regex.IsMatch(displayedPage, pattern, RegexOptions.CultureInvariant);
    }
}