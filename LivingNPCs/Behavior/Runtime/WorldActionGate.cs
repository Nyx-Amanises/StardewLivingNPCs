using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// The single gate every AI-initiated world action must pass: policy exclusions, the global
/// config switch, scene validity, and the relationship/conflict thresholds. Runtimes receive
/// <see cref="CanUseWorldAction"/> as their <see cref="CanUseWorldActionHandler"/>.
/// </summary>
internal sealed class WorldActionGate
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;

    public WorldActionGate(ModConfig config, BehaviorMemory memory)
    {
        this.config = config;
        this.memory = memory;
    }

    public bool CanUseWorldAction(
        NPC npc,
        string actionName,
        bool requireFriendly,
        out string reason,
        bool allowDuringEvents = false,
        bool allowDistantWhenExplicit = false
    )
    {
        reason = string.Empty;
        if (RsvAiPolicy.IsBlockedNpc(npc))
        {
            reason = "RSV NPCs are excluded from AI world actions";
            return false;
        }

        if (!this.config.EnableAiWorldActions)
        {
            reason = "AI world actions are disabled";
            return false;
        }

        if ((!allowDuringEvents && Game1.eventUp) || Game1.currentLocation == null || npc.currentLocation != Game1.currentLocation)
        {
            reason = "world action cannot run in the current scene";
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            reason = "there is no NPC state yet";
            return false;
        }

        bool atLeastFamiliar = state.InteractionComfortTier is "Familiar" or "Friendly" or "Trusted" or "Intimate";
        bool atLeastFriendly = state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate";
        if (state.HighestUnresolvedConflictSeverity >= 30)
        {
            reason = $"{actionName} is blocked by unresolved conflict";
            return false;
        }

        if (!allowDistantWhenExplicit && (requireFriendly ? !atLeastFriendly : !atLeastFamiliar))
        {
            reason = $"{actionName} requires a closer relationship";
            return false;
        }

        return true;
    }
}
