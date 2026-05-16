using System;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace ValleyTalk;

internal static class StreamingDialoguePreview
{
    private static readonly Regex EmotionTokenPattern = new(@"\$[A-Za-z0-9]+", RegexOptions.Compiled);

    public static string ExtractVisibleText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var lines = rawText
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();

        int dialogueIndex = lines.FindIndex(line => line.StartsWith("-", StringComparison.Ordinal));
        if (dialogueIndex < 0)
        {
            return string.Empty;
        }

        var visibleLines = lines
            .Skip(dialogueIndex)
            .TakeWhile(line =>
                !line.StartsWith("%", StringComparison.Ordinal)
                && !line.StartsWith("!LIVINGNPCS_META", StringComparison.Ordinal))
            .ToList();

        return PrepareDisplayText(string.Join(" ", visibleLines));
    }

    public static string PrepareDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string display = text.Trim().TrimStart('-', ' ', '"').TrimEnd('"');
        display = display.Replace("#b#", "#$b#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("#e#", "#$e#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("#$b#", "\f", StringComparison.Ordinal);
        display = display.Replace("#$e#", "\f", StringComparison.Ordinal);
        display = display.Replace("@", Game1.player?.Name ?? string.Empty, StringComparison.Ordinal);
        display = EmotionTokenPattern.Replace(display, string.Empty);

        var segments = display
            .Split('\f')
            .Select(segment => Regex.Replace(segment, @"\s+", " ").Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        return string.Join("\f", segments);
    }
}
