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

internal sealed class PendingEscortToLocation
{
    public PendingEscortToLocation(
        string npcName,
        int totalDays,
        string targetLocation,
        string targetLocationLabel,
        int endTimeOfDay,
        string lastLocationName,
        Point lastPlayerTile,
        bool originalIgnoreScheduleToday,
        bool originalFollowSchedule
    )
    {
        this.NpcName = npcName;
        this.TotalDays = totalDays;
        this.TargetLocation = targetLocation;
        this.TargetLocationLabel = targetLocationLabel;
        this.EndTimeOfDay = endTimeOfDay;
        this.LastLocationName = lastLocationName;
        this.LastPlayerTile = lastPlayerTile;
        this.OriginalIgnoreScheduleToday = originalIgnoreScheduleToday;
        this.OriginalFollowSchedule = originalFollowSchedule;
    }

    public string NpcName { get; }
    public int TotalDays { get; }
    public string TargetLocation { get; }
    public string TargetLocationLabel { get; }
    public int EndTimeOfDay { get; }
    public string LastLocationName { get; set; }
    public Point LastPlayerTile { get; set; }
    public bool OriginalIgnoreScheduleToday { get; }
    public bool OriginalFollowSchedule { get; }
    public Point LastWaypointTile { get; set; } = Point.Zero;
    public PathFindController? LastAssignedController { get; set; }
    public bool HintShownForLocation { get; set; }
    public bool WaitingInNextLocation { get; set; }
    public string WaitingLocationName { get; set; } = string.Empty;
    public string WaitingSourceLocationName { get; set; } = string.Empty;
}

internal sealed class PendingWalkTogether
{
    public PendingWalkTogether(
        string npcName,
        int totalDays,
        string locationName,
        int endTimeOfDay,
        Point lastPlayerTile,
        PathFindController? lastAssignedController,
        bool originalIgnoreScheduleToday,
        bool originalFollowSchedule
    )
    {
        this.NpcName = npcName;
        this.TotalDays = totalDays;
        this.LocationName = locationName;
        this.EndTimeOfDay = endTimeOfDay;
        this.LastPlayerTile = lastPlayerTile;
        this.LastAssignedController = lastAssignedController;
        this.OriginalIgnoreScheduleToday = originalIgnoreScheduleToday;
        this.OriginalFollowSchedule = originalFollowSchedule;
    }

    public string NpcName { get; }
    public int TotalDays { get; }
    public string LocationName { get; }
    public int EndTimeOfDay { get; }
    public Point LastPlayerTile { get; set; }
    public PathFindController? LastAssignedController { get; set; }
    public bool OriginalIgnoreScheduleToday { get; }
    public bool OriginalFollowSchedule { get; }
}
