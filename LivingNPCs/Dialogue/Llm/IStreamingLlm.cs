using System;
using System.Threading;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

internal interface IStreamingLlm
{
    Task<LlmResponse> RunInferenceStreaming(
        string systemPromptString,
        string gameCacheString,
        string npcCacheString,
        string promptString,
        Action<string> onToken,
        CancellationToken cancellationToken,
        string responseStart = "",
        int n_predict = 2048,
        string cacheContext = "",
        bool allowRetry = true
    );
}
