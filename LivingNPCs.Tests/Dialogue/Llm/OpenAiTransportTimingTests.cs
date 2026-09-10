using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class OpenAiTransportTimingTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"},\"finish_reason\":\"stop\"}]}";
    private const string ContentChunk = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello there\"}}]}\n";
    private static readonly TimeSpan GateDeadline = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task BufferedStreamSeparatesHeadersFirstContentAndCompletionWithoutTimingReasoning()
    {
        Config.UseStreamingDialogueTransport = true;
        using var stream = new GatedStream(
            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"PRIVATE_REASONING\"}}]}\n"
                + "data: {\"choices\":[{\"delta\":{\"content\":\" \\n\"}}]}\n",
            ContentChunk,
            "data: [DONE]\n");
        Http.Enqueue(_ => Response(stream));
        var timings = new List<LlmTransportTiming>();
        using var caller = new CancellationTokenSource();
        Task<LlmReply> pending = CreateClient().CompleteAsync(TimedRequest(timings.Add), caller.Token);
        try
        {
            await stream.BeforeContent.WaitAsync(GateDeadline);
            Assert.Empty(timings);
            await Task.Delay(40);
            stream.ReleaseContent();
            await stream.BeforeEnd.WaitAsync(GateDeadline);
            Assert.Empty(timings);
            await Task.Delay(40);
            stream.ReleaseEnd();

            LlmReply reply = await pending.WaitAsync(GateDeadline);

            Assert.True(reply.IsSuccess);
            LlmTransportTiming timing = Assert.Single(timings);
            Assert.True(timing.Streaming);
            Assert.NotNull(timing.HeadersMilliseconds);
            Assert.NotNull(timing.FirstContentMilliseconds);
            Assert.True(timing.FirstContentMilliseconds!.Value - timing.HeadersMilliseconds!.Value >= 30);
            Assert.True(timing.CompleteMilliseconds - timing.FirstContentMilliseconds.Value >= 30);
            Assert.DoesNotContain("PRIVATE_REASONING", timing.ToString(), StringComparison.Ordinal);
            Assert.Single(Http.Requests);
        }
        finally
        {
            caller.Cancel();
            stream.ReleaseContent();
            stream.ReleaseEnd();
        }
    }

    [Fact]
    public async Task StreamCompatibilityFallbackReportsBothPhysicalRequestsThroughTheClonedRequest()
    {
        Config.UseStreamingDialogueTransport = true;
        Http.EnqueueJson("{\"error\":\"stream unsupported\"}", HttpStatusCode.BadRequest);
        Http.EnqueueJson(CompletionJson);
        var timings = new List<LlmTransportTiming>();

        LlmReply reply = await CreateClient().CompleteAsync(TimedRequest(timings.Add), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal(2, Http.Requests.Count);
        Assert.Collection(timings,
            stream =>
            {
                Assert.True(stream.Streaming);
                Assert.Null(stream.HeadersMilliseconds);
                Assert.Null(stream.FirstContentMilliseconds);
                Assert.True(stream.CompleteMilliseconds >= 0);
            },
            buffered =>
            {
                Assert.False(buffered.Streaming);
                Assert.Null(buffered.HeadersMilliseconds);
                Assert.Null(buffered.FirstContentMilliseconds);
                Assert.True(buffered.CompleteMilliseconds >= 0);
            });
    }

    [Fact]
    public async Task GatewayIgnoringStreamReportsContentOnlyWhenItsJsonIsAvailable()
    {
        Config.UseStreamingDialogueTransport = true;
        Http.EnqueueJson(CompletionJson);
        var timings = new List<LlmTransportTiming>();

        LlmReply reply = await CreateClient().CompleteAsync(TimedRequest(timings.Add), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        LlmTransportTiming timing = Assert.Single(timings);
        Assert.True(timing.Streaming);
        Assert.NotNull(timing.HeadersMilliseconds);
        Assert.NotNull(timing.FirstContentMilliseconds);
        Assert.True(timing.FirstContentMilliseconds >= timing.HeadersMilliseconds);
        Assert.True(timing.CompleteMilliseconds >= timing.FirstContentMilliseconds);
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task CancelledPartialReplyStillReportsItsObservedMilestonesOnce()
    {
        Config.UseStreamingDialogueTransport = true;
        using var stream = new GatedStream(ContentChunk, string.Empty, "data: [DONE]\n");
        Http.Enqueue(_ => Response(stream));
        var timings = new List<LlmTransportTiming>();
        using var caller = new CancellationTokenSource();
        Task<LlmReply> pending = CreateClient().CompleteAsync(TimedRequest(timings.Add), caller.Token);
        try
        {
            await stream.BeforeContent.WaitAsync(GateDeadline);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending.WaitAsync(GateDeadline));

            LlmTransportTiming timing = Assert.Single(timings);
            Assert.True(timing.Streaming);
            Assert.NotNull(timing.HeadersMilliseconds);
            Assert.NotNull(timing.FirstContentMilliseconds);
            Assert.True(timing.CompleteMilliseconds >= timing.FirstContentMilliseconds);
            Assert.Single(Http.Requests);
        }
        finally
        {
            caller.Cancel();
            stream.ReleaseContent();
            stream.ReleaseEnd();
        }
    }

    [Fact]
    public async Task CompletedEmptyStreamReportsHeadersWithoutInventingFirstContent()
    {
        Config.UseStreamingDialogueTransport = true;
        Http.Enqueue(_ => FakeHttpHandler.Text("data: [DONE]\n"));
        var timings = new List<LlmTransportTiming>();

        LlmReply reply = await CreateClient().CompleteAsync(TimedRequest(timings.Add), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        LlmTransportTiming timing = Assert.Single(timings);
        Assert.True(timing.Streaming);
        Assert.NotNull(timing.HeadersMilliseconds);
        Assert.Null(timing.FirstContentMilliseconds);
        Assert.True(timing.CompleteMilliseconds >= timing.HeadersMilliseconds);
        Assert.Single(Http.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingObserverCannotChangeTheReplyOrCauseAnotherRequest(bool streaming)
    {
        Config.UseStreamingDialogueTransport = streaming;
        Http.Enqueue(_ => streaming
            ? FakeHttpHandler.Text(ContentChunk + "data: [DONE]\n")
            : FakeHttpHandler.Json(CompletionJson));

        LlmReply reply = await CreateClient().CompleteAsync(
            TimedRequest(_ => throw new InvalidOperationException("diagnostic observer failed")), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello there", reply.Text);
        Assert.Single(Http.Requests);
    }

    private static OpenAiCompatibleClient CreateClient() =>
        new(Settings("OpenAiCompatible", modelName: "gpt-5.6-sol"));

    private static LlmRequest TimedRequest(Action<LlmTransportTiming> observer) => new()
    {
        SystemPrompt = "SYS",
        StableContext = "WORLD",
        NpcContext = "NPC",
        Tail = "TAIL",
        AllowRetry = false,
        TransportTimingObserver = observer
    };

    private static HttpResponseMessage Response(Stream stream) => new(HttpStatusCode.OK)
    {
        Content = new ProvidedStreamContent(stream)
    };

    private sealed class ProvidedStreamContent : HttpContent
    {
        private readonly Stream stream;

        public ProvidedStreamContent(Stream stream) => this.stream = stream;

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(this.stream);

        protected override Task SerializeToStreamAsync(Stream destination, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Separates response headers, first real content, and end-of-stream without using a server.</summary>
    private sealed class GatedStream : Stream
    {
        private readonly byte[][] parts;
        private readonly TaskCompletionSource<bool>[] waiting = { NewSignal(), NewSignal() };
        private readonly TaskCompletionSource<bool>[] released = { NewSignal(), NewSignal() };
        private int part;
        private int position;

        public GatedStream(string prefix, string content, string ending) =>
            this.parts = new[] { Encoding.UTF8.GetBytes(prefix), Encoding.UTF8.GetBytes(content), Encoding.UTF8.GetBytes(ending) };

        public Task BeforeContent => this.waiting[0].Task;
        public Task BeforeEnd => this.waiting[1].Task;
        public void ReleaseContent() => this.released[0].TrySetResult(true);
        public void ReleaseEnd() => this.released[1].TrySetResult(true);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (this.part < this.parts.Length)
            {
                if (this.part > 0)
                {
                    this.waiting[this.part - 1].TrySetResult(true);
                    await this.released[this.part - 1].Task.WaitAsync(cancellationToken);
                }

                if (this.position < this.parts[this.part].Length)
                {
                    int copied = Math.Min(buffer.Length, this.parts[this.part].Length - this.position);
                    this.parts[this.part].AsMemory(this.position, copied).CopyTo(buffer);
                    this.position += copied;
                    return copied;
                }

                this.part++;
                this.position = 0;
            }

            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            this.ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.ReleaseContent();
                this.ReleaseEnd();
            }

            base.Dispose(disposing);
        }

        private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
