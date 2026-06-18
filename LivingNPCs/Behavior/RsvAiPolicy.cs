using System;
using System.Collections.Generic;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class RsvAiPolicy
{
    private static readonly HashSet<string> BlockedNpcNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Acorn", "Aguar", "Alissa", "Althea", "Anton", "Ariah", "Belinda", "Bert", "Blair", "Bliss", "Bryle", "Carmen",
        "Corine", "Daia", "Ezekiel", "Faye", "Flor", "Freddie", "Helen", "Ian", "Irene", "Jeric", "Jio", "June", "Keahi",
        "Kenneth", "Kiarra", "Kimpoi", "Kiwi", "Lenny", "Lola", "Lorenzo", "Lorraine", "Louie", "Maddie", "Maive",
        "Malaya", "Nadaline", "Naomi", "Olga", "Paula", "Philip", "Pika", "Pipo", "Raeriyala", "RelicSpirit", "Richard",
        "Sari", "Sean", "Shanice", "Shiro", "Sonny", "Torts", "TreehouseGirl", "Trinnie", "Undreya", "Ysabelle", "Yuuma",
        "Zachary", "Zayne"
    };

    internal static bool IsBlockedNpc(NPC? npc)
    {
        return npc != null
            && (IsBlockedNpcName(npc.Name)
                || IsBlockedNpcName(npc.displayName)
                || IsRidgesideLocationName(npc.currentLocation?.Name));
    }

    internal static bool IsBlockedNpcName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string normalized = name.Trim().TrimEnd('·', '•', '-');
        return BlockedNpcNames.Contains(normalized);
    }

    private static bool IsRidgesideLocationName(string? locationName)
    {
        return !string.IsNullOrWhiteSpace(locationName)
            && locationName.StartsWith("Custom_Ridgeside_", StringComparison.OrdinalIgnoreCase);
    }
}
