using Microsoft.Xna.Framework;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal sealed record GiftTasteLabels(string DebugLabel, string PromptLabel);
internal sealed record PendingAmbientRemark(
    string NpcName,
    string Text,
    int TotalDays,
    int NotBeforeTimeOfDay,
    string LocationName,
    Vector2 OriginTile
);

internal sealed record PendingDelayedTravelAction(
    string NpcName,
    int TotalDays,
    string LocationName,
    int NotBeforeTimeOfDay,
    string Type,
    string TargetLocation,
    int DurationMinutes,
    string Reason
);

internal enum CompanionOutingPhase
{
    Traveling,
    AtDestination,
    Returning
}

internal sealed class PendingCompanionOuting
{
    public PendingCompanionOuting(
        string npcName,
        int totalDays,
        string targetLocation,
        string targetLocationLabel,
        string activityStyle,
        Point anchorTile,
        int anchorFacingDirection,
        string anchorLabel,
        bool originalIgnoreScheduleToday,
        bool originalFollowSchedule
    )
    {
        this.NpcName = npcName;
        this.TotalDays = totalDays;
        this.TargetLocation = targetLocation;
        this.TargetLocationLabel = targetLocationLabel;
        this.ActivityStyle = activityStyle;
        this.AnchorTile = anchorTile;
        this.AnchorFacingDirection = anchorFacingDirection;
        this.AnchorLabel = anchorLabel;
        this.OriginalIgnoreScheduleToday = originalIgnoreScheduleToday;
        this.OriginalFollowSchedule = originalFollowSchedule;
    }

    public string NpcName { get; }
    public int TotalDays { get; }
    public string TargetLocation { get; }
    public string TargetLocationLabel { get; }
    public string ActivityStyle { get; }
    public Point AnchorTile { get; set; }
    public int AnchorFacingDirection { get; set; }
    public string AnchorLabel { get; set; }
    public bool OriginalIgnoreScheduleToday { get; }
    public bool OriginalFollowSchedule { get; }
    public CompanionOutingPhase Phase { get; set; } = CompanionOutingPhase.Traveling;
    public int ArrivalTimeOfDay { get; set; }
    public int StayUntilTimeOfDay { get; set; }
    public int LastObservedTimeOfDay { get; set; }
    public int SharedMinutesAtDestination { get; set; }
    public int RouteRetryCount { get; set; }
    public int AnchorRelocationCount { get; set; }
    public bool ArrivalRemarkShown { get; set; }
    public bool ReturnRemarkShown { get; set; }
    public bool SharedExperienceRecorded { get; set; }
    public PathFindController? LastAssignedController { get; set; }
    public string ReturnLocationName { get; set; } = string.Empty;
    public Point ReturnTile { get; set; } = Point.Zero;
    public int ReturnFacingDirection { get; set; } = 2;
}
