namespace LivingNPCs.Dialogue.Llm;

/// <summary>DeepSeek（Provider 值 "DeepSeek"）：只开流式 usage；上下文缓存全自动，不发 prompt_cache_key。</summary>
internal sealed class DeepSeekClient : OpenAiChatClientBase
{
    public DeepSeekClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "DeepSeek";

    protected override string DefaultModelName => "deepseek-chat";

    protected override string BaseAddress => "https://api.deepseek.com";

    protected override bool SendStreamUsageOptions => true;
}
