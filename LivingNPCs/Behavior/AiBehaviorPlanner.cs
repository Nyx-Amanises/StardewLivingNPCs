using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class AiBehaviorPlanner : IBehaviorPlanner
{
    private readonly IBehaviorPlanner fallbackPlanner;

    public AiBehaviorPlanner(IBehaviorPlanner fallbackPlanner)
    {
        this.fallbackPlanner = fallbackPlanner;
    }

    public BehaviorIntent ChooseIntent(NPC npc, BehaviorTrigger trigger)
    {
        // Future hook: ask an LLM for a constrained behavior intent, then validate it before execution.
        return this.fallbackPlanner.ChooseIntent(npc, trigger);
    }
}
