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

internal enum CompanionOutingPhase
{
    Traveling,
    TravelingToFarmBoundary,
    TravelingFromFarmBoundary,
    AtDestination,
    Returning,
    ReturningToFarmBoundary,
    ReturningFromFarmBoundary,

    /// <summary>Walking to the boarding spot of a vehicle destination (bus/boat/theater/spa door).</summary>
    TravelingToVehicleGateway
}

internal sealed class PendingCompanionOuting
{
    public PendingCompanionOuting(
        string npcName,
        int totalDays,
        string targetLocation,
        string targetLocationLabel,
        string activityStyle,
        string reason,
        int plannedStayMinutes,
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
        this.Reason = reason;
        this.PlannedStayMinutes = plannedStayMinutes;
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
    public string Reason { get; }
    public int PlannedStayMinutes { get; }
    public bool IsShortVisit => this.PlannedStayMinutes < CompanionOutingRules.MinimumStayMinutes;
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
    public int SettledEmoteId { get; set; }
    public int SettledEmoteNotBeforeTimeOfDay { get; set; }
    public bool SettledEmoteShown { get; set; }
    public bool SharedExperienceRecorded { get; set; }
    /// <summary>
    /// Whether an event interrupted this outing. Event scripts can replace or invalidate an
    /// NPC's path controller, so the route must be rebuilt once normal gameplay resumes.
    /// </summary>
    public bool WasPausedForEvent { get; set; }
    public PathFindController? LastAssignedController { get; set; }
    public string ReturnLocationName { get; set; } = string.Empty;
    public Point ReturnTile { get; set; } = Point.Zero;
    public int ReturnFacingDirection { get; set; } = 2;
    public string FarmBoundaryLocationName { get; set; } = string.Empty;
    public Point FarmBoundarySourceTile { get; set; } = Point.Zero;
    public Point FarmBoundaryTargetTile { get; set; } = Point.Zero;

    // ---- Vehicle-gateway leg (Desert / IslandSouth / MovieTheater / BathHouse_Entry) ----
    public string VehicleKind { get; set; } = string.Empty;
    public string VehicleGatewayLocationName { get; set; } = string.Empty;
    public Point VehicleBoardingTile { get; set; } = Point.Zero;
    public int VehicleBoardingFacingDirection { get; set; } = 2;
    public bool VehicleBoardingRemarkShown { get; set; }

    // ---- Side-by-side stroll bookkeeping (stay phase) ----
    public Point LastFarmerTile { get; set; } = new Point(int.MinValue, int.MinValue);
    public int LastFarmerMoveTick { get; set; } = int.MinValue;
    public int LastStrollRepathTick { get; set; } = int.MinValue;
    public Point StrollTargetTile { get; set; } = Point.Zero;

    // ---- Small-talk bubble bookkeeping ----
    public int NextChatTimeOfDay { get; set; }
    public int ChatRemarksShown { get; set; }
    public int LastChatVariant { get; set; }
}
