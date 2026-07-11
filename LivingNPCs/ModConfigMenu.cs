using GenericModConfigMenu;
using LivingNPCs.Dialogue.Content;
using StardewModdingAPI;

namespace LivingNPCs;

internal static class ModConfigMenu
{
    public static void Register(ModEntry modEntry, ModConfig config)
    {
        var configMenu = modEntry.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (configMenu == null)
        {
            modEntry.Monitor.Log(I18n.Get("log.gmcm.missing"), LogLevel.Info);
            return;
        }

        var manifest = modEntry.ModManifest;
        configMenu.Register(
            mod: manifest,
            reset: () =>
            {
                config.ResetToDefaults();
                DialogueConfigMenuSection.ResetEngineDefaults(config);
            },
            save: () =>
            {
                config.Validate();
                DialogueConfigMenuSection.OnSave(modEntry, config);
                modEntry.Helper.WriteConfig(config);
            }
        );

        // Keep the connection and dialogue controls at the top of the page, where players
        // normally need them. The remaining sections contain optional behaviour tuning.
        DialogueConfigMenuSection.Append(configMenu, modEntry, config);

        configMenu.AddSectionTitle(
            mod: manifest,
            text: () => I18n.Get("gmcm.section.basic")
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
