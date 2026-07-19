using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs;

/// <summary>
/// Central policy boundary for Ridgeside Village content. The mod does not have permission to
/// use RSV text, characters, or locations as AI context, so every prompt-facing collector should
/// consult this policy before exposing an identity or location.
/// </summary>
internal static class RsvAiPolicy
{
    internal const string WithheldPlayerMessage = "[WITHHELD_PLAYER_MESSAGE]";

    private static readonly object AliasCacheGate = new();
    private static readonly HashSet<string> RuntimeBlockedAliases = new(StringComparer.OrdinalIgnoreCase);
    private static string[]? blockedFreeTextAliasCache;
    private static string[]? blockedDialogueAliasCache;
    private static ulong registeredGameAliasSaveId = ulong.MaxValue;
    private static string registeredGameAliasLocale = string.Empty;

    private static readonly string[] BlockedTextAliases =
    {
        "Ridgeside Village", "Ridgeside", "Rafseazz", "RSV", "Torts", "里奇赛德村", "里奇赛德", "岭边村", "山脊村", "托托"
    };

    // RSV injects a small number of generic conversation-topic keys into vanilla NPC dialogue.
    // These keys contain neither an RSV namespace nor an RSV NPC name, so they need an explicit
    // deny-list in addition to the namespace/name checks below.
    private static readonly string[] BlockedDialogueKeys =
    {
        "event_postweddingreception",
        "AntonLikesFarmer",
        "AntonLikesPaula",
        "PaulaLikesAnton",
        "PaulaLikesFarmer",
        "RSVFayeFasionShowInfo"
    };

    private static readonly HashSet<string> BlockedNpcNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acorn", "Aguar", "Alissa", "Althea", "Anton", "Ariah", "Belinda", "Bert", "Blair", "Bliss", "Bryle", "Carmen",
        "Corine", "Daia", "Ezekiel", "Faye", "Flor", "Freddie", "Helen", "Ian", "Irene", "Jeric", "Jio", "June", "Keahi",
        "Kenneth", "Kiarra", "Kimpoi", "Kiwi", "Lenny", "Lidens", "Lola", "Lorenzo", "Lorraine", "Louie", "Maddie", "Maive",
        "Malaya", "Nadaline", "Naomi", "Olga", "Paula", "Philip", "Pika", "Pipo", "Raeriyala", "RelicSpirit", "Richard",
        "Sari", "Sean", "Shanice", "Shiro", "Sonny", "Torts", "TreehouseGirl", "Trinnie", "Undreya", "Ysabelle", "Yuuma",
        "Zachary", "Zayne"
    };

    // Only highly distinctive names belong in the generic free-text fallback. All RSV NPC names
    // remain blocked in structured identity/dialogue-key fields, while ordinary prose can keep
    // common words and names such as Acorn, June, Helen, Richard, and Kiwi.
    private static readonly HashSet<string> BlockedFreeTextNpcNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Torts", "Raeriyala", "RelicSpirit", "TreehouseGirl", "Undreya", "Kimpoi", "Nadaline"
    };

    // RSV's Data/Locations entries retain these unnamespaced FormerLocationNames for save
    // migration. They can still appear in live game state, so match the complete legacy names
    // exactly instead of relying only on the current Custom_Ridgeside_/RSV namespace patterns.
    private static readonly HashSet<string> BlockedLegacyLocationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AguarBasement", "AguarCave", "AguarLab", "AlissaHouse", "AlissaShed", "BertHouse", "BertHouse2ndFloor",
        "Custom_JuneNPC_ResortDate", "EmberNight", "EzekielHouse", "EzekielPic", "FreddieHouse", "IanHouse",
        "JericHouse", "KennethHouse", "LennyHouse", "LogCabinHotel2ndFloor", "LogCabinHotel3rdFloor",
        "LogCabinHotelLobby", "LolaShed", "MaddieHouse", "MysticFalls1", "MysticFalls2", "MysticFalls3",
        "PikaHouse", "PrincessVilleDate", "Ridge", "RidgeFalls", "RidgesideVillage", "RSVAbandonedHouse",
        "RSVCableCar", "RSVCliff", "RSVGathering", "RSVGreenhouse1", "RSVGreenhouse2", "RSVHiddenWarp",
        "RSVHiddenWarp2", "RSVNinjaHouse", "RSVTheHike", "RSVTheRide", "ShiroHouse"
    };

    internal static bool IsBlockedNpc(NPC? npc)
    {
        // Internal names identify the installed RSV characters. Display names may legitimately
        // overlap another mod's localized NPC name, so they must not disable that NPC outright.
        return npc != null && IsBlockedNpcName(npc.Name);
    }

    internal static bool IsBlockedNpcName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string normalized = name.Trim().TrimEnd('·', '•', '-');
        return BlockedNpcNames.Contains(normalized)
            || IsStructuredRsvIdentifier(normalized);
    }

    internal static bool IsBlockedLocationName(string? locationName)
    {
        if (string.IsNullOrWhiteSpace(locationName))
        {
            return false;
        }

        string normalized = locationName.Trim();
        return BlockedLegacyLocationNames.Contains(normalized)
            || normalized.Contains("Ridgeside", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Rafseazz.RSV", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("RSV_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("RSV.", StringComparison.OrdinalIgnoreCase)
            || ContainsStructuredRsvDialogueIdentifier(normalized);
    }

    internal static bool IsBlockedContentId(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId))
        {
            return false;
        }

        return contentId.Contains("Rafseazz.RSV", StringComparison.OrdinalIgnoreCase)
            || contentId.Contains("Ridgeside", StringComparison.OrdinalIgnoreCase)
            || contentId.Contains("RSV_", StringComparison.OrdinalIgnoreCase)
            || contentId.Contains("RSV.", StringComparison.OrdinalIgnoreCase)
            || ContainsStructuredRsvDialogueIdentifier(contentId);
    }

    /// <summary>
    /// Detects dialogue keys sourced from RSV, including keys injected into vanilla NPC dialogue.
    /// Dialogue-key tokens use underscores as separators, unlike prose identifiers where an
    /// underscore is part of a word; keep this boundary rule separate from
    /// <see cref="ContainsBlockedReference(string?)"/>.
    /// </summary>
    internal static bool IsBlockedDialogueKey(string? dialogueKey)
    {
        if (string.IsNullOrWhiteSpace(dialogueKey))
        {
            return false;
        }

        string key = dialogueKey.Trim();
        if (IsBlockedContentId(key)
            || ContainsDialogueKeyIdentity(key, "RSV")
            || ContainsStructuredRsvDialogueIdentifier(key))
        {
            return true;
        }

        foreach (string knownKey in BlockedDialogueKeys)
        {
            if (ContainsDialogueKeyIdentity(key, knownKey))
            {
                return true;
            }
        }

        foreach (string alias in GetBlockedDialogueAliases())
        {
            if (ContainsDialogueKeyIdentity(key, alias))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Captures localized RSV names while the caller is already on the game thread. String
    /// sanitizers may run after an await, so they only read this cache and never touch Game1.
    /// </summary>
    internal static void RegisterRuntimeNpcAliases(IEnumerable<NPC>? villagers)
    {
        if (villagers == null)
        {
            return;
        }

        lock (AliasCacheGate)
        {
            foreach (NPC villager in villagers)
            {
                if (villager != null && BlockedFreeTextNpcNames.Contains(villager.Name))
                {
                    AddLocalizedRuntimeAlias(villager.Name, villager.displayName);
                }
            }
        }
    }

    internal static void RegisterRuntimeNpcAlias(string? internalName, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(internalName) || !BlockedFreeTextNpcNames.Contains(internalName))
        {
            return;
        }

        lock (AliasCacheGate)
        {
            AddLocalizedRuntimeAlias(internalName, displayName);
        }
    }

    internal static void RegisterRuntimeLocationAlias(string? internalName, string? displayName)
    {
        if (!IsBlockedLocationName(internalName) || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        lock (AliasCacheGate)
        {
            AddLocalizedRuntimeAlias(internalName, displayName);
        }
    }

    internal static void RegisterRuntimeContentAliases(string? contentId, params string?[] aliases)
    {
        if (!IsBlockedContentId(contentId))
        {
            return;
        }

        lock (AliasCacheGate)
        {
            foreach (string? alias in aliases ?? Array.Empty<string>())
            {
                AddRuntimeAlias(alias);
            }
        }
    }

    /// <summary>
    /// Captures localized RSV identities from loaded game data. Call only on the game thread;
    /// prompt sanitizers themselves remain game-state-free and only read the resulting cache.
    /// </summary>
    internal static void RegisterGameThreadAliases()
    {
        ulong saveId;
        string locale;
        try
        {
            saveId = Game1.uniqueIDForThisGame;
            locale = Dialogue.DialogueServices.Helper?.GameContent.CurrentLocale ?? string.Empty;
            if (string.IsNullOrWhiteSpace(locale))
            {
                locale = LocalizedContentManager.CurrentLanguageCode.ToString();
            }
        }
        catch
        {
            return;
        }

        lock (AliasCacheGate)
        {
            if (registeredGameAliasSaveId == saveId
                && string.Equals(registeredGameAliasLocale, locale, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Runtime aliases are locale/save specific. Mark this context as attempted up front so
            // a broken custom asset can't trigger a full world scan on every dialogue snapshot.
            RuntimeBlockedAliases.Clear();
            blockedFreeTextAliasCache = null;
            blockedDialogueAliasCache = null;
            registeredGameAliasSaveId = saveId;
            registeredGameAliasLocale = locale;
        }

        try
        {
            RegisterRuntimeNpcAliases(Utility.getAllVillagers());
            foreach (var pair in Game1.characterData)
            {
                RegisterRuntimeNpcAlias(pair.Key, pair.Value?.DisplayName);
            }
        }
        catch
        {
            // Structured NPC-name filters remain active if localized alias discovery fails.
        }

    }

    /// <summary>Detects known RSV identities/IDs in old prompt-facing text.</summary>
    internal static bool ContainsBlockedReference(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (IsBlockedContentId(text))
        {
            return true;
        }

        foreach (string legacyLocationName in BlockedLegacyLocationNames)
        {
            // "ridge" is ordinary geography in English. Structured location fields still block
            // the exact RSV legacy location, but free prose keeps the normal word.
            if (string.Equals(legacyLocationName, "Ridge", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ContainsIdentity(text, legacyLocationName))
            {
                return true;
            }
        }

        foreach (string alias in GetBlockedFreeTextAliases())
        {
            if (ContainsIdentity(text, alias))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsWithheldPlayerMessage(string? text)
        => string.Equals(text, WithheldPlayerMessage, StringComparison.Ordinal);

    /// <summary>Drops complete legacy-memory lines which contain RSV references.</summary>
    internal static string RemoveBlockedLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Preserve untouched prompt text byte-for-byte. Besides avoiding an unnecessary alias scan
        // per line, this keeps the caller's newline convention and prompt-cache keys stable when
        // there is no RSV data to remove.
        if (!ContainsBlockedReference(text))
        {
            return text;
        }

        return string.Join(
            "\n",
            text.Replace("\r", string.Empty)
                .Split('\n')
                .Where(line => !ContainsBlockedReference(line)));
    }

    private static IReadOnlyCollection<string> GetBlockedFreeTextAliases()
    {
        lock (AliasCacheGate)
        {
            return blockedFreeTextAliasCache ??= BlockedFreeTextNpcNames
                    .Concat(BlockedTextAliases)
                    .Concat(RuntimeBlockedAliases)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
    }

    private static IReadOnlyCollection<string> GetBlockedDialogueAliases()
    {
        lock (AliasCacheGate)
        {
            return blockedDialogueAliasCache ??= BlockedNpcNames
                    .Concat(BlockedTextAliases)
                    .Concat(RuntimeBlockedAliases)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }
    }

    private static void AddLocalizedRuntimeAlias(string? internalName, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || string.Equals(internalName?.Trim(), displayName.Trim(), StringComparison.OrdinalIgnoreCase)
            || displayName.All(character => character <= 0x7f))
        {
            return;
        }

        // Source-aware checks already reject the internal ID. Only cache genuinely localized
        // aliases here; English display labels like "The Ridge" are too generic for free-text
        // removal and can damage unrelated dialogue.
        AddRuntimeAlias(displayName);
    }

    private static void AddRuntimeAlias(string? alias)
    {
        if (!string.IsNullOrWhiteSpace(alias))
        {
            if (RuntimeBlockedAliases.Add(alias.Trim()))
            {
                blockedFreeTextAliasCache = null;
                blockedDialogueAliasCache = null;
            }
        }
    }

    private static bool ContainsIdentity(string text, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        bool asciiWord = alias.All(character => character <= 0x7f && (char.IsLetterOrDigit(character) || character == '_'));
        int start = 0;
        while ((start = text.IndexOf(alias, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (!asciiWord
                || ((start == 0 || !IsAsciiWordCharacter(text[start - 1]))
                    && (start + alias.Length == text.Length || !IsAsciiWordCharacter(text[start + alias.Length]))))
            {
                return true;
            }

            start += alias.Length;
        }

        return false;
    }

    private static bool ContainsDialogueKeyIdentity(string text, string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return false;
        }

        int start = 0;
        while ((start = text.IndexOf(alias, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + alias.Length;
            if ((start == 0 || IsDialogueKeyBoundary(text[start - 1]))
                && (end == text.Length || IsDialogueKeyBoundary(text[end])))
            {
                return true;
            }

            start = end;
        }

        return false;
    }

    private static bool ContainsStructuredRsvDialogueIdentifier(string text)
    {
        int start = 0;
        while ((start = text.IndexOf("RSV", start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if ((start == 0 || IsDialogueKeyBoundary(text[start - 1]))
                && IsStructuredRsvIdentifier(text[start..]))
            {
                return true;
            }

            start += 3;
        }

        return false;
    }

    private static bool IsStructuredRsvIdentifier(string value)
    {
        if (!value.StartsWith("RSV", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Keep the ordinary English acronym "RSVP" available when it is a complete key token.
        // RSV itself has identifiers such as RSVProtest, so a letter/digit continuing after the P
        // still denotes an RSV structural name.
        return value.Length == 3
            || char.ToUpperInvariant(value[3]) != 'P'
            || (value.Length > 4 && char.IsLetterOrDigit(value[4]));
    }

    private static bool IsAsciiWordCharacter(char character)
        => character <= 0x7f && (char.IsLetterOrDigit(character) || character == '_');

    private static bool IsDialogueKeyBoundary(char character)
        => !char.IsLetterOrDigit(character);
}
