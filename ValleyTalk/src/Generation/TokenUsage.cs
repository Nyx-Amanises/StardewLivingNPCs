using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ValleyTalk;

internal sealed class TokenUsage
{
    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int TotalTokens { get; init; }

    public int CachedPromptTokens { get; init; }

    public int ReasoningTokens { get; init; }

    public bool IsEstimated { get; init; }

    public string Source { get; init; } = "unknown";

    public bool HasAnyTokens => PromptTokens > 0 || CompletionTokens > 0 || TotalTokens > 0;

    public static TokenUsage Estimate(string promptText, string completionText, string source = "local estimate")
    {
        int promptTokens = EstimateTokenCount(promptText);
        int completionTokens = EstimateTokenCount(completionText);

        return new TokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            IsEstimated = true,
            Source = source
        };
    }

    public static TokenUsage FromOpenAiUsage(JObject usage)
    {
        if (usage == null)
        {
            return new TokenUsage();
        }

        int promptTokens = usage.Value<int?>("prompt_tokens") ?? 0;
        int completionTokens = usage.Value<int?>("completion_tokens") ?? 0;
        int totalTokens = usage.Value<int?>("total_tokens") ?? promptTokens + completionTokens;
        int cachedPromptTokens = usage["prompt_tokens_details"]?.Value<int?>("cached_tokens") ?? 0;
        int reasoningTokens = usage["completion_tokens_details"]?.Value<int?>("reasoning_tokens") ?? 0;

        return new TokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            CachedPromptTokens = cachedPromptTokens,
            ReasoningTokens = reasoningTokens,
            Source = "provider usage"
        };
    }

    public static TokenUsage FromClaudeUsage(JObject usage)
    {
        if (usage == null)
        {
            return new TokenUsage();
        }

        int inputTokens = usage.Value<int?>("input_tokens") ?? 0;
        int outputTokens = usage.Value<int?>("output_tokens") ?? 0;
        int cacheCreationTokens = usage.Value<int?>("cache_creation_input_tokens") ?? 0;
        int cacheReadTokens = usage.Value<int?>("cache_read_input_tokens") ?? 0;

        return new TokenUsage
        {
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            CachedPromptTokens = cacheCreationTokens + cacheReadTokens,
            Source = "provider usage"
        };
    }

    public static TokenUsage FromGeminiUsage(JObject usage)
    {
        if (usage == null)
        {
            return new TokenUsage();
        }

        int promptTokens = usage.Value<int?>("promptTokenCount") ?? 0;
        int completionTokens = usage.Value<int?>("candidatesTokenCount") ?? 0;
        int totalTokens = usage.Value<int?>("totalTokenCount") ?? promptTokens + completionTokens;
        int reasoningTokens = usage.Value<int?>("thoughtsTokenCount") ?? 0;

        return new TokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            ReasoningTokens = reasoningTokens,
            Source = "provider usage"
        };
    }

    public static TokenUsage FromLlamaCppTimings(JObject timings)
    {
        if (timings == null)
        {
            return new TokenUsage();
        }

        int promptTokens = timings.Value<int?>("prompt_n") ?? 0;
        int completionTokens = timings.Value<int?>("predicted_n") ?? 0;

        return new TokenUsage
        {
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            Source = "llama.cpp timings"
        };
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        int cjkCharacters = text.Count(IsCjkCharacter);
        int nonCjkCharacters = text.Length - cjkCharacters;
        return cjkCharacters + (int)Math.Ceiling(nonCjkCharacters / 4.0);
    }

    private static bool IsCjkCharacter(char character)
    {
        return character is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF'
            or >= '\u3040' and <= '\u30FF'
            or >= '\uAC00' and <= '\uD7AF';
    }
}
