namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// OpenAI 兼容"预设"提供商（WP11 扩展）：端点与默认模型内置，玩家通常只需要填 API Key
/// （本地预设如 Ollama / LM Studio 连 Key 都不用）。ServerAddress 留空时使用预设端点；
/// 在 config.json 里显式填写仍可覆盖（GMCM 对预设提供商不展示该字段）。
/// </summary>
internal sealed class OpenAiPresetClient : OpenAiChatClientBase
{
    private readonly string _providerId;
    private readonly string _baseAddress;
    private readonly string _defaultModelName;

    public OpenAiPresetClient(
        LlmConnectionSettings settings,
        string providerId,
        string presetBaseAddress,
        string defaultModelName)
        : base(settings)
    {
        _providerId = providerId;
        _baseAddress = NormalizeBaseAddress(
            string.IsNullOrWhiteSpace(settings.ServerAddress) ? presetBaseAddress : settings.ServerAddress);
        _defaultModelName = defaultModelName;
    }

    public override string ProviderId => _providerId;

    protected override string DefaultModelName => _defaultModelName;

    protected override string BaseAddress => _baseAddress;

    protected override bool SupportsInstructionsFallback => true;

    protected override bool RequireApiKeyForModelList => false;
}
