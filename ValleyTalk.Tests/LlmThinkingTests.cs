using Newtonsoft.Json.Linq;
using ValleyTalk;
using Xunit;

namespace ValleyTalk.Tests;

public sealed class LlmThinkingTests
{
    [Theory]
    [InlineData("gpt-5.5", LlmThinking.Off, "none")]
    [InlineData("gpt-5.5", LlmThinking.Minimal, "minimal")]
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
    [InlineData("deepseek-v4-flash", LlmThinking.Minimal, "enabled", "high")]
    [InlineData("deepseek-v4-flash", LlmThinking.Low, "enabled", "high")]
    [InlineData("deepseek-reasoner", LlmThinking.High, "enabled", "high")]
    [InlineData("deepseek-v4-pro", LlmThinking.XHigh, "enabled", "max")]
    public void DeepSeekThinkingModelsUseOfficialThinkingShape(string model, string level, string expectedType, string expectedEffort)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, level);

        Assert.Equal(expectedType, body["thinking"]?.Value<string>("type"));
        Assert.Equal(string.IsNullOrEmpty(expectedEffort) ? null : expectedEffort, body.Value<string>("reasoning_effort"));
        Assert.Null(body["enable_thinking"]);
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
    [InlineData(LlmThinking.XHigh, "gemini-3.5-flash", "xhigh")]
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

    [Fact]
    public void AutoDoesNotAddOpenAiCompatibleThinkingParameters()
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, "gpt-5.5", LlmThinking.Auto);

        Assert.Empty(body);
    }

    [Fact]
    public void AutoDoesNotBuildGeminiThinkingConfig()
    {
        Assert.Null(LlmThinking.BuildGeminiThinkingConfig(LlmThinking.Auto, "gemini-3.5-flash"));
        Assert.Null(LlmThinking.BuildGeminiThinkingConfig(LlmThinking.Auto, "gemini-2.5-flash"));
    }
}