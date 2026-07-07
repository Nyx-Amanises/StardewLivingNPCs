using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using StardewModdingAPI;
using StardewModdingAPI.Framework.Logging;

namespace LivingNPCs.Tests.Dialogue.Llm;

/// <summary>
/// WP11 测试共用夹具。LLM 层大量依赖进程级静态（共享 HttpClient、重试间隔覆盖、平台开关、
/// DialogueServices），因此所有 WP11 测试类挂在同一 collection 里互相串行，
/// 并由基类在每个测试前后设置/还原静态状态；与其他并行测试类共享的全局默认值保持不变。
/// </summary>
[CollectionDefinition("LlmLayer")]
public sealed class LlmLayerCollection
{
}

public abstract class LlmTestBase : IDisposable
{
    protected FakeMonitor Monitor { get; } = new();

    protected FakeHttpHandler Http { get; } = new();

    internal DialogueConfig Config { get; } = new();

    protected LlmTestBase()
    {
        DialogueServices.Initialize(null!, Monitor, Config);
        LlmHttp.SetHandlerForTests(Http);
        LlmClientBase.RetryDelayOverrideForTests = TimeSpan.Zero;
        AndroidPlatform.OverrideForTests = false;
        NetworkAvailability.ProbeForTests = null;
        LlmHudNotifier.SinkForTests = null;
    }

    public void Dispose()
    {
        LlmHttp.SetHandlerForTests(null);
        LlmClientBase.RetryDelayOverrideForTests = null;
        AndroidPlatform.OverrideForTests = null;
        NetworkAvailability.ProbeForTests = null;
        NetworkAvailability.RecheckDelay = TimeSpan.FromSeconds(1);
        LlmHudNotifier.SinkForTests = null;
        LegacyLlm.Instance = new LegacyLlmDummy();
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    internal static LlmConnectionSettings Settings(
        string provider,
        string apiKey = "test-key",
        string modelName = "",
        string serverAddress = "https://openrouter.ai/api",
        string promptFormat = "[INST] {system}\n{prompt}[/INST]\n{response_start}",
        bool suppressConnectionCheck = true)
    {
        return new LlmConnectionSettings
        {
            Provider = provider,
            ApiKey = apiKey,
            ModelName = modelName,
            ServerAddress = serverAddress,
            PromptFormat = promptFormat,
            SuppressConnectionCheck = suppressConnectionCheck
        };
    }

    internal static LlmRequest Request(
        string system = "SYS",
        string stable = "WORLD",
        string npc = "NPC",
        string tail = "TAIL",
        bool allowRetry = true,
        bool disableThinking = false,
        int maxTokens = 2048,
        string responseStart = "")
    {
        return new LlmRequest
        {
            SystemPrompt = system,
            StableContext = stable,
            NpcContext = npc,
            Tail = tail,
            AllowRetry = allowRetry,
            DisableThinking = disableThinking,
            MaxTokens = maxTokens,
            ResponseStart = responseStart
        };
    }

    internal static async Task<List<LlmStreamEvent>> CollectAsync(IAsyncEnumerable<LlmStreamEvent> stream, CancellationToken ct = default)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (LlmStreamEvent item in stream.WithCancellation(ct))
        {
            events.Add(item);
        }

        return events;
    }
}

public sealed class FakeMonitor : IMonitor
{
    private readonly object _gate = new();

    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public bool IsVerbose => false;

    public void Log(string message, LogLevel level = LogLevel.Trace)
    {
        lock (_gate)
        {
            Entries.Add((level, message));
        }
    }

    public void LogOnce(string message, LogLevel level = LogLevel.Trace)
    {
        Log(message, level);
    }

    public void VerboseLog(string message)
    {
    }

    public void VerboseLog(ref VerboseLogStringHandler message)
    {
    }

    public IReadOnlyList<string> MessagesAt(LogLevel level)
    {
        lock (_gate)
        {
            return Entries.Where(entry => entry.Level == level).Select(entry => entry.Message).ToList();
        }
    }

    public bool Contains(LogLevel level, string substring)
    {
        return MessagesAt(level).Any(message => message.Contains(substring, StringComparison.Ordinal));
    }
}

public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<RecordedRequest, HttpResponseMessage>> _responders = new();

    public sealed record RecordedRequest(
        HttpMethod Method,
        string Url,
        string? Body,
        string? Authorization,
        IReadOnlyDictionary<string, string> Headers);

    public List<RecordedRequest> Requests { get; } = new();

    public Func<RecordedRequest, HttpResponseMessage>? DefaultResponder { get; set; }

    public void Enqueue(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        lock (_gate)
        {
            _responders.Enqueue(responder);
        }
    }

    public void EnqueueJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        Enqueue(_ => Json(json, status));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content == null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri?.ToString() ?? string.Empty,
            body,
            request.Headers.Authorization?.ToString(),
            headers);

        Func<RecordedRequest, HttpResponseMessage>? responder;
        lock (_gate)
        {
            Requests.Add(recorded);
            responder = _responders.Count > 0 ? _responders.Dequeue() : DefaultResponder;
        }

        if (responder == null)
        {
            throw new InvalidOperationException($"No fake responder configured for {recorded.Method} {recorded.Url}");
        }

        return responder(recorded);
    }
}
