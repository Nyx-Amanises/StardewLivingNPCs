using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValleyTalk;
using ValleyTalk.Platform;

namespace ValleyTalk;

internal class LlmVolcEngine : Llm, IGetModelNames
{
    protected string apiKey;
    protected string modelName;

    record PromptElement
    {
        public string role { get; set; }
        public string content { get; set; }
    }

    public LlmVolcEngine(string apiKey, string modelName = null)
    {
        url = "https://ark.cn-beijing.volces.com/api/v3";

        this.apiKey = apiKey;
        this.modelName = modelName ?? "doubao-1.5-pro";
    }

    public override string ExtraInstructions => "";

    public override bool IsHighlySensoredModel => false;

    public string[] GetModelNames()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return Array.Empty<string>();
        }
        return CoreGetModelNames();
    }

    // Only Volcengine models whose deep thinking can actually be switched off accept thinking:disabled.
    // Sending it to a plain model (e.g. doubao-1.5-pro) is a hard "parameter not supported" error, and
    // forced-thinking models (deepseek-r1, doubao-seed-1.6-thinking) can't turn it off. Conservative by
    // design: unknown names get no parameter (routing still works, just without the speedup) rather than
    // risking an error on every router call. Extend the lists below as new models ship.
    internal static bool ModelSupportsDisablingThinking(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        string m = model.ToLowerInvariant();

        // Forced-thinking models: accept the concept but can't disable it — never send the parameter.
        if (m.Contains("seed-1.6-thinking") || m.Contains("seed-1-6-thinking") || m.Contains("deepseek-r1"))
        {
            return false;
        }

        // Hybrid/dynamic models whose thinking can be turned off via the parameter.
        return m.Contains("seed-1.6") || m.Contains("seed-1-6")          // doubao-seed-1.6 / -lite / -flash
            || m.Contains("1.5-thinking") || m.Contains("1-5-thinking")  // doubao-1.5-thinking-pro (+ vision / -m)
            || m.Contains("deepseek-v3.1") || m.Contains("deepseek-v3-1");
    }

    internal override async Task<LlmResponse> RunInference(string systemPromptString, string gameCacheString, string npcCacheString, string promptString, string responseStart = "",int n_predict = 2048,string cacheContext="",bool allowRetry = true,bool disableThinking = false)
    {
        var inputString = JsonConvert.SerializeObject(new
            {
                model = modelName,
                max_tokens = n_predict,
                // Volcengine Ark 官方字段：关闭深度思考。仅在快速路由调用 (disableThinking) 且当前模型
                // 确实支持关闭思考时才传入（见 ModelSupportsDisablingThinking）——给不支持的模型传会直接
                // 报参数错误。直接 HTTP 调用时 thinking 与 model/messages 同级；OpenAI SDK 才需 extra_body。
                // null 会被序列化忽略，主对话请求体保持不变。
                thinking = (disableThinking && ModelSupportsDisablingThinking(modelName)) ? (object)new { type = "disabled" } : null,
                // Fast JSON passes (routing/gift-mail/action) request strict JSON to stop weak models
                // wrapping the object in prose. Only on the disableThinking path so chat replies are untouched.
                response_format = disableThinking ? (object)new { type = "json_object" } : null,
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
            },
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        var json = new StringContent(
            inputString,
            Encoding.UTF8,
            "application/json"
        );

        // call out to URL passing the object as the body, and return the result
        int retry = allowRetry ? 3 : 1;
        var fullUrl = $"{url}/chat/completions";
        
        // Check network availability on Android
        if (AndroidHelper.IsAndroid && !NetworkHelper.IsNetworkAvailable())
        {
            throw new InvalidOperationException("Network not available");
        }

        int apiResponseCode = 500;
        string responseString = "";
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

                    if (!responseJson.TryGetValue("choices", out var choicesToken) || !(choicesToken is JArray choicesArray) || !choicesArray.HasValues) { retry--; continue; }

                    var firstChoice = choicesArray.FirstOrDefault();
                    if (firstChoice == null) { retry--; continue; }

                    var messageToken = firstChoice["message"];
                    if (messageToken == null) { retry--; continue; }

                    var contentToken = messageToken["content"];
                    if (contentToken == null) { retry--; continue; }

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

    public string[] CoreGetModelNames(Dictionary<string, string> extraHeaders = null)
    {
        if (extraHeaders == null)
        {
            extraHeaders = new Dictionary<string, string>();
        }
        try 
        {
        var fullUrl = $"{url}/models";
        
        // Use Android-compatible network helper
        string responseString;
        if (AndroidHelper.IsAndroid && NetworkHelper.IsNetworkAvailable())
        {
            responseString = NetworkHelper.MakeRequestAsync(fullUrl, null, CancellationToken.None, apiKey).Result;
        }
        else
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(1)
            };
            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            foreach (var header in extraHeaders)
            {
                request.Headers.Add(header.Key, header.Value);
            }
            var response = client.SendAsync(request).Result;
            responseString = response.Content.ReadAsStringAsync().Result;
        }
        
        var responseJson = JObject.Parse(responseString);
        var dataToken = responseJson["data"];
        if (!(dataToken is JArray modelsArray))
        {
            return Array.Empty<string>();
        }

        var modelNames = new List<string>();
        foreach (var model in modelsArray)
        {
            var idToken = model["id"];
            if (idToken != null)
            {
                modelNames.Add(idToken.ToString());
            }
        }
        return modelNames.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            return Array.Empty<string>();
        }
    }
}
