using ValleyTalk;

namespace ValleyTalk;

internal class LlmDeepSeek : LlmOpenAiBase, IGetModelNames
{
    public LlmDeepSeek(string apiKey, string modelName = null)
    {
        url = "https://api.deepseek.com";

        this.apiKey = apiKey;
        this.modelName = modelName ?? "deepseek-chat";
    }

    public override string ExtraInstructions => "";

    public override bool IsHighlySensoredModel => false;

    // DeepSeek 上下文缓存全自动（命中数见 usage 的 prompt_cache_hit_tokens / cached_tokens），
    // 这里只需让流式回包带真实 usage。
    protected override bool SendStreamUsageOptions => true;

    public string[] GetModelNames()
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return new string[] { };
        }
        return CoreGetModelNames();
    }
}