using System;
using System.Linq;

namespace LivingNPCs.Dialogue.Llm;

internal static partial class LlmThinking
{
    // Public API capabilities, checked 2026-09-12. Ultra remains a readable saved value,
    // but no supported API here accepts it verbatim. See docs/model-thinking-levels.md.
    public static string[] OptionsFor(string provider, string modelName)
    {
        string model = EffectiveThinkingModel(provider, modelName);
        if (string.Equals(provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeThinking.GetOptions(model);
        }

        if (IsDeepSeekThinkingModel(model))
        {
            return SupportsDeepSeekLowEffort(model)
                ? [Auto, Off, Low, High, Max]
                : [Auto, Off, High];
        }

        if (IsGeminiThinkingModel(model))
        {
            return GeminiOptions(model);
        }

        return IsOpenAiReasoningModel(model) ? OpenAiOptions(model) : [Auto];
    }

    public static string NormalizeForModel(string level, string provider, string modelName, string fallback = Auto)
    {
        string model = EffectiveThinkingModel(provider, modelName);
        string normalized = Normalize(level, fallback);
        if (string.Equals(provider, "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return ClaudeThinking.NormalizeLevel(normalized, model);
        }

        if (IsDeepSeekThinkingModel(model))
        {
            if (normalized is Auto or Off)
            {
                return normalized;
            }

            return ToDeepSeekReasoningEffort(normalized, model) switch
            {
                "low" => Low,
                "max" => Max,
                _ => High
            };
        }

        if (IsGeminiThinkingModel(model))
        {
            return NormalizeGeminiLevel(normalized, model);
        }

        return IsOpenAiReasoningModel(model) ? NormalizeOpenAiLevel(normalized, model) : Auto;
    }

    private static string EffectiveThinkingModel(string provider, string modelName)
    {
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            return modelName;
        }

        return (provider ?? string.Empty).ToLowerInvariant() switch
        {
            "google" => "gemini-2.5-flash",
            "anthropic" => "claude-haiku-4-5",
            "deepseek" => "deepseek-flash",
            "openai" => "gpt-4o",
            _ => string.Empty
        };
    }

    private static string NormalizeOpenAiLevel(string level, string modelName)
    {
        return NormalizeToOptions(level, OpenAiOptions(modelName));
    }

    private static string[] OpenAiOptions(string modelName)
    {
        if (IsGptFamily(modelName, "6"))
        {
            return [Auto, Low, Medium, High, XHigh, Max];
        }

        if (IsGptFamily(modelName, "5-6"))
        {
            return [Auto, Off, Low, Medium, High, XHigh, Max];
        }

        if (IsGptFamily(modelName, "5-3-codex") || IsGptFamily(modelName, "5-2-codex")
            || IsGptFamily(modelName, "5-1-codex-max"))
        {
            return [Auto, Low, Medium, High, XHigh];
        }

        if (IsGptFamily(modelName, "5-codex") || IsGptFamily(modelName, "5-1-codex"))
        {
            return [Auto, Low, Medium, High];
        }

        if (IsGptFamily(modelName, "5-5") || IsGptFamily(modelName, "5-4") || IsGptFamily(modelName, "5-2"))
        {
            return ModelLeaf(modelName).Contains("-pro", StringComparison.OrdinalIgnoreCase)
                ? [Auto, Medium, High, XHigh]
                : [Auto, Off, Low, Medium, High, XHigh];
        }

        if (IsGptFamily(modelName, "5-1"))
        {
            return [Auto, Off, Low, Medium, High];
        }

        if (IsGptFamily(modelName, "5"))
        {
            return ModelLeaf(modelName).Contains("-pro", StringComparison.OrdinalIgnoreCase)
                ? [Auto, High]
                : [Auto, Minimal, Low, Medium, High];
        }

        return [Auto, Low, Medium, High];
    }

    private static string NormalizeGeminiLevel(string level, string modelName)
    {
        string normalized = Normalize(level);
        // This image model has only minimal/high; low should retain the low-latency setting.
        if (IsGemini31FlashLiteImage(modelName) && normalized == Low)
        {
            return Minimal;
        }

        return NormalizeToOptions(normalized, GeminiOptions(modelName));
    }

    private static string[] GeminiOptions(string modelName)
    {
        if (IsGemini3Model(modelName))
        {
            if (IsGemini31FlashLiteImage(modelName))
            {
                return [Auto, Minimal, High];
            }

            if (HasModelFamilyName(modelName, "gemini-3-pro"))
            {
                return [Auto, Low, High];
            }

            bool supportsMinimal = HasModelFamilyName(modelName, "gemini-3-flash")
                || HasModelFamilyName(modelName, "gemini-3-1-flash-lite")
                || HasModelFamilyName(modelName, "gemini-3-5-flash")
                || HasModelFamilyName(modelName, "gemini-3-6-flash");
            return supportsMinimal
                ? [Auto, Minimal, Low, Medium, High]
                : [Auto, Low, Medium, High];
        }

        if (HasModelFamilyName(modelName, "gemini-2-5"))
        {
            return IsGeminiProModel(modelName)
                ? [Auto, Low, Medium, High]
                : [Auto, Off, Low, Medium, High];
        }

        return [Auto];
    }

    private static bool IsGemini31FlashLiteImage(string modelName)
    {
        return HasModelFamilyName(modelName, "gemini-3-1-flash-lite-image");
    }

    private static string NormalizeToOptions(string level, string[] supported)
    {
        string normalized = Normalize(level);
        if (supported.Contains(normalized))
        {
            return normalized;
        }

        int requestedRank = Array.IndexOf(Options, normalized);
        return supported.FirstOrDefault(option => Array.IndexOf(Options, option) >= requestedRank)
            ?? supported[^1];
    }

    private static bool IsGptFamily(string modelName, string version)
    {
        return HasModelFamilyName(modelName, "gpt-" + version)
            || HasModelFamilyName(modelName, "gpt" + version);
    }

    private static string ModelLeaf(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }
}
