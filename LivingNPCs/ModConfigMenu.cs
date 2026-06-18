using GenericModConfigMenu;
using StardewModdingAPI;

namespace LivingNPCs;

internal static class ModConfigMenu
{
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
            name: () => I18n.Get("gmcm.inspectMemoryHotkey.name"),
            tooltip: () => I18n.Get("gmcm.inspectMemoryHotkey.tooltip"),
            getValue: () => config.InspectMemoryHotkey,
            setValue: value => config.InspectMemoryHotkey = value
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.showHud.name"),
            tooltip: () => I18n.Get("gmcm.showHud.tooltip"),
            getValue: () => config.ShowHudMessages,
            setValue: value => config.ShowHudMessages = value
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
            name: () => I18n.Get("gmcm.helpDailyChance.name"),
            tooltip: () => I18n.Get("gmcm.helpDailyChance.tooltip"),
            getValue: () => config.HelpRequestDailyOfferChancePercent,
            setValue: value => config.HelpRequestDailyOfferChancePercent = value,
            min: 0,
            max: ModConfig.MaxHelpRequestDailyOfferChancePercent,
            interval: 1,
            formatValue: FormatPercent
        );

        configMenu.AddParagraph(
            mod: manifest,
            text: () => I18n.Get("gmcm.para.commands")
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

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.giftChanceMin.name"),
            tooltip: () => I18n.Get("gmcm.giftChanceMin.tooltip"),
            getValue: () => config.AiDailyGiftChanceMinPercent,
            setValue: value => config.AiDailyGiftChanceMinPercent = value,
            min: 0,
            max: ModConfig.MaxAiDailyGiftChancePercent,
            interval: 1,
            formatValue: FormatPercent
        );

        configMenu.AddNumberOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.giftChanceMax.name"),
            tooltip: () => I18n.Get("gmcm.giftChanceMax.tooltip"),
            getValue: () => config.AiDailyGiftChanceMaxPercent,
            setValue: value => config.AiDailyGiftChanceMaxPercent = value,
            min: 0,
            max: ModConfig.MaxAiDailyGiftChancePercent,
            interval: 1,
            formatValue: FormatPercent
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
            text: () => I18n.Get("gmcm.section.compat")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.sve.name"),
            tooltip: () => I18n.Get("gmcm.sve.tooltip"),
            getValue: () => config.EnableSveCompatibility,
            setValue: value => config.EnableSveCompatibility = value
        );

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.valleyTalk")
        );

        configMenu.AddBoolOption(
            mod: manifest,
            name: () => I18n.Get("gmcm.concisePrompt.name"),
            tooltip: () => I18n.Get("gmcm.concisePrompt.tooltip"),
            getValue: () => config.ConcisePromptContext,
            setValue: value => config.ConcisePromptContext = value
        );
    }

    private static string FormatPercent(int value)
    {
        return $"{value}%";
    }
}
