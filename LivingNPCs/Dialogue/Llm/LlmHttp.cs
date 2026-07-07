using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 全部提供商共用的进程级 HttpClient（WP11 §4.3）：连接与 TLS 握手池化复用。
/// 客户端自身 Timeout 无限，每个请求用"调用方取消令牌 + 超时"链接的 CTS 自行限时，
/// 因此配置 QueryTimeout 改动对下一个请求即时生效，流式请求也可不受统一超时约束地长跑。
/// </summary>
internal static class LlmHttp
{
    private static readonly object Gate = new();
    private static HttpClient? _client;
    private static HttpMessageHandler? _handlerForTests;

    /// <summary>默认单请求超时 = max(5, 配置 QueryTimeout) 秒；配置缺省 85。</summary>
    public static TimeSpan DefaultTimeout
    {
        get
        {
            int configured = DialogueServices.Config?.QueryTimeout ?? 85;
            return TimeSpan.FromSeconds(Math.Max(5, configured));
        }
    }

    /// <summary>测试注入假传输层；设置后强制重建客户端。传 null 恢复真实网络。</summary>
    internal static void SetHandlerForTests(HttpMessageHandler? handler)
    {
        lock (Gate)
        {
            _handlerForTests = handler;
            _client?.Dispose();
            _client = null;
        }
    }

    /// <summary>mod 卸载路径调用（实践中进程随游戏退出，非关键）。</summary>
    public static void DisposeClient()
    {
        lock (Gate)
        {
            _client?.Dispose();
            _client = null;
        }
    }

    /// <summary>
    /// 统一入口：内容为空发 GET，否则 POST JSON。成功（2xx）返回响应体字符串。
    /// 异常翻译（调用方依赖类型区分）：调用方令牌 → OperationCanceledException("Request was cancelled")；
    /// 超时 → TimeoutException("Request timed out")；HTTP 层错误 → InvalidOperationException，
    /// 已拿到响应时消息附 (HTTP {状态码} - {响应体})，内层 HttpRequestException 携带状态码。
    /// </summary>
    public static async Task<string> SendAsync(
        string url,
        string? jsonContent,
        string? authToken,
        IReadOnlyDictionary<string, string>? headers,
        TimeSpan? timeout,
        CancellationToken ct)
    {
        using var request = BuildRequest(url, jsonContent, authToken, headers);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw TranslateSendException(ex, ct);
        }

        using (response)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw TranslateSendException(ex, ct);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildHttpError((int)response.StatusCode, body);
            }

            return body;
        }
    }

    /// <summary>
    /// 流式专用入口：ResponseHeadersRead 发送，只等响应头（受同一超时约束），
    /// 响应体流交调用方持有并负责释放，读取由调用方令牌限界。非 2xx 时读出错误体并按统一口径抛出。
    /// </summary>
    public static async Task<HttpResponseMessage> SendForStreamAsync(
        string url,
        string jsonContent,
        string? authToken,
        IReadOnlyDictionary<string, string>? headers,
        TimeSpan? headerTimeout,
        CancellationToken ct)
    {
        using var request = BuildRequest(url, jsonContent, authToken, headers);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(headerTimeout ?? DefaultTimeout);

        HttpResponseMessage response;
        try
        {
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw TranslateSendException(ex, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                body = string.Empty;
            }
            finally
            {
                response.Dispose();
            }

            throw BuildHttpError((int)response.StatusCode, body);
        }

        return response;
    }

    private static HttpClient Client
    {
        get
        {
            lock (Gate)
            {
                if (_client == null)
                {
                    _client = _handlerForTests != null
                        ? new HttpClient(_handlerForTests, disposeHandler: false)
                        : new HttpClient();
                    _client.Timeout = Timeout.InfiniteTimeSpan;
                    if (AndroidPlatform.IsAndroid)
                    {
                        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BuildAndroidUserAgent());
                    }
                }

                return _client;
            }
        }
    }

    private static string BuildAndroidUserAgent()
    {
        string version = "1.0";
        try
        {
            version = typeof(LlmHttp).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        }
        catch
        {
        }

        return $"LivingNPCs/{version} (Android; Stardew Valley Mod)";
    }

    private static HttpRequestMessage BuildRequest(
        string url,
        string? jsonContent,
        string? authToken,
        IReadOnlyDictionary<string, string>? headers)
    {
        var request = string.IsNullOrEmpty(jsonContent)
            ? new HttpRequestMessage(HttpMethod.Get, url)
            : new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

        if (!string.IsNullOrEmpty(authToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        if (headers != null)
        {
            foreach ((string name, string value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return request;
    }

    private static Exception TranslateSendException(Exception ex, CancellationToken callerToken)
    {
        if (ex is OperationCanceledException)
        {
            if (callerToken.IsCancellationRequested)
            {
                return new OperationCanceledException("Request was cancelled", callerToken);
            }

            return new TimeoutException("Request timed out");
        }

        return new InvalidOperationException(ex.Message, ex);
    }

    private static InvalidOperationException BuildHttpError(int statusCode, string body)
    {
        var inner = new HttpRequestException($"HTTP {statusCode}", null, (System.Net.HttpStatusCode)statusCode);
        return new InvalidOperationException($"Request failed (HTTP {statusCode} - {body})", inner);
    }
}
