using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class ClaudeClientTests : LlmTestBase
{
    private const string ReplyJson = "{\"content\":[{\"type\":\"text\",\"text\":\"Greetings, farmer!\"}],\"usage\":{\"input_tokens\":8,\"output_tokens\":5}}";

    [Fact]
    public async Task RequestUsesApiKeyHeadersInsteadOfBearer()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson(ReplyJson);

        await client.CompleteAsync(Request(), CancellationToken.None);

        FakeHttpHandler.RecordedRequest request = Http.Requests.Single();
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Url);
        Assert.Null(request.Authorization);
        Assert.Equal("test-key", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
    }

    [Fact]
    public async Task CacheBreakpointsCoverStableWorldAndNpcSegments()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson(ReplyJson);

        await client.CompleteAsync(Request(system: "SYS", stable: "WORLD", npc: "BIO", tail: "NOW"), CancellationToken.None);

        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal("claude-haiku-4-5", body.Value<string>("model"));

        var system = (JArray)body["system"]!;
        Assert.Equal(2, system.Count);
        Assert.Equal("SYS", system[0].Value<string>("text"));
        Assert.Null(system[0]["cache_control"]);
        Assert.Equal("WORLD", system[1].Value<string>("text"));
        Assert.Equal("ephemeral", system[1]["cache_control"]!.Value<string>("type"));

        var content = (JArray)body["messages"]![0]!["content"]!;
        Assert.Equal(2, content.Count);
        Assert.Equal("BIO", content[0].Value<string>("text"));
        Assert.Equal("ephemeral", content[0]["cache_control"]!.Value<string>("type"));
        Assert.Equal("NOW", content[1].Value<string>("text"));
        Assert.Null(content[1]["cache_control"]);
    }

    [Fact]
    public async Task EmptyNpcSegmentSendsPlainStringContent()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson(ReplyJson);

        await client.CompleteAsync(Request(npc: "", tail: "NOW"), CancellationToken.None);

        var body = JObject.Parse(Http.Requests.Single().Body!);
        JToken content = body["messages"]![0]!["content"]!;
        Assert.Equal(JTokenType.String, content.Type);
        Assert.Equal("NOW", content.ToString());
    }

    [Theory]
    [InlineData(0, 0, 8, 8, 0, 0)]      // 无缓存：全部记在 input_tokens
    [InlineData(100, 0, 8, 108, 0, 100)] // 首次写入：cache_creation 计入 prompt 且记加价写入
    [InlineData(0, 200, 8, 208, 200, 0)] // 全缓存命中：cache_read 计入 prompt 且记命中
    public async Task UsageAddsCacheCreationAndCacheReadToPromptTokens(
        int cacheCreation,
        int cacheRead,
        int inputTokens,
        int expectedPrompt,
        int expectedCached,
        int expectedCacheWrite)
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson(
            "{\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}],\"usage\":{"
            + $"\"input_tokens\":{inputTokens},\"output_tokens\":5,"
            + $"\"cache_creation_input_tokens\":{cacheCreation},\"cache_read_input_tokens\":{cacheRead}}}}}");

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.Equal(expectedPrompt, reply.Usage.PromptTokens);
        Assert.Equal(expectedCached, reply.Usage.CachedPromptTokens);
        Assert.Equal(expectedCacheWrite, reply.Usage.CacheWritePromptTokens);
        Assert.Equal(expectedPrompt + 5, reply.Usage.TotalTokens);
    }

    [Fact]
    public async Task BlankTextRetriesWithinBudget()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"content\":[{\"type\":\"text\",\"text\":\"\"}]}");

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal(3, Http.Requests.Count);
    }

    [Fact]
    public void ModelListUsesSameHeadersAndParsesDataIds()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson("{\"data\":[{\"id\":\"claude-haiku-4-5\"},{\"id\":\"claude-sonnet-4-6\"}]}");

        Assert.Equal(new[] { "claude-haiku-4-5", "claude-sonnet-4-6" }, client.GetModelNames());
        FakeHttpHandler.RecordedRequest request = Http.Requests.Single();
        Assert.Equal("https://api.anthropic.com/v1/models", request.Url);
        Assert.Equal("test-key", request.Headers["x-api-key"]);

        Http.Requests.Clear();
        var keyless = new ClaudeClient(Settings("Anthropic", apiKey: ""));
        Assert.Empty(keyless.GetModelNames());
        Assert.Empty(Http.Requests);
    }
}

[Collection("LlmLayer")]
public sealed class GeminiClientTests : LlmTestBase
{
    private static string Success(string text)
    {
        return "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"" + text + "\"}]}}],"
            + "\"usageMetadata\":{\"promptTokenCount\":20,\"candidatesTokenCount\":6,\"totalTokenCount\":26,\"thoughtsTokenCount\":2,\"cachedContentTokenCount\":10}}";
    }

    [Fact]
    public async Task AuthGoesThroughUrlQueryAndBodyCarriesSafetyAndSampling()
    {
        var client = new GeminiClient(Settings("Google"));
        Http.EnqueueJson(Success("Hi"));

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        FakeHttpHandler.RecordedRequest request = Http.Requests.Single();
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key=test-key",
            request.Url);
        Assert.Null(request.Authorization);

        var body = JObject.Parse(request.Body!);
        var safety = (JArray)body["safetySettings"]!;
        Assert.Equal(2, safety.Count);
        Assert.Equal("HARM_CATEGORY_SEXUALLY_EXPLICIT", safety[0].Value<string>("category"));
        Assert.Equal("BLOCK_NONE", safety[0].Value<string>("threshold"));
        Assert.Equal("HARM_CATEGORY_HARASSMENT", safety[1].Value<string>("category"));
        Assert.Equal("BLOCK_MEDIUM_AND_ABOVE", safety[1].Value<string>("threshold"));
        Assert.Equal("SYS", body["system_instruction"]!["parts"]![0]!.Value<string>("text"));
        Assert.Equal("WORLDNPCTAIL", body["contents"]![0]!["parts"]![0]!.Value<string>("text"));
        Assert.Equal(1.5, body["generationConfig"]!.Value<double>("temperature"));
        Assert.Equal(0.9, body["generationConfig"]!.Value<double>("topP"));
        Assert.Equal(2048, body["generationConfig"]!.Value<int>("maxOutputTokens"));

        // usage 口径：cachedContentTokenCount 已含在 promptTokenCount 内，不重复相加。
        Assert.Equal(20, reply.Usage.PromptTokens);
        Assert.Equal(10, reply.Usage.CachedPromptTokens);
        Assert.Equal(2, reply.Usage.ReasoningTokens);
        Assert.Equal(26, reply.Usage.TotalTokens);
    }

    [Fact]
    public async Task NonStopFinishReasonRetriesThenBareCandidateSucceeds()
    {
        // DisableThinking=true + 默认路由档位 off → thinkingConfig 候选 + 裸候选。
        var client = new GeminiClient(Settings("Google"));
        Http.DefaultResponder = request =>
        {
            bool hasThinking = JObject.Parse(request.Body!)["generationConfig"]!["thinkingConfig"] != null;
            return hasThinking
                ? FakeHttpHandler.Json("{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[{\"text\":\"cut\"}]}}]}")
                : FakeHttpHandler.Json(Success("clean"));
        };

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("clean", reply.Text);
        Assert.Equal(4, Http.Requests.Count);
        Assert.All(Http.Requests.Take(3), r => Assert.NotNull(JObject.Parse(r.Body!)["generationConfig"]!["thinkingConfig"]));
        Assert.Null(JObject.Parse(Http.Requests[3].Body!)["generationConfig"]!["thinkingConfig"]);
        Assert.Contains(Monitor.Entries, e => e.Message.Contains("thinking parameters failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyTextWith200AbortsCandidateWithoutRetry()
    {
        var client = new GeminiClient(Settings("Google"));
        int thinkingCalls = 0;
        Http.DefaultResponder = request =>
        {
            bool hasThinking = JObject.Parse(request.Body!)["generationConfig"]!["thinkingConfig"] != null;
            if (hasThinking)
            {
                thinkingCalls++;
                return FakeHttpHandler.Json("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"\"}]}}]}");
            }

            return FakeHttpHandler.Json(Success("ok"));
        };

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        // 空文本 200 不再重试该候选：思考候选只打了一次。
        Assert.Equal(1, thinkingCalls);
        Assert.Equal(2, Http.Requests.Count);
    }

    [Fact]
    public async Task FastJsonChannelSetsResponseMimeTypeAndChatDoesNot()
    {
        var client = new GeminiClient(Settings("Google"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(Success("ok"));

        await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None);
        Assert.Equal(
            "application/json",
            JObject.Parse(Http.Requests[0].Body!)["generationConfig"]!.Value<string>("responseMimeType"));

        Http.Requests.Clear();
        await client.CompleteAsync(Request(), CancellationToken.None);
        Assert.Null(JObject.Parse(Http.Requests[0].Body!)["generationConfig"]!["responseMimeType"]);
        // 聊天档位 auto → 无 thinkingConfig，单候选。
        Assert.Single(Http.Requests);
    }

    [Fact]
    public void ModelListStripsModelsPrefix()
    {
        var client = new GeminiClient(Settings("Google"));
        Http.EnqueueJson("{\"models\":[{\"name\":\"models/gemini-2.5-flash\"},{\"name\":\"models/gemini-2.5-pro\"}]}");

        Assert.Equal(new[] { "gemini-2.5-flash", "gemini-2.5-pro" }, client.GetModelNames());
        Assert.Contains("models?key=test-key", Http.Requests.Single().Url, StringComparison.Ordinal);
    }
}

[Collection("LlmLayer")]
public sealed class VolcEngineClientTests : LlmTestBase
{
    private const string ReplyJson = "{\"choices\":[{\"message\":{\"content\":\"你好\"}}],\"usage\":{\"prompt_tokens\":7,\"completion_tokens\":2,\"total_tokens\":9}}";

    [Theory]
    [InlineData("doubao-1.5-pro", false)]          // 名单之外：宁可少提速也不发
    [InlineData("doubao-seed-1.6", true)]
    [InlineData("doubao-seed-1-6-flash", true)]
    [InlineData("doubao-seed-1.6-thinking", false)] // 强制思考：黑名单优先
    [InlineData("deepseek-r1-distill", false)]
    [InlineData("deepseek-v3.1", true)]
    [InlineData("deepseek-v3-1-terminus", true)]
    [InlineData("doubao-1.5-thinking-pro", true)]
    [InlineData("unknown-model", false)]
    public void DisableThinkingWhitelistMatchesBySubstring(string model, bool expected)
    {
        Assert.Equal(expected, VolcEngineClient.SupportsDisableThinking(model));
    }

    [Fact]
    public async Task EndpointHasNoV1AndThinkingFieldOnlyForWhitelistedFastPass()
    {
        var whitelisted = new VolcEngineClient(Settings("VolcEngine", modelName: "doubao-seed-1.6"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(ReplyJson);

        await whitelisted.CompleteAsync(Request(disableThinking: true), CancellationToken.None);
        Assert.Equal("https://ark.cn-beijing.volces.com/api/v3/chat/completions", Http.Requests[0].Url);
        var body = JObject.Parse(Http.Requests[0].Body!);
        Assert.Equal("disabled", body["thinking"]!.Value<string>("type"));
        Assert.Equal("json_object", body["response_format"]!.Value<string>("type"));

        // 非白名单模型：快速通道仍带 response_format，但不发 thinking（发了会报参数不支持）。
        Http.Requests.Clear();
        var plain = new VolcEngineClient(Settings("VolcEngine", modelName: "doubao-1.5-pro"));
        await plain.CompleteAsync(Request(disableThinking: true), CancellationToken.None);
        body = JObject.Parse(Http.Requests[0].Body!);
        Assert.Null(body["thinking"]);
        Assert.Equal("json_object", body["response_format"]!.Value<string>("type"));

        // 主对话请求体两者皆不带。
        Http.Requests.Clear();
        await whitelisted.CompleteAsync(Request(), CancellationToken.None);
        body = JObject.Parse(Http.Requests[0].Body!);
        Assert.Null(body["thinking"]);
        Assert.Null(body["response_format"]);
        Assert.Equal("doubao-seed-1.6", body.Value<string>("model"));
    }
}

[Collection("LlmLayer")]
public sealed class LlamaCppClientTests : LlmTestBase
{
    private static LlmConnectionSettings LocalSettings(string promptFormat = "[INST] {system}\n{prompt}[/INST]\n{response_start}")
    {
        return Settings("LlamaCpp", apiKey: "", serverAddress: "http://localhost:8080/completion", promptFormat: promptFormat);
    }

    [Fact]
    public async Task ServerAddressIsUsedVerbatimAndTemplateReplacesPlaceholders()
    {
        var client = new LlamaCppClient(LocalSettings());
        Http.EnqueueJson("{\"content\":\"Hi\",\"timings\":{\"prompt_n\":12,\"predicted_n\":3}}");

        LlmReply reply = await client.CompleteAsync(
            Request(system: "S", stable: "A", npc: "B", tail: "C", responseStart: "R:"),
            CancellationToken.None);

        Assert.True(reply.IsSuccess);
        FakeHttpHandler.RecordedRequest request = Http.Requests.Single();
        Assert.Equal("http://localhost:8080/completion", request.Url);
        Assert.Null(request.Authorization);
        var body = JObject.Parse(request.Body!);
        Assert.Equal("[INST] S\nABC[/INST]\nR:", body.Value<string>("prompt"));
        Assert.Equal(2048, body.Value<int>("n_predict"));
        Assert.False(body.Value<bool>("stream"));
        Assert.True(body.Value<bool>("cache_prompt"));
        Assert.Equal(1.5, body.Value<double>("temperature"));
        Assert.Equal(0.88, body.Value<double>("top_p"));
        Assert.Equal(0.05, body.Value<double>("min_p"));
        Assert.Equal(1.05, body.Value<double>("repeat_penalty"));
        // timings 折算 usage。
        Assert.Equal(12, reply.Usage.PromptTokens);
        Assert.Equal(3, reply.Usage.CompletionTokens);
        Assert.Equal("llama.cpp timings", reply.Usage.Source);
    }

    [Fact]
    public async Task SingleTokenPredictionUsesZeroTemperature()
    {
        var client = new LlamaCppClient(LocalSettings());
        Http.EnqueueJson("{\"content\":\"Y\"}");

        await client.CompleteAsync(Request(maxTokens: 1), CancellationToken.None);

        Assert.Equal(0, JObject.Parse(Http.Requests.Single().Body!).Value<double>("temperature"));
    }

    [Fact]
    public async Task RetryIsFiniteThreeAttempts()
    {
        // 旧实现失败无限重试；裁决 1 改为有限 3 次（明确的行为修正）。
        var client = new LlamaCppClient(LocalSettings());
        Http.DefaultResponder = _ => FakeHttpHandler.Json("down", HttpStatusCode.ServiceUnavailable);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal(3, Http.Requests.Count);
        Assert.Equal(503, reply.HttpStatus);
    }

    [Fact]
    public async Task ProbabilityQuerySendsNProbsAndAggregatesOptionBuckets()
    {
        var client = new LlamaCppClient(LocalSettings());
        Http.EnqueueJson(
            "{\"content\":\"Yes\",\"completion_probabilities\":["
            + "{\"probs\":[{\"tok_str\":\"Yes\",\"prob\":0.7},{\"tok_str\":\"No\",\"prob\":0.2},{\"tok_str\":\"Eh\",\"prob\":0.1}]}"
            + "]}");

        IReadOnlyDictionary<string, double> buckets = await client.GetOptionProbabilitiesAsync(
            Request(maxTokens: 4),
            new[] { "Yes", "No" },
            CancellationToken.None);

        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(10, body.Value<int>("n_probs"));
        Assert.Equal(0.8, body.Value<double>("temperature"));
        Assert.True(body.Value<bool>("cache_prompt"));
        Assert.Equal(0.7, buckets["Yes"], 5);
        Assert.Equal(0.2, buckets["No"], 5);
    }

    [Fact]
    public void ExtraInstructionsAreDeclaredForPromptSide()
    {
        var client = new LlamaCppClient(LocalSettings());
        Assert.Equal(
            "Include only the new line and any responses in the output, no descriptions or explanations.",
            client.ExtraInstructions);
        Assert.False(client.IsHighlySensoredModel);
        Assert.False((object)client is IModelNameSource);
    }
}
