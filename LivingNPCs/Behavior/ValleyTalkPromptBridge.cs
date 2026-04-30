using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class ValleyTalkPromptBridge
{
    private const string ValleyTalkUniqueId = "dandm1.ValleyTalk";
    private const string PromptElement = "ThirdPartyContext";

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private ValleyTalk.IValleyTalkInterface? api;

    public ValleyTalkPromptBridge(IModHelper helper, IMonitor monitor, ModConfig config)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
    }

    public void TryInitialize()
    {
        if (!this.config.EnableValleyTalkPromptBridge)
        {
            return;
        }

        this.api = this.helper.ModRegistry.GetApi<ValleyTalk.IValleyTalkInterface>(ValleyTalkUniqueId);
        if (this.api == null)
        {
            this.monitor.Log("ValleyTalk was not found. LivingNPCs will run behavior hooks without prompt integration.", LogLevel.Info);
            return;
        }

        this.api.SetModName("LivingNPCs");
        this.monitor.Log("Connected to ValleyTalk prompt API.", LogLevel.Info);
    }

    public void PushBehaviorContext(NPC npc, string promptText)
    {
        if (this.api == null || string.IsNullOrWhiteSpace(promptText))
        {
            return;
        }

        this.api.RegisterPromptOverride(npc.Name, PromptElement, promptText);
    }

    public void ClearAll()
    {
        this.api?.ClearPromptOverrides();
    }
}
