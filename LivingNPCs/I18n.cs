using StardewModdingAPI;

namespace LivingNPCs;

/// <summary>
/// Lightweight accessor for SMAPI translations so that static helpers and runtime services can
/// fetch localized, player-facing text without each needing an <see cref="ITranslationHelper"/>.
/// Initialized once from <c>ModEntry.Entry</c>. The player's chosen game language selects the file
/// (e.g. <c>i18n/zh.json</c>); anything missing falls back to <c>i18n/default.json</c> (English).
/// </summary>
internal static class I18n
{
    private static ITranslationHelper? translations;

    public static void Init(ITranslationHelper helper)
    {
        translations = helper;
    }

    /// <summary>Gets the localized string for <paramref name="key"/>, or the key itself if not initialized.</summary>
    public static string Get(string key)
    {
        return translations is null ? key : translations.Get(key).ToString();
    }

    /// <summary>Gets the localized string for <paramref name="key"/>, substituting <c>{{token}}</c> placeholders.</summary>
    public static string Get(string key, object tokens)
    {
        return translations is null ? key : translations.Get(key, tokens).ToString();
    }
}
