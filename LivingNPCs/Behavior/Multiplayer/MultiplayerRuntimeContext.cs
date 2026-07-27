using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>联机拓扑与本地玩家信息的可测试读取边界。</summary>
internal interface IMultiplayerRuntimeContext
{
    bool IsMultiplayer { get; }
    bool IsMainPlayer { get; }
    bool IsOnHostComputer { get; }
    long? HostPlayerId { get; }
    string LocalPlayerName { get; }
    int TotalDays { get; }
    int TimeOfDay { get; }
}

/// <summary>从 SMAPI/Stardew Valley 全局状态读取当前联机上下文。</summary>
internal sealed class GameMultiplayerRuntimeContext : IMultiplayerRuntimeContext
{
    public static GameMultiplayerRuntimeContext Instance { get; } = new();

    public bool IsMultiplayer => Context.IsMultiplayer;
    public bool IsMainPlayer => Context.IsMainPlayer;
    public bool IsOnHostComputer => Context.IsOnHostComputer;

    public long? HostPlayerId
    {
        get
        {
            try
            {
                return Game1.MasterPlayer?.UniqueMultiplayerID;
            }
            catch
            {
                return null;
            }
        }
    }

    public string LocalPlayerName => Game1.player?.Name ?? string.Empty;
    public int TotalDays => Context.IsWorldReady ? Game1.Date.TotalDays : -1;
    public int TimeOfDay => Context.IsWorldReady ? Game1.timeOfDay : 0;
}
