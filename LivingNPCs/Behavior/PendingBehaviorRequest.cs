using System.Threading.Tasks;

namespace LivingNPCs.Behavior;

internal sealed record PendingBehaviorRequest(
    string NpcName,
    BehaviorTrigger Trigger,
    string Source,
    int TotalDays,
    BehaviorIntent FallbackIntent,
    Task<BehaviorIntent?> Task
);
