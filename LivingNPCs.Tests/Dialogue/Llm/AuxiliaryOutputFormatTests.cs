using System.Net;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Tests.Dialogue.Llm;

[Collection("LlmLayer")]
public sealed class AuxiliaryOutputFormatTests : LlmTestBase
{
    private const string GeneratedProse = "Thank you for the tea. I hope you enjoy this little gift.";

    [Theory]
    [InlineData("OpenAI", "off", false)]
    [InlineData("OpenAI", "off", true)]
    [InlineData("OpenAI", "low", false)]
    [InlineData("OpenAI", "low", true)]
    [InlineData("OpenAI", "auto", false)]
    [InlineData("OpenAI", "auto", true)]
    [InlineData("Google", "off", false)]
    [InlineData("Google", "off", true)]
    [InlineData("Google", "low", false)]
    [InlineData("Google", "low", true)]
    [InlineData("Google", "auto", false)]
    [InlineData("Google", "auto", true)]
    [InlineData("VolcEngine", "off", false)]
    [InlineData("VolcEngine", "off", true)]
    [InlineData("VolcEngine", "low", false)]
    [InlineData("VolcEngine", "low", true)]
    [InlineData("VolcEngine", "auto", false)]
    [InlineData("VolcEngine", "auto", true)]
    public async Task ProseGeneratorsKeepPlainTextAndTheirOutputBudget(string provider, string thinkingLevel, bool giftMail)
    {
        Config.ThinkingLevel = thinkingLevel;
        var host = new LlmClientHost();
        host.ReplaceClient(ProviderSettings(provider));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(Success(provider, GeneratedProse));

        string? result = giftMail
            ? await GiftMailGenerator.Instance.GenerateAsync(
                new GiftMailRequest("Penny", "Penny", "reciprocal", "(O)346", "Beer", "Tea", "small", 10),
                CancellationToken.None)
            : await MemoryImpressionGenerator.Instance.GenerateAsync(
                new MemoryImpressionRequest("Penny", "Penny", "", new[] { "The farmer returned a borrowed book." }, 10),
                CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(GeneratedProse, result);
        Assert.NotEmpty(Http.Requests);
        Assert.All(Http.Requests, request =>
        {
            var body = JObject.Parse(request.Body!);
            Assert.Null(JsonFormat(body, provider));
            Assert.Equal(10_000, provider == "Google"
                ? body["generationConfig"]!.Value<int>("maxOutputTokens")
                : (body.Value<int?>("max_completion_tokens") ?? body.Value<int>("max_tokens")));
        });
    }

    [Theory]
    [InlineData("OpenAI", "off")]
    [InlineData("OpenAI", "low")]
    [InlineData("OpenAI", "auto")]
    [InlineData("Google", "off")]
    [InlineData("Google", "low")]
    [InlineData("Google", "auto")]
    [InlineData("VolcEngine", "off")]
    [InlineData("VolcEngine", "low")]
    [InlineData("VolcEngine", "auto")]
    public async Task ExplicitJsonCrossesLegacyBridgeAtEveryThinkingLevel(string provider, string thinkingLevel)
    {
        Config.ThinkingLevel = thinkingLevel;
        var host = new LlmClientHost();
        host.ReplaceClient(ProviderSettings(provider));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(Success(provider, "{\"complete\":true}"));

        LlmResponse result = await LegacyLlm.Instance.RunInference(
            "Return only a JSON object.", "", "", "Classify this exchange.",
            disableThinking: true, outputFormat: LlmOutputFormat.JsonObject);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(Http.Requests);
        Assert.All(Http.Requests, request =>
        {
            var body = JObject.Parse(request.Body!);
            Assert.Equal(provider == "Google" ? "application/json" : "json_object",
                JsonFormat(body, provider));
        });
    }

    [Theory]
    [InlineData("OpenAI", "gpt-4o", false)]
    [InlineData("OpenAI", "gpt-4o", true)]
    [InlineData("OpenAI", "gpt-6-astra", false)]
    [InlineData("OpenAI", "gpt-6-astra", true)]
    [InlineData("Google", "gemini-3-pro-preview", false)]
    [InlineData("Google", "gemini-3-pro-preview", true)]
    [InlineData("Google", "gemini-3.5-flash", false)]
    [InlineData("Google", "gemini-3.5-flash", true)]
    public async Task MenuThinkingNormalizationPreservesExplicitOutputFormat(string provider, string model, bool jsonOutput)
    {
        string normalized = LlmThinking.NormalizeForModel(LlmThinking.Off, provider, model, LlmThinking.Off);
        Assert.NotEqual(LlmThinking.Off, normalized);
        var host = new LlmClientHost();
        host.ReplaceClient(Settings(provider, modelName: model));
        Http.DefaultResponder = _ => FakeHttpHandler.Json(Success(provider, "{\"complete\":true}"));

        foreach (string level in new[] { LlmThinking.Off, normalized })
        {
            Config.ThinkingLevel = level;
            LlmResponse result = await LegacyLlm.Instance.RunInference(
                "Return the requested output format.", "", "", "Classify this exchange.",
                disableThinking: true,
                outputFormat: jsonOutput ? LlmOutputFormat.JsonObject : LlmOutputFormat.Text);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(2, Http.Requests.Count);
        Assert.All(Http.Requests, request => Assert.Equal(
            jsonOutput ? provider == "Google" ? "application/json" : "json_object" : null,
            JsonFormat(JObject.Parse(request.Body!), provider)));
    }

    [Fact]
    public async Task MetadataExtractorExplicitlyRequestsJson()
    {
        var host = new LlmClientHost();
        host.ReplaceClient(ProviderSettings("OpenAI"));
        Http.EnqueueJson(Success("OpenAI", "{\"complete\":true,\"rapportDelta\":1}"));

        LivingNpcMetadataExtractionResult result = await LivingNpcMetadataExtractionPass.TryExtractAsync(
            new Character("Penny"),
            new DialogueContext { Location = "Town", TimeOfDay = "1200" },
            "Hello.", "Good morning.", Array.Empty<string>());

        Assert.True(result.Success);
        Assert.Equal(1, result.Analysis.RapportDelta);
        Assert.Equal("json_object", JsonFormat(JObject.Parse(Assert.Single(Http.Requests).Body!), "OpenAI"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamFallbackRetainsExplicitOutputFormatWhenCloningRequest(bool jsonOutput)
    {
        // The auxiliary flag survives CloneWithoutRetry without changing the shared effort or output format.
        Config.ThinkingLevel = LlmThinking.Low;
        var client = new OpenAiClient(Settings("OpenAI", modelName: "gpt-5.5"));
        Http.DefaultResponder = request => JObject.Parse(request.Body!).Value<bool?>("stream") == true
            ? FakeHttpHandler.Json("{\"error\":\"stream is unsupported\"}", HttpStatusCode.BadRequest)
            : FakeHttpHandler.Json(Success("OpenAI", "{\"complete\":true}"));

        var events = await CollectAsync(client.StreamAsync(Request(
            disableThinking: true,
            outputFormat: jsonOutput ? LlmOutputFormat.JsonObject : LlmOutputFormat.Text), CancellationToken.None));

        Assert.Equal(LlmStreamEventKind.Done, events[^1].Kind);
        Assert.Equal(2, Http.Requests.Count);
        Assert.False(JObject.Parse(Http.Requests[^1].Body!).Value<bool?>("stream") == true);
        Assert.All(Http.Requests, request =>
        {
            var body = JObject.Parse(request.Body!);
            Assert.Equal("low", body.Value<string>("reasoning_effort"));
            Assert.Equal(jsonOutput ? "json_object" : null, JsonFormat(body, "OpenAI"));
        });
    }

    private static LlmConnectionSettings ProviderSettings(string provider) => Settings(provider, modelName: provider switch
    {
        "Google" => "gemini-2.5-flash",
        "VolcEngine" => "doubao-seed-1.6",
        _ => "gpt-4o"
    });

    private static string? JsonFormat(JObject body, string provider) => provider == "Google"
        ? body["generationConfig"]?.Value<string>("responseMimeType")
        : body["response_format"]?.Value<string>("type");

    private static string Success(string provider, string text) => provider == "Google"
        ? new JObject
        {
            ["candidates"] = new JArray(new JObject
            {
                ["finishReason"] = "STOP",
                ["content"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = text }) }
            })
        }.ToString()
        : new JObject
        {
            ["choices"] = new JArray(new JObject
            {
                ["finish_reason"] = "stop",
                ["message"] = new JObject { ["content"] = text }
            })
        }.ToString();
}
