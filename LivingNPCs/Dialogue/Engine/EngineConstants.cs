using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 引擎 ↔ 补丁（WP12）共享常量（WP10 §3.2）。键名与标记是旧存档兼容契约，逐字保留。
/// </summary>
internal static class EngineConstants
{
    /// <summary>对话键前缀（3.2 契约：SLD_，旧存档进行中的对话要能被新引擎的键接住）。</summary>
    public const string KeyPrefix = "SLD_";

    public const string KeyDefault = KeyPrefix + "Default";
    public const string KeySilent = KeyPrefix + "Silent";
    public const string KeyNext = KeyPrefix + "Next";
    public const string KeyTypedResponse = KeyPrefix + "TypedResponse";
    public const string KeyConversation = KeyPrefix + "Conversation";
    public const string KeyError = KeyPrefix + "Error";
    public const string KeyStreaming = KeyPrefix + "Streaming";

    /// <summary>送礼应答键前缀：Accept_{礼物内部名}。</summary>
    public const string GiftKeyPrefix = "Accept_";

    /// <summary>会话应答行前缀：告知 WP12 补丁"跳过下一句原版台词"。</summary>
    public const string SkipPrefix = "skip#";

    /// <summary>WP12 补丁消费的标记对（3.2）。</summary>
    public const string DialogueGenerationTag = "$$$%%%";
    public const string DialogueSkipTag = "$$%%";

    /// <summary>$q 菜单静态自增计数器初值（3.2 契约）。</summary>
    public const int QuestionIdSeed = 20000;

    /// <summary>回退台词（4.2/4.16）。</summary>
    public const string FallbackLine = "...";

    /// <summary>元数据尾部标记（3.1；解析由 ConversationAnalysis 持有，这里供行清理用）。</summary>
    public const string MetadataMarker = "!LIVINGNPCS_META";
}
