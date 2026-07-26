using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Llm;

/// <summary>
/// 审查发现 4/5 与附带项的回归钉子。
/// 发现 4：不含 /v1 的完整 chat/completions 端点（Cloudflare AI Gateway /compat 形态）原样直用、
/// 不强插 /v1；主流"裸基地址 / …/v1 / …/v1/chat/completions"用法保持既有规整拼接不变。
/// 发现 5：Claude 请求在 NpcContext 与 Tail 皆空时以占位文本保证 user 消息内容非空合法。
/// 附带：ApiKey 统一 Trim（对齐 ModelName），尾随空白不再进 Bearer/x-api-key/URL query。
/// </summary>
[Collection("LlmLayer")]
public sealed class EndpointVerbatimAndClaudePlaceholderTests : LlmTestBase
{
    private const string CompletionJson = "{\"choices\":[{\"message\":{\"content\":\"Hello there\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3,\"total_tokens\":13}}";
    private const string ClaudeReplyJson = "{\"content\":[{\"type\":\"text\",\"text\":\"Greetings\"}],\"usage\":{\"input_tokens\":8,\"output_tokens\":5}}";

    // ---- 发现 4：完整端点直用判定 ----

    [Theory]
    // 主流形态一律返回 null（继续走 NormalizeBaseAddress + /v1 拼接）。
    [InlineData("", null)]
    [InlineData("https://openrouter.ai/api", null)]
    [InlineData("https://openrouter.ai/api/", null)]
    [InlineData("https://openrouter.ai/api/v1", null)]
    [InlineData("https://openrouter.ai/api/v1/chat/completions", null)]
    [InlineData("https://openrouter.ai/api/v1/chat/completions/", null)]
    [InlineData("http://localhost:8080/v1/", null)]
    // 不含 /v1 的完整端点：原样直用（去尾部斜杠；后缀大小写不敏感、保留原始大小写）。
    [InlineData("https://gw.example/compat/chat/completions", "https://gw.example/compat/chat/completions")]
    [InlineData("https://gw.example/compat/chat/completions/", "https://gw.example/compat/chat/completions")]
    [InlineData("https://gw.example/chat/completions", "https://gw.example/chat/completions")]
    [InlineData("https://gw.example/compat/Chat/Completions", "https://gw.example/compat/Chat/Completions")]
    public void DetectVerbatimChatEndpointMatchesOnlyNonV1FullEndpoints(string configured, string? expected)
    {
        Assert.Equal(expected, OpenAiChatClientBase.DetectVerbatimChatEndpoint(configured));
    }

    [Fact]
    public async Task VerbatimEndpointIsUsedAsConfiguredForChatModelsAndLogsTrace()
    {
        var client = new OpenAiCompatibleClient(Settings(
            "OpenAiCompatible",
            serverAddress: "https://gw.example/compat/chat/completions"));
        Http.EnqueueJson(CompletionJson);

        LlmReply reply = await client.CompleteAsync(Request(), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        // 修复前会被改写成 https://gw.example/compat/v1/chat/completions（凭空插 /v1 → 404）。
        Assert.Equal("https://gw.example/compat/chat/completions", Http.Requests.Single().Url);

        // 模型列表打同级 /models。
        Http.Requests.Clear();
        Http.EnqueueJson("{\"data\":[{\"id\":\"m1\"}]}");
        Assert.Equal(new[] { "m1" }, client.GetModelNames());
        Assert.Equal("https://gw.example/compat/models", Http.Requests.Single().Url);

        // 端点改写结果留 Trace 便于排查。
        Assert.True(Monitor.Contains(LogLevel.Trace, "verbatim, no /v1 inserted"));
        Assert.True(Monitor.Contains(LogLevel.Trace, "https://gw.example/compat/chat/completions"));
    }

    [Fact]
    public async Task MainstreamAddressesKeepAutoV1Joining()
    {
        // 裸基地址：自动补 /v1/chat/completions（既有主流用法不受影响）。
        var bare = new OpenAiCompatibleClient(Settings("OpenAiCompatible", serverAddress: "https://openrouter.ai/api"));
        Http.EnqueueJson(CompletionJson);
        await bare.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", Http.Requests.Single().Url);

        // 粘贴了带 /v1 的完整端点：规整后重建出同一地址。
        Http.Requests.Clear();
        var fullV1 = new OpenAiCompatibleClient(Settings(
            "OpenAiCompatible",
            serverAddress: "https://openrouter.ai/api/v1/chat/completions"));
        Http.EnqueueJson(CompletionJson);
        await fullV1.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", Http.Requests.Single().Url);
    }

    [Fact]
    public async Task StreamingAlsoUsesVerbatimEndpoint()
    {
        var client = new OpenAiCompatibleClient(Settings(
            "OpenAiCompatible",
            serverAddress: "https://gw.example/compat/chat/completions"));
        string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"}}]}\n"
            + "data: [DONE]\n";
        Http.Enqueue(_ => FakeHttpHandler.Text(sse));

        List<LlmStreamEvent> events = await CollectAsync(client.StreamAsync(Request(), CancellationToken.None));

        Assert.Contains(events, e => e.Kind == LlmStreamEventKind.Done);
        Assert.Equal("https://gw.example/compat/chat/completions", Http.Requests.Single().Url);
        Assert.True(JObject.Parse(Http.Requests.Single().Body!).Value<bool>("stream"));
    }

    // ---- 发现 5：Claude 空 user 内容占位 ----

    [Fact]
    public void ClaudeBuildBodyUsesPlaceholderOnlyWhenNpcAndTailBothEmpty()
    {
        var client = new ClaudeClient(Settings("Anthropic"));

        // 两段皆空：占位符顶上，消息内容非空合法。
        JObject degenerate = client.BuildBody(Request(system: "SYS", stable: "", npc: "", tail: ""));
        JToken content = degenerate["messages"]![0]!["content"]!;
        Assert.Equal(JTokenType.String, content.Type);
        Assert.Equal(ClaudeClient.EmptyUserContentPlaceholder, content.ToString());

        // Tail 非空：原样发送，不掺占位符。
        JObject tailOnly = client.BuildBody(Request(npc: "", tail: "NOW"));
        Assert.Equal("NOW", tailOnly["messages"]![0]!["content"]!.ToString());

        // NpcContext 非空、Tail 空：仍是单块结构（本就合法），不追加占位块。
        JObject npcOnly = client.BuildBody(Request(npc: "BIO", tail: ""));
        var blocks = (JArray)npcOnly["messages"]![0]!["content"]!;
        Assert.Single(blocks);
        Assert.Equal("BIO", blocks[0].Value<string>("text"));
    }

    [Fact]
    public async Task ClaudeDegenerateRequestSendsPlaceholderEndToEnd()
    {
        var client = new ClaudeClient(Settings("Anthropic"));
        Http.EnqueueJson(ClaudeReplyJson);

        LlmReply reply = await client.CompleteAsync(Request(system: "SYS", stable: "", npc: "", tail: ""), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        var body = JObject.Parse(Http.Requests.Single().Body!);
        Assert.Equal(ClaudeClient.EmptyUserContentPlaceholder, body["messages"]![0]!["content"]!.ToString());
    }

    // ---- 附带：ApiKey Trim ----

    [Fact]
    public async Task ApiKeyIsTrimmedForHeaderBearerAndUrlQueryAuth()
    {
        const string paddedKey = "  test-key  ";

        // Claude：x-api-key 头。
        var claude = new ClaudeClient(Settings("Anthropic", apiKey: paddedKey));
        Http.EnqueueJson(ClaudeReplyJson);
        await claude.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("test-key", Http.Requests[^1].Headers["x-api-key"]);

        // OpenAI：Authorization Bearer。
        var openAi = new OpenAiClient(Settings("OpenAI", apiKey: paddedKey));
        Http.EnqueueJson(CompletionJson);
        await openAi.CompleteAsync(Request(), CancellationToken.None);
        Assert.Equal("Bearer test-key", Http.Requests[^1].Authorization);

        // Gemini：URL query 参数（修复前会带 %20%20）。
        var gemini = new GeminiClient(Settings("Google", apiKey: paddedKey));
        Http.EnqueueJson("{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"text\":\"Hi\"}]}}]}");
        await gemini.CompleteAsync(Request(), CancellationToken.None);
        Assert.EndsWith(":generateContent?key=test-key", Http.Requests[^1].Url, StringComparison.Ordinal);
    }
}
