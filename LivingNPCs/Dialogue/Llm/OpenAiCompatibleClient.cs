namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 自定义 OpenAI 兼容端点（Provider 值 "OpenAiCompatible"）：基地址来自用户配置并做规整；
/// 标准形态耗尽后追加 instructions 回退形态；模型列表无条件发（本地端点可无鉴权）。
/// </summary>
internal sealed class OpenAiCompatibleClient : OpenAiChatClientBase
{
    private readonly string _baseAddress;

    public OpenAiCompatibleClient(LlmConnectionSettings settings)
        : base(settings)
    {
        _baseAddress = NormalizeBaseAddress(settings.ServerAddress);
    }

    public override string ProviderId => "OpenAiCompatible";

    protected override string DefaultModelName => "mistral-large-latest";

    protected override string BaseAddress => _baseAddress;

    protected override bool SupportsInstructionsFallback => true;

    protected override bool RequireApiKeyForModelList => false;
}
