using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq; // Added
using System.Net.Http;
using System.Text;
using Newtonsoft.Json; // Changed
using Newtonsoft.Json.Linq; // Added
using System.Threading;
using System.Threading.Tasks;
using ValleyTalk;
using ValleyTalk.Platform;

namespace ValleyTalk;

internal class LlmGemini : Llm, IGetModelNames
{
    private string apiKey;
    private string modelName;

    private sealed record GeminiRequestBody(
        string Json,
        bool HasThinkingParameters,
        string ThinkingLevel,
        string ThinkingParameters
    );

    public LlmGemini(string apiKey, string modelName = null)
    {
        this.apiKey = apiKey;
        this.modelName = modelName ?? "gemini-2.5-flash";

        url = $"https://generativelanguage.googleapis.com/v1beta/models/{this.modelName}:generateContent?key=";
    }

    public Dictionary<string,string> CacheContexts { get; private set; } = new Dictionary<string, string>();

    public override string ExtraInstructions => "";

    public override bool IsHighlySensoredModel => false;

    public string[] GetModelNames()
    {
        try{
        var modelsUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key="+apiKey;
        
        // Use Android-compatible network helper
        string responseString;
        if (AndroidHelper.IsAndroid && NetworkHelper.IsNetworkAvailable())
        {
            responseString = NetworkHelper.MakeRequestAsync(modelsUrl).Result;
        }
        else
        {
            var client = new HttpClient();
            var response = client.GetAsync(modelsUrl).Result;
            responseString = response.Content.ReadAsStringAsync().Result;
        }
        
        var responseJson = JObject.Parse(responseString); // Changed
        var modelsToken = responseJson["models"]; // Changed
        var modelNames = new List<string>();
        if (modelsToken is JArray modelsArray) // Changed
        {
            foreach (var model in modelsArray)
            {
                var nameToken = model["name"]; // Changed
                if (nameToken != null)
                {
                    var name = nameToken.ToString(); // Changed
                    if (name.StartsWith("models/"))
                    {
                        name = name.Substring(7);
                    }
                    modelNames.Add(name);
                }
            }
        }
        return modelNames.ToArray();
        }
        catch(Exception ex)
        {
            Log.Debug(ex.Message);
            return new string[] { };
        }
    }

    internal override async Task<LlmResponse> RunInference(string systemPromptString, string gameCacheString, string npcCacheString, string promptString, string responseStart = "",int n_predict = 2048,string cacheContext="",bool allowRetry = true,bool disableThinking = false)
    {
        var useContext = string.Empty;

        promptString = gameCacheString + npcCacheString + promptString;
        if (!string.IsNullOrEmpty(cacheContext) && CacheContexts.TryGetValue(cacheContext, out var cachedContextValue))
        {
            useContext = cachedContextValue;
        }

        // call out to URL passing the object as the body, and return the result
        var fullUrl = url + apiKey;
        
        // Check network availability on Android
        if (AndroidHelper.IsAndroid && !NetworkHelper.IsNetworkAvailable())
        {
            throw new InvalidOperationException("Network not available");
        }

        string responseString = "";
        HttpResponseMessage response = new HttpResponseMessage();
        foreach (GeminiRequestBody requestBody in BuildRequestBodies(systemPromptString, promptString, n_predict, disableThinking))
        {
            string jsonData = requestBody.Json;
            int retry = allowRetry ? 3 : 1;
            string lastFailure = string.Empty;
            while (retry > 0)
            {
                try
                {
                    var json = new StringContent(
                        jsonData,
                        Encoding.UTF8,
                        "application/json"
                    );

                    if (AndroidHelper.IsAndroid)
                    {
                        responseString = await NetworkHelper.MakeRequestAsync(fullUrl, jsonData);
                    }
                    else
                    {
                        var client = new HttpClient
                        {
                            Timeout = TimeSpan.FromSeconds(ModEntry.Config.QueryTimeout)
                        };
                        response = await client.PostAsync(fullUrl, json);
                        responseString = await response.Content.ReadAsStringAsync();
                    }
                    lastFailure = responseString;
                    var responseJson = JObject.Parse(responseString); // Changed

                    if (responseJson == null)
                    {
                        throw new Exception("Failed to parse response");
                    }
                    else
                    {
                        if (!responseJson.TryGetValue("candidates", out var candidatesToken)
                            || !(candidatesToken is JArray candidatesArray)
                            || !candidatesArray.HasValues)
                        {
                            retry--;
                            continue;
                        }

                        var firstCandidate = candidatesArray.FirstOrDefault();
                        if (firstCandidate == null)
                        {
                            retry--;
                            continue;
                        }

                        var finishReasonToken = firstCandidate["finishReason"];
                        if (finishReasonToken == null || finishReasonToken.ToString() != "STOP")
                        {
                            retry--;
                            continue;
                        }

                        var contentToken = firstCandidate["content"];
                        if (contentToken == null)
                        {
                            retry--;
                            continue;
                        }

                        var partsToken = contentToken["parts"];
                        if (!(partsToken is JArray partsArray) || !partsArray.HasValues)
                        {
                            retry--;
                            continue;
                        }

                        var firstPart = partsArray.FirstOrDefault();
                        if (firstPart == null)
                        {
                            retry--;
                            continue;
                        }

                        var textToken = firstPart["text"];
                        if (textToken == null)
                        {
                            retry--;
                            continue;
                        }

                        var text = textToken.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return new LlmResponse(text, usage: TokenUsage.FromGeminiUsage(responseJson["usageMetadata"] as JObject));
                        }
                        return new LlmResponse("Empty response", (int)response.StatusCode);
                    }
                }
                catch(Exception ex)
                {
                    Log.Debug(ex.Message);
                    Log.Debug("Retrying...");
                    lastFailure = ex.Message;
                    retry--;
                    Thread.Sleep(100);
                }
            }

            if (requestBody.HasThinkingParameters)
            {
                LlmThinking.LogThinkingFallbackWarning(
                    modelName,
                    requestBody.ThinkingLevel,
                    requestBody.ThinkingParameters,
                    lastFailure
                );
            }
        }
        return new LlmResponse(responseString, (int)response.StatusCode);
    }

    private IEnumerable<GeminiRequestBody> BuildRequestBodies(
        string systemPromptString,
        string promptString,
        int n_predict,
        bool disableThinking)
    {
        string thinkingLevel = LlmThinking.ForCall(disableThinking);
        JObject thinkingConfig = LlmThinking.BuildGeminiThinkingConfig(thinkingLevel, modelName);
        if (thinkingConfig != null)
        {
            yield return new GeminiRequestBody(
                BuildRequestBody(systemPromptString, promptString, n_predict, thinkingConfig),
                true,
                thinkingLevel,
                $"thinkingConfig={thinkingConfig.ToString(Formatting.None)}"
            );
        }

        if (thinkingConfig == null || !LlmThinking.IsAuto(thinkingLevel))
        {
            yield return new GeminiRequestBody(
                BuildRequestBody(systemPromptString, promptString, n_predict, thinkingConfig: null),
                false,
                LlmThinking.Auto,
                string.Empty
            );
        }
    }

    private static string BuildRequestBody(
        string systemPromptString,
        string promptString,
        int n_predict,
        JObject thinkingConfig)
    {
        var generationConfig = new JObject
        {
            ["maxOutputTokens"] = n_predict,
            ["temperature"] = 1.5,
            ["topP"] = 0.9
        };
        if (thinkingConfig != null)
        {
            generationConfig["thinkingConfig"] = thinkingConfig;
        }

        var body = new JObject
        {
            ["safetySettings"] = new JArray(
                new JObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "BLOCK_NONE" },
                new JObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "BLOCK_MEDIUM_AND_ABOVE" }),
            ["system_instruction"] = new JObject
            {
                ["parts"] = new JObject { ["text"] = systemPromptString }
            },
            ["contents"] = new JObject
            {
                ["parts"] = new JObject { ["text"] = promptString }
            },
            ["generationConfig"] = generationConfig
        };

        return body.ToString(Formatting.None);
    }

    internal override Dictionary<string, double>[] RunInferenceProbabilities(string fullPrompt, int n_predict = 1)
    {
        throw new System.NotImplementedException();
    }
}
