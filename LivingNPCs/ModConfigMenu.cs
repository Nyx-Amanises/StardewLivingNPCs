using GenericModConfigMenu;
using StardewModdingAPI;

namespace LivingNPCs;

internal static class ModConfigMenu
{
    private static readonly string[] ManualModes =
    [
        "Auto",
        "FacePlayer",
        "Emote",
        "ApproachPlayer",
        "Pause",
        "LookAround",
        "StepAway"
    ];

    public static void Register(ModEntry modEntry, ModConfig config)
    {
        var configMenu = modEntry.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu == null)
        {
            modEntry.Monitor.Log("Generic Mod Config Menu not installed. LivingNPCs will still work, but no in-game config menu will be shown.", LogLevel.Info);
            return;
        }

        var manifest = modEntry.ModManifest;
        configMenu.Register(
            mod: manifest,
            reset: config.ResetToDefaults,
            save: () =>
            {
                config.Validate();
                modEntry.Helper.WriteConfig(config);
            }
        );

        configMenu.AddParagraph(
            mod: manifest,
            text: () => I18n.Get("gmcm.para.loaded")
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.basic")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.enableMod.name"),
            tooltip: () => I18n.Get("gmcm.enableMod.tooltip"),
            getValue: () => config.EnableMod,
            setValue: value => config.EnableMod = value
        );

        configMenu.AddKeybindList(
            mod: manifest,
            name: () => I18n.Get("gmcm.behaviorHotkey.name"),
            tooltip: () => I18n.Get("gmcm.behaviorHotkey.tooltip"),
            getValue: () => config.BehaviorHotkey,
            setValue: value => config.BehaviorHotkey = value
        );

        configMenu.AddKeybindList(
            mod: manifest,
            name: () => I18n.Get("gmcm.inspectMemoryHotkey.name"),
            tooltip: () => I18n.Get("gmcm.inspectMemoryHotkey.tooltip"),
            getValue: () => config.InspectMemoryHotkey,
            setValue: value => config.InspectMemoryHotkey = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.debug.name"),
            tooltip: () => I18n.Get("gmcm.debug.tooltip"),
            getValue: () => config.Debug,
            setValue: value => config.Debug = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.showHud.name"),
            tooltip: () => I18n.Get("gmcm.showHud.tooltip"),
            getValue: () => config.ShowHudMessages,
            setValue: value => config.ShowHudMessages = value
        );

        configMenu.AddParagraph(
            mod: manifest,
            text: () => I18n.Get("gmcm.para.commands")
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.manualTest")
        );

        configMenu.AddTextOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.manualMode.name"),
            tooltip: () => I18n.Get("gmcm.manualMode.tooltip"),
            getValue: () => config.ManualBehaviorMode,
            setValue: value => config.ManualBehaviorMode = value,
            allowedValues: ManualModes,
            formatAllowedValue: FormatManualMode
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.manualEmote.name"),
            tooltip: () => I18n.Get("gmcm.manualEmote.tooltip"),
            getValue: () => config.ManualEmoteId,
            setValue: value => config.ManualEmoteId = value,
            min: 0,
            max: 40,
            interval: 1
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.memory")
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.maxMemory.name"),
            tooltip: () => I18n.Get("gmcm.maxMemory.tooltip"),
            getValue: () => config.MaxMemoryEntriesPerNpc,
            setValue: value => config.MaxMemoryEntriesPerNpc = value,
            min: 1,
            max: 100,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.promptMemory.name"),
            tooltip: () => I18n.Get("gmcm.promptMemory.tooltip"),
            getValue: () => config.PromptMemoryEntries,
            setValue: value => config.PromptMemoryEntries = value,
            min: 1,
            max: 20,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.conversationMemory.name"),
            tooltip: () => I18n.Get("gmcm.conversationMemory.tooltip"),
            getValue: () => config.EnableConversationMemory,
            setValue: value => config.EnableConversationMemory = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.helpRequests.name"),
            tooltip: () => I18n.Get("gmcm.helpRequests.tooltip"),
            getValue: () => config.EnableHelpRequests,
            setValue: value => config.EnableHelpRequests = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.maxPendingHelp.name"),
            tooltip: () => I18n.Get("gmcm.maxPendingHelp.tooltip"),
            getValue: () => config.MaxPendingHelpRequestsPerNpc,
            setValue: value => config.MaxPendingHelpRequestsPerNpc = value,
            min: 0,
            max: 4,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.helpCooldown.name"),
            tooltip: () => I18n.Get("gmcm.helpCooldown.tooltip"),
            getValue: () => config.HelpRequestCooldownDays,
            setValue: value => config.HelpRequestCooldownDays = value,
            min: 0,
            max: 14,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.helpMinTrust.name"),
            tooltip: () => I18n.Get("gmcm.helpMinTrust.tooltip"),
            getValue: () => config.MinRelationshipTrustForHelpRequests,
            setValue: value => config.MinRelationshipTrustForHelpRequests = value,
            min: 0,
            max: 100,
            interval: 5
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.helpDailyChance.name"),
            tooltip: () => I18n.Get("gmcm.helpDailyChance.tooltip"),
            getValue: () => config.HelpRequestDailyOfferChancePercent,
            setValue: value => config.HelpRequestDailyOfferChancePercent = value,
            min: 0,
            max: 100,
            interval: 5,
            formatValue: value => $"{value}%"
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.helpMinReward.name"),
            tooltip: () => I18n.Get("gmcm.helpMinReward.tooltip"),
            getValue: () => config.MinHelpRequestFriendshipReward,
            setValue: value => config.MinHelpRequestFriendshipReward = value,
            min: 0,
            max: 100,
            interval: 5
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.helpMaxReward.name"),
            tooltip: () => I18n.Get("gmcm.helpMaxReward.tooltip"),
            getValue: () => config.MaxHelpRequestFriendshipReward,
            setValue: value => config.MaxHelpRequestFriendshipReward = value,
            min: 0,
            max: 100,
            interval: 5
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.aiFriendship.name"),
            tooltip: () => I18n.Get("gmcm.aiFriendship.tooltip"),
            getValue: () => config.EnableAiDialogueFriendship,
            setValue: value => config.EnableAiDialogueFriendship = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.aiFriendshipCap.name"),
            tooltip: () => I18n.Get("gmcm.aiFriendshipCap.tooltip"),
            getValue: () => config.MaxAiDialogueFriendshipPerNpcPerDay,
            setValue: value => config.MaxAiDialogueFriendshipPerNpcPerDay = value,
            min: 0,
            max: 30,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.dialogueFollowUps.name"),
            tooltip: () => I18n.Get("gmcm.dialogueFollowUps.tooltip"),
            getValue: () => config.EnableDialogueFollowUps,
            setValue: value => config.EnableDialogueFollowUps = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.dialogueDrivenBehaviors.name"),
            tooltip: () => I18n.Get("gmcm.dialogueDrivenBehaviors.tooltip"),
            getValue: () => config.EnableDialogueDrivenBehaviors,
            setValue: value => config.EnableDialogueDrivenBehaviors = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.behaviorInfluenceDays.name"),
            tooltip: () => I18n.Get("gmcm.behaviorInfluenceDays.tooltip"),
            getValue: () => config.MaxDialogueBehaviorInfluenceDays,
            setValue: value => config.MaxDialogueBehaviorInfluenceDays = value,
            min: 1,
            max: 7,
            interval: 1
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.aiWorld")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.aiWorldActions.name"),
            tooltip: () => I18n.Get("gmcm.aiWorldActions.tooltip"),
            getValue: () => config.EnableAiWorldActions,
            setValue: value => config.EnableAiWorldActions = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.smallGifts.name"),
            tooltip: () => I18n.Get("gmcm.smallGifts.tooltip"),
            getValue: () => config.AllowAiSmallGifts,
            setValue: value => config.AllowAiSmallGifts = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.meaningfulGifts.name"),
            tooltip: () => I18n.Get("gmcm.meaningfulGifts.tooltip"),
            getValue: () => config.AllowAiMeaningfulGifts,
            setValue: value => config.AllowAiMeaningfulGifts = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.meaningfulCooldown.name"),
            tooltip: () => I18n.Get("gmcm.meaningfulCooldown.tooltip"),
            getValue: () => config.AiMeaningfulGiftCooldownDays,
            setValue: value => config.AiMeaningfulGiftCooldownDays = value,
            min: 1,
            max: 28,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.giftChanceMin.name"),
            tooltip: () => I18n.Get("gmcm.giftChanceMin.tooltip"),
            getValue: () => config.AiDailyGiftChanceMinPercent,
            setValue: value => config.AiDailyGiftChanceMinPercent = value,
            min: 0,
            max: 100,
            interval: 5
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.giftChanceMax.name"),
            tooltip: () => I18n.Get("gmcm.giftChanceMax.tooltip"),
            getValue: () => config.AiDailyGiftChanceMaxPercent,
            setValue: value => config.AiDailyGiftChanceMaxPercent = value,
            min: 0,
            max: 100,
            interval: 5
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.moneyGifts.name"),
            tooltip: () => I18n.Get("gmcm.moneyGifts.tooltip"),
            getValue: () => config.AllowAiMoneyGifts,
            setValue: value => config.AllowAiMoneyGifts = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.moneyCap.name"),
            tooltip: () => I18n.Get("gmcm.moneyCap.tooltip"),
            getValue: () => config.MaxAiMoneyGiftAmount,
            setValue: value => config.MaxAiMoneyGiftAmount = value,
            min: 25,
            max: 1000,
            interval: 25
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.farmHelp.name"),
            tooltip: () => I18n.Get("gmcm.farmHelp.tooltip"),
            getValue: () => config.AllowAiFarmHelp,
            setValue: value => config.AllowAiFarmHelp = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.waterTiles.name"),
            tooltip: () => I18n.Get("gmcm.waterTiles.tooltip"),
            getValue: () => config.MaxAiWateredTilesPerAction,
            setValue: value => config.MaxAiWateredTilesPerAction = value,
            min: 1,
            max: 24,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.companionOutings.name"),
            tooltip: () => I18n.Get("gmcm.companionOutings.tooltip"),
            getValue: () => config.AllowAiCompanionOutings,
            setValue: value => config.AllowAiCompanionOutings = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.outingStay.name"),
            tooltip: () => I18n.Get("gmcm.outingStay.tooltip"),
            getValue: () => config.MinimumCompanionOutingStayMinutes,
            setValue: value => config.MinimumCompanionOutingStayMinutes = value,
            min: 120,
            max: 600,
            interval: 30
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.festival.name"),
            tooltip: () => I18n.Get("gmcm.festival.tooltip"),
            getValue: () => config.AllowAiFestivalInteractions,
            setValue: value => config.AllowAiFestivalInteractions = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.questAssist.name"),
            tooltip: () => I18n.Get("gmcm.questAssist.tooltip"),
            getValue: () => config.AllowAiQuestAssists,
            setValue: value => config.AllowAiQuestAssists = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.npcState")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.npcState.name"),
            tooltip: () => I18n.Get("gmcm.npcState.tooltip"),
            getValue: () => config.EnableNpcState,
            setValue: value => config.EnableNpcState = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.stateDecay.name"),
            tooltip: () => I18n.Get("gmcm.stateDecay.tooltip"),
            getValue: () => config.NpcStateDailyDecay,
            setValue: value => config.NpcStateDailyDecay = value,
            min: 0,
            max: 50,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.emotionDecay.name"),
            tooltip: () => I18n.Get("gmcm.emotionDecay.tooltip"),
            getValue: () => config.NpcEmotionDailyDecay,
            setValue: value => config.NpcEmotionDailyDecay = value,
            min: 0,
            max: 50,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.conflictDecay.name"),
            tooltip: () => I18n.Get("gmcm.conflictDecay.tooltip"),
            getValue: () => config.NpcConflictDailyDecay,
            setValue: value => config.NpcConflictDailyDecay = value,
            min: 0,
            max: 50,
            interval: 1
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.behavior")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.passive.name"),
            tooltip: () => I18n.Get("gmcm.passive.tooltip"),
            getValue: () => config.EnablePassiveBehaviors,
            setValue: value => config.EnablePassiveBehaviors = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.passiveChance.name"),
            tooltip: () => I18n.Get("gmcm.passiveChance.tooltip"),
            getValue: () => config.PassiveBehaviorChancePercent,
            setValue: value => config.PassiveBehaviorChancePercent = value,
            min: 0,
            max: 100,
            interval: 1,
            formatValue: value => $"{value}%"
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.maxBehaviors.name"),
            tooltip: () => I18n.Get("gmcm.maxBehaviors.tooltip"),
            getValue: () => config.MaxBehaviorsPerNpcPerDay,
            setValue: value => config.MaxBehaviorsPerNpcPerDay = value,
            min: 0,
            max: 20,
            interval: 1
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.interactionDistance.name"),
            tooltip: () => I18n.Get("gmcm.interactionDistance.tooltip"),
            getValue: () => config.MaxInteractionDistanceTiles,
            setValue: value => config.MaxInteractionDistanceTiles = value,
            min: 1,
            max: 20,
            interval: 1
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.allowFace.name"),
            tooltip: () => I18n.Get("gmcm.allowFace.tooltip"),
            getValue: () => config.AllowFacePlayer,
            setValue: value => config.AllowFacePlayer = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.allowEmote.name"),
            tooltip: () => I18n.Get("gmcm.allowEmote.tooltip"),
            getValue: () => config.AllowEmotes,
            setValue: value => config.AllowEmotes = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.allowApproach.name"),
            tooltip: () => I18n.Get("gmcm.allowApproach.tooltip"),
            getValue: () => config.AllowApproachPlayer,
            setValue: value => config.AllowApproachPlayer = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.compat")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.sve.name"),
            tooltip: () => I18n.Get("gmcm.sve.tooltip"),
            getValue: () => config.EnableSveCompatibility,
            setValue: value => config.EnableSveCompatibility = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.rsv.name"),
            tooltip: () => I18n.Get("gmcm.rsv.tooltip"),
            getValue: () => config.EnableRsvCompatibility,
            setValue: value => config.EnableRsvCompatibility = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.valleyTalk")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.bridge.name"),
            tooltip: () => I18n.Get("gmcm.bridge.tooltip"),
            getValue: () => config.EnableValleyTalkPromptBridge,
            setValue: value => config.EnableValleyTalkPromptBridge = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.aiPlanner")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.aiPlanner.name"),
            tooltip: () => I18n.Get("gmcm.aiPlanner.tooltip"),
            getValue: () => config.EnableAiPlanner,
            setValue: value => config.EnableAiPlanner = value
        );

        configMenu.AddTextOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.plannerEndpoint.name"),
            tooltip: () => I18n.Get("gmcm.plannerEndpoint.tooltip"),
            getValue: () => config.AiPlannerEndpoint,
            setValue: value => config.AiPlannerEndpoint = value
        );

        configMenu.AddTextOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.plannerModel.name"),
            tooltip: () => I18n.Get("gmcm.plannerModel.tooltip"),
            getValue: () => config.AiPlannerModel,
            setValue: value => config.AiPlannerModel = value
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.plannerTimeout.name"),
            tooltip: () => I18n.Get("gmcm.plannerTimeout.tooltip"),
            getValue: () => config.AiPlannerTimeoutSeconds,
            setValue: value => config.AiPlannerTimeoutSeconds = value,
            min: 1,
            max: 60,
            interval: 1,
            formatValue: value => $"{value}s"
        );
    }

    private static string FormatManualMode(string value)
    {
        return value switch
        {
            "Auto" => I18n.Get("gmcm.manualMode.auto"),
            "FacePlayer" => I18n.Get("gmcm.manualMode.facePlayer"),
            "Emote" => I18n.Get("gmcm.manualMode.emote"),
            "ApproachPlayer" => I18n.Get("gmcm.manualMode.approachPlayer"),
            "Pause" => I18n.Get("gmcm.manualMode.pause"),
            "LookAround" => I18n.Get("gmcm.manualMode.lookAround"),
            "StepAway" => I18n.Get("gmcm.manualMode.stepAway"),
            _ => value
        };
    }
}
