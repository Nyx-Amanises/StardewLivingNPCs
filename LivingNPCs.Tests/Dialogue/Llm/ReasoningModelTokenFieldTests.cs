using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

/// <summary>
/// OpenAI 推理模型（gpt-5 全系 + o1/o3/o4 系）在 chat/completions 上拒绝 max_tokens，
/// 必须发 max_completion_tokens；普通模型（含兼容端点自建服务）保持 max_tokens 不变。
/// 覆盖：判定谓词、非流式裸候选、思考候选回退链、流式请求体、行为规划器载荷。
/// </summary>
[Collection("LlmLayer")]
public sealed class ReasoningModelTokenFieldTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}";

    [Theory]
    // gpt-5 全系（归一化把 . / _ 折为 -）。
    [InlineData("gpt-5", true)]
    [InlineData("gpt-5-mini", true)]
    [InlineData("gpt-5.2", true)]
    [InlineData("gpt5-turbo", true)]
    // o 系：段首 o+数字+边界（串尾或 -），含网关前缀形态。
    [InlineData("o1", true)]
    [InlineData("o1-mini", true)]
    [InlineData("o1-preview", true)]
    [InlineData("o3", true)]
    [InlineData("o3-mini-2025-01-31", true)]
    [InlineData("o4-mini", true)]
    [InlineData("O3-MINI", true)]
    [InlineData("openai/o3-mini", true)]
    // 不得误伤的普通模型名。
    [InlineData("gpt-4o", false)]
    [InlineData("gpt-4o-mini", false)]
    [InlineData("chatgpt-4o-latest", false)]
    [InlineData("gpt-4.1", false)]
    [InlineData("olmo-2", false)]
    [InlineData("orca-2", false)]
    [InlineData("deepseek-chat", false)]
    [InlineData("mistral-large-latest", false)]
    [InlineData("llama-3.1-8b-instruct", false)]
    [InlineData("doubao-1.5-pro", false)]
    [InlineData("", false)]
    public void ReasoningModelPredicateMatchesBySegmentBoundary(string model, bool expected)
    {
        Assert.Equal(expected, LlmThinking.IsOpenAiReasoningModel(model));
        Assert.Equal(
            expected ? "max_completion_tokens" : "max_tokens",
            LlmThinking.OpenAiMaxTokensFieldName(model));
    }

    [Theory]
    [InlineData("gpt-5-mini")]
    [InlineData("o3-mini")]
    public async Task ReasoningModelSendsMaxCompletionTokensInsteadOfMaxTokens(string model)
    {
        // 默认聊天档位 auto：无思考参数、单候选（裸候选路径本身即被覆盖）。
        var client = new OpenAiClient(Settings("OpenAI", modelName: model));
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(2048, body.Value<int>("max_completion_tokens"));
        Assert.Null(body["max_tokens"]);
        Assert.Null(body["reasoning_effort"]);
    }

    [Fact]
    public async Task NonReasoningModelKeepsMaxTokens()
    {
        var client = new OpenAiClient(Settings("OpenAI", modelName: "gpt-4o"));
        Http.EnqueueJson(CompletionJson);

        await client.CompleteAsync(Request(), CancellationToken.None);

        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(2048, body.Value<int>("max_tokens"));
        Assert.Null(body["max_completion_tokens"]);
    }

    [Fact]
    public async Task CompatibleEndpointModelsAreUnaffectedUnlessReasoningNamed()
    {
        // 兼容端点（vLLM 等自建服务）上的普通模型名必须保持 max_tokens。
        var plain = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "llama-3.1-8b-instruct"));
        Http.EnqueueJson(CompletionJson);
        await plain.CompleteAsync(Request(), CancellationToken.None);
        var plainBody = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(2048, plainBody.Value<int>("max_tokens"));
        Assert.Null(plainBody["max_completion_tokens"]);

        // 兼容端点转发 OpenAI 推理模型（网关前缀形态）时同样换字段。
        Http.Requests.Clear();
        var proxied = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "openai/o3-mini"));
        Http.EnqueueJson(CompletionJson);
        await proxied.CompleteAsync(Request(), CancellationToken.None);
        var proxiedBody = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(2048, proxiedBody.Value<int>("max_completion_tokens"));
        Assert.Null(proxiedBody["max_tokens"]);
    }

    [Fact]
    public async Task ThinkingCandidateAndBareFallbackBothUseMaxCompletionTokens()
    {
        // 思考候选（带 reasoning_effort）3 次耗尽后进入裸候选：两种请求体都必须已换字段。
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
        Assert.Equal(4, Http.Requests.Count);
        Assert.All(Http.Requests, r =>
        {
            var body = JObject.Parse(r.Body!);
            Assert.Equal(2048, body.Value<int>("max_completion_tokens"));
            Assert.Null(body["max_tokens"]);
        });
        Assert.Equal("high", JObject.Parse(Http.Requests[0].Body!).Value<string>("reasoning_effort"));
        Assert.Null(JObject.Parse(Http.Requests[3].Body!)["reasoning_effort"]);
    }

    [Fact]
    public async Task StreamingBodyUsesMaxCompletionTokensForReasoningModel()
    {
        var client = new OpenAiClient(Settings("OpenAI", modelName: "gpt-5.5"));
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Contains(events, e => e.Kind == LlmStreamEventKind.Done);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.True(body.Value<bool>("stream"));
        Assert.Equal(2048, body.Value<int>("max_completion_tokens"));
        Assert.Null(body["max_tokens"]);
    }

    [Fact]
    public void BehaviorPlannerPayloadSwitchesFieldAndDropsTemperatureForReasoningModels()
    {
        // 推理模型：max_completion_tokens、无 max_tokens、无 temperature（推理模型拒绝非默认采样）。
        var reasoning = JObject.Parse(AiBehaviorClient.BuildPayloadJson("o3-mini", "SYS", "USER"));
        Assert.Equal("o3-mini", reasoning.Value<string>("model"));
        Assert.Equal(120, reasoning.Value<int>("max_completion_tokens"));
        Assert.Null(reasoning["max_tokens"]);
        Assert.Null(reasoning["temperature"]);
        var reasoningMessages = (JArray)reasoning["messages"]!;
        Assert.Equal("system", reasoningMessages[0].Value<string>("role"));
        Assert.Equal("SYS", reasoningMessages[0].Value<string>("content"));
        Assert.Equal("user", reasoningMessages[1].Value<string>("role"));
        Assert.Equal("USER", reasoningMessages[1].Value<string>("content"));

        // 普通模型：原字段原值保持不变。
        var plain = JObject.Parse(AiBehaviorClient.BuildPayloadJson("gpt-4o-mini", "SYS", "USER"));
        Assert.Equal(120, plain.Value<int>("max_tokens"));
        Assert.Null(plain["max_completion_tokens"]);
        Assert.Equal(0.2, plain.Value<double>("temperature"), 5);
    }
}
