using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Llm;

/// <summary>
/// 审查发现 2/3 的回归钉子。
/// 发现 2：usage 的 prompt_tokens_details / completion_tokens_details（及 choices[0].message、
/// SSE delta）被兼容端点写成字面量 null 时不得抛异常、不得把成功响应判为失败。
/// 发现 3：流式中途断连（已交付增量后传输异常）必须记日志并以 LlmStreamException 上抛，
/// 让调用方丢弃半截文本；熔断器经异常路径记失败。自然收尾（[DONE] 或干净 EOF）仍算成功。
/// </summary>
[Collection("LlmLayer")]
public sealed class UsageNullDetailsAndStreamAbortTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Recovered\"}}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":1,\"total_tokens\":6}}";

    // ---- 发现 2：字面量 null 容错 ----

    [Fact]
    public void FromOpenAiUsage_ToleratesNullDetailsFields()
    {
        var usage = JObject.Parse(
            "{\"prompt_tokens\":5,\"completion_tokens\":2,\"total_tokens\":7,"
            + "\"prompt_tokens_details\":null,\"completion_tokens_details\":null}");

        TokenUsage parsed = TokenUsage.FromOpenAiUsage(usage);

        Assert.Equal(5, parsed.PromptTokens);
        Assert.Equal(2, parsed.CompletionTokens);
        Assert.Equal(7, parsed.TotalTokens);
        Assert.Equal(0, parsed.CachedPromptTokens);
        Assert.Equal(0, parsed.ReasoningTokens);
    }

    [Fact]
    public void FromOpenAiUsage_StillReadsDetailsObjectsAndDeepSeekLegacyField()
    {
        var withObjects = TokenUsage.FromOpenAiUsage(JObject.Parse(
            "{\"prompt_tokens\":10,\"completion_tokens\":4,"
            + "\"prompt_tokens_details\":{\"cached_tokens\":6},"
            + "\"completion_tokens_details\":{\"reasoning_tokens\":3}}"));
        Assert.Equal(6, withObjects.CachedPromptTokens);
        Assert.Equal(3, withObjects.ReasoningTokens);

        // details 为 null 时仍能落到 DeepSeek 旧字段。
        var legacy = TokenUsage.FromOpenAiUsage(JObject.Parse(
            "{\"prompt_tokens\":10,\"prompt_tokens_details\":null,\"prompt_cache_hit_tokens\":3}"));
        Assert.Equal(3, legacy.CachedPromptTokens);
    }

    [Fact]
    public async Task NullDetailsInUsageDoesNotFailSuccessfulCompletion()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.EnqueueJson(
            "{\"choices\":[{\"message\":{\"content\":\"你好呀\"}}],"
            + "\"usage\":{\"prompt_tokens\":8,\"completion_tokens\":3,\"total_tokens\":11,"
            + "\"prompt_tokens_details\":null,\"completion_tokens_details\":null}}");

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("你好呀", reply.Text);
        Assert.Equal(11, reply.Usage.TotalTokens);
        // 修复前该响应会抛 InvalidOperationException 并烧光 3 次重试；现在一发即成。
        Assert.Single(Http.Requests);
    }

    [Fact]
    public void ExtractTextAndUsage_ToleratesNullMessageAndNullChoiceElements()
    {
        // message 为字面量 null：回落旧式 text 字段，不抛异常。
        (string? text, _) = OpenAiChatClientBase.ExtractTextAndUsage(
            "{\"choices\":[{\"message\":null,\"text\":\"old style\"}]}");
        Assert.Equal("old style", text);

        // choices[0] 为字面量 null：不抛异常，按无文本处理。
        (string? nullChoice, _) = OpenAiChatClientBase.ExtractTextAndUsage("{\"choices\":[null]}");
        Assert.True(string.IsNullOrEmpty(nullChoice));

        // SSE 重组路径里 delta 为字面量 null 的块被跳过，不打断后续块。
        (string? sseText, _) = OpenAiChatClientBase.ExtractTextAndUsage(
            "data: {\"choices\":[{\"delta\":null}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n"
            + "data: [DONE]\n");
        Assert.Equal("ok", sseText);
    }

    [Fact]
    public async Task StreamingUsageChunkWithNullDetailsStillYieldsProviderUsage()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"嗨\"}}]}\n"
            + "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2,\"total_tokens\":12,"
            + "\"prompt_tokens_details\":null,\"completion_tokens_details\":null}}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Equal(
            new[] { LlmStreamEventKind.TextDelta, LlmStreamEventKind.Usage, LlmStreamEventKind.Done },
            events.Select(e => e.Kind).ToArray());
        // 修复前 usage 块解析抛异常打断流，只能拿到估算 usage；现在读到真实 usage。
        Assert.Equal("provider usage", events[1].Usage!.Source);
        Assert.Equal(12, events[1].Usage!.TotalTokens);
        Assert.Equal(0, events[1].Usage!.CachedPromptTokens);
    }

    // ---- 发现 3：中途断连 vs 自然收尾 ----

    [Fact]
    public async Task MidStreamAbortAfterDeltasThrowsAndLogsInsteadOfFakeSuccess()
    {
        var client = new OpenAiClient(Settings("OpenAI"));
        string head = "data: {\"choices\":[{\"delta\":{\"content\":\"Hel\"}}]}\n"
            + "data: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}\n";
        Http.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ExplodingSseContent(head) });

        var events = new List<LlmStreamEvent>();
        LlmStreamException exception = await Assert.ThrowsAsync<LlmStreamException>(async () =>
        {
            await foreach (LlmStreamEvent item in client.StreamAsync(Request(), CancellationToken.None))
            {
                events.Add(item);
            }
        });

        // 断连前的增量已交付；之后没有 Usage/Done 假成功收尾。
        Assert.Equal(new[] { LlmStreamEventKind.TextDelta, LlmStreamEventKind.TextDelta }, events.Select(e => e.Kind).ToArray());
        Assert.Equal("Hel", events[0].Text);
        Assert.Equal("lo", events[1].Text);
        Assert.Contains("Streaming connection lost mid-reply", exception.Message, StringComparison.Ordinal);
        Assert.Contains("simulated connection reset", exception.Message, StringComparison.Ordinal);
        // 已交付增量后不得在本层重试（会把新增量拼在旧残句后面）：只打了一个请求。
        Assert.Single(Http.Requests);
        // 中途断连必须留下含异常信息的日志。
        Assert.True(Monitor.Contains(LogLevel.Warn, "Streaming connection lost mid-reply"));
        Assert.True(Monitor.Contains(LogLevel.Warn, "simulated connection reset"));
    }

    [Fact]
    public async Task PreDeltaTransportErrorStillRetriesThenFallsBackToNonStreaming()
    {
        // 尚未交付任何增量的传输异常保持既有行为：3 次流式重试 + 非流式兜底，不上抛。
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.DefaultResponder = request =>
        {
            bool isStream = JObject.Parse(request.Body!).Value<bool?>("stream") == true;
            return isStream
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ExplodingSseContent(string.Empty) }
                : FakeHttpHandler.Json(CompletionJson);
        };

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Equal(4, Http.Requests.Count);
        Assert.Equal("Recovered", events[0].Text);
        Assert.Equal(LlmStreamEventKind.Done, events[^1].Kind);
    }

    [Fact]
    public async Task CleanEofWithoutDoneStillCountsAsSuccess()
    {
        // 自然 EOF（无 [DONE]，服务端干净关流）仍按成功收尾——与中途异常严格区分。
        var client = new OpenAiClient(Settings("OpenAI"));
        Http.Enqueue(_ => FakeHttpHandler.Text("data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n"));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Equal(
            new[] { LlmStreamEventKind.TextDelta, LlmStreamEventKind.Usage, LlmStreamEventKind.Done },
            events.Select(e => e.Kind).ToArray());
        Assert.Equal("Hi", events[0].Text);
        Assert.False(Monitor.Contains(LogLevel.Warn, "Streaming connection lost mid-reply"));
    }

    [Fact]
    public async Task GuardedStreamRecordsMidAbortAsFailureNotSuccess()
    {
        // 熔断器视角：中途断连经 LlmStreamException 路径记"失败"。预置 4 次 429 后，
        // 一次携带 429 的中途断连应作为第 5 次连续失败触发熔断——若被误记成功则计数清零、不会触发。
        var breaker = new LlmCircuitBreaker();
        for (int i = 0; i < LlmCircuitBreaker.RateTripThreshold - 1; i++)
        {
            Assert.Equal(LlmSuspensionKind.None, breaker.RecordOutcome(success: false, 429));
        }

        LlmSuspensionKind tripped = LlmSuspensionKind.None;
        var guarded = new BreakerGuardedLlmClient(new MidAbortStubClient(), breaker, kind => tripped = kind);

        var events = new List<LlmStreamEvent>();
        await Assert.ThrowsAsync<LlmStreamException>(async () =>
        {
            await foreach (LlmStreamEvent item in guarded.StreamAsync(Request(), CancellationToken.None))
            {
                events.Add(item);
            }
        });

        Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.TextDelta, events[0].Kind);
        Assert.Equal(LlmSuspensionKind.RateLimit, tripped);
        Assert.Equal(LlmSuspensionKind.RateLimit, breaker.CurrentSuspension);
    }

    // ---- 测试基建：交付部分字节后抛 IOException 的假 SSE 内容 ----

    private sealed class ExplodingSseContent : HttpContent
    {
        private readonly byte[] _head;

        public ExplodingSseContent(string head)
        {
            _head = Encoding.UTF8.GetBytes(head);
            Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new ExplodingStream(_head));
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new IOException("simulated connection reset");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    private sealed class ExplodingStream : Stream
    {
        private readonly byte[] _head;
        private int _position;

        public ExplodingStream(byte[] head)
        {
            _head = head;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < _head.Length)
            {
                int copied = Math.Min(count, _head.Length - _position);
                Array.Copy(_head, _position, buffer, offset, copied);
                _position += copied;
                return copied;
            }

            throw new IOException("simulated connection reset");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>吐一个增量后以 429 流式异常中断的桩客户端（模拟提供商层的中途断连上抛）。</summary>
    private sealed class MidAbortStubClient : LlmClientBase
    {
        public MidAbortStubClient()
            : base(new LlmConnectionSettings { Provider = "OpenAI", ApiKey = "stub" })
        {
        }

        public override string ProviderId => "Stub";

        protected override string DefaultModelName => "stub";

        protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
        {
            return Array.Empty<RequestCandidate>();
        }

        protected override Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
        {
            throw new NotSupportedException("stub");
        }

        public override async IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return LlmStreamEvent.Delta("partial");
            await Task.Yield();
            throw new LlmStreamException("Streaming connection lost mid-reply: connection reset", 429);
        }
    }
}
