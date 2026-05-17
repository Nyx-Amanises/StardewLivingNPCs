using System;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class CommunityReactionStyle
{
    public static CommunityReactionCue For(NPC npc)
    {
        NpcDispositionProfile disposition = NpcDisposition.For(npc);
        string label = disposition.PromptLabel.ToLowerInvariant();

        if (ContainsAny(label, "blunt", "sharp", "guarded", "pessimistic"))
        {
            return new CommunityReactionCue(
                "Blunt",
                "frame community news plainly, with a little skepticism or dry directness",
                "直接一点，略带审视",
                35,
                "usually does not embellish much, but may repeat a point if it seems relevant"
            );
        }

        if (ContainsAny(label, "shy", "cautious", "reserved"))
        {
            return new CommunityReactionCue(
                "Reserved",
                "mention community news softly or indirectly, as if testing whether it is welcome",
                "含蓄、试探",
                18,
                "is slow to pass along news and often keeps uncertain impressions to themself"
            );
        }

        if (ContainsAny(label, "warm", "gentle", "caring", "approachable"))
        {
            return new CommunityReactionCue(
                "Warm",
                "turn community news into a warm check-in rather than idle gossip",
                "温和关心",
                42,
                "may share a concern with close people, but tends not to make it sharper than it was"
            );
        }

        if (ContainsAny(label, "curious", "thoughtful", "scholarly", "observant"))
        {
            return new CommunityReactionCue(
                "Curious",
                "frame community news as a curious observation or thoughtful question",
                "好奇、会追问",
                58,
                "is more likely to pass along an interesting observation, especially inside familiar circles"
            );
        }

        if (disposition.EmoteModifier >= 0.03)
        {
            return new CommunityReactionCue(
                "Expressive",
                "react to community news with visible feeling before settling back into character",
                "情绪更外露",
                64,
                "is fairly likely to repeat fresh news, and their retelling may pick up a little color"
            );
        }

        if (disposition.ApproachModifier <= -0.03)
        {
            return new CommunityReactionCue(
                "Measured",
                "keep community news measured and brief, without leaning into gossip",
                "克制、简短",
                24,
                "rarely carries news very far unless it directly concerns someone close"
            );
        }

        return new CommunityReactionCue(
            "Balanced",
            "mention community news naturally and without exaggeration",
            "自然、不夸张",
            38,
            "may pass along ordinary public news, but usually without much embellishment"
        );
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record CommunityReactionCue(
    string Key,
    string PromptLabel,
    string DebugLabel,
    int SharePropensity,
    string RetellingPromptLabel
);
