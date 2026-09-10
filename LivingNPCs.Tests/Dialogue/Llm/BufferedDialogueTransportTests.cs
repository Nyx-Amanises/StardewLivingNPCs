using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class BufferedDialogueTransportTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}";
    private const string UsageChunk = "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13,\"prompt_tokens_details\":{\"cached_tokens\":4},\"completion_tokens_details\":{\"reasoning_tokens\":1}}}\n";
    private static readonly TimeSpan BodyStartDeadline = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task MainReplyBuffersAllSseTextAndProviderUsageInOnePost()
    {
        Config.UseStreamingDialogueTransport = true;
        Config.ChatThinkingLevel = LlmThinking.Low;
        var client = CreateClient();
        string sse = "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"你好！\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"\\n聊聊农场吧。\"}}]}\n"
            + UsageChunk
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        LlmReply reply = await client.CompleteAsync(Request(maxTokens: 321), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("你好！\n聊聊农场吧。", reply.Text);
        Assert.Equal(200, reply.HttpStatus);
        AssertProviderUsage(reply.Usage);
        FakeHttpHandler.RecordedRequest sent = Assert.Single(Http.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        var body = JObject.Parse(sent.Body!);
        Assert.True(body.Value<bool>("stream"));
        Assert.True(body["stream_options"]!.Value<bool>("include_usage"));
        Assert.Equal("deepseek-v4-flash", body.Value<string>("model"));
        Assert.Equal("low", body.Value<string>("reasoning_effort"));
        Assert.Equal("enabled", body["thinking"]!.Value<string>("type"));
        Assert.Equal(321, body.Value<int>("max_tokens"));
        Assert.Null(body["response_format"]);
        AssertStandardMessages(body);
    }

    [Theory]
    [InlineData(LlmThinking.Off, "disabled", null)]
    [InlineData(LlmThinking.High, "enabled", "high")]
    public async Task FastPassRemainsNonStreamingAndKeepsItsOwnThinkingLevel(
        string routingLevel, string thinkingType, string? effort)
    {
        Config.UseStreamingDialogueTransport = true;
        Config.ChatThinkingLevel = LlmThinking.Low;
        Config.RoutingThinkingLevel = routingLevel;
        var client = CreateClient();
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: true), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var body = JObject.Parse(Assert.Single(Http.Requests).Body!);
        Assert.False(body.Value<bool?>("stream") == true);
        Assert.Null(body["stream_options"]);
        Assert.Equal(thinkingType, body["thinking"]!.Value<string>("type"));
        Assert.Equal(effort, body.Value<string>("reasoning_effort"));
        if (routingLevel == LlmThinking.Off)
        {
            Assert.Equal("json_object", body["response_format"]!.Value<string>("type"));
        }
        else
        {
            Assert.Null(body["response_format"]);
        }

        AssertStandardMessages(body);
    }

    [Fact]
    public async Task DisabledFlagPreservesTheNonStreamingMainReply()
    {
        Config.UseStreamingDialogueTransport = false;
        Config.ChatThinkingLevel = LlmThinking.Low;
        var client = CreateClient();
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Equal(13, reply.Usage.TotalTokens);
        var body = JObject.Parse(Assert.Single(Http.Requests).Body!);
        Assert.False(body.Value<bool?>("stream") == true);
        Assert.Null(body["stream_options"]);
        Assert.Equal("low", body.Value<string>("reasoning_effort"));
        AssertStandardMessages(body);
    }

    [Fact]
    public async Task RejectedStreamFallsBackToNonStreamingWithoutReenteringBufferedTransport()
    {
        Config.UseStreamingDialogueTransport = true;
        Config.ChatThinkingLevel = LlmThinking.Low;
        var client = CreateClient();
        Http.EnqueueJson("{\"error\":\"stream is unsupported\"}", HttpStatusCode.BadRequest);
        // Returning complete JSON for the second request also bounds a recursive-dispatch regression.
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(allowRetry: false), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Equal(13, reply.Usage.TotalTokens);
        Assert.Equal(2, Http.Requests.Count);
        var streamed = JObject.Parse(Http.Requests[0].Body!);
        var fallback = JObject.Parse(Http.Requests[1].Body!);
        Assert.True(streamed.Value<bool>("stream"));
        Assert.False(fallback.Value<bool?>("stream") == true);
        Assert.Null(fallback["stream_options"]);
        Assert.Equal("low", fallback.Value<string>("reasoning_effort"));
        AssertStandardMessages(fallback);
    }

    [Fact]
    public async Task EndpointIgnoringStreamReturnsItsCompleteJsonOnce()
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        Http.DefaultResponder = _ => FakeHttpHandler.Json(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Equal(13, reply.Usage.TotalTokens);
        Assert.Equal("provider usage", reply.Usage.Source);
        Assert.False(reply.Usage.IsEstimated);
        Assert.True(JObject.Parse(Assert.Single(Http.Requests).Body!).Value<bool>("stream"));
    }

    [Fact]
    public async Task BufferedReplyWithoutCompletionMarkerDiscardsTextAndRetainsUsage()
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"UNFINISHED_REPLY\"}}]}\n"
            + UsageChunk;
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.True(reply.Retryable);
        Assert.Equal(500, reply.HttpStatus);
        Assert.Empty(reply.Text);
        AssertProviderUsage(reply.Usage);
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task BufferedReplyWithFinishReasonAndNoDoneMarkerSucceeds()
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello there\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n"
            + UsageChunk;
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal(200, reply.HttpStatus);
        Assert.Equal("Hello there", reply.Text);
        AssertProviderUsage(reply.Usage);
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task DisabledFlagKeepsLegacyCleanEofBehaviorForPublicStream()
    {
        Config.UseStreamingDialogueTransport = false;
        var client = CreateClient();
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello there\"}}]}\n"
            + UsageChunk;
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        var events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Collection(events,
            item =>
            {
                Assert.Equal(LlmStreamEventKind.TextDelta, item.Kind);
                Assert.Equal("Hello there", item.Text);
            },
            item =>
            {
                Assert.Equal(LlmStreamEventKind.Usage, item.Kind);
                AssertProviderUsage(item.Usage!);
            },
            item => Assert.Equal(LlmStreamEventKind.Done, item.Kind));
        Assert.True(JObject.Parse(Assert.Single(Http.Requests).Body!).Value<bool>("stream"));
    }

    [Theory]
    [InlineData("stop", false)]
    [InlineData("stop", true)]
    [InlineData("length", false)]
    [InlineData("length", true)]
    public async Task CompletedEmptyReplyPreservesUsageAndDoesNotRegenerate(string finishReason, bool whitespaceDelta)
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        string sse = (whitespaceDelta
                ? "data: {\"choices\":[{\"delta\":{\"content\":\" \\n\"}}]}\n"
                : string.Empty)
            + "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"" + finishReason + "\"}]}\n"
            + UsageChunk
            + "data: [DONE]\n";
        Http.DefaultResponder = _ => FakeHttpHandler.Text(sse);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Empty(reply.Text);
        Assert.Equal(200, reply.HttpStatus);
        Assert.Contains($"finish_reason={finishReason}", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_REASONING", reply.ErrorMessage, StringComparison.Ordinal);
        AssertProviderUsage(reply.Usage);
        Assert.Single(Http.Requests);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DoneOnlyEmptyStreamIsTerminalWithoutFinishReasonOrReasoning(bool whitespaceDelta, bool allowRetry)
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        string sse = (whitespaceDelta
                ? "data: {\"choices\":[{\"delta\":{\"content\":\" \\n\"}}]}\n"
                : "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n")
            + "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":17,\"completion_tokens\":0,\"total_tokens\":17}}\n"
            + "data: [DONE]\n";
        Http.DefaultResponder = _ => FakeHttpHandler.Text(sse);

        LlmReply reply = await client.CompleteAsync(Request(allowRetry: allowRetry), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Empty(reply.Text);
        Assert.Equal(200, reply.HttpStatus);
        Assert.Equal(17, reply.Usage.PromptTokens);
        Assert.Equal(0, reply.Usage.ReasoningTokens);
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task NonStreamingRequestReceivingDoneOnlySseDoesNotRegenerate()
    {
        Config.UseStreamingDialogueTransport = false;
        var client = CreateClient();
        Http.DefaultResponder = _ => FakeHttpHandler.Text("data: [DONE]\n");

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Empty(reply.Text);
        Assert.Single(Http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, false, 1)]
    [InlineData(HttpStatusCode.BadGateway, true, 3)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true, 3)]
    [InlineData(HttpStatusCode.GatewayTimeout, false, 1)]
    [InlineData(HttpStatusCode.GatewayTimeout, true, 3)]
    public async Task TransientGatewayFailureKeepsTheSameStreamShapeAndOriginalRetryBudget(
        HttpStatusCode status, bool allowRetry, int expectedRequests)
    {
        Config.UseStreamingDialogueTransport = true;
        Config.ChatThinkingLevel = LlmThinking.Low;
        var client = CreateClient();
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"temporarily unavailable\"}", status);

        LlmReply reply = await client.CompleteAsync(Request(allowRetry: allowRetry), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.True(reply.Retryable);
        Assert.Empty(reply.Text);
        Assert.Equal((int)status, reply.HttpStatus);
        Assert.Equal(expectedRequests, Http.Requests.Count);
        Assert.All(Http.Requests, sent =>
        {
            Assert.Equal(Http.Requests[0].Body, sent.Body);
            var body = JObject.Parse(sent.Body!);
            Assert.True(body.Value<bool>("stream"));
            Assert.Equal("low", body.Value<string>("reasoning_effort"));
            AssertStandardMessages(body);
        });
    }

    [Fact]
    public async Task TransientGatewayFailureCanRecoverWithinTheExistingRetryBudget()
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        Http.EnqueueJson("temporarily unavailable", HttpStatusCode.ServiceUnavailable);
        Http.Enqueue(_ => FakeHttpHandler.Text("data: {\"choices\":[{\"delta\":{\"content\":\"Recovered\"}}]}\n"
            + UsageChunk + "data: [DONE]\n"));

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Recovered", reply.Text);
        AssertProviderUsage(reply.Usage);
        Assert.Equal(2, Http.Requests.Count);
        Assert.Equal(Http.Requests[0].Body, Http.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthenticationFailureRemainsTerminalWithItsOriginalStatus(HttpStatusCode status)
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        Http.DefaultResponder = _ => FakeHttpHandler.Json("authentication rejected", status);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Equal((int)status, reply.HttpStatus);
        Assert.Single(Http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task MidReplyAbortDiscardsPartialTextAndRetainsAlreadyReadUsage(HttpStatusCode status)
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        Exception failure = status == HttpStatusCode.InternalServerError
            ? new IOException("simulated connection reset")
            : new HttpRequestException("simulated connection reset", null, status);
        string head = "data: {\"choices\":[{\"delta\":{\"content\":\"UNFINISHED_REPLY\"}}]}\n" + UsageChunk;
        using var stream = new ControlledSseStream(head, failure);
        Http.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamedSseContent(stream) });

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.True(reply.Retryable);
        Assert.Empty(reply.Text);
        Assert.Equal((int)status, reply.HttpStatus);
        Assert.Contains("Streaming connection lost mid-reply", reply.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("UNFINISHED_REPLY", reply.ErrorMessage, StringComparison.Ordinal);
        AssertProviderUsage(reply.Usage);
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task CallerCancellationDuringBodyReadPropagatesWithoutReturningPartialTextOrRetrying()
    {
        Config.UseStreamingDialogueTransport = true;
        var client = CreateClient();
        using var caller = new CancellationTokenSource();
        using var stream = new ControlledSseStream("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n");
        Http.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamedSseContent(stream) });
        Task<LlmReply> completion = client.CompleteAsync(Request(), caller.Token);

        try
        {
            await stream.BodyBlocked.WaitAsync(BodyStartDeadline);
            Assert.False(completion.IsCompleted);
            caller.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => completion.WaitAsync(BodyStartDeadline));

            Assert.True(stream.IsDisposed);
            Assert.Single(Http.Requests);
        }
        finally
        {
            caller.Cancel();
            stream.Dispose();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimeoutCoversResponseBodyAndPropagatesCancellation(bool useRequestOverride)
    {
        Config.UseStreamingDialogueTransport = true;
        // The configured fallback has an intentional five-second minimum; the override must win.
        Config.QueryTimeout = useRequestOverride ? 120 : 5;
        var client = CreateClient();
        using var caller = new CancellationTokenSource();
        using var stream = new ControlledSseStream();
        Http.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamedSseContent(stream) });
        var request = new LlmRequest
        {
            SystemPrompt = "SYS",
            Tail = "TAIL",
            AllowRetry = true,
            TimeoutOverride = useRequestOverride ? TimeSpan.FromMilliseconds(250) : null
        };
        Task<LlmReply> completion = client.CompleteAsync(request, caller.Token);

        try
        {
            await stream.BodyBlocked.WaitAsync(BodyStartDeadline);
            TimeSpan completionDeadline = useRequestOverride ? BodyStartDeadline : TimeSpan.FromSeconds(8);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => completion.WaitAsync(completionDeadline));

            Assert.False(caller.IsCancellationRequested);
            Assert.True(stream.IsDisposed);
            Assert.Single(Http.Requests);
        }
        finally
        {
            caller.Cancel();
            stream.Dispose();
        }
    }

    private static OpenAiCompatibleClient CreateClient()
    {
        return new OpenAiCompatibleClient(Settings("OpenAiCompatible", modelName: "deepseek-v4-flash"));
    }

    private static void AssertProviderUsage(TokenUsage usage)
    {
        Assert.Equal(10, usage.PromptTokens);
        Assert.Equal(3, usage.CompletionTokens);
        Assert.Equal(13, usage.TotalTokens);
        Assert.Equal(4, usage.CachedPromptTokens);
        Assert.Equal(1, usage.ReasoningTokens);
        Assert.False(usage.IsEstimated);
        Assert.Equal("provider usage", usage.Source);
    }

    private static void AssertStandardMessages(JObject body)
    {
        Assert.Null(body["instructions"]);
        var messages = (JArray)body["messages"]!;
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Value<string>("role"));
        Assert.Equal("SYS", messages[0].Value<string>("content"));
        Assert.Equal("user", messages[1].Value<string>("role"));
        Assert.Equal("WORLDNPCTAIL", messages[1].Value<string>("content"));
    }

    private sealed class StreamedSseContent : HttpContent
    {
        private readonly Stream stream;

        public StreamedSseContent(Stream stream)
        {
            this.stream = stream;
            Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);

        protected override Task SerializeToStreamAsync(Stream destination, TransportContext? context)
            => throw new NotSupportedException("This response must be consumed as a stream.");

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Supplies a prefix, then either fails or waits until the response is disposed.</summary>
    private sealed class ControlledSseStream : Stream
    {
        private readonly byte[] head;
        private readonly Exception? failure;
        private readonly TaskCompletionSource<bool> bodyBlocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int position;

        public ControlledSseStream(string head = "", Exception? failure = null)
        {
            this.head = Encoding.UTF8.GetBytes(head);
            this.failure = failure;
        }

        public Task BodyBlocked => bodyBlocked.Task;

        public bool IsDisposed => disposed.Task.IsCompleted;

        public override bool CanRead => !IsDisposed;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position < head.Length)
            {
                int copied = Math.Min(buffer.Length, head.Length - position);
                head.AsMemory(position, copied).CopyTo(buffer);
                position += copied;
                return copied;
            }

            bodyBlocked.TrySetResult(true);
            if (failure != null)
            {
                throw failure;
            }

            // StreamReader.ReadLineAsync on .NET 6 has no cancellation token. The production
            // response-disposal registration must release a blocked body read instead.
            await disposed.Task.ConfigureAwait(false);
            throw new ObjectDisposedException(nameof(ControlledSseStream));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                disposed.TrySetResult(true);
            }

            base.Dispose(disposing);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
