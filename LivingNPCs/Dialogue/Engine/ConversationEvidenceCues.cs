using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Retrieval hints for original conversation quotes. These cues neither resolve a reference nor
/// establish that a promise was accepted, changed, cancelled or fulfilled.
/// </summary>
internal static class ConversationEvidenceCues
{
    private static readonly string[] RevisionPhrases =
    {
        "取消", "作废", "算了", "不去了", "不用了", "不再", "改主意", "改成", "改到",
        "改期", "说错", "更正", "纠正", "才对", "记错", "先别", "暂时别", "挪后", "挪到", "推迟", "提前",
        "换成", "换到", "换个", "还是原来", "按原来", "后天吧", "明天再", "改天再",
        "cancel", "cancelled", "canceled", "forget it", "never mind", "no longer", "not anymore",
        "instead", "actually", "changed my mind", "change my mind", "correction", "correct that",
        "was wrong", "got that wrong", "i meant", "make that", "scratch that", "reschedule", "rescheduled", "postpone", "postponed",
        "push it back", "push that back", "move it", "move that", "one day later", "another day",
        "original plan", "original arrangement", "not today", "not now", "after all"
    };

    private static readonly string[] CommitmentPhrases =
    {
        "答应", "承诺", "说好了", "约好了", "说定", "约定", "我会帮", "我会给", "我会把",
        "我帮你", "我们明天", "promise", "promised", "agreed", "agreement", "i'll bring",
        "i will bring", "i'll help", "i will help", "we will meet", "we'll meet", "i'll meet", "i owe you"
    };

    private static readonly string[] AgreementRevisionPhrases =
    {
        "取消", "作废", "算了", "不去了", "不用了", "不需要了", "改期", "挪后", "挪到",
        "换到", "改到", "推迟", "提前", "后天吧", "明天再", "改天再", "先别", "暂时别",
        "还是原来", "按原来", "别带", "不要带",
        "cancel", "cancelled", "canceled", "reschedule", "rescheduled", "postpone", "postponed",
        "push it back", "push that back", "move it", "move that", "forget it", "never mind",
        "no longer needed", "not needed anymore", "don't bring", "do not bring", "let's not",
        "one day later", "another day", "original plan", "original arrangement", "not today", "not now"
    };

    private static readonly string[] AgreementReferencePhrases =
    {
        "约定", "说好", "约好", "答应", "承诺", "约会", "安排", "见面",
        "promise", "promises", "agreement", "agreements", "agreed", "plan", "plans",
        "arrangement", "arrangements", "appointment", "appointments", "meeting"
    };

    private static readonly Regex ScheduleDateWords = new(
        @"\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|today|tomorrow|tonight)\b"
        + @"|(?:星期|礼拜|周)[一二三四五六日天]|(?:大后天|后天|明天|明早|明晚|今天|今晚)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // A changed date is the new value, not the identity of an older promise which happened to
    // already use that date. This is only for linking revision evidence; quotations keep dates.
    private static readonly Regex ReferenceTimeWords = new(
        @"\b(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday|today|tomorrow|tonight|"
        + @"yesterday|day|days|week|weeks|later|earlier|morning|afternoon|evening|noon|midnight|am|pm|\d+)\b"
        + @"|(?:星期|礼拜|周)[一二三四五六日天]|(?:大后天|后天|明天|明早|明晚|今天|今晚|早上|上午|中午|下午|晚上)"
        + @"|[零一二两三四五六七八九十百0-9]+(?:天|日|号|点|分|时)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] AnaphoricPhrases =
    {
        "那件", "这件", "那个", "这个", "那次", "这次", "那样", "这样", "那里", "这里",
        "它", "那就", "原来", "刚才", "之前那个", "确定吗", "哪一", "哪层", "哪天",
        "挪后", "挪到", "改到", "改成", "改期", "换成", "换到", "推迟", "提前", "后天吧", "明天再", "改天再", "还是算了", "才对", "记错",
        "it", "that", "those", "them", "there", "the former", "the latter", "which",
        "are you sure", "original plan", "original arrangement", "one day later", "another day",
        "actually", "instead", "i meant", "make that", "scratch that", "reschedule", "postpone"
    };

    // Shared action words are poor topic anchors: two different promises may both say "meet"
    // or "cancel". Strip only these search terms, never participant names, places, objects or dates.
    // The returned text is used only to build a query; callers must render the original quote.
    private static readonly Regex GenericEnglishWords = new(
        @"\b(?:promise[ds]?|agree[ds]?|agreement|meet(?:ing)?|plan(?:s|ned)?|arrangement|"
        + @"cancel(?:led|ed)?|reschedul(?:e|ed)|postpon(?:e|ed)|actually|instead|"
        + @"correction|correct|wrong|bring|help|understood|okay|sure|said|say|told|tell)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] GenericChinesePhrases =
    {
        "约好了", "说好了", "改主意", "说定", "约定", "答应", "承诺", "碰头", "见面",
        "取消", "作废", "改期", "改成", "改到", "更正", "纠正", "说错", "明白", "好的"
    };

    internal static bool HasRevisionCue(string? text) => ContainsAny(text, RevisionPhrases) || HasAgreementRevisionCue(text);

    /// <summary>Cancellation/rescheduling cues only; a general factual correction is not a changed agreement.</summary>
    internal static bool HasAgreementRevisionCue(string? text) => ContainsAny(text, AgreementRevisionPhrases)
        || (LocalTextSearch.ContainsPhrase(text, "instead") && ScheduleDateWords.IsMatch(text ?? string.Empty));

    /// <summary>A request can mention an agreement without supplying a locally searchable subject.</summary>
    internal static bool HasAgreementReferenceCue(string? text) => ContainsAny(text, AgreementReferencePhrases)
        || HasAgreementRevisionCue(text);

    internal static bool HasCommitmentCue(string? text) => ContainsAny(text, CommitmentPhrases);

    internal static bool HasAnaphoricReference(string? text) => ContainsAny(text, AnaphoricPhrases);

    internal static string GetTopicText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string topic = GenericEnglishWords.Replace(text, " ");
        foreach (string phrase in GenericChinesePhrases)
        {
            topic = topic.Replace(phrase, " ", StringComparison.Ordinal);
        }
        return topic;
    }

    internal static string GetRevisionTopicText(string? text) => ReferenceTimeWords.Replace(GetTopicText(text), " ");

    private static bool ContainsAny(string? text, string[] phrases) =>
        !string.IsNullOrWhiteSpace(text) && phrases.Any(phrase => LocalTextSearch.ContainsPhrase(text, phrase));
}
