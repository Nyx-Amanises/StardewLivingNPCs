using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>Claude 的手动预算与 adaptive/effort 协议按已验证模型分开，未知模型不猜测能力。</summary>
internal static class ClaudeThinking
{
    private const int MinimumBudgetTokens = 1024;

    private enum ThinkingSupport
    {
        None,
        Manual,
        ManualWithEffort,
        Adaptive,
        AdaptiveAlwaysOn,
        AdaptiveWithXHigh,
        AdaptiveWithXHighAlwaysOn
    }

    public static string[] GetOptions(string modelName)
    {
        ThinkingSupport support = GetSupport(modelName);
        var options = new List<string> { LlmThinking.Auto };
        if (support == ThinkingSupport.None)
        {
            return options.ToArray();
        }

        if (CanDisable(support))
        {
            options.Add(LlmThinking.Off);
        }

        options.AddRange([LlmThinking.Low, LlmThinking.Medium, LlmThinking.High]);
        if (SupportsXHigh(support))
        {
            options.Add(LlmThinking.XHigh);
        }

        if (IsAdaptive(support))
        {
            options.Add(LlmThinking.Max);
        }

        return options.ToArray();
    }

    public static string NormalizeLevel(string level, string modelName)
    {
        string normalized = LlmThinking.Normalize(level);
        ThinkingSupport support = GetSupport(modelName);
        if (support == ThinkingSupport.None)
        {
            return LlmThinking.Auto;
        }

        return normalized switch
        {
            LlmThinking.Off => CanDisable(support) ? LlmThinking.Off : LlmThinking.Low,
            LlmThinking.Minimal => LlmThinking.Low,
            LlmThinking.XHigh => SupportsXHigh(support) ? LlmThinking.XHigh
                : IsAdaptive(support) ? LlmThinking.Max : LlmThinking.High,
            LlmThinking.Max or LlmThinking.Ultra => IsAdaptive(support) ? LlmThinking.Max : LlmThinking.High,
            _ => normalized
        };
    }

    public static void AddRequestParameters(JObject body, string level, string modelName, int maxTokens)
    {
        string normalized = NormalizeLevel(level, modelName);
        if (normalized == LlmThinking.Auto)
        {
            return;
        }

        if (normalized == LlmThinking.Off)
        {
            body["thinking"] = new JObject { ["type"] = "disabled" };
            return;
        }

        ThinkingSupport support = GetSupport(modelName);
        if (IsAdaptive(support))
        {
            // Claude 4.7+ rejects the old enabled/budget_tokens shape.
            body["thinking"] = new JObject { ["type"] = "adaptive" };
        }
        else
        {
            // Manual thinking requires >=1024 tokens and budget_tokens < max_tokens.
            // Never enlarge a caller's total output allowance just to enable thinking.
            if (maxTokens <= MinimumBudgetTokens)
            {
                body["thinking"] = new JObject { ["type"] = "disabled" };
                return;
            }

            int desiredBudget = normalized switch
            {
                LlmThinking.Low => MinimumBudgetTokens,
                LlmThinking.Medium => 4096,
                _ => 8192
            };
            int responseReserve = Math.Min(1024, maxTokens / 4);
            int maximumBudget = Math.Max(MinimumBudgetTokens, maxTokens - responseReserve);
            body["thinking"] = new JObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = Math.Min(desiredBudget, maximumBudget)
            };
        }

        if (IsAdaptive(support) || support == ThinkingSupport.ManualWithEffort)
        {
            var outputConfig = body["output_config"] as JObject ?? new JObject();
            outputConfig["effort"] = normalized.ToLowerInvariant();
            body["output_config"] = outputConfig;
        }
    }

    private static bool IsAdaptive(ThinkingSupport support)
    {
        return support is ThinkingSupport.Adaptive or ThinkingSupport.AdaptiveAlwaysOn
            or ThinkingSupport.AdaptiveWithXHigh or ThinkingSupport.AdaptiveWithXHighAlwaysOn;
    }

    private static bool SupportsXHigh(ThinkingSupport support)
    {
        return support is ThinkingSupport.AdaptiveWithXHigh or ThinkingSupport.AdaptiveWithXHighAlwaysOn;
    }

    private static bool CanDisable(ThinkingSupport support)
    {
        return support is not (ThinkingSupport.AdaptiveAlwaysOn or ThinkingSupport.AdaptiveWithXHighAlwaysOn);
    }

    private static ThinkingSupport GetSupport(string modelName)
    {
        // Match the model after a provider namespace, including date and -latest suffixes.
        string model = (string.IsNullOrWhiteSpace(modelName) ? "claude-haiku-4-5" : modelName.Trim())
            .ToLowerInvariant().Replace('.', '-').Replace('_', '-');
        model = model[(model.LastIndexOf('/') + 1)..];
        model = model.Split(':')[0];
        string[] parts = model.Split('-');
        if (parts.Length < 3 || parts[0] != "claude")
        {
            return ThinkingSupport.None;
        }

        if (parts[1] == "mythos" && parts[2] == "preview")
        {
            return ThinkingSupport.AdaptiveAlwaysOn;
        }

        if (parts.Length >= 4 && parts[1] == "3" && parts[2] == "7" && parts[3] == "sonnet")
        {
            return ThinkingSupport.Manual;
        }

        if (!int.TryParse(parts[2], out int major))
        {
            return ThinkingSupport.None;
        }

        // Eight-digit release dates are suffixes, not minor versions.
        int minor = 0;
        if (parts.Length > 3)
        {
            if (int.TryParse(parts[3], out int parsedMinor))
            {
                minor = parts[3].Length == 8 ? 0 : parsedMinor;
            }
            else if (parts[3] != "latest")
            {
                return ThinkingSupport.None;
            }
        }

        // Verified 2026-09-12: https://platform.claude.com/docs/en/build-with-claude/effort
        // https://platform.claude.com/docs/en/build-with-claude/thinking-troubleshooting
        return (parts[1], major, minor) switch
        {
            ("fable" or "mythos", 5, 0 or 1) => ThinkingSupport.AdaptiveWithXHighAlwaysOn,
            ("opus" or "sonnet", 5, 0) => ThinkingSupport.AdaptiveWithXHigh,
            ("opus", 4, 7 or 8) => ThinkingSupport.AdaptiveWithXHigh,
            ("opus" or "sonnet", 4, 6) => ThinkingSupport.Adaptive,
            ("opus", 4, 5) => ThinkingSupport.ManualWithEffort,
            ("sonnet" or "haiku", 4, 5) => ThinkingSupport.Manual,
            ("opus", 4, 0 or 1) => ThinkingSupport.Manual,
            ("sonnet", 4, 0) => ThinkingSupport.Manual,
            _ => ThinkingSupport.None
        };
    }
}
