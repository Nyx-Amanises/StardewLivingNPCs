using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

internal static class StreamingDialoguePreview
{
    private static readonly Regex EmotionTokenPattern = new(
        @"\$(?<token>[A-Za-z0-9_]+)",
        RegexOptions.Compiled);
    private static readonly Regex PageBoundaryPattern = new(
        @"#\$(?:b|e)#|\f",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MetadataPattern = new(@"!+LIVINGNPCS_META\s*\{.*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BareMetadataTailPattern = new(
        @"(?im)^\s*(rapportDelta|endConversation|ambientFollowUp|emotionImpact|actions|behaviorInfluences|helpRequests|helpRequestUpdates|conflicts|memories)\s*[:=].*$",
        RegexOptions.Compiled);
    private static readonly Regex DialogueCommandPattern = new(@"#\$(?:q|r|c)\b.*", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex MarkdownHeadingPattern = new(
        @"(?m)(?:^|[ \t])##(?!\$[be]#)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TrailingItemCommandPattern = new(
        @"(?:\[\([A-Za-z]+\)[^\]\r\n]+\]|\[\d+\])\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

    /// <summary>
    /// A visible dialogue segment and the portrait frame selected for that segment. The frame is
    /// derived only from text which has already passed the response parser; malformed markers map
    /// to neutral so a streaming UI can never expose an arbitrary numeric frame.
    /// </summary>
    internal sealed record DisplaySegment(string Text, int PortraitFrameIndex);

    public static string ExtractVisibleText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var lines = ConversationTextPostProcessor.RemoveInvisibleCharacters(rawText)
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
        return string.Join(
            "\f",
            PrepareDisplaySegments(text).Select(segment => segment.Text));
    }

    /// <summary>
    /// Prepare final, parser-validated dialogue for the streaming window while retaining the
    /// portrait frame for each logical page. Raw token previews should use this only after the
    /// response parser has accepted the complete line; incomplete streams intentionally have no
    /// expression information and therefore use neutral frames.
    /// </summary>
    internal static IReadOnlyList<DisplaySegment> PrepareDisplaySegments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<DisplaySegment>();
        }

        string display = StripHiddenAndResponseTail(ConversationTextPostProcessor.RemoveInvisibleCharacters(text));
        display = TrailingItemCommandPattern.Replace(display, string.Empty);
        display = display.Trim().TrimStart('-', ' ', '"', '“').TrimEnd('"', '”');
        display = display.Replace("#b#", "#$b#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("#e#", "#$e#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("##$b#", "#$b#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("##$e#", "#$e#", StringComparison.OrdinalIgnoreCase);
        display = display.Replace("@", Game1.player?.Name ?? string.Empty, StringComparison.Ordinal);

        var segments = new List<DisplaySegment>();
        foreach (string rawSegment in PageBoundaryPattern.Split(display))
        {
            if (string.IsNullOrWhiteSpace(rawSegment))
            {
                continue;
            }

            MatchCollection emotionMatches = EmotionTokenPattern.Matches(rawSegment);
            int frameIndex = emotionMatches.Count == 0
                ? 0
                : ResolvePortraitFrameIndex(emotionMatches[^1].Groups["token"].Value);

            string visible = EmotionTokenPattern.Replace(rawSegment, string.Empty);
            visible = visible.Replace("$", string.Empty, StringComparison.Ordinal);
            visible = Regex.Replace(visible, @"(?<!\f)#(?!\$)", " ");
            visible = Regex.Replace(visible, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(visible))
            {
                segments.Add(new DisplaySegment(visible, frameIndex));
            }
        }

        return segments;
    }

    /// <summary>Convert a validated marker token to a Stardew frame index; invalid values are neutral.</summary>
    internal static int ResolvePortraitFrameIndex(string? marker)
    {
        string? normalized = PortraitMarkerRules.NormalizeGameMarker(marker);
        return normalized != null
            && PortraitMarkerRules.TryGetFrameIndex(normalized, out int frameIndex)
            ? frameIndex
            : 0;
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

        int percentIndex = FindGluedResponseTailIndex(cleaned);
        if (percentIndex > 0)
        {
            cleaned = cleaned[..percentIndex];
        }

        Match markdownHeading = MarkdownHeadingPattern.Match(cleaned);
        if (markdownHeading.Success)
        {
            cleaned = cleaned[..markdownHeading.Index];
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

    /// <summary>
    /// Locates a "%option" tail glued onto visible text. A percent sign that reads as prose —
    /// preceded by a digit ("20%"), followed by a digit ("%20"), followed by whitespace, or at the
    /// very end of the text — never truncates. This mirrors the response parser's
    /// MidLineOptionPattern, which likewise refuses to treat "%" + digit as a response option.
    /// Returns -1 when nothing should be cut.
    /// </summary>
    internal static int FindGluedResponseTailIndex(string text)
    {
        for (int index = text.IndexOf('%'); index > 0; index = text.IndexOf('%', index + 1))
        {
            // "20%" is a percentage, not a hidden tail; keep scanning after this marker.
            if (char.IsDigit(text[index - 1]))
            {
                continue;
            }

            int runEnd = index;
            while (runEnd < text.Length && text[runEnd] == '%')
            {
                runEnd++;
            }

            // A trailing percent run has no option text behind it — nothing to cut.
            if (runEnd >= text.Length)
            {
                return -1;
            }

            char next = text[runEnd];
            if (char.IsDigit(next) || char.IsWhiteSpace(next))
            {
                // "%20" / "% " read as prose; resume the scan after the whole run.
                index = runEnd - 1;
                continue;
            }

            return index;
        }

        return -1;
    }
}
