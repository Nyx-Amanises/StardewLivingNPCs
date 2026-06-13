using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal delegate bool TryResolveNpcScheduleTargetHandler(
    NPC npc,
    out GameLocation? location,
    out Point targetTile,
    out int facingDirection
);

internal sealed class CompanionOutingRuntime
{
    private const int OutingSpeechCooldownMilliseconds = 5500;
    private const float ArrivalRemarkDistanceTiles = 5f;

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;
    private readonly CanUseWorldActionHandler canUseWorldAction;
    private readonly CompanionOutingAnchorSelector anchorSelector;
    private readonly TryResolveNpcScheduleTargetHandler tryResolveScheduleTarget;
    private readonly Func<NPC, bool, bool> returnNpcToSchedule;
    private readonly Action<NPC, string> refreshPromptContext;
    private readonly List<PendingCompanionOuting> pendingOutings = new();

    public CompanionOutingRuntime(
        ModConfig config,
        IMonitor monitor,
        BehaviorMemory memory,
        BehaviorFeedbackService feedback,
        CommunityRippleRuntime communityRipples,
        CanUseWorldActionHandler canUseWorldAction,
        Func<GameLocation, Point, NPC?, bool> isSafeDestinationTile,
        TryResolveNpcScheduleTargetHandler tryResolveScheduleTarget,
        Func<NPC, bool, bool> returnNpcToSchedule,
        Action<NPC, string> refreshPromptContext)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.feedback = feedback;
        this.communityRipples = communityRipples;
        this.canUseWorldAction = canUseWorldAction;
        this.anchorSelector = new CompanionOutingAnchorSelector(isSafeDestinationTile);
        this.tryResolveScheduleTarget = tryResolveScheduleTarget;
        this.returnNpcToSchedule = returnNpcToSchedule;
        this.refreshPromptContext = refreshPromptContext;
    }

    public void Clear()
    {
        this.pendingOutings.Clear();
    }

    public bool TryStart(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiCompanionOutings)
        {
            reason = "companion outings are disabled";
            return false;
        }

        if (!this.canUseWorldAction(npc, "companion_outing", requireFriendly: false, out reason, allowDuringEvents: false, allowDistantWhenExplicit: true))
        {
            return false;
        }

        if (IsProtectedScene(npc, out reason))
        {
            return false;
        }

        string targetLocation = BehaviorMemory.NormalizeTravelLocation(action.TargetLocation, string.Empty);
        if (string.IsNullOrWhiteSpace(targetLocation) || !TravelLocationRules.IsKnownPublicOutingTarget(targetLocation))
        {
            reason = "a supported outing destination is required";
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            reason = "there is no NPC state yet";
            return false;
        }

        if (state.LastAiWalkTogetherTotalDays == Game1.Date.TotalDays)
        {
            reason = "a companion outing was already used today";
            return false;
        }

        int minimumStayMinutes = Math.Max(
            CompanionOutingRules.MinimumStayMinutes,
            this.config.MinimumCompanionOutingStayMinutes
        );
        if (!CompanionOutingRules.CanFitMinimumStay(Game1.timeOfDay, minimumStayMinutes))
        {
            reason = "there is not enough time left today for a two-hour outing";
            return false;
        }

        GameLocation? destination = ResolveLocation(targetLocation);
        if (destination == null)
        {
            reason = $"outing destination {targetLocation} is unavailable";
            return false;
        }

        string sourceLocation = BehaviorMemory.NormalizeTravelLocation(npc.currentLocation?.Name ?? string.Empty, string.Empty);
        string activityStyle = CompanionOutingRules.DetermineActivityStyle(targetLocation, action.Reason);
        var reservedTiles = this.pendingOutings
            .Where(outing => string.Equals(outing.TargetLocation, targetLocation, StringComparison.OrdinalIgnoreCase))
            .Select(outing => outing.AnchorTile)
            .ToHashSet();
        if (!this.anchorSelector.TrySelect(
                npc,
                destination,
                targetLocation,
                sourceLocation,
                activityStyle,
                Game1.Date.TotalDays,
                reservedTiles,
                out CompanionOutingAnchor? anchor)
            || anchor == null)
        {
            reason = $"no suitable public standing place was found in {targetLocation}";
            return false;
        }

        this.StopForNpc(npc, returnToSchedule: false);
        this.feedback.ClearAmbientRemarksForNpc(npc.Name);
        bool originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        bool originalFollowSchedule = npc.followSchedule;
        NpcTravelRuntime.SuppressSchedule(npc);

        var outing = new PendingCompanionOuting(
            npc.Name,
            Game1.Date.TotalDays,
            targetLocation,
            TravelLocationRules.GetChineseLabel(targetLocation),
            activityStyle,
            anchor.Tile,
            anchor.FacingDirection,
            anchor.SemanticLabel,
            originalIgnoreScheduleToday,
            originalFollowSchedule
        );
        this.pendingOutings.Add(outing);

        var unavailableAnchorTiles = new HashSet<Point>(reservedTiles);
        bool routeAssigned = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (this.TryAssignRoute(npc, outing, destination, outing.AnchorTile, outing.AnchorFacingDirection))
            {
                routeAssigned = true;
                break;
            }

            unavailableAnchorTiles.Add(outing.AnchorTile);
            if (!this.anchorSelector.TrySelect(
                    npc,
                    destination,
                    targetLocation,
                    sourceLocation,
                    activityStyle,
                    Game1.Date.TotalDays + attempt + 1,
                    unavailableAnchorTiles,
                    out CompanionOutingAnchor? replacement)
                || replacement == null)
            {
                break;
            }

            outing.AnchorTile = replacement.Tile;
            outing.AnchorFacingDirection = replacement.FacingDirection;
            outing.AnchorLabel = replacement.SemanticLabel;
        }

        if (!routeAssigned)
        {
            this.pendingOutings.Remove(outing);
            NpcTravelRuntime.RestoreSchedule(npc, originalIgnoreScheduleToday, originalFollowSchedule);
            state.LastAiWalkTogetherTotalDays = -1;
            reason = "the game's schedule pathfinder could not build a natural route";
            return false;
        }

        state.LastAiWalkTogetherTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "StartedCompanionOuting",
            BuildWorldActionReason(
                action.Reason,
                $"they agreed to spend time with the farmer at {outing.TargetLocationLabel}"
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(
            state,
            $"they agreed to spend time with the farmer at {outing.TargetLocationLabel}"
        );

        this.ShowOutingRemark(npc, BuildStartRemark(outing.TargetLocation, outing.ActivityStyle));
        if (IsAtTile(npc, destination, outing.AnchorTile))
        {
            this.BeginStay(npc, outing);
        }
        else
        {
            this.refreshPromptContext(npc, $"Started companion outing for {npc.Name}.");
        }

        return true;
    }

    public bool HasActiveOuting(NPC npc)
    {
        return this.pendingOutings.Any(outing => outing.NpcName == npc.Name);
    }

    public void TryUpdatePending()
    {
        if (this.pendingOutings.Count == 0)
        {
            return;
        }

        foreach (var outing in this.pendingOutings.ToList())
        {
            try
            {
                NPC? npc = Game1.getCharacterFromName(outing.NpcName);
                if (outing.TotalDays != Game1.Date.TotalDays || npc == null)
                {
                    this.pendingOutings.Remove(outing);
                    continue;
                }

                if (Game1.eventUp || Game1.currentLocation?.currentEvent != null)
                {
                    this.Stop(outing, npc, returnToSchedule: true);
                    continue;
                }

                if (Game1.activeClickableMenu != null)
                {
                    continue;
                }

                NpcTravelRuntime.SuppressSchedule(npc);
                switch (outing.Phase)
                {
                    case CompanionOutingPhase.Traveling:
                        this.UpdateTraveling(npc, outing);
                        break;
                    case CompanionOutingPhase.AtDestination:
                        this.UpdateStay(npc, outing);
                        break;
                    case CompanionOutingPhase.Returning:
                        this.UpdateReturning(npc, outing);
                        break;
                }
            }
            catch (Exception ex)
            {
                this.pendingOutings.Remove(outing);
                this.monitor.Log(
                    $"Companion outing for {outing.NpcName} hit an error and was cancelled to keep the NPC safe: {ex.Message}",
                    LogLevel.Warn
                );
                this.TryRecoverNpcAfterOutingFailure(outing);
            }
        }
    }

    private void TryRecoverNpcAfterOutingFailure(PendingCompanionOuting outing)
    {
        try
        {
            NPC? npc = Game1.getCharacterFromName(outing.NpcName);
            if (npc == null)
            {
                return;
            }

            npc.controller = null;
            npc.DirectionsToNewLocation = null;
            npc.Halt();
            NpcTravelRuntime.RestoreSchedule(npc, outing.OriginalIgnoreScheduleToday, outing.OriginalFollowSchedule);
            this.returnNpcToSchedule(npc, npc.currentLocation != Game1.currentLocation);
        }
        catch
        {
            // best-effort recovery; the outing has already been removed from tracking
        }
    }

    public void StopForNpc(NPC npc, bool returnToSchedule)
    {
        foreach (var outing in this.pendingOutings.Where(outing => outing.NpcName == npc.Name).ToList())
        {
            this.Stop(outing, npc, returnToSchedule);
        }
    }

    public string BuildPromptContext(NPC npc)
    {
        var outing = this.pendingOutings.FirstOrDefault(candidate => candidate.NpcName == npc.Name);
        if (outing == null)
        {
            return string.Empty;
        }

        bool farmerPresent = outing.Phase == CompanionOutingPhase.AtDestination
            ? IsPlayerAtLocation(outing.TargetLocation)
            : npc.currentLocation == Game1.currentLocation;
        var prompt = new StringBuilder();
        prompt.AppendLine("## Active Companion Outing");
        prompt.AppendLine($"- {npc.displayName} and the farmer have an active shared outing to {outing.TargetLocationLabel}.");
        prompt.AppendLine($"- Phase: {FormatPhase(outing.Phase)}.");
        prompt.AppendLine($"- Shared activity: {CompanionOutingRules.GetActivityPromptLabel(outing.ActivityStyle)}.");
        prompt.AppendLine($"- The farmer is {(farmerPresent ? "currently present with the NPC" : "temporarily elsewhere")}.");
        if (outing.Phase == CompanionOutingPhase.AtDestination)
        {
            prompt.AppendLine($"- They arrived at {BehaviorTimeMath.FormatTime(outing.ArrivalTimeOfDay)} and the NPC plans to remain until at least {BehaviorTimeMath.FormatTime(outing.StayUntilTimeOfDay)}.");
            prompt.AppendLine($"- The NPC is settled {outing.AnchorLabel}.");
            prompt.AppendLine($"- Shared time together at the destination so far: about {outing.SharedMinutesAtDestination} game minutes.");
        }
        else if (outing.Phase == CompanionOutingPhase.Traveling)
        {
            prompt.AppendLine("- The NPC is walking there through normal doors and map exits; this is not a teleport or an escort task.");
        }
        else
        {
            prompt.AppendLine("- The planned stay has ended and the NPC is naturally returning to the day's schedule.");
        }

        prompt.AppendLine("- Let the place, time, weather, nearby people, and shared company shape the reply naturally.");
        prompt.AppendLine("- Do not announce travel status, give route instructions, repeat the agreement, or describe game mechanics.");
        return prompt.ToString().TrimEnd();
    }

    private void UpdateTraveling(NPC npc, PendingCompanionOuting outing)
    {
        GameLocation? destination = ResolveLocation(outing.TargetLocation);
        if (destination == null)
        {
            this.Stop(outing, npc, returnToSchedule: true);
            return;
        }

        this.TryShowArrivalRemarkNearAnchor(npc, outing, destination);
        if (IsAtTile(npc, destination, outing.AnchorTile))
        {
            this.BeginStay(npc, outing);
            return;
        }

        if (npc.controller != null && npc.controller == outing.LastAssignedController)
        {
            return;
        }

        if (outing.RouteRetryCount < 2
            && this.TryAssignRoute(npc, outing, destination, outing.AnchorTile, outing.AnchorFacingDirection))
        {
            outing.RouteRetryCount++;
            return;
        }

        if (outing.AnchorRelocationCount < 2
            && this.TryReplaceAnchor(npc, outing, destination)
            && this.TryAssignRoute(npc, outing, destination, outing.AnchorTile, outing.AnchorFacingDirection))
        {
            outing.RouteRetryCount = 0;
            return;
        }

        if (npc.currentLocation != Game1.currentLocation)
        {
            NpcTravelRuntime.PlaceInLocation(
                npc,
                destination,
                outing.AnchorTile,
                outing.AnchorFacingDirection
            );
            this.BeginStay(npc, outing);
            return;
        }

        this.Stop(outing, npc, returnToSchedule: true);
    }

    private void BeginStay(NPC npc, PendingCompanionOuting outing)
    {
        npc.controller = null;
        npc.DirectionsToNewLocation = null;
        npc.Halt();
        npc.faceDirection(outing.AnchorFacingDirection);
        outing.Phase = CompanionOutingPhase.AtDestination;
        outing.ArrivalTimeOfDay = Game1.timeOfDay;
        outing.StayUntilTimeOfDay = BehaviorTimeMath.AddMinutesToTime(
            Game1.timeOfDay,
            Math.Max(CompanionOutingRules.MinimumStayMinutes, this.config.MinimumCompanionOutingStayMinutes)
        );
        outing.LastObservedTimeOfDay = Game1.timeOfDay;
        outing.RouteRetryCount = 0;

        var state = this.memory.GetState(npc);
        if (state != null)
        {
            MarkStateAfterWorldAction(
                state,
                $"they arrived for shared time with the farmer at {outing.TargetLocationLabel}"
            );
        }

        this.PlanSettledEmote(outing, state);
        this.TryShowArrivalRemark(npc, outing);
        this.refreshPromptContext(npc, $"Companion outing reached {outing.TargetLocation} for {npc.Name}.");
    }

    private void UpdateStay(NPC npc, PendingCompanionOuting outing)
    {
        this.AccumulateSharedTime(outing);
        GameLocation? destination = ResolveLocation(outing.TargetLocation);
        if (destination == null)
        {
            this.Stop(outing, npc, returnToSchedule: true);
            return;
        }

        if (Game1.timeOfDay >= outing.StayUntilTimeOfDay)
        {
            this.BeginReturn(npc, outing);
            return;
        }

        if (npc.currentLocation != destination)
        {
            if (!this.TryAssignRoute(npc, outing, destination, outing.AnchorTile, outing.AnchorFacingDirection))
            {
                this.Stop(outing, npc, returnToSchedule: true);
            }

            return;
        }

        bool occupiedByAnotherNpc = destination.characters.Any(candidate =>
            candidate != npc && candidate.TilePoint == outing.AnchorTile);
        if (occupiedByAnotherNpc && outing.AnchorRelocationCount < 1)
        {
            this.TryReplaceAnchor(npc, outing, destination);
        }

        if (Vector2.Distance(npc.Tile, new Vector2(outing.AnchorTile.X, outing.AnchorTile.Y)) > 0.75f)
        {
            if (npc.controller == null || npc.controller != outing.LastAssignedController)
            {
                this.TryAssignRoute(npc, outing, destination, outing.AnchorTile, outing.AnchorFacingDirection);
            }

            return;
        }

        npc.controller = null;
        npc.DirectionsToNewLocation = null;
        npc.Halt();
        npc.faceDirection(outing.AnchorFacingDirection);
        this.TryShowSettledEmote(npc, outing, destination);
    }

    private void AccumulateSharedTime(PendingCompanionOuting outing)
    {
        if (outing.LastObservedTimeOfDay == Game1.timeOfDay)
        {
            return;
        }

        int elapsedMinutes = BehaviorTimeMath.GetElapsedMinutes(
            outing.LastObservedTimeOfDay,
            Game1.timeOfDay
        );
        if (IsPlayerAtLocation(outing.TargetLocation))
        {
            outing.SharedMinutesAtDestination += elapsedMinutes;
        }

        outing.LastObservedTimeOfDay = Game1.timeOfDay;
    }

    private void BeginReturn(NPC npc, PendingCompanionOuting outing)
    {
        this.AccumulateSharedTime(outing);
        this.RecordCompletedStay(npc, outing);
        outing.Phase = CompanionOutingPhase.Returning;
        outing.RouteRetryCount = 0;
        this.TryShowReturnRemark(npc, outing);

        if (!this.tryResolveScheduleTarget(
                npc,
                out GameLocation? destination,
                out Point targetTile,
                out int facingDirection)
            || destination == null)
        {
            this.FinishReturn(npc, outing, useFallback: false);
            return;
        }

        outing.ReturnLocationName = destination.Name;
        outing.ReturnTile = targetTile;
        outing.ReturnFacingDirection = facingDirection is >= 0 and <= 3 ? facingDirection : 2;
        if (IsAtTile(npc, destination, targetTile))
        {
            this.FinishReturn(npc, outing, useFallback: false);
            return;
        }

        if (!this.TryAssignRoute(npc, outing, destination, targetTile, outing.ReturnFacingDirection))
        {
            this.FinishReturn(npc, outing, useFallback: true);
            return;
        }

        this.refreshPromptContext(npc, $"Companion outing began returning {npc.Name} to schedule.");
    }

    private void UpdateReturning(NPC npc, PendingCompanionOuting outing)
    {
        GameLocation? destination = ResolveLocation(outing.ReturnLocationName);
        if (destination == null)
        {
            this.FinishReturn(npc, outing, useFallback: true);
            return;
        }

        if (IsAtTile(npc, destination, outing.ReturnTile))
        {
            this.FinishReturn(npc, outing, useFallback: false);
            return;
        }

        if (npc.controller != null && npc.controller == outing.LastAssignedController)
        {
            return;
        }

        if (outing.RouteRetryCount < 2
            && this.TryAssignRoute(npc, outing, destination, outing.ReturnTile, outing.ReturnFacingDirection))
        {
            outing.RouteRetryCount++;
            return;
        }

        this.FinishReturn(npc, outing, useFallback: true);
    }

    private void TryShowArrivalRemarkNearAnchor(
        NPC npc,
        PendingCompanionOuting outing,
        GameLocation destination)
    {
        if (outing.ArrivalRemarkShown || npc.currentLocation != destination)
        {
            return;
        }

        float distanceToAnchor = Vector2.Distance(
            npc.Tile,
            new Vector2(outing.AnchorTile.X, outing.AnchorTile.Y)
        );
        if (distanceToAnchor <= ArrivalRemarkDistanceTiles)
        {
            this.TryShowArrivalRemark(npc, outing);
        }
    }

    private void TryShowArrivalRemark(NPC npc, PendingCompanionOuting outing)
    {
        if (outing.ArrivalRemarkShown)
        {
            return;
        }

        if (this.ShowOutingRemark(npc, BuildArrivalRemark(outing.TargetLocation, outing.ActivityStyle)))
        {
            outing.ArrivalRemarkShown = true;
        }
    }

    private void TryShowReturnRemark(NPC npc, PendingCompanionOuting outing)
    {
        if (outing.ReturnRemarkShown)
        {
            return;
        }

        if (this.ShowOutingRemark(npc, BuildReturnRemark(outing.TargetLocation, outing.ActivityStyle)))
        {
            outing.ReturnRemarkShown = true;
        }
    }

    private void PlanSettledEmote(PendingCompanionOuting outing, LivingNpcState? state)
    {
        outing.SettledEmoteNotBeforeTimeOfDay = BehaviorTimeMath.AddMinutesToTime(
            Game1.timeOfDay,
            CompanionOutingRules.SettledEmoteDelayMinutes
        );
        if (state == null)
        {
            outing.SettledEmoteId = 0;
            return;
        }

        bool warmRelationship = state.Familiarity >= 45
            || state.RelationshipTrust >= 55
            || state.LastFriendshipHearts >= 4
            || state.Mood is "Warm" or "Comfortable" or "Delighted" or "Pleased";
        bool emotionallyComfortable = state.HighestUnresolvedConflictSeverity < 25
            && state.CurrentEmotion is not ("Jealous" or "Disappointed" or "Uneasy" or "Upset" or "Angry" or "Sad")
            && state.Mood is not ("Guarded" or "Upset" or "Overloaded" or "Awkward");
        outing.SettledEmoteId = CompanionOutingRules.SelectSettledEmoteId(
            outing.NpcName,
            outing.TargetLocation,
            outing.ActivityStyle,
            outing.TotalDays,
            warmRelationship,
            emotionallyComfortable
        );
    }

    private void TryShowSettledEmote(
        NPC npc,
        PendingCompanionOuting outing,
        GameLocation destination)
    {
        if (outing.SettledEmoteShown
            || outing.SettledEmoteId <= 0
            || !outing.ArrivalRemarkShown
            || !this.config.AllowEmotes
            || Game1.timeOfDay < outing.SettledEmoteNotBeforeTimeOfDay
            || Game1.activeClickableMenu != null
            || Game1.eventUp
            || Game1.player == null
            || Game1.currentLocation != destination
            || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 2)
        {
            return;
        }

        npc.doEmote(outing.SettledEmoteId);
        outing.SettledEmoteShown = true;
    }

    private bool ShowOutingRemark(NPC npc, string text)
    {
        return this.feedback.TryShowNpcSpeechBubble(npc, text, OutingSpeechCooldownMilliseconds);
    }

    private bool TryAssignRoute(
        NPC npc,
        PendingCompanionOuting outing,
        GameLocation destination,
        Point targetTile,
        int facingDirection)
    {
        bool assigned = NpcTravelRuntime.TryAssignVanillaScheduleRoute(
            npc,
            destination,
            targetTile,
            facingDirection,
            out PathFindController? controller
        );
        outing.LastAssignedController = controller;
        if (!assigned && this.config.Debug)
        {
            this.monitor.Log(
                $"Could not build vanilla-style companion outing route for {npc.Name}: {npc.currentLocation?.Name} -> {destination.Name} ({targetTile.X}, {targetTile.Y}).",
                LogLevel.Debug
            );
        }

        return assigned;
    }

    private bool TryReplaceAnchor(
        NPC npc,
        PendingCompanionOuting outing,
        GameLocation destination)
    {
        var reservedTiles = this.pendingOutings
            .Where(candidate => candidate != outing
                && string.Equals(candidate.TargetLocation, outing.TargetLocation, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.AnchorTile)
            .Append(outing.AnchorTile)
            .ToHashSet();
        string sourceLocation = BehaviorMemory.NormalizeTravelLocation(
            npc.currentLocation?.Name ?? destination.Name,
            string.Empty
        );
        if (!this.anchorSelector.TrySelect(
                npc,
                destination,
                outing.TargetLocation,
                sourceLocation,
                outing.ActivityStyle,
                outing.TotalDays + outing.AnchorRelocationCount + 1,
                reservedTiles,
                out CompanionOutingAnchor? replacement)
            || replacement == null)
        {
            return false;
        }

        outing.AnchorTile = replacement.Tile;
        outing.AnchorFacingDirection = replacement.FacingDirection;
        outing.AnchorLabel = replacement.SemanticLabel;
        outing.AnchorRelocationCount++;
        return true;
    }

    private void RecordCompletedStay(NPC npc, PendingCompanionOuting outing)
    {
        if (outing.SharedExperienceRecorded)
        {
            return;
        }

        outing.SharedExperienceRecorded = true;
        var state = this.memory.GetState(npc);
        bool sharedEnough = outing.SharedMinutesAtDestination >= CompanionOutingRules.MinimumSharedMinutesForMemory;
        string result = sharedEnough
            ? $"they spent a long shared outing with the farmer at {outing.TargetLocationLabel}"
            : $"they stayed at {outing.TargetLocationLabel}, but the farmer was only briefly present";
        this.memory.RecordNpcWorldAction(
            npc,
            sharedEnough ? "CompletedCompanionOuting" : "CompanionOutingWithoutSharedTime",
            result,
            this.config.MaxMemoryEntriesPerNpc
        );
        if (state != null)
        {
            MarkStateAfterWorldAction(state, result);
        }

        if (!sharedEnough || state == null)
        {
            return;
        }

        string key = $"companion_outing:{outing.TargetLocation.ToLowerInvariant()}";
        string summary = $"the farmer and {npc.displayName} spent time together at {outing.TargetLocationLabel}, {CompanionOutingRules.GetActivityPromptLabel(outing.ActivityStyle)}";
        var existing = state.SharedExperiences.FirstOrDefault(experience => experience.Key == key);
        if (existing != null)
        {
            existing.Summary = summary;
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            existing.Importance = Math.Min(100, existing.Importance + 6);
            existing.FollowUpEligibleTotalDays = Game1.Date.TotalDays + 1;
            existing.FollowUpShownTotalDays = -1;
        }
        else
        {
            state.SharedExperiences.Add(new SharedExperienceFact
            {
                Key = key,
                Type = "companion_outing",
                Summary = summary,
                LocationName = outing.TargetLocation,
                LocationLabel = outing.TargetLocationLabel,
                CreatedTotalDays = Game1.Date.TotalDays,
                CreatedTimeOfDay = Game1.timeOfDay,
                LastUpdatedTotalDays = Game1.Date.TotalDays,
                LastUpdatedTimeOfDay = Game1.timeOfDay,
                Importance = 78,
                TimesReinforced = 1,
                FollowUpEligibleTotalDays = Game1.Date.TotalDays + 1
            });
            state.SharedExperiences = state.SharedExperiences
                .OrderByDescending(experience => experience.Importance)
                .ThenByDescending(experience => experience.LastUpdatedTotalDays)
                .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
                .Take(12)
                .ToList();
        }

        this.communityRipples.Spread(
            npc,
            "shared_experience",
            $"the farmer spent a long outing with {npc.displayName} at {outing.TargetLocationLabel}",
            importance: 58
        );
    }

    private void FinishReturn(NPC npc, PendingCompanionOuting outing, bool useFallback)
    {
        npc.controller = null;
        npc.DirectionsToNewLocation = null;
        npc.Halt();
        NpcTravelRuntime.RestoreSchedule(
            npc,
            outing.OriginalIgnoreScheduleToday,
            outing.OriginalFollowSchedule
        );
        if (useFallback)
        {
            this.returnNpcToSchedule(npc, npc.currentLocation != Game1.currentLocation);
        }

        this.pendingOutings.Remove(outing);
        this.refreshPromptContext(npc, $"Completed companion outing lifecycle for {npc.Name}.");
    }

    private void Stop(PendingCompanionOuting outing, NPC npc, bool returnToSchedule)
    {
        npc.controller = null;
        npc.DirectionsToNewLocation = null;
        npc.Halt();
        NpcTravelRuntime.RestoreSchedule(
            npc,
            outing.OriginalIgnoreScheduleToday,
            outing.OriginalFollowSchedule
        );
        if (returnToSchedule)
        {
            this.returnNpcToSchedule(npc, npc.currentLocation != Game1.currentLocation);
        }

        this.pendingOutings.Remove(outing);
        this.refreshPromptContext(npc, $"Stopped companion outing for {npc.Name}.");
    }

    private static bool IsAtTile(NPC npc, GameLocation destination, Point tile)
    {
        return npc.currentLocation == destination
            && Vector2.Distance(npc.Tile, new Vector2(tile.X, tile.Y)) <= 0.75f;
    }

    private static bool IsPlayerAtLocation(string targetLocation)
    {
        string current = BehaviorMemory.NormalizeTravelLocation(Game1.currentLocation?.Name ?? string.Empty, string.Empty);
        return string.Equals(current, targetLocation, StringComparison.OrdinalIgnoreCase);
    }

    private static GameLocation? ResolveLocation(string locationName)
    {
        try
        {
            string normalized = BehaviorMemory.NormalizeTravelLocation(locationName, locationName);
            if (normalized == "Farm")
            {
                return Game1.getFarm();
            }

            GameLocation? location = Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(normalized);
            return location ?? (normalized == "Trailer" ? Game1.getLocationFromName("Trailer_Big") : null);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProtectedScene(NPC npc, out string reason)
    {
        if (Game1.eventUp || Game1.currentLocation?.currentEvent != null)
        {
            reason = "companion outings are blocked during events or festivals";
            return true;
        }

        if (npc.IsInvisible || npc.isSleeping.Value)
        {
            reason = "companion outings are blocked while the NPC cannot naturally interact";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static string BuildStartRemark(string targetLocation, string activityStyle)
    {
        return targetLocation switch
        {
            "Beach" or "Custom_GrampletonCoast" => "走吧，去海边透透气。",
            "SeedShop" => "走吧，去店里看看。",
            "ArchaeologyHouse" => "走吧，去里面慢慢看看。",
            "Saloon" => "走吧，去酒吧坐一会儿。",
            "Mountain" => "走吧，去山边看看。",
            "Forest" or "Custom_ForestWest" => "走吧，去林子里走走。",
            "Farm" => "走吧，去农场待一会儿。",
            "FlowerDance" => "走吧，去花田边看看。",
            _ when activityStyle == "scenic" => "走吧，去看看那边的风景。",
            _ when activityStyle == "browse" => "走吧，去逛一会儿。",
            _ when activityStyle == "quiet" => "走吧，找个安静点的地方。",
            _ => "走吧。"
        };
    }

    private static string BuildArrivalRemark(string targetLocation, string activityStyle)
    {
        string timePrefix = GetSceneTimePrefix(Game1.timeOfDay);
        return targetLocation switch
        {
            "Beach" or "Custom_GrampletonCoast" => $"{timePrefix}海边挺舒服的。",
            "SeedShop" => "这会儿店里正好可以慢慢看看。",
            "ArchaeologyHouse" => "这里安静下来以后，很适合慢慢看。",
            "Saloon" => "这里的热闹声听着还挺安心。",
            "Mountain" => $"{timePrefix}山边空气很好。",
            "Forest" or "Custom_ForestWest" => $"{timePrefix}林子里很安静。",
            "Farm" => $"{timePrefix}农场看起来很舒服。",
            "BusStop" => "这里风吹过来的时候，还挺适合停一下。",
            "Town" => "镇上的这个时候，刚好不算太吵。",
            "Hospital" => "这里还是安静一点比较好。",
            "FlowerDance" => $"{timePrefix}花田边真好看。",
            "Custom_Ridgeside_RidgeFalls" => $"{timePrefix}瀑布声听起来很清楚。",
            "Custom_Ridgeside_Ridge" or "Custom_Ridgeside_RSVCliff" => $"{timePrefix}这边的视野真开阔。",
            _ when activityStyle == "scenic" => $"{timePrefix}这里的景色不错。",
            _ when activityStyle == "browse" => "这里可以慢慢看看。",
            _ when activityStyle == "quiet" => "这里安静得刚刚好。",
            _ => "这里还挺适合待一会儿。"
        };
    }

    private static string BuildReturnRemark(string targetLocation, string activityStyle)
    {
        string opening = GetReturnOpening(Game1.timeOfDay);
        return targetLocation switch
        {
            "Beach" or "Custom_GrampletonCoast" => $"{opening}海风也吹够啦。",
            "SeedShop" => $"{opening}东西也看得差不多了。",
            "ArchaeologyHouse" => $"{opening}书和展品下次再慢慢看。",
            "Saloon" => $"{opening}再坐下去就该忘了时间了。",
            "Mountain" => $"{opening}山边待久了也该回去了。",
            "Forest" or "Custom_ForestWest" => $"{opening}林子里开始有点静了。",
            "Farm" => $"{opening}农场这边待得很舒服。",
            "FlowerDance" => $"{opening}花田边的风也该留给别人了。",
            "Custom_Ridgeside_RidgeFalls" => $"{opening}瀑布声听久了，反而有点舍不得走。",
            _ when activityStyle == "scenic" => $"{opening}风景已经看得很满足了。",
            _ when activityStyle == "browse" => $"{opening}这边也逛得差不多了。",
            _ when activityStyle == "quiet" => $"{opening}这段安静时间刚刚好。",
            _ => $"{opening}我们回去吧。"
        };
    }

    private static string GetSceneTimePrefix(int timeOfDay)
    {
        return timeOfDay switch
        {
            < 900 => "清晨的",
            < 1200 => "上午的",
            < 1700 => "午后的",
            < 2000 => "傍晚的",
            _ => "夜里的"
        };
    }

    private static string GetReturnOpening(int timeOfDay)
    {
        return timeOfDay switch
        {
            < 1200 => "差不多该回去了，",
            < 1800 => "时间差不多了，",
            < 2200 => "天色不早了，",
            _ => "夜深了，"
        };
    }

    private static string FormatPhase(CompanionOutingPhase phase)
    {
        return phase switch
        {
            CompanionOutingPhase.Traveling => "traveling naturally toward the destination",
            CompanionOutingPhase.AtDestination => "spending time together at the destination",
            _ => "returning to the normal daily schedule"
        };
    }

    private static string BuildWorldActionReason(string requestedReason, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedReason)
            ? fallback
            : $"{requestedReason.Trim()}; {fallback}";
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
