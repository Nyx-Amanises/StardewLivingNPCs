using System.Collections.Generic;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 模型名列表能力（旧世界 IGetModelNames 的对应物）。实现方：OpenAI 家族、Anthropic、Google、VolcEngine。
/// 调用是同步阻塞的（内部 1 分钟超时），只允许在 GMCM UI 线程按钮点击、连接自检等一次性场景使用（WP11 §6）。
/// GMCM 的"是否显示模型下拉"请走 LlmClientFactory 的提供商元数据，不要对熔断装饰器做类型探测。
/// </summary>
internal interface IModelNameSource
{
    /// <summary>取不到（异常、无 key）时记错误日志并返回空列表，永不抛出。</summary>
    IReadOnlyList<string> GetModelNames();
}
