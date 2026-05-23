using System;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace ValleyTalk;

internal static class StreamingDialoguePreview
{
    private static readonly Regex EmotionTokenPattern = new(@"\$[A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex MetadataPattern = new(@"!+LIVINGNPCS_META\s*\{.*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BareMetadataTailPattern = new(
        @"(?im)^\s*(rapportDelta|endConversation|ambientFollowUp|emotionImpact|actions|behaviorInfluences|commitments|helpRequests|helpRequestUpdates|conflicts|memories)\s*[:=].*$",
        RegexOptions.Compiled);
    private static readonly Regex DialogueCommandPattern = new(@"#\$(?:q|r|c)\b.*", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly string[] InlineResponseMarkers =
    {
        "##回应",
        "#回应",
        "##回复",
        "#回复",
        "##响应",
        "#响应",
        "##Response",
        "#Response",
        "Respond:",
        "回应：",
        "回应:"
    };

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
                && !line.TrimStart('!').StartsWith("LIVINGNPCS_META", StringComparison.Ordinal))
            .ToList();

        return PrepareDisplayText(string.Join(" ", visibleLines));
    }

    public static string PrepareDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string display = StripHiddenAndResponseTail(text);
        display = display.Trim().TrimStart('-', ' ', '"', '“').TrimEnd('"', '”');
        display = display.Replace("#b#", "#$b#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("#e#", "#$e#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("##$b#", "#$b#", StringComparison.Ordinal);
        display = display.Replace("##$e#", "#$e#", StringComparison.Ordinal);
        display = display.Replace("#$b#", "\f", StringComparison.Ordinal);
        display = display.Replace("#$e#", "\f", StringComparison.Ordinal);
        display = display.Replace("@", Game1.player?.Name ?? string.Empty, StringComparison.Ordinal);
        display = EmotionTokenPattern.Replace(display, string.Empty);
        display = Regex.Replace(display, @"(?<!\f)#(?!\$)", " ");

        var segments = display
            .Split('\f')
            .Select(segment => Regex.Replace(segment, @"\s+", " ").Trim())
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        return string.Join("\f", segments);
    }

    public static string StripHiddenAndResponseTail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string cleaned = text.Replace("\r", string.Empty);
        cleaned = MetadataPattern.Replace(cleaned, string.Empty);
        cleaned = BareMetadataTailPattern.Replace(cleaned, string.Empty);
        cleaned = DialogueCommandPattern.Replace(cleaned, string.Empty);

        int percentIndex = cleaned.IndexOf('%');
        if (percentIndex > 0)
        {
            cleaned = cleaned[..percentIndex];
        }

        int doubleHashIndex = cleaned.IndexOf("##", StringComparison.Ordinal);
        if (doubleHashIndex >= 0)
        {
            cleaned = cleaned[..doubleHashIndex];
        }

        foreach (string marker in InlineResponseMarkers)
        {
            int markerIndex = cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0)
            {
                cleaned = cleaned[..markerIndex];
            }
        }

        return cleaned.Trim();
    }
}
