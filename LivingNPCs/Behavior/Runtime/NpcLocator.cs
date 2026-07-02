using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// NPC lookup used by the behavior pipeline: nearest budget-eligible candidate for triggered
/// behaviors, by-name lookup in the current location, and cursor/facing-based lookup for
/// conversation starts. All lookups exclude policy-blocked NPCs.
/// </summary>
internal sealed class NpcLocator
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;

    public NpcLocator(ModConfig config, BehaviorMemory memory)
    {
        this.config = config;
        this.memory = memory;
    }

    public bool TryFindNearestNpc(out NPC? nearest)
    {
        nearest = null;
        if (Game1.currentLocation == null || Game1.player == null)
        {
            return false;
        }

        float maxDistance = this.config.MaxInteractionDistanceTiles;
        nearest = Game1.currentLocation.characters
            .Where(this.IsCandidateNpc)
            .Select(npc => new
            {
                Npc = npc,
                Distance = Vector2.Distance(npc.Tile, Game1.player.Tile)
            })
            .Where(pair => pair.Distance <= maxDistance)
            .OrderBy(pair => pair.Distance)
            .Select(pair => pair.Npc)
            .FirstOrDefault();

        return nearest != null;
    }

    private bool IsCandidateNpc(NPC npc)
    {
        return npc.currentLocation == Game1.currentLocation
            && !string.IsNullOrWhiteSpace(npc.Name)
            && !RsvAiPolicy.IsBlockedNpc(npc)
            && this.memory.HasDailyBudget(npc, this.config.MaxBehaviorsPerNpcPerDay);
    }

    public bool TryFindNpcInCurrentLocation(string npcName, out NPC? npc)
    {
        npc = Game1.currentLocation?.characters.FirstOrDefault(candidate => candidate.Name == npcName);
        if (RsvAiPolicy.IsBlockedNpc(npc))
        {
            npc = null;
            return false;
        }

        return npc != null;
    }

    public bool TryFindNpcForInteraction(ICursorPosition cursor, out NPC? npc)
    {
        npc = null;
        if (Game1.currentLocation == null || Game1.player == null)
        {
            return false;
        }

        var facingTile = BehaviorActionExecutor.GetPlayerFacingTile();
        var targetTiles = new[]
        {
            cursor.GrabTile,
            new Vector2(facingTile.X, facingTile.Y)
        };

        const float maxConversationDistanceToPlayer = 2.5f;
        npc = Game1.currentLocation.characters
            .Where(candidate =>
                candidate.currentLocation == Game1.currentLocation
                && !string.IsNullOrWhiteSpace(candidate.Name)
                && !RsvAiPolicy.IsBlockedNpc(candidate)
                && !candidate.IsInvisible
                && !candidate.isSleeping.Value
            )
            .Select(candidate => new
            {
                Npc = candidate,
                DistanceToTarget = targetTiles.Min(tile => Vector2.Distance(candidate.Tile, tile)),
                DistanceToPlayer = Vector2.Distance(candidate.Tile, Game1.player.Tile)
            })
            .Where(pair => pair.DistanceToTarget <= 1.5f && pair.DistanceToPlayer <= maxConversationDistanceToPlayer)
            .OrderBy(pair => pair.DistanceToTarget)
            .ThenBy(pair => pair.DistanceToPlayer)
            .Select(pair => pair.Npc)
            .FirstOrDefault();

        return npc != null;
    }
}
