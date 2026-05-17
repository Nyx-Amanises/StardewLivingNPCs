using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Behavior;

internal static class NpcSocialGraph
{
    private static readonly IReadOnlyList<StableSocialCircle> StableCircles =
    [
        new StableSocialCircle(
            "saloon_regulars",
            "酒吧常客圈",
            [
                "Gus",
                "Pam",
                "Shane",
                "Clint",
                "Emily",
                "Willy",
                "Lewis"
            ],
            AllowsPersonalNews: false
        ),
        new StableSocialCircle(
            "young_adults",
            "年轻人圈",
            [
                "Abigail",
                "Sam",
                "Sebastian",
                "Alex",
                "Haley",
                "Emily",
                "Leah",
                "Maru",
                "Penny"
            ],
            AllowsPersonalNews: false
        )
    ];

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

    public static IReadOnlyCollection<StableSocialConnection> GetStablePropagationTargets(string npcName, string visibility)
    {
        var targets = new Dictionary<string, StableSocialConnection>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in GetCloseConnections(npcName))
        {
            targets[name] = new StableSocialConnection(name, "close_connections", "亲近关系", true);
        }

        if (!string.Equals(visibility, "Public", StringComparison.OrdinalIgnoreCase))
        {
            return targets.Values.ToList();
        }

        foreach (var circle in StableCircles.Where(circle => circle.Members.Contains(npcName, StringComparer.OrdinalIgnoreCase)))
        {
            foreach (string member in circle.Members)
            {
                if (string.Equals(member, npcName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                targets.TryAdd(
                    member,
                    new StableSocialConnection(member, circle.Key, circle.DebugLabel, circle.AllowsPersonalNews)
                );
            }
        }

        return targets.Values.ToList();
    }

    public static IReadOnlyCollection<string> GetStableCircleLabels(string npcName)
    {
        var labels = StableCircles
            .Where(circle => circle.Members.Contains(npcName, StringComparer.OrdinalIgnoreCase))
            .Select(circle => circle.DebugLabel)
            .ToList();

        if (GetCloseConnections(npcName).Count > 0)
        {
            labels.Insert(0, "亲近关系");
        }

        return labels;
    }
}

internal sealed record StableSocialConnection(
    string NpcName,
    string CircleKey,
    string CircleLabel,
    bool AllowsPersonalNews
);

internal sealed record StableSocialCircle(
    string Key,
    string DebugLabel,
    IReadOnlyCollection<string> Members,
    bool AllowsPersonalNews
);
