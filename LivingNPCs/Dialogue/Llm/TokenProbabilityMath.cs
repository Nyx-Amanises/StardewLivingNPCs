using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 选项概率归并算法（WP11 §3.5）：把逐位置的 token 概率（前缀树）递归聚合到选项桶。
/// 沿位置展开 token 组合并累乘概率；累计文本一旦覆盖某选项（忽略大小写与首部空白），
/// 该组合的概率计入该选项；仍是某选项前缀的组合继续下钻；对不上任何选项的分支剪枝。
/// </summary>
internal static class TokenProbabilityMath
{
    public static IReadOnlyDictionary<string, double> AggregateOptions(
        IReadOnlyList<IReadOnlyDictionary<string, double>> positions,
        IReadOnlyList<string> options)
    {
        var buckets = new Dictionary<string, double>();
        foreach (string option in options)
        {
            buckets[option] = 0;
        }

        if (positions.Count == 0 || options.Count == 0)
        {
            return buckets;
        }

        var normalizedOptions = options
            .Select(option => (Original: option, Normalized: Normalize(option)))
            .Where(pair => pair.Normalized.Length > 0)
            .ToList();
        Recurse(positions, normalizedOptions, buckets, 0, string.Empty, 1.0);
        return buckets;
    }

    private static void Recurse(
        IReadOnlyList<IReadOnlyDictionary<string, double>> positions,
        List<(string Original, string Normalized)> options,
        Dictionary<string, double> buckets,
        int index,
        string prefix,
        double probability)
    {
        if (index >= positions.Count)
        {
            return;
        }

        foreach ((string token, double tokenProbability) in positions[index])
        {
            double weight = probability * tokenProbability;
            if (weight <= 0)
            {
                continue;
            }

            string text = prefix + token;
            string normalized = Normalize(text);
            if (normalized.Length == 0)
            {
                // 纯空白前缀（如引导空格 token）：尚无信息，继续下钻。
                Recurse(positions, options, buckets, index + 1, text, weight);
                continue;
            }

            string? matched = null;
            bool anyPartial = false;
            foreach ((string original, string optionText) in options)
            {
                if (normalized.StartsWith(optionText, StringComparison.Ordinal))
                {
                    matched = original;
                    break;
                }

                if (optionText.StartsWith(normalized, StringComparison.Ordinal))
                {
                    anyPartial = true;
                }
            }

            if (matched != null)
            {
                buckets[matched] += weight;
            }
            else if (anyPartial)
            {
                Recurse(positions, options, buckets, index + 1, text, weight);
            }
        }
    }

    private static string Normalize(string text)
    {
        return text.TrimStart().ToLowerInvariant();
    }
}
