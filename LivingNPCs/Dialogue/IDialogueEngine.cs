using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>玩家开启对话/送礼/事件触发时请求一次生成；结果经 callback 回主线程。</summary>
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct);

    /// <summary>流式：每产生一个可显示片段回调一次（已做后处理的安全文本）。</summary>
    Task StreamAsync(GenerationRequest request, IStreamSink sink, CancellationToken ct);
}

/// <summary>触发类型（WP10 §5.1）。婚姻台词归 Scheduled，靠 DialogueKey 解析。</summary>
internal enum GenerationTrigger
{
    Scheduled,
    Conversation,
    Gift
}

/// <summary>会话元素（文本 + 是否玩家行 + GUID；GUID 用于会话续接去重合并，§4.14）。</summary>
internal sealed record ConversationTurn(string Text, bool IsPlayerLine, string Id);

/// <summary>一次生成请求（WP10 §5.1）。快照由引擎入口在主线程采集。</summary>
internal sealed class GenerationRequest
{
    public string NpcName { get; init; } = string.Empty;
    public GenerationTrigger Trigger { get; init; }
    public string DialogueKey { get; init; } = string.Empty;
    public string OriginalLine { get; init; } = string.Empty;
    public IReadOnlyList<ConversationTurn> Conversation { get; init; } = new List<ConversationTurn>();
    public string GiftItemId { get; init; } = string.Empty;

    /// <summary>礼物口味：0 喜爱 / 2 喜欢 / 4 不喜欢 / 6 讨厌 / 其他 中性（§4.1）。</summary>
    public int GiftTaste { get; init; } = 8;

    /// <summary>行为系统注入的上下文（WP16 直调）。</summary>
    public string BehaviorContext { get; init; } = string.Empty;

    public GameStateSnapshot Snapshot { get; init; } = new();
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

    public TokenUsage Usage { get; init; } = new();
}

/// <summary>流式回调（WP10 §5.2/§4.13）。OnToken 给原始增量，过滤由消费方用搬运件做。</summary>
internal interface IStreamSink
{
    void OnToken(string delta);

    void OnCompleted(GenerationResult result, IReadOnlyList<StreamingResponseOption> options);

    void OnFailed();
}
