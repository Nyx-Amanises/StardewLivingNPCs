using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

internal static partial class LlmThinking
{
    public const string Auto = "Auto";
    public const string Off = "Off";
    public const string Minimal = "Minimal";
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string XHigh = "XHigh";
    public const string Max = "Max";
    public const string Ultra = "Ultra";

    public static readonly string[] Options = [Auto, Off, Minimal, Low, Medium, High, XHigh, Max, Ultra];

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
            ? Normalize(DialogueServices.Config?.RoutingThinkingLevel, Off)
            : Normalize(DialogueServices.Config?.ChatThinkingLevel, Auto);
    }

    public static bool IsOff(string level)
    {
        return string.Equals(Normalize(level), Off, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAuto(string level)
    {
        return string.Equals(Normalize(level), Auto, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToOpenAiReasoningEffort(string level, string modelName)
    {
        return NormalizeOpenAiLevel(level, modelName) switch
        {
            Off => "none",
            Minimal => "minimal",
            Low => "low",
            Medium => "medium",
            High => "high",
            XHigh => "xhigh",
            Max => "max",
            _ => null
        };
    }

    public static string ToDeepSeekReasoningEffort(string level, string modelName)
    {
        return Normalize(level) switch
        {
            // V4 and the deepseek-flash alias support low/high/max; legacy reasoner/R1 keep high.
            Minimal or Low => SupportsDeepSeekLowEffort(modelName) ? "low" : "high",
            Medium or High or XHigh => "high",
            Max or Ultra => SupportsDeepSeekLowEffort(modelName) ? "max" : "high",
            _ => null
        };
    }

    public static string ToGeminiOpenAiReasoningEffort(string level, string modelName)
    {
        return NormalizeGeminiLevel(level, modelName) switch
        {
            Off => "none",
            Minimal => "minimal",
            Low => "low",
            Medium => "medium",
            High => "high",
            _ => null
        };
    }

    public static JObject BuildGeminiThinkingConfig(string level, string modelName)
    {
        string normalizedLevel = NormalizeGeminiLevel(level, modelName);
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
        string normalizedLevel = NormalizeGeminiLevel(level, modelName);
        return normalizedLevel switch
        {
            Minimal => "minimal",
            Low => "low",
            Medium => "medium",
            High => "high",
            _ => null
        };
    }

    public static int? ToGeminiThinkingBudget(string level, string modelName)
    {
        return NormalizeGeminiLevel(level, modelName) switch
        {
            Off => 0,
            Minimal => 128,
            Low => 128,
            Medium => 512,
            High => 1024,
            _ => null
        };
    }

    public static string ToDeepSeekThinkingType(string level)
    {
        return Normalize(level) switch
        {
            Off => "disabled",
            Minimal or Low or Medium or High or XHigh or Max or Ultra => "enabled",
            _ => null
        };
    }

    /// <summary>
    /// OpenAI 推理模型判定：gpt-5/gpt-6 全系 + o 系（o1/o3/o4…）。仅匹配命名空间后的模型名。
    /// （段分隔符取 '/'，兼容 openai/o3-mini 这类网关前缀；边界为串尾或 '-'，
    /// 归一化已把 '.'/'_' 折为 '-'），避免误伤 gpt-4o、olmo、orca 等含字母 o 的普通模型名。
    /// </summary>
    public static bool IsOpenAiReasoningModel(string modelName)
    {
        if (IsGptFamily(modelName, "5") || IsGptFamily(modelName, "6"))
        {
            return true;
        }

        string segment = ModelLeaf(modelName);
        if (segment.Length >= 2
            && (segment[0] == 'o' || segment[0] == 'O')
            && segment[1] is >= '1' and <= '9'
            && (segment.Length == 2 || segment[2] is '-' or ':'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// chat/completions 输出上限字段名：推理模型（gpt-5/gpt-6 与 o 系）使用 max_completion_tokens
    /// （HTTP 400，要求 max_completion_tokens）；其余模型保持 max_tokens，
    /// 兼容端点（vLLM 等自建服务）上的普通模型名不受影响。
    /// </summary>
    public static string OpenAiMaxTokensFieldName(string modelName)
    {
        return IsOpenAiReasoningModel(modelName) ? "max_completion_tokens" : "max_tokens";
    }

    public static bool IsGeminiThinkingModel(string modelName)
    {
        return HasModelFamilyName(modelName, "gemini") || HasModelFamilyName(modelName, "gemini3");
    }

    public static bool IsGemini3Model(string modelName)
    {
        return HasModelFamilyName(modelName, "gemini-3") || HasModelFamilyName(modelName, "gemini3");
    }

    public static bool IsGeminiFlashModel(string modelName)
    {
        return ModelLeaf(modelName).Contains("flash", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGeminiProModel(string modelName)
    {
        return ModelLeaf(modelName).Contains("pro", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDeepSeekThinkingModel(string modelName)
    {
        return SupportsDeepSeekLowEffort(modelName)
            || HasModelFamilyName(modelName, "deepseek-reasoner")
            || HasModelFamilyName(modelName, "deepseek-r1");
    }

    public static bool SupportsDeepSeekLowEffort(string modelName)
    {
        return HasModelFamilyName(modelName, "deepseek-v4")
            || HasModelFamilyName(modelName, "deepseek-flash");
    }

    private static bool HasModelFamilyName(string modelName, string familyName)
    {
        string normalized = NormalizeModelName(modelName);
        // Gate on the model after any provider namespace, never a matching namespace itself.
        // Allow revision/variant suffixes, but not lookalikes such as v40 or flashlight.
        string model = normalized[(normalized.LastIndexOf('/') + 1)..];
        return model.StartsWith(familyName, StringComparison.OrdinalIgnoreCase)
            && (model.Length == familyName.Length || model[familyName.Length] is '-' or ':');
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
            string effort = ToOpenAiReasoningEffort(normalizedLevel, modelName);
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

            string effort = ToDeepSeekReasoningEffort(normalizedLevel, modelName);
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
            parts.Add($"thinking={ToCompactJson(thinking)}");
        }
        if (body["generationConfig"]?["thinkingConfig"] is JToken thinkingConfig)
        {
            parts.Add($"thinkingConfig={ToCompactJson(thinkingConfig)}");
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
            var error = json["error"] as JObject;
            string message = error?["message"]?.ToString()
                ?? json["message"]?.ToString()
                ?? json["error"]?.ToString()
                ?? string.Empty;
            string code = error?["code"]?.ToString() ?? json["code"]?.ToString() ?? string.Empty;
            string type = error?["type"]?.ToString() ?? json["type"]?.ToString() ?? string.Empty;

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
        DialogueServices.Monitor?.Log(Util.GetConsoleString(
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

    private static string ToCompactJson(JToken token)
    {
        return CompactJsonText(token.ToString());
    }

    private static string CompactJsonText(string json)
    {
        var result = new System.Text.StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;

        foreach (char ch in json)
        {
            if (escaped)
            {
                result.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                result.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                result.Append(ch);
                continue;
            }

            if (!inString && char.IsWhiteSpace(ch))
            {
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
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
