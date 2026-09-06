using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

using LivingNPCs.Dialogue.Llm;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>一次关系印象生成或更新的强类型请求（WP16 §4.1：废除 requestId 与 JSON 往返）。
/// 每条记忆可带 "[-Nd]" 前缀 = 约 N 游戏日前。</summary>
internal sealed record MemoryImpressionRequest(
    string NpcName,
    string NpcDisplayName,
    string ExistingImpression,
    IReadOnlyList<string> Memories,
    int TimeoutSeconds);

/// <summary>
/// Updates the relationship impression from existing prose and supplied relationship evidence,
/// including important active memories, current state, and evicted memories, using one LLM call.
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
        if (request == null
            || string.IsNullOrWhiteSpace(request.NpcName)
            || RsvAiPolicy.IsBlockedNpcName(request.NpcName)
            || RsvAiPolicy.ContainsBlockedReference(request.NpcName))
        {
            return null;
        }

        bool zh = IsChineseLocale();
        string rawDisplay = string.IsNullOrWhiteSpace(request.NpcDisplayName) ? request.NpcName : request.NpcDisplayName;
        string display = RsvPromptSanitizer.SafeInline(rawDisplay, zh ? "这位村民" : "the villager");
        List<string> memories = RsvPromptSanitizer.SafeLines(request.Memories).ToList();
        int timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 10, 180);

        if (memories.Count == 0)
        {
            return Fail(display, "no-memories");
        }

        if (LegacyLlm.Instance == null || LegacyLlm.Instance is LegacyLlmDummy)
        {
            return Fail(display, "no-model");
        }

        string system = BuildSystemPrompt(zh);
        string existingImpression = RsvPromptSanitizer.SafeMultiline(request.ExistingImpression);
        string user = BuildUserPrompt(zh, display, existingImpression, memories);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            LlmResponse response = await LegacyLlm.Instance
                .RunInference(system, string.Empty, string.Empty, user, string.Empty, n_predict: ResponseTokens, allowRetry: false, disableThinking: true, ct: cts.Token)
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
        string instruction = zh
            ? "你在为一个游戏角色维护对农夫的当前关系印象。根据已有印象和提供的证据，生成首次印象或更新现有印象。"
                + "证据可能包括重要的长期记忆、玩家偏好、共同经历、矛盾及其当前状态，以及需压缩的旧记忆。"
                + "用具体事实支持角色对农夫的看法，保留重要的承诺、边界和关系变化；以最新明确的状态纠正过时的印象，未解决的矛盾不得写成已经和解。"
                + "证据与已有印象或不同来源之间可能重合：同一事实只算一次，不得把重复记录当作多次互动。"
                + "不得凭空提升亲密、信任或恋爱关系；区分愿望、约定和实际发生的事，未履约的承诺不能写成已经完成。"
                + "概括久远细节，避免只堆砌事实或昵称。用中文、第三人称写成一段，最多5句话。只输出完整的印象文字，不要标题、列表或解释。"
            : "You maintain a game character's current relationship impression of the farmer. Use the existing impression and supplied evidence to write a first impression or update the existing one. "
                + "Evidence may include important long-term memories, farmer preferences, shared experiences, conflicts and their current status, and older memories being compressed. "
                + "Ground the character's view of the farmer in concrete facts, preserving important promises, boundaries, and relationship changes. Use the latest explicit status to correct outdated impressions; do not describe unresolved conflicts as reconciled. "
                + "Evidence may overlap with the existing impression or with other sources: count each fact only once, and never treat duplicate records as repeated interactions. "
                + "Do not invent greater intimacy, trust, or romance. Distinguish wishes and agreements from events that actually happened; unfulfilled promises are not completed actions. "
                + "Summarize older details more coarsely, and avoid merely listing facts or nicknames. Write one paragraph in English and third person, with at most 5 sentences. Output ONLY the full impression text, with no headings, lists, or explanations.";
        return instruction + " " + PromptDataBoundary.SystemRule;
    }

    private static string BuildUserPrompt(bool zh, string display, string existingImpression, List<string> memories)
    {
        display = RsvPromptSanitizer.SafeInline(display, zh ? "这位村民" : "the villager");
        existingImpression = RsvPromptSanitizer.SafeMultiline(existingImpression);
        memories = RsvPromptSanitizer.SafeLines(memories).ToList();
        var prompt = new StringBuilder();
        if (zh)
        {
            prompt.AppendLine("角色资料:");
            prompt.AppendLine(PromptDataBoundary.Wrap("memory_npc_identity", display));
            prompt.AppendLine("现有印象:");
            prompt.AppendLine(PromptDataBoundary.Wrap(
                "memory_existing_impression",
                string.IsNullOrWhiteSpace(existingImpression) ? "(尚无，这是首次形成关系印象。)" : existingImpression));
            prompt.AppendLine("关系证据（含当前事实、状态与待压缩的旧记忆，可能与已有印象重合；每条开头的 [-Nd] 表示大约发生在 N 天前；游戏中一季=28天，一年=112天）：");
        }
        else
        {
            prompt.AppendLine("Character data:");
            prompt.AppendLine(PromptDataBoundary.Wrap("memory_npc_identity", display));
            prompt.AppendLine("Existing impression:");
            prompt.AppendLine(PromptDataBoundary.Wrap(
                "memory_existing_impression",
                string.IsNullOrWhiteSpace(existingImpression) ? "(none yet; this is the first relationship impression.)" : existingImpression));
            prompt.AppendLine("Relationship evidence (current facts and status, plus older memories to compress; may overlap with the existing impression; the [-Nd] prefix means roughly N in-game days ago; one season = 28 days, one year = 112 days):");
        }

        var memoryData = new StringBuilder();
        foreach (string memory in memories)
        {
            memoryData.AppendLine($"- {memory}");
        }
        prompt.AppendLine(PromptDataBoundary.Wrap("memory_new_memories", memoryData.ToString()));

        prompt.AppendLine(zh
            ? "现在输出更新后的完整印象。"
            : "Now output the full updated impression.");
        return prompt.ToString();
    }

    internal static string BuildSystemPromptForTesting(bool zh) => BuildSystemPrompt(zh);

    internal static string BuildUserPromptForTesting(
        bool zh,
        string display,
        string existingImpression,
        IReadOnlyList<string> memories) => BuildUserPrompt(zh, display, existingImpression, memories.ToList());

    private static bool IsChineseLocale()
    {
        return DialogueServices.Helper?.Translation.Locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
    }
}
