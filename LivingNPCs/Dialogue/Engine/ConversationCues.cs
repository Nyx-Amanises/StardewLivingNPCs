using System;
using System.Linq;
using System.Text.RegularExpressions;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Shared conversational-cue vocabulary. The semantic routing fallback and the future-schedule
/// heuristic both need to spot "where are you going" style player lines; keeping the overlapping
/// terms here stops the two call sites from drifting apart when one is edited.
/// Language boundary: these cues cover Chinese/English only, by design. For other game languages
/// the routing layer compensates by re-routing every turn (see ContextRoutingDecisionPass), and a
/// missed cue only skips an additive promotion/heuristic — it never blocks a feature.
/// </summary>
public static class ConversationCues
{
    // Asking where the NPC is or is going. Shared by routing (location/companion promotion) and the
    // future-schedule heuristic.
    private static readonly string[] Whereabouts = { "去哪", "去哪里", "where" };

    // Routing-only: explicit travel / "bring me along" intent.
    private static readonly string[] TravelAndCompanion = { "go", "一起去", "带我", "带你", "陪我", "陪你", "come with", "show me" };

    // Schedule-only: asking about plans, what is next, or how busy the NPC is.
    private static readonly string[] ScheduleInquiry = { "接下来", "之后", "等下", "待会", "忙什么", "在做什么", "计划", "日程", "going", "next", "plan", "schedule" };

    // Explicit help/favor language is a per-turn action cue. It must override a cached ordinary
    // conversation plan so the help-request readiness and item fit context survive compression.
    private static readonly string[] ChineseHelpAction =
    {
        "帮忙", "帮个忙", "帮助", "求助", "搭手", "搭把手", "协助", "需要我帮", "要不要我帮", "我能帮", "我可以帮",
        "有什么需要我帮", "有什么我可以帮", "有什么要我帮", "有需要我帮", "需要我做什么", "要我做什么",
        "需要我做点什么", "需要我做些什么", "有什么事需要我做", "有什么事情需要我做", "有什么事我能做", "有什么事情我能做",
        "有什么我能做", "有什么我可以做", "有事要我做", "帮我", "帮你", "任务", "委托"
    };

    private static readonly Regex EnglishHelpAction = new(
        @"\b(?:
            (?:can|could|may|might|would|will|shall)\s+(?:i|you|we)\s+(?:help|assist)\b
            |(?:i|we)\s+(?:can|could|may|might|would|will|shall)\s+(?:help|assist)\b
            |let\s+me\s+(?:help|assist)\b
            |i'll\s+(?:help|assist)\b
            |how\s+can\s+i\s+(?:help|assist)\b
            |(?:can|could|may|might|would)\s+i\s+do\s+(?:anything|something)(?:\s+for\s+you|\s+to\s+help)?\b
            |what\s+(?:can|could|may|might|would|should)\s+(?:i|we)\s+do\s+for\s+you\b
            |(?:is|was)\s+there\s+anything\s+(?:i|we)\s+(?:can|could)\s+(?:do|help)\b
            |(?:is|was)\s+there\s+(?:anything|something)\s+you\s+(?:need|want)\s+(?:me|us)\s+to\s+do\b
            |(?:can|could|would)\s+(?:i\s+)?(?:give|lend)\s+(?:you|me|us)\s+a\s+hand\b
            |(?:do|did|would)\s+you\s+(?:need|want)\s+(?:any\s+|some\s+|my\s+|our\s+)?(?:help|assistance)\b
            |(?:do|did|would)\s+you\s+(?:need|want)\s+(?:me|us)\s+to\s+do\s+(?:anything|something)\b
            |(?:need|want)\s+(?:me|us)\s+to\s+do\s+(?:anything|something)\b
            |(?:i|we|you)\s+(?:need|want|could\s+use)\s+(?:any\s+|some\s+|your\s+|my\s+|our\s+)?(?:help|assistance)\b
            |(?:need|want)\s+(?:any\s+|some\s+|a\s+)?(?:help|hand|assistance)\b
            |(?:help|assist)\s+(?:me|you|us)\b
            |(?:give|lend)\s+(?:me|you|us)\s+a\s+hand\b
            |(?:anything|something)\s+(?:i|we)\s+(?:can|could)\s+(?:do|help)\b
            |(?:do|doing)\s+(?:anything|something)\s+to\s+help\b
            |(?:favor|favour|quest|task)\b
        )",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex EnglishHelpNegation = new(
        @"\b(?:
            (?:(?:do|does|did)\s+not|don't|doesn't|didn't)\s+(?:really\s+)?(?:need|want)\s+(?:any\s+|some\s+|your\s+|my\s+|our\s+)?(?:help|assistance)\b
            |(?:cannot|can't|could\s+not|couldn't|will\s+not|won't|would\s+not|wouldn't|never)\s+(?:really\s+)?(?:help|assist)\b
            |(?:(?:do|does|did)\s+not|don't|doesn't|didn't)\s+(?:help|assist)\b
            |no\s+need\s+(?:for\s+)?(?:help|assistance)\b
            |not\s+(?:asking|looking)\s+for\s+(?:help|assistance)\b
            |(?:have|has)\s+no\s+(?:quest|task|favor|favour)\b
        )",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex ChineseHelpNegation = new(
        @"(?:不需要|不用|无需|不必|不要|别|不能|无法|没法|不想|不愿)(?:再|来|让|请|你|我|他|她|我们|他们|的|任何|什么|一点|一点点|真的|特意|继续|现在|今天|\s){0,6}(?:帮忙|帮|帮助|协助|搭把手|搭手|求助)|没(?:有|什么)?.{0,4}(?:任务|委托|需要.{0,3}帮)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChineseNegativeHelpQuestion = new(
        @"(?:
            (?:你|您|我|我们|他|她|他们|她们)?
            (?:不需要|不用|无需|不必)
            (?:再|你|我|我们|的|任何|什么|一点|一点点|\s){0,6}
            (?:帮忙|帮|帮助|协助|搭把手|搭手)
            (?:了)?
            |
            没(?:有|什么)?(?:任何|什么)?
            (?:
                需要(?:我|我们|你|你们)?(?:帮忙|帮|帮助|协助|搭把手|搭手)(?:的)?
                |
                (?:任务|委托)(?:(?:要)?(?:给|交给|留给)(?:我|我们))?
            )
        )(?:(?:吗|么)[？?]?|[？?])\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex EnglishNegativeHelpQuestion = new(
        @"\b(?:
            (?:don't\s+you|do\s+you\s+not|wouldn't\s+you|couldn't\s+you)
            \s+(?:need|want|like|use)\s+(?:any\s+|some\s+|my\s+|our\s+|a\s+)?(?:help|assistance|hand)
            |
            (?:you|we|i)\s+(?:don't|do\s+not|wouldn't|would\s+not|couldn't|could\s+not)
            \s+(?:need|want|like|use)\s+(?:any\s+|some\s+|my\s+|your\s+|our\s+|a\s+)?(?:help|assistance|hand)
            |
            (?:you|we|i)\s+(?:have|has)\s+no\s+(?:quest|task|favor|favour)(?:\s+for\s+(?:me|us|you))?
        )\b\s*\?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

    private static readonly Regex HelpClauseSeparator = new(
        @"[，,。！？!?；;\r\n]+|\b(?:but|however|though|yet|and|then)\b|但是|不过|可是|但|而且|然后",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Cues that promote Location/LivingNpc context to full in the routing fallback.</summary>
    public static readonly string[] LocationPromotion = Whereabouts.Concat(TravelAndCompanion).ToArray();

    /// <summary>Cues that justify surfacing the NPC's upcoming schedule.</summary>
    public static readonly string[] FutureSchedule = Whereabouts.Concat(ScheduleInquiry).ToArray();

    public static bool ContainsAny(string text, string[] cues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return cues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the current player turn explicitly offers or requests concrete help.</summary>
    public static bool ContainsHelpAction(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Normalize apostrophes before applying word-bounded English patterns. Evaluate joined or
        // contrast-separated clauses independently, so an earlier refusal doesn't cancel a later
        // explicit offer in the same player turn.
        string normalized = text.Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'');
        if (ChineseNegativeHelpQuestion.IsMatch(normalized)
            || EnglishNegativeHelpQuestion.IsMatch(normalized))
        {
            return true;
        }

        return HelpClauseSeparator
            .Split(normalized)
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .Any(clause => !EnglishHelpNegation.IsMatch(clause)
                && !ChineseHelpNegation.IsMatch(clause)
                && (ContainsAny(clause, ChineseHelpAction)
                    || EnglishHelpAction.IsMatch(clause)));
    }
}
