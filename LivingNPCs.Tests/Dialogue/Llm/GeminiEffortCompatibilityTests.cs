using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class GeminiEffortCompatibilityTests : LlmTestBase
{
    private const string Reply = "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"Hello\"}]}}]}";

    [Theory]
    [InlineData("gemini-3.8-flash", LlmThinking.Off, "low")]
    [InlineData("gemini-3.7-flash", LlmThinking.Minimal, "low")]
    [InlineData("gemini-3.8-flash", LlmThinking.Medium, "medium")]
    [InlineData("gemini-3.8-flash", LlmThinking.Ultra, "high")]
    [InlineData("gemini-3.6-flash", LlmThinking.Minimal, "low")]
    [InlineData("gemini-3.5-flash-lite", LlmThinking.Off, "minimal")]
    [InlineData("gemini-3-flash-preview", LlmThinking.Minimal, "low")]
    [InlineData("gemini-3.1-pro-preview", LlmThinking.Medium, "medium")]
    [InlineData("gemini-3-pro-preview", LlmThinking.Medium, "high")]
    [InlineData("gemini-3-pro-preview", LlmThinking.Minimal, "low")]
    [InlineData("gemini-3.1-flash-lite", LlmThinking.Minimal, "low")]
    [InlineData("gemini-3.1-flash-lite-image", LlmThinking.Low, "minimal")]
    [InlineData("gemini-3.1-flash-lite-image", LlmThinking.Medium, "high")]
    public async Task NativeGemini3UsesOnlyTheModelsSupportedThinkingLevel(string model, string level, string expected)
    {
        Config.ThinkingLevel = level;
        var client = new GeminiClient(Settings("Google", modelName: model));
        Http.EnqueueJson(Reply);

        var reply = await client.CompleteAsync(Request(maxTokens: 10_000), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var generation = JObject.Parse(Http.Requests.Single().Body!)["generationConfig"]!;
        Assert.Equal(10_000, generation.Value<int>("maxOutputTokens"));
        Assert.Equal(expected, generation["thinkingConfig"]!.Value<string>("thinkingLevel"));
        Assert.Null(generation["thinkingConfig"]!["thinkingBudget"]);
    }

    [Theory]
    [InlineData("gemini-2.5-flash", LlmThinking.Off, 0)]
    [InlineData("gemini-2.5-flash-lite", LlmThinking.Off, 0)]
    [InlineData("gemini-2.5-pro", LlmThinking.Off, 128)]
    [InlineData("gemini-2.5-flash", LlmThinking.Medium, 512)]
    [InlineData("gemini-2.5-pro", LlmThinking.Ultra, 1024)]
    public async Task NativeGemini25KeepsItsExistingBudgetProtocol(string model, string level, int expectedBudget)
    {
        Config.ThinkingLevel = level;
        var client = new GeminiClient(Settings("Google", modelName: model));
        Http.EnqueueJson(Reply);

        Assert.True((await client.CompleteAsync(Request(maxTokens: 10_000), CancellationToken.None)).IsSuccess);

        var thinking = JObject.Parse(Http.Requests.Single().Body!)["generationConfig"]!["thinkingConfig"]!;
        Assert.Equal(expectedBudget, thinking.Value<int>("thinkingBudget"));
        Assert.Null(thinking["thinkingLevel"]);
    }

    [Theory]
    [InlineData("google/gemini-3.8-flash", LlmThinking.Off, "low")]
    [InlineData("gemini-3.7-flash", LlmThinking.Ultra, "high")]
    [InlineData("gemini-3-pro-preview", LlmThinking.Medium, "high")]
    [InlineData("gemini-3.1-flash-lite-image", LlmThinking.Low, "minimal")]
    public async Task CompatibleEndpointUsesMatchingReasoningEffort(string model, string level, string expected)
    {
        Config.ThinkingLevel = level;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: model));
        Http.EnqueueJson("{\"choices\":[{\"message\":{\"content\":\"Hello\"}}]}");

        Assert.True((await client.CompleteAsync(Request(maxTokens: 10_000), CancellationToken.None)).IsSuccess);

        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        Assert.Null(body["thinking"]);
        Assert.Equal(10_000, body.Value<int>("max_tokens"));
    }

    [Theory]
    [InlineData("gemini-3.8-flash", LlmThinking.Auto)]
    [InlineData("gemini-2.5-pro", LlmThinking.Auto)]
    [InlineData("gemini-2.0-flash", LlmThinking.High)]
    public async Task AutoAndModelsWithoutThinkingDoNotReceiveThinkingConfig(string model, string level)
    {
        Config.ThinkingLevel = level;
        var client = new GeminiClient(Settings("Google", modelName: model));
        Http.EnqueueJson(Reply);

        Assert.True((await client.CompleteAsync(Request(), CancellationToken.None)).IsSuccess);

        Assert.Null(JObject.Parse(Http.Requests.Single().Body!)["generationConfig"]!["thinkingConfig"]);
    }

    [Theory]
    [InlineData("gemini-3.6-flash")]
    [InlineData("gemini-3-flash-preview")]
    [InlineData("gemini-3.1-flash-lite")]
    public void NativeMinimalRemainsAvailableInsideTheProviderAdapter(string model)
    {
        JObject thinking = LlmThinking.BuildGeminiThinkingConfig(LlmThinking.Minimal, model);

        Assert.Equal("minimal", thinking.Value<string>("thinkingLevel"));
        Assert.Null(thinking["thinkingBudget"]);
    }

    [Theory]
    [InlineData(LlmThinking.Auto, null)]
    [InlineData(LlmThinking.Low, "low")]
    [InlineData(LlmThinking.High, "high")]
    public async Task BackgroundAndDialogueUseTheSameGeminiLevel(string level, string? expected)
    {
        Config.ThinkingLevel = level;
        var client = new GeminiClient(Settings("Google", modelName: "gemini-3.8-flash"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(Reply);

        Assert.True((await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None)).IsSuccess);
        Assert.True((await client.CompleteAsync(Request(), CancellationToken.None)).IsSuccess);

        Assert.Equal(2, Http.Requests.Count);
        Assert.All(Http.Requests, request => Assert.Equal(expected,
            JObject.Parse(request.Body!)["generationConfig"]!["thinkingConfig"]?.Value<string>("thinkingLevel")));
    }
}
