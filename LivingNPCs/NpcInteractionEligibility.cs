using StardewValley;

namespace LivingNPCs;

/// <summary>Shared eligibility rule for player-to-NPC conversation interactions.</summary>
internal static class NpcInteractionEligibility
{
    /// <summary>
    /// Keep ordinary and content-pack villagers eligible while excluding pets, horses, monsters,
    /// farm animals, and other character subclasses which may also appear in a location's
    /// <see cref="GameLocation.characters"/> collection.
    /// </summary>
    public static bool IsEligible(NPC? npc)
    {
        return npc?.IsVillager == true;
    }
}
