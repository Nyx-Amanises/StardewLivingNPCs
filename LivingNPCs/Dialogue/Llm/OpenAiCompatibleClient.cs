using StardewModdingAPI;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 自定义 OpenAI 兼容端点（Provider 值 "OpenAiCompatible"）：基地址来自用户配置并做规整；
/// 地址是"不含 /v1 的完整 chat/completions 端点"（Cloudflare AI Gateway 等网关路径）时
/// 原样直用、不再强插 /v1（审查发现 4）；标准形态耗尽后追加 instructions 回退形态；
/// 模型列表无条件发（本地端点可无鉴权）。
/// </summary>
internal sealed class OpenAiCompatibleClient : OpenAiChatClientBase
{
    private readonly string _baseAddress;
    private readonly string? _verbatimChatEndpoint;

    public OpenAiCompatibleClient(LlmConnectionSettings settings)
        : base(settings)
    {
        _baseAddress = NormalizeBaseAddress(settings.ServerAddress);
        _verbatimChatEndpoint = DetectVerbatimChatEndpoint(settings.ServerAddress);

        // 端点改写结果记 Trace 便于排查（配置串 → 实际请求地址；两条路径都覆盖）。
        string configured = (settings.ServerAddress ?? string.Empty).Trim();
        if (configured.Length > 0)
        {
            DialogueServices.Monitor?.Log(
                $"[OpenAiCompatible] Server address '{configured}' -> chat endpoint '{ChatEndpoint}'"
                + (_verbatimChatEndpoint != null ? " (verbatim, no /v1 inserted)." : "."),
                LogLevel.Trace);
        }
    }

    public override string ProviderId => "OpenAiCompatible";

    protected override string DefaultModelName => "mistral-large-latest";

    protected override string BaseAddress => _baseAddress;

    /// <summary>完整端点直用时，chat 请求打用户配置的原始地址。</summary>
    protected override string ChatEndpoint => _verbatimChatEndpoint ?? base.ChatEndpoint;

    /// <summary>完整端点直用时，模型列表打同级 /models（网关不提供则照常空列表 + 错误日志）。</summary>
    protected override string ModelsEndpoint => _verbatimChatEndpoint != null
        ? _verbatimChatEndpoint[..^"/chat/completions".Length] + "/models"
        : base.ModelsEndpoint;

    protected override bool SupportsInstructionsFallback => true;

    protected override bool RequireApiKeyForModelList => false;
}
