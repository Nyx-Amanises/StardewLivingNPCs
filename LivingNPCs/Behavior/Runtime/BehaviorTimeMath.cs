namespace LivingNPCs.Behavior;

internal static class BehaviorTimeMath
{
    public static int AddMinutesToTime(int timeOfDay, int minutes)
    {
        int hours = timeOfDay / 100;
        int mins = timeOfDay % 100;
        int totalMinutes = (hours * 60) + mins + minutes;
        return ((totalMinutes / 60) * 100) + (totalMinutes % 60);
    }

    public static int GetElapsedMinutes(int earlierTimeOfDay, int laterTimeOfDay)
    {
        return System.Math.Max(0, ToMinutes(laterTimeOfDay) - ToMinutes(earlierTimeOfDay));
    }

    public static string FormatTime(int timeOfDay)
    {
        return $"{timeOfDay / 100:00}:{timeOfDay % 100:00}";
    }

    private static int ToMinutes(int timeOfDay)
    {
        return ((timeOfDay / 100) * 60) + (timeOfDay % 100);
    }
}
