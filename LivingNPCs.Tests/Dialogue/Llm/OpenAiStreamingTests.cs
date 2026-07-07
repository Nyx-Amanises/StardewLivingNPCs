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
public sealed class OpenAiStreamingTests : LlmTestBase
{
    [Fact]
    public async Task NormalSseStreamYieldsDeltasThenRealUsageThenDone()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        string sse = "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n"
            + "\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}\n"
            + "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2,\"total_tokens\":12,\"prompt_tokens_details\":{\"cached_tokens\":4}}}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Equal(
            new[] { LlmStreamEventKind.TextDelta, LlmStreamEventKind.TextDelta, LlmStreamEventKind.Usage, LlmStreamEventKind.Done },
            events.Select(e => e.Kind).ToArray());
        Assert.Equal("Hel", events[0].Text);
        Assert.Equal("lo", events[1].Text);
        Assert.Equal(12, events[2].Usage!.TotalTokens);
        Assert.Equal(4, events[2].Usage!.CachedPromptTokens);
        Assert.Equal("provider usage", events[2].Usage!.Source);

        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.True(body.Value<bool>("stream"));
        Assert.True(body["stream_options"]!.Value<bool>("include_usage"));
    }

    [Fact]
    public async Task MistralStreamOmitsStreamOptionsAndEstimatesUsageWhenAbsent()
    {
        var client = new MistralClient(Settings("Mistral"));
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Bonjour\"}}]}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Null(JObject.Parse(Http.Requests.Single().Body!)["stream_options"]);
        LlmStreamEvent usage = events.Single(e => e.Kind == LlmStreamEventKind.Usage);
        Assert.True(usage.Usage!.IsEstimated);
        Assert.Equal("stream estimate", usage.Usage!.Source);
    }

    [Fact]
    public async Task CompleteJsonBodyFallsBackToSingleDeltaWithFallbackEstimate()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        // 端点无视 stream 参数直接回完整 JSON：SSE 解析拿不到增量，降级 ① 把全文一次性推给回调。
        Http.DefaultResponder = _ => FakeHttpHandler.Json(
            "{\"choices\":[{\"message\":{\"content\":\"Hi there\"}}],\"usage\":{\"prompt_tokens\":9,\"completion_tokens\":2,\"total_tokens\":11}}");

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        // 流式预算 3 次全部拿不到增量后走降级 ①，不再发第 4 个请求。
        Assert.Equal(3, Http.Requests.Count);
        Assert.Equal(
            new[] { LlmStreamEventKind.TextDelta, LlmStreamEventKind.Usage, LlmStreamEventKind.Done },
            events.Select(e => e.Kind).ToArray());
        Assert.Equal("Hi there", events[0].Text);
        Assert.Equal("stream fallback estimate", events[1].Usage!.Source);
    }

    [Fact]
    public async Task GarbageStreamFallsBackToNonStreamingChannelWithoutRetry()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        int calls = 0;
        Http.DefaultResponder = request =>
        {
            calls++;
            bool isStream = JObject.Parse(request.Body!).Value<bool?>("stream") == true;
            if (isStream)
            {
                return FakeHttpHandler.Text(": keep-alive comment only\n");
            }

            return FakeHttpHandler.Json("{\"choices\":[{\"message\":{\"content\":\"Recovered\"}}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":1,\"total_tokens\":6}}");
        };

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        // 3 次流式尝试 + 1 次非流式兜底（禁止重试）。
        Assert.Equal(4, calls);
        Assert.False(JObject.Parse(Http.Requests[3].Body!).Value<bool?>("stream") == true);
        Assert.Equal("Recovered", events[0].Text);
        Assert.Equal("provider usage", events[1].Usage!.Source);
        Assert.Equal(LlmStreamEventKind.Done, events[2].Kind);
    }

    [Fact]
    public async Task ExhaustedFallbackChainThrowsStreamExceptionWithStatus()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"quota\"}", HttpStatusCode.TooManyRequests);

        var exception = await Assert.ThrowsAsync<LlmStreamException>(
            async () => await CollectAsync(client.StreamAsync(Request(), CancellationToken.None)));

        Assert.Equal(429, exception.HttpStatus);
        // 3 次流式 + 1 次非流式兜底全部失败。
        Assert.Equal(4, Http.Requests.Count);
    }

    [Fact]
    public async Task StreamingBodyUsesChatThinkingLevelWithoutFallbackCandidates()
    {
        Config.ChatThinkingLevel = "High";
        var client = new OpenAiClient(Settings("OpenAI", modelName: "gpt-5.5"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"bad param\"}", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<LlmStreamException>(
            async () => await CollectAsync(client.StreamAsync(Request(allowRetry: false), CancellationToken.None)));

        // 流式没有思考参数回退序列：仅一个流式请求（带思考参数），失败后直接进降级链
        // （非流式兜底按自己的候选序列跑：思考版 + 裸版各 1 次）。
        var streamRequests = Http.Requests.Where(r => JObject.Parse(r.Body!).Value<bool?>("stream") == true).ToList();
        Assert.Single(streamRequests);
        Assert.Equal("high", JObject.Parse(streamRequests[0].Body!).Value<string>("reasoning_effort"));
        Assert.Equal(3, Http.Requests.Count);
    }

    [Fact]
    public async Task CancellationPropagatesWithoutFallback()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        using var cts = new CancellationTokenSource();
        Http.DefaultResponder = _ =>
        {
            cts.Cancel();
            return FakeHttpHandler.Text("data: [DONE]\n");
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CollectAsync(client.StreamAsync(Request(), cts.Token), cts.Token));

        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task ClaudeStreamWrapsNonStreamingReplyAsSingleDelta()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson("{\"content\":[{\"type\":\"text\",\"text\":\"Hi from Claude\"}],\"usage\":{\"input_tokens\":3,\"output_tokens\":4}}");

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Equal(
            new[] { LlmStreamEventKind.TextDelta, LlmStreamEventKind.Usage, LlmStreamEventKind.Done },
            events.Select(e => e.Kind).ToArray());
        Assert.Equal("Hi from Claude", events[0].Text);
        Assert.Equal("provider usage", events[1].Usage!.Source);
    }
}
