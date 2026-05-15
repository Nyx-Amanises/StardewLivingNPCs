using System;
using System.Text.RegularExpressions;
using StardewValley;

namespace ValleyTalk;

internal static class ConversationTextPostProcessor
{
    public static string NormalizeImmediateNicknameReply(string dialogue, string playerText)
    {
        if (string.IsNullOrWhiteSpace(dialogue)
            || !TryExtractNicknameRequest(playerText, out string nickname)
            || string.IsNullOrWhiteSpace(Game1.player?.Name)
            || !dialogue.Contains(nickname, StringComparison.Ordinal)
            || !dialogue.Contains(Game1.player.Name, StringComparison.Ordinal))
        {
            return dialogue;
        }

        return dialogue.Replace(Game1.player.Name, nickname, StringComparison.Ordinal);
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
            @"(?:以后|以后就|以后你可以|你可以|之后|以后请)?\s*(?:叫|喊|称呼)我(?:为|作|做)?\s*(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,12}?)(?=就|吧|好了|可以了|行了|，|。|,|\.|!|！|\?|？|$)",
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
