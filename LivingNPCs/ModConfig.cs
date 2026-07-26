using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace LivingNPCs;

internal sealed class ModConfig
{
    public const int MaxHelpRequestDailyOfferChancePercent = 60;
    public const int MaxAiDailyGiftChancePercent = 25;

    /// <summary>TypedResponses 的合法值（WP15 §3.1，兼容契约按原样保留）。</summary>
    public static readonly string[] TypedResponsesValues = { "Always", "With Generated", "Never" };

    public bool EnableMod { get; set; } = true;
    public bool Debug { get; set; } = false;
    public KeybindList BehaviorHotkey { get; set; } = KeybindList.Parse("LeftShift + H");
    public KeybindList InspectMemoryHotkey { get; set; } = KeybindList.Parse("LeftShift + J");
    public string ManualBehaviorMode { get; set; } = "Auto";
    public int ManualEmoteId { get; set; } = 16;
    public int MaxMemoryEntriesPerNpc { get; set; } = 20;
    public int PromptMemoryEntries { get; set; } = 4;
    public bool EnableConversationMemory { get; set; } = true;

    /// <summary>
    /// 多人同步总开关（隐藏项，v1 主机权威）：farmhand 上报交换给主机统一入账、主机回发
    /// NPC 心智镜像。关闭后 farmhand 回到旧行为（本地临时记忆，不上报、不落盘）。
    /// </summary>
    public bool EnableMultiplayerSync { get; set; } = true;

    public bool EnableHelpRequests { get; set; } = true;
    public int MaxPendingHelpRequestsPerNpc { get; set; } = 1;
    public int HelpRequestCooldownDays { get; set; } = 3;
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
    public bool AllowOutingSideBySideStroll { get; set; } = true;
    public bool AllowOutingSmallTalk { get; set; } = true;
    public bool AllowAiFestivalInteractions { get; set; } = true;
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
    /// <summary>把行为系统上下文（记忆摘要/礼物机会/求助机会/出游段）注入 AI 对话生成
    /// （WP16 裁决 5：原 EnableValleyTalkPromptBridge 改名，Migrate 搬旧键值）。</summary>
    public bool EnableBehaviorContextInDialogue { get; set; } = true;

    /// <summary>旧配置键（0.1.x）：仅用于反序列化迁移，Migrate 后清空、不再写出。</summary>
    [Newtonsoft.Json.JsonProperty("EnableValleyTalkPromptBridge", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    private bool? legacyEnableValleyTalkPromptBridge;

    public bool ConcisePromptContext { get; set; } = false;
    public bool ShowHudMessages { get; set; } = true;

    // AI-written gift mail (reciprocal / birthday / help-request reward). Generated through the
    // built-in dialogue engine when the mail is triggered; the i18n templates remain the fallback.
    // Hand-edit in config.json (intentionally not surfaced in GMCM).
    public bool EnableAiGiftMail { get; set; } = true;
    public int AiGiftMailTimeoutSeconds { get; set; } = 30;

    // Compress long-term memories evicted by the 24-entry cap into a biographical "relationship
    // impression" with one LLM call through the built-in dialogue engine; the impression is
    // injected as fixed relationship background. Hand-edit in config.json (intentionally not
    // surfaced in GMCM).
    public bool EnableMemoryImpressions { get; set; } = true;
    public int MemoryImpressionTimeoutSeconds { get; set; } = 45;

    // —— 对话引擎字段（WP15 §3.1；平铺进现有类，旧 ValleyTalk config.json 由 WP14 迁移）——

    /// <summary>旧 EnableMod（引擎总开关）改名而来：与 LivingNPCs 的 EnableMod（全 mod 总开关）区分。关→开需重启。</summary>
    public bool EnableDialogueEngine { get; set; } = true;
    public bool ExportAiResponseLogs { get; set; } = true;

    /// <summary>LLM 提供商 ID（合法值见 WP11 提供商注册表，大小写不敏感）。新装默认 OpenAiCompatible（WP11 裁决 6）。</summary>
    public string Provider { get; set; } = "OpenAiCompatible";

    /// <summary>敏感：不得写入任何日志/导出文件。</summary>
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = string.Empty;
    public string PromptFormat { get; set; } = "[INST] {system}\n{prompt}[/INST]\n{response_start}";
    public int QueryTimeout { get; set; } = 85;
    public bool ApplyTranslation { get; set; } = true;
    public int GeneralFrequency { get; set; } = 4;
    public int MarriageFrequency { get; set; } = 4;
    public int GiftFrequency { get; set; } = 4;
    public bool GenerateAiForNormalRightClick { get; set; } = false;
    public string TypedResponses { get; set; } = "Always";
    public SButton InitiateTypedDialogueKey { get; set; } = SButton.LeftAlt;
    /// <summary>允许对睡觉中的 NPC 发起主动搭话（叫醒对方，提示词会带"刚被叫醒"情境）。</summary>
    public bool AllowWakeSleepingNpc { get; set; } = true;
    public bool UseOptimizedPrompts { get; set; } = false;
    public bool EnableSemanticContextRouting { get; set; } = true;
    public int SemanticContextRoutingTimeoutSeconds { get; set; } = 8;
    public string RoutingThinkingLevel { get; set; } = "Off";
    public string ChatThinkingLevel { get; set; } = "Auto";
    public bool SuppressConnectionCheck { get; set; } = false;

    /// <summary>旧 config.json 是否已迁移（WP14 裁决 5）。不进 GMCM。</summary>
    public bool LegacyConfigImported { get; set; } = false;

    private string disableCharacters = string.Empty;

    /// <summary>禁用 AI 对话的 NPC 名单，逗号/空格分隔；setter 同步解析成 Title Case 内部列表。</summary>
    public string DisableCharacters
    {
        get => this.disableCharacters;
        set
        {
            this.disableCharacters = value ?? string.Empty;
            this.DisabledCharacterList = ParseDisabledCharacters(this.disableCharacters);
        }
    }

    [Newtonsoft.Json.JsonIgnore]
    public IReadOnlyList<string> DisabledCharacterList { get; private set; } = Array.Empty<string>();

    internal static IReadOnlyList<string> ParseDisabledCharacters(string raw)
    {
        TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
        return raw
            .Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => textInfo.ToTitleCase(name.ToLowerInvariant()))
            .ToList();
    }

    public bool Migrate()
    {
        bool changed = false;
        if (this.BehaviorHotkey.ToString().Equals("B", StringComparison.OrdinalIgnoreCase))
        {
            this.BehaviorHotkey = KeybindList.Parse("LeftShift + H");
            changed = true;
        }

        if (this.legacyEnableValleyTalkPromptBridge.HasValue)
        {
            this.EnableBehaviorContextInDialogue = this.legacyEnableValleyTalkPromptBridge.Value;
            this.legacyEnableValleyTalkPromptBridge = null;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Clamps hand-editable numeric settings into safe ranges so a malformed config.json
    /// (negative values, absurd magnitudes, inverted min/max pairs) cannot destabilize the game.
    /// </summary>
    /// <returns><c>true</c> if any value was adjusted and the config should be rewritten.</returns>
    public bool Validate()
    {
        bool changed = false;

        // These are intentionally always-on behaviors. The old settings remain deserializable
        // for config compatibility, but are no longer exposed in GMCM.
        if (!this.EnableMod)
        {
            this.EnableMod = true;
            changed = true;
        }

        if (!this.ShowHudMessages)
        {
            this.ShowHudMessages = true;
            changed = true;
        }

        if (!this.AllowWakeSleepingNpc)
        {
            this.AllowWakeSleepingNpc = true;
            changed = true;
        }

        if (!this.ApplyTranslation)
        {
            this.ApplyTranslation = true;
            changed = true;
        }

        if (!this.AllowAiFestivalInteractions)
        {
            this.AllowAiFestivalInteractions = true;
            changed = true;
        }

        if (!string.Equals(this.TypedResponses, "Always", StringComparison.Ordinal))
        {
            this.TypedResponses = "Always";
            changed = true;
        }

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
        this.MemoryImpressionTimeoutSeconds = Clamp(this.MemoryImpressionTimeoutSeconds, 10, 180);

        // 对话引擎字段（WP15 §3.1/§4.9：超时与档位钳制、思考档位归一）。
        this.QueryTimeout = Clamp(this.QueryTimeout, 5, 180);
        this.SemanticContextRoutingTimeoutSeconds = Clamp(this.SemanticContextRoutingTimeoutSeconds, 2, 30);
        this.GeneralFrequency = Clamp(this.GeneralFrequency, 0, 4);
        this.MarriageFrequency = Clamp(this.MarriageFrequency, 0, 4);
        this.GiftFrequency = Clamp(this.GiftFrequency, 0, 4);

        string routing = Dialogue.Llm.LlmThinking.Normalize(this.RoutingThinkingLevel, Dialogue.Llm.LlmThinking.Off);
        if (!string.Equals(routing, this.RoutingThinkingLevel, StringComparison.Ordinal))
        {
            this.RoutingThinkingLevel = routing;
            changed = true;
        }

        string chat = Dialogue.Llm.LlmThinking.Normalize(this.ChatThinkingLevel, Dialogue.Llm.LlmThinking.Auto);
        if (!string.Equals(chat, this.ChatThinkingLevel, StringComparison.Ordinal))
        {
            this.ChatThinkingLevel = chat;
            changed = true;
        }

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
        this.AllowOutingSideBySideStroll = defaults.AllowOutingSideBySideStroll;
        this.AllowOutingSmallTalk = defaults.AllowOutingSmallTalk;
        this.AllowAiFestivalInteractions = defaults.AllowAiFestivalInteractions;
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
        this.EnableBehaviorContextInDialogue = defaults.EnableBehaviorContextInDialogue;
        this.ConcisePromptContext = defaults.ConcisePromptContext;
        this.ShowHudMessages = defaults.ShowHudMessages;
        this.EnableAiGiftMail = defaults.EnableAiGiftMail;
        this.AiGiftMailTimeoutSeconds = defaults.AiGiftMailTimeoutSeconds;
        this.EnableMemoryImpressions = defaults.EnableMemoryImpressions;
        this.MemoryImpressionTimeoutSeconds = defaults.MemoryImpressionTimeoutSeconds;
    }

    /// <summary>
    /// 仅重置对话引擎字段（WP15 §4.9：引擎段 reset 不得动行为系统字段）。
    /// 有意保留 LegacyConfigImported（迁移只做一次）与 ApiKey 之外的一切敏感语义——
    /// ApiKey 属引擎字段，重置即清空。
    /// </summary>
    public void ResetDialogueEngineDefaults()
    {
        var defaults = new ModConfig();

        this.EnableDialogueEngine = defaults.EnableDialogueEngine;
        this.ExportAiResponseLogs = defaults.ExportAiResponseLogs;
        this.Provider = defaults.Provider;
        this.ApiKey = defaults.ApiKey;
        this.ModelName = defaults.ModelName;
        this.ServerAddress = defaults.ServerAddress;
        this.PromptFormat = defaults.PromptFormat;
        this.QueryTimeout = defaults.QueryTimeout;
        this.ApplyTranslation = defaults.ApplyTranslation;
        this.GeneralFrequency = defaults.GeneralFrequency;
        this.MarriageFrequency = defaults.MarriageFrequency;
        this.GiftFrequency = defaults.GiftFrequency;
        this.GenerateAiForNormalRightClick = defaults.GenerateAiForNormalRightClick;
        this.TypedResponses = defaults.TypedResponses;
        this.InitiateTypedDialogueKey = defaults.InitiateTypedDialogueKey;
        this.UseOptimizedPrompts = defaults.UseOptimizedPrompts;
        this.EnableSemanticContextRouting = defaults.EnableSemanticContextRouting;
        this.SemanticContextRoutingTimeoutSeconds = defaults.SemanticContextRoutingTimeoutSeconds;
        this.RoutingThinkingLevel = defaults.RoutingThinkingLevel;
        this.ChatThinkingLevel = defaults.ChatThinkingLevel;
        this.SuppressConnectionCheck = defaults.SuppressConnectionCheck;
        this.DisableCharacters = defaults.DisableCharacters;
    }
}
