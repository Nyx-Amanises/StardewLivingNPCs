using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using StardewValley;

namespace LivingNPCs.Dialogue;

/// <summary>
/// 跨包契约（01 §2）：引擎对游戏侧（WP12/WP16）暴露的唯一入口。WP10 实现。
/// </summary>
internal interface IDialogueEngine
{
    bool IsEnabledFor(NPC npc);

    /// <summary>玩家开启对话/送礼/事件触发时请求一次纯生成；调用方在展示成功后提交结果。</summary>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct);

    /// <summary>在主线程确认结果仍有效且已展示后提交一次游戏状态副作用。</summary>
    bool CommitResult(GenerationResult result);

    /// <summary>流式：每产生一个可显示片段回调一次（已做后处理的安全文本）。</summary>
    Task StreamAsync(GenerationRequest request, IStreamSink sink, CancellationToken ct);
}

/// <summary>触发类型（WP10 §5.1）。婚姻台词归 Scheduled，靠 DialogueKey 解析。</summary>
internal enum GenerationTrigger
{
    Scheduled,
    /// <summary>由玩家普通右键开启的 AI 对话首句；保留 Scheduled 的改写语义，但会写入会话上下文。</summary>
    ConversationOpening,
    Conversation,
    Gift
}

/// <summary>会话元素（文本 + 是否玩家行 + GUID；GUID 用于会话续接去重合并，§4.14）。</summary>
internal sealed record ConversationTurn(string Text, bool IsPlayerLine, string Id);

/// <summary>
/// Content captured while the request is still on the game thread. NpcBio instances are treated as
/// immutable after DialogueContentService finalizes them; invalidation replaces the cached object.
/// </summary>
internal sealed record GenerationContentSnapshot(
    NpcBio Bio,
    IReadOnlyDictionary<string, string> DialogueSamples,
    IReadOnlyList<PortraitFrameSemantics.Match>? PortraitFrames = null,
    int PortraitFrameCount = 0,
    string PortraitSignature = "missing")
{
    public IReadOnlyList<PortraitFrameSemantics.Match> ResolvedPortraitFrames =>
        this.PortraitFrames ?? System.Array.Empty<PortraitFrameSemantics.Match>();
}

/// <summary>一次生成请求（WP10 §5.1）。快照由引擎入口在主线程采集。</summary>
internal sealed class GenerationRequest
{
    public string NpcName { get; init; } = string.Empty;
    /// <summary>Captured on the game thread so generation workers never lazy-load NPC display data.</summary>
    public string NpcDisplayName { get; init; } = string.Empty;
    public GenerationTrigger Trigger { get; init; }
    public string DialogueKey { get; init; } = string.Empty;
    public string OriginalLine { get; init; } = string.Empty;
    public IReadOnlyList<ConversationTurn> Conversation { get; init; } = new List<ConversationTurn>();
    public string GiftItemId { get; init; } = string.Empty;

    /// <summary>
    /// Whether this request originated from the game-thread capture path and must therefore use
    /// the final-portrait whitelist even if content snapshot capture failed.
    /// </summary>
    public bool UsesRuntimePortraitWhitelist { get; init; }

    /// <summary>礼物口味：0 喜爱 / 2 喜欢 / 4 不喜欢 / 6 讨厌 / 其他 中性（§4.1）。</summary>
    public int GiftTaste { get; init; } = 8;

    /// <summary>行为系统注入的上下文（WP16 直调）。</summary>
    public string BehaviorContext { get; init; } = string.Empty;

    public GameStateSnapshot Snapshot { get; init; } = new();

    /// <summary>主线程捕获的内容；测试或旧调用方为空时使用引擎的纯数据回退。</summary>
    public GenerationContentSnapshot? ContentSnapshot { get; init; }
}

/// <summary>
/// A successful generation's game-state commit payload. Generation is deliberately pure until
/// the scheduler has validated the generation and presented the result on the main thread.
/// </summary>
internal sealed class GenerationCommit
{
    private int claimed;

    public GenerationRequest Request { get; init; } = new();
    public string NpcDisplayName { get; init; } = string.Empty;
    public string LastPlayerLine { get; init; } = string.Empty;
    public string DialogueLine { get; init; } = string.Empty;
    public IReadOnlyList<ConversationTurn> Conversation { get; init; } = System.Array.Empty<ConversationTurn>();

    public bool TryClaim()
    {
        return Interlocked.CompareExchange(ref this.claimed, 1, 0) == 0;
    }
}

/// <summary>一次生成的产物（WP10 §5.2）。</summary>
internal sealed class GenerationResult
{
    /// <summary>含 skip#/$q$r 菜单的成品 Stardew 对话串。</summary>
    public string FormattedLine { get; init; } = string.Empty;

    /// <summary>首行台词 + 各选项（净文本）。</summary>
    public string[] ParsedLines { get; init; } = System.Array.Empty<string>();

    /// <summary>3.3 契约的序列化分析（行为系统 RecordExchange 第四参数）。</summary>
    public string AnalysisJson { get; init; } = string.Empty;

    public bool EndConversation { get; init; }

    /// <summary>呈现用对话键（§4.12）。</summary>
    public string DialogueKey { get; init; } = string.Empty;

    /// <summary>true = 生成失败后的占位回退行（"..."）；呈现层据此给玩家游戏内失败提示。</summary>
    public bool IsFallback { get; init; }

    public TokenUsage Usage { get; init; } = new();

    /// <summary>内部提交载荷；生成失败或未成功解析时为空。</summary>
    internal GenerationCommit? Commit { get; init; }
}

/// <summary>流式回调（WP10 §5.2/§4.13）。OnToken 给原始增量，过滤由消费方用搬运件做。</summary>
internal interface IStreamSink
{
    void OnToken(string delta);

    void OnCompleted(GenerationResult result, IReadOnlyList<StreamingResponseOption> options);

    void OnFailed();
}
