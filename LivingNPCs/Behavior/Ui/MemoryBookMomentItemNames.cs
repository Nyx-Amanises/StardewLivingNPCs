using System;
using System.Collections.Generic;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace LivingNPCs.Behavior.Ui;

/// <summary>Resolves item labels for the book without changing stored memories or creating items.</summary>
internal static class MemoryBookMomentItemNames
{
    internal static string Resolve(string itemId, string storedLabel)
    {
        return Resolve(itemId, storedLabel, ItemRegistry.GetData, EnumerateItems);
    }

    internal static string Resolve(
        string itemId,
        string storedLabel,
        Func<string, ParsedItemData?> getData,
        Func<IEnumerable<ParsedItemData?>> enumerateItems)
    {
        string fallback = storedLabel ?? string.Empty;
        try
        {
            // A stored ID is authoritative. A removed item must not turn into another item
            // which happens to share its old display name.
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                ParsedItemData? data = getData(itemId.Trim());
                return IsUsable(data) ? data!.DisplayName : fallback;
            }

            if (string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            // Older gift records only have a name. Accept a complete, unique match; never
            // strip quality/flavor words or guess from a substring of a different item.
            string name = fallback.Trim();
            ParsedItemData? match = null;
            foreach (ParsedItemData? data in enumerateItems())
            {
                if (!IsUsable(data))
                {
                    continue;
                }

                ParsedItemData candidate = data!;
                if (!string.Equals(name, candidate.InternalName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(name, candidate.DisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match != null
                    && !string.Equals(match.QualifiedItemId, candidate.QualifiedItemId, StringComparison.Ordinal))
                {
                    return fallback;
                }

                match = candidate;
            }

            return match?.DisplayName ?? fallback;
        }
        catch
        {
            // The game/content manager may be unavailable, or a removed mod's item data
            // may fail to load. A read-only book should still open with its saved label.
            return fallback;
        }
    }

    private static bool IsUsable(ParsedItemData? data)
    {
        return data != null && !data.IsErrorItem && !string.IsNullOrWhiteSpace(data.DisplayName);
    }

    private static IEnumerable<ParsedItemData?> EnumerateItems()
    {
        foreach (IItemDataDefinition definition in ItemRegistry.ItemTypes)
        {
            foreach (string id in definition.GetAllIds())
            {
                yield return ItemRegistry.GetData(definition.Identifier + id);
            }
        }
    }
}
