using Newtonsoft.Json.Linq;
using ValleyTalk;
using Xunit;

namespace ValleyTalk.Tests;

public sealed class LlmThinkingTests
{
    [Theory]
    [InlineData("gpt-5.5", LlmThinking.Off, "none")]
    [InlineData("gpt-5.4", LlmThinking.Low, "low")]
    [InlineData("gpt5.5", LlmThinking.High, "high")]
    public void GptReasoningModelsUseReasoningEffort(string model, string level, string expected)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, level);

        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        Assert.Null(body["enable_thinking"]);
    }

    [Theory]
    [InlineData("deepseek-v4-pro", LlmThinking.Off, false)]
    [InlineData("deepseek-v4-flash", LlmThinking.Low, true)]
    [InlineData("deepseek-reasoner", LlmThinking.High, true)]
    public void DeepSeekThinkingModelsUseEnableThinking(string model, string level, bool expected)
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, model, level);

        Assert.Equal(expected, body.Value<bool>("enable_thinking"));
        Assert.Null(body["reasoning_effort"]);
    }

    [Theory]
    [InlineData(LlmThinking.Off, "gemini-3.5-flash", 0)]
    [InlineData(LlmThinking.Auto, "gemini-3.5-flash", 0)]
    [InlineData(LlmThinking.Auto, "gemini-3.1-pro", 128)]
    [InlineData(LlmThinking.Medium, "gemini-3.1-pro", 512)]
    [InlineData(LlmThinking.High, "gemini-3.5-flash", 1024)]
    public void GeminiThinkingLevelsMapToBudgets(string level, string model, int expected)
    {
        Assert.Equal(expected, LlmThinking.ToGeminiThinkingBudget(level, model));
    }

    [Fact]
    public void AutoDoesNotAddOpenAiCompatibleThinkingParameters()
    {
        var body = new JObject();

        LlmThinking.AddOpenAiCompatibleThinkingParameters(body, "gpt-5.5", LlmThinking.Auto);

        Assert.Empty(body);
    }
}
