using StardewValley;

namespace LivingNPCs.Behavior;

internal delegate bool CanUseWorldActionHandler(
    NPC npc,
    string actionName,
    bool requireFriendly,
    out string reason,
    bool allowDuringEvents,
    bool allowDistantWhenExplicit
);
