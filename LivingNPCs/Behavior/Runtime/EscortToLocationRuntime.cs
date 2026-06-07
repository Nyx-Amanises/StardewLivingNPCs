using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace LivingNPCs.Behavior;

internal delegate bool TryFindNpcTileHandler(NPC npc, out Point targetTile);

internal delegate bool TryFindOpenTileNearHandler(GameLocation location, Point center, NPC ignoredNpc, out Point targetTile);

internal sealed class EscortToLocationRuntime
{
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;
    private readonly CanUseWorldActionHandler canUseWorldAction;
    private readonly Action<NPC, bool> stopTravelActionsForNpc;
    private readonly TryFindNpcTileHandler findApproachTile;
    private readonly TryFindOpenTileNearHandler findOpenTileNear;
    private readonly Func<GameLocation, Point, NPC?, bool> isSafeDestinationTile;
    private readonly Func<NPC, bool> facePlayer;
    private readonly Func<Point, int> getDirectionTowardPlayerFromTile;
    private readonly Func<NPC, bool, bool> returnNpcToSchedule;
    private readonly List<PendingEscortToLocation> pendingEscorts = new();

    public EscortToLocationRuntime(
        ModConfig config,
        IMonitor monitor,
        BehaviorMemory memory,
        BehaviorFeedbackService feedback,
        CommunityRippleRuntime communityRipples,
        CanUseWorldActionHandler canUseWorldAction,
        Action<NPC, bool> stopTravelActionsForNpc,
        TryFindNpcTileHandler findApproachTile,
        TryFindOpenTileNearHandler findOpenTileNear,
        Func<GameLocation, Point, NPC?, bool> isSafeDestinationTile,
        Func<NPC, bool> facePlayer,
        Func<Point, int> getDirectionTowardPlayerFromTile,
        Func<NPC, bool, bool> returnNpcToSchedule)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.feedback = feedback;
        this.communityRipples = communityRipples;
        this.canUseWorldAction = canUseWorldAction;
        this.stopTravelActionsForNpc = stopTravelActionsForNpc;
        this.findApproachTile = findApproachTile;
        this.findOpenTileNear = findOpenTileNear;
        this.isSafeDestinationTile = isSafeDestinationTile;
        this.facePlayer = facePlayer;
        this.getDirectionTowardPlayerFromTile = getDirectionTowardPlayerFromTile;
        this.returnNpcToSchedule = returnNpcToSchedule;
    }

    public void Clear()
    {
        this.pendingEscorts.Clear();
    }

    public bool TryStart(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.config.AllowAiEscortToLocation)
        {
            reason = "escort to location is disabled";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.TargetLocation))
        {
            reason = "escort target location is missing";
            return false;
        }

        string targetLocation = BehaviorMemory.NormalizeTravelLocation(action.TargetLocation, string.Empty);

        if (!this.canUseWorldAction(npc, "escort_to_location", requireFriendly: false, out reason, allowDuringEvents: false, allowDistantWhenExplicit: true))
        {
            return false;
        }

        if (IsProtectedEscortScene(npc, out reason))
        {
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
            reason = "walk or escort action was already used today";
            return false;
        }

        if (!IsKnownEscortTarget(targetLocation))
        {
            reason = $"escort target {targetLocation} is not supported yet";
            return false;
        }

        int durationMinutes = Math.Clamp(
            action.DurationMinutes <= 0 ? this.config.MaxAiWalkTogetherMinutes : action.DurationMinutes,
            10,
            Math.Max(10, this.config.MaxAiWalkTogetherMinutes)
        );
        string targetLabel = GetEscortTargetLabel(targetLocation);

        this.stopTravelActionsForNpc(npc, true);
        this.feedback.ClearAmbientRemarksForNpc(npc.Name);
        bool originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        bool originalFollowSchedule = npc.followSchedule;
        NpcTravelRuntime.SuppressSchedule(npc);
        npc.controller = null;
        npc.Halt();
        this.pendingEscorts.Add(new PendingEscortToLocation(
            npc.Name,
            Game1.Date.TotalDays,
            targetLocation,
            targetLabel,
            BehaviorTimeMath.AddMinutesToTime(Game1.timeOfDay, durationMinutes),
            Game1.currentLocation?.Name ?? string.Empty,
            Game1.player.TilePoint,
            originalIgnoreScheduleToday,
            originalFollowSchedule
        ));

        state.LastAiWalkTogetherTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "StartedEscortToLocation",
            BuildWorldActionReason(action.Reason, $"they agreed to guide the farmer toward {targetLabel}"),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, $"they agreed to guide the farmer toward {targetLabel}");

        if (IsEscortTargetReached(Game1.currentLocation, targetLocation))
        {
            this.Complete(npc, this.pendingEscorts.First(escort => escort.NpcName == npc.Name));
            return true;
        }

        this.feedback.TryShowNpcSpeechBubble(npc, BuildEscortStartGreeting(targetLocation));
        this.feedback.Show($"LivingNPCs：{npc.displayName} 会带你去{targetLabel}。");
        return true;
    }

    public void TryUpdatePending()
    {
        if (this.pendingEscorts.Count == 0)
        {
            return;
        }

        if (Game1.eventUp)
        {
            foreach (var escort in this.pendingEscorts.ToList())
            {
                this.Stop(escort, Game1.getCharacterFromName(escort.NpcName), returnToSchedule: true);
            }

            return;
        }

        foreach (var escort in this.pendingEscorts.ToList())
        {
            NPC? npc = Game1.getCharacterFromName(escort.NpcName);
            if (escort.TotalDays != Game1.Date.TotalDays
                || Game1.timeOfDay >= escort.EndTimeOfDay
                || npc == null
                || Game1.currentLocation == null
                || Game1.player == null)
            {
                this.Stop(escort, npc, returnToSchedule: true);
                continue;
            }

            if (Game1.activeClickableMenu != null)
            {
                continue;
            }

            NpcTravelRuntime.SuppressSchedule(npc);

            if (escort.WaitingInNextLocation)
            {
                if (npc.currentLocation != Game1.currentLocation)
                {
                    string npcLocation = BehaviorMemory.NormalizeTravelLocation(npc.currentLocation?.Name ?? string.Empty, string.Empty);
                    if (string.Equals(npcLocation, escort.WaitingLocationName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    escort.WaitingInNextLocation = false;
                    escort.WaitingLocationName = string.Empty;
                    escort.WaitingSourceLocationName = string.Empty;
                }
                else
                {
                    escort.WaitingInNextLocation = false;
                    escort.WaitingLocationName = string.Empty;
                    escort.WaitingSourceLocationName = string.Empty;
                    escort.LastLocationName = Game1.currentLocation.Name;
                    escort.HintShownForLocation = false;
                    escort.LastWaypointTile = Point.Zero;
                    escort.LastAssignedController = null;
                    if (!IsEscortTargetReached(Game1.currentLocation, escort.TargetLocation))
                    {
                        this.feedback.TryShowNpcSpeechBubble(npc, BuildEscortCaughtUpGreeting(escort));
                    }
                }
            }

            if (npc.currentLocation != Game1.currentLocation)
            {
                this.Stop(escort, npc, returnToSchedule: false);
                this.feedback.Show($"LivingNPCs：{npc.displayName} 没跟上，护送中断了。");
                continue;
            }
            else if (!Game1.currentLocation.characters.Contains(npc))
            {
                Game1.currentLocation.characters.Add(npc);
                npc.currentLocation = Game1.currentLocation;
            }

            if (!string.Equals(escort.LastLocationName, Game1.currentLocation.Name, StringComparison.OrdinalIgnoreCase))
            {
                escort.LastLocationName = Game1.currentLocation.Name;
                escort.HintShownForLocation = false;
                escort.LastWaypointTile = Point.Zero;
                escort.LastAssignedController = null;
            }

            if (IsEscortTargetReached(Game1.currentLocation, escort.TargetLocation))
            {
                this.Complete(npc, escort);
                continue;
            }

            if (Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles + 8)
            {
                if (!escort.HintShownForLocation)
                {
                    escort.HintShownForLocation = this.feedback.TryShowNpcSpeechBubble(npc, "你离得有点远了，先跟上我。");
                }
            }

            if (npc.controller != null && escort.LastAssignedController != null && npc.controller != escort.LastAssignedController)
            {
                npc.controller = null;
                npc.Halt();
            }

            if (this.TryFindEscortWaypointTile(npc, escort, out Point waypointTile, out string nextLocation))
            {
                float distanceToWaypoint = Vector2.Distance(npc.Tile, new Vector2(waypointTile.X, waypointTile.Y));
                if (distanceToWaypoint > 1.5f)
                {
                    if (escort.LastWaypointTile != waypointTile || npc.controller == null)
                    {
                        npc.controller = new PathFindController(
                            npc,
                            Game1.currentLocation,
                            waypointTile,
                            this.getDirectionTowardPlayerFromTile(waypointTile)
                        );
                        escort.LastWaypointTile = waypointTile;
                        escort.LastAssignedController = npc.controller;
                    }

                    if (!escort.HintShownForLocation)
                    {
                        escort.HintShownForLocation = this.feedback.TryShowNpcSpeechBubble(npc, BuildEscortDirectionHint(nextLocation));
                    }

                    continue;
                }

                npc.controller = null;
                npc.Halt();
                this.facePlayer(npc);
                if (IsPlayerCloseEnoughToFollowEscort(npc, waypointTile)
                    && this.TryAdvanceEscortNpcToNextLocation(npc, escort, nextLocation))
                {
                    continue;
                }

                if (!escort.HintShownForLocation)
                {
                    escort.HintShownForLocation = this.feedback.TryShowNpcSpeechBubble(npc, BuildEscortExitWaitHint(nextLocation));
                }

                continue;
            }

            escort.HintShownForLocation = true;
            this.TryKeepEscortNearPlayer(npc, escort);
        }
    }

    public void StopForNpc(NPC npc, bool returnToSchedule)
    {
        foreach (var escort in this.pendingEscorts.Where(escort => escort.NpcName == npc.Name).ToList())
        {
            this.Stop(escort, npc, returnToSchedule);
        }
    }

    private bool TryKeepEscortNearPlayer(NPC npc, PendingEscortToLocation escort)
    {
        if (Vector2.Distance(npc.Tile, Game1.player.Tile) <= 1.75f)
        {
            this.facePlayer(npc);
            return true;
        }

        if (escort.LastPlayerTile == Game1.player.TilePoint && npc.controller != null)
        {
            return true;
        }

        if (!this.findApproachTile(npc, out Point targetTile))
        {
            return false;
        }

        npc.controller = new PathFindController(
            npc,
            Game1.currentLocation,
            targetTile,
            this.getDirectionTowardPlayerFromTile(targetTile)
        );
        escort.LastPlayerTile = Game1.player.TilePoint;
        escort.LastAssignedController = npc.controller;
        return true;
    }

    private static bool IsPlayerCloseEnoughToFollowEscort(NPC npc, Point waypointTile)
    {
        var waypoint = new Vector2(waypointTile.X, waypointTile.Y);
        return Vector2.Distance(Game1.player.Tile, npc.Tile) <= 3f
            || Vector2.Distance(Game1.player.Tile, waypoint) <= 3.5f;
    }

    private bool TryAdvanceEscortNpcToNextLocation(NPC npc, PendingEscortToLocation escort, string nextLocation)
    {
        GameLocation? source = npc.currentLocation;
        GameLocation? destination = ResolveEscortLocation(nextLocation);
        if (source == null || destination == null)
        {
            return false;
        }

        string sourceName = BehaviorMemory.NormalizeTravelLocation(source.Name, source.Name);
        string destinationName = BehaviorMemory.NormalizeTravelLocation(destination.Name, destination.Name);
        if (string.IsNullOrWhiteSpace(destinationName))
        {
            return false;
        }

        if (!TryReadWarpDestinationTile(source, nextLocation, out Point targetTile)
            && !this.TryFindReverseWarpEntryTile(destination, sourceName, npc, out targetTile))
        {
            targetTile = GetLocationCenterTile(destination);
        }

        if (!this.isSafeDestinationTile(destination, targetTile, npc)
            && !this.findOpenTileNear(destination, targetTile, npc, out targetTile)
            && !this.findOpenTileNear(destination, GetLocationCenterTile(destination), npc, out targetTile))
        {
            return false;
        }

        try
        {
            npc.controller = null;
            npc.Halt();
            NpcTravelRuntime.PlaceInLocation(npc, destination, targetTile, 2);

            escort.WaitingInNextLocation = true;
            escort.WaitingLocationName = destinationName;
            escort.WaitingSourceLocationName = sourceName;
            escort.LastLocationName = destination.Name;
            escort.LastWaypointTile = Point.Zero;
            escort.LastAssignedController = null;
            escort.HintShownForLocation = false;
            return true;
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Could not advance {npc.Name} during LivingNPCs escort: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private bool TryFindEscortWaypointTile(NPC npc, PendingEscortToLocation escort, out Point waypointTile, out string nextLocation)
    {
        waypointTile = Point.Zero;
        nextLocation = string.Empty;
        if (Game1.currentLocation == null
            || !this.TryGetNextEscortLocation(Game1.currentLocation, escort.TargetLocation, out nextLocation))
        {
            return false;
        }

        if (this.TryFindWarpTileToward(Game1.currentLocation, nextLocation, npc, out waypointTile))
        {
            return true;
        }

        return false;
    }

    private static GameLocation? ResolveEscortLocation(string locationName)
    {
        try
        {
            return BehaviorMemory.NormalizeTravelLocation(locationName, locationName) == "Farm"
                ? Game1.getFarm()
                : Game1.getLocationFromName(locationName);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadWarpDestinationTile(GameLocation sourceLocation, string targetLocation, out Point targetTile)
    {
        foreach (var warp in sourceLocation.warps.Where(warp => string.Equals(
            BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
            targetLocation,
            StringComparison.OrdinalIgnoreCase)))
        {
            if (ScheduleReflectionReader.TryReadWarpTargetTile(warp, out targetTile))
            {
                return true;
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private bool TryFindReverseWarpEntryTile(GameLocation destination, string sourceLocationName, NPC npc, out Point targetTile)
    {
        foreach (var warp in destination.warps.Where(warp => string.Equals(
            BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
            sourceLocationName,
            StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var candidate in GetTilesAround(new Point(warp.X, warp.Y), 4))
            {
                if (this.isSafeDestinationTile(destination, candidate, npc))
                {
                    targetTile = candidate;
                    return true;
                }
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private static Point GetLocationCenterTile(GameLocation location)
    {
        try
        {
            var layer = location.map.Layers[0];
            return new Point(layer.LayerWidth / 2, layer.LayerHeight / 2);
        }
        catch
        {
            return new Point(10, 10);
        }
    }

    private bool TryGetNextEscortLocation(GameLocation currentLocation, string targetLocation, out string nextLocation)
    {
        nextLocation = string.Empty;
        string current = BehaviorMemory.NormalizeTravelLocation(currentLocation.Name, currentLocation.Name);
        if (string.Equals(current, targetLocation, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HasWarpToLocation(currentLocation, targetLocation))
        {
            nextLocation = targetLocation;
            return true;
        }

        string targetHub = GetEscortHubLocation(targetLocation);
        if (!string.Equals(current, targetHub, StringComparison.OrdinalIgnoreCase)
            && HasWarpToLocation(currentLocation, targetHub))
        {
            nextLocation = targetHub;
            return true;
        }

        nextLocation = current switch
        {
            "Farm" => targetLocation == "Farm" ? string.Empty : "BusStop",
            "BusStop" => targetLocation == "Farm" ? "Farm" : "Town",
            "Town" => GetTownEscortExit(targetLocation),
            "Mountain" => targetLocation == "Mine" ? "Mine" : "Town",
            "Mine" => "Mountain",
            "Beach" => "Town",
            "Forest" => targetLocation == "Farm" && HasWarpToLocation(currentLocation, "Farm") ? "Farm" : "Town",
            "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
                or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
                or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent" => "Town",
            _ => string.Empty
        };
        return !string.IsNullOrWhiteSpace(nextLocation);
    }

    private static string GetTownEscortExit(string targetLocation)
    {
        return targetLocation switch
        {
            "Farm" => "BusStop",
            "BusStop" => "BusStop",
            "Mountain" or "Mine" => "Mountain",
            "Beach" => "Beach",
            "Forest" => "Forest",
            "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
                or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
                or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent" => targetLocation,
            _ => string.Empty
        };
    }

    private static string GetEscortHubLocation(string targetLocation)
    {
        return targetLocation switch
        {
            "Mine" => "Mountain",
            "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
                or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
                or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent" => "Town",
            _ => targetLocation
        };
    }

    private static bool HasWarpToLocation(GameLocation location, string targetLocation)
    {
        return location.warps.Any(warp =>
            string.Equals(
                BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
                targetLocation,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private bool TryFindWarpTileToward(GameLocation location, string targetLocation, NPC npc, out Point targetTile)
    {
        foreach (var warp in location.warps
            .Where(warp => string.Equals(
                BehaviorMemory.NormalizeTravelLocation(ScheduleReflectionReader.GetWarpTargetName(warp), string.Empty),
                targetLocation,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(warp => Vector2.Distance(new Vector2(warp.X, warp.Y), npc.Tile)))
        {
            foreach (var candidate in GetTilesAround(new Point(warp.X, warp.Y), 3)
                .OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), npc.Tile)))
            {
                if (this.isSafeDestinationTile(location, candidate, npc))
                {
                    targetTile = candidate;
                    return true;
                }
            }
        }

        targetTile = Point.Zero;
        return false;
    }

    private bool TryMoveNpcNearPlayerForEscort(NPC npc, GameLocation location, out Point targetTile)
    {
        targetTile = Point.Zero;
        foreach (var candidate in GetTilesAround(Game1.player.TilePoint, 4)
            .Where(tile => tile != Game1.player.TilePoint)
            .OrderBy(tile => Vector2.Distance(new Vector2(tile.X, tile.Y), Game1.player.Tile)))
        {
            if (!this.isSafeDestinationTile(location, candidate, npc))
            {
                continue;
            }

            npc.controller = null;
            npc.Halt();
            npc.currentLocation?.characters.Remove(npc);
            if (!location.characters.Contains(npc))
            {
                location.characters.Add(npc);
            }

            npc.currentLocation = location;
            npc.Position = new Vector2(candidate.X * Game1.tileSize, candidate.Y * Game1.tileSize);
            npc.faceDirection(this.getDirectionTowardPlayerFromTile(candidate));
            targetTile = candidate;
            return true;
        }

        return false;
    }

    private static bool IsEscortTargetReached(GameLocation? location, string targetLocation)
    {
        if (location == null)
        {
            return false;
        }

        string current = BehaviorMemory.NormalizeTravelLocation(location.Name, location.Name);
        return string.Equals(current, targetLocation, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownEscortTarget(string targetLocation)
    {
        return targetLocation is "Farm" or "Town" or "Mountain" or "Mine" or "Beach" or "Forest" or "BusStop"
            or "Saloon" or "SeedShop" or "ArchaeologyHouse" or "Hospital" or "Trailer"
            or "JoshHouse" or "HaleyHouse" or "SamHouse" or "ScienceHouse" or "LeahHouse"
            or "AnimalShop" or "ElliottHouse" or "Blacksmith" or "FishShop" or "WizardHouse" or "Tent";
    }

    private void Complete(NPC npc, PendingEscortToLocation escort)
    {
        npc.controller = null;
        npc.Halt();
        this.feedback.TryShowNpcSpeechBubble(npc, BuildEscortArrivalGreeting(escort));
        var state = this.memory.GetState(npc);
        if (state != null)
        {
            this.memory.RecordNpcWorldAction(
                npc,
                "CompletedEscortToLocation",
                $"they guided the farmer to {escort.TargetLocationLabel}",
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(state, $"they guided the farmer to {escort.TargetLocationLabel}");
        }

        this.communityRipples.Spread(
            npc,
            "shared_experience",
            $"the farmer went with {npc.displayName} to {escort.TargetLocationLabel}",
            importance: 58
        );
        this.pendingEscorts.Remove(escort);
        this.feedback.Show($"LivingNPCs：{npc.displayName} 已带你到{escort.TargetLocationLabel}。");
        NpcTravelRuntime.RestoreSchedule(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
        this.returnNpcToSchedule(npc, false);
    }

    private void Stop(PendingEscortToLocation escort, NPC? npc, bool returnToSchedule)
    {
        if (npc != null && npc.controller == escort.LastAssignedController)
        {
            npc.controller = null;
            npc.Halt();
        }

        if (npc != null && returnToSchedule)
        {
            NpcTravelRuntime.RestoreSchedule(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
            this.returnNpcToSchedule(npc, true);
            var state = this.memory.GetState(npc);
            if (state != null && state.LastAiWalkTogetherTotalDays == Game1.Date.TotalDays)
            {
                state.LastAiWalkTogetherTotalDays = -1;
            }
        }
        else if (npc != null)
        {
            NpcTravelRuntime.RestoreSchedule(npc, escort.OriginalIgnoreScheduleToday, escort.OriginalFollowSchedule);
        }

        this.pendingEscorts.Remove(escort);
    }

    private static bool IsProtectedEscortScene(NPC npc, out string reason)
    {
        if (Game1.eventUp || Game1.currentLocation?.currentEvent != null)
        {
            reason = "escort is blocked during events or festivals";
            return true;
        }

        if (npc.IsInvisible || npc.isSleeping.Value)
        {
            reason = "escort is blocked while the NPC cannot naturally interact";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static string BuildEscortStartGreeting(string targetLocation)
    {
        return targetLocation switch
        {
            "Mine" => "我带你往矿井走，跟紧一点。",
            "Beach" => "我带你往海边走。",
            "ArchaeologyHouse" => "我带你去图书馆。",
            "Farm" => "我跟你去农场看看。",
            "Trailer" => "我带你去我家那边，跟上我。",
            _ => "我带路，你跟着我。"
        };
    }

    private static string BuildEscortCaughtUpGreeting(PendingEscortToLocation escort)
    {
        return $"好，你跟上来了，我们继续去{escort.TargetLocationLabel}。";
    }

    private static string BuildEscortDirectionHint(string nextLocation)
    {
        return nextLocation switch
        {
            "Mine" => "矿井入口就在前面。",
            "Mountain" => "先往山上走。",
            "Town" => "先回镇上。",
            "BusStop" => "先往巴士站那边走。",
            "Beach" => "从这边去海边。",
            "Forest" => "从这边去森林。",
            "Farm" => "从这边回农场。",
            "Trailer" => "我家就在镇上这边。",
            _ => "从这边走。"
        };
    }

    private static string BuildEscortExitWaitHint(string nextLocation)
    {
        return nextLocation switch
        {
            "Mine" => "我先去矿井入口那边等你。",
            "Mountain" => "我先到山路那边等你。",
            "Town" => "我先到镇上那边等你。",
            "BusStop" => "我先到巴士站那边等你。",
            "Beach" => "我先到海边那边等你。",
            "Forest" => "我先到森林那边等你。",
            "Farm" => "我先到农场那边等你。",
            "Trailer" => "我先到家门口那边等你。",
            _ => "我先过去等你。"
        };
    }

    private static string BuildEscortArrivalGreeting(PendingEscortToLocation escort)
    {
        return escort.TargetLocation switch
        {
            "Mine" => "到了，这里就是矿井。小心点。",
            "Beach" => "到了，海边就在这里。",
            "ArchaeologyHouse" => "到了，这里就是图书馆。",
            "Farm" => "到了，你的农场就在这里。",
            "Trailer" => "到了，这里就是我家。",
            _ => $"到了，就是{escort.TargetLocationLabel}。"
        };
    }

    private static string GetEscortTargetLabel(string targetLocation)
    {
        return targetLocation switch
        {
            "Farm" => "农场",
            "Town" => "鹈鹕镇",
            "Mountain" => "山上",
            "Mine" => "矿井",
            "Beach" => "海边",
            "Forest" => "煤矿森林",
            "BusStop" => "巴士站",
            "Trailer" => "潘妮和帕姆的家",
            "Saloon" => "星之果实酒吧",
            "SeedShop" => "皮埃尔的杂货店",
            "ArchaeologyHouse" => "博物馆和图书馆",
            "Hospital" => "诊所",
            "JoshHouse" => "亚历克斯家",
            "HaleyHouse" => "海莉和艾米丽家",
            "SamHouse" => "山姆家",
            "ScienceHouse" => "罗宾家",
            "LeahHouse" => "莉亚家",
            "AnimalShop" => "玛妮牧场",
            "ElliottHouse" => "艾利欧特小屋",
            "Blacksmith" => "铁匠铺",
            "FishShop" => "鱼店",
            "WizardHouse" => "法师塔",
            "Tent" => "莱纳斯的帐篷",
            _ => targetLocation
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

    private static IEnumerable<Point> GetTilesAround(Point center, int maxRadius)
    {
        yield return center;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }

                    yield return new Point(center.X + dx, center.Y + dy);
                }
            }
        }
    }
}
