using System;
using System.Text.RegularExpressions;
using StardewValley;

namespace ValleyTalk;

internal static class ConversationTextPostProcessor
{
    private static readonly Regex PlayerFarewellPattern = new(
        @"(再见|拜拜|回头见|回头聊|下次见|改天聊|晚点见|先走|先去|去忙|我得走|我要走|不打扰|先这样|回农场|去农场忙|see\s+you|goodbye|bye|later|talk\s+later)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NpcFarewellPattern = new(
        @"(再见|拜拜|下次见|改天聊|我该走了|我得走了|我先走了|see\s+you|goodbye|bye|talk\s+later)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string NormalizeImmediateNicknameReply(string dialogue, string playerText)
    {
        if (string.IsNullOrWhiteSpace(dialogue)
            || !TryExtractNicknameRequest(playerText, out string nickname)
            || string.IsNullOrWhiteSpace(Game1.player?.Name))
        {
            return dialogue;
        }

        string farmerName = Game1.player.Name;
        if (!dialogue.Contains(farmerName, StringComparison.Ordinal)
            && !dialogue.Contains(nickname, StringComparison.Ordinal))
        {
            return dialogue;
        }

        string normalized = dialogue
            .Replace($"{farmerName}{nickname}", nickname, StringComparison.Ordinal)
            .Replace($"{nickname}{farmerName}", nickname, StringComparison.Ordinal)
            .Replace(farmerName, nickname, StringComparison.Ordinal);

        string escapedNickname = Regex.Escape(nickname);
        normalized = Regex.Replace(
            normalized,
            $"{escapedNickname}(?:[，,、\\s]*{escapedNickname})+",
            nickname,
            RegexOptions.CultureInvariant);

        return normalized;
    }

    public static bool PlayerLikelyEndedConversation(string playerText)
    {
        return !string.IsNullOrWhiteSpace(playerText)
            && PlayerFarewellPattern.IsMatch(playerText);
    }

    public static bool NpcLikelyEndedConversation(string npcText)
    {
        return !string.IsNullOrWhiteSpace(npcText)
            && NpcFarewellPattern.IsMatch(npcText);
    }

    private static bool TryExtractNicknameRequest(string playerText, out string nickname)
    {
        nickname = string.Empty;
        if (string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        var patterns = new[]
        {
            @"(?:以后|以后就|以后你可以|以后你就|你以后可以|你可以|你能不能|能不能|请|之后|以后请)?\s*(?:叫|喊|称呼|管)我(?:为|作|做)?\s*(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,12}?)(?=就|吧|好了|可以了|行了|吗|么|嘛|啦|呀|，|。|,|\.|!|！|\?|？|$)",
            @"(?:call|name)\s+me\s+(?<name>[A-Za-z0-9_·•\-]{1,24})(?=\s|,|\.|!|\?|$)"
        };

        foreach (string pattern in patterns)
        {
            var match = Regex.Match(playerText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            nickname = match.Groups["name"].Value
                .Trim()
                .Trim('“', '”', '"', '\'', '‘', '’', '，', ',', '。', '.', '！', '!', '？', '?', '：', ':');
            return !string.IsNullOrWhiteSpace(nickname);
        }

        return false;
    }
}
