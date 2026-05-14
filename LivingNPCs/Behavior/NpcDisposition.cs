using System;
using System.Collections.Generic;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class NpcDisposition
{
    private static readonly NpcDispositionProfile Warm = new(
        "warm and approachable",
        "温和、容易回应",
        0.06,
        0.02,
        20,
        "warm temperament"
    );

    private static readonly NpcDispositionProfile Reserved = new(
        "reserved and slow to approach",
        "谨慎、慢热",
        -0.08,
        -0.02,
        16,
        "reserved temperament"
    );

    private static readonly NpcDispositionProfile Expressive = new(
        "expressive and emotionally visible",
        "外露、反应明显",
        0.02,
        0.08,
        32,
        "expressive temperament"
    );

    private static readonly NpcDispositionProfile Curious = new(
        "curious and observant",
        "好奇、爱观察",
        0.04,
        0.05,
        8,
        "curious temperament"
    );

    private static readonly Dictionary<string, NpcDispositionProfile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Abigail"] = Curious,
        ["Alex"] = new("confident and direct", "自信、直接", 0.07, 0.03, 32, "confident temperament"),
        ["Caroline"] = Warm,
        ["Clint"] = Reserved,
        ["Demetrius"] = Curious,
        ["Dwarf"] = Curious,
        ["Elliott"] = Expressive,
        ["Emily"] = Expressive,
        ["Evelyn"] = Warm,
        ["George"] = Reserved,
        ["Gus"] = Warm,
        ["Haley"] = new("selective but expressive", "挑剔、反应明显", -0.03, 0.06, 16, "selective temperament"),
        ["Harvey"] = Reserved,
        ["Jas"] = Reserved,
        ["Jodi"] = Warm,
        ["Kent"] = Reserved,
        ["Krobus"] = Reserved,
        ["Leah"] = Warm,
        ["Lewis"] = new("formal and socially aware", "正式、重视礼节", 0.02, 0.02, 16, "formal temperament"),
        ["Linus"] = Reserved,
        ["Marnie"] = Warm,
        ["Maru"] = Curious,
        ["Pam"] = new("blunt and reactive", "直率、反应快", 0.02, 0.05, 32, "blunt temperament"),
        ["Penny"] = Reserved,
        ["Pierre"] = new("social but businesslike", "健谈、偏事务", 0.04, 0.01, 16, "businesslike temperament"),
        ["Robin"] = new("practical and friendly", "务实、友好", 0.06, 0.02, 20, "practical temperament"),
        ["Sam"] = Expressive,
        ["Sandy"] = Expressive,
        ["Sebastian"] = Reserved,
        ["Shane"] = Reserved,
        ["Vincent"] = Expressive,
        ["Willy"] = Warm,
        ["Wizard"] = Reserved
    };

    public static NpcDispositionProfile For(NPC npc)
    {
        if (Profiles.TryGetValue(npc.Name, out var profile))
        {
            return profile;
        }

        if (!string.IsNullOrWhiteSpace(npc.displayName) && Profiles.TryGetValue(npc.displayName, out profile))
        {
            return profile;
        }

        return FallbackFor(npc.Name);
    }

    private static NpcDispositionProfile FallbackFor(string npcName)
    {
        return StableBucket(npcName) switch
        {
            0 => Warm,
            1 => Reserved,
            2 => Expressive,
            _ => Curious
        };
    }

    private static int StableBucket(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return (int)(hash % 4);
        }
    }
}

internal sealed record NpcDispositionProfile(
    string PromptLabel,
    string DebugLabel,
    double ApproachModifier,
    double EmoteModifier,
    int PassiveEmoteId,
    string Reason
);
