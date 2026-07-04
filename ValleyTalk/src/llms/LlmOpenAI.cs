using ValleyTalk;

namespace ValleyTalk;

internal class LlmOpenAi : LlmOpenAiBase, IGetModelNames
{
    public LlmOpenAi(string apiKey, string modelName = null)
    {
        url = "https://api.openai.com";
        this.apiKey = apiKey;
        this.modelName = modelName ?? "gpt-4o";
    }

    public override string ExtraInstructions => "";

    public override bool IsHighlySensoredModel => false;

    // 官方端点：按 NPC 前缀哈希分片路由缓存，并让流式回包带真实 usage（含 cached_tokens）。
    protected override bool SendPromptCacheKey => true;

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
