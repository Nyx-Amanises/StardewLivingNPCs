namespace LivingNPCs.Behavior;

internal static class ModCompatibility
{
    internal const string SveSourceLabel = "Stardew Valley Expanded";
    internal const string RsvSourceLabel = "Ridgeside Village";

    internal static bool EnableSve => ModEntry.ActiveConfig.EnableSveCompatibility;

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
