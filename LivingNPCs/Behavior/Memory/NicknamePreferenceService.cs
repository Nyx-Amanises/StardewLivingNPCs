using System;
using System.Linq;
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

    public static bool TryUpdateStateFromDialogue(
        LivingNpcState state,
        string playerText,
        string npcResponse,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        if (!TryExtractNicknameRequest(playerText, out string nickname))
        {
            return false;
        }

        ApplyNicknameState(
            state,
            nickname,
            DetermineNicknameStatus(nickname, npcResponse),
            currentTotalDays,
            currentTimeOfDay);
        return true;
    }

    public static bool RecoverStateFromStoredMemories(LivingNpcState state)
    {
        if (!string.IsNullOrWhiteSpace(state.FarmerNickname))
        {
            return false;
        }

        foreach (var preference in (state.PlayerPreferenceMemories ?? new())
                     .OrderByDescending(memory => memory.LastUpdatedTotalDays)
                     .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
                     .ThenByDescending(memory => memory.Importance))
        {
            if (!TryExtractNicknameFromPlayerPreference(preference, out string nickname))
            {
                continue;
            }

            ApplyNicknameState(
                state,
                nickname,
                DetermineStoredNicknameStatus(preference.Summary),
                preference.LastUpdatedTotalDays >= 0
                    ? preference.LastUpdatedTotalDays
                    : preference.CreatedTotalDays,
                preference.LastUpdatedTotalDays >= 0
                    ? preference.LastUpdatedTimeOfDay
                    : preference.CreatedTimeOfDay);
            return true;
        }

        foreach (var memory in (state.LongTermMemories ?? new())
                     .Where(memory => memory.Kind == "preference")
                     .OrderByDescending(memory => memory.LastUpdatedTotalDays)
                     .ThenByDescending(memory => memory.LastUpdatedTimeOfDay)
                     .ThenByDescending(memory => memory.Importance))
        {
            UpdateStateFromMemory(
                state,
                memory,
                memory.LastUpdatedTotalDays,
                memory.LastUpdatedTimeOfDay);
            if (!string.IsNullOrWhiteSpace(state.FarmerNickname))
            {
                return true;
            }
        }

        return false;
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
            @"(?:called|称呼|叫)(?:\s+as)?\s*(?:我|她|他|农夫|玩家|自己)?\s*[“""']?(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,24})",
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

        ApplyNicknameState(
            state,
            nickname,
            DetermineStoredNicknameStatus(memory.Summary),
            currentTotalDays,
            currentTimeOfDay);
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

    private static bool TryExtractNicknameFromPlayerPreference(
        PlayerPreferenceFact preference,
        out string nickname)
    {
        nickname = string.Empty;
        string summary = preference.Summary?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(summary)
            || !Regex.IsMatch(
                summary,
                @"(?:农夫|玩家).{0,32}(?:希望|想|要求|请).{0,32}(?:叫|喊|称呼)|(?:farmer|player).{0,48}(?:prefer|want|ask).{0,48}(?:called|call)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        var summaryMatch = Regex.Match(
            summary,
            @"(?:叫|喊|称呼)(?:我|她|他|其|自己|农夫|玩家)?(?:为|作|做)?\s*[“""']?(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,24})|(?:called|call)(?:\s+(?:me|her|him|them|the\s+farmer))?(?:\s+as)?\s*[“""']?(?<name>[A-Za-z0-9_·•\-]{1,24})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (summaryMatch.Success)
        {
            nickname = CleanNickname(summaryMatch.Groups["name"].Value);
        }

        if (string.IsNullOrWhiteSpace(nickname))
        {
            var subjectMatch = Regex.Match(
                preference.Subject?.Trim() ?? string.Empty,
                @"^(?:称呼|昵称|nickname)\s*[:：]?\s*[“""']?(?<name>[\u4e00-\u9fffA-Za-z0-9_·•\-]{1,24})[”""']?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (subjectMatch.Success)
            {
                nickname = CleanNickname(subjectMatch.Groups["name"].Value);
            }
        }

        return IsUsableNickname(nickname);
    }

    private static string DetermineStoredNicknameStatus(string summary)
    {
        if (ContainsAny(
                summary.ToLowerInvariant(),
                "did not accept",
                "refused",
                "rejected",
                "未接受",
                "不接受",
                "拒绝",
                "不愿意"))
        {
            return "Rejected";
        }

        return ContainsAny(summary.ToLowerInvariant(), "unclear", "尚不明确", "不明确")
            ? "Requested"
            : "Accepted";
    }

    private static bool IsUsableNickname(string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 24)
        {
            return false;
        }

        return nickname != "@"
            && !ContainsAny(
                nickname.ToLowerInvariant(),
                "我",
                "她",
                "他",
                "其",
                "自己",
                "农夫",
                "玩家",
                "farmer",
                "player");
    }

    private static void ApplyNicknameState(
        LivingNpcState state,
        string nickname,
        string status,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        state.FarmerNickname = nickname;
        state.FarmerNicknameStatus = status;
        state.FarmerNicknameTotalDays = currentTotalDays;
        state.FarmerNicknameTimeOfDay = currentTimeOfDay;
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
