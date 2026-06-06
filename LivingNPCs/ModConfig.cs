using System;
using StardewModdingAPI.Utilities;

namespace LivingNPCs;

internal sealed class ModConfig
{
    public bool EnableMod { get; set; } = true;
    public bool Debug { get; set; } = false;
    public KeybindList BehaviorHotkey { get; set; } = KeybindList.Parse("LeftShift + H");
    public KeybindList InspectMemoryHotkey { get; set; } = KeybindList.Parse("LeftShift + J");
    public string ManualBehaviorMode { get; set; } = "Auto";
    public int ManualEmoteId { get; set; } = 16;
    public int MaxMemoryEntriesPerNpc { get; set; } = 20;
    public int PromptMemoryEntries { get; set; } = 4;
    public bool EnableConversationMemory { get; set; } = true;
    public bool EnableHelpRequests { get; set; } = true;
    public int MaxPendingHelpRequestsPerNpc { get; set; } = 1;
    public int HelpRequestCooldownDays { get; set; } = 3;
    public int MinRelationshipTrustForHelpRequests { get; set; } = 35;
    public int MinHelpRequestFriendshipReward { get; set; } = 50;
    public int MaxHelpRequestFriendshipReward { get; set; } = 100;
    public bool EnableAiDialogueFriendship { get; set; } = true;
    public int MaxAiDialogueFriendshipPerNpcPerDay { get; set; } = 30;
    public bool EnableDialogueFollowUps { get; set; } = true;
    public bool EnableDialogueDrivenBehaviors { get; set; } = true;
    public int MaxDialogueBehaviorInfluenceDays { get; set; } = 3;
    public bool EnableAiWorldActions { get; set; } = true;
    public bool AllowAiSmallGifts { get; set; } = true;
    public bool AllowAiMeaningfulGifts { get; set; } = true;
    public int AiMeaningfulGiftCooldownDays { get; set; } = 7;
    public int AiDailyGiftChanceMinPercent { get; set; } = 30;
    public int AiDailyGiftChanceMaxPercent { get; set; } = 50;
    public bool AllowAiMoneyGifts { get; set; } = true;
    public int MaxAiMoneyGiftAmount { get; set; } = 250;
    public bool AllowAiFarmHelp { get; set; } = true;
    public int MaxAiWateredTilesPerAction { get; set; } = 12;
    public bool AllowAiWalkTogether { get; set; } = true;
    public int MaxAiWalkTogetherMinutes { get; set; } = 20;
    public bool AllowAiEscortToLocation { get; set; } = true;
    public bool AllowAiFestivalInteractions { get; set; } = true;
    public bool AllowAiQuestAssists { get; set; } = true;
    public bool EnableNpcState { get; set; } = true;
    public int NpcStateDailyDecay { get; set; } = 12;
    public int NpcEmotionDailyDecay { get; set; } = 12;
    public int NpcConflictDailyDecay { get; set; } = 8;
    public bool EnablePassiveBehaviors { get; set; } = true;
    public int PassiveBehaviorChancePercent { get; set; } = 6;
    public int MaxBehaviorsPerNpcPerDay { get; set; } = 2;
    public int MaxInteractionDistanceTiles { get; set; } = 6;
    public bool AllowFacePlayer { get; set; } = true;
    public bool AllowEmotes { get; set; } = true;
    public bool AllowApproachPlayer { get; set; } = true;
    public bool EnableAiPlanner { get; set; } = false;
    public string AiPlannerEndpoint { get; set; } = "http://localhost:11434/v1/chat/completions";
    public string AiPlannerApiKey { get; set; } = string.Empty;
    public string AiPlannerModel { get; set; } = string.Empty;
    public int AiPlannerTimeoutSeconds { get; set; } = 8;
    public bool EnableValleyTalkPromptBridge { get; set; } = true;
    public bool ShowHudMessages { get; set; } = true;

    public bool Migrate()
    {
        if (this.BehaviorHotkey.ToString().Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            this.BehaviorHotkey = KeybindList.Parse("LeftShift + H");
            return true;
        }

        return false;
    }

    public void ResetToDefaults()
    {
        var defaults = new ModConfig();

        this.EnableMod = defaults.EnableMod;
        this.Debug = defaults.Debug;
        this.BehaviorHotkey = defaults.BehaviorHotkey;
        this.InspectMemoryHotkey = defaults.InspectMemoryHotkey;
        this.ManualBehaviorMode = defaults.ManualBehaviorMode;
        this.ManualEmoteId = defaults.ManualEmoteId;
        this.MaxMemoryEntriesPerNpc = defaults.MaxMemoryEntriesPerNpc;
        this.PromptMemoryEntries = defaults.PromptMemoryEntries;
        this.EnableConversationMemory = defaults.EnableConversationMemory;
        this.EnableHelpRequests = defaults.EnableHelpRequests;
        this.MaxPendingHelpRequestsPerNpc = defaults.MaxPendingHelpRequestsPerNpc;
        this.HelpRequestCooldownDays = defaults.HelpRequestCooldownDays;
        this.MinRelationshipTrustForHelpRequests = defaults.MinRelationshipTrustForHelpRequests;
        this.MinHelpRequestFriendshipReward = defaults.MinHelpRequestFriendshipReward;
        this.MaxHelpRequestFriendshipReward = defaults.MaxHelpRequestFriendshipReward;
        this.EnableAiDialogueFriendship = defaults.EnableAiDialogueFriendship;
        this.MaxAiDialogueFriendshipPerNpcPerDay = defaults.MaxAiDialogueFriendshipPerNpcPerDay;
        this.EnableDialogueFollowUps = defaults.EnableDialogueFollowUps;
        this.EnableDialogueDrivenBehaviors = defaults.EnableDialogueDrivenBehaviors;
        this.MaxDialogueBehaviorInfluenceDays = defaults.MaxDialogueBehaviorInfluenceDays;
        this.EnableAiWorldActions = defaults.EnableAiWorldActions;
        this.AllowAiSmallGifts = defaults.AllowAiSmallGifts;
        this.AllowAiMeaningfulGifts = defaults.AllowAiMeaningfulGifts;
        this.AiMeaningfulGiftCooldownDays = defaults.AiMeaningfulGiftCooldownDays;
        this.AiDailyGiftChanceMinPercent = defaults.AiDailyGiftChanceMinPercent;
        this.AiDailyGiftChanceMaxPercent = defaults.AiDailyGiftChanceMaxPercent;
        this.AllowAiMoneyGifts = defaults.AllowAiMoneyGifts;
        this.MaxAiMoneyGiftAmount = defaults.MaxAiMoneyGiftAmount;
        this.AllowAiFarmHelp = defaults.AllowAiFarmHelp;
        this.MaxAiWateredTilesPerAction = defaults.MaxAiWateredTilesPerAction;
        this.AllowAiWalkTogether = defaults.AllowAiWalkTogether;
        this.MaxAiWalkTogetherMinutes = defaults.MaxAiWalkTogetherMinutes;
        this.AllowAiEscortToLocation = defaults.AllowAiEscortToLocation;
        this.AllowAiFestivalInteractions = defaults.AllowAiFestivalInteractions;
        this.AllowAiQuestAssists = defaults.AllowAiQuestAssists;
        this.EnableNpcState = defaults.EnableNpcState;
        this.NpcStateDailyDecay = defaults.NpcStateDailyDecay;
        this.NpcEmotionDailyDecay = defaults.NpcEmotionDailyDecay;
        this.NpcConflictDailyDecay = defaults.NpcConflictDailyDecay;
        this.EnablePassiveBehaviors = defaults.EnablePassiveBehaviors;
        this.PassiveBehaviorChancePercent = defaults.PassiveBehaviorChancePercent;
        this.MaxBehaviorsPerNpcPerDay = defaults.MaxBehaviorsPerNpcPerDay;
        this.MaxInteractionDistanceTiles = defaults.MaxInteractionDistanceTiles;
        this.AllowFacePlayer = defaults.AllowFacePlayer;
        this.AllowEmotes = defaults.AllowEmotes;
        this.AllowApproachPlayer = defaults.AllowApproachPlayer;
        this.EnableAiPlanner = defaults.EnableAiPlanner;
        this.AiPlannerEndpoint = defaults.AiPlannerEndpoint;
        this.AiPlannerApiKey = defaults.AiPlannerApiKey;
        this.AiPlannerModel = defaults.AiPlannerModel;
        this.AiPlannerTimeoutSeconds = defaults.AiPlannerTimeoutSeconds;
        this.EnableValleyTalkPromptBridge = defaults.EnableValleyTalkPromptBridge;
        this.ShowHudMessages = defaults.ShowHudMessages;
    }
}
