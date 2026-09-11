using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// A local, text-only documentation selector over already captured request data. Capability
/// markers are read only from runtime fields/sections, never inferred from memory prose.
/// Unknown capability shapes retain the complete contract instead of guessing permissions.
/// </summary>
internal static class SceneActionContractSelector
{
    private const int MaxRecentTurns = 6;
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex EnglishMotion = new(@"\b(?:go|going|come|coming|walk|head|leave|travel|visit|take|accompany|escort|join)\b", Options);
    private static readonly Regex EnglishInvitation = new(@"\b(?:together|with me|with you|shall we|let's|let us|would you|could you|can you|will you|want to go|want to come|take me|join me|accompany me)\b", Options);
    private static readonly Regex ChineseInvitation = new(
        @"(?:一起|陪我|跟我|和我|带我|帶我|我们|我們|咱们|咱們|要不要|想不想|能不能|可不可以|愿不愿意|願不願意)[^。！？!?；;]{0,28}(?:去|来|來|走|出发|出發)|(?:去|来|來|走)[^。！？!?；;]{0,18}(?:一起|陪我|跟我|和我)|走吧|出发吧|出發吧", Options);
    private static readonly Regex PastVisit = new(@"去过|去過|来过|來過|到过|到過|\b(?:have you been|ever been|been to|used to|went to|visited)\b", Options);
    private static readonly Regex OutgoingGiftRequest = new(
        @"(?:送|给|給|分|留)(?:点|點|些|一份)?我|我可以(?:拿|收下)|\b(?:give|bring|offer|send|spare|lend|save)\s+me\b|\b(?:can|could|may)\s+i\s+(?:have|take|get)\b", Options);
    private static readonly Regex MoneyTopic = new(
        @"钱|錢|金币|金幣|报酬|報酬|酬劳|酬勞|付款|\b(?:money|gold|coins?|payment|pay|repay|refund|lend|borrow)\b", Options);
    private static readonly Regex MoneyTransfer = new(
        @"给你|給你|借你|付给|付給|还你|還你|\b(?:give|lend|pay|repay|refund|reward)\b", Options);
    private static readonly Regex FestivalTopic = new(
        @"节日|節日|节庆|節慶|花舞节|花舞節|花舞会|花舞會|复活节|復活節|冰雪节|冰雪節|月光水母|冬日星盛宴|\b(?:festival|flower dance|egg hunt|luau|moonlight jellies|feast of the winter star|spirit's eve)\b", Options);
    private static readonly Regex ItemRequestCue = new(
        @"(?:能不能|能否|可以|能|请|請|麻烦|麻煩)(?:你)?(?:帮我|幫我)?(?:带|帶|找|捎|弄)|(?:帮我|幫我)(?:带|帶|找|捎|弄)|\b(?:can|could|would)\s+you\s+(?:please\s+)?(?:bring|find|get|pick up)\b|\bplease\s+(?:bring|find|get|pick up)\b", Options);
    private static readonly Regex NonItemRequest = new(
        @"笑容|好心情|找时间|找時間|带上你自己|帶上你自己|\b(?:a smile|good mood|find time|find the time|bring yourself|get together)\b", Options);
    private static readonly Regex RequestNegationOrCompletion = new(
        @"不用|不必|不需要|不要|别帮|別幫|已经|已經|谢谢|謝謝|\b(?:don't|do not|no need|already|thanks|thank you)\b", Options);
    private static readonly Regex DeferredGift = new(
        @"明天|明日|下次|改天|以后|以後|晚点|晚點|稍后|稍後|等会|等會|邮寄|郵寄|寄给|寄給|给你寄|給你寄|寄到|\b(?:mail|tomorrow|later|next time|another day)\b", Options);
    private static readonly Regex Continuation = new(
        @"^(?:(?:好|好的|好呀|好啊|好吧|行|行啊|嗯|嗯嗯|可以|当然|當然|愿意|願意|没问题|沒問題|太好了|谢谢|谢谢你|謝謝|謝謝你|交给我|交給我|包在我身上|一个不少|一個不少|都齐了|都齊了|就是这些|就是這些|就是这个|就是這個|带来了|帶來了|拿到了|做不到|算了吧|不接了|不了|不用了|不行|不去了|走吧|出发吧|出發吧|等一下|等会儿|等會兒|等会再去|等會再去|明天吧|那就明天|yes|no|ok|okay|sure|agreed|deal|done|great|thanks|thank you|sounds good|of course|absolutely|go ahead|let's go|here it is|here they are|i have it|got it|that's everything|that is everything|last one|not now|not today|maybe later|tomorrow|wait a moment|in a moment|what about the other one|the other one too)(?:[\s，,。.!！?？;；:：…~～-]+|$))+$", Options);
    private static readonly Regex ActiveHelpStatus = new(@"\bstatus\s+(?:Offered|Pending)\b", Options);
    private static readonly Regex AnyHelpStatus = new(@"\bstatus\s+(?:Offered|Pending|Fulfilled|Declined|Expired|Cancelled|Canceled)\b", Options);

    public static SceneActionContractPlan Select(
        string? playerText,
        string? behaviorContext,
        IReadOnlyList<ConversationElement>? recentConversation,
        GenerationTrigger trigger,
        bool isFestival = false,
        bool isPhysicalItemHandIn = false,
        string? locale = null)
    {
        if (trigger is not (GenerationTrigger.Conversation or GenerationTrigger.Gift))
        {
            return SceneActionContractPlan.Full("unsupported-trigger");
        }

        string player = Normalize(RsvPromptSanitizer.SafeInline(playerText));
        if (RsvAiPolicy.IsWithheldPlayerMessage(playerText ?? string.Empty)
            || (!string.IsNullOrWhiteSpace(playerText) && player.Length == 0))
        {
            return SceneActionContractPlan.Full("withheld-player-input");
        }

        if (!IsSupportedLocale(locale) || HasUnsupportedLetters(player))
        {
            return SceneActionContractPlan.Full("unsupported-language");
        }

        string context = RsvPromptSanitizer.SafeMultiline(behaviorContext);
        if (!TryReadCapabilities(context, out Capabilities capabilities, out string reason))
        {
            return SceneActionContractPlan.Full(reason);
        }

        Families current = ReadTextFamilies(player, isPlayer: true);
        Families recent = Families.None;
        if (player.Length > 0 && player.Length <= 160 && Continuation.IsMatch(player))
        {
            recent = ReadRecentFamilies(player, recentConversation, out bool unsupportedHistory);
            if (unsupportedHistory)
            {
                return SceneActionContractPlan.Full("unsupported-recent-language");
            }
        }

        Families mentioned = current | recent;
        return new SceneActionContractPlan
        {
            IncludeTravel = capabilities.ActiveOuting || mentioned.HasFlag(Families.Travel),
            IncludeGifts = (capabilities.GiftOpportunity && !capabilities.DeferredGiftMail)
                || mentioned.HasFlag(Families.Gifts),
            IncludeNewHelp = capabilities.HelpAllowed && !capabilities.NoRequestableItems,
            IncludeHelpUpdates = capabilities.ActiveHelp || capabilities.HelpHandIn || isPhysicalItemHandIn
                || mentioned.HasFlag(Families.Help),
            IncludeMoney = mentioned.HasFlag(Families.Money),
            IncludeFestival = isFestival || mentioned.HasFlag(Families.Festival),
            Reason = "captured-capabilities-and-current-conversation"
        };
    }

    private static bool TryReadCapabilities(string context, out Capabilities result, out string reason)
    {
        result = new Capabilities();
        reason = "missing-behavior-context";
        if (string.IsNullOrWhiteSpace(context))
        {
            return false;
        }

        bool hasHeader = false;
        bool hasCurrentState = false;
        bool giftBlocked = false;
        bool helpBlocked = false;
        bool helpOpportunity = false;
        bool giftAuthorization = false;
        bool giftItemList = false;
        bool unknownReadiness = false;
        bool unknownHelpState = false;
        foreach (string rawLine in context.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("## LivingNPCs Context: ", StringComparison.OrdinalIgnoreCase))
            {
                hasHeader = line.Length > "## LivingNPCs Context: ".Length;
            }
            else if (line.Equals("Current state:", StringComparison.OrdinalIgnoreCase))
            {
                hasCurrentState = true;
            }
            else if (line.Equals("## LivingNPCs Gift Opportunity", StringComparison.OrdinalIgnoreCase))
            {
                result.GiftOpportunity = true;
            }
            else if (line.Equals("## LivingNPCs Help Request Opportunity", StringComparison.OrdinalIgnoreCase))
            {
                helpOpportunity = true;
            }
            else if (line.StartsWith("## LivingNPCs Gift Restriction: no NPC gift is authorized for this reply", StringComparison.OrdinalIgnoreCase))
            {
                giftBlocked = true;
            }
            else if (line.Equals("## LivingNPCs Birthday Gift Mail", StringComparison.OrdinalIgnoreCase)
                || line.Equals("## LivingNPCs Reciprocal Gift Mail", StringComparison.OrdinalIgnoreCase))
            {
                result.DeferredGiftMail = true;
            }
            else if (line.Equals("## Active Companion Outing", StringComparison.OrdinalIgnoreCase))
            {
                result.ActiveOuting = true;
            }
            else if (line.Equals(PromptFragments.HelpRequestHandIn.Header, StringComparison.OrdinalIgnoreCase)
                || line.Equals(PromptFragments.HelpRequestDelivery.Header, StringComparison.OrdinalIgnoreCase))
            {
                result.HelpHandIn = true;
            }

            string field = line.TrimStart('-', ' ', '\t');
            giftAuthorization |= field.StartsWith("This authorization applies to this one reply only.", StringComparison.OrdinalIgnoreCase);
            giftItemList |= field.StartsWith("Shared small gift IDs: ", StringComparison.OrdinalIgnoreCase)
                && field.Length > "Shared small gift IDs: ".Length;
            result.NoRequestableItems |= field.StartsWith("Help-request fit: no currently reasonable item request", StringComparison.OrdinalIgnoreCase);
            const string readinessPrefix = "Help-request readiness:";
            if (field.StartsWith(readinessPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string value = field[readinessPrefix.Length..].TrimStart();
                if (value.StartsWith("may naturally ask for one modest favor now", StringComparison.OrdinalIgnoreCase))
                {
                    result.HelpAllowed = true;
                }
                else if (value.StartsWith("should not open a new help request now", StringComparison.OrdinalIgnoreCase))
                {
                    helpBlocked = true;
                }
                else
                {
                    unknownReadiness = true;
                }
            }

            if (field.StartsWith("Active help request:", StringComparison.OrdinalIgnoreCase)
                || field.StartsWith("Help requests involving the farmer:", StringComparison.OrdinalIgnoreCase)
                || field.StartsWith("Help requests:", StringComparison.OrdinalIgnoreCase))
            {
                string value = field[(field.IndexOf(':') + 1)..].Trim();
                bool empty = value.StartsWith("none", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("no ", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("<none>", StringComparison.OrdinalIgnoreCase);
                result.ActiveHelp |= ActiveHelpStatus.IsMatch(value);
                unknownHelpState |= !empty && !AnyHelpStatus.IsMatch(value);
            }
        }

        if (!hasHeader || !hasCurrentState)
        {
            reason = "unrecognized-behavior-context";
            return false;
        }

        if (unknownReadiness || unknownHelpState
            || (result.GiftOpportunity && giftBlocked) || (result.HelpAllowed && helpBlocked)
            || (helpOpportunity && helpBlocked))
        {
            reason = "ambiguous-capability-context";
            return false;
        }

        if ((!result.GiftOpportunity && !giftBlocked && !result.DeferredGiftMail)
            || (!result.HelpAllowed && !helpBlocked)
            || (result.GiftOpportunity && (!giftAuthorization || !giftItemList)))
        {
            reason = "missing-capability-context";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static Families ReadRecentFamilies(
        string player,
        IReadOnlyList<ConversationElement>? history,
        out bool unsupportedLanguage)
    {
        unsupportedLanguage = false;
        int inspected = 0;
        Families recent = Families.None;
        for (int index = (history?.Count ?? 0) - 1; index >= 0 && inspected < MaxRecentTurns; index--)
        {
            ConversationElement turn = history![index];
            string text = Normalize(RsvPromptSanitizer.SafeInline(turn.Text));
            if (text.Length == 0 || RsvAiPolicy.IsWithheldPlayerMessage(turn.Text))
            {
                break;
            }

            // Some callers include the just-captured player turn; it is not an older topic.
            if (inspected == 0 && turn.IsPlayerLine && text.Equals(player, StringComparison.Ordinal))
            {
                inspected++;
                continue;
            }

            inspected++;
            if (HasUnsupportedLetters(text))
            {
                unsupportedLanguage = true;
                return Families.None;
            }

            Families families = ReadTextFamilies(text, turn.IsPlayerLine);
            recent |= families;
            if (turn.IsPlayerLine && families != Families.None)
            {
                // Keep all effects in the NPC's answer to this invitation/request. A gift
                // offered while accepting an outing must not hide the earlier travel topic.
                return recent;
            }

            if (turn.IsPlayerLine && (text.Length > 160 || !Continuation.IsMatch(text)))
            {
                // A new player topic ends the chain. NPC replies may contain preparation or
                // negotiation prose while still responding to the same player invitation.
                break;
            }
        }

        return recent;
    }

    private static Families ReadTextFamilies(string text, bool isPlayer)
    {
        Families result = !isPlayer && LooksLikeImmediateNpcGift(text) ? Families.Gifts : Families.None;
        foreach (string clause in text.Split(
                     new[] { '.', ',', ';', '!', '?', '。', '，', '；', '！', '？', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!PastVisit.IsMatch(clause)
                && (ChineseInvitation.IsMatch(clause)
                    || (EnglishMotion.IsMatch(clause) && EnglishInvitation.IsMatch(clause))))
            {
                result |= Families.Travel;
            }

            if (isPlayer && OutgoingGiftRequest.IsMatch(clause))
            {
                result |= Families.Gifts;
            }

            if (MoneyTopic.IsMatch(clause) && (isPlayer || MoneyTransfer.IsMatch(clause)))
            {
                result |= Families.Money;
            }

            if (FestivalTopic.IsMatch(clause))
            {
                result |= Families.Festival;
            }

            if (!isPlayer && ItemRequestCue.IsMatch(clause)
                && !NonItemRequest.IsMatch(clause) && !RequestNegationOrCompletion.IsMatch(clause))
            {
                result |= Families.Help;
            }
        }

        return result;
    }

    private static bool LooksLikeImmediateNpcGift(string text)
    {
        // Check deferred wording over the whole reply, not an isolated later clause. An
        // imperative avoids treating "I'll take this" as the NPC giving an item back.
        return !DeferredGift.IsMatch(text)
            && ConversationActionCueRules.VisibleDialogueOffersImmediateGift(text)
            && (text.StartsWith("拿着", StringComparison.Ordinal)
                || text.StartsWith("收下", StringComparison.Ordinal)
                || text.StartsWith("take this", StringComparison.OrdinalIgnoreCase)
                || text.Contains("给你", StringComparison.Ordinal)
                || text.Contains("送你", StringComparison.Ordinal)
                || text.Contains("給你", StringComparison.Ordinal)
                || text.Contains("for you", StringComparison.OrdinalIgnoreCase)
                || text.Contains("i brought you", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return true;
        }

        string normalized = locale.Trim().Replace('_', '-').ToLowerInvariant();
        return normalized is "en" or "zh" or "english" or "chinese" or "中文" or "简体中文" or "繁體中文" or "繁体中文"
            || normalized.StartsWith("en-", StringComparison.Ordinal)
            || normalized.StartsWith("zh-", StringComparison.Ordinal);
    }

    private static bool HasUnsupportedLetters(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune)
                && !(rune.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z'
                    or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
                    or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x3134F))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string? text) => (text ?? string.Empty).Trim().Replace('’', '\'');

    [Flags]
    private enum Families { None = 0, Travel = 1, Gifts = 2, Help = 4, Money = 8, Festival = 16 }

    private sealed class Capabilities
    {
        public bool GiftOpportunity { get; set; }
        public bool DeferredGiftMail { get; set; }
        public bool HelpAllowed { get; set; }
        public bool NoRequestableItems { get; set; }
        public bool ActiveHelp { get; set; }
        public bool HelpHandIn { get; set; }
        public bool ActiveOuting { get; set; }
    }
}
