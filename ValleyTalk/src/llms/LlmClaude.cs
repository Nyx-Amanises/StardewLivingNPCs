using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using ValleyTalk;
using ValleyTalk.Platform;
using System.Threading.Tasks;

namespace ValleyTalk;

internal class LlmClaude : Llm, IGetModelNames
{
    private readonly string apiKey;
    private readonly string modelName;

    class PromptElement
    {
#pragma warning disable IDE1006 // Naming Styles
        public string type { get; set; }
        public string text { get; set; }
        public object cache_control { get; set; }
#pragma warning restore IDE1006 // Naming Styles
    }
    public LlmClaude(string apiKey, string modelName = null)
    {
        url = "https://api.anthropic.com/v1/messages";
        
        this.apiKey = apiKey;
        this.modelName = modelName ?? "claude-3-5-haiku-latest";
    }

    public Dictionary<string,string> CacheContexts { get; private set; } = new Dictionary<string, string>();

    public override string ExtraInstructions => "";

    public override bool IsHighlySensoredModel => true;

    internal override async Task<LlmResponse> RunInference(string systemPromptString, string gameCacheString, string npcCacheString, string promptString, string responseStart = "",int n_predict = 2048,string cacheContext="",bool allowRetry = true,bool disableThinking = false)
    {
        // Cache breakpoint 1: the static world summary (system). Breakpoint 2: the per-NPC
        // biography/samples, kept in the user turn but split into its own cache_control block so
        // repeated turns with the same NPC reuse it. The variable prompt tail stays uncached.
        object userContent = string.IsNullOrEmpty(npcCacheString)
            ? promptString
            : new object[]
            {
                new { type = "text", text = npcCacheString, cache_control = new { type = "ephemeral" } },
                new { type = "text", text = promptString }
            };
        var inputString = JsonConvert.SerializeObject(new
            {
                model = this.modelName,
                max_tokens = n_predict,
                system = new PromptElement[]
                {
                    new()
                    {
                        type = "text",
                        text = systemPromptString
                    },
                    new()
                    {
                        type = "text",
                        cache_control = new { type = "ephemeral" },
                        text = gameCacheString
                    }
                },
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = userContent
                    }
                }
            });
        var json = new StringContent(
            inputString,
            Encoding.UTF8,
            "application/json"
        );

        // call out to URL passing the object as the body, and return the result
        int retry = allowRetry ? 3 : 1;
        var fullUrl = url;
        
        // Check network availability on Android
        if (AndroidHelper.IsAndroid && !NetworkHelper.IsNetworkAvailable())
        {
            throw new InvalidOperationException("Network not available");
        }
        
        string responseString = "";
        int apiResponseCode = 500;
        while (retry > 0)
        {
            try
            {
                // Use Android-compatible network helper with Claude-specific headers
                var headers = new Dictionary<string, string>
                {
                    { "x-api-key", apiKey },
                    { "anthropic-version", "2023-06-01" },
                    { "anthropic-beta", "prompt-caching-2024-07-31" }
                };
                responseString = await NetworkHelper.MakeRequestWithCustomHeadersAsync(fullUrl, inputString, headers);
                var responseJson = JObject.Parse(responseString);

                if (responseJson == null)
                {
                    throw new Exception("Failed to parse response");
                }
                else
                {

                    if (!responseJson.TryGetValue("content", out var contentToken) || contentToken.Type == JTokenType.Null) { retry--; continue; }
                    var contentArray = contentToken as JArray;
                    if (contentArray == null || !contentArray.HasValues) { retry--; continue; }

                    var firstContentElement = contentArray.FirstOrDefault();
                    if (firstContentElement == null || firstContentElement["text"] == null) { retry--; continue; }

                    var text = firstContentElement["text"].ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new LlmResponse(text, usage: TokenUsage.FromClaudeUsage(responseJson["usage"] as JObject));
                    }
                    else
                    {
                        retry--;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException is HttpRequestException httpException && httpException.StatusCode.HasValue)
                {
                    apiResponseCode = (int)httpException.StatusCode.Value;
                }
                else if (ex is HttpRequestException directHttpException && directHttpException.StatusCode.HasValue)
                {
                    apiResponseCode = (int)directHttpException.StatusCode.Value;
                }
                Log.Debug(ex.Message);
                Log.Debug("Retrying...");
                retry--;
                Thread.Sleep(100);
            }
        }
        return new LlmResponse(responseString, apiResponseCode);
    }

    internal override Dictionary<string, double>[] RunInferenceProbabilities(string fullPrompt, int n_predict = 1)
    {
        throw new NotImplementedException();
    }

    public string[] GetModelNames()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return new string[] { };
        }
        try 
        {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(1)
        };
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        var response = client.SendAsync(request).Result;
        var responseString = response.Content.ReadAsStringAsync().Result;
        var responseJson = JObject.Parse(responseString);
        var models = responseJson["data"] as JArray;
        var modelNames = new List<string>();
        if (models != null)
        {
            foreach (var model in models)
            {
                modelNames.Add(model["id"].ToString());
            }
        }
        return modelNames.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            return new string[] { };
        }
    }
}
