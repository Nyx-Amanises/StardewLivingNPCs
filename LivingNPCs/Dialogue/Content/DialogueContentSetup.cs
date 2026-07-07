using System;
using System.Collections.Generic;
using System.IO;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 内容层装配（WP15 §4.1/§4.10）：注册资产提供与失效事件（一次、静态，不随 NPC 实例增挂）、
/// 挂 WP14 的旧配置导入器、GameLaunched 时扫描内容包授权并按配置建一次 LLM 客户端。
/// 引擎开关为关时只保留资产事件与导入器（GMCM 照常可改，关→开需重启）。
/// </summary>
internal static class DialogueContentSetup
{
    private static IModHelper helper = null!;
    private static ModConfig config = null!;
    private static GameContentPipeline pipeline = null!;

    public static void Register(IModHelper modHelper, ModConfig modConfig)
    {
        helper = modHelper;
        config = modConfig;
        pipeline = new GameContentPipeline(modHelper);

        DialogueServices.Config.SyncFrom(modConfig);
        DialogueContentService.Instance = new DialogueContentService(pipeline);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;

        // WP14 的文件夹迁移器在 GameLaunched 移交旧 config（找没找到都通知，幂等靠 LegacyConfigImported）。
        LegacyFolderMigrator.ConfigImporter = legacy => ImportLegacyConfig(legacy);

        // 注册顺序在 DialoguePersistence.RegisterEvents 之后，保证先导入旧配置再建客户端。
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private static void ImportLegacyConfig(LegacyValleyTalkConfig? legacy)
    {
        if (!LegacyConfigImporter.Apply(legacy, config))
        {
            return;
        }

        config.Validate();
        try
        {
            helper.WriteConfig(config);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log($"Failed to persist the imported ValleyTalk config: {ex.Message}", LogLevel.Warn);
        }

        DialogueServices.Config.SyncFrom(config);
    }

    private static void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        ThirdPartyContentPolicy.Scan(helper.ModRegistry, DialogueServices.Monitor);

        if (!config.EnableDialogueEngine)
        {
            DialogueServices.Monitor?.Log("[LivingNPCs] The AI dialogue engine is disabled in the config; skipping LLM client setup.", LogLevel.Info);
            return;
        }

        if (DialoguePersistence.LegacyValleyTalkLoaded)
        {
            // 01 §5：旧 mod 还在加载时引擎保持关闭（错误提示已由持久化层给出）。
            return;
        }

        // §4.10：启动即按配置建一次 LLM 客户端（Provider 非法时由工厂记 Error 且引擎不启动）。
        LlmClientHost.Instance.ReplaceClient(LlmConnectionSettings.FromConfig(DialogueServices.Config));
    }

    // ---- 资产提供（§4.1）----

    private static void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(ContentAssetNames.Prompts))
        {
            e.LoadFrom(LoadPromptsFile, AssetLoadPriority.Medium);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo(ContentAssetNames.GameSummary))
        {
            e.LoadFrom(() => LoadWorldFile(optimized: false), AssetLoadPriority.Medium);
        }
        else if (e.NameWithoutLocale.IsEquivalentTo(ContentAssetNames.GameSummaryOptimized))
        {
            e.LoadFrom(() => LoadWorldFile(optimized: true), AssetLoadPriority.Medium);
        }
        else if (e.NameWithoutLocale.StartsWith(ContentAssetNames.BiosPrefix))
        {
            string npcName = e.NameWithoutLocale.Name[ContentAssetNames.BiosPrefix.Length..];
            e.LoadFrom(() => LoadBioFile(npcName), AssetLoadPriority.Medium);
        }
    }

    private static void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
    {
        DialogueContentService? service = DialogueContentService.Instance;
        if (service == null)
        {
            return;
        }

        foreach (IAssetName name in e.NamesWithoutLocale)
        {
            if (name.IsEquivalentTo(ContentAssetNames.Prompts))
            {
                service.InvalidatePrompts();
            }
            else if (name.IsEquivalentTo(ContentAssetNames.GameSummary)
                || name.IsEquivalentTo(ContentAssetNames.GameSummaryOptimized))
            {
                service.InvalidateWorldSummaries();
            }
            else if (name.StartsWith(ContentAssetNames.BiosPrefix))
            {
                service.InvalidateBio(name.Name[ContentAssetNames.BiosPrefix.Length..]);
            }
        }
    }

    /// <summary>Prompts 资产：default.json 为基，zh locale 逐键覆盖合并（§4.8）。</summary>
    private static Dictionary<string, string> LoadPromptsFile()
    {
        var table = pipeline.ReadJsonFile<Dictionary<string, string>>(ContentAssetNames.PromptsDefaultFile)
            ?? new Dictionary<string, string>();
        if (pipeline.CurrentLocale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            var zh = pipeline.ReadJsonFile<Dictionary<string, string>>(ContentAssetNames.PromptsZhFile);
            if (zh != null)
            {
                foreach (var pair in zh)
                {
                    table[pair.Key] = pair.Value;
                }
            }
        }

        return table;
    }

    private static WorldSummary LoadWorldFile(bool optimized)
    {
        string relative = $"{ContentAssetNames.WorldDir}/{(optimized ? "GameSummaryOptimized" : "GameSummary")}.json";
        WorldSummary? summary = pipeline.ReadJsonFile<WorldSummary>(relative);
        if (summary == null)
        {
            DialogueServices.Monitor?.Log($"[LivingNPCs] Default world summary file '{relative}' is missing or unreadable.", LogLevel.Error);
            return new WorldSummary();
        }

        return summary;
    }

    /// <summary>
    /// Bios/&lt;Name&gt; 资产：按（SVE 活跃 × zh locale）择目录，SVE 集优先、zh 变体优先（§4.4/§4.8）。
    /// 文件不存在返回空传记对象（触发 §4.3 回退链）。
    /// </summary>
    private static NpcBio LoadBioFile(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName) || npcName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return new NpcBio();
        }

        bool zh = pipeline.CurrentLocale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        bool sveActive = pipeline.IsSveLoaded && DialogueServices.Config.EnableSveCompatibility;

        var directories = new List<string>();
        if (sveActive)
        {
            if (zh)
            {
                directories.Add(ContentAssetNames.BiosSveZhDir);
            }

            directories.Add(ContentAssetNames.BiosSveDir);
        }

        if (zh)
        {
            directories.Add(ContentAssetNames.BiosZhDir);
        }

        directories.Add(ContentAssetNames.BiosDir);

        foreach (string directory in directories)
        {
            NpcBio? bio = pipeline.ReadJsonFile<NpcBio>($"{directory}/{npcName}.json");
            if (bio != null)
            {
                return bio;
            }
        }

        return new NpcBio();
    }
}
