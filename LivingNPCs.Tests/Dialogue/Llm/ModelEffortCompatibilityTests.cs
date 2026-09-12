using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class ModelEffortCompatibilityTests : LlmTestBase
{
    private const int OutputBudget = 10000;
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}";

    public static IEnumerable<object[]> ModernOpenAiEfforts()
    {
        (string Provider, string Model)[] models =
        [
            ("OpenAI", "gpt-6-astra"),
            ("OpenAI", "gpt-6"),
            ("OpenAI", "gpt-5.6"),
            ("OpenAI", "gpt-5.6-sol"),
            ("OpenAI", "gpt-5.6-terra"),
            ("OpenAI", "gpt-5.6-luna"),
            ("OpenAiCompatible", "openai/gpt-6-astra"),
            ("OpenAiCompatible", "gpt6-astra"),
            ("OpenAiCompatible", "openai/gpt-5.6-sol"),
            ("OpenAiCompatible", "gpt5.6-luna"),
            ("OpenAiCompatible", "OPENAI/GPT-5.6-TERRA")
        ];

        foreach ((string provider, string model) in models)
        {
            foreach (string level in new[] { LlmThinking.Max, LlmThinking.Ultra })
            {
                yield return [provider, model, level];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ModernOpenAiEfforts))]
    public async Task ModernOpenAiModelsSendMaxForMaxAndUltra(string provider, string model, string level)
    {
        Config.ChatThinkingLevel = level;

        JObject body = await CompleteAndReadBodyAsync(provider, model);

        Assert.Equal(model, body.Value<string>("model"));
        Assert.Equal("max", body.Value<string>("reasoning_effort"));
        Assert.Null(body["thinking"]);
        AssertOutputBudget(body, "max_completion_tokens");
    }

    [Theory]
    [InlineData("gpt-6-astra", LlmThinking.Off, "low")]
    [InlineData("gpt-6-astra", LlmThinking.Minimal, "low")]
    [InlineData("openai/gpt6-astra", LlmThinking.Off, "low")]
    [InlineData("gpt-5.6", LlmThinking.Off, "none")]
    [InlineData("gpt-5.6", LlmThinking.Minimal, "low")]
    [InlineData("openai/gpt-5.6-luna", LlmThinking.Off, "none")]
    public async Task OffAndMinimalRespectEachOpenAiModelsMinimumEffort(string model, string level, string expected)
    {
        Config.ChatThinkingLevel = level;

        JObject body = await CompleteAndReadBodyAsync("OpenAiCompatible", model);

        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        AssertOutputBudget(body, "max_completion_tokens");
    }

    [Theory]
    [InlineData("gpt-5.5", LlmThinking.Max, "xhigh")]
    [InlineData("gpt-5.5", LlmThinking.Ultra, "xhigh")]
    [InlineData("openai/gpt-5.5", LlmThinking.Max, "xhigh")]
    [InlineData("gpt-5.1", LlmThinking.XHigh, "high")]
    [InlineData("gpt-5.1", LlmThinking.Max, "high")]
    [InlineData("gpt-5.1", LlmThinking.Ultra, "high")]
    public async Task OlderOpenAiModelsReceiveTheirSupportedMaximum(string model, string level, string expected)
    {
        Config.ChatThinkingLevel = level;

        JObject body = await CompleteAndReadBodyAsync("OpenAiCompatible", model);

        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        AssertOutputBudget(body, "max_completion_tokens");
    }

    [Theory]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Minimal, "low")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Low, "low")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Medium, "high")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.High, "high")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.XHigh, "high")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Max, "max")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Ultra, "max")]
    [InlineData("DeepSeek", "deepseek-v4-pro", LlmThinking.XHigh, "high")]
    [InlineData("DeepSeek", "deepseek-v4-flash", LlmThinking.Ultra, "max")]
    [InlineData("OpenAiCompatible", "deepseek/deepseek-flash", LlmThinking.Max, "max")]
    [InlineData("OpenAiCompatible", "deepseek-ai/DeepSeek-V4-Pro", LlmThinking.Medium, "high")]
    [InlineData("OpenAiCompatible", "deepseek/deepseek-v4-flash:free", LlmThinking.XHigh, "high")]
    public async Task ModernDeepSeekRequestsUseLowHighOrMax(string provider, string model, string level, string expected)
    {
        Config.ChatThinkingLevel = level;

        JObject body = await CompleteAndReadBodyAsync(provider, model);

        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        Assert.Equal("enabled", body["thinking"]?.Value<string>("type"));
        AssertOutputBudget(body, "max_tokens");
    }

    [Theory]
    [InlineData("DeepSeek", "deepseek-flash")]
    [InlineData("OpenAiCompatible", "deepseek/deepseek-v4-pro")]
    public async Task DeepSeekOffExplicitlyDisablesThinking(string provider, string model)
    {
        Config.ChatThinkingLevel = LlmThinking.Off;

        JObject body = await CompleteAndReadBodyAsync(provider, model);

        Assert.Equal("disabled", body["thinking"]?.Value<string>("type"));
        Assert.Null(body["reasoning_effort"]);
        AssertOutputBudget(body, "max_tokens");
    }

    [Theory]
    [InlineData("deepseek-flashlight")]
    [InlineData("deepseek/deepseek-flashlight")]
    [InlineData("not-deepseek-flash")]
    [InlineData("deepseek-v40-flash")]
    [InlineData("deepseek-flash/other-model")]
    [InlineData("deepseek-v4-pro/other-model")]
    public async Task SimilarNamesAndProviderNamespacesDoNotEnableDeepSeekParameters(string model)
    {
        Config.ChatThinkingLevel = LlmThinking.Ultra;

        JObject body = await CompleteAndReadBodyAsync("OpenAiCompatible", model);

        Assert.Null(body["reasoning_effort"]);
        Assert.Null(body["thinking"]);
        AssertOutputBudget(body, "max_tokens");
    }

    [Theory]
    [InlineData("OpenAI", "gpt-6-astra", "max_completion_tokens")]
    [InlineData("OpenAiCompatible", "openai/gpt-5.6-sol", "max_completion_tokens")]
    [InlineData("DeepSeek", "deepseek-flash", "max_tokens")]
    [InlineData("OpenAiCompatible", "deepseek/deepseek-v4-pro", "max_tokens")]
    public async Task RoutingAndChatUseTheirOwnEffortWithoutReducingOutputBudget(string provider, string model, string tokenField)
    {
        Config.ChatThinkingLevel = LlmThinking.Ultra;
        Config.RoutingThinkingLevel = LlmThinking.Low;

        JObject routingBody = await CompleteAndReadBodyAsync(provider, model, routing: true);
        Http.Requests.Clear();
        JObject chatBody = await CompleteAndReadBodyAsync(provider, model);

        Assert.Equal("low", routingBody.Value<string>("reasoning_effort"));
        Assert.Equal("max", chatBody.Value<string>("reasoning_effort"));
        AssertOutputBudget(routingBody, tokenField);
        AssertOutputBudget(chatBody, tokenField);
    }

    [Theory]
    [InlineData("OpenAI", "gpt-6-astra", "max_completion_tokens", false)]
    [InlineData("OpenAI", "gpt-6-astra", "max_completion_tokens", true)]
    [InlineData("OpenAiCompatible", "openai/gpt-5.6-luna", "max_completion_tokens", false)]
    [InlineData("DeepSeek", "deepseek-flash", "max_tokens", false)]
    [InlineData("DeepSeek", "deepseek-flash", "max_tokens", true)]
    [InlineData("OpenAiCompatible", "deepseek/deepseek-v4-pro", "max_tokens", true)]
    public async Task AutoOmitsThinkingParametersInSelectedChannel(string provider, string model, string tokenField, bool routing)
    {
        Config.ChatThinkingLevel = routing ? LlmThinking.Ultra : LlmThinking.Auto;
        Config.RoutingThinkingLevel = routing ? LlmThinking.Auto : LlmThinking.Ultra;

        JObject body = await CompleteAndReadBodyAsync(provider, model, routing);

        Assert.Null(body["reasoning_effort"]);
        Assert.Null(body["thinking"]);
        AssertOutputBudget(body, tokenField);
    }

    [Theory]
    [InlineData("OpenAI", "gpt-6-astra", LlmThinking.Ultra, "max", "max_completion_tokens")]
    [InlineData("OpenAI", "gpt-6-astra", LlmThinking.Off, "low", "max_completion_tokens")]
    [InlineData("OpenAiCompatible", "openai/gpt-5.6-terra", LlmThinking.Off, "none", "max_completion_tokens")]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.XHigh, "high", "max_tokens")]
    [InlineData("OpenAiCompatible", "deepseek/deepseek-v4-pro", LlmThinking.Ultra, "max", "max_tokens")]
    public async Task StreamingPayloadPreservesChatEffortAndFullBudget(string provider, string model, string level, string expected, string tokenField)
    {
        Config.ChatThinkingLevel = level;
        Config.RoutingThinkingLevel = LlmThinking.Medium;
        LlmClientBase client = LlmClientFactory.TryCreate(Settings(provider, modelName: model))!;
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":\"stop\"}]}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(maxTokens: OutputBudget), CancellationToken.None));

        Assert.Contains(events, item => item.Kind == LlmStreamEventKind.Done);
        Assert.Equal("Hello", string.Concat(events.Where(item => item.Kind == LlmStreamEventKind.TextDelta).Select(item => item.Text)));
        var body = JObject.Parse(Assert.Single(Http.Requests).Body!);
        Assert.True(body.Value<bool>("stream"));
        Assert.Equal(expected, body.Value<string>("reasoning_effort"));
        AssertOutputBudget(body, tokenField);
    }

    private async Task<JObject> CompleteAndReadBodyAsync(string provider, string model, bool routing = false)
    {
        Config.UseStreamingDialogueTransport = false;
        LlmClientBase client = LlmClientFactory.TryCreate(Settings(provider, modelName: model))!;
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: routing, maxTokens: OutputBudget), CancellationToken.None);

        Assert.True(reply.IsSuccess, reply.ErrorMessage);
        Assert.Equal("Hello there", reply.Text);
        return JObject.Parse(Assert.Single(Http.Requests).Body!);
    }

    private static void AssertOutputBudget(JObject body, string tokenField)
    {
        Assert.Equal(OutputBudget, body.Value<int>(tokenField));
        Assert.Null(body[tokenField == "max_tokens" ? "max_completion_tokens" : "max_tokens"]);
    }
}
