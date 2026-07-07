using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class LlmClientHostTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Connection successful\"}}]}";

    [Fact]
    public void InvalidProviderDisablesEngineWithoutCrashing()
    {
        var host = new LlmClientHost();

        host.ReplaceClient(Settings("NoSuchProvider"));

        Assert.True(host.EngineDisabled);
        Assert.Null(host.Current);
        Assert.False(host.CanGenerate);
        Assert.True(Monitor.Contains(LogLevel.Error, "Invalid LLM type: NoSuchProvider"));

        // 换回有效配置后恢复（清除禁用标志）。
        host.ReplaceClient(Settings("OpenAI"));
        Assert.False(host.EngineDisabled);
        Assert.True(host.CanGenerate);
    }

    [Fact]
    public async Task AuthBreakerSuspendsAfterTwoFailedRepliesUntilConfigChange()
    {
        var hudMessages = new List<string>();
        LlmHudNotifier.SinkForTests = hudMessages.Add;
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"bad key\"}", HttpStatusCode.Unauthorized);

        LlmRequest request = Request(allowRetry: false);
        LlmReply first = await host.Current!.CompleteAsync(request, CancellationToken.None);
        LlmReply second = await host.Current!.CompleteAsync(request, CancellationToken.None);

        Assert.False(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(LlmSuspensionKind.Auth, host.Suspension);
        Assert.False(host.CanGenerate);
        // 一次性 HUD 提示 + SMAPI error 日志。
        Assert.Single(hudMessages);
        Assert.Contains("401/403", hudMessages[0], StringComparison.Ordinal);
        Assert.Single(Monitor.MessagesAt(LogLevel.Error), m => m.Contains("401/403", StringComparison.Ordinal));

        // 挂起期间生成入口直接失败，不打网络请求。
        int requestsBefore = Http.Requests.Count;
        LlmReply suspended = await host.Current!.CompleteAsync(request, CancellationToken.None);
        Assert.False(suspended.IsSuccess);
        Assert.Equal(401, suspended.HttpStatus);
        Assert.Equal(requestsBefore, Http.Requests.Count);

        // 配置变更（重建客户端）复位；密钥不做定时自愈。
        host.ReplaceClient(Settings("OpenAI", apiKey: "fixed-key"));
        Assert.Equal(LlmSuspensionKind.None, host.Suspension);
        Assert.True(host.CanGenerate);
    }

    [Fact]
    public async Task RateBreakerRecoversAfterTenMinuteCooldown()
    {
        DateTime now = DateTime.UtcNow;
        var host = new LlmClientHost { ClockForTests = () => now };
        host.ReplaceClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"slow down\"}", HttpStatusCode.TooManyRequests);

        LlmRequest request = Request(allowRetry: false);
        for (int i = 0; i < 5; i++)
        {
            await host.Current!.CompleteAsync(request, CancellationToken.None);
        }

        Assert.Equal(LlmSuspensionKind.RateLimit, host.Suspension);

        now += TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1);
        Assert.Equal(LlmSuspensionKind.None, host.Suspension);
        Assert.True(host.CanGenerate);
    }

    [Fact]
    public async Task SuccessfulReplyClearsBreakerCounters()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI"));
        LlmRequest request = Request(allowRetry: false);

        Http.EnqueueJson("{\"error\":\"bad key\"}", HttpStatusCode.Unauthorized);
        await host.Current!.CompleteAsync(request, CancellationToken.None);
        Http.EnqueueJson(CompletionJson);
        await host.Current!.CompleteAsync(request, CancellationToken.None);
        Http.EnqueueJson("{\"error\":\"bad key\"}", HttpStatusCode.Unauthorized);
        await host.Current!.CompleteAsync(request, CancellationToken.None);

        Assert.Equal(LlmSuspensionKind.None, host.Suspension);
    }

    [Fact]
    public async Task SuspendedStreamThrowsWithoutNetwork()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI"));
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{}", HttpStatusCode.Unauthorized);
        LlmRequest request = Request(allowRetry: false);
        await host.Current!.CompleteAsync(request, CancellationToken.None);
        await host.Current!.CompleteAsync(request, CancellationToken.None);
        int requestsBefore = Http.Requests.Count;

        var exception = await Assert.ThrowsAsync<LlmStreamException>(
            async () => await CollectAsync(host.Current!.StreamAsync(request, CancellationToken.None)));

        Assert.Equal(401, exception.HttpStatus);
        Assert.Equal(requestsBefore, Http.Requests.Count);
    }

    [Fact]
    public void ReplaceClientBridgesLegacyLlmEntryPoint()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI"));
        Assert.IsType<LegacyLlmBridge>(LegacyLlm.Instance);

        host.ReplaceClient(Settings("NoSuchProvider"));
        Assert.IsType<LegacyLlmDummy>(LegacyLlm.Instance);
    }

    [Fact]
    public async Task LegacyBridgeForwardsFourSegmentsToCurrentClient()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI"));
        Http.EnqueueJson(CompletionJson);

        LlmResponse response = await LegacyLlm.Instance.RunInference("S", "G", "N", "P", allowRetry: false);

        Assert.True(response.IsSuccess);
        var body = Newtonsoft.Json.Linq.JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal("S", body["messages"]![0]!.Value<string>("content"));
        Assert.Equal("GNP", body["messages"]![1]!.Value<string>("content"));
    }
}

[Collection("LlmLayer")]
public sealed class ConnectionSelfCheckTests : LlmTestBase
{
    [Fact]
    public async Task SuppressedCheckMakesZeroNetworkCalls()
    {
        var host = new LlmClientHost();

        host.ReplaceClient(Settings("OpenAI", suppressConnectionCheck: true));
        await Task.Yield();

        Assert.Null(host.LastSelfCheck);
        Assert.Empty(Http.Requests);
    }

    [Fact]
    public async Task SuccessfulCheckLogsSingleInfo()
    {
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"choices\":[{\"message\":{\"content\":\"Connection successful\"}}]}");
        var host = new LlmClientHost();

        host.ReplaceClient(Settings("OpenAI", suppressConnectionCheck: false));
        await host.LastSelfCheck!;

        Assert.True(Monitor.Contains(LogLevel.Info, "connection check passed"));
        Assert.DoesNotContain(Monitor.Entries, e => e.Level == LogLevel.Error);
        // 自检请求禁止重试：只打一发。
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task FailureWithEmptyApiKeySuggestsConfiguringKeyAndNeverDisablesEngine()
    {
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"denied\"}", HttpStatusCode.Unauthorized);
        var host = new LlmClientHost();

        host.ReplaceClient(Settings("OpenAI", apiKey: "", suppressConnectionCheck: false));
        await host.LastSelfCheck!;

        Assert.True(Monitor.Contains(LogLevel.Error, "connection check failed"));
        Assert.True(Monitor.Contains(LogLevel.Error, "No API key is configured"));
        Assert.True(Monitor.Contains(LogLevel.Warn, "stays enabled"));
        // 自检失败但引擎保持启用（钉死语义），且不计入熔断。
        Assert.True(host.CanGenerate);
        Assert.Equal(LlmSuspensionKind.None, host.Suspension);
    }

    [Fact]
    public async Task FailureWithValidModelNameGradesTowardsProviderSideCauses()
    {
        Http.DefaultResponder = request => request.Method == HttpMethod.Get
            ? FakeHttpHandler.Json("{\"data\":[{\"id\":\"gpt-4o\"},{\"id\":\"gpt-4o-mini\"}]}")
            : FakeHttpHandler.Json("{\"error\":\"boom\"}", HttpStatusCode.InternalServerError);
        var host = new LlmClientHost();

        host.ReplaceClient(Settings("OpenAI", modelName: "gpt-4o", suppressConnectionCheck: false));
        await host.LastSelfCheck!;

        Assert.True(Monitor.Contains(LogLevel.Error, "lists the configured model"));
    }

    [Fact]
    public async Task FailureWithUnknownModelNamePointsAtModelName()
    {
        Http.DefaultResponder = request => request.Method == HttpMethod.Get
            ? FakeHttpHandler.Json("{\"data\":[{\"id\":\"gpt-4o\"}]}")
            : FakeHttpHandler.Json("{\"error\":\"boom\"}", HttpStatusCode.InternalServerError);
        var host = new LlmClientHost();

        host.ReplaceClient(Settings("OpenAI", modelName: "gpt-nonexistent", suppressConnectionCheck: false));
        await host.LastSelfCheck!;

        Assert.True(Monitor.Contains(LogLevel.Error, "Check the configured model name"));
    }

    [Fact]
    public async Task InsecureCompatibleAddressGetsExtraHint()
    {
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{\"error\":\"boom\"}", HttpStatusCode.InternalServerError);
        var host = new LlmClientHost();

        host.ReplaceClient(Settings(
            "OpenAiCompatible",
            serverAddress: "http://localhost:1234/v1",
            suppressConnectionCheck: false));
        await host.LastSelfCheck!;

        Assert.True(Monitor.Contains(LogLevel.Error, "does not use HTTPS"));
    }

    [Fact]
    public async Task ProviderWithoutModelListGetsGenericHint()
    {
        Http.DefaultResponder = _ => FakeHttpHandler.Json("nope", HttpStatusCode.InternalServerError);
        var host = new LlmClientHost();

        host.ReplaceClient(Settings(
            "LlamaCpp",
            apiKey: "",
            serverAddress: "http://localhost:8080/completion",
            suppressConnectionCheck: false));
        await host.LastSelfCheck!;

        Assert.True(Monitor.Contains(LogLevel.Error, "Check the server address"));
        Assert.True(Monitor.Contains(LogLevel.Error, "does not use HTTPS"));
        Assert.True(Monitor.Contains(LogLevel.Warn, "stays enabled"));
    }

    [Fact]
    public async Task StaleCheckAbortsSilently()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(Settings("OpenAI", suppressConnectionCheck: true));
        var strayClient = LlmClientFactory.TryCreate(Settings("OpenAI"))!;

        // strayClient 不是 host 的当前实例：自检应静默放弃，零日志零请求。
        await ConnectionSelfCheck.Start(host, strayClient, Settings("OpenAI"));

        Assert.Empty(Http.Requests);
        Assert.DoesNotContain(Monitor.Entries, e => e.Level is LogLevel.Error or LogLevel.Info or LogLevel.Warn);
    }
}

[Collection("LlmLayer")]
public sealed class AndroidNetworkAvailabilityTests : LlmTestBase
{
    [Fact]
    public async Task InferenceEntryFailsFastWhenAndroidHasNoNetwork()
    {
        AndroidPlatform.OverrideForTests = true;
        NetworkAvailability.ProbeForTests = () => false;
        var client = new OpenAiClient(Settings("OpenAI"));

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.Equal("Network not available", reply.ErrorMessage);
        Assert.Empty(Http.Requests);
    }

    [Fact]
    public async Task GameHookPrecheckRetriesFiveTimesThenGivesUp()
    {
        AndroidPlatform.OverrideForTests = true;
        NetworkAvailability.RecheckDelay = TimeSpan.FromMilliseconds(1);
        int probes = 0;
        NetworkAvailability.ProbeForTests = () =>
        {
            probes++;
            return false;
        };

        bool available = await NetworkAvailability.EnsureAvailableForGenerationAsync();

        Assert.False(available);
        Assert.Equal(6, probes); // 首查 + 5 次重查
        Assert.Equal(2, Monitor.MessagesAt(LogLevel.Warn).Count);
    }

    [Fact]
    public async Task GameHookPrecheckSucceedsWhenNetworkComesBack()
    {
        AndroidPlatform.OverrideForTests = true;
        NetworkAvailability.RecheckDelay = TimeSpan.FromMilliseconds(1);
        int probes = 0;
        NetworkAvailability.ProbeForTests = () => ++probes >= 3;

        bool available = await NetworkAvailability.EnsureAvailableForGenerationAsync();

        Assert.True(available);
        Assert.Equal(3, probes);
    }

    [Fact]
    public void NonAndroidPlatformSkipsProbesEntirely()
    {
        AndroidPlatform.OverrideForTests = false;
        int probes = 0;
        NetworkAvailability.ProbeForTests = () =>
        {
            probes++;
            return false;
        };

        Assert.True(NetworkAvailability.EnsureAvailableForGeneration());
        NetworkAvailability.ThrowIfUnavailable();
        Assert.Equal(0, probes);
    }
}

[Collection("LlmLayer")]
public sealed class LlmHttpTests : LlmTestBase
{
    [Fact]
    public void DefaultTimeoutTracksLiveConfigWithFloor()
    {
        Config.QueryTimeout = 120;
        Assert.Equal(TimeSpan.FromSeconds(120), LlmHttp.DefaultTimeout);

        // 改配置即时生效（不重建客户端），且有 5 秒下限。
        Config.QueryTimeout = 2;
        Assert.Equal(TimeSpan.FromSeconds(5), LlmHttp.DefaultTimeout);
    }

    [Fact]
    public async Task CallerCancellationTranslatesToOperationCanceled()
    {
        Http.DefaultResponder = _ => FakeHttpHandler.Json("{}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LlmHttp.SendAsync("https://example.test/x", "{}", null, null, null, cts.Token));

        Assert.Equal("Request was cancelled", exception.Message);
    }

    [Fact]
    public async Task TimeoutTranslatesToTimeoutException()
    {
        Http.DefaultResponder = _ => throw new OperationCanceledException();

        await Assert.ThrowsAsync<TimeoutException>(
            () => LlmHttp.SendAsync("https://example.test/x", "{}", null, null, TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }

    [Fact]
    public async Task HttpErrorCarriesStatusAndBody()
    {
        Http.DefaultResponder = _ => FakeHttpHandler.Json("who are you", HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LlmHttp.SendAsync("https://example.test/x", "{}", null, null, null, CancellationToken.None));

        Assert.Contains("(HTTP 401 - who are you)", exception.Message, StringComparison.Ordinal);
        Assert.Equal(401, LlmClientBase.ExtractHttpStatus(exception));
    }

    [Fact]
    public async Task EmptyContentSendsGetAndBearerIsOmittedWithoutToken()
    {
        Http.EnqueueJson("{}");
        await LlmHttp.SendAsync("https://example.test/models", null, null, null, null, CancellationToken.None);

        Assert.Equal(HttpMethod.Get, Http.Requests.Single().Method);
        Assert.Null(Http.Requests.Single().Authorization);
    }
}

[Collection("LlmLayer")]
public sealed class LlmClientFactoryTests : LlmTestBase
{
    [Fact]
    public void RegistryLookupIsCaseInsensitive()
    {
        Assert.IsType<OpenAiClient>(LlmClientFactory.TryCreate(Settings("openai")));
        Assert.IsType<ClaudeClient>(LlmClientFactory.TryCreate(Settings("ANTHROPIC")));
        Assert.IsType<GeminiClient>(LlmClientFactory.TryCreate(Settings("google")));
        Assert.IsType<VolcEngineClient>(LlmClientFactory.TryCreate(Settings("volcengine")));
        Assert.IsType<LlamaCppClient>(LlmClientFactory.TryCreate(Settings("llamacpp")));
        Assert.IsType<DeepSeekClient>(LlmClientFactory.TryCreate(Settings("deepseek")));
        Assert.IsType<MistralClient>(LlmClientFactory.TryCreate(Settings("mistral")));
        Assert.IsType<OpenAiCompatibleClient>(LlmClientFactory.TryCreate(Settings("openaicompatible")));
    }

    [Fact]
    public void InvalidProviderLogsErrorAndReturnsNull()
    {
        Assert.Null(LlmClientFactory.TryCreate(Settings("Skynet")));
        Assert.True(Monitor.Contains(LogLevel.Error, "Invalid LLM type: Skynet"));
    }

    [Fact]
    public void MetadataDrivesGmcmFieldVisibility()
    {
        Assert.True(LlmClientFactory.TryGetMetadata("LlamaCpp", out LlmProviderMetadata llama));
        Assert.False(llama.RequiresApiKey);
        Assert.False(llama.RequiresModelName);
        Assert.True(llama.RequiresServerAddress);
        Assert.True(llama.RequiresPromptFormat);
        Assert.False(llama.SupportsModelList);

        Assert.True(LlmClientFactory.TryGetMetadata("OpenAiCompatible", out LlmProviderMetadata compatible));
        Assert.True(compatible.RequiresApiKey);
        Assert.True(compatible.RequiresServerAddress);
        Assert.True(compatible.SupportsModelList);

        Assert.True(LlmClientFactory.TryGetMetadata("Anthropic", out LlmProviderMetadata claude));
        Assert.False(claude.RequiresServerAddress);
        Assert.True(claude.SupportsModelList);
        Assert.False(claude.SupportsThinkingLevels);
    }

    [Fact]
    public void CapabilityFlagsMatchSpec()
    {
        Assert.True(((ILlmCapabilities)LlmClientFactory.TryCreate(Settings("Anthropic"))!).IsHighlySensoredModel);
        Assert.False(((ILlmCapabilities)LlmClientFactory.TryCreate(Settings("OpenAI"))!).IsHighlySensoredModel);
        Assert.Equal(string.Empty, ((ILlmCapabilities)LlmClientFactory.TryCreate(Settings("OpenAI"))!).ExtraInstructions);
    }

#if DEBUG
    [Fact]
    public void DummyIsRegisteredInDebugBuilds()
    {
        Assert.Contains("Dummy", LlmClientFactory.ProviderIds);
        Assert.IsType<DummyClient>(LlmClientFactory.TryCreate(Settings("dummy")));
    }

    [Fact]
    public async Task DummyReturnsOneOfTwoFixedTexts()
    {
        var client = LlmClientFactory.TryCreate(Settings("Dummy"))!;
        var allowed = new HashSet<string>
        {
            "- LLM generated string.",
            "- LLM generated question\n% One answer\n% Another answer\n% A third answer"
        };

        for (int i = 0; i < 20; i++)
        {
            LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);
            Assert.True(reply.IsSuccess);
            Assert.Contains(reply.Text, allowed);
        }

        Assert.Empty(Http.Requests);
        Assert.True(((ILlmCapabilities)client).IsHighlySensoredModel);
        Assert.False(client is ITokenProbabilities);
    }
#endif
}
