using System;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 流式通道的终态失败（降级链 ③，WP11 §3.1）。非流式通道以失败 LlmReply 表达，
/// 流式事件流没有错误事件位（01 §2 钉死三种 Kind），故以异常向消费方（WP10）传播。
/// </summary>
internal sealed class LlmStreamException : Exception
{
    public int HttpStatus { get; }

    public LlmStreamException(string message, int httpStatus)
        : base(message)
    {
        HttpStatus = httpStatus <= 0 ? 500 : httpStatus;
    }
}
