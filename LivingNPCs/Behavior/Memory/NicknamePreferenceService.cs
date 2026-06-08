using System;
using System.Text.RegularExpressions;

namespace LivingNPCs.Behavior;

internal static class NicknamePreferenceService
{
    public static bool TryCreateFallbackMemory(
        string playerText,
        string npcResponse,
        out ValleyTalkMemoryCandidate memory)
    {
        memory = new ValleyTalkMemoryCandidate();
        if (!TryExtractNicknameRequest(playerText, out string nickname))
        {
            return false;
        }

        string status = DetermineNicknameStatus(nickname, npcResponse);
        memory = new ValleyTalkMemoryCandidate
        {
            Kind = "preference",
            Summary = status switch
            {
                "Accepted" => $"The farmer prefers to be called {nickname}, and this NPC accepted.",
                "Rejected" => $"The farmer asked to be called {nickname}, but this NPC did not accept.",
                _ => $"The farmer asked to be called {nickname}; acceptance is unclear."
            },
            Importance = 85
        };
        return true;
    }

    public static void UpdateStateFromMemory(
        LivingNpcState state,
        LongTermMemoryFact? memory,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        if (memory == null || memory.Kind != "preference")
        {
            return;
        }

        var match = Regex.Match(
            memory.Summary,
            @"(?:called|称呼|叫)(?:\s+as)?\s*[“""']?(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,24})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        if (!match.Success)
        {
            return;
        }

        string nickname = CleanNickname(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return;
        }

        state.FarmerNickname = nickname;
        state.FarmerNicknameStatus = memory.Summary.Contains("did not accept", StringComparison.OrdinalIgnoreCase)
            || memory.Summary.Contains("未接受", StringComparison.OrdinalIgnoreCase)
            ? "Rejected"
            : "Accepted";
        state.FarmerNicknameTotalDays = currentTotalDays;
        state.FarmerNicknameTimeOfDay = currentTimeOfDay;
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

            nickname = CleanNickname(match.Groups["name"].Value);
            return !string.IsNullOrWhiteSpace(nickname);
        }

        return false;
    }

    private static string CleanNickname(string nickname)
    {
        return nickname
            .Trim()
            .Trim('“', '”', '"', '\'', '‘', '’', '，', ',', '。', '.', '！', '!', '？', '?', '：', ':');
    }

    private static string DetermineNicknameStatus(string nickname, string npcResponse)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return "Requested";
        }

        string response = npcResponse.ToLowerInvariant();
        bool rejected = ContainsAny(response, "不行", "不能", "不太", "不熟", "暂时", "抱歉", "对不起", "还是算了", "don't", "cannot", "can't", "won't");
        if (rejected)
        {
            return "Rejected";
        }

        bool accepted = response.Contains(nickname.ToLowerInvariant())
            || ContainsAny(response, "可以", "当然", "好啊", "好的", "没问题", "行", "愿意", "sure", "okay", "ok", "of course");

        return accepted ? "Accepted" : "Requested";
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle))
            {
                return true;
            }
        }

        return false;
    }
}
