using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace LivingNPCs.Behavior;

internal static class ScheduleReflectionReader
{
    public static bool TryReadScheduleDestination(object scheduleEntry, out string locationName, out Point targetTile, out int facingDirection)
    {
        locationName = ReadString(scheduleEntry, "targetLocationName");
        facingDirection = ReadInt(scheduleEntry, "facingDirection", -1);
        targetTile = Point.Zero;
        return !string.IsNullOrWhiteSpace(locationName)
            && TryReadTile(scheduleEntry, "targetTile", out targetTile);
    }

    public static bool TryReadWarpTargetTile(object warp, out Point targetTile)
    {
        bool hasX = TryReadNumericMember(warp, "TargetX", out int x)
            || TryReadNumericMember(warp, "targetX", out x);
        bool hasY = TryReadNumericMember(warp, "TargetY", out int y)
            || TryReadNumericMember(warp, "targetY", out y);

        if (hasX && hasY)
        {
            targetTile = new Point(x, y);
            return true;
        }

        targetTile = Point.Zero;
        return false;
    }

    public static string GetWarpTargetName(object warp)
    {
        foreach (string memberName in new[] { "TargetName", "targetName", "TargetLocationName", "targetLocationName" })
        {
            if (ReadMember(warp, memberName) is string targetName && !string.IsNullOrWhiteSpace(targetName))
            {
                return targetName;
            }
        }

        return string.Empty;
    }

    private static bool TryReadTile(object scheduleEntry, string memberName, out Point targetTile)
    {
        object? value = ReadMember(scheduleEntry, memberName);
        if (value is Point point)
        {
            targetTile = point;
            return true;
        }

        if (value is Vector2 vector)
        {
            targetTile = new Point((int)vector.X, (int)vector.Y);
            return true;
        }

        if (value != null
            && TryReadNumericMember(value, "X", out int x)
            && TryReadNumericMember(value, "Y", out int y))
        {
            targetTile = new Point(x, y);
            return true;
        }

        targetTile = Point.Zero;
        return false;
    }

    private static string ReadString(object source, string memberName)
    {
        return ReadMember(source, memberName) as string ?? string.Empty;
    }

    private static int ReadInt(object source, string memberName, int fallback)
    {
        object? value = ReadMember(source, memberName);
        if (value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TryReadNumericMember(object source, string memberName, out int value)
    {
        object? raw = ReadMember(source, memberName);
        if (raw == null)
        {
            value = 0;
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static object? ReadMember(object source, string memberName)
    {
        var type = source.GetType();
        return type.GetField(memberName)?.GetValue(source)
            ?? type.GetProperty(memberName)?.GetValue(source);
    }
}
