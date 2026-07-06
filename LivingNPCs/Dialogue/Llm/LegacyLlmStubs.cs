using System;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

internal class LegacyLlm
{
    public static LegacyLlm Instance { get; set; } = new LegacyLlmDummy();

    public virtual Task<LlmResponse> RunInference(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true,
        bool disableThinking = false)
    {
        // WP11-TODO: replace with ILlmClient.CompleteAsync/StreamAsync.
        throw new NotImplementedException("WP11-TODO: LLM provider layer is not implemented yet.");
    }
}

internal sealed class LegacyLlmDummy : LegacyLlm
{
    public override Task<LlmResponse> RunInference(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true,
        bool disableThinking = false)
    {
        return Task.FromResult(new LlmResponse
        {
            IsSuccess = false,
            ErrorMessage = "WP11-TODO: dummy LLM client"
        });
    }
}

internal sealed class LlmResponse
{
    public bool IsSuccess { get; init; }
    public string Text { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public TokenUsage Usage { get; init; } = new();
}