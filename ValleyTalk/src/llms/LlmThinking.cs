using System;
using Newtonsoft.Json.Linq;

namespace ValleyTalk;

internal static class LlmThinking
{
    public const string Auto = "Auto";
    public const string Off = "Off";
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";

    public static readonly string[] Options = [Auto, Off, Low, Medium, High];

    public static string Normalize(string value, string fallback = Auto)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        foreach (string option in Options)
        {
            if (string.Equals(value.Trim(), option, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        return fallback;
    }

    public static string ForCall(bool fastPass)
    {
        return fastPass
            ? Normalize(ModEntry.Config?.RoutingThinkingLevel, Off)
            : Normalize(ModEntry.Config?.ChatThinkingLevel, Auto);
    }

    public static bool IsOff(string level)
    {
        return string.Equals(Normalize(level), Off, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAuto(string level)
    {
        return string.Equals(Normalize(level), Auto, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToOpenAiReasoningEffort(string level)
    {
        return Normalize(level) switch
        {
            Off => "none",
            Low => "low",
            Medium => "medium",
            High => "high",
            _ => null
        };
    }

    public static int ToGeminiThinkingBudget(string level, string modelName)
    {
        return Normalize(level) switch
        {
            Off => 0,
            Low => 128,
            Medium => 512,
            High => 1024,
            _ => modelName.Contains("flash", StringComparison.OrdinalIgnoreCase) ? 0 : 128
        };
    }

    public static bool? ToDeepSeekThinkingEnabled(string level)
    {
        return Normalize(level) switch
        {
            Off => false,
            Low or Medium or High => true,
            _ => null
        };
    }

    public static bool IsOpenAiReasoningModel(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("gpt5", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gpt-5", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeepSeekThinkingModel(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            && (normalized.Contains("v4", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("flash", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("reasoner", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("r1", StringComparison.OrdinalIgnoreCase));
    }

    public static void AddOpenAiCompatibleThinkingParameters(JObject body, string modelName, string level)
    {
        string normalizedLevel = Normalize(level);
        if (IsAuto(normalizedLevel))
        {
            return;
        }

        if (IsOpenAiReasoningModel(modelName))
        {
            string effort = ToOpenAiReasoningEffort(normalizedLevel);
            if (!string.IsNullOrWhiteSpace(effort))
            {
                body["reasoning_effort"] = effort;
            }

            return;
        }

        if (IsDeepSeekThinkingModel(modelName))
        {
            bool? enabled = ToDeepSeekThinkingEnabled(normalizedLevel);
            if (enabled.HasValue)
            {
                body["enable_thinking"] = enabled.Value;
            }
        }
    }

    public static string RoutingSystemPrompt()
    {
        string level = ForCall(fastPass: true);
        string thinkingHint = IsOff(level)
            ? "Thinking/reasoning is disabled."
            : $"Use {level.ToLowerInvariant()} routing reasoning only if the model supports it.";
        return $"You are a fast JSON router. {thinkingHint} Do not over-analyze. Output only one compact JSON object.";
    }

    private static string NormalizeModelName(string modelName)
    {
        return (modelName ?? string.Empty)
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(".", "-", StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
