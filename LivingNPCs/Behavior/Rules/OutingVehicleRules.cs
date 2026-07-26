using System;
using System.Collections.Generic;

namespace LivingNPCs.Behavior;

/// <summary>
/// One "ride" outing destination: the walkable gateway map the NPC boards from, and the vehicle
/// kind that drives the boarding remark wording (bus / boat / theater door / spa door).
/// </summary>
internal sealed record OutingVehicle(string TargetLocation, string GatewayLocation, string Kind);

/// <summary>
/// Vehicle-gateway rules for outing destinations that vanilla NPC schedule pathfinding can never
/// reach on foot (Calico Desert, Ginger Island, the movie theater interior, the railroad spa).
/// The outing walks the NPC to a boarding spot on the gateway map, shows a boarding remark, then
/// warps across — the same abstraction the game itself uses for the bus, Willy's boat, and
/// vanilla island visits (SetupIslandSchedules also teleports villagers).
/// </summary>
internal static class OutingVehicleRules
{
    private static readonly Dictionary<string, OutingVehicle> Vehicles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Desert"] = new("Desert", "BusStop", "bus"),
            ["IslandSouth"] = new("IslandSouth", "Beach", "boat"),
            ["MovieTheater"] = new("MovieTheater", "Town", "theater"),
            ["BathHouse_Entry"] = new("BathHouse_Entry", "Mountain", "spa")
        };

    /// <summary>Railroad landslide clears on the morning of day 31 (Summer 3, year 1).</summary>
    public const uint RailroadOpenDaysPlayed = 31;

    public static bool IsVehicleTarget(string targetLocation)
    {
        return Vehicles.ContainsKey(targetLocation ?? string.Empty);
    }

    public static bool TryGetVehicle(string targetLocation, out OutingVehicle vehicle)
    {
        if (Vehicles.TryGetValue(targetLocation ?? string.Empty, out OutingVehicle? found) && found != null)
        {
            vehicle = found;
            return true;
        }

        vehicle = null!;
        return false;
    }

    /// <summary>
    /// Save-progress gate, pure for tests. Reasons are English memory-facing strings, matching the
    /// existing outing decline convention (they end up in SkippedCompanionOuting memory entries).
    /// </summary>
    public static bool IsUnlocked(
        string targetLocation,
        Func<string, bool> hasMail,
        uint daysPlayed,
        out string lockedReason)
    {
        lockedReason = string.Empty;
        switch (TravelLocationRules.Normalize(targetLocation, targetLocation ?? string.Empty))
        {
            case "Desert":
                if (!hasMail("ccVault") && !hasMail("jojaVault"))
                {
                    lockedReason = "the bus to Calico Desert has not been repaired yet";
                    return false;
                }

                return true;
            case "IslandSouth":
                if (!hasMail("willyBoatFixed"))
                {
                    lockedReason = "Willy's boat to Ginger Island has not been repaired yet";
                    return false;
                }

                return true;
            case "MovieTheater":
                if (!hasMail("ccMovieTheater") && !hasMail("ccMovieTheaterJoja"))
                {
                    lockedReason = "the movie theater has not been built yet";
                    return false;
                }

                return true;
            case "BathHouse_Entry":
                if (daysPlayed < RailroadOpenDaysPlayed)
                {
                    lockedReason = "the railroad path to the spa has not opened yet";
                    return false;
                }

                return true;
            default:
                return true;
        }
    }

    /// <summary>Runtime wrapper over the master player's save flags.</summary>
    public static bool IsUnlockedInCurrentSave(string targetLocation, out string lockedReason)
    {
        try
        {
            var master = StardewValley.Game1.MasterPlayer;
            uint daysPlayed = StardewValley.Game1.stats?.DaysPlayed ?? 0;
            return IsUnlocked(
                targetLocation,
                mailId => master?.mailReceived.Contains(mailId) == true,
                daysPlayed,
                out lockedReason);
        }
        catch
        {
            lockedReason = "the destination's unlock state could not be checked";
            return false;
        }
    }
}
