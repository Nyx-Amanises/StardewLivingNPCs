using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Diagnostics;
using StardewModdingAPI;

namespace LivingNPCs;

public sealed class ModEntry : Mod
{
    internal static ModConfig ActiveConfig { get; private set; } = new();

    private BehaviorEngine? engine;
    private ModConfig config = null!;

    public override void Entry(IModHelper helper)
    {
        I18n.Init(helper.Translation);
        DialogueServices.Initialize(helper, Monitor);
        DiagnosticMarkdownLogWriter.BeginSession(this.ModManifest.Version.ToString());
        this.config = helper.ReadConfig<ModConfig>();
        ActiveConfig = this.config;
        ModCompatibility.Initialize(helper.ModRegistry);
        bool configChanged = this.config.Migrate();
        configChanged |= this.config.Validate();
        if (configChanged)
        {
            helper.WriteConfig(this.config);
        }

        // 对话引擎的运行时配置视图（WP15 §3.1）：无论行为系统开关如何都保持同步。
        DialogueServices.Config.SyncFrom(this.config);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

        if (!this.config.EnableMod)
        {
            Monitor.Log(I18n.Get("log.mod.disabled"), LogLevel.Info);
            return;
        }

        NpcDisposition.LoadCommunityProfiles(helper.DirectoryPath, Monitor);
        this.engine = new BehaviorEngine(helper, Monitor, this.config, this.ModManifest.UniqueID);
        this.engine.RegisterEvents();

        // 交换记录直调（WP16 §4.1）：引擎生成完成后在后台线程回调；BehaviorEngine 内部用
        // ConcurrentQueue + UpdateTicked 把应用挪回主线程。display name 由行为侧自行解析。
        Dialogue.Engine.DialogueEngine.RecordExchangeCallback = (npcName, playerText, npcLine, analysisJson) =>
            this.engine?.RecordValleyTalkExchange(npcName, npcName, playerText, npcLine, analysisJson);

        // 对话引擎接线单入口（WP12 §4.7）：收编 WP14 持久化与 WP15 内容装配的注册调用，
        // 装配引擎/调度器/补丁/输入队列/命令；行为上下文经委托在触发时刻注入（WP16 方向反转），
        // EnableBehaviorContextInDialogue 关闭时注入空串（行为系统其余功能不受影响）。
        Dialogue.GameHooks.DialogueEngineBootstrapper.Attach(
            helper,
            Monitor,
            this.config,
            this.ModManifest.UniqueID,
            npc => this.config.EnableBehaviorContextInDialogue
                ? this.engine?.GetConversationContext(npc.Name, npc.displayName) ?? string.Empty
                : string.Empty,
            (npc, itemId, itemName, taste) => this.config.EnableBehaviorContextInDialogue
                ? this.engine?.GetGiftResponseContext(npc.Name, npc.displayName, itemId, itemName, taste) ?? string.Empty
                : string.Empty);

        Monitor.Log(I18n.Get("log.mod.loaded"), LogLevel.Info);
    }

    private void OnGameLaunched(object? sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
    {
        ModConfigMenu.Register(this, this.config);
    }
}
