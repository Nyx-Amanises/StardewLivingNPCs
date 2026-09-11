using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class MemoryRecallService
{
    private const int MaxRecallScore = 512;
    // The native dialogue box accepts 500 characters; include a full CJK turn after bigram expansion.
    private const int MaxQueryTokens = 512;

    public static MemoryRecallPlan BuildPlan(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        int longTermCount,
        int preferenceCount,
        int currentTotalDays,
        string? currentPlayerText = null)
    {
        MemoryRecallContext context = BuildContext(state, world, recentEntries);
        // Keep the query transient and separate from the passive scene context and saved facts.
        IReadOnlySet<string> queryTokens = LocalTextSearch.Tokenize(currentPlayerText, MaxQueryTokens);
        var longTermMemories = state.LongTermMemories
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && IsPromptSafeMemory(memory.Subject, memory.Summary, memory.Tags))
            .Select(LongTermMemoryStore.NormalizeForStore)
            .Select(memory => ScoreLongTermMemory(memory, context, queryTokens, currentTotalDays))
            .Where(candidate => candidate.Selection.Score >= 45)
            // An explicit current topic must not lose to many weak cues from an old scene.
            .OrderByDescending(candidate => candidate.QueryScore)
            .ThenByDescending(candidate => candidate.Selection.Score)
            .ThenByDescending(candidate => candidate.Selection.Memory.Importance)
            .ThenByDescending(candidate => candidate.Selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(candidate => candidate.Selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, longTermCount))
            .Select(candidate => candidate.Selection)
            .ToList();
        var playerPreferences = state.PlayerPreferenceMemories
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && IsPromptSafeMemory(memory.Subject, memory.Summary, memory.Tags))
            .Select(PlayerPreferenceMemoryStore.NormalizeForStore)
            .Select(memory => ScorePlayerPreferenceMemory(memory, context, queryTokens, currentTotalDays))
            .Where(candidate => candidate.Selection.Score >= 45)
            .OrderByDescending(candidate => candidate.QueryScore)
            .ThenByDescending(candidate => candidate.Selection.Score)
            .ThenByDescending(candidate => candidate.Selection.Memory.Importance)
            .ThenByDescending(candidate => candidate.Selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(candidate => candidate.Selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, preferenceCount))
            .Select(candidate => candidate.Selection)
            .ToList();

        return new MemoryRecallPlan(context, longTermMemories, playerPreferences);
    }

    private static bool IsPromptSafeMemory(string? subject, string? summary, IEnumerable<string>? tags)
    {
        return !RsvAiPolicy.ContainsBlockedReference(subject)
            && !RsvAiPolicy.ContainsBlockedReference(summary)
            && !(tags ?? Enumerable.Empty<string>()).Any(RsvAiPolicy.ContainsBlockedReference);
    }

    public static IReadOnlyList<CommunityImpressionSelection> BuildCommunityImpressionPlan(
        LivingNpcState state,
        int maxCount,
        int currentTotalDays,
        string? currentPlayerText = null)
    {
        IReadOnlySet<string> queryTokens = LocalTextSearch.Tokenize(currentPlayerText, MaxQueryTokens);
        // Only impressions already known by this NPC are candidates. Source and visibility stay
        // attached to the selected fact; a query never grants access to another NPC's memories.
        return state.CommunityImpressions
            .Where(memory => memory != null
                && !string.IsNullOrWhiteSpace(memory.Summary)
                && (memory.ExpiresTotalDays < 0 || memory.ExpiresTotalDays >= currentTotalDays)
                && !RsvAiPolicy.IsBlockedNpcName(memory.SubjectNpcName)
                && !RsvAiPolicy.IsBlockedNpcName(memory.HeardFromNpcName)
                && !RsvAiPolicy.ContainsBlockedReference(memory.Summary))
            .Select(memory => ScoreCommunityImpression(memory, queryTokens, currentTotalDays))
            .Where(candidate => candidate.Selection.Score >= 45)
            .OrderByDescending(candidate => candidate.QueryScore)
            .ThenByDescending(candidate => candidate.Selection.Score)
            .ThenByDescending(candidate => candidate.Selection.Memory.Importance)
            .ThenByDescending(candidate => candidate.Selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(candidate => candidate.Selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, maxCount))
            .Select(candidate => candidate.Selection)
            .ToList();
    }

    public static string FormatLongTermMemoryDebugLabel(IReadOnlyList<LongTermMemorySelection> selections)
    {
        return selections.Count == 0
            ? "暂无"
            : string.Join("；", selections.Select(selection =>
                $"{selection.Memory.Summary}（分数 {selection.Score}，{selection.Reason}）"));
    }

    public static string FormatPlayerPreferenceDebugLabel(IReadOnlyList<PlayerPreferenceSelection> selections)
    {
        return selections.Count == 0
            ? "暂无"
            : string.Join("；", selections.Select(selection =>
                $"{selection.Memory.Summary}（分数 {selection.Score}，{selection.Reason}）"));
    }

    public static string FormatCommunityImpressionDebugLabel(IReadOnlyList<CommunityImpressionSelection> selections)
    {
        return selections.Count == 0
            ? "暂无"
            : string.Join("；", selections.Select(selection =>
                $"{selection.Memory.Summary}（{selection.Memory.Source}/{selection.Memory.Visibility}，分数 {selection.Score}，{selection.Reason}）"));
    }

    public static void MarkRecalled(MemoryRecallPlan recallPlan, int currentTotalDays, int currentTimeOfDay)
    {
        foreach (var selection in recallPlan.LongTermMemories)
        {
            MarkMemoryRecalled(selection.Memory, currentTotalDays, currentTimeOfDay);
        }

        foreach (var selection in recallPlan.PlayerPreferences)
        {
            MarkMemoryRecalled(selection.Memory, currentTotalDays, currentTimeOfDay);
        }
    }

    public static void MarkCommunityImpressionsRecalled(
        IReadOnlyList<CommunityImpressionSelection> selections,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        foreach (var selection in selections)
        {
            var memory = selection.Memory;
            if (memory.LastRecalledTotalDays == currentTotalDays
                && memory.LastRecalledTimeOfDay == currentTimeOfDay)
            {
                continue;
            }

            memory.LastRecalledTotalDays = currentTotalDays;
            memory.LastRecalledTimeOfDay = currentTimeOfDay;
            memory.RecallCount += 1;
        }
    }

    private static (CommunityImpressionSelection Selection, int QueryScore) ScoreCommunityImpression(
        CommunityImpressionFact memory,
        IReadOnlySet<string> queryTokens,
        int currentTotalDays)
    {
        int age = GetMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays);
        int freshnessScore = age switch
        {
            0 => 30,
            1 => 24,
            <= 3 => 18,
            <= 7 => 10,
            <= 14 => 4,
            _ => -20
        };
        int sourceScore = memory.Source switch
        {
            "Witnessed" => 12,
            "CloseCircle" => 5,
            _ => 1
        };
        int lifecycleScore = CommunityImpressionStore.GetFreshnessStage(memory, currentTotalDays) switch
        {
            "fresh" => 10,
            "settled" => 2,
            "fading" => -8,
            _ => -20
        };
        int daysSinceRecall = memory.LastRecalledTotalDays >= 0
            ? currentTotalDays - memory.LastRecalledTotalDays
            : int.MaxValue;
        int recentRecallPenalty = daysSinceRecall switch
        {
            <= 0 => 60,
            1 => 35,
            2 => 20,
            _ => 0
        };
        recentRecallPenalty += (int)System.Math.Clamp((long)memory.RecallCount * 3, 0, 12);
        int queryScore = GetCurrentTopicScore(
            queryTokens,
            $"{memory.SubjectNpcName} {memory.SubjectDisplayName}",
            memory.Summary);
        long score = (long)memory.Importance
            + (memory.Confidence / 5)
            + freshnessScore
            + sourceScore
            + lifecycleScore
            + ((long)memory.TimesReinforced * 2)
            + queryScore
            - recentRecallPenalty
            - (memory.DistortionLevel / 8);
        string reason = memory.Source switch
        {
            "Witnessed" => $"目击，{FormatMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays)}",
            "CloseCircle" => $"熟人转述，{FormatMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays)}",
            _ => $"公共场所里听到一点，{FormatMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays)}"
        };
        if (queryScore > 0)
        {
            reason += $", current topic +{queryScore}";
        }

        return (new CommunityImpressionSelection(memory, (int)System.Math.Clamp(score, 0, MaxRecallScore), reason), queryScore);
    }

    private static MemoryRecallContext BuildContext(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries)
    {
        var tags = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var tokens = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        AddWorldRecallTags(world, tags);
        AddRecallSignals(world.LocationName, tags, tokens);
        AddRecallSignals(world.LocationDisplayName, tags, tokens);
        AddRecallSignals(world.PromptLabel, tags, tokens);
        AddRecallSignals(world.ProgressionKnowledge.PromptLabel, tags, tokens);
        AddRecallSignals(state.LastGiftName, tags, tokens);
        AddRecallSignals(state.LastEventContext, tags, tokens);
        AddRecallSignals(state.LastInteraction, tags, tokens);
        AddRecallSignals(state.LastEmotionReason, tags, tokens);

        foreach (var entry in recentEntries.TakeLast(6))
        {
            AddRecallSignals(entry.Action, tags, tokens);
            AddRecallSignals(entry.Reason, tags, tokens);
        }

        foreach (var request in state.HelpRequests.Where(request => request.Status == "Pending").Take(2))
        {
            AddRecallSignals(request.Summary, tags, tokens);
            AddRecallSignals(request.QuestionTopic, tags, tokens);
            AddRecallSignals(request.RequestedItemLabel, tags, tokens);
        }

        return new MemoryRecallContext(tags, tokens);
    }

    private static void AddWorldRecallTags(WorldContextSnapshot world, ISet<string> tags)
    {
        switch (world.LocationName)
        {
            case "Farm":
                tags.Add("farming");
                tags.Add("work");
                tags.Add("nature");
                break;

            case "Beach":
                tags.Add("fishing");
                tags.Add("nature");
                break;

            case "Mountain":
                tags.Add("mining");
                tags.Add("nature");
                break;

            case "ArchaeologyHouse":
                tags.Add("scholarly");
                break;

            case "Saloon":
                tags.Add("food");
                tags.Add("drink");
                tags.Add("comfort");
                break;
        }

        if (world.TimeOfDay < 900)
        {
            tags.Add("morning");
        }
        else if (world.TimeOfDay >= 1800)
        {
            tags.Add("night");
        }

        if (world.Progression.GreenhouseRepaired)
        {
            tags.Add("farming");
            tags.Add("work");
        }

        if (world.Progression.MinecartsRepaired)
        {
            tags.Add("mining");
        }

        if (world.Progression.GingerIslandUnlocked || world.Progression.BusRepaired)
        {
            tags.Add("adventurous");
            tags.Add("nature");
        }

        if (world.Progression.MovieTheaterOpen)
        {
            tags.Add("artistic");
            tags.Add("comfort");
        }
    }

    private static void AddRecallSignals(string? text, ISet<string> tags, ISet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        text = text.Length > 8192 ? text.Substring(0, 8192) : text;
        foreach (string token in ExtractRecallTokens(text))
        {
            tokens.Add(token);
        }

        BehaviorValueNormalizer.AddInferredTags(text, tags);
    }

    private static IEnumerable<string> ExtractRecallTokens(string text)
    {
        return Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value)
            .Where(token => token.Length >= 2)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .Take(24);
    }

    private static (LongTermMemorySelection Selection, int QueryScore) ScoreLongTermMemory(
        LongTermMemoryFact memory,
        MemoryRecallContext context,
        IReadOnlySet<string> queryTokens,
        int currentTotalDays)
    {
        var reasons = new List<string>();
        int score = memory.Importance;
        int reinforcementBonus = System.Math.Min(6, memory.TimesReinforced) * 3;
        score += reinforcementBonus;
        if (reinforcementBonus > 0)
        {
            reasons.Add($"reinforced +{reinforcementBonus}");
        }

        int kindBonus = LongTermMemoryStore.GetKindBonus(memory.Kind);
        score += kindBonus;
        if (kindBonus > 0)
        {
            reasons.Add($"{memory.Kind} +{kindBonus}");
        }

        int freshnessBonus = GetMemoryFreshnessBonus(memory.LastUpdatedTotalDays, currentTotalDays);
        score += freshnessBonus;
        if (freshnessBonus > 0)
        {
            reasons.Add($"fresh +{freshnessBonus}");
        }

        int tagOverlap = memory.Tags.Count(tag => context.Tags.Contains(tag));
        if (tagOverlap > 0)
        {
            int tagBonus = tagOverlap * 14;
            score += tagBonus;
            reasons.Add($"tags +{tagBonus}");
        }

        int tokenOverlap = CountRecallTokenOverlap(memory.Subject, memory.Summary, context.Tokens);
        if (tokenOverlap > 0)
        {
            int tokenBonus = tokenOverlap * 8;
            score += tokenBonus;
            reasons.Add($"topic +{tokenBonus}");
        }

        int queryScore = GetCurrentTopicScore(queryTokens, memory.Subject, memory.Summary, memory.Tags);
        if (queryScore > 0)
        {
            score += queryScore;
            reasons.Add($"current topic +{queryScore}");
        }

        int recallPenalty = GetRecentRecallPenalty(memory.LastRecalledTotalDays, currentTotalDays);
        score -= recallPenalty;
        if (recallPenalty > 0)
        {
            reasons.Add($"recent recall -{recallPenalty}");
        }

        return (new LongTermMemorySelection(
            memory,
            System.Math.Clamp(score, 0, MaxRecallScore),
            reasons.Count == 0 ? "base salience" : string.Join(", ", reasons)), queryScore);
    }

    private static (PlayerPreferenceSelection Selection, int QueryScore) ScorePlayerPreferenceMemory(
        PlayerPreferenceFact memory,
        MemoryRecallContext context,
        IReadOnlySet<string> queryTokens,
        int currentTotalDays)
    {
        var reasons = new List<string>();
        int score = memory.Importance;
        int reinforcementBonus = System.Math.Min(6, memory.TimesReinforced) * 3;
        score += reinforcementBonus;
        if (reinforcementBonus > 0)
        {
            reasons.Add($"reinforced +{reinforcementBonus}");
        }

        int freshnessBonus = GetMemoryFreshnessBonus(memory.LastUpdatedTotalDays, currentTotalDays);
        score += freshnessBonus;
        if (freshnessBonus > 0)
        {
            reasons.Add($"fresh +{freshnessBonus}");
        }

        int tagOverlap = memory.Tags.Count(tag => context.Tags.Contains(tag));
        if (tagOverlap > 0)
        {
            int tagBonus = tagOverlap * 16;
            score += tagBonus;
            reasons.Add($"tags +{tagBonus}");
        }

        int tokenOverlap = CountRecallTokenOverlap(memory.Subject, memory.Summary, context.Tokens);
        if (tokenOverlap > 0)
        {
            int tokenBonus = tokenOverlap * 10;
            score += tokenBonus;
            reasons.Add($"topic +{tokenBonus}");
        }

        int queryScore = GetCurrentTopicScore(queryTokens, memory.Subject, memory.Summary, memory.Tags);
        if (queryScore > 0)
        {
            score += queryScore;
            reasons.Add($"current topic +{queryScore}");
        }

        int recallPenalty = GetRecentRecallPenalty(memory.LastRecalledTotalDays, currentTotalDays);
        score -= recallPenalty;
        if (recallPenalty > 0)
        {
            reasons.Add($"recent recall -{recallPenalty}");
        }

        return (new PlayerPreferenceSelection(
            memory,
            System.Math.Clamp(score, 0, MaxRecallScore),
            reasons.Count == 0 ? "base salience" : string.Join(", ", reasons)), queryScore);
    }

    private static int GetCurrentTopicScore(
        IReadOnlySet<string> queryTokens,
        string subject,
        string summary,
        IReadOnlyList<string>? tags = null)
    {
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var subjectTokens = LocalTextSearch.Tokenize(subject, maxTokens: 64);
        var memoryTokens = new HashSet<string>(subjectTokens, System.StringComparer.OrdinalIgnoreCase);
        memoryTokens.UnionWith(LocalTextSearch.Tokenize(summary, maxTokens: 256));
        if (tags != null)
        {
            memoryTokens.UnionWith(tags);
        }

        int overlap = memoryTokens.Count(queryTokens.Contains);
        if (overlap == 0)
        {
            return 0;
        }

        int subjectOverlap = subjectTokens.Count(queryTokens.Contains);
        int coverageBonus = 16 * overlap / queryTokens.Count;
        return System.Math.Min(100, 48 + (System.Math.Min(4, overlap) * 10)
            + (System.Math.Min(2, subjectOverlap) * 8) + coverageBonus);
    }

    private static int CountRecallTokenOverlap(string subject, string summary, IReadOnlySet<string> contextTokens)
    {
        return ExtractRecallTokens($"{subject} {summary}")
            .Count(contextTokens.Contains);
    }

    private static int GetMemoryAge(int totalDays, int currentTotalDays)
    {
        return totalDays < 0
            ? int.MaxValue
            : System.Math.Max(0, currentTotalDays - totalDays);
    }

    private static string FormatMemoryAge(int totalDays, int currentTotalDays)
    {
        int age = GetMemoryAge(totalDays, currentTotalDays);
        return age switch
        {
            0 => "today",
            1 => "yesterday",
            int.MaxValue => "at an unknown time",
            _ => $"{age} days ago"
        };
    }

    private static int GetMemoryFreshnessBonus(int totalDays, int currentTotalDays)
    {
        return GetMemoryAge(totalDays, currentTotalDays) switch
        {
            0 => 18,
            1 => 14,
            <= 7 => 10,
            <= 28 => 5,
            _ => 0
        };
    }

    private static int GetRecentRecallPenalty(int totalDays, int currentTotalDays)
    {
        return GetMemoryAge(totalDays, currentTotalDays) switch
        {
            0 => 18,
            1 => 10,
            <= 3 => 4,
            _ => 0
        };
    }

    private static void MarkMemoryRecalled(LongTermMemoryFact memory, int currentTotalDays, int currentTimeOfDay)
    {
        if (memory.LastRecalledTotalDays == currentTotalDays
            && memory.LastRecalledTimeOfDay == currentTimeOfDay)
        {
            return;
        }

        memory.LastRecalledTotalDays = currentTotalDays;
        memory.LastRecalledTimeOfDay = currentTimeOfDay;
        memory.RecallCount += 1;
    }

    private static void MarkMemoryRecalled(PlayerPreferenceFact memory, int currentTotalDays, int currentTimeOfDay)
    {
        if (memory.LastRecalledTotalDays == currentTotalDays
            && memory.LastRecalledTimeOfDay == currentTimeOfDay)
        {
            return;
        }

        memory.LastRecalledTotalDays = currentTotalDays;
        memory.LastRecalledTimeOfDay = currentTimeOfDay;
        memory.RecallCount += 1;
    }
}
