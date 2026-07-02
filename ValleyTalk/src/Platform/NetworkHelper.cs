using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace ValleyTalk.Platform
{
    /// <summary>
    /// Shared HTTP entry point for every LLM provider. All requests reuse one HttpClient so
    /// connections (and their TLS handshakes) are pooled instead of being re-established per call.
    /// The client itself has no timeout; each request enforces its own deadline via a linked
    /// cancellation token, so QueryTimeout changes apply live and streaming can run unbounded.
    /// </summary>
    public static class NetworkHelper
    {
        private static readonly HttpClient _httpClient;

        static NetworkHelper()
        {
            var handler = new HttpClientHandler();

            _httpClient = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            // Android-specific configuration
            if (AndroidHelper.IsAndroid)
            {
                // Add mobile-friendly headers
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "ValleyTalk/1.0 (Android; Stardew Valley Mod)");
            }
        }

        private static TimeSpan DefaultTimeout =>
            TimeSpan.FromSeconds(Math.Max(5, ModEntry.Config?.QueryTimeout ?? 85));

        /// <summary>
        /// Makes an HTTP request with Android-compatible settings. A null/empty content sends GET,
        /// otherwise POST with a JSON body.
        /// </summary>
        public static Task<string> MakeRequestAsync(string url, string content = null, CancellationToken cancellationToken = default, string authToken = null, TimeSpan? timeout = null)
        {
            return MakeRequestAsync(
                string.IsNullOrEmpty(content) ? HttpMethod.Get : HttpMethod.Post,
                url,
                content,
                headers: null,
                authToken,
                cancellationToken,
                timeout
            );
        }

        /// <summary>
        /// Makes an HTTP POST request with custom headers for specific LLM providers
        /// </summary>
        public static Task<string> MakeRequestWithCustomHeadersAsync(string url, string content, Dictionary<string, string> headers, CancellationToken cancellationToken = default, TimeSpan? timeout = null)
        {
            return MakeRequestAsync(HttpMethod.Post, url, content, headers, authToken: null, cancellationToken, timeout);
        }

        /// <summary>
        /// Core request method every overload funnels into: shared client, per-request headers,
        /// per-request timeout (defaults to the configured QueryTimeout).
        /// </summary>
        public static async Task<string> MakeRequestAsync(
            HttpMethod method,
            string url,
            string content,
            Dictionary<string, string> headers,
            string authToken,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            HttpResponseMessage response = null;
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (!string.IsNullOrEmpty(content))
                {
                    request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                }
                if (!string.IsNullOrEmpty(authToken))
                {
                    request.Headers.Add("Authorization", $"Bearer {authToken}");
                }
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout ?? DefaultTimeout);

                response = await _httpClient.SendAsync(request, timeoutCts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Request was cancelled");
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Request timed out");
            }
            catch (HttpRequestException ex)
            {
                string message = ex.Message;
                if (response != null && response.Content != null)
                {
                    message += $"\n (HTTP {(int)response.StatusCode} - {await response.Content.ReadAsStringAsync(CancellationToken.None)})";
                }
                throw new InvalidOperationException($"Network request failed: {message}", ex);
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Sends a request on the shared client for streaming consumption: only the response
        /// headers are awaited, the body stream stays open for the caller to read. The caller owns
        /// disposing the request and response, and bounds the read with its cancellation token.
        /// </summary>
        public static Task<HttpResponseMessage> SendForStreamingAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        /// <summary>
        /// Checks if network is available (basic check for Android)
        /// </summary>
        public static bool IsNetworkAvailable()
        {
            try
            {
                // Basic connectivity test
                return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Disposes the HTTP client
        /// </summary>
        public static void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
