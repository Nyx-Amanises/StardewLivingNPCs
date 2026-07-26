using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 内容层装配（WP15 §4.1/§4.10）：注册资产提供与失效事件（一次、静态，不随 NPC 实例增挂）、
/// 挂 WP14 的旧配置导入器，并在 GameLaunched 后等待游戏语言就绪，再扫描内容包授权并创建 LLM 客户端。
/// 引擎开关为关时只保留资产事件与导入器（GMCM 照常可改，关→开需重启）。
/// </summary>
internal static class DialogueContentSetup
{
    private const int LocaleStartupFallbackSeconds = 20;

    private static IModHelper helper = null!;
    private static ModConfig config = null!;
    private static GameContentPipeline pipeline = null!;
    private static bool startupInitializationPending;
    private static int startupWaitSeconds;

    public static void Register(IModHelper modHelper, ModConfig modConfig)
    {
        helper = modHelper;
        config = modConfig;
        pipeline = new GameContentPipeline(modHelper);

        DialogueServices.Config.SyncFrom(modConfig);
        DialogueContentService.Instance = new DialogueContentService(pipeline);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
        helper.Events.Content.LocaleChanged += OnLocaleChanged;
        helper.Events.Content.LocaleChanged += OnLocaleChangedInvalidateCaches;
        helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;

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
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.stepFailed",
                    new { step = "persist the imported ValleyTalk config", error = ex.Message },
                    $"Failed to persist the imported ValleyTalk config: {ex.Message}"),
                LogLevel.Warn);
        }

        DialogueServices.Config.SyncFrom(config);
    }

    private static void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        // SMAPI raises GameLaunched before Stardew applies the saved game language. On a Chinese
        // install the locale is still "en" here and changes to "zh" a few seconds later, so any
        // startup diagnostics resolved now are permanently logged in English. Wait for the locale
        // event; English installs use the timed fallback below because their locale may not change.
        startupInitializationPending = true;
        startupWaitSeconds = 0;
    }

    private static void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
    {
        InitializeAfterLocaleReady();
    }

    /// <summary>
    /// 常驻的语言切换失效订阅（§4.1 补线）。与上面的一次性启动等待订阅分离：那个在初始化后
    /// 注销，这个伴随整个进程。SMAPI 只在 mod 主动 InvalidateCache 时才触发 AssetsInvalidated，
    /// 游戏语言切换只有本事件可依赖，而传记/世界摘要/提示词缓存与引擎台词样本库都含 locale
    /// 敏感内容（切语言后不清会陈旧到重启）。
    /// </summary>
    private static void OnLocaleChangedInvalidateCaches(object? sender, LocaleChangedEventArgs e)
    {
        InvalidateLocaleSensitiveCaches(DialogueContentService.Instance, DialogueEngineHost.Instance);
    }

    /// <summary>
    /// 语言切换失效动作（可测缝）：内容服务全量失效（传记缓存 + 四槽世界摘要 + 提示词表），
    /// 加引擎台词样本库重建。肖像语义缓存不在此列——其纹理缓存不含 locale 数据，快照缓存自带
    /// locale 比对。未装配的一侧（null）跳过。
    /// </summary>
    internal static void InvalidateLocaleSensitiveCaches(DialogueContentService? service, DialogueEngine? engine)
    {
        service?.InvalidateAll();
        engine?.InvalidateSampleCaches();
    }

    private static void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        if (!startupInitializationPending || ++startupWaitSeconds < LocaleStartupFallbackSeconds)
        {
            return;
        }

        InitializeAfterLocaleReady();
    }

    private static void InitializeAfterLocaleReady()
    {
        if (!startupInitializationPending)
        {
            return;
        }

        startupInitializationPending = false;
        helper.Events.Content.LocaleChanged -= OnLocaleChanged;
        helper.Events.GameLoop.OneSecondUpdateTicked -= OnOneSecondUpdateTicked;
        ThirdPartyContentPolicy.Scan(helper.ModRegistry);

        if (!config.EnableDialogueEngine)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.engineDisabledConfig",
                    null,
                    "The AI dialogue engine is disabled in the config; LLM client setup, Harmony patches, schedulers and dialogue commands stay off (enable it in GMCM and restart)."),
                LogLevel.Info);
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
        ContentInvalidationPlan plan = ClassifyInvalidatedAssets(
            e.NamesWithoutLocale.Select(name => name.Name));
        ApplyInvalidationPlan(plan, DialogueContentService.Instance, DialogueEngineHost.Instance);
    }

    /// <summary>一次 AssetsInvalidated 批次映射出的缓存失效动作集（纯数据，单测钉规则）。</summary>
    internal sealed class ContentInvalidationPlan
    {
        public bool Portraits { get; set; }

        public bool Prompts { get; set; }

        public bool WorldSummaries { get; set; }

        public bool DialogueSamples { get; set; }

        public List<string> BioNames { get; } = new();
    }

    /// <summary>
    /// 纯函数：失效资产基名（SMAPI 已剥 locale 并把分隔符归一为 '/'）→ 失效计划。
    /// 引擎台词样本库以 NPC 当前对话与净版 Characters/Dialogue 资产为源、并合并传记的
    /// Dialogue 字典，因此这两类资产失效都要求样本库重建（InvalidateSampleCaches 的
    /// WP15 事件接线，此前缺失导致样本一次捕获终身冻结）。
    /// </summary>
    internal static ContentInvalidationPlan ClassifyInvalidatedAssets(IEnumerable<string> assetBaseNames)
    {
        var plan = new ContentInvalidationPlan();
        foreach (string rawName in assetBaseNames)
        {
            string name = (rawName ?? string.Empty).Replace('\\', '/').Trim();
            if (name.StartsWith("Portraits/", StringComparison.OrdinalIgnoreCase))
            {
                plan.Portraits = true;
            }
            else if (name.StartsWith("Characters/Dialogue/", StringComparison.OrdinalIgnoreCase))
            {
                plan.DialogueSamples = true;
            }
            else if (string.Equals(name, ContentAssetNames.Prompts, StringComparison.OrdinalIgnoreCase))
            {
                plan.Prompts = true;
            }
            else if (string.Equals(name, ContentAssetNames.GameSummary, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ContentAssetNames.GameSummaryOptimized, StringComparison.OrdinalIgnoreCase))
            {
                plan.WorldSummaries = true;
            }
            else if (name.StartsWith(ContentAssetNames.BiosPrefix, StringComparison.OrdinalIgnoreCase))
            {
                plan.BioNames.Add(name[ContentAssetNames.BiosPrefix.Length..]);
                plan.DialogueSamples = true;
            }
        }

        return plan;
    }

    /// <summary>执行失效计划；未装配的一侧（null）对应动作跳过。</summary>
    internal static void ApplyInvalidationPlan(
        ContentInvalidationPlan plan,
        DialogueContentService? service,
        DialogueEngine? engine)
    {
        if (plan.Portraits)
        {
            DialogueEngineHost.InvalidatePortraitCaches();
        }

        if (plan.DialogueSamples)
        {
            engine?.InvalidateSampleCaches();
        }

        if (service == null)
        {
            return;
        }

        if (plan.Prompts)
        {
            service.InvalidatePrompts();
        }

        if (plan.WorldSummaries)
        {
            service.InvalidateWorldSummaries();
        }

        foreach (string npcName in plan.BioNames)
        {
            service.InvalidateBio(npcName);
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
                MergeLocalizedPrompts(table, zh);
            }
        }

        return table;
    }

    /// <summary>
    /// 将本地化提示词逐键覆盖到默认表。若本地化文件提供了基础键但没有显式提供对应的 NPC 性别变体，
    /// 则移除默认语言中的性别变体，使查找正确回退到本地化基础键，而不是重新命中默认语言旧文案。
    /// </summary>
    internal static void MergeLocalizedPrompts(
        Dictionary<string, string> table,
        IReadOnlyDictionary<string, string> localized)
    {
        foreach (string key in localized.Keys)
        {
            if (key.EndsWith(".MaleNpc", StringComparison.Ordinal)
                || key.EndsWith(".FemaleNpc", StringComparison.Ordinal))
            {
                continue;
            }

            string maleKey = key + ".MaleNpc";
            string femaleKey = key + ".FemaleNpc";
            if (!localized.ContainsKey(maleKey))
            {
                table.Remove(maleKey);
            }

            if (!localized.ContainsKey(femaleKey))
            {
                table.Remove(femaleKey);
            }
        }

        foreach (var pair in localized)
        {
            table[pair.Key] = pair.Value;
        }
    }

    private static WorldSummary LoadWorldFile(bool optimized)
    {
        string relative = $"{ContentAssetNames.WorldDir}/{(optimized ? "GameSummaryOptimized" : "GameSummary")}.json";
        WorldSummary? summary = pipeline.ReadJsonFile<WorldSummary>(relative);
        if (summary == null)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.assetLoadFailed",
                    new { asset = relative, error = "the file is missing or unreadable" },
                    $"Failed to load content asset '{relative}': the file is missing or unreadable"),
                LogLevel.Error);
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
