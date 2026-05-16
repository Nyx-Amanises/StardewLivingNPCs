using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Behavior;

internal static class NpcSocialGraph
{
    public static IReadOnlyCollection<string> GetCloseConnections(string npcName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(npcName) || Game1.characterData == null)
        {
            return names;
        }

        if (Game1.characterData.TryGetValue(npcName, out CharacterData? data)
            && data.FriendsAndFamily is { Count: > 0 })
        {
            foreach (string name in data.FriendsAndFamily.Keys)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }

        foreach (var pair in Game1.characterData)
        {
            if (pair.Value.FriendsAndFamily?.ContainsKey(npcName) == true)
            {
                names.Add(pair.Key);
            }
        }

        names.Remove(npcName);
        return names;
    }

    public static bool AreCloseConnections(string firstNpcName, string secondNpcName)
    {
        if (string.IsNullOrWhiteSpace(firstNpcName)
            || string.IsNullOrWhiteSpace(secondNpcName)
            || string.Equals(firstNpcName, secondNpcName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GetCloseConnections(firstNpcName).Contains(secondNpcName, StringComparer.OrdinalIgnoreCase)
            || GetCloseConnections(secondNpcName).Contains(firstNpcName, StringComparer.OrdinalIgnoreCase);
    }
}
