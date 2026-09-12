using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class ClaudeThinkingRequestTests : LlmTestBase
{
    private const string ReplyJson = "{\"content\":[{\"type\":\"text\",\"text\":\"Hello\"}],\"usage\":{\"input_tokens\":8,\"output_tokens\":5}}";

    [Theory]
    [InlineData("claude-fable-5-1", "Ultra", "max")]
    [InlineData("claude-mythos-5-1", "XHigh", "xhigh")]
    [InlineData("claude-fable-5", "High", "high")]
    [InlineData("claude-mythos-5", "Max", "max")]
    [InlineData("claude-mythos-preview", "XHigh", "max")]
    [InlineData("claude-opus-5", "XHigh", "xhigh")]
    [InlineData("claude-sonnet-5", "Max", "max")]
    [InlineData("claude-opus-4-8", "Medium", "medium")]
    [InlineData("claude-opus-4-7", "Ultra", "max")]
    [InlineData("anthropic/claude-opus-4.7", "XHigh", "xhigh")]
    [InlineData("claude-opus-4-6", "XHigh", "max")]
    [InlineData("claude-sonnet-4-6", "Max", "max")]
    [InlineData("claude-sonnet-4-6-20260217", "Minimal", "low")]
    public async Task AdaptiveModelsSendSupportedEffortWithoutManualBudget(string model, string level, string effort)
    {
        JObject body = await SendRequestAsync(model, level);

        Assert.Equal("adaptive", body["thinking"]!.Value<string>("type"));
        Assert.Null(body["thinking"]!["budget_tokens"]);
        Assert.Equal(effort, body["output_config"]!.Value<string>("effort"));
        Assert.Equal(10000, body.Value<int>("max_tokens"));
        Assert.Null(body["reasoning_effort"]);
    }

    [Theory]
    [InlineData("claude-haiku-4-5", "Low", 1024, null)]
    [InlineData("claude-haiku-4-5", "Medium", 4096, null)]
    [InlineData("claude-sonnet-4-5-20250929", "High", 8192, null)]
    [InlineData("claude-opus-4-5", "Low", 1024, "low")]
    [InlineData("claude-opus-4-5-20251101", "Ultra", 8192, "high")]
    [InlineData("claude-opus-4-1", "XHigh", 8192, null)]
    [InlineData("claude-opus-4-20250514", "Minimal", 1024, null)]
    [InlineData("claude-sonnet-4", "Medium", 4096, null)]
    [InlineData("claude-3-7-sonnet-20250219", "Low", 1024, null)]
    public async Task LegacyThinkingUsesBudgetsAndOnlyOpus45ReceivesEffort(string model, string level, int budget, string? effort)
    {
        JObject body = await SendRequestAsync(model, level);

        Assert.Equal("enabled", body["thinking"]!.Value<string>("type"));
        Assert.Equal(budget, body["thinking"]!.Value<int>("budget_tokens"));
        Assert.Equal(effort, body["output_config"]?.Value<string>("effort"));
        Assert.Equal(10000, body.Value<int>("max_tokens"));
    }

    [Theory]
    [InlineData("Low", 220, null)]
    [InlineData("Low", 500, null)]
    [InlineData("High", 1024, null)]
    [InlineData("High", 1025, 1024)]
    [InlineData("High", 2048, 1536)]
    [InlineData("Medium", 4096, 3072)]
    [InlineData("High", 10000, 8192)]
    public async Task ManualBudgetRespectsCallerLimitAndMinimum(string level, int maxTokens, int? budget)
    {
        JObject body = await SendRequestAsync("claude-opus-4-5", level, maxTokens);

        Assert.Equal(maxTokens, body.Value<int>("max_tokens"));
        Assert.Equal(budget, body["thinking"]!.Value<int?>("budget_tokens"));
        Assert.Equal(budget.HasValue ? "enabled" : "disabled", body["thinking"]!.Value<string>("type"));
        if (budget.HasValue)
        {
            Assert.InRange(budget.Value, 1024, maxTokens - 1);
        }
        else
        {
            Assert.Null(body["output_config"]);
        }
    }

    [Theory]
    [InlineData("claude-haiku-4-5")]
    [InlineData("claude-opus-4-5")]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-opus-4-7")]
    [InlineData("claude-opus-5")]
    [InlineData("claude-sonnet-5")]
    public async Task OffUsesDisabledWithoutConflictingEffort(string model)
    {
        JObject body = await SendRequestAsync(model, LlmThinking.Off);

        Assert.Equal("disabled", body["thinking"]!.Value<string>("type"));
        Assert.Null(body["thinking"]!["budget_tokens"]);
        Assert.Null(body["output_config"]);
    }

    [Theory]
    [InlineData("claude-fable-5-1")]
    [InlineData("claude-mythos-5")]
    [InlineData("claude-mythos-preview")]
    public async Task AlwaysThinkingModelsNormalizeSavedOffToLow(string model)
    {
        JObject body = await SendRequestAsync(model, LlmThinking.Off);

        Assert.Equal("adaptive", body["thinking"]!.Value<string>("type"));
        Assert.Equal("low", body["output_config"]!.Value<string>("effort"));
        Assert.DoesNotContain(LlmThinking.Off, ClaudeThinking.GetOptions(model));
    }

    [Theory]
    [InlineData("claude-opus-5")]
    [InlineData("claude-fable-5-1")]
    [InlineData("claude-opus-4-6")]
    [InlineData("claude-haiku-4-5")]
    public async Task AutoLeavesProviderDefaultsIntact(string model)
    {
        JObject body = await SendRequestAsync(model, LlmThinking.Auto);

        Assert.Null(body["thinking"]);
        Assert.Null(body["output_config"]);
    }

    [Theory]
    [InlineData("claude-3-5-haiku-20241022")]
    [InlineData("claude-3-opus-20240229")]
    [InlineData("claude-opus-40")]
    [InlineData("claude-opus-4-70")]
    [InlineData("claude-opus-4-700")]
    [InlineData("claude-opus-4-experimental")]
    [InlineData("claude-sonnet-5-1")]
    [InlineData("custom-claude-opus-5")]
    public async Task UnsupportedModelsDoNotReceiveInventedThinkingFields(string model)
    {
        JObject body = await SendRequestAsync(model, LlmThinking.Ultra);

        Assert.Null(body["thinking"]);
        Assert.Null(body["output_config"]);
        Assert.Equal(new[] { LlmThinking.Auto }, ClaudeThinking.GetOptions(model));
    }

    [Theory]
    [InlineData("", "Auto,Off,Low,Medium,High")]
    [InlineData("claude-opus-4-5", "Auto,Off,Low,Medium,High")]
    [InlineData("claude-sonnet-4-6", "Auto,Off,Low,Medium,High,Max")]
    [InlineData("claude-opus-4-7", "Auto,Off,Low,Medium,High,XHigh,Max")]
    [InlineData("claude-opus-5", "Auto,Off,Low,Medium,High,XHigh,Max")]
    [InlineData("claude-mythos-preview", "Auto,Low,Medium,High,Max")]
    [InlineData("claude-fable-5-1", "Auto,Low,Medium,High,XHigh,Max")]
    public void MenuOffersOnlySupportedLevels(string model, string expected)
    {
        Assert.Equal(expected, string.Join(",", ClaudeThinking.GetOptions(model)));
        Assert.Equal(LlmThinking.Auto, ClaudeThinking.NormalizeLevel("unknown", model));
        Assert.Equal(LlmThinking.Low, ClaudeThinking.NormalizeLevel(" low ", model));
    }

    [Theory]
    [InlineData(LlmThinking.Low, "low")]
    [InlineData(LlmThinking.Max, "max")]
    public async Task BackgroundAndDialogueShareEffortAndPreserveCachingAndAuth(string level, string expectedEffort)
    {
        Config.ThinkingLevel = level;
        var client = new ClaudeClient(Settings("Anthropic", modelName: "claude-sonnet-4-6"));
        Http.EnqueueJson(ReplyJson);

        LlmReply reply = await client.CompleteAsync(Request(disableThinking: true, maxTokens: 10000), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        FakeHttpHandler.RecordedRequest sent = Assert.Single(Http.Requests);
        JObject body = JObject.Parse(sent.Body!);
        Assert.Equal(expectedEffort, body["output_config"]!.Value<string>("effort"));
        Assert.Equal("ephemeral", body["system"]![1]!["cache_control"]!.Value<string>("type"));
        Assert.Equal("ephemeral", body["messages"]![0]!["content"]![0]!["cache_control"]!.Value<string>("type"));
        Assert.Null(body["messages"]![0]!["content"]![1]!["cache_control"]);
        Assert.Null(sent.Authorization);
        Assert.Equal("test-key", sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);

        Http.EnqueueJson(ReplyJson);
        LlmReply dialogueReply = await client.CompleteAsync(Request(maxTokens: 10000), CancellationToken.None);

        Assert.True(dialogueReply.IsSuccess);
        Assert.Equal(2, Http.Requests.Count);
        var dialogueBody = JObject.Parse(Http.Requests[^1].Body!);
        Assert.Equal(expectedEffort, dialogueBody["output_config"]!.Value<string>("effort"));
        Assert.True(JToken.DeepEquals(body["thinking"], dialogueBody["thinking"]));
    }

    [Fact]
    public async Task ThinkingBlocksBeforeAndBetweenTextDoNotHideTheReply()
    {
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new ClaudeClient(Settings("Anthropic", modelName: "claude-opus-5"));
        Http.EnqueueJson("""
            {"content":[
                {"type":"thinking","thinking":"private reasoning","text":"not visible text","signature":"opaque"},
                {"type":"text","text":"Hello, "},
                {"type":"redacted_thinking","data":"opaque"},
                {"type":"text","text":"farmer!"}
            ],"usage":{"input_tokens":8,"output_tokens":1200,"cache_read_input_tokens":40}}
            """);

        LlmReply reply = await client.CompleteAsync(Request(maxTokens: 10000), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        Assert.Equal("Hello, farmer!", reply.Text);
        Assert.Equal(48, reply.Usage.PromptTokens);
        Assert.Equal(1200, reply.Usage.CompletionTokens);
        Assert.Single(Http.Requests);
    }

    [Fact]
    public async Task ExhaustedThinkingOnlyReplyKeepsUsageWithoutLoggingReasoningOrRetrying()
    {
        Config.ThinkingLevel = LlmThinking.High;
        var client = new ClaudeClient(Settings("Anthropic", modelName: "claude-opus-5"));
        Http.EnqueueJson("""
            {"content":[
                {"type":"thinking","thinking":"PRIVATE_REASONING","signature":"PRIVATE_SIGNATURE"},
                {"type":"redacted_thinking","data":"PRIVATE_REDACTED_REASONING"}
            ],"stop_reason":"max_tokens","usage":{"input_tokens":8,"output_tokens":10000,"cache_read_input_tokens":40}}
            """);

        LlmReply reply = await client.CompleteAsync(Request(maxTokens: 10000), CancellationToken.None);

        Assert.False(reply.IsSuccess);
        Assert.False(reply.Retryable);
        Assert.Equal(48, reply.Usage.PromptTokens);
        Assert.Equal(10000, reply.Usage.CompletionTokens);
        Assert.Contains("stop_reason=max_tokens", reply.ErrorMessage);
        Assert.Contains("input_tokens=8", reply.ErrorMessage);
        Assert.Contains("output_tokens=10000", reply.ErrorMessage);
        Assert.Contains("max_tokens=10000", reply.ErrorMessage);
        Assert.DoesNotContain("PRIVATE", reply.ErrorMessage);
        Assert.NotEmpty(Monitor.Entries);
        Assert.All(Monitor.Entries, entry => Assert.DoesNotContain("PRIVATE", entry.Message));
        Assert.Single(Http.Requests);
    }

    private async Task<JObject> SendRequestAsync(string modelName, string level, int maxTokens = 10000)
    {
        Config.ThinkingLevel = level;
        var client = new ClaudeClient(Settings("Anthropic", modelName: modelName));
        Http.EnqueueJson(ReplyJson);

        LlmReply reply = await client.CompleteAsync(Request(maxTokens: maxTokens), CancellationToken.None);

        Assert.True(reply.IsSuccess);
        return JObject.Parse(Assert.Single(Http.Requests).Body!);
    }
}
