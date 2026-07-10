using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 启动/切换提供商时的连接自检（WP11 §4.5）。自检 = 一次禁止重试的真实推理；
/// 永不禁用引擎（钉死语义）；不经过熔断装饰器（不计入熔断统计）。
/// 所有本地化诊断文案先在主线程解析完再交给后台任务（文案解析要过游戏内容管线）。
/// </summary>
internal static class ConnectionSelfCheck
{
    /// <summary>主线程入口：同步解析文案，后台跑纯网络 IO + 线程安全日志。</summary>
    public static Task Start(LlmClientHost host, LlmClientBase client, LlmConnectionSettings settings)
    {
        SelfCheckTexts texts = ResolveTexts(client);
        return Task.Run(async () =>
        {
            try
            {
                await RunAsync(host, client, settings, texts).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.stepFailed",
                        new { step = "run the LLM connection check", error = ex },
                        $"Failed to run the LLM connection check: {ex}"),
                    LogLevel.Error);
            }
        });
    }

    private static async Task RunAsync(LlmClientHost host, LlmClientBase client, LlmConnectionSettings settings, SelfCheckTexts texts)
    {
        // 陈旧检查防护（前）：用户连续切换提供商时，旧检查静默放弃、不打日志。
        if (!ReferenceEquals(host.CurrentRaw, client))
        {
            return;
        }

        var request = new LlmRequest
        {
            SystemPrompt = "You are performing LLM connection testing",
            StableContext = string.Empty,
            NpcContext = string.Empty,
            Tail = "Please just respond with 'Connection successful'",
            AllowRetry = false,
            TimeoutOverride = TimeSpan.FromMinutes(1)
        };

        LlmReply reply;
        try
        {
            reply = await client.CompleteAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            reply = LlmReply.Failure(ex.Message, 500);
        }

        // 陈旧检查防护（后）。
        if (!ReferenceEquals(host.CurrentRaw, client))
        {
            return;
        }

        IMonitor? monitor = DialogueServices.Monitor;
        if (reply.IsSuccess && !string.IsNullOrWhiteSpace(reply.Text) && reply.Text.Length >= 5)
        {
            monitor?.Log(texts.Success, LogLevel.Info);
            return;
        }

        monitor?.Log(texts.CantGenerate, LogLevel.Error);
        if (!string.IsNullOrWhiteSpace(reply.ErrorMessage))
        {
            monitor?.Log(reply.ErrorMessage, LogLevel.Error);
        }

        bool providerNeedsApiKey = !LlmClientFactory.TryGetMetadata(client.ProviderId, out LlmProviderMetadata metadata) || metadata.RequiresApiKey;
        if (providerNeedsApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            monitor?.Log(texts.ApiKey, LogLevel.Error);
        }
        else if (client is IModelNameSource modelSource)
        {
            IReadOnlyList<string> names = modelSource.GetModelNames();
            if (names.Count == 0)
            {
                monitor?.Log(texts.GetNames, LogLevel.Error);
            }
            else if (string.IsNullOrWhiteSpace(settings.ModelName))
            {
                monitor?.Log(texts.ModelName, LogLevel.Error);
            }
            else if (names.Contains(settings.ModelName, StringComparer.Ordinal))
            {
                monitor?.Log(texts.ValidModelName, LogLevel.Error);
            }
            else
            {
                monitor?.Log(texts.ModelName, LogLevel.Error);
            }
        }
        else
        {
            monitor?.Log(texts.GenericError, LogLevel.Error);
        }

        bool usesServerAddress = metadata is { RequiresServerAddress: true };
        if (usesServerAddress && !(settings.ServerAddress ?? string.Empty).Contains("https", StringComparison.OrdinalIgnoreCase))
        {
            monitor?.Log(texts.Insecure, LogLevel.Error);
        }

        // 无论怎么失败，最后补一条 Warn：自检永不禁用引擎，真实对话时再试。
        monitor?.Log(texts.NonBlocking, LogLevel.Warn);
    }

    /// <summary>九条文案走 i18n（键归 WP15），键缺失时用英文兜底（§4.5）。必须在主线程调用。</summary>
    private static SelfCheckTexts ResolveTexts(LlmClientBase client)
    {
        string model = client.EffectiveModelName;
        string provider = client.GetType().Name;
        var tokens = new { Model = model, Provider = provider };
        return new SelfCheckTexts
        {
            Success = Util.GetConsoleString(
                "modelCheckSuccess",
                tokens,
                $"LLM connection check passed for model {model} ({provider})."),
            CantGenerate = Util.GetConsoleString(
                "modelCheckCantGenerate",
                tokens,
                $"LLM connection check failed for model {model} ({provider})."),
            ApiKey = Util.GetConsoleString(
                "modelCheckApiKey",
                null,
                "No API key is configured. Enter the provider's API key in the mod config."),
            ModelName = Util.GetConsoleString(
                "modelCheckModelName",
                null,
                "Check the configured model name: it is empty or not in the provider's model list."),
            ValidModelName = Util.GetConsoleString(
                "modelCheckValidModelName",
                null,
                "The provider lists the configured model, so the connection settings look right. The model may not be a text model, the endpoint may be unsafe, or the API key may be rejected for generation."),
            GetNames = Util.GetConsoleString(
                "modelCheckGetNames",
                null,
                "The provider returned no model list. Check the API key."),
            GenericError = Util.GetConsoleString(
                "modelCheckGenericError",
                null,
                "Check the server address and connection settings."),
            Insecure = Util.GetConsoleString(
                "modelCheckInsecure",
                null,
                "The server address does not use HTTPS; the connection is not secure."),
            NonBlocking = Util.GetConsoleString(
                "modelCheckNonBlocking",
                null,
                "The LLM connection check failed, but the dialogue engine stays enabled and will retry on the next real conversation.")
        };
    }

    private sealed class SelfCheckTexts
    {
        public string Success { get; init; } = string.Empty;
        public string CantGenerate { get; init; } = string.Empty;
        public string ApiKey { get; init; } = string.Empty;
        public string ModelName { get; init; } = string.Empty;
        public string ValidModelName { get; init; } = string.Empty;
        public string GetNames { get; init; } = string.Empty;
        public string GenericError { get; init; } = string.Empty;
        public string Insecure { get; init; } = string.Empty;
        public string NonBlocking { get; init; } = string.Empty;
    }
}
