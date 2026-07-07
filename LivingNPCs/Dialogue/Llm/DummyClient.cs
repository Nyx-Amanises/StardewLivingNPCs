using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>
/// 测试桩（Provider 值 "Dummy"，仅 DEBUG 构建注册；WP11 §3.6）。不发任何网络请求，
/// 五五开返回两种恰好符合引擎输出解析格式的固定文本，供无网/无 key 环境调试 UI 与解析管线。
/// </summary>
internal sealed class DummyClient : LlmClientBase
{
    private const string PlainLine = "- LLM generated string.";
    private const string QuestionLines = "- LLM generated question\n% One answer\n% Another answer\n% A third answer";

    public DummyClient(LlmConnectionSettings settings)
        : base(settings)
    {
    }

    public override string ProviderId => "Dummy";

    public override bool IsHighlySensoredModel => true;

    protected override string DefaultModelName => "dummy";

    public override Task<LlmReply> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        string text = Random.Shared.Next(2) == 0 ? PlainLine : QuestionLines;
        return Task.FromResult(LlmReply.Success(text, new TokenUsage()));
    }

    protected override IReadOnlyList<RequestCandidate> BuildRequestCandidates(LlmRequest request)
    {
        return Array.Empty<RequestCandidate>();
    }

    protected override Task<AttemptOutcome> ExecuteCandidateAsync(RequestCandidate candidate, LlmRequest request, CancellationToken ct)
    {
        throw new NotSupportedException("Dummy client does not execute HTTP candidates.");
    }
}
