using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;

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
            this.monitor.Log(I18n.Get("log.bridge.valleyTalkMissing"), LogLevel.Info);
            return;
        }

        this.api.SetModName("LivingNPCs");
        this.monitor.Log(I18n.Get("log.bridge.connected"), LogLevel.Info);
    }

    public bool PushBehaviorContext(NPC npc, string promptText)
    {
        if (RsvAiPolicy.IsBlockedNpc(npc) || this.api == null || string.IsNullOrWhiteSpace(promptText))
        {
            return false;
        }

        try
        {
            this.api.RegisterPromptOverride(npc.Name, PromptElement, promptText);
            return true;
        }
        catch (System.Exception ex)
        {
            this.monitor.Log(I18n.Get("log.bridge.registerPromptFailed", new { npc = npc.Name, error = ex.Message }), LogLevel.Debug);
            return false;
        }
    }

    public bool TryRequestGiftDialogue(NPC npc, SObject gift, int taste)
    {
        if (npc == null || gift == null || RsvAiPolicy.IsBlockedNpc(npc))
        {
            return false;
        }

        if (this.api == null)
        {
            this.TryInitialize();
        }

        if (this.api == null)
        {
            return false;
        }

        try
        {
            return this.api.RequestGiftDialogue(npc, gift, taste);
        }
        catch (System.Exception ex)
        {
            this.monitor.Log(I18n.Get("log.bridge.giftDialogueFailed", new { npc = npc.Name, error = ex.Message }), LogLevel.Debug);
            return false;
        }
    }

    public bool IsConnected => this.api != null;

    public void RequestGiftMailText(string requestId, NPC npc, string payloadJson)
    {
        if (this.api == null || npc == null || string.IsNullOrWhiteSpace(requestId) || RsvAiPolicy.IsBlockedNpc(npc))
        {
            return;
        }

        try
        {
            this.api.RequestGiftMailText(requestId, npc.Name, payloadJson);
        }
        catch (System.Exception ex)
        {
            this.monitor.Log(I18n.Get("log.bridge.requestGiftMailFailed", new { npc = npc.Name, error = ex.Message }), LogLevel.Debug);
        }
    }

    public string TryGetGiftMailText(string requestId)
    {
        if (this.api == null || string.IsNullOrWhiteSpace(requestId))
        {
            return string.Empty;
        }

        try
        {
            return this.api.TryGetGiftMailText(requestId) ?? string.Empty;
        }
        catch (System.Exception ex)
        {
            this.monitor.Log(I18n.Get("log.bridge.getGiftMailFailed", new { error = ex.Message }), LogLevel.Debug);
            return string.Empty;
        }
    }

    public void ClearAll()
    {
        if (this.api == null)
        {
            return;
        }

        try
        {
            this.api.ClearPromptOverrides();
        }
        catch (System.Exception ex)
        {
            this.monitor.Log(I18n.Get("log.bridge.clearPromptsFailed", new { error = ex.Message }), LogLevel.Debug);
        }
    }
}
