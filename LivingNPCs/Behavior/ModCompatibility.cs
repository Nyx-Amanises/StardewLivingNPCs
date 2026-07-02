namespace LivingNPCs.Behavior;

internal static class ModCompatibility
{
    internal const string SveSourceLabel = "Stardew Valley Expanded";
    internal const string RsvSourceLabel = "Ridgeside Village";
    internal const string SveModId = "FlashShifter.StardewValleyExpandedCP";

    private static bool sveLoaded;

    /// <summary>Called once from ModEntry with the mod registry.</summary>
    internal static void Initialize(StardewModdingAPI.IModRegistry registry)
    {
        sveLoaded = registry.IsLoaded(SveModId);
    }

    /// <summary>
    /// SVE-specific content (anchor tables, gift pools, NPC policies) activates only when SVE is
    /// actually installed AND the compatibility toggle is on. The config flag defaults to true, so
    /// without the install check every vanilla player would match SVE map coordinates — the SVE
    /// Beach/Forest/Mountain maps share the vanilla canvas size, making size checks alone useless.
    /// </summary>
    internal static bool EnableSve => sveLoaded && ModEntry.ActiveConfig.EnableSveCompatibility;

    internal static bool EnableRsv => false;

    internal static bool IsSveSource(string? sourceLabel)
    {
        return string.Equals(sourceLabel, SveSourceLabel, System.StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsRsvSource(string? sourceLabel)
    {
        return string.Equals(sourceLabel, RsvSourceLabel, System.StringComparison.OrdinalIgnoreCase);
    }
}
