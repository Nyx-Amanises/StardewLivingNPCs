using System;
using System.Linq;
using System.Text;

namespace ValleyTalk;

/// <summary>
/// Validates and normalizes an LLM-produced gift-mail body before it is allowed to replace the
/// hand-written template. The model only ever produces the letter prose; the attachment line,
/// title and signature are added by the mail builder, so anything that looks like a mail control
/// code, a refusal, the wrong language, or an empty/oversized blob is rejected and the caller
/// falls back to the template.
/// </summary>
public static class GiftMailContentValidator
{
    private const int MinLength = 16;
    private const int MaxLength = 700;

    private static readonly string[] RefusalMarkers =
    {
        "as an ai", "as a language model", "language model", "i cannot", "i can't", "i can not",
        "i'm sorry", "i am sorry", "cannot assist", "对不起", "抱歉", "我不能", "我无法", "作为一个ai", "作为ai"
    };

    /// <summary>
    /// Attempts to turn raw model output into a usable mail body. Returns <c>false</c> (with a short
    /// <paramref name="reason"/> for logging) when the output is not safe to use.
    /// </summary>
    public static bool TryNormalize(string raw, out string body, out string reason)
    {
        body = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            reason = "empty";
            return false;
        }

        string text = raw.Trim();

        // Drop a single layer of wrapping quotes the model sometimes adds around the whole letter.
        text = TrimWrappingQuotes(text);

        // Mail line breaks are "^"; convert real newlines and collapse runs so the letter is tidy.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = text.Replace("\n", "^");
        while (text.Contains("^^^"))
        {
            text = text.Replace("^^^", "^^");
        }

        text = text.Trim().Trim('^').Trim();

        if (text.Length < MinLength)
        {
            reason = "too-short";
            return false;
        }

        if (text.Length > MaxLength)
        {
            reason = "too-long";
            return false;
        }

        // "%" begins a mail command (e.g. %item, %money) and "[" begins a mail tag (e.g. [#], [letterbg]);
        // either would corrupt the letter, and the body should never contain them.
        if (text.IndexOf('%') >= 0)
        {
            reason = "contains-%";
            return false;
        }

        if (text.IndexOf('[') >= 0 || text.IndexOf(']') >= 0)
        {
            reason = "contains-bracket";
            return false;
        }

        if (text.Contains("{{") || text.Contains("}}"))
        {
            reason = "template-placeholder-leak";
            return false;
        }

        if (text.Contains("```"))
        {
            reason = "code-fence";
            return false;
        }

        string lower = text.ToLowerInvariant();
        if (RefusalMarkers.Any(marker => lower.Contains(marker)))
        {
            reason = "refusal-or-meta";
            return false;
        }

        body = text;
        return true;
    }

    private static string TrimWrappingQuotes(string text)
    {
        if (text.Length < 2)
        {
            return text;
        }

        char first = text[0];
        char last = text[^1];
        bool wrapped =
            (first == '"' && last == '"') ||
            (first == '\'' && last == '\'') ||
            (first == '“' && last == '”') ||
            (first == '「' && last == '」');

        return wrapped ? text[1..^1].Trim() : text;
    }
}
