using System;

namespace LivingNPCs.Behavior;

/// <summary>What the side-by-side stroll logic should do with the companion NPC on this tick.</summary>
internal enum OutingStrollStep
{
    /// <summary>Stroll does not own this tick (disabled, farmer absent, or NPC off-map): anchor behavior applies.</summary>
    None,

    /// <summary>Walk toward a tile beside the farmer.</summary>
    FollowFarmer,

    /// <summary>Close enough: stop and stand beside the farmer, facing them.</summary>
    SettleBesideFarmer
}

/// <summary>
/// Pure decision rules for the two "the outing feels alive" behaviors: walking side by side with
/// the farmer at the destination, and periodic small-talk speech bubbles. Extracted from the
/// runtime so cadence, hysteresis, and pool selection are testable without game state.
/// </summary>
internal static class OutingCompanionshipRules
{
    /// <summary>Beyond this the NPC starts walking toward the farmer.</summary>
    public const float FollowTriggerDistanceTiles = 3.2f;

    /// <summary>At or under this the NPC settles beside the farmer.</summary>
    public const float SettleDistanceTiles = 1.9f;

    /// <summary>The farmer counts as "moving" for this many ticks after their tile last changed (~1.5s).</summary>
    public const int FarmerRecentMoveTicks = 90;

    /// <summary>Minimum ticks between follow-path reassignments (~0.5s), so pathing never thrashes.</summary>
    public const int StrollRepathIntervalTicks = 30;

    public const int MaxChatRemarksPerOuting = 6;
    public const int MinChatIntervalMinutes = 20;
    public const int ChatIntervalJitterMinutes = 21;
    public const int ChatPoolVariants = 3;

    /// <summary>
    /// Side-by-side step decision with hysteresis: between the settle and follow radii the NPC
    /// keeps doing whatever the farmer's movement suggests (follow while they move, settle once
    /// they stop), so it neither orbits the farmer nor freezes mid-step.
    /// </summary>
    public static OutingStrollStep PlanStrollStep(
        bool strollEnabled,
        bool farmerAtDestination,
        bool npcAtDestination,
        float distanceToFarmerTiles,
        int ticksSinceFarmerMoved)
    {
        if (!strollEnabled || !farmerAtDestination || !npcAtDestination)
        {
            return OutingStrollStep.None;
        }

        if (distanceToFarmerTiles > FollowTriggerDistanceTiles)
        {
            return OutingStrollStep.FollowFarmer;
        }

        if (distanceToFarmerTiles <= SettleDistanceTiles)
        {
            return OutingStrollStep.SettleBesideFarmer;
        }

        return ticksSinceFarmerMoved <= FarmerRecentMoveTicks
            ? OutingStrollStep.FollowFarmer
            : OutingStrollStep.SettleBesideFarmer;
    }

    /// <summary>Whether one small-talk bubble may fire on this tick.</summary>
    public static bool ShouldShowChat(
        bool chatEnabled,
        bool farmerPresent,
        bool arrivalRemarkShown,
        int chatRemarksShown,
        int timeOfDay,
        int nextChatTimeOfDay,
        float distanceToFarmerTiles,
        float maxDistanceTiles)
    {
        return chatEnabled
            && farmerPresent
            && arrivalRemarkShown
            && chatRemarksShown < MaxChatRemarksPerOuting
            && nextChatTimeOfDay > 0
            && timeOfDay >= nextChatTimeOfDay
            && distanceToFarmerTiles <= maxDistanceTiles;
    }

    /// <summary>Next chat slot: 20-40 game minutes ahead, jittered stably per NPC/outing/count.</summary>
    public static int NextChatTimeOfDay(
        string npcName,
        string targetLocation,
        int totalDays,
        int chatRemarksShown,
        int fromTimeOfDay)
    {
        int jitter = (int)(ComputeStableHash($"{npcName}|{targetLocation}|{totalDays}|chat-slot|{chatRemarksShown}")
            % (uint)ChatIntervalJitterMinutes);
        return BehaviorTimeMath.AddMinutesToTime(fromTimeOfDay, MinChatIntervalMinutes + jitter);
    }

    /// <summary>
    /// i18n key of the line to show. Vehicle destinations get their own flavor pools; everything
    /// else falls back to the activity-style pool. While actively strolling side by side, some
    /// slots use the walking-together pool instead. The variant rotates stably and never repeats
    /// the previous one back to back.
    /// </summary>
    public static string SelectChatKey(
        string targetLocation,
        string activityStyle,
        string npcName,
        int totalDays,
        int chatRemarksShown,
        int previousVariant,
        bool strollingNow,
        out int selectedVariant)
    {
        string pool = strollingNow && chatRemarksShown % 2 == 1
            ? "stroll"
            : TravelLocationRules.Normalize(targetLocation, targetLocation ?? string.Empty) switch
            {
                "Desert" => "desert",
                "IslandSouth" => "island",
                "MovieTheater" => "theater",
                "BathHouse_Entry" => "spa",
                _ => NormalizeStylePool(activityStyle)
            };

        selectedVariant = 1 + (int)(ComputeStableHash($"{npcName}|{targetLocation}|{totalDays}|chat-line|{chatRemarksShown}")
            % (uint)ChatPoolVariants);
        if (selectedVariant == previousVariant)
        {
            selectedVariant = (selectedVariant % ChatPoolVariants) + 1;
        }

        return $"outing.chat.{pool}.{selectedVariant}";
    }

    private static string NormalizeStylePool(string activityStyle)
    {
        return activityStyle switch
        {
            "scenic" or "browse" or "quiet" or "social" => activityStyle,
            "festival" => "quiet",
            _ => "visit"
        };
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }
}
