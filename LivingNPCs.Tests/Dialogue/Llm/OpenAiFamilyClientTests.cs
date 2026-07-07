using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class OpenAiFamilyClientTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}";

    [Fact]
    public async Task RequestBodyUsesSystemAndConcatenatedUserMessages()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Equal(13, reply.Usage.TotalTokens);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal("https://api.openai.com/v1/chat/completions", Http.Requests.Single().Url);
        Assert.Equal("gpt-4o", body.Value<string>("model"));
        Assert.Equal(2048, body.Value<int>("max_tokens"));
        var messages = (JArray)body["messages"]!;
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Value<string>("role"));
        Assert.Equal("SYS", messages[0].Value<string>("content"));
        Assert.Equal("user", messages[1].Value<string>("role"));
        Assert.Equal("WORLDNPCTAIL", messages[1].Value<string>("content"));
        Assert.Null(body["temperature"]);
        Assert.Null(body["top_p"]);
        Assert.Equal("Bearer test-key", Http.Requests.Single().Authorization);
    }

    [Fact]
    public async Task PromptCacheKeyIsStablePerNpcAndOnlySentByOpenAi()
    {
        var openAi = new OpenAiClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(CompletionJson);

        await openAi.CompleteAsync(Request(npc: "Abigail"), CancellationToken.None);
        await openAi.CompleteAsync(Request(npc: "Abigail"), CancellationToken.None);
        await openAi.CompleteAsync(Request(npc: "Sebastian"), CancellationToken.None);

        string? key1 = JObject.Parse(Http.Requests[0].Body!).Value<string>("prompt_cache_key");
        string? key2 = JObject.Parse(Http.Requests[1].Body!).Value<string>("prompt_cache_key");
        string? key3 = JObject.Parse(Http.Requests[2].Body!).Value<string>("prompt_cache_key");
        Assert.NotNull(key1);
        Assert.Matches("^livingnpcs-[0-9A-F]{16}$", key1);
        Assert.Equal(key1, key2);
        Assert.NotEqual(key1, key3);

        foreach (string provider in new[] { "DeepSeek", "Mistral", "OpenAiCompatible" })
        {
            Http.Requests.Clear();
            LlmClientBase other = LlmClientFactory.TryCreate(Settings(provider))!;
            await other.CompleteAsync(Request(), CancellationToken.None);
            Assert.Null(JObject.Parse(Http.Requests[0].Body!)["prompt_cache_key"]);
        }
    }

    [Fact]
    public async Task FastJsonChannelAddsResponseFormatWhenThinkingOff()
    {
        // 默认配置 RoutingThinkingLevel = off：快速通道请求带 response_format。
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.EnqueueJson(CompletionJson);
        await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None);
        Assert.Equal("json_object", JObject.Parse(Http.Requests[0].Body!)["response_format"]?.Value<string>("type"));

        // 主对话（DisableThinking=false）不带，即便档位是 off 也不污染正常对话。
        Http.Requests.Clear();
        Config.ChatThinkingLevel = "off";
        Http.EnqueueJson(CompletionJson);
        await client.CompleteAsync(Request(), CancellationToken.None);
        Assert.Null(JObject.Parse(Http.Requests[0].Body!)["response_format"]);
    }

    [Fact]
    public async Task ThinkingCandidateFallsBackToBareBodyAndWarnsOnce()
    {
        Config.ChatThinkingLevel = "High";
        var client = new OpenAiClient(Settings("OpenAI", modelName: "gpt-5.5"));
        Http.DefaultResponder = request =>
        {
            bool hasThinking = JObject.Parse(request.Body!)["reasoning_effort"] != null;
            return hasThinking
                ? FakeHttpHandler.Json("{\"error\":{\"message\":\"Unsupported parameter\"}}", HttpStatusCode.BadRequest)
                : FakeHttpHandler.Json(CompletionJson);
        };

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        // 思考参数候选独享 3 次预算，随后裸版本一次成功。
        Assert.Equal(4, Http.Requests.Count);
        Assert.All(Http.Requests.Take(3), r => Assert.Equal("high", JObject.Parse(r.Body!).Value<string>("reasoning_effort")));
        Assert.Null(JObject.Parse(Http.Requests[3].Body!)["reasoning_effort"]);
        Assert.Single(Monitor.Entries, e => e.Message.Contains("thinking parameters failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompatibleEndpointFallsBackToInstructionsForm()
    {
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", serverAddress: "https://gw.example/v1/"));
        Http.DefaultResponder = request =>
        {
            bool instructionsForm = JObject.Parse(request.Body!)["instructions"] != null;
            return instructionsForm
                ? FakeHttpHandler.Json(CompletionJson)
                : FakeHttpHandler.Json("bad gateway", HttpStatusCode.InternalServerError);
        };

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        // 标准形态 3 次耗尽后进入 instructions 回退形态。
        Assert.Equal(4, Http.Requests.Count);
        Assert.Equal("https://gw.example/v1/chat/completions", Http.Requests[0].Url);
        var fallbackBody = JObject.Parse(Http.Requests[3].Body!);
        Assert.Equal("SYS", fallbackBody.Value<string>("instructions"));
        var messages = (JArray)fallbackBody["messages"]!;
        Assert.Single(messages);
        Assert.Equal("user", messages[0].Value<string>("role"));
    }

    [Theory]
    [InlineData("https://openrouter.ai/api", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/chat/completions", "https://openrouter.ai/api")]
    [InlineData("https://openrouter.ai/api/v1/chat/completions/", "https://openrouter.ai/api")]
    [InlineData("http://localhost:8080/v1/", "http://localhost:8080")]
    public void ServerAddressIsNormalized(string configured, string expectedBase)
    {
        Assert.Equal(expectedBase, OpenAiChatClientBase.NormalizeBaseAddress(configured));
    }

    [Fact]
    public async Task NonStreamingSseReplyIsReassembled()
    {
        var client = new MistralClient(Settings("Mistral"));
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}],\"usage\":{\"prompt_tokens\":4,\"completion_tokens\":2,\"total_tokens\":6}}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello", reply.Text);
        Assert.Equal(6, reply.Usage.TotalTokens);
    }

    [Fact]
    public async Task LegacyCompletionsTextFieldIsAccepted()
    {
        var client = new MistralClient(Settings("Mistral"));
        Http.EnqueueJson("{\"choices\":[{\"text\":\"Old style\"}]}");

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Old style", reply.Text);
    }

    [Fact]
    public async Task BlankTextIsRetriedThenFails()
    {
        var client = new MistralClient(Settings("Mistral"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"choices\":[{\"message\":{\"content\":\"   \"}}]}");

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal(3, Http.Requests.Count);
        Assert.Equal(200, reply.HttpStatus);
    }

    [Fact]
    public async Task NoRetryBudgetMakesSingleAttempt()
    {
        var client = new MistralClient(Settings("Mistral"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("oops", HttpStatusCode.InternalServerError);

        LlmReply reply = await client.CompleteAsync(Request(allowRetry: false), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Single(Http.Requests);
        Assert.Equal(500, reply.HttpStatus);
    }

    [Fact]
    public async Task FailureCarriesHttpStatusFromExceptionChain()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"key\"}", HttpStatusCode.Unauthorized);

        LlmReply reply = await client.CompleteAsync(Request(allowRetry: false), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal(401, reply.HttpStatus);
        Assert.Contains("HTTP 401", reply.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelListRequiresApiKeyForOfficialEndpointsOnly()
    {
        var openAi = new OpenAiClient(Settings("OpenAI", apiKey: ""));
        Assert.Empty(openAi.GetModelNames());
        Assert.Empty(Http.Requests);

        var compatible = new OpenAiCompatibleClient(Settings("OpenAiCompatible", apiKey: ""));
        Http.EnqueueJson("{\"data\":[{\"id\":\"m1\"},{\"id\":\"m2\"}]}");
        Assert.Equal(new[] { "m1", "m2" }, compatible.GetModelNames());
        Assert.Equal("https://openrouter.ai/api/v1/models", Http.Requests.Single().Url);
        Assert.Equal(HttpMethod.Get, Http.Requests.Single().Method);
    }

    [Fact]
    public void ModelListErrorsAreLoggedAndReturnEmpty()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("nope", HttpStatusCode.InternalServerError);

        Assert.Empty(client.GetModelNames());
        Assert.True(Monitor.Contains(LogLevel.Error, "Failed to list models"));
    }
}
