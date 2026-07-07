using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 选项概率查询能力（仅 llama.cpp 实现，WP11 §3.5/§5）。不进 ILlmClient，
/// 消费方（WP10 若仍需要）对原始客户端做类型探测使用。其余提供商不实现即"不支持"。
/// </summary>
internal interface ITokenProbabilities
{
    /// <summary>
    /// 对给定请求做一次带 n_probs 的补全，把 token 前缀树上的概率聚合到各选项桶。
    /// 返回 选项 → 概率；查询失败抛出异常（与推理通道同一套异常翻译）。
    /// </summary>
    Task<IReadOnlyDictionary<string, double>> GetOptionProbabilitiesAsync(
        LlmRequest request,
        IReadOnlyList<string> options,
        CancellationToken ct);
}
