using System;
using System.Collections.Generic;
using System.Linq;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>选出的风格样本：解析后的键上下文 + 台词文本。</summary>
internal sealed record DialogueSample(DialogueKeyFacts Facts, string Text);

/// <summary>
/// 台词风格样本选择（WP10 §5.4 样本选择）：按与当前上下文的差异分升序取前 20 条。
/// 缓存键 =（季节, 季节日, 心数）三元组（§4.7.3），由 CharacterRuntime 持有。
/// </summary>
internal static class SampleSelector
{
    public const int MaxSamples = 20;

    /// <summary>把（键 → 台词）字典解析入库；婚后台词由加载方以 M_ 前缀键传入。</summary>
    public static List<DialogueSample> BuildLibrary(IReadOnlyDictionary<string, string> samples)
    {
        var library = new List<DialogueSample>(samples.Count);
        foreach (var pair in samples)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            library.Add(new DialogueSample(DialogueKeyGrammar.Parse(pair.Key), pair.Value));
        }

        return library;
    }

    /// <summary>差异分升序取前 20（含 0–9 随机抖动）。</summary>
    public static List<DialogueSample> Select(
        IReadOnlyList<DialogueSample> library,
        DialogueKeyFacts current,
        Random? random = null)
    {
        random ??= Random.Shared;
        return library
            .Select(sample => (sample, score: Score(current, sample.Facts) + random.Next(10)))
            .OrderBy(pair => pair.score)
            .Take(MaxSamples)
            .Select(pair => pair.sample)
            .ToList();
    }

    /// <summary>差异分（越小越相似；权重表见 §5.4）。</summary>
    public static int Score(DialogueKeyFacts current, DialogueKeyFacts candidate)
    {
        int score = 0;

        if (current.Hearts.HasValue && candidate.Hearts.HasValue)
        {
            score += Math.Abs(current.Hearts.Value - candidate.Hearts.Value) * 100;
        }

        if (!string.IsNullOrEmpty(current.Season) && !string.IsNullOrEmpty(candidate.Season)
            && !string.Equals(current.Season, candidate.Season, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (current.Weekday.HasValue && candidate.Weekday.HasValue && current.Weekday != candidate.Weekday)
        {
            score += 1;
        }

        if (current.DayOfMonth.HasValue && candidate.DayOfMonth.HasValue && current.DayOfMonth != candidate.DayOfMonth)
        {
            score += 200;
        }

        if (current.IsGift != candidate.IsGift)
        {
            score += 2000;
        }

        if (!string.IsNullOrEmpty(current.TimeBucket) && !string.IsNullOrEmpty(candidate.TimeBucket)
            && !string.Equals(current.TimeBucket, candidate.TimeBucket, StringComparison.Ordinal))
        {
            score += 20;
        }

        score += EnumMismatch(current.RandomAction != RandomAction.None, candidate.RandomAction != RandomAction.None, current.RandomAction == candidate.RandomAction);
        score += EnumMismatch(current.SpouseAction != SpouseAction.None, candidate.SpouseAction != SpouseAction.None, current.SpouseAction == candidate.SpouseAction);
        score += PairMismatch(current.SpouseName, candidate.SpouseName, both: 10000, single: 2000);

        if (current.Year.HasValue != candidate.Year.HasValue
            || (current.Year.HasValue && current.Year != candidate.Year))
        {
            score += 200;
        }

        score += PairMismatch(current.InlawName, candidate.InlawName, both: 500, single: 1000);
        return score;
    }

    /// <summary>枚举类差异：双方都有但不同 +200；仅一方有 +2000。</summary>
    private static int EnumMismatch(bool currentHas, bool candidateHas, bool equal)
    {
        if (currentHas && candidateHas)
        {
            return equal ? 0 : 200;
        }

        return currentHas != candidateHas ? 2000 : 0;
    }

    private static int PairMismatch(string current, string candidate, int both, int single)
    {
        bool currentHas = !string.IsNullOrEmpty(current);
        bool candidateHas = !string.IsNullOrEmpty(candidate);
        if (currentHas && candidateHas)
        {
            return string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase) ? 0 : both;
        }

        return currentHas != candidateHas ? single : 0;
    }
}
