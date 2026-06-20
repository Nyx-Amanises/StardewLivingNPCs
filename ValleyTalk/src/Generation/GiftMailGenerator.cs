using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;

namespace ValleyTalk;

/// <summary>
/// Generates gift-mail letter bodies with the LLM on demand and caches them by request id so the
/// LivingNPCs side can poll for the result later (the letter is delivered a day or more after the
/// gift is given, and the mail asset is assembled synchronously, so generation must be detached).
/// On any failure the cache simply never reports a body and the caller keeps its template.
/// </summary>
internal sealed class GiftMailGenerator
{
    private enum Status { Pending, Ready, Failed }

    private sealed class Entry
    {
        public Status Status;
        public string Text = string.Empty;
    }

    private const int MaxEntries = 64;
    private const int MaxConcurrent = 2;
    private const int PromptTokens = 220;

    private static readonly GiftMailGenerator _instance = new();
    public static GiftMailGenerator Instance => _instance;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(MaxConcurrent, MaxConcurrent);

    private GiftMailGenerator()
    {
    }

    /// <summary>
    /// Starts (once) an asynchronous generation for the given request. Safe to call from the game
    /// thread: the NPC persona is captured here, and only the network call runs in the background.
    /// </summary>
    public void Request(string requestId, string npcName, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(requestId) || _entries.ContainsKey(requestId))
        {
            return;
        }

        string motive = "reciprocal";
        string itemLabel = string.Empty;
        string sourceGift = string.Empty;
        string displayName = npcName;
        int timeoutSeconds = 30;
        try
        {
            var json = JObject.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            motive = NullIfBlank(json.Value<string>("motive")) ?? motive;
            itemLabel = json.Value<string>("itemLabel") ?? string.Empty;
            sourceGift = json.Value<string>("sourceGift") ?? string.Empty;
            displayName = NullIfBlank(json.Value<string>("npcDisplayName")) ?? npcName;
            timeoutSeconds = json.Value<int?>("timeoutSeconds") ?? timeoutSeconds;
        }
        catch
        {
            // Malformed payload: fall through with defaults; validation/template fallback still applies.
        }

        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 120);

        if (Llm.Instance == null || Llm.Instance is LlmDummy)
        {
            Fail(requestId, displayName, motive, "no-model");
            return;
        }

        // Capture persona on the calling (game) thread; never touch game state in the background task.
        Character character = DialogueBuilder.Instance.GetCharacterByName(npcName);
        string display = character?.StardewNpc?.displayName ?? displayName;
        string persona = BuildPersona(character);
        bool zh = IsChineseLocale();
        string system = BuildSystemPrompt(zh);
        string user = BuildUserPrompt(zh, display, persona, motive, itemLabel, sourceGift);

        Prune();
        _entries[requestId] = new Entry { Status = Status.Pending };
        _ = Task.Run(() => GenerateAsync(requestId, display, motive, system, user, zh, timeoutSeconds));
    }

    /// <summary>Returns the validated mail body if ready, otherwise an empty string.</summary>
    public string TryGet(string requestId)
    {
        if (!string.IsNullOrWhiteSpace(requestId)
            && _entries.TryGetValue(requestId, out Entry entry)
            && entry.Status == Status.Ready)
        {
            return entry.Text;
        }

        return string.Empty;
    }

    private async Task GenerateAsync(string requestId, string display, string motive, string system, string user, bool zh, int timeoutSeconds)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            LlmResponse response = await Llm.Instance
                .RunInference(system, string.Empty, $"NPC: {display}", user, string.Empty, n_predict: PromptTokens, allowRetry: false, disableThinking: true)
                .WaitAsync(cts.Token)
                .ConfigureAwait(false);

            if (response == null || !response.IsSuccess || string.IsNullOrWhiteSpace(response.Text))
            {
                Fail(requestId, display, motive, "model-failed");
                return;
            }

            if (!GiftMailContentValidator.TryNormalize(response.Text, out string body, out string reason))
            {
                Fail(requestId, display, motive, reason);
                return;
            }

            // Language check lives here (not in the pure validator) because it depends on the
            // configured game locale via SMAPI.
            if (ConversationTextPostProcessor.LooksLikeWrongLanguage(body))
            {
                Fail(requestId, display, motive, "wrong-language");
                return;
            }

            body = EnsureSalutation(body, zh);
            _entries[requestId] = new Entry { Status = Status.Ready, Text = body };
            ModEntry.SMonitor?.Log($"AI gift mail generated for {display} ({motive}, {body.Length} chars).", LogLevel.Info);
        }
        catch (Exception ex)
        {
            string reason = ex is OperationCanceledException or TimeoutException ? "timeout" : ex.GetType().Name;
            Fail(requestId, display, motive, reason);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Fail(string requestId, string display, string motive, string reason)
    {
        _entries[requestId] = new Entry { Status = Status.Failed };
        ModEntry.SMonitor?.Log($"AI gift mail generation failed for {display} ({motive}): {reason}; template will be used.", LogLevel.Info);
    }

    private void Prune()
    {
        if (_entries.Count <= MaxEntries)
        {
            return;
        }

        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            if (pair.Value.Status != Status.Pending)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
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
        return zh
            ? "你在写一封简短、符合角色性格的信。只输出信的正文(纯文本散文),不要标题、不要署名,也不要任何游戏符号(例如 % 或 [ ])。2 到 4 个短句,保持该角色的语气。"
            : "You are writing a short, in-character letter. Output ONLY the letter body as plain prose — no subject line, no signature, and no game symbols (such as % or [ ]). 2 to 4 short sentences. Stay in the character's voice.";
    }

    private static string BuildUserPrompt(bool zh, string display, string persona, string motive, string itemLabel, string sourceGift)
    {
        var prompt = new StringBuilder();
        string item = string.IsNullOrWhiteSpace(itemLabel) ? (zh ? "一件小东西" : "a small gift") : itemLabel;
        string source = string.IsNullOrWhiteSpace(sourceGift) ? (zh ? "你之前送的礼物" : "the gift you gave earlier") : sourceGift;

        if (zh)
        {
            prompt.AppendLine($"角色:{display}。");
            if (!string.IsNullOrWhiteSpace(persona))
            {
                prompt.AppendLine($"性格:{persona}。");
            }

            prompt.AppendLine(motive switch
            {
                "birthday" => $"情境:农夫在{display}生日时送了「{source}」。{display}想随信回赠「{item}」作为生日谢礼。",
                "help_request_reward" => $"情境:农夫帮{display}完成了关于「{source}」的请求。{display}想随信附上「{item}」作为答谢。",
                _ => $"情境:农夫之前送给{display}「{source}」。{display}想随信回赠「{item}」。",
            });
            prompt.AppendLine("用 @ 代表农夫的名字(游戏会自动替换)。现在用该角色的口吻,写这封信的正文。");
        }
        else
        {
            prompt.AppendLine($"Character: {display}.");
            if (!string.IsNullOrWhiteSpace(persona))
            {
                prompt.AppendLine($"Personality: {persona}.");
            }

            prompt.AppendLine(motive switch
            {
                "birthday" => $"Context: the farmer gave {display} \"{source}\" for their birthday. {display} wants to enclose \"{item}\" as a birthday thank-you.",
                "help_request_reward" => $"Context: the farmer completed {display}'s request involving \"{source}\". {display} wants to enclose \"{item}\" as thanks.",
                _ => $"Context: the farmer earlier gave {display} \"{source}\". {display} wants to enclose \"{item}\" as a return gift.",
            });
            prompt.AppendLine("Use @ as a placeholder for the farmer's name (the game replaces it). Now write the body of this letter in the character's voice.");
        }

        return prompt.ToString();
    }

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
        return ModEntry.SHelper?.Translation.Locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NullIfBlank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
