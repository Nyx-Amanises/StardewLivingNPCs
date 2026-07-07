namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 资产名与磁盘路径的全项目唯一定义点（WP15 §3.2/§5）。
/// 资产名可被第三方 Content Patcher 以 EditData 覆盖/扩充（生态扩展点，01 §4）。
/// </summary>
internal static class ContentAssetNames
{
    public const string Prefix = "Mods/Yuki.LivingNPCs/";

    public const string GameSummary = Prefix + "GameSummary";
    public const string GameSummaryOptimized = Prefix + "GameSummaryOptimized";
    public const string Prompts = Prefix + "Prompts";
    public const string BiosPrefix = Prefix + "Bios/";

    /// <summary>mod 文件夹内的默认数据根（相对 helper.DirectoryPath）。</summary>
    public const string AssetRoot = "assets/dialogue";

    public const string WorldDir = AssetRoot + "/world";
    public const string WorldSveDir = AssetRoot + "/world-sve";
    public const string BiosDir = AssetRoot + "/bios";
    public const string BiosZhDir = AssetRoot + "/bios-zh";
    public const string BiosSveDir = AssetRoot + "/bios-sve";
    public const string BiosSveZhDir = AssetRoot + "/bios-sve-zh";
    public const string PromptsDefaultFile = AssetRoot + "/prompts/default.json";
    public const string PromptsZhFile = AssetRoot + "/prompts/zh.json";
    public const string SveRelationshipPatchesFile = AssetRoot + "/sve-relationship-patches.json";
    public const string SveRelationshipPatchesZhFile = AssetRoot + "/sve-relationship-patches-zh.json";
}
