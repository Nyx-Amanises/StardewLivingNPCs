using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// 生成请求发起时对游戏状态的一次性快照（WP10 §4.3）。纯数据、可直接构造（单测友好）；
/// 游戏读取集中在 <see cref="GameStateSnapshotCollector"/>。字段为空/缺省时相应提示词小节静默降级。
/// </summary>
internal sealed class GameStateSnapshot
{
    // ---- 日期与时间 ----
    public int Year { get; init; } = 1;

    /// <summary>季节序：春0夏1秋2冬3。</summary>
    public int SeasonIndex { get; init; }

    /// <summary>季节内部名（小写：spring/summer/fall/winter）。</summary>
    public string SeasonName { get; init; } = "spring";

    public int DayOfMonth { get; init; } = 1;

    /// <summary>星期 = dayOfMonth % 7（0=Sun…6=Sat，§4.3）。</summary>
    public int DayOfWeek => this.DayOfMonth % 7;

    /// <summary>游戏钟表值（如 1330）。</summary>
    public int TimeOfDay { get; init; } = 600;

    // ---- 天气（按当前位置判定的标志列表：rain/snow/lightning/green rain） ----
    public IReadOnlyList<string> WeatherFlags { get; init; } = Array.Empty<string>();

    // ---- 好感与关系 ----
    /// <summary>好感点原始值；-1 = 无好感数据。</summary>
    public int FriendshipPoints { get; init; } = -1;

    public bool FriendshipExists => this.FriendshipPoints >= 0;

    /// <summary>心数 = 点数/250；0 点视为 -1（从未正式认识，§4.3）。</summary>
    public int Hearts => !this.FriendshipExists ? -1 : this.FriendshipPoints == 0 ? -1 : this.FriendshipPoints / 250;

    public bool IsMarriedToFarmer { get; init; }
    public bool IsRoommate { get; init; }
    public int DaysMarried { get; init; }
    public bool IsDating { get; init; }
    /// <summary>约会中：公开（true）/低调（false）变体（§4.6.4.17）。</summary>
    public bool DatingPublicly { get; init; } = true;
    public bool IsEngaged { get; init; }
    /// <summary>已订婚时距婚期的天数。</summary>
    public int DaysUntilWedding { get; init; }
    public bool IsDivorced { get; init; }
    public bool ProposalRejected { get; init; }
    /// <summary>性向词（约会文案参数，采集时按双方性别给出；可空）。</summary>
    public string OrientationWord { get; init; } = string.Empty;

    /// <summary>NPC 是儿童（未婚分支分档用）。</summary>
    public bool NpcIsChild { get; init; }

    /// <summary>NPC 是可浪漫对象。</summary>
    public bool NpcIsDatable { get; init; }

    /// <summary>NPC 已与他人结婚/非单身成人。</summary>
    public bool NpcIsMarriedToOther { get; init; }

    /// <summary>本 NPC 是配偶亲属（Inlaw 判定，§4.3）；值为配偶名，空 = 不是。</summary>
    public string InlawOfSpouse { get; init; } = string.Empty;

    // ---- 位置与日程 ----
    public string LocationName { get; init; } = string.Empty;
    public string LocationDisplayName { get; init; } = string.Empty;
    public int TileX { get; init; }
    public int TileY { get; init; }
    public bool IsTravelling { get; init; }

    /// <summary>NPC 家地图内部名（在家判定）。</summary>
    public string HomeLocationName { get; init; } = string.Empty;

    /// <summary>当前位置可能是营业中的店铺（在家判定的店铺提示）。</summary>
    public bool MightBeInShop { get; init; }

    /// <summary>当前活动描述（采集时已按 endOfRouteBehavior 归一化为活动短语）。</summary>
    public string CurrentActivity { get; init; } = string.Empty;

    /// <summary>当前日程停留点描述（≤当前时间的最近一条）。</summary>
    public string CurrentSchedulePoint { get; init; } = string.Empty;

    /// <summary>下一日程点目标地点（显示名）；空 = 无后续日程。</summary>
    public string NextScheduleLocation { get; init; } = string.Empty;

    /// <summary>距下一日程点的分钟数（向下取 0）。</summary>
    public int? MinutesUntilNextSchedule { get; init; }

    /// <summary>剩余日程的不重复地点显示名列表（未来计划）。</summary>
    public IReadOnlyList<string> RemainingScheduleStops { get; init; } = Array.Empty<string>();

    // ---- 农夫侧 ----
    public bool FarmerIsMale { get; init; } = true;
    public string FarmerName { get; init; } = string.Empty;
    public int FarmerMoney { get; init; }
    public string FarmerSpouseName { get; init; } = string.Empty;
    public bool FarmerIsMarried { get; init; }
    public IReadOnlyList<ChildDescription> Children { get; init; } = Array.Empty<ChildDescription>();

    /// <summary>怀孕/领养倒计时天数；null = 无。</summary>
    public int? DaysUntilBirth { get; init; }

    // ---- 世界进度（GameState 小节：各 yes/no） ----
    public bool CommunityCenterDone { get; init; }
    public bool BusFixed { get; init; }
    public bool QuarryBridgeFixed { get; init; }
    public bool MinecartFixed { get; init; }
    public bool BoulderRemoved { get; init; }

    /// <summary>Kent 未回归（第 1 年）。</summary>
    public bool KentAbsent { get; init; }

    // ---- 附近 NPC ----
    public IReadOnlyList<string> NearbyNpcNames { get; init; } = Array.Empty<string>();

    // ---- 农场与财富（Farm 小节） ----
    public IReadOnlyList<string> FarmBuildings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FarmAnimals { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FarmCrops { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FarmPets { get; init; } = Array.Empty<string>();

    // ---- 饰品（Trinkets 小节） ----
    public bool HasFairyBox { get; init; }
    public bool HasFrogCompanion { get; init; }
    public bool HasFlyCompanion { get; init; }

    // ---- 近期活动事件（previousActiveDialogueEvents：键 → 天数） ----
    public IReadOnlyList<KeyValuePair<string, int>> ActiveDialogueEvents { get; init; }
        = Array.Empty<KeyValuePair<string, int>>();

    /// <summary>当日是该 NPC 生日。</summary>
    public bool IsBirthdayToday { get; init; }

    /// <summary>NPC 是成人（Saloon 微醺提示等）。</summary>
    public bool NpcIsAdult { get; init; } = true;

    // ---- 纯派生 ----

    /// <summary>农夫入住总天数 = (年-1)×112 + 季节序×28 + 日-1（§4.6.4.5）。</summary>
    public int ResidencyDays => ((this.Year - 1) * 112) + (this.SeasonIndex * 28) + (this.DayOfMonth - 1);

    /// <summary>财富四档：0 = &lt;1000，1 = &lt;10000，2 = &lt;100000，3 = 以上。</summary>
    public int WealthTier => this.FarmerMoney < 1000 ? 0 : this.FarmerMoney < 10000 ? 1 : this.FarmerMoney < 100000 ? 2 : 3;

    public StardewTime Now => new(
        this.Year,
        this.SeasonIndex switch
        {
            1 => StardewValley.Season.Summer,
            2 => StardewValley.Season.Fall,
            3 => StardewValley.Season.Winter,
            _ => StardewValley.Season.Spring
        },
        this.DayOfMonth,
        this.TimeOfDay);

    /// <summary>时间桶 WP20 键（§4.3）。</summary>
    public string TimeBucketKey => TimeBucketKeyFor(this.TimeOfDay);

    public static string TimeBucketKeyFor(int timeOfDay)
    {
        return timeOfDay switch
        {
            <= 800 => "generalEarlyMorning",
            <= 1130 => "generalLateMorning",
            <= 1400 => "generalMidday",
            <= 1700 => "generalAfternoon",
            <= 2200 => "generalEvening",
            _ => "generalLateNight"
        };
    }

    /// <summary>钟表值 → "H:mm"（时间桶后缀，§4.3）。</summary>
    public static string ClockText(int timeOfDay)
    {
        int hours = timeOfDay / 100;
        int minutes = timeOfDay % 100;
        return $"{hours}:{minutes:00}";
    }
}
