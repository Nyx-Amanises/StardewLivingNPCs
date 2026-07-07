using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Llm;

namespace LivingNPCs.Dialogue;

/// <summary>
/// LLM 提供商层（WP11）对引擎（WP10）暴露的统一客户端接口。
/// 成员由 01 §2 钉死：ProviderId、CompleteAsync、StreamAsync（事件流）。
/// </summary>
internal interface ILlmClient
{
    string ProviderId { get; }

    Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct);

    /// <summary>
    /// 事件流：若干 TextDelta 增量，随后一条 Usage 事件（流内真实用量或估算，Source 标注口径），最后一条 Done。
    /// 终态失败（含降级链全部耗尽）以异常抛出（<see cref="LlmStreamException"/>）；取消异常向上传播。
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(LlmRequest request, CancellationToken ct);
}

internal enum LlmStreamEventKind
{
    TextDelta,
    Usage,
    Done
}

/// <summary>Kind: TextDelta | Usage | Done；TextDelta 带 Text，Usage 带 TokenUsage（01 §2 裁决）。</summary>
internal readonly record struct LlmStreamEvent(LlmStreamEventKind Kind, string Text, TokenUsage? Usage)
{
    public static LlmStreamEvent Delta(string text) => new(LlmStreamEventKind.TextDelta, text, null);

    public static LlmStreamEvent ForUsage(TokenUsage usage) => new(LlmStreamEventKind.Usage, string.Empty, usage);

    public static LlmStreamEvent Done() => new(LlmStreamEventKind.Done, string.Empty, null);
}

/// <summary>
/// 提供商能力标志（WP11 §4.7），提示词侧（WP10）消费。
/// 01 §2 钉死了 ILlmClient 的成员表，故按 WP11 §6 的建议做成独立能力接口而非并入 ILlmClient。
/// </summary>
internal interface ILlmCapabilities
{
    /// <summary>内容审查严格的模型（Claude、Dummy 为 true），提示词侧据此回避易触发拒答的措辞。</summary>
    bool IsHighlySensoredModel { get; }

    /// <summary>追加进指令区的提供商特定文案；仅 llama.cpp 非空。</summary>
    string ExtraInstructions { get; }
}
