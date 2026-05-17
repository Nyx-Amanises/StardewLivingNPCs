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
        @"(回头见|下次见|改天聊|先忙|先去|再见|拜拜|不耽误你|你去忙吧|我该走了|see\s+you|goodbye|bye|talk\s+later)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
