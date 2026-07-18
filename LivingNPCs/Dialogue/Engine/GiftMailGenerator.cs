using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

using LivingNPCs.Dialogue.Llm;
namespace LivingNPCs.Dialogue.Engine;

/// <summary>一次礼物邮件正文生成的强类型请求（WP16 §4.1：废除 requestId 与 JSON 往返）。</summary>
internal sealed record GiftMailRequest(
    string NpcName,
    string NpcDisplayName,
    string Motive,
    string ItemLabel,
    string SourceGift,
    string Tier,
    int TimeoutSeconds);

/// <summary>
/// Generates gift-mail letter bodies with the LLM on demand (the letter is delivered a day or
/// more after the gift is given, and the mail asset is assembled synchronously, so generation
/// runs detached from the mail build). True-async API (WP16): the caller awaits the task and a
/// null result means failure — it keeps its template fallback. Call from the game thread: the
/// NPC persona is captured before the first await, only the network call runs in the background.
/// </summary>
internal sealed class GiftMailGenerator
{
    private const int MaxConcurrent = 2;
    private const int PromptTokens = 220;

    private static readonly GiftMailGenerator _instance = new();
    public static GiftMailGenerator Instance => _instance;

    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);

    private GiftMailGenerator()
    {
    }

    /// <summary>Generates a validated mail body, or null on any failure (caller keeps its template).</summary>
    public async Task<string?> GenerateAsync(GiftMailRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.NpcName))
        {
            return null;
        }

        string motive = string.IsNullOrWhiteSpace(request.Motive) ? "reciprocal" : request.Motive;
        int timeoutSeconds = Math.Clamp(request.TimeoutSeconds, 5, 120);

        if (LegacyLlm.Instance == null || LegacyLlm.Instance is LegacyLlmDummy)
        {
            return Fail(request.NpcName, motive, "no-model");
        }

        // Capture persona on the calling (game) thread; never touch game state past the first await.
        Character character = DialogueBuilder.Instance.GetCharacterByName(request.NpcName);
        string display = character?.StardewNpc?.displayName
            ?? (string.IsNullOrWhiteSpace(request.NpcDisplayName) ? request.NpcName : request.NpcDisplayName);
        string persona = BuildPersona(character);
        bool zh = IsChineseLocale();
        string system = BuildSystemPrompt(zh);
        string user = BuildUserPrompt(zh, display, persona, motive, request.ItemLabel, request.SourceGift);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            LlmResponse response = await LegacyLlm.Instance
                .RunInference(system, string.Empty, string.Empty, user, string.Empty, n_predict: PromptTokens, allowRetry: false, disableThinking: true, ct: cts.Token)
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);

            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
            {
                return Fail(display, motive, "model-failed");
            }

            if (!GiftMailContentValidator.TryNormalize(response.Text, out string body, out string reason))
            {
                return Fail(display, motive, reason);
            }

            // Language check lives here (not in the pure validator) because it depends on the
            // configured game locale via SMAPI.
            if (ConversationTextPostProcessor.LooksLikeWrongLanguage(body))
            {
                return Fail(display, motive, "wrong-language");
            }

            body = EnsureSalutation(body, zh);
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.giftMailGenerated",
                    new { npc = display, motive, chars = body.Length },
                    $"AI gift mail generated for {display} ({motive}, {body.Length} chars)."),
                LogLevel.Info);
            return body;
        }
        catch (Exception ex)
        {
            string reason = ex is OperationCanceledException or TimeoutException ? "timeout" : ex.GetType().Name;
            return Fail(display, motive, reason);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? Fail(string display, string motive, string reason)
    {
        DialogueServices.Monitor?.Log(
            Util.GetConsoleString(
                "dialogue.log.giftMailFailed",
                new { npc = display, motive, reason },
                $"AI gift mail generation failed for {display} ({motive}): {reason}; template will be used."),
            LogLevel.Info);
        return null;
    }

    private static string BuildPersona(Character character)
    {
        if (character?.Bio == null)
        {
            return string.Empty;
        }

        var traits = character.Bio.Traits?.Values
            .Where(t => !string.IsNullOrWhiteSpace(t.Heading))
            .Take(3)
            .Select(t => string.IsNullOrWhiteSpace(t.Description) ? t.Heading : $"{t.Heading}: {t.Description}")
            .ToList();

        return traits != null && traits.Count > 0 ? string.Join("; ", traits) : string.Empty;
    }

    private static string BuildSystemPrompt(bool zh)
    {
        string instruction = zh
            ? "你在写一封简短、符合角色性格的信。只输出信的正文(纯文本散文),不要标题、不要署名,也不要任何游戏符号(例如 % 或 [ ])。2 到 4 个短句,保持该角色的语气。"
            : "You are writing a short, in-character letter. Output ONLY the letter body as plain prose — no subject line, no signature, and no game symbols (such as % or [ ]). 2 to 4 short sentences. Stay in the character's voice.";
        return instruction + " " + PromptDataBoundary.SystemRule;
    }

    private static string BuildUserPrompt(bool zh, string display, string persona, string motive, string itemLabel, string sourceGift)
    {
        var prompt = new StringBuilder();
        string item = string.IsNullOrWhiteSpace(itemLabel) ? (zh ? "一件小东西" : "a small gift") : itemLabel;
        string source = string.IsNullOrWhiteSpace(sourceGift) ? (zh ? "你之前送的礼物" : "the gift you gave earlier") : sourceGift;

        if (zh)
        {
            prompt.AppendLine("角色资料:");
            prompt.AppendLine(PromptDataBoundary.Wrap("gift_mail_npc_identity", display));
            if (!string.IsNullOrWhiteSpace(persona))
            {
                prompt.AppendLine("性格资料:");
                prompt.AppendLine(PromptDataBoundary.Wrap("gift_mail_persona", persona));
            }

            string context = motive switch
            {
                "birthday" => $"情境:农夫在{display}生日时送了「{source}」。{display}想随信回赠「{item}」作为生日谢礼。",
                "help_request_reward" => $"情境:农夫帮{display}完成了关于「{source}」的请求。{display}想随信附上「{item}」作为答谢。",
                _ => $"情境:农夫之前送给{display}「{source}」。{display}想随信回赠「{item}」。",
            };
            prompt.AppendLine(PromptDataBoundary.Wrap("gift_mail_context", context));
            prompt.AppendLine("用 @ 代表农夫的名字(游戏会自动替换)。现在用该角色的口吻,写这封信的正文。");
        }
        else
        {
            prompt.AppendLine("Character data:");
            prompt.AppendLine(PromptDataBoundary.Wrap("gift_mail_npc_identity", display));
            if (!string.IsNullOrWhiteSpace(persona))
            {
                prompt.AppendLine("Personality data:");
                prompt.AppendLine(PromptDataBoundary.Wrap("gift_mail_persona", persona));
            }

            string context = motive switch
            {
                "birthday" => $"Context: the farmer gave {display} \"{source}\" for their birthday. {display} wants to enclose \"{item}\" as a birthday thank-you.",
                "help_request_reward" => $"Context: the farmer completed {display}'s request involving \"{source}\". {display} wants to enclose \"{item}\" as thanks.",
                _ => $"Context: the farmer earlier gave {display} \"{source}\". {display} wants to enclose \"{item}\" as a return gift.",
            };
            prompt.AppendLine(PromptDataBoundary.Wrap("gift_mail_context", context));
            prompt.AppendLine("Use @ as a placeholder for the farmer's name (the game replaces it). Now write the body of this letter in the character's voice.");
        }

        return prompt.ToString();
    }

    internal static string BuildSystemPromptForTesting(bool zh) => BuildSystemPrompt(zh);

    internal static string BuildUserPromptForTesting(
        bool zh,
        string display,
        string persona,
        string motive,
        string itemLabel,
        string sourceGift) => BuildUserPrompt(zh, display, persona, motive, itemLabel, sourceGift);

    private static string EnsureSalutation(string body, bool zh)
    {
        if (body.Contains('@'))
        {
            return body;
        }

        return zh ? $"@，^{body}" : $"@,^{body}";
    }

    private static bool IsChineseLocale()
    {
        return DialogueServices.Helper?.Translation.Locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
    }
}
