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
}
