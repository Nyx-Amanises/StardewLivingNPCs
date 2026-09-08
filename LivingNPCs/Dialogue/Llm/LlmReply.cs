using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Dialogue;

/// <summary>推理响应（WP11 §5）。失败不抛异常，调用方（WP10）看 IsSuccess 决定回落原版对话。</summary>
internal sealed class LlmReply
{
    public bool IsSuccess { get; init; }

    /// <summary>False when regenerating the same request cannot repair a completed but unusable answer.</summary>
    public bool Retryable { get; init; } = true;

    public string Text { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>最后一次尝试的 HTTP 状态码；从异常链取不到时默认 500。</summary>
    public int HttpStatus { get; init; }

    public TokenUsage Usage { get; init; } = new();

    public static LlmReply Success(string text, TokenUsage? usage)
    {
        return new LlmReply
        {
            IsSuccess = true,
            Text = text,
            HttpStatus = 200,
            Usage = usage ?? new TokenUsage()
        };
    }

    public static LlmReply Failure(string errorMessage, int httpStatus, bool retryable = true, TokenUsage? usage = null)
    {
        return new LlmReply
        {
            IsSuccess = false,
            Retryable = retryable,
            ErrorMessage = errorMessage,
            HttpStatus = httpStatus <= 0 ? 500 : httpStatus,
            Usage = usage ?? new TokenUsage()
        };
    }
}
