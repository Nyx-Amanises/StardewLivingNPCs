using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using StardewModdingAPI;

namespace LivingNPCs;

public sealed class ModEntry : Mod
{
    internal static ModConfig ActiveConfig { get; private set; } = new();

    private BehaviorEngine? engine;
    private ModConfig config = null!;

    public override object GetApi()
    {
        return new LivingNPCsApi(this.engine);
    }

    public override void Entry(IModHelper helper)
    {
        I18n.Init(helper.Translation);
        DialogueServices.Initialize(helper, Monitor);
        this.config = helper.ReadConfig<ModConfig>();
        ActiveConfig = this.config;
        ModCompatibility.Initialize(helper.ModRegistry);
        bool configChanged = this.config.Migrate();
        configChanged |= this.config.Validate();
        if (configChanged)
        {
            helper.WriteConfig(this.config);
        }

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

        if (!this.config.EnableMod)
        {
            Monitor.Log(I18n.Get("log.mod.disabled"), LogLevel.Info);
            return;
        }

        NpcDisposition.LoadCommunityProfiles(helper.DirectoryPath, Monitor);
        this.engine = new BehaviorEngine(helper, Monitor, this.config);
        this.engine.RegisterEvents();

        // WP14 持久化与旧数据迁移接线；WP12-TODO: 事件接线最终收编到游戏集成层。
        Dialogue.Persistence.DialoguePersistence.RegisterEvents();

        Monitor.Log(I18n.Get("log.mod.loaded"), LogLevel.Info);
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

    public string GetConversationContext(string npcName, string npcDisplayName)
    {
        return this.engine?.GetConversationContext(npcName, npcDisplayName) ?? string.Empty;
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
