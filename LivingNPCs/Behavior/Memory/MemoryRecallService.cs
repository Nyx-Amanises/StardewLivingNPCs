using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class MemoryRecallService
{
    public static MemoryRecallPlan BuildPlan(
        LivingNpcState state,
        WorldContextSnapshot world,
        IReadOnlyList<BehaviorMemoryEntry> recentEntries,
        int longTermCount,
        int preferenceCount,
        int currentTotalDays)
    {
        MemoryRecallContext context = BuildContext(state, world, recentEntries);
        var longTermMemories = state.LongTermMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(LongTermMemoryStore.NormalizeForStore)
            .Select(memory => ScoreLongTermMemory(memory, context, currentTotalDays))
            .Where(selection => selection.Score >= 45)
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Memory.Importance)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, longTermCount))
            .ToList();
        var playerPreferences = state.PlayerPreferenceMemories
            .Where(memory => memory != null && !string.IsNullOrWhiteSpace(memory.Summary))
            .Select(BehaviorMemory.NormalizePlayerPreferenceMemoryForStore)
            .Select(memory => ScorePlayerPreferenceMemory(memory, context, currentTotalDays))
            .Where(selection => selection.Score >= 45)
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Memory.Importance)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, preferenceCount))
            .ToList();

        return new MemoryRecallPlan(context, longTermMemories, playerPreferences);
    }

    public static IReadOnlyList<CommunityImpressionSelection> BuildCommunityImpressionPlan(
        LivingNpcState state,
        int maxCount,
        int currentTotalDays)
    {
        return state.CommunityImpressions
            .Select(memory => ScoreCommunityImpression(memory, currentTotalDays))
            .Where(selection => selection.Score >= 45)
            .OrderByDescending(selection => selection.Score)
            .ThenByDescending(selection => selection.Memory.Importance)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTotalDays)
            .ThenByDescending(selection => selection.Memory.LastUpdatedTimeOfDay)
            .Take(System.Math.Max(0, maxCount))
            .ToList();
    }

    public static string FormatLongTermMemoryPromptLabel(IReadOnlyList<LongTermMemorySelection> selections)
    {
        return selections.Count == 0
            ? "no durable personal memory is especially relevant right now"
            : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
    }

    public static string FormatPlayerPreferencePromptLabel(IReadOnlyList<PlayerPreferenceSelection> selections)
    {
        return selections.Count == 0
            ? "no durable farmer preference memory is especially relevant right now"
            : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
    }

    public static string FormatCommunityImpressionPromptLabel(NPC npc, IReadOnlyList<CommunityImpressionSelection> selections)
    {
        CommunityReactionCue reaction = CommunityReactionStyle.For(npc);
        return selections.Count == 0
            ? "no community impression is especially relevant right now"
            : $"observer tendency: {reaction.PromptLabel}; retelling tendency: {reaction.RetellingPromptLabel}; {string.Join("; ", selections.Select(selection => selection.Memory.PromptLabel))}";
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

    private static CommunityImpressionSelection ScoreCommunityImpression(
        CommunityImpressionFact memory,
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
        int lifecycleScore = memory.FreshnessStage switch
        {
            "fresh" => 10,
            "settled" => 2,
            "fading" => -8,
            _ => -20
        };
        int recentRecallPenalty = memory.LastRecalledTotalDays == currentTotalDays ? 18 : 0;
        int score = memory.Importance
            + (memory.Confidence / 5)
            + freshnessScore
            + sourceScore
            + lifecycleScore
            + (memory.TimesReinforced * 2)
            - recentRecallPenalty
            - (memory.DistortionLevel / 8);
        string reason = memory.Source switch
        {
            "Witnessed" => $"目击，{FormatMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays)}",
            "CloseCircle" => $"熟人转述，{FormatMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays)}",
            _ => $"公共场所里听到一点，{FormatMemoryAge(memory.LastUpdatedTotalDays, currentTotalDays)}"
        };
        return new CommunityImpressionSelection(memory, score, reason);
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

    private static LongTermMemorySelection ScoreLongTermMemory(
        LongTermMemoryFact memory,
        MemoryRecallContext context,
        int currentTotalDays)
    {
        var reasons = new List<string>();
        int score = memory.Importance;
        int reinforcementBonus = System.Math.Min(18, memory.TimesReinforced * 3);
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

        int recallPenalty = GetRecentRecallPenalty(memory.LastRecalledTotalDays, currentTotalDays);
        score -= recallPenalty;
        if (recallPenalty > 0)
        {
            reasons.Add($"recent recall -{recallPenalty}");
        }

        return new LongTermMemorySelection(memory, score, reasons.Count == 0 ? "base salience" : string.Join(", ", reasons));
    }

    private static PlayerPreferenceSelection ScorePlayerPreferenceMemory(
        PlayerPreferenceFact memory,
        MemoryRecallContext context,
        int currentTotalDays)
    {
        var reasons = new List<string>();
        int score = memory.Importance;
        int reinforcementBonus = System.Math.Min(18, memory.TimesReinforced * 3);
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

        int recallPenalty = GetRecentRecallPenalty(memory.LastRecalledTotalDays, currentTotalDays);
        score -= recallPenalty;
        if (recallPenalty > 0)
        {
            reasons.Add($"recent recall -{recallPenalty}");
        }

        return new PlayerPreferenceSelection(memory, score, reasons.Count == 0 ? "base salience" : string.Join(", ", reasons));
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
