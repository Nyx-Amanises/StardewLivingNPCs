using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 成品 Stardew 对话串与流式选项拼装（WP10 §3.2 / §4.11 / §4.13）。
/// $q/$r 的 id 与键是旧存档兼容契约，逐字保留。
/// </summary>
internal static class ResponseFormatter
{
    /// <summary>$q index 静态自增计数器，初值 20000（§3.2 契约）。</summary>
    private static int questionId = EngineConstants.QuestionIdSeed;

    internal static void ResetQuestionIdForTests()
    {
        questionId = EngineConstants.QuestionIdSeed;
    }

    /// <summary>是否拼装应答菜单（§4.11）。</summary>
    public static bool ShouldBuildMenu(int lineCount, string typedResponses, bool endConversation, bool allowTypedFallback)
    {
        if (endConversation)
        {
            return false;
        }

        if (lineCount <= 1 && !string.Equals(typedResponses, "Always", StringComparison.OrdinalIgnoreCase) && !allowTypedFallback)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 拼装 FormattedLine：台词 + 可选 $q/$r 菜单 + 可选 skip# 前缀（§3.2）。
    /// </summary>
    public static string BuildFormattedLine(
        string dialogueLine,
        IReadOnlyList<string> options,
        string typedResponses,
        bool endConversation,
        bool allowTypedFallback,
        bool dontSkipNext)
    {
        var builder = new StringBuilder();
        builder.Append(dialogueLine);

        int lineCount = 1 + options.Count;
        if (ShouldBuildMenu(lineCount, typedResponses, endConversation, allowTypedFallback))
        {
            int index = Interlocked.Increment(ref questionId);
            string respond = Util.GetString("outputRespond");
            string staySilent = Util.GetString("outputStaySilent", returnNull: true) ?? "Stay silent";
            builder.Append($"#$q {index} {EngineConstants.KeyDefault}#{respond}");
            builder.Append($"#$r -999999 0 {EngineConstants.KeySilent}#{staySilent}");
            foreach (string option in options)
            {
                builder.Append($"#$r -999998 0 {EngineConstants.KeyNext}#{option}");
            }

            if (!string.Equals(typedResponses, "Never", StringComparison.OrdinalIgnoreCase))
            {
                string typed = Util.GetString("uiTypeYourResponse", returnNull: true) ?? "Type your response";
                builder.Append($"#$r -999997 0 {EngineConstants.KeyTypedResponse}#{typed}");
            }
        }

        string formatted = builder.ToString();
        return dontSkipNext ? formatted : EngineConstants.SkipPrefix + formatted;
    }

    /// <summary>
    /// 流式选项集合（§4.13；不用 $q/$r 字符串）。行数 ≤1 且 TypedResponses != Always → 空集；
    /// 结束判定为真 → 空集。
    /// </summary>
    public static List<StreamingResponseOption> BuildStreamingOptions(
        IReadOnlyList<string> options,
        string typedResponses,
        bool endConversation)
    {
        var result = new List<StreamingResponseOption>();
        if (endConversation)
        {
            return result;
        }

        int lineCount = 1 + options.Count;
        if (lineCount <= 1 && !string.Equals(typedResponses, "Always", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        string staySilent = Util.GetString("outputStaySilent", returnNull: true) ?? "Stay silent";
        result.Add(new StreamingResponseOption(staySilent, StreamingResponseOptionKind.Silent));
        result.AddRange(options.Select(option => new StreamingResponseOption(option, StreamingResponseOptionKind.Generated)));
        if (!string.Equals(typedResponses, "Never", StringComparison.OrdinalIgnoreCase))
        {
            string typed = Util.GetString("uiTypeYourResponse", returnNull: true) ?? "Type your response";
            result.Add(new StreamingResponseOption(typed, StreamingResponseOptionKind.Typed));
        }

        return result;
    }

    /// <summary>
    /// 结束会话判定（§4.11，三者取或）：分析 EndConversation、玩家末句启发式、NPC 首行启发式。
    /// </summary>
    public static bool IsConversationEnded(ConversationAnalysis analysis, string lastPlayerLine, string npcFirstLine)
    {
        return analysis.EndConversation
            || ConversationTextPostProcessor.PlayerLikelyEndedConversation(lastPlayerLine)
            || ConversationTextPostProcessor.NpcLikelyEndedConversation(npcFirstLine);
    }
}
