using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ValleyTalk;

internal static class LlmThinking
{
    public const string Auto = "Auto";
    public const string Off = "Off";
    public const string Minimal = "Minimal";
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string XHigh = "XHigh";

    public static readonly string[] Options = [Auto, Off, Minimal, Low, Medium, High, XHigh];

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
            Minimal => "minimal",
            Low => "low",
            Medium => "medium",
            High => "high",
            XHigh => "xhigh",
            _ => null
        };
    }

    public static string ToDeepSeekReasoningEffort(string level)
    {
        return Normalize(level) switch
        {
            Minimal or Low or Medium or High => "high",
            XHigh => "max",
            _ => null
        };
    }

    public static string ToGeminiOpenAiReasoningEffort(string level, string modelName)
    {
        string normalizedLevel = Normalize(level);
        if (normalizedLevel == Off)
        {
            if (IsGemini3Model(modelName))
            {
                return IsGeminiFlashModel(modelName) ? "minimal" : "low";
            }

            return IsGeminiProModel(modelName) ? "low" : "none";
        }

        return normalizedLevel switch
        {
            Minimal => IsGeminiProModel(modelName) ? "low" : "minimal",
            Low => "low",
            Medium => "medium",
            High => "high",
            XHigh => "high",
            _ => null
        };
    }

    public static JObject BuildGeminiThinkingConfig(string level, string modelName)
    {
        string normalizedLevel = Normalize(level);
        if (IsAuto(normalizedLevel))
        {
            return null;
        }

        if (IsGemini3Model(modelName))
        {
            string thinkingLevel = ToGeminiThinkingLevel(normalizedLevel, modelName);
            return string.IsNullOrWhiteSpace(thinkingLevel)
                ? null
                : new JObject { ["thinkingLevel"] = thinkingLevel };
        }

        int? thinkingBudget = ToGeminiThinkingBudget(normalizedLevel, modelName);
        return thinkingBudget.HasValue
            ? new JObject { ["thinkingBudget"] = thinkingBudget.Value }
            : null;
    }

    public static string ToGeminiThinkingLevel(string level, string modelName)
    {
        string normalizedLevel = Normalize(level);
        return normalizedLevel switch
        {
            Off => IsGeminiFlashModel(modelName) ? "minimal" : "low",
            Minimal => IsGeminiFlashModel(modelName) ? "minimal" : "low",
            Low => "low",
            Medium => "medium",
            High or XHigh => "high",
            _ => null
        };
    }

    public static int? ToGeminiThinkingBudget(string level, string modelName)
    {
        return Normalize(level) switch
        {
            Off => IsGeminiProModel(modelName) ? 128 : 0,
            Minimal => 128,
            Low => 128,
            Medium => 512,
            High or XHigh => 1024,
            _ => null
        };
    }

    public static string ToDeepSeekThinkingType(string level)
    {
        return Normalize(level) switch
        {
            Off => "disabled",
            Minimal or Low or Medium or High or XHigh => "enabled",
            _ => null
        };
    }

    public static bool IsOpenAiReasoningModel(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("gpt5", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gpt-5", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeminiThinkingModel(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("gemini", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGemini3Model(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("gemini-3", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("gemini3", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeminiFlashModel(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("flash", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeminiProModel(string modelName)
    {
        string normalized = NormalizeModelName(modelName);
        return normalized.Contains("pro", StringComparison.OrdinalIgnoreCase);
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

        if (IsGeminiThinkingModel(modelName))
        {
            string effort = ToGeminiOpenAiReasoningEffort(normalizedLevel, modelName);
            if (!string.IsNullOrWhiteSpace(effort))
            {
                body["reasoning_effort"] = effort;
            }

            return;
        }

        if (IsDeepSeekThinkingModel(modelName))
        {
            string thinkingType = ToDeepSeekThinkingType(normalizedLevel);
            if (!string.IsNullOrWhiteSpace(thinkingType))
            {
                body["thinking"] = new JObject
                {
                    ["type"] = thinkingType
                };
            }

            string effort = ToDeepSeekReasoningEffort(normalizedLevel);
            if (!string.IsNullOrWhiteSpace(effort))
            {
                body["reasoning_effort"] = effort;
            }
        }
    }

    public static string DescribeThinkingParameters(JObject body)
    {
        if (body == null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (body.TryGetValue("reasoning_effort", out JToken reasoningEffort) && reasoningEffort.Type != JTokenType.Null)
        {
            parts.Add($"reasoning_effort={reasoningEffort}");
        }
        if (body.TryGetValue("thinking", out JToken thinking) && thinking.Type != JTokenType.Null)
        {
            parts.Add($"thinking={thinking.ToString(Newtonsoft.Json.Formatting.None)}");
        }
        if (body["generationConfig"]?["thinkingConfig"] is JToken thinkingConfig)
        {
            parts.Add($"thinkingConfig={thinkingConfig.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        return string.Join(", ", parts);
    }

    public static string SummarizeProviderError(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "no provider error body";
        }

        try
        {
            var json = JObject.Parse(text);
            string message = json["error"]?["message"]?.ToString()
                ?? json["message"]?.ToString()
                ?? json["error"]?.ToString()
                ?? string.Empty;
            string code = json["error"]?["code"]?.ToString() ?? json["code"]?.ToString() ?? string.Empty;
            string type = json["error"]?["type"]?.ToString() ?? json["type"]?.ToString() ?? string.Empty;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(message))
            {
                parts.Add(message);
            }
            if (!string.IsNullOrWhiteSpace(code))
            {
                parts.Add($"code={code}");
            }
            if (!string.IsNullOrWhiteSpace(type))
            {
                parts.Add($"type={type}");
            }

            if (parts.Count > 0)
            {
                return Truncate(string.Join("; ", parts), 300);
            }
        }
        catch
        {
        }

        return Truncate(text.Trim(), 300);
    }

    public static void LogThinkingFallbackWarning(string modelName, string level, string parameters, string providerError)
    {
        Log.Warning(Util.GetConsoleString(
            "warningThinkingParametersRejected",
            new
            {
                Model = modelName,
                Level = level,
                Parameters = parameters,
                Error = SummarizeProviderError(providerError)
            },
            $"The request with thinking parameters failed for model {modelName} ({parameters}, level {level}); retrying without thinking controls. Provider response: {SummarizeProviderError(providerError)}"
        ));
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

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
