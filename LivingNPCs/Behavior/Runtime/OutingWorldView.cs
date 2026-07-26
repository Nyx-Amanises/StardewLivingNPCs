using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Thin read-only view of the ambient game state the companion-outing state machine consults.
/// Only primitive reads belong here, so tests can drive outing decisions with a fake view
/// (see <see cref="CompanionOutingRules.PlanTick"/>). Deliberately NOT included: NPC lookup,
/// location objects, pathfinding, and state stamping — wrapping those would be a wide facade
/// rather than a seam, and they stay directly on Game1 in the runtime. Extend member by member
/// as the outing system grows (e.g. multi-leg invitations).
/// </summary>
internal interface IOutingWorldView
{
    int TimeOfDay { get; }
    int TotalDays { get; }

    /// <summary>
    /// 刻意用 Game1.IsMasterGame 而非 Context.IsMainPlayer：出游驱动的是 NPC 寻路模拟
    /// （PathFindController 只在主模拟端更新），判据是"本进程是否模拟世界"。分屏时副屏
    /// 与主屏同属主模拟进程，IsMasterGame 为真而 IsMainPlayer 为假——用后者会错杀。
    /// 每玩家数据（存档/任务栏/好感）才用 Context.IsMainPlayer。
    /// </summary>
    bool IsMasterGame { get; }
    bool IsFestivalToday { get; }
    bool IsLightning { get; }

    /// <summary>An event or festival cutscene is currently playing in the player's location.</summary>
    bool IsEventActive { get; }

    bool IsMenuOpen { get; }

    /// <summary>Name of the location the player is currently in (empty when unknown).</summary>
    string CurrentLocationName { get; }
}

internal sealed class GameOutingWorldView : IOutingWorldView
{
    public static GameOutingWorldView Instance { get; } = new();

    public int TimeOfDay => Game1.timeOfDay;
    public int TotalDays => Game1.Date.TotalDays;
    public bool IsMasterGame => Game1.IsMasterGame;
    public bool IsFestivalToday => Utility.isFestivalDay(Game1.dayOfMonth, Game1.season);
    public bool IsLightning => Game1.isLightning;
    public bool IsEventActive => Game1.eventUp || Game1.currentLocation?.currentEvent != null;
    public bool IsMenuOpen => Game1.activeClickableMenu != null;
    public string CurrentLocationName => Game1.currentLocation?.Name ?? string.Empty;
}
