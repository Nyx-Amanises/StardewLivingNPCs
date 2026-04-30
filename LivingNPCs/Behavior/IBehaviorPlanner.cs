using StardewValley;

namespace LivingNPCs.Behavior;

internal interface IBehaviorPlanner
{
    BehaviorIntent ChooseIntent(NPC npc, BehaviorTrigger trigger);
}
