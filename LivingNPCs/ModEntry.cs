using LivingNPCs.Behavior;
using StardewModdingAPI;

namespace LivingNPCs;

public sealed class ModEntry : Mod
{
    private BehaviorEngine engine = null!;
    private ModConfig config = null!;

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        if (this.config.Migrate())
        {
            helper.WriteConfig(this.config);
        }

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

        if (!this.config.EnableMod)
        {
            Monitor.Log("LivingNPCs is disabled in config.json.", LogLevel.Info);
            return;
        }

        this.engine = new BehaviorEngine(helper, Monitor, this.config);
        this.engine.RegisterEvents();

        Monitor.Log("LivingNPCs loaded.", LogLevel.Info);
    }

    private void OnGameLaunched(object? sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
    {
        ModConfigMenu.Register(this, this.config);
    }
}
