using System;
using StardewModdingAPI.Utilities;

namespace LivingNPCs;

internal sealed class ModConfig
{
    public const int MaxHelpRequestDailyOfferChancePercent = 60;
    public const int MaxAiDailyGiftChancePercent = 25;

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
    public int HelpRequestDailyOfferChancePercent { get; set; } = 3;
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
    public int AiDailyGiftChanceMinPercent { get; set; } = 3;
    public int AiDailyGiftChanceMaxPercent { get; set; } = 5;
    public bool AllowAiMoneyGifts { get; set; } = true;
    public int MaxAiMoneyGiftAmount { get; set; } = 250;
    public bool AllowAiCompanionOutings { get; set; } = true;
    public int MinimumCompanionOutingStayMinutes { get; set; } = 60;
    public bool AllowAiFestivalInteractions { get; set; } = true;
    public bool AllowAiQuestAssists { get; set; } = true;
    public bool EnableNpcState { get; set; } = true;
    public int NpcStateDailyDecay { get; set; } = 12;
    public int NpcEmotionDailyDecay { get; set; } = 12;
    public int NpcConflictDailyDecay { get; set; } = 8;
    public bool EnablePassiveBehaviors { get; set; } = false;
    public int PassiveBehaviorChancePercent { get; set; } = 0;
    public int MaxBehaviorsPerNpcPerDay { get; set; } = 2;
    public int MaxInteractionDistanceTiles { get; set; } = 6;
    public bool AllowFacePlayer { get; set; } = true;
    public bool AllowEmotes { get; set; } = true;
    public bool AllowApproachPlayer { get; set; } = true;
    public bool EnableSveCompatibility { get; set; } = true;
    public bool EnableAiPlanner { get; set; } = false;
    public string AiPlannerEndpoint { get; set; } = "http://localhost:11434/v1/chat/completions";
    public string AiPlannerApiKey { get; set; } = string.Empty;
    public string AiPlannerModel { get; set; } = string.Empty;
    public int AiPlannerTimeoutSeconds { get; set; } = 8;
    public bool EnableValleyTalkPromptBridge { get; set; } = true;
    public bool ConcisePromptContext { get; set; } = false;
    public bool ShowHudMessages { get; set; } = true;

    // AI-written gift mail (reciprocal / birthday / help-request reward). Generated through ValleyTalk
    // when the mail is triggered; the i18n templates remain the fallback. Hand-edit in config.json
    // (intentionally not surfaced in GMCM).
    public bool EnableAiGiftMail { get; set; } = true;
    public int AiGiftMailTimeoutSeconds { get; set; } = 30;

    public bool Migrate()
    {
        if (this.BehaviorHotkey.ToString().Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            this.BehaviorHotkey = KeybindList.Parse("LeftShift + H");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Clamps hand-editable numeric settings into safe ranges so a malformed config.json
    /// (negative values, absurd magnitudes, inverted min/max pairs) cannot destabilize the game.
    /// </summary>
    /// <returns><c>true</c> if any value was adjusted and the config should be rewritten.</returns>
    public bool Validate()
    {
        bool changed = false;

        int Clamp(int value, int min, int max)
        {
            int clamped = value < min ? min : (value > max ? max : value);
            if (clamped != value)
            {
                changed = true;
            }

            return clamped;
        }

        this.ManualEmoteId = Clamp(this.ManualEmoteId, 0, 120);
        this.MaxMemoryEntriesPerNpc = Clamp(this.MaxMemoryEntriesPerNpc, 0, 1000);
        this.PromptMemoryEntries = Clamp(this.PromptMemoryEntries, 0, 100);
        this.MaxPendingHelpRequestsPerNpc = Clamp(this.MaxPendingHelpRequestsPerNpc, 0, 20);
        this.HelpRequestCooldownDays = Clamp(this.HelpRequestCooldownDays, 0, 112);
        this.MinRelationshipTrustForHelpRequests = Clamp(this.MinRelationshipTrustForHelpRequests, 0, 100);
        this.HelpRequestDailyOfferChancePercent = Clamp(this.HelpRequestDailyOfferChancePercent, 0, MaxHelpRequestDailyOfferChancePercent);
        this.MinHelpRequestFriendshipReward = Clamp(this.MinHelpRequestFriendshipReward, 0, 5000);
        this.MaxHelpRequestFriendshipReward = Clamp(this.MaxHelpRequestFriendshipReward, 0, 5000);
        this.MaxAiDialogueFriendshipPerNpcPerDay = Clamp(this.MaxAiDialogueFriendshipPerNpcPerDay, 0, 1000);
        this.MaxDialogueBehaviorInfluenceDays = Clamp(this.MaxDialogueBehaviorInfluenceDays, 0, 28);
        this.AiMeaningfulGiftCooldownDays = Clamp(this.AiMeaningfulGiftCooldownDays, 0, 112);
        this.AiDailyGiftChanceMinPercent = Clamp(this.AiDailyGiftChanceMinPercent, 0, MaxAiDailyGiftChancePercent);
        this.AiDailyGiftChanceMaxPercent = Clamp(this.AiDailyGiftChanceMaxPercent, 0, MaxAiDailyGiftChancePercent);
        this.MaxAiMoneyGiftAmount = Clamp(this.MaxAiMoneyGiftAmount, 0, 100000);
        this.MinimumCompanionOutingStayMinutes = Clamp(this.MinimumCompanionOutingStayMinutes, 0, 1200);
        this.NpcStateDailyDecay = Clamp(this.NpcStateDailyDecay, 0, 100);
        this.NpcEmotionDailyDecay = Clamp(this.NpcEmotionDailyDecay, 0, 100);
        this.NpcConflictDailyDecay = Clamp(this.NpcConflictDailyDecay, 0, 100);
        this.PassiveBehaviorChancePercent = Clamp(this.PassiveBehaviorChancePercent, 0, 100);
        this.MaxBehaviorsPerNpcPerDay = Clamp(this.MaxBehaviorsPerNpcPerDay, 0, 100);
        this.MaxInteractionDistanceTiles = Clamp(this.MaxInteractionDistanceTiles, 1, 128);
        this.AiPlannerTimeoutSeconds = Clamp(this.AiPlannerTimeoutSeconds, 1, 120);
        this.AiGiftMailTimeoutSeconds = Clamp(this.AiGiftMailTimeoutSeconds, 5, 120);

        if (this.MinHelpRequestFriendshipReward > this.MaxHelpRequestFriendshipReward)
        {
            this.MaxHelpRequestFriendshipReward = this.MinHelpRequestFriendshipReward;
            changed = true;
        }

        if (this.AiDailyGiftChanceMinPercent > this.AiDailyGiftChanceMaxPercent)
        {
            this.AiDailyGiftChanceMaxPercent = this.AiDailyGiftChanceMinPercent;
            changed = true;
        }

        return changed;
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
        this.HelpRequestDailyOfferChancePercent = defaults.HelpRequestDailyOfferChancePercent;
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
        this.AllowAiCompanionOutings = defaults.AllowAiCompanionOutings;
        this.MinimumCompanionOutingStayMinutes = defaults.MinimumCompanionOutingStayMinutes;
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
        this.EnableSveCompatibility = defaults.EnableSveCompatibility;
        this.EnableAiPlanner = defaults.EnableAiPlanner;
        this.AiPlannerEndpoint = defaults.AiPlannerEndpoint;
        this.AiPlannerApiKey = defaults.AiPlannerApiKey;
        this.AiPlannerModel = defaults.AiPlannerModel;
        this.AiPlannerTimeoutSeconds = defaults.AiPlannerTimeoutSeconds;
        this.EnableValleyTalkPromptBridge = defaults.EnableValleyTalkPromptBridge;
        this.ConcisePromptContext = defaults.ConcisePromptContext;
        this.ShowHudMessages = defaults.ShowHudMessages;
        this.EnableAiGiftMail = defaults.EnableAiGiftMail;
        this.AiGiftMailTimeoutSeconds = defaults.AiGiftMailTimeoutSeconds;
    }
}
