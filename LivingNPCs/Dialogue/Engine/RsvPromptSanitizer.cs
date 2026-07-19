using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Final, prompt-facing sanitization helpers for the no-RSV-AI policy. These helpers deliberately
/// avoid looking up game state, so they are safe to use after an asynchronous boundary.
/// </summary>
internal static class RsvPromptSanitizer
{
    public static bool IsBlockedCharacter(Character? character)
    {
        if (character == null)
        {
            return false;
        }

        return RsvAiPolicy.IsBlockedNpcName(character.Name);
    }

    public static string CharacterIdentity(Character? character, string fallback)
    {
        if (IsBlockedCharacter(character))
        {
            return fallback;
        }

        string name = character?.Name ?? string.Empty;
        string display = character?.DisplayName ?? string.Empty;
        string identity = string.IsNullOrWhiteSpace(display)
            ? name
            : string.IsNullOrWhiteSpace(name) || string.Equals(display, name, StringComparison.OrdinalIgnoreCase)
                ? display
                : $"{display} ({name})";

        return string.IsNullOrWhiteSpace(identity) ? fallback : identity.Trim();
    }

    public static string SafeInline(string? value, string fallback = "")
    {
        return string.IsNullOrWhiteSpace(value) || RsvAiPolicy.ContainsBlockedReference(value)
            ? fallback
            : value.Trim();
    }

    public static string SafeMultiline(string? value)
    {
        return RsvAiPolicy.RemoveBlockedLines(value ?? string.Empty);
    }

    public static IReadOnlyList<string> SafeLines(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => !RsvAiPolicy.ContainsBlockedReference(value))
            .Select(value => value.Trim())
            .ToList();
    }
}
