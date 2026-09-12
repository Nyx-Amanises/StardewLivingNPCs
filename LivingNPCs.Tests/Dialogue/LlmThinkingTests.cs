using Newtonsoft.Json.Linq;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Diagnostics;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

public sealed class LlmThinkingTests
{
    [Theory]
    [InlineData("gpt-5.5", LlmThinking.Off, "none")]
    [InlineData("gpt-5.5", LlmThinking.Minimal, "low")]
    [InlineData("gpt-5.4", LlmThinking.Low, "low")]
    [InlineData("gpt5.5", LlmThinking.High, "high")]
    [InlineData("gpt-5.5", LlmThinking.XHigh, "xhigh")]
    public void GptReasoningModelsUseReasoningEffort(string model, string level, string expected)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, level);

        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        Assert.Null(body["thinking"]);
    }

    [Theory]
    [InlineData("deepseek-v4-pro", LlmThinking.Off, "disabled", "")]
    [InlineData("deepseek-v4-flash", LlmThinking.Minimal, "enabled", "low")]
    [InlineData("deepseek-v4-flash", LlmThinking.Low, "enabled", "low")]
    [InlineData("deepseek-v4-pro", LlmThinking.Low, "enabled", "low")]
    [InlineData("deepseek-ai/DeepSeek-V4-Flash", LlmThinking.Low, "enabled", "low")]
    [InlineData("deepseek-flash", LlmThinking.Off, "disabled", "")]
    [InlineData("deepseek-flash", LlmThinking.Minimal, "enabled", "low")]
    [InlineData("deepseek-flash", LlmThinking.Low, "enabled", "low")]
    [InlineData("proxy/deepseek-ai/DeepSeek_Flash", LlmThinking.Low, "enabled", "low")]
    [InlineData("deepseek/deepseek-flash:free", LlmThinking.Minimal, "enabled", "low")]
    [InlineData("deepseek-flash-20260912", LlmThinking.Low, "enabled", "low")]
    [InlineData("deepseek-flash", LlmThinking.Medium, "enabled", "high")]
    [InlineData("deepseek-flash", LlmThinking.High, "enabled", "high")]
    [InlineData("deepseek-flash", LlmThinking.XHigh, "enabled", "high")]
    [InlineData("deepseek-v4-flash", LlmThinking.Medium, "enabled", "high")]
    [InlineData("deepseek-reasoner", LlmThinking.Minimal, "enabled", "high")]
    [InlineData("deepseek-reasoner", LlmThinking.Low, "enabled", "high")]
    [InlineData("deepseek-r1", LlmThinking.Low, "enabled", "high")]
    [InlineData("deepseek-ai/DeepSeek-R1-Distill-Qwen-32B", LlmThinking.Minimal, "enabled", "high")]
    [InlineData("deepseek/deepseek-r1:free", LlmThinking.Low, "enabled", "high")]
    [InlineData("deepseek-reasoner", LlmThinking.High, "enabled", "high")]
    [InlineData("deepseek-v4-pro", LlmThinking.XHigh, "enabled", "high")]
    public void DeepSeekThinkingModelsUseOfficialThinkingShape(string model, string level, string expectedType, string expectedEffort)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, level);

        Assert.Equal(expectedType, body["thinking"]?.Value<string>("type"));
        Assert.Equal(string.IsNullOrEmpty(expectedEffort) ? null : expectedEffort, body.Value<string>("reasoning_effort"));
        Assert.Null(body["enable_thinking"]);
    }

    [Theory]
    [InlineData("deepseek-v4", true)]
    [InlineData("DeepSeek-V4-Flash", true)]
    [InlineData("deepseek-ai/deepseek_v4_pro", true)]
    [InlineData("deepseek-flash", true)]
    [InlineData("gateway/DeepSeek.Flash", true)]
    [InlineData("deepseek-flash-20260912", true)]
    [InlineData("deepseek/deepseek-flash:free", true)]
    [InlineData("deepseek-v40-flash", false)]
    [InlineData("deepseek-flashlight", false)]
    [InlineData("not-deepseek-flash", false)]
    [InlineData("not-deepseek-v4", false)]
    [InlineData("deepseek-flash/other-model", false)]
    [InlineData("deepseek-v3.1", false)]
    [InlineData("deepseek-r1", false)]
    [InlineData("deepseek-reasoner", false)]
    public void DeepSeekLowEffortCapabilityUsesKnownModelFamilies(string model, bool expected)
    {
        Assert.Equal(expected, LlmThinking.SupportsDeepSeekLowEffort(model));
    }

    [Theory]
    [InlineData("other-flash")]
    [InlineData("deepseek-flashlight")]
    [InlineData("not-deepseek-flash")]
    [InlineData("deepseek-flash/other-model")]
    [InlineData("deepseek-v40-flash")]
    [InlineData("not-deepseek-v4")]
    [InlineData("deepseek-reasonerish")]
    [InlineData("deepseek-r10")]
    public void LookalikeNamesDoNotGetDeepSeekThinkingParameters(string model)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, LlmThinking.Low);

        Assert.Empty(body);
        Assert.False(LlmThinking.IsDeepSeekThinkingModel(model));
    }

    [Theory]
    [InlineData(LlmThinking.Off, "gemini-3.5-flash", "minimal")]
    [InlineData(LlmThinking.Minimal, "gemini-3.5-flash", "minimal")]
    [InlineData(LlmThinking.Off, "gemini-3.1-pro", "low")]
    [InlineData(LlmThinking.Medium, "gemini-3.1-pro", "medium")]
    [InlineData(LlmThinking.High, "gemini-3.5-flash", "high")]
    [InlineData(LlmThinking.XHigh, "gemini-3.5-flash", "high")]
    public void Gemini3ThinkingLevelsUseThinkingLevel(string level, string model, string expected)
    {
        JObject config = LlmThinking.BuildGeminiThinkingConfig(level, model);

        Assert.Equal(expected, config?.Value<string>("thinkingLevel"));
        Assert.Null(config?["thinkingBudget"]);
    }

    [Theory]
    [InlineData(LlmThinking.Off, "gemini-2.5-flash", 0)]
    [InlineData(LlmThinking.Off, "gemini-2.5-pro", 128)]
    [InlineData(LlmThinking.Minimal, "gemini-2.5-flash", 128)]
    [InlineData(LlmThinking.Medium, "gemini-2.5-flash", 512)]
    [InlineData(LlmThinking.XHigh, "gemini-2.5-flash", 1024)]
    public void Gemini25ThinkingLevelsUseThinkingBudget(string level, string model, int expected)
    {
        JObject config = LlmThinking.BuildGeminiThinkingConfig(level, model);

        Assert.Equal(expected, config?.Value<int>("thinkingBudget"));
        Assert.Null(config?["thinkingLevel"]);
    }

    [Theory]
    [InlineData(LlmThinking.Off, "gemini-3.5-flash", "minimal")]
    [InlineData(LlmThinking.Minimal, "gemini-3.5-flash", "minimal")]
    [InlineData(LlmThinking.Off, "gemini-3.1-pro", "low")]
    [InlineData(LlmThinking.Off, "gemini-2.5-flash", "none")]
    [InlineData(LlmThinking.High, "gemini-3.1-pro", "high")]
    [InlineData(LlmThinking.XHigh, "gemini-3.5-flash", "high")]
    public void OpenAiCompatibleGeminiModelsUseReasoningEffort(string level, string model, string expected)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, level);

        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        Assert.Null(body["thinking"]);
    }

    [Fact]
    public void ThinkingOptionsExposeAllCommonReasoningEfforts()
    {
        Assert.Contains(LlmThinking.Auto, LlmThinking.Options);
        Assert.Contains(LlmThinking.Off, LlmThinking.Options);
        Assert.Contains(LlmThinking.Minimal, LlmThinking.Options);
        Assert.Contains(LlmThinking.Low, LlmThinking.Options);
        Assert.Contains(LlmThinking.Medium, LlmThinking.Options);
        Assert.Contains(LlmThinking.High, LlmThinking.Options);
        Assert.Contains(LlmThinking.XHigh, LlmThinking.Options);
    }

    [Theory]
    [InlineData("gpt-5.5")]
    [InlineData("deepseek-flash")]
    [InlineData("deepseek-v4-flash")]
    [InlineData("deepseek-reasoner")]
    public void AutoDoesNotAddOpenAiCompatibleThinkingParameters(string model)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, LlmThinking.Auto);

        Assert.Empty(body);
    }

    [Fact]
    public void DescribeThinkingParametersIncludesSupportedShapes()
    {
        var body = new JObject
        {
            ["reasoning_effort"] = "high",
            ["thinking"] = new JObject { ["type"] = "enabled" }
        };

        string description = LlmThinking.DescribeThinkingParameters(body);

        Assert.Contains("reasoning_effort=high", description);
        Assert.Contains("thinking={\"type\":\"enabled\"}", description);
    }

    [Fact]
    public void SummarizeProviderErrorExtractsErrorMessage()
    {
        string error = "{\"error\":{\"message\":\"Unsupported parameter: reasoning_effort\",\"code\":\"bad_request\",\"type\":\"invalid_request_error\"}}";

        string summary = LlmThinking.SummarizeProviderError(error);

        Assert.Contains("Unsupported parameter: reasoning_effort", summary);
        Assert.Contains("code=bad_request", summary);
        Assert.Contains("type=invalid_request_error", summary);
    }

    [Fact]
    public void AutoDoesNotBuildGeminiThinkingConfig()
    {
        Assert.Null(LlmThinking.BuildGeminiThinkingConfig(LlmThinking.Auto, "gemini-3.5-flash"));
        Assert.Null(LlmThinking.BuildGeminiThinkingConfig(LlmThinking.Auto, "gemini-2.5-flash"));
    }
}
