using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class OpenAiProviderErrorEnvelopeTests : LlmTestBase
{
    private const string ErrorJson = "{\"error\":{\"message\":\"Our servers are currently overloaded. PRIVATE_DETAIL sk-audit-secret https://private.invalid/gateway PRIVATE_REASONING\",\"type\":\"server_error\"}}";
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"},\"finish_reason\":\"stop\"}]}";
    private const string ContentChunk = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello there\"}}]}\n";

    [Theory]
    [InlineData("json", false)]
    [InlineData("json", true)]
    [InlineData("sse", false)]
    [InlineData("sse", true)]
    [InlineData("ignored-stream-json", false)]
    [InlineData("ignored-stream-json", true)]
    [InlineData("ignored-stream-pretty-json", false)]
    [InlineData("ignored-stream-pretty-json", true)]
    [InlineData("nonstream-sse", false)]
    [InlineData("nonstream-sse", true)]
    public async Task ServiceErrorEnvelopeUsesOnlyTheSameRequestShapeBudget(string shape, bool allowRetry)
    {
        Config.UseStreamingDialogueTransport = UsesStreamingTransport(shape);
        Config.ChatThinkingLevel = LlmThinking.Low;
        var client = CreateClient();
        Http.DefaultResponder = _ => ErrorResponse(shape);

        LlmReply reply = await client.CompleteAsync(Request(allowRetry: allowRetry), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.True(reply.Retryable);
        Assert.Equal(503, reply.HttpStatus);
        Assert.Empty(reply.Text);
        Assert.Equal(allowRetry ? 3 : 1, Http.Requests.Count);
        Assert.Single(Http.Requests.Select(request => request.Body).Distinct(StringComparer.Ordinal));
        AssertSafeError(reply.ErrorMessage);
        foreach (FakeHttpHandler.RecordedRequest request in Http.Requests)
        {
            JObject body = JObject.Parse(request.Body!);
            Assert.Equal(UsesStreamingTransport(shape), body.Value<bool?>("stream") == true);
            Assert.Equal("low", body.Value<string>("reasoning_effort"));
            Assert.Null(body["instructions"]);
        }
    }

    [Theory]
    [InlineData("json")]
    [InlineData("sse")]
    [InlineData("ignored-stream-pretty-json")]
    public async Task ATransientEnvelopeCanRecoverWithinTheSameFormatBudget(string shape)
    {
        Config.UseStreamingDialogueTransport = UsesStreamingTransport(shape);
        Config.ChatThinkingLevel = LlmThinking.Low;
        Http.Enqueue(_ => ErrorResponse(shape));
        Http.Enqueue(_ => shape == "sse"
            ? FakeHttpHandler.Text(ContentChunk + "data: [DONE]\n")
            : FakeHttpHandler.Json(CompletionJson));

        LlmReply reply = await CreateClient().CompleteAsync(Request(allowRetry: true), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Equal(2, Http.Requests.Count);
        Assert.Equal(Http.Requests[0].Body, Http.Requests[1].Body);
        AssertSafeError(string.Join("\n", Monitor.Entries.Select(entry => entry.Message)), requireFailureLabel: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SseErrorAfterPartialContentCannotBecomeASuccess(bool streaming)
    {
        Config.UseStreamingDialogueTransport = streaming;
        Http.DefaultResponder = _ => FakeHttpHandler.Text(ContentChunk + "data: " + ErrorJson + "\ndata: [DONE]\n");

        LlmReply reply = await CreateClient().CompleteAsync(Request(allowRetry: false), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.True(reply.Retryable);
        Assert.Equal(503, reply.HttpStatus);
        Assert.Empty(reply.Text);
        Assert.Single(Http.Requests);
        AssertSafeError(reply.ErrorMessage);
    }

    [Fact]
    public async Task PublicStreamStopsAfterPartialContentAndReportsTheProviderFailure()
    {
        Http.Enqueue(_ => FakeHttpHandler.Text(ContentChunk + "data: " + ErrorJson + "\ndata: [DONE]\n"));
        var events = new List<LlmStreamEvent>();
        var client = CreateClient();

        LlmStreamException failure = await Assert.ThrowsAsync<LlmStreamException>(async () =>
        {
            await foreach (LlmStreamEvent item in client.StreamAsync(Request(allowRetry: true), CancellationToken.None))
            {
                events.Add(item);
            }
        });

        Assert.Equal(503, failure.HttpStatus);
        Assert.True(failure.Retryable);
        Assert.Equal(LlmStreamEventKind.TextDelta, Assert.Single(events).Kind);
        Assert.Single(Http.Requests);
        AssertSafeError(failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorEnvelopeTakesPrecedenceOverACompleteJsonAnswer(bool streaming)
    {
        Config.UseStreamingDialogueTransport = streaming;
        JObject json = JObject.Parse(CompletionJson);
        json["error"] = JObject.Parse(ErrorJson)["error"];
        Http.DefaultResponder = _ => FakeHttpHandler.Json(json.ToString(Formatting.Indented));

        LlmReply reply = await CreateClient().CompleteAsync(Request(allowRetry: false), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal(503, reply.HttpStatus);
        Assert.Empty(reply.Text);
        Assert.Single(Http.Requests);
        AssertSafeError(reply.ErrorMessage);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("sse")]
    [InlineData("ignored-stream-pretty-json")]
    public async Task NullErrorDoesNotRejectValidContent(string shape)
    {
        Config.UseStreamingDialogueTransport = UsesStreamingTransport(shape);
        Http.Enqueue(_ => shape == "sse"
            ? FakeHttpHandler.Text("data: {\"error\":null,\"choices\":[{\"delta\":{\"content\":\"Hello there\"}}]}\ndata: [DONE]\n")
            : FakeHttpHandler.Json("{\n  \"error\": null,\n  \"choices\": [{\"message\":{\"content\":\"Hello there\"},\"finish_reason\":\"stop\"}]\n}"));

        LlmReply reply = await CreateClient().CompleteAsync(Request(allowRetry: false), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Single(Http.Requests);
    }

    private void AssertSafeError(string error, bool requireFailureLabel = true)
    {
        string diagnosticText = error + "\n" + string.Join("\n", Monitor.Entries.Select(entry => entry.Message));
        foreach (string secret in new[] { "PRIVATE_DETAIL", "sk-audit-secret", "https://private.invalid", "PRIVATE_REASONING" })
        {
            Assert.DoesNotContain(secret, diagnosticText, StringComparison.Ordinal);
        }

        if (requireFailureLabel)
        {
            Assert.Contains("provider", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("error", error, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HttpResponseMessage ErrorResponse(string shape) => shape switch
    {
        "sse" or "nonstream-sse" => FakeHttpHandler.Text("data: " + ErrorJson + "\n"),
        "ignored-stream-pretty-json" => FakeHttpHandler.Json(JObject.Parse(ErrorJson).ToString(Formatting.Indented)),
        _ => FakeHttpHandler.Json(ErrorJson)
    };

    private static bool UsesStreamingTransport(string shape) => shape is not ("json" or "nonstream-sse");

    private static OpenAiCompatibleClient CreateClient() => new(Settings("OpenAiCompatible", modelName: "gpt-5.6-sol"));
}
