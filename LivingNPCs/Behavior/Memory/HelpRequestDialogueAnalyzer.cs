using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LivingNPCs.Behavior;

/// <summary>A requestable item and every visible name that may identify it in dialogue.</summary>
internal sealed class HelpRequestItemAlias
{
    public string ItemId { get; }
    public string Label { get; }
    public IReadOnlyList<string> Aliases { get; }

    public HelpRequestItemAlias(string itemId, string label, IEnumerable<string> aliases)
    {
        this.ItemId = BehaviorValueNormalizer.NormalizeQualifiedObjectItemId(itemId);
        this.Label = label?.Trim() ?? string.Empty;
        this.Aliases = (aliases ?? Array.Empty<string>())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(alias => alias.Length)
            .ToList();
    }
}

/// <summary>A high-confidence item request found in the NPC's own visible words.</summary>
internal sealed record ExplicitHelpRequestItem(
    string ItemId,
    string Label,
    int TextIndex,
    bool IsOptionalAddOn);

/// <summary>
/// Pure-text guard shared by request synthesis and metadata consistency checks. It deliberately
/// recognizes only an item name and a request cue in the same clause; ordinary mentions, thanks,
/// and item names supplied only by the farmer never become quests.
/// </summary>
internal static class HelpRequestDialogueAnalyzer
{
    private static readonly Regex PageMarkerPattern = new(
        @"#\$[be]#",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PortraitMarkerPattern = new(
        @"\$[A-Za-z0-9_]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SequencedRequestPattern = new(
        @"(?:先|再|然后|接着|顺便|另外|还有)\s*(?:也\s*)?(?:帮我\s*)?(?:给我\s*)?(?:找|带|弄|捎|留意)|\b(?:first|then|next|also)\s+(?:please\s+)?(?:bring|find|get|pick\s+up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DirectRequestPattern = new(
        @"^(?:请|麻烦你?)\s*(?:帮我\s*)?(?:给我\s*)?(?:找|带|弄|捎|留意)|^\s*(?:please\s+)?(?:bring|find|get|pick\s+up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StrongOptionalChineseRequestPattern = new(
        @"(?:如果|要是)(?:你)?(?:(?:还|也)\s*(?:能|可以|方便)|(?:能|可以|方便)\s*(?:再|顺便|也))\s*(?:帮我\s*)?(?:给我\s*)?(?:带|找|弄|捎|留意)[^。！？!?；;]{1,80}|(?:顺便)(?:再|也)?\s*(?:帮我\s*)?(?:给我\s*)?(?:带|找|弄|捎|留意)[^。！？!?；;]{1,80}|(?:带|找|弄|捎)[^。！？!?；;]{1,60}(?:会|就|能)?(?:更好|更合适|更完美|最好|就好了)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PoliteConditionalChineseRequestPattern = new(
        @"(?:如果|要是)(?:你)?(?:还|也)?(?:能|可以|方便)[^。！？!?；;]{0,36}(?:带|找|弄|捎|留意)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StrongOptionalEnglishRequestPattern = new(
        @"\bif\s+you\s+(?:(?:also\s+)(?:can|could)|(?:can|could)\s+also)\b[^.!?;]{0,40}\b(?:bring|find|get|pick\s+up)\b[^.!?;]{1,80}|\bwhile\s+you(?:'re|\s+are)\s+at\s+it\b[^.!?;]{0,40}\b(?:bring|find|get|pick\s+up)\b[^.!?;]{1,80}|\b(?:bring|find|get|pick\s+up)\b[^.!?;]{1,60}\b(?:would\s+be|is)\s+(?:even\s+)?(?:better|perfect)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PoliteConditionalEnglishRequestPattern = new(
        @"\b(?:if\s+you\s+(?:can|could)|if\s+possible|if\s+you\s+have\s+time)\b[^.!?;]{0,48}\b(?:bring|find|get|pick\s+up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OptionalOutcomePattern = new(
        @"(?:更好|更合适|更完美|最好|就好了|那就太好了|我会(?:很|非常)?(?:开心|高兴|感激))|\b(?:better|perfect|that\s+would\s+be\s+great|i\s+would\s+appreciate\s+it|i'd\s+appreciate\s+it)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex OptionalComparisonPattern = new(
        @"(?:如果|要是)[^。！？!?；;]{0,48}(?:更好|更合适|更完美|最好|就好了)|(?:不过|但是|只是|其实|可)?[^。！？!?；;]{0,36}(?:会|就|可能会|也许会)?(?:更好|更合适|更完美|最好|就好了)|\b(?:if\b[^.!?;]{0,48}\b(?:better|perfect)|(?:would\s+be|is)\s+(?:even\s+)?(?:better|perfect))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RequiredMultiItemPattern = new(
        @"(?:两样|两个|几样|这些|它们|全部|所有)[^。！？!?；;]{0,16}(?:都|全都)[^。！？!?；;]{0,6}(?:需要|要|必须|不能少)|(?:都|全都)[^。！？!?；;]{0,6}(?:需要|要|必须|不能少)|缺一不可|\b(?:need|require|must\s+have|must\s+bring|must\s+find)\s+(?:them\s+)?(?:both|all)\b|\b(?:both|all)(?:\s+\w+){0,5}\s+(?:are\s+)?(?:needed|required|necessary)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NpcSelfSupplyPattern = new(
        @"(?:如果|要是)?\s*我(?:还|也)?(?:能|可以|会|来|打算)\s*(?:给你\s*)?(?:带|找|弄|捎)|\b(?:i\s+can|i\s+could|i(?:'ll|\s+will)|let\s+me)\s+(?:also\s+)?(?:bring|find|get|pick\s+up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NonItemOptionalPhrasePattern = new(
        @"(?:带我|带我们|带他|带她|带大家)\s*(?:去|到)|带着?(?:笑容|好心情|心意|诚意|勇气|耐心)|带上(?:你自己|好心情)|找(?:点|些|一些)?(?:时间|机会|勇气|办法)|\b(?:find\s+(?:the\s+)?(?:time|courage|a\s+way)|bring\s+(?:yourself|a\s+smile|your\s+smile|a\s+good\s+mood)|get\s+together)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NegatedChineseRequestPattern = new(
        @"(?:不用|不必|无需|不需要|别|不要)\s*(?:(?:你|再|特地|专门|继续|麻烦你?|劳烦你?)\s*){0,4}(?:(?:帮我|给我)\s*)?(?:找|带|弄|捎|留意)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NegatedEnglishRequestPattern = new(
        @"\b(?:please\s+)?(?:(?:do\s+not|don['’]?t|never)|(?:you\s+)?(?:do\s+not|don['’]?t)\s+need\s+to|(?:you\s+)?needn['’]?t|(?:you\s+)?shouldn['’]?t|no\s+need\s+to|i\s+(?:do\s+not|don['’]?t)\s+want\s+you\s+to)\s+(?:help\s+me\s+)?(?:bring|find|get|pick\s+up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CompletedChineseRequestPattern = new(
        @"(?:谢谢|多谢|感谢|辛苦)(?:你)?[^。！？!?；;，,]{0,32}(?:帮我|给我)\s*(?:找|带|弄|捎|留意)|(?:已经|刚刚|刚才|之前|上次|前几天)[^。！？!?；;，,]{0,24}(?:帮我|给我)\s*(?:找|带|弄|捎|留意)|(?:帮我|给我)\s*(?:找|带|弄|捎)[^。！？!?；;，,]{0,24}(?:到了|到的|来的|过的|好了|完了)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CompletedEnglishRequestPattern = new(
        @"\b(?:thank\s+you|thanks|much\s+appreciated)\b[^.!?;,]{0,40}\b(?:for\s+)?(?:helping\s+me\s+)?(?:bring(?:ing)?|find(?:ing)?|get(?:ting)?|pick(?:ing)?\s+up)\b|\b(?:already|just|previously|last\s+time)\b[^.!?;,]{0,24}\b(?:helped\s+me\s+)?(?:brought|found|got|picked\s+up)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<ExplicitHelpRequestItem> FindExplicitItemRequests(
        string npcResponse,
        IReadOnlyList<HelpRequestItemAlias> requestableItems,
        bool hasPriorRequiredRequest = false,
        IReadOnlySet<string>? priorRequiredItemIds = null)
    {
        if (string.IsNullOrWhiteSpace(npcResponse) || requestableItems == null || requestableItems.Count == 0)
        {
            return Array.Empty<ExplicitHelpRequestItem>();
        }

        string visible = PortraitMarkerPattern.Replace(PageMarkerPattern.Replace(npcResponse, "\n"), string.Empty);
        var found = new List<ExplicitHelpRequestItem>();
        var seenItemIds = new HashSet<string>(
            priorRequiredItemIds is null ? Array.Empty<string>() : priorRequiredItemIds,
            StringComparer.OrdinalIgnoreCase);
        bool priorRequiredRequest = hasPriorRequiredRequest || seenItemIds.Count > 0;

        foreach ((string clause, int clauseStart) in EnumerateClauses(visible))
        {
            if (LooksLikeNpcSupplyingTheItem(clause)
                || LooksLikeNegatedItemRequest(clause)
                || LooksLikeCompletedItemRequest(clause))
            {
                continue;
            }

            List<AliasOccurrence> acceptedOccurrences = FindAliasOccurrences(
                clause,
                clauseStart,
                requestableItems);
            if (acceptedOccurrences.Count == 0)
            {
                continue;
            }

            string sentenceRemainder = GetSentenceRemainder(visible, clauseStart);
            bool hasRequestCue = HasExplicitItemRequestCue(clause);
            bool comparisonAddOn = priorRequiredRequest
                && acceptedOccurrences.Any(occurrence => !seenItemIds.Contains(occurrence.Item.ItemId))
                && IsOptionalItemComparison(clause)
                && !HasRequiredMultiItemSignal(sentenceRemainder);
            if (!hasRequestCue && !comparisonAddOn)
            {
                continue;
            }

            bool optional = comparisonAddOn
                || IsOptionalAddOnRequest(clause, priorRequiredRequest, sentenceRemainder);
            bool addedRequiredRequest = false;
            foreach (AliasOccurrence occurrence in acceptedOccurrences.OrderBy(candidate => candidate.TextIndex))
            {
                if (!seenItemIds.Add(occurrence.Item.ItemId))
                {
                    continue;
                }

                found.Add(new ExplicitHelpRequestItem(
                    occurrence.Item.ItemId,
                    occurrence.Item.Label,
                    occurrence.TextIndex,
                    optional));
                addedRequiredRequest |= !optional;
            }

            if (addedRequiredRequest)
            {
                priorRequiredRequest = true;
            }
        }

        return found.OrderBy(match => match.TextIndex).ToList();
    }

    public static bool IsCandidateSequenceConsistent(
        ValleyTalkHelpRequestCandidate candidate,
        IReadOnlyList<ExplicitHelpRequestItem> visibleRequests,
        int maxSteps)
    {
        if (candidate == null)
        {
            return false;
        }

        List<string> encodedIds = GetEncodedItemIds(candidate);
        if (encodedIds.Count == 0 || encodedIds.Count > Math.Max(1, maxSteps))
        {
            return false;
        }

        // A new journal task must be grounded in the NPC's visible words. Hidden metadata alone
        // is never enough, even if its item ID is otherwise whitelisted.
        if (visibleRequests == null
            || visibleRequests.Count == 0
            || visibleRequests.Any(request => request.IsOptionalAddOn))
        {
            return false;
        }

        if (encodedIds.Count != visibleRequests.Count)
        {
            return false;
        }

        for (int i = 0; i < encodedIds.Count; i++)
        {
            if (!string.Equals(encodedIds[i], visibleRequests[i].ItemId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryBuildSynthesisCandidate(
        IReadOnlyList<ExplicitHelpRequestItem> visibleRequests,
        bool farmerAlreadyAgreed,
        int maxSteps,
        out ValleyTalkHelpRequestCandidate candidate)
    {
        candidate = new ValleyTalkHelpRequestCandidate();
        if (visibleRequests == null
            || visibleRequests.Count == 0
            || visibleRequests.Count > Math.Max(1, maxSteps)
            || visibleRequests.Any(request => request.IsOptionalAddOn))
        {
            return false;
        }

        var ordered = visibleRequests.OrderBy(request => request.TextIndex).ToList();
        candidate = new ValleyTalkHelpRequestCandidate
        {
            Type = "item_request",
            Summary = string.Join(" -> ", ordered.Select(request => request.Label)),
            RequestedItemId = ordered[0].ItemId,
            RequestedItemLabel = ordered[0].Label,
            DueInDays = 3,
            RequiresAcceptance = !farmerAlreadyAgreed,
            FollowUpPotential = "none",
            Reason = "synthesized from explicit visible item request",
            Steps = ordered.Select(request => new ValleyTalkHelpRequestStepCandidate
            {
                Type = "item_request",
                Summary = request.Label,
                RequestedItemId = request.ItemId,
                RequestedItemLabel = request.Label
            }).ToList()
        };
        return true;
    }

    internal static bool IsOptionalAddOnRequest(string text)
    {
        return IsOptionalAddOnRequest(text, hasPriorRequiredRequest: false, text);
    }

    internal static bool IsOptionalAddOnRequest(
        string text,
        bool hasPriorRequiredRequest,
        string surroundingSentence)
    {
        if (string.IsNullOrWhiteSpace(text)
            || NpcSelfSupplyPattern.IsMatch(text)
            || NonItemOptionalPhrasePattern.IsMatch(text)
            || HasRequiredMultiItemSignal(surroundingSentence))
        {
            return false;
        }

        if (StrongOptionalChineseRequestPattern.IsMatch(text)
            || StrongOptionalEnglishRequestPattern.IsMatch(text))
        {
            return true;
        }

        // "If you can bring Sugar, I'd appreciate it" is a normal polite single request. It only
        // becomes an unsupported add-on when another concrete request already exists and the
        // surrounding sentence explicitly compares the extra item as better/perfect.
        bool politeConditional = PoliteConditionalChineseRequestPattern.IsMatch(text)
            || PoliteConditionalEnglishRequestPattern.IsMatch(text);
        return hasPriorRequiredRequest
            && politeConditional
            && HasOptionalOutcomeSignal(surroundingSentence);
    }

    private static bool HasExplicitItemRequestCue(string clause)
    {
        return HelpRequestMemoryService.LooksLikeItemFavorRequested(clause)
            || SequencedRequestPattern.IsMatch(clause)
            || DirectRequestPattern.IsMatch(clause)
            || PoliteConditionalChineseRequestPattern.IsMatch(clause)
            || PoliteConditionalEnglishRequestPattern.IsMatch(clause)
            || StrongOptionalChineseRequestPattern.IsMatch(clause)
            || StrongOptionalEnglishRequestPattern.IsMatch(clause);
    }

    internal static bool HasRequiredMultiItemSignal(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && RequiredMultiItemPattern.IsMatch(text);
    }

    internal static bool HasOptionalOutcomeSignal(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && OptionalOutcomePattern.IsMatch(text);
    }

    internal static bool ContainsItemAlias(string text, string alias)
    {
        return !string.IsNullOrWhiteSpace(text)
            && !string.IsNullOrWhiteSpace(alias)
            && IndexOfAlias(text, alias.Trim()) >= 0;
    }

    private static bool IsOptionalItemComparison(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && OptionalComparisonPattern.IsMatch(text);
    }

    private static bool LooksLikeNpcSupplyingTheItem(string clause)
    {
        return NpcSelfSupplyPattern.IsMatch(clause)
            || HelpRequestMemoryService.LooksLikeNpcOfferingItemToFarmer(clause);
    }

    private static bool LooksLikeNegatedItemRequest(string clause)
    {
        // Keep this deliberately syntactic and close to the request verb. It rejects direct
        // cancellation/refusal such as "不用帮我带" or "don't bring", while wording like
        // "不要忘了帮我带" / "don't forget to bring" doesn't match and remains a request.
        return NegatedChineseRequestPattern.IsMatch(clause)
            || NegatedEnglishRequestPattern.IsMatch(clause);
    }

    private static bool LooksLikeCompletedItemRequest(string clause)
    {
        // A completed favor can contain the exact same request words as a new favor in Chinese
        // (for example "谢谢你帮我带糖"). Keep the guard within one punctuation-delimited clause,
        // so "谢谢，你能帮我带糖吗" is split and the actual new request remains detectable.
        return CompletedChineseRequestPattern.IsMatch(clause)
            || CompletedEnglishRequestPattern.IsMatch(clause);
    }

    private static List<AliasOccurrence> FindAliasOccurrences(
        string text,
        int textStart,
        IReadOnlyList<HelpRequestItemAlias> requestableItems)
    {
        var occurrences = new List<AliasOccurrence>();
        foreach (HelpRequestItemAlias item in requestableItems)
        {
            AliasOccurrence? best = null;
            foreach (string alias in item.Aliases)
            {
                int index = IndexOfAlias(text, alias);
                if (index < 0)
                {
                    continue;
                }

                var occurrence = new AliasOccurrence(item, alias, textStart + index);
                if (best == null
                    || occurrence.TextIndex < best.TextIndex
                    || (occurrence.TextIndex == best.TextIndex && occurrence.Alias.Length > best.Alias.Length))
                {
                    best = occurrence;
                }
            }

            if (best != null)
            {
                occurrences.Add(best);
            }
        }

        var accepted = new List<AliasOccurrence>();
        foreach (AliasOccurrence occurrence in occurrences
                     .OrderBy(candidate => candidate.TextIndex)
                     .ThenByDescending(candidate => candidate.Alias.Length))
        {
            bool overlapsLongerMatch = accepted.Any(existing =>
                occurrence.TextIndex < existing.TextIndex + existing.Alias.Length
                && existing.TextIndex < occurrence.TextIndex + occurrence.Alias.Length);
            if (!overlapsLongerMatch)
            {
                accepted.Add(occurrence);
            }
        }

        return accepted;
    }

    private static string GetSentenceRemainder(string text, int clauseStart)
    {
        int end = clauseStart;
        while (end < text.Length && text[end] is not ('。' or '！' or '？' or '.' or '!' or '?' or '；' or ';' or '\n' or '\r'))
        {
            end++;
        }

        if (end < text.Length)
        {
            end++;
        }

        return text[clauseStart..end];
    }

    private static List<string> GetEncodedItemIds(ValleyTalkHelpRequestCandidate candidate)
    {
        IEnumerable<string> rawIds = candidate.Steps != null && candidate.Steps.Count > 0
            ? candidate.Steps
                .Where(step => BehaviorValueNormalizer.NormalizeHelpRequestType(step.Type) == "item_request")
                .Select(step => step.RequestedItemId)
            : new[] { candidate.RequestedItemId };

        return rawIds
            .Select(BehaviorValueNormalizer.NormalizeQualifiedObjectItemId)
            .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
            .ToList();
    }

    private static IEnumerable<(string Clause, int Start)> EnumerateClauses(string text)
    {
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool atEnd = i == text.Length;
            bool boundary = !atEnd && text[i] is '。' or '！' or '？' or '.' or '!' or '?' or '；' or ';' or '，' or ',' or '\n' or '\r';
            if (!atEnd && !boundary)
            {
                continue;
            }

            string clause = text[start..i].Trim();
            if (clause.Length > 0)
            {
                int leadingWhitespace = text[start..i].Length - text[start..i].TrimStart().Length;
                yield return (clause, start + leadingWhitespace);
            }

            start = i + 1;
        }
    }

    private static int IndexOfAlias(string text, string alias)
    {
        int searchStart = 0;
        while (searchStart <= text.Length - alias.Length)
        {
            int index = text.IndexOf(alias, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return -1;
            }

            bool needsLeftBoundary = char.IsLetterOrDigit(alias[0]);
            bool needsRightBoundary = char.IsLetterOrDigit(alias[^1]);
            bool leftOkay = !needsLeftBoundary || index == 0 || !IsAsciiWordCharacter(text[index - 1]);
            int after = index + alias.Length;
            bool rightOkay = !needsRightBoundary || after == text.Length || !IsAsciiWordCharacter(text[after]);
            bool cjkSingleCharacterOkay = !IsSingleCjkAlias(alias)
                || HasSingleCjkItemBoundary(text, index);
            if (leftOkay && rightOkay && cjkSingleCharacterOkay)
            {
                return index;
            }

            searchStart = index + 1;
        }

        return -1;
    }

    private static bool IsSingleCjkAlias(string alias)
    {
        return alias.Length == 1 && IsCjk(alias[0]);
    }

    private static bool HasSingleCjkItemBoundary(string text, int index)
    {
        char? before = index > 0 ? text[index - 1] : null;
        char? after = index + 1 < text.Length ? text[index + 1] : null;
        return IsAllowedSingleCjkLeftContext(before)
            && IsAllowedSingleCjkRightContext(after);
    }

    private static bool IsAllowedSingleCjkLeftContext(char? value)
    {
        if (value == null || !IsCjk(value.Value))
        {
            return true;
        }

        // High-precision contexts that naturally precede a one-character item name in a request:
        // measure words/quantifiers and the final character of common request verbs.
        return "一二两三四五六七八九十几些点包袋罐盒份勺块瓶杯带找要需缺拿捎弄买给送备有过是而若如换用".Contains(value.Value);
    }

    private static bool IsAllowedSingleCjkRightContext(char? value)
    {
        if (value == null || !IsCjk(value.Value))
        {
            return true;
        }

        // Reject compounds such as 糖果/枫糖浆 while allowing normal grammar like 糖来、糖给我、
        // 糖也行. The analyzer already requires an explicit request cue in the same clause.
        return "来给吧呢呀哦啊了吗嘛么用做烤煮就和再也可会都更最".Contains(value.Value);
    }

    private static bool IsCjk(char value)
    {
        return value is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
    }

    private static bool IsAsciiWordCharacter(char value)
    {
        return value <= 127 && (char.IsLetterOrDigit(value) || value == '_');
    }

    private sealed record AliasOccurrence(HelpRequestItemAlias Item, string Alias, int TextIndex);
}
