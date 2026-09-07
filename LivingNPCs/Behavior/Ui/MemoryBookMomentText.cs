using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LivingNPCs.Behavior.Ui;

/// <summary>
/// Localizes the book's generated moment descriptions without rewriting the memories used by
/// the dialogue engine. Old saves and older multiplayer snapshots still contain prompt prose,
/// so recognize those exact templates as well as the current type/key fields.
/// </summary>
internal sealed class MemoryBookMomentText
{
    private const string HelpPrefix = "the farmer helped with a personal request:";
    private const string FollowUpPrefix = "; this could naturally grow into ";
    private const string FollowUpSuffix = " if the next conversation supports it";
    private static readonly string[] ActivityStyles = ["scenic", "browse", "quiet", "social", "festival", "visit"];

    private readonly string npcDisplayName;
    private readonly string locale;
    private readonly MemoryBookData.Translate translate;
    private readonly Func<string, string, string>? localizeItem;

    public MemoryBookMomentText(
        string npcDisplayName,
        string? locale,
        MemoryBookData.Translate translate,
        Func<string, string, string>? localizeItem)
    {
        this.npcDisplayName = npcDisplayName;
        this.locale = locale ?? string.Empty;
        this.translate = translate;
        this.localizeItem = localizeItem;
    }

    public (string Summary, string Location) FormatExperience(
        SharedExperienceFact experience,
        IReadOnlyList<NpcHelpRequestFact> helpRequests)
    {
        string summary = experience.Summary?.Trim() ?? string.Empty;
        if (IsType(experience, "help_request") || summary.StartsWith(HelpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string details = StripHelpPrompt(summary);
            NpcHelpRequestFact? request = helpRequests.FirstOrDefault(candidate =>
                string.Equals(NormalizeSummary(candidate.Summary), NormalizeSummary(details), StringComparison.OrdinalIgnoreCase));
            if (request != null)
            {
                details = this.FormatHelpRequest(request);
            }
            else if (!this.IsCurrentLanguage(details))
            {
                details = string.Empty;
            }

            string text = string.IsNullOrWhiteSpace(details)
                ? this.translate("book.moments.help.completed", new { npc = this.npcDisplayName })
                : this.translate("book.moments.help.completedWithSummary", new { npc = this.npcDisplayName, summary = details });
            return (text, this.translate("book.moments.personalFavor"));
        }

        bool legacyOuting = TryReadOuting(summary, out bool shortVisit, out string savedLocation, out string activityStyle);
        string location = this.FormatLocation(experience, savedLocation);
        if (legacyOuting || (IsType(experience, "companion_outing") && !this.IsCurrentLanguage(summary)))
        {
            string where = string.IsNullOrWhiteSpace(location) ? this.translate("book.moments.unknownLocation") : location;
            string text = string.IsNullOrWhiteSpace(activityStyle)
                ? this.translate("book.moments.outing.generic", new { npc = this.npcDisplayName, where })
                : this.translate(shortVisit ? "book.moments.outing.short" : "book.moments.outing.long", new
                {
                    npc = this.npcDisplayName,
                    where,
                    activity = this.translate($"book.moments.outing.activity.{activityStyle}")
                });
            return (text, location);
        }

        // Do not paraphrase unrecognized, possibly player-authored memories as if we knew
        // what happened. Only the mod's own generated templates are reconstructed above.
        return (summary, location);
    }

    public string FormatHelpRequest(NpcHelpRequestFact request)
    {
        string summary = StripHelpPrompt(request.Summary ?? string.Empty);
        string itemLabel = string.IsNullOrWhiteSpace(request.RequestedItemId) && string.IsNullOrWhiteSpace(request.RequestedItemLabel)
            ? string.Empty
            : this.ResolveItemLabel(request.RequestedItemId, request.RequestedItemLabel);
        summary = ReplaceLabel(summary, request.RequestedItemLabel, itemLabel);
        // A translated item or NPC name doesn't make an English sentence Chinese. Judge the
        // surrounding prose; preserve local prose while updating a known item's display name.
        string prose = ReplaceLabel(ReplaceLabel(summary, itemLabel, string.Empty), this.npcDisplayName, string.Empty);
        if (!string.IsNullOrWhiteSpace(summary) && this.IsCurrentLanguage(prose))
        {
            return summary;
        }

        if (!string.IsNullOrWhiteSpace(request.RequestedItemId) || !string.IsNullOrWhiteSpace(request.RequestedItemLabel))
        {
            return this.translate("book.moments.request.item", new
            {
                item = this.FormatItem(request.RequestedItemId, request.RequestedItemLabel)
            });
        }

        if (string.Equals(request.Type, "question_request", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(request.QuestionTopic))
        {
            return !string.IsNullOrWhiteSpace(request.QuestionTopic) && this.IsCurrentLanguage(request.QuestionTopic)
                ? this.translate("book.moments.request.questionTopic", new { topic = request.QuestionTopic.Trim() })
                : this.translate("book.moments.request.question");
        }

        return this.translate("book.moments.request.generic");
    }

    public string FormatItem(string itemId, string storedLabel)
    {
        string label = this.ResolveItemLabel(itemId, storedLabel);
        return !string.IsNullOrWhiteSpace(label) && this.IsCurrentLanguage(label)
            ? label.Trim()
            : this.translate("book.moments.unknownItem");
    }

    private string ResolveItemLabel(string itemId, string storedLabel)
    {
        return this.localizeItem?.Invoke(itemId ?? string.Empty, storedLabel ?? string.Empty) ?? storedLabel ?? string.Empty;
    }

    private static string ReplaceLabel(string text, string? label, string replacement)
    {
        if (string.IsNullOrWhiteSpace(label) || string.Equals(label, replacement, StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        string name = label.Trim();
        static bool IsAsciiWord(char character) => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_';
        string pattern = (IsAsciiWord(name[0]) ? @"(?<![A-Za-z0-9_])" : string.Empty)
            + Regex.Escape(name)
            + (IsAsciiWord(name[^1]) ? @"(?![A-Za-z0-9_])" : string.Empty);
        return Regex.Replace(text, pattern, _ => replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private string FormatLocation(SharedExperienceFact experience, string savedLocation)
    {
        string keyLocation = experience.Key?.StartsWith("companion_outing:", StringComparison.OrdinalIgnoreCase) == true
            ? experience.Key["companion_outing:".Length..]
            : string.Empty;
        string[] candidates = [experience.LocationName, keyLocation, experience.LocationLabel, savedLocation];
        foreach (string candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string normalized = TravelLocationRules.Normalize(candidate, string.Empty);
            if (TravelLocationRules.IsKnownPublicOutingTarget(normalized))
            {
                return TravelLocationRules.GetLocalizedLabel(normalized, key => this.translate(key));
            }

            // The first book snapshots stored display labels, not always canonical names.
            // Match the old English/Chinese labels too before falling back to custom content.
            string? target = TravelLocationRules.KnownPublicOutingTargets.FirstOrDefault(name =>
                string.Equals(candidate.Trim(), TravelLocationRules.GetLabel(name), StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Trim(), TravelLocationRules.GetChineseLabel(name), StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                return TravelLocationRules.GetLocalizedLabel(target, key => this.translate(key));
            }
        }

        return new[] { experience.LocationLabel, savedLocation, experience.LocationName }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private bool IsCurrentLanguage(string text)
    {
        int chineseCharacters = text.Count(character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff');
        if (this.locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            int latinLetters = text.Count(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
            return chineseCharacters > 0 && latinLetters <= chineseCharacters * 2;
        }

        return !this.locale.StartsWith("en", StringComparison.OrdinalIgnoreCase) || chineseCharacters == 0;
    }

    private static bool IsType(SharedExperienceFact experience, string type)
    {
        return string.Equals(experience.Type, type, StringComparison.OrdinalIgnoreCase)
            || experience.Key?.StartsWith(type + ":", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string StripHelpPrompt(string summary)
    {
        string result = summary.Trim();
        if (result.StartsWith(HelpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            result = result[HelpPrefix.Length..].Trim();
        }

        int followUp = result.IndexOf(FollowUpPrefix, StringComparison.OrdinalIgnoreCase);
        if (followUp >= 0 && result.TrimEnd('.', '。').EndsWith(FollowUpSuffix, StringComparison.OrdinalIgnoreCase))
        {
            result = result[..followUp].Trim();
        }

        return result;
    }

    private static string NormalizeSummary(string? text) => (text ?? string.Empty).Trim().TrimEnd('.', '。', ';', '；').TrimEnd();

    private static bool TryReadOuting(string summary, out bool shortVisit, out string location, out string activityStyle)
    {
        shortVisit = false;
        location = string.Empty;
        activityStyle = string.Empty;
        if (!summary.StartsWith("the farmer and ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string shortMarker = " briefly went together to ";
        const string longMarker = " spent time together at ";
        int marker = summary.IndexOf(shortMarker, StringComparison.OrdinalIgnoreCase);
        shortVisit = marker >= 0;
        if (!shortVisit)
        {
            marker = summary.IndexOf(longMarker, StringComparison.OrdinalIgnoreCase);
        }

        if (marker < 0)
        {
            return false;
        }

        string tail = summary[(marker + (shortVisit ? shortMarker.Length : longMarker.Length))..].Trim().TrimEnd('.', '。');
        foreach (string style in ActivityStyles)
        {
            string suffix = ", " + CompanionOutingRules.GetActivityPromptLabel(style);
            if (tail.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                activityStyle = style;
                location = tail[..^suffix.Length].Trim();
                return true;
            }
        }

        // A different activity phrase may come from a newer writer. Retain the known visit,
        // but don't turn an unrecognized prompt fragment into player-facing prose.
        int comma = tail.LastIndexOf(',');
        location = (comma < 0 ? tail : tail[..comma]).Trim();
        return true;
    }
}
