namespace LivingNPCs.Behavior;

internal enum BehaviorTrigger
{
    Manual,
    Passive
}

internal enum BehaviorIntentType
{
    FacePlayer,
    Emote,
    ApproachPlayer
}

internal sealed record BehaviorIntent(
    BehaviorIntentType Type,
    string NpcName,
    string Reason,
    int EmoteId = 16
);
