using System;
using StardewValley;

namespace LivingNPCs.Dialogue;

/// <summary>
/// 游戏内时间戳。JSON 形态是持久化契约（WP14 §3.1.1/§5.2）：
/// {"season": "Spring", "dayOfMonth": 14, "timeOfDay": 1300, "year": 3}，
/// 属性名小写开头、季节经 StringEnumConverter 序列化为首字母大写字符串。
/// </summary>
internal readonly record struct StardewTime(int year, Season season, int dayOfMonth, int timeOfDay) : IComparable<StardewTime>
{
    private const int DaysPerSeason = 28;
    private const int SeasonsPerYear = 4;
    private const int DaysPerYear = DaysPerSeason * SeasonsPerYear;

    /// <summary>绝对天数 = year×112 + 季节序号(春0夏1秋2冬3)×28 + dayOfMonth（转录归档水位公式，§3.3.1）。</summary>
    public int ToAbsoluteDays()
    {
        return (this.year * DaysPerYear) + (SeasonIndex(this.season) * DaysPerSeason) + this.dayOfMonth;
    }

    public StardewTime AddDays(int days)
    {
        int zeroBasedDay = Math.Max(0, (this.year * DaysPerYear) + (SeasonIndex(this.season) * DaysPerSeason) + (this.dayOfMonth - 1) + days);
        int newYear = zeroBasedDay / DaysPerYear;
        int dayOfYear = zeroBasedDay % DaysPerYear;
        int newSeasonIndex = dayOfYear / DaysPerSeason;
        int newDay = (dayOfYear % DaysPerSeason) + 1;
        return new StardewTime(newYear, SeasonFromIndex(newSeasonIndex), newDay, this.timeOfDay);
    }

    public int CompareTo(StardewTime other)
    {
        int dayComparison = this.ToAbsoluteDays().CompareTo(other.ToAbsoluteDays());
        return dayComparison != 0 ? dayComparison : this.timeOfDay.CompareTo(other.timeOfDay);
    }

    private static int SeasonIndex(Season season)
    {
        return season switch
        {
            Season.Spring => 0,
            Season.Summer => 1,
            Season.Fall => 2,
            Season.Winter => 3,
            _ => 0
        };
    }

    private static Season SeasonFromIndex(int index)
    {
        return index switch
        {
            1 => Season.Summer,
            2 => Season.Fall,
            3 => Season.Winter,
            _ => Season.Spring
        };
    }
}
