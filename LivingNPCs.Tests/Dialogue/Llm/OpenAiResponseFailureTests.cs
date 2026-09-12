using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class OpenAiResponseFailureTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello\"},\"finish_reason\":\"stop\"}]}";

    [Theory]
    [InlineData("DeepSeek", "deepseek-v4-flash", LlmThinking.Low, false, false)]
    [InlineData("OpenAiCompatible", "deepseek-v4-flash", LlmThinking.Low, false, false)]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Low, false, false)]
    [InlineData("OpenAiCompatible", "deepseek-flash", LlmThinking.Low, false, false)]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Minimal, false, false)]
    [InlineData("OpenAiCompatible", "deepseek-flash", LlmThinking.Minimal, false, false)]
    [InlineData("DeepSeek", "deepseek-flash", LlmThinking.Low, true, true)]
    [InlineData("OpenAiCompatible", "proxy/deepseek-ai/DeepSeek_Flash", LlmThinking.Minimal, true, true)]
    [InlineData("OpenAiCompatible", "deepseek-flash-20260912", LlmThinking.Low, false, true)]
    [InlineData("OpenAiCompatible", "deepseek-flash", LlmThinking.Minimal, false, true)]
    public async Task DeepSeekLowEffortRequestsHonorSharedSettingsForBothRequestKinds(
        string provider, string model, string level, bool fastPass, bool bufferedStream)
    {
        Config.ThinkingLevel = level;
        Config.UseStreamingDialogueTransport = bufferedStream;
        LlmClientBase client = LlmClientFactory.TryCreate(Settings(provider, modelName: model))!;
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: fastPass), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(model, body.Value<string>("model"));
        Assert.Equal("enabled", body["thinking"]!.Value<string>("type"));
        Assert.Equal("low", body.Value<string>("reasoning_effort"));
        Assert.Equal(bufferedStream && !fastPass, body.Value<bool?>("stream") == true);
    }

    [Theory]
    [InlineData("DeepSeek", "deepseek-reasoner", LlmThinking.Low)]
    [InlineData("DeepSeek", "deepseek-r1", LlmThinking.Minimal)]
    [InlineData("OpenAiCompatible", "deepseek-reasoner", LlmThinking.Minimal)]
    [InlineData("OpenAiCompatible", "deepseek-ai/DeepSeek-R1-Distill-Qwen-32B", LlmThinking.Low)]
    public async Task LegacyDeepSeekRequestsKeepCompatibleReasoningEffort(string provider, string model, string level)
    {
        Config.ThinkingLevel = level;
        LlmClientBase client = LlmClientFactory.TryCreate(Settings(provider, modelName: model))!;
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(model, body.Value<string>("model"));
        Assert.Equal("enabled", body["thinking"]!.Value<string>("type"));
        Assert.Equal("high", body.Value<string>("reasoning_effort"));
    }

    [Theory]
    [InlineData("other-flash")]
    [InlineData("not-deepseek-flash")]
    [InlineData("deepseek/deepseek-flashlight")]
    [InlineData("deepseek-flash/other-model")]
    public async Task CompatibleEndpointDoesNotAddDeepSeekControlsToLookalikeModels(string model)
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: model));
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(model, body.Value<string>("model"));
        Assert.Null(body["thinking"]);
        Assert.Null(body["reasoning_effort"]);
    }

    [Theory]
    [InlineData(384, true)]
    [InlineData(512, true)]
    [InlineData(2048, false)]
    public async Task ReasoningExhaustionStopsAllCandidatesAndKeepsReasoningOutOfErrors(int budget, bool fastPass)
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(ReasoningOnlyJson(budget));

        LlmReply reply = await client.CompleteAsync(Request(maxTokens: budget, disableThinking: fastPass), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Single(Http.Requests);
        Assert.Equal(200, reply.HttpStatus);
        Assert.Equal(budget, reply.Usage.CompletionTokens);
        Assert.Equal(budget, reply.Usage.ReasoningTokens);
        Assert.Contains("finish_reason=length", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains($"reasoning_tokens={budget}", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains($"max_tokens={budget}", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.True(reply.ErrorMessage.Length < 300);
        Assert.DoesNotContain("PRIVATE_REASONING", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.All(Monitor.Entries, e => Assert.DoesNotContain("PRIVATE_REASONING", e.Message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task CompletedEmptyResponseIsNotRegenerated(string finishReason)
    {
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(new JObject
        {
            ["choices"] = new JArray(new JObject
            {
                ["message"] = new JObject { ["content"] = "" },
                ["finish_reason"] = finishReason
            })
        }.ToString());

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Single(Http.Requests);
        Assert.Contains($"finish_reason={finishReason}", reply.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeepSeekOffKeepsDisabledSwitchInCompatibilityFallback()
    {
        Config.ThinkingLevel = LlmThinking.Off;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = request => JObject.Parse(request.Body!)["instructions"] == null
            ? FakeHttpHandler.Json("{\"error\":{\"message\":\"Unsupported system role\"}}", HttpStatusCode.BadRequest)
            : FakeHttpHandler.Json(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal(2, Http.Requests.Count);
        Assert.All(Http.Requests, request =>
        {
            var body = JObject.Parse(request.Body!);
            Assert.Equal("disabled", body["thinking"]!.Value<string>("type"));
            Assert.Null(body["reasoning_effort"]);
        });
        Assert.DoesNotContain(Monitor.Entries, entry => entry.Message.Contains("retrying without thinking", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("DeepSeek", "deepseek-v4-flash")]
    [InlineData("OpenAiCompatible", "deepseek-v4-flash")]
    [InlineData("DeepSeek", "deepseek-flash")]
    [InlineData("OpenAiCompatible", "deepseek-flash")]
    public async Task RejectedDeepSeekOffNeverFallsBackToEnabledDefaults(string provider, string model)
    {
        Config.ThinkingLevel = LlmThinking.Off;
        LlmClientBase client = LlmClientFactory.TryCreate(Settings(provider, modelName: model))!;
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":{\"message\":\"Unsupported thinking\"}}", HttpStatusCode.BadRequest);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal(provider == "DeepSeek" ? 1 : 2, Http.Requests.Count);
        Assert.All(Http.Requests, request => Assert.Equal("disabled", JObject.Parse(request.Body!)["thinking"]!.Value<string>("type")));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 3, false)]
    [InlineData(HttpStatusCode.TooManyRequests, 3, true)]
    [InlineData(HttpStatusCode.InternalServerError, 6, false)]
    [InlineData(HttpStatusCode.InternalServerError, 6, true)]
    public async Task TransientFailuresKeepSharedThinkingControlsThroughoutRetries(HttpStatusCode status, int expectedCalls, bool background)
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("temporarily unavailable", status);

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: background), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        // Rate limiting keeps one shape; HTTP 500 retains compatibility with older gateways.
        Assert.Equal(expectedCalls, Http.Requests.Count);
        Assert.True(reply.Retryable);
        Assert.All(Http.Requests, request =>
        {
            var body = JObject.Parse(request.Body!);
            Assert.Equal("enabled", body["thinking"]!.Value<string>("type"));
            Assert.Equal("low", body.Value<string>("reasoning_effort"));
        });
        Assert.DoesNotContain(Monitor.Entries, entry => entry.Message.Contains("retrying without thinking", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TransientFailureStillRecoversWithSameRequest()
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new DeepSeekClient(Settings("DeepSeek", modelName: "deepseek-v4-flash"));
        Http.EnqueueJson("temporarily unavailable", HttpStatusCode.ServiceUnavailable);
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal(2, Http.Requests.Count);
        Assert.Equal(Http.Requests[0].Body, Http.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    public async Task AuthenticationFailureStopsImmediatelyAcrossAllShapes(HttpStatusCode status, bool stream)
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("authentication rejected", status);

        if (stream)
        {
            var exception = await Assert.ThrowsAsync<LlmStreamException>(
                async () => await CollectAsync(client.StreamAsync(Request(), CancellationToken.None)));
            Assert.False(exception.Retryable);
            Assert.Equal((int)status, exception.HttpStatus);
        }
        else
        {
            LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);
            Assert.False(reply.IsSuccess);
            Assert.False(reply.Retryable);
            Assert.Equal((int)status, reply.HttpStatus);
        }

        Assert.Single(Http.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransportFailuresKeepSameShapeBudgetWithoutStreamOrInstructionsFallback(bool timeout, bool stream)
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = _ =>
        {
            if (timeout)
            {
                throw new TaskCanceledException("simulated request timeout");
            }

            throw new System.Net.Http.HttpRequestException("simulated connection reset");
        };

        if (stream)
        {
            var exception = await Assert.ThrowsAsync<LlmStreamException>(
                async () => await CollectAsync(client.StreamAsync(Request(), CancellationToken.None)));
            Assert.True(exception.Retryable);
        }
        else
        {
            LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);
            Assert.False(reply.IsSuccess);
            Assert.True(reply.Retryable);
        }

        Assert.Equal(3, Http.Requests.Count);
        Assert.All(Http.Requests, request =>
        {
            var body = JObject.Parse(request.Body!);
            Assert.Null(body["instructions"]);
            Assert.Equal(stream, body.Value<bool?>("stream") == true);
            Assert.Equal("low", body.Value<string>("reasoning_effort"));
        });
        Assert.True(Http.Requests.All(request => request.Body == Http.Requests[0].Body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoRetryRateLimitMakesExactlyOneRequest(bool stream)
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("rate limited", HttpStatusCode.TooManyRequests);

        if (stream)
        {
            var exception = await Assert.ThrowsAsync<LlmStreamException>(
                async () => await CollectAsync(client.StreamAsync(Request(allowRetry: false), CancellationToken.None)));
            Assert.True(exception.Retryable);
            Assert.Equal(429, exception.HttpStatus);
        }
        else
        {
            LlmReply reply = await client.CompleteAsync(Request(allowRetry: false), CancellationToken.None);
            Assert.True(reply.Retryable);
            Assert.Equal(429, reply.HttpStatus);
        }

        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task Http500GatewayStillSupportsNonStreamingFallback()
    {
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible"));
        Http.DefaultResponder = request => JObject.Parse(request.Body!).Value<bool?>("stream") == true
            ? FakeHttpHandler.Json("gateway stream unsupported", HttpStatusCode.InternalServerError)
            : FakeHttpHandler.Json(CompletionJson);

        var events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Equal(4, Http.Requests.Count);
        Assert.False(JObject.Parse(Http.Requests[^1].Body!).Value<bool?>("stream") == true);
        Assert.Equal("Hello", events[0].Text);
        Assert.Equal(LlmStreamEventKind.Done, events[^1].Kind);
    }

    [Fact]
    public async Task SseReasoningExhaustionStopsWithoutNonStreamingFallback()
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        string sse = "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}],\"usage\":{\"completion_tokens\":2048,\"completion_tokens_details\":{\"reasoning_tokens\":2048}}}\n"
            + "data: [DONE]\n";
        Http.DefaultResponder = _ => FakeHttpHandler.Text(sse);

        LlmStreamException exception = await Assert.ThrowsAsync<LlmStreamException>(
            async () => await CollectAsync(client.StreamAsync(Request(), CancellationToken.None)));

        Assert.Single(Http.Requests);
        Assert.False(exception.Retryable);
        Assert.Equal(2048, exception.Usage.ReasoningTokens);
        Assert.Contains("finish_reason=length", exception.Message, StringComparison.Ordinal);
        Assert.Contains("reasoning_tokens=2048", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_REASONING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteReasoningOnlyJsonReturnedToStreamIsNotRegenerated()
    {
        var client = new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(ReasoningOnlyJson(2048));

        LlmStreamException exception = await Assert.ThrowsAsync<LlmStreamException>(
            async () => await CollectAsync(client.StreamAsync(Request(), CancellationToken.None)));

        Assert.Single(Http.Requests);
        Assert.False(exception.Retryable);
        Assert.Equal(2048, exception.Usage.ReasoningTokens);
        Assert.Contains("reasoning_tokens=2048", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyBridgePreservesTerminalFailureAndReasoningUsage()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
        Http.EnqueueJson(ReasoningOnlyJson(2048));

        LlmResponse response = await LegacyLlm.Instance.RunInference("SYS", "", "", "TAIL", allowRetry: false);

        Assert.False(response.IsSuccess);
        Assert.False(response.Retryable);
        Assert.Equal(2048, response.Usage.ReasoningTokens);
        Assert.Single(Http.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedEmptyAnswerDoesNotTripNetworkBreaker(bool stream)
    {
        var breaker = new LlmCircuitBreaker();
        for (int i = 0; i < LlmCircuitBreaker.RateTripThreshold - 1; i++)
        {
            Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(success: false, 429));
        }

        int trips = 0;
        var client = new BreakerGuardedLlmClient(
            new DeepSeekClient(Settings("DeepSeek", modelName: "deepseek-v4-flash")), breaker, _ => trips++);
        Http.EnqueueJson(ReasoningOnlyJson(2048));
        if (stream)
        {
            var exception = await Assert.ThrowsAsync<LlmStreamException>(
                async () => await CollectAsync(client.StreamAsync(Request(), CancellationToken.None)));
            Assert.False(exception.Retryable);
        }
        else
        {
            LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);
            Assert.False(reply.Retryable);
        }

        Assert.Single(Http.Requests);
        Assert.Equal(0, trips);
        Assert.Equal(LlmSuspensionKind.None, breaker.CurrentSuspension);
        // HTTP 200 breaks a sequence of transport failures even when the model gives no usable answer.
        Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(success: false, 429));
    }

    [Fact]
    public async Task SseReasoningWithVisibleAnswerUsesOnlyAnswerText()
    {
        var client = new DeepSeekClient(Settings("DeepSeek", modelName: "deepseek-v4-flash"));
        Http.Enqueue(_ => FakeHttpHandler.Text(
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n"
            + "data: [DONE]\n"));

        var events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Single(Http.Requests);
        Assert.Equal("Hello", string.Concat(events.Where(e => e.Kind == LlmStreamEventKind.TextDelta).Select(e => e.Text)));
        Assert.Equal(LlmStreamEventKind.Done, events[^1].Kind);
    }

    private static string ReasoningOnlyJson(int budget)
    {
        return new JObject
        {
            ["choices"] = new JArray(new JObject
            {
                ["message"] = new JObject
                {
                    ["content"] = "",
                    ["reasoning_content"] = "PRIVATE_REASONING" + new string('x', 8000)
                },
                ["finish_reason"] = "length"
            }),
            ["usage"] = new JObject
            {
                ["completion_tokens"] = budget,
                ["completion_tokens_details"] = new JObject { ["reasoning_tokens"] = budget }
            }
        }.ToString();
    }
}
