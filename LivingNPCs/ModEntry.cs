using LivingNPCs.Behavior;
using StardewModdingAPI;

namespace LivingNPCs;

public sealed class ModEntry : Mod
{
    private BehaviorEngine? engine;
    private ModConfig config = null!;

    public override object GetApi()
    {
        return new LivingNPCsApi(this.engine);
    }

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

        NpcDisposition.LoadCommunityProfiles(helper.DirectoryPath, Monitor);
        this.engine = new BehaviorEngine(helper, Monitor, this.config);
        this.engine.RegisterEvents();

        Monitor.Log("LivingNPCs loaded.", LogLevel.Info);
    }

    private void OnGameLaunched(object? sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
    {
        ModConfigMenu.Register(this, this.config);
    }
}

public sealed class LivingNPCsApi
{
    private readonly BehaviorEngine? engine;

    internal LivingNPCsApi(BehaviorEngine? engine)
    {
        this.engine = engine;
    }

    public string GetGiftResponseContext(string npcName, string npcDisplayName, string giftItemId, string giftName, int taste)
    {
        return this.engine?.GetGiftResponseContext(npcName, npcDisplayName, giftItemId, giftName, taste) ?? string.Empty;
    }

    public bool RecordValleyTalkExchange(string npcName, string npcDisplayName, string playerText, string npcResponse, string analysisJson)
    {
        return this.engine?.RecordValleyTalkExchange(npcName, npcDisplayName, playerText, npcResponse, analysisJson) == true;
    }
}
