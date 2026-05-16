using System;
using System.Collections.Generic;
using System.Linq; // Added
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json; // Changed
using Newtonsoft.Json.Linq; // Added
using System.Threading;
using System.Threading.Tasks;
using ValleyTalk;
using ValleyTalk.Platform;

namespace ValleyTalk;

internal abstract class LlmOpenAiBase : Llm, IStreamingLlm
{
    protected string apiKey;
    protected string modelName;

    record PromptElement
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    internal override async Task<LlmResponse> RunInference(string systemPromptString, string gameCacheString, string npcCacheString, string promptString, string responseStart = "",int n_predict = 2048,string cacheContext="",bool allowRetry = true)
    {
        var inputString = JsonConvert.SerializeObject(new // Changed
            {
                model = modelName,
                max_tokens = n_predict,
                messages = new PromptElement[]
                { 
                    new()
                    {
                        role = "system",
                        content = systemPromptString
                    },
                    new()
                    {
                        role = "user",
                        content = gameCacheString + npcCacheString + promptString
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
        var fullUrl = $"{url}/v1/chat/completions";
        
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
                // Use Android-compatible network helper
                responseString = await NetworkHelper.MakeRequestAsync(fullUrl, inputString, CancellationToken.None, apiKey);
                var responseJson = JObject.Parse(responseString);

                if (responseJson == null)
                {
                    throw new Exception("Failed to parse response");
                }
                else
                {

                    if (!responseJson.TryGetValue("choices", out var choicesToken) || choicesToken.Type == JTokenType.Null) { retry--; continue; } // Changed
                    var choicesArray = choicesToken as JArray;
                    if (choicesArray == null || !choicesArray.HasValues) { retry--; continue; }

                    var firstChoice = choicesArray.FirstOrDefault();
                    if (firstChoice == null) { retry--; continue; }

                    var messageToken = firstChoice["message"];
                    if (messageToken == null || messageToken.Type == JTokenType.Null) { retry--; continue; }

                    var contentToken = messageToken["content"];
                    if (contentToken == null || contentToken.Type == JTokenType.Null) { retry--; continue; }

                    var text = contentToken.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new LlmResponse(text, usage: TokenUsage.FromOpenAiUsage(responseJson["usage"] as JObject));
                    }
                    else
                    {
                        retry--;
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException is HttpRequestException)
                {
                    apiResponseCode = (int)((HttpRequestException)ex.InnerException).StatusCode;
                }
                Log.Debug(ex.Message);
                retry--;
                if (retry > 0)
                {
                    Log.Debug("Retrying...");
                    await Task.Delay(100);
                }
            }
        }
        return new LlmResponse(responseString, apiResponseCode);
    }

    public async Task<LlmResponse> RunInferenceStreaming(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        Action<string> onToken,
        CancellationToken cancellationToken,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true)
    {
        var inputString = JsonConvert.SerializeObject(new
        {
            model = modelName,
            max_tokens = n_predict,
            stream = true,
            messages = new PromptElement[]
            {
                new()
                {
                    role = "system",
                    content = systemPromptString
                },
                new()
                {
                    role = "user",
                    content = gameCacheString + npcCacheString + promptString
                }
            }
        });

        int retry = allowRetry ? 3 : 1;
        var fullUrl = $"{url}/v1/chat/completions";
        string lastResponse = string.Empty;
        int apiResponseCode = 500;

        while (retry > 0)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, fullUrl)
                {
                    Content = new StringContent(inputString, Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                }

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                apiResponseCode = (int)response.StatusCode;
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);
                var fullText = new StringBuilder();
                var rawResponse = new StringBuilder();

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string line = await reader.ReadLineAsync().WaitAsync(cancellationToken) ?? string.Empty;
                    rawResponse.AppendLine(line);

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    string payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        ? line["data:".Length..].Trim()
                        : line.Trim();
                    if (payload == "[DONE]")
                    {
                        break;
                    }

                    if (!TryExtractStreamingContent(payload, out string token))
                    {
                        continue;
                    }

                    fullText.Append(token);
                    onToken?.Invoke(token);
                }

                if (fullText.Length > 0)
                {
                    return new LlmResponse(
                        fullText.ToString(),
                        usage: TokenUsage.Estimate(
                            string.Concat(systemPromptString, gameCacheString, npcCacheString, promptString, responseStart),
                            fullText.ToString(),
                            "stream estimate"
                        )
                    );
                }

                lastResponse = rawResponse.ToString();
                if (TryExtractNonStreamingContent(lastResponse, out string fallbackText))
                {
                    onToken?.Invoke(fallbackText);
                    return new LlmResponse(
                        fallbackText,
                        usage: TokenUsage.Estimate(
                            string.Concat(systemPromptString, gameCacheString, npcCacheString, promptString, responseStart),
                            fallbackText,
                            "stream fallback estimate"
                        )
                    );
                }

                retry--;
            }
            catch (Exception ex)
            {
                Log.Debug(ex.Message);
                retry--;
                if (retry > 0)
                {
                    Log.Debug("Retrying...");
                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fallback = await this.RunInference(
            systemPromptString,
            gameCacheString,
            npcCacheString,
            promptString,
            responseStart,
            n_predict,
            cacheContext,
            allowRetry: false
        );
        if (fallback.IsSuccess && !string.IsNullOrWhiteSpace(fallback.Text))
        {
            onToken?.Invoke(fallback.Text);
            return fallback;
        }

        if (TryExtractNonStreamingContent(lastResponse, out string text))
        {
            return new LlmResponse(
                text,
                usage: TokenUsage.Estimate(
                    string.Concat(systemPromptString, gameCacheString, npcCacheString, promptString, responseStart),
                    text,
                    "stream fallback estimate"
                )
            );
        }

        return new LlmResponse(lastResponse, apiResponseCode);
    }

    private static bool TryExtractStreamingContent(string payload, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var json = JObject.Parse(payload);
            token = json["choices"]?[0]?["delta"]?["content"]?.ToString() ?? string.Empty;
            return !string.IsNullOrEmpty(token);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractNonStreamingContent(string payload, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            var json = JObject.Parse(payload);
            text = json["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(text);
        }
        catch
        {
            return false;
        }
    }

    internal override Dictionary<string, double>[] RunInferenceProbabilities(string fullPrompt, int n_predict = 1)
    {
        throw new NotImplementedException();
    }

    public string[] CoreGetModelNames(Dictionary<string, string> extraHeaders = null)
    {
        if (extraHeaders == null)
        {
            extraHeaders = new Dictionary<string, string>();
        }
        try 
        {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(1)
        };
        var fullUrl = $"{url}/v1/models";
        var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
        foreach (var header in extraHeaders)
        {
            request.Headers.Add(header.Key, header.Value);
        }
        var response = client.SendAsync(request).Result;
        var responseString = response.Content.ReadAsStringAsync().Result;
        var responseJson = JObject.Parse(responseString); // Changed
        var dataToken = responseJson["data"];
        if (dataToken == null || dataToken.Type == JTokenType.Null || !(dataToken is JArray modelsArray)) // Changed and added checks
        {
            return Array.Empty<string>(); // Return empty array if data is not as expected
        }

        var modelNames = new List<string>();
        foreach (var model in modelsArray)
        {
            var idToken = model["id"];
            if (idToken != null && idToken.Type != JTokenType.Null)
            {
                modelNames.Add(idToken.ToString()); // Changed
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
