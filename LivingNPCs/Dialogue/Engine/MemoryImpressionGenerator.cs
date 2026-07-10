using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

using LivingNPCs.Dialogue.Llm;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>一次记忆印象压缩的强类型请求（WP16 §4.1：废除 requestId 与 JSON 往返）。
/// 每条记忆可带 "[-Nd]" 前缀 = 约 N 游戏日前。</summary>
internal sealed record MemoryImpressionRequest(
    string NpcName,
    string NpcDisplayName,
    string ExistingImpression,
    IReadOnlyList<string> Memories,
    int TimeoutSeconds);

/// <summary>
/// Compresses a batch of evicted long-term memories (supplied by LivingNPCs) plus the existing
/// relationship impression into an updated biographical impression, using one LLM call.
/// True-async API (WP16): the caller awaits the task; null means failure, so it keeps its backlog
/// and retries another day — no memory is lost. Everything needed is captured before the first
/// await, only the network call runs in the background.
/// </summary>
internal sealed class MemoryImpressionGenerator
{
    private const int MaxConcurrent = 1;
    private const int ResponseTokens = 500;
    private const int MaxImpressionCharacters = 1200;

    private static readonly MemoryImpressionGenerator _instance = new();
    public static MemoryImpressionGenerator Instance => _instance;

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);

    private MemoryImpressionGenerator()
    {
    }

    /// <summary>Generates the updated impression paragraph, or null on any failure.</summary>
    public async Task<string?> GenerateAsync(MemoryImpressionRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.NpcName))
        {
            return null;
        }

        string display = string.IsNullOrWhiteSpace(request.NpcDisplayName) ? request.NpcName : request.NpcDisplayName;
        var memories = (request.Memories ?? Array.Empty<string>())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();
        int timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 10, 180);

        if (memories.Count == 0)
        {
            return Fail(display, "no-memories");
        }

        if (LegacyLlm.Instance == null || LegacyLlm.Instance is LegacyLlmDummy)
        {
            return Fail(display, "no-model");
        }

        bool zh = IsChineseLocale();
        string system = BuildSystemPrompt(zh);
        string user = BuildUserPrompt(zh, display, request.ExistingImpression ?? string.Empty, memories);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            LlmResponse response = await LegacyLlm.Instance
                .RunInference(system, string.Empty, $"NPC: {display}", user, string.Empty, n_predict: ResponseTokens, allowRetry: false, disableThinking: true)
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);

            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
            {
                return Fail(display, "model-failed");
            }

            if (!TryNormalize(response.Text, out string impression))
            {
                return Fail(display, "empty-after-normalize");
            }

            if (ConversationTextPostProcessor.LooksLikeWrongLanguage(impression))
            {
                return Fail(display, "wrong-language");
            }

            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.memoryCompressed",
                    new { npc = display, chars = impression.Length },
                    $"Memory impression compressed for {display} ({impression.Length} chars)."),
                LogLevel.Info);
            return impression;
        }
        catch (Exception ex)
        {
            string reason = ex is OperationCanceledException or TimeoutException ? "timeout" : ex.GetType().Name;
            return Fail(display, reason);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? Fail(string display, string reason)
    {
        DialogueServices.Monitor?.Log(
            Util.GetConsoleString(
                "dialogue.log.memoryCompressFailed",
                new { npc = display, reason },
                $"Memory impression compression failed for {display}: {reason}; memories stay queued for a later retry."),
            LogLevel.Info);
        return null;
    }

    private static bool TryNormalize(string raw, out string impression)
    {
        impression = StripJsonFence(raw)
            .Replace("\r", string.Empty)
            .Trim();

        // The impression is injected as one prose paragraph; collapse list markers and line breaks.
        var lines = impression
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimStart(' ', '-', '*', '•').Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
        impression = string.Join(" ", lines).Trim();

        if (impression.Length > MaxImpressionCharacters)
        {
            impression = impression[..MaxImpressionCharacters];
        }

        return !string.IsNullOrWhiteSpace(impression);
    }

    private static string StripJsonFence(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        int firstNewLine = text.IndexOf('\n');
        int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
        {
            return text;
        }

        return text[(firstNewLine + 1)..lastFence].Trim();
    }

    private static string BuildSystemPrompt(bool zh)
    {
        return zh
            ? "你在为一个游戏角色维护对农夫的长期印象。把现有印象与新增的零散记忆合并成一段更新后的印象叙述。要求:第三人称;保留具体事实(帮忙的次数、喜好、承诺、边界、重要事件);合并重复内容;越久远的事越概括;最多5句话。只输出这段印象文字,不要标题、列表或解释。"
            : "You maintain a game character's long-term impression of the farmer. Merge the existing impression with the newly evicted memories into one updated impression narrative. Requirements: third person; keep concrete facts (how many times the farmer helped, likes/dislikes, promises, boundaries, notable events); merge duplicates; summarize older events more coarsely; at most 5 sentences. Output ONLY the impression text — no headings, lists, or explanations.";
    }

    private static string BuildUserPrompt(bool zh, string display, string existingImpression, List<string> memories)
    {
        var prompt = new StringBuilder();
        if (zh)
        {
            prompt.AppendLine($"角色:{display}。");
            prompt.AppendLine(string.IsNullOrWhiteSpace(existingImpression)
                ? "现有印象:(尚无,这是第一次总结。)"
                : $"现有印象:{existingImpression}");
            prompt.AppendLine("新增记忆(每条开头的 [-N天] 表示大约发生在 N 天前;游戏中一季=28天,一年=112天):");
        }
        else
        {
            prompt.AppendLine($"Character: {display}.");
            prompt.AppendLine(string.IsNullOrWhiteSpace(existingImpression)
                ? "Existing impression: (none yet; this is the first summary.)"
                : $"Existing impression: {existingImpression}");
            prompt.AppendLine("New memories (the [-Nd] prefix means roughly N in-game days ago; one season = 28 days, one year = 112 days):");
        }

        foreach (string memory in memories)
        {
            prompt.AppendLine($"- {memory}");
        }

        prompt.AppendLine(zh
            ? "现在输出更新后的完整印象。"
            : "Now output the full updated impression.");
        return prompt.ToString();
    }

    private static bool IsChineseLocale()
    {
        return DialogueServices.Helper?.Translation.Locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
    }
}
