namespace LivingNPCs.Dialogue.Llm;

/// <summary>Mistral（Provider 值 "Mistral"）：无缓存键、无 stream_options（端点不认）。</summary>
internal sealed class MistralClient : OpenAiChatClientBase
{
    public MistralClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "Mistral";

    protected override string DefaultModelName => "mistral-large-latest";

    protected override string BaseAddress => "https://api.mistral.ai";
}
