namespace LivingNPCs.Dialogue.Llm;

/// <summary>OpenAI 官方端点（Provider 值 "OpenAI"）：缓存键与流式 usage 双开。</summary>
internal sealed class OpenAiClient : OpenAiChatClientBase
{
    public OpenAiClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "OpenAI";

    protected override string DefaultModelName => "gpt-4o";

    protected override string BaseAddress => "https://api.openai.com";

    protected override bool SendPromptCacheKey => true;

    protected override bool SendStreamUsageOptions => true;
}
