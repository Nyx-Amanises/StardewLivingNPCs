using System;

namespace LivingNPCs.Dialogue.Llm;

/// <summary>Android 平台判定（WP11 §4.4）：主游戏程序集名含 "Android"。</summary>
internal static class AndroidPlatform
{
    /// <summary>测试用假平台开关（验收要点 8）；null 走真实判定。</summary>
    internal static bool? OverrideForTests;

    private static bool? _detected;

    public static bool IsAndroid => OverrideForTests ?? (_detected ??= Detect());

    private static bool Detect()
    {
        try
        {
            string? assemblyName = typeof(StardewValley.Game1).Assembly.GetName().Name;
            return assemblyName?.Contains("Android", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }
}
