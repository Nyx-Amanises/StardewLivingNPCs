using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// <see cref="IContentPipeline"/> 的游戏实现：SMAPI 内容 API、Game1 数据、mod 文件夹磁盘文件。
/// 除此薄层外的全部内容逻辑都可脱离游戏单测。
/// </summary>
internal sealed class GameContentPipeline : IContentPipeline
{
    private readonly IModHelper helper;
    private readonly object communityBioRootsLock = new();
    private IReadOnlyList<string>? activeCommunityBioRoots;

    public GameContentPipeline(IModHelper helper)
    {
        this.helper = helper;
    }

    public string CurrentLocale
    {
        get
        {
            string locale = this.helper.GameContent.CurrentLocale;
            return string.IsNullOrWhiteSpace(locale) ? "en" : locale;
        }
    }

    public string PlayerGenderKey
    {
        get
        {
            try
            {
                return Game1.getPlayerOrEventFarmer()?.Gender.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public bool IsSveLoaded => this.helper.ModRegistry.IsLoaded(SveContentRules.SveUniqueId);

    public Dictionary<string, string> LoadPromptDictionary()
    {
        return this.helper.GameContent.Load<Dictionary<string, string>>(ContentAssetNames.Prompts);
    }

    public string PreprocessString(string text)
    {
        return Game1.content.PreprocessString(text);
    }

    public WorldSummary? LoadWorldSummaryAsset(bool optimized)
    {
        string assetName = optimized ? ContentAssetNames.GameSummaryOptimized : ContentAssetNames.GameSummary;
        try
        {
            return this.helper.GameContent.Load<WorldSummary>(assetName);
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.assetLoadFailed",
                    new { asset = assetName, error = ex.Message },
                    $"Failed to load content asset '{assetName}': {ex.Message}"),
                LogLevel.Error);
            return null;
        }
    }

    public NpcBio? LoadBioAsset(string normalizedName)
    {
        try
        {
            // LocalizedContentManager 自带 locale 后缀回退（第三方本地化传记的挂载点，§4.3.2）。
            return Game1.content.Load<NpcBio>(ContentAssetNames.BiosPrefix + normalizedName);
        }
        catch
        {
            return null;
        }
    }

    public CharacterData? GetCharacterData(string npcName)
    {
        try
        {
            if (Game1.characterData == null)
            {
                return null;
            }

            foreach (string candidate in SveContentRules.GetGameDataLookupNames(npcName))
            {
                if (Game1.characterData.TryGetValue(candidate, out CharacterData? data))
                {
                    return data;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public (IReadOnlyList<string> Loved, IReadOnlyList<string> Hated)? GetGiftTasteNames(string npcName)
    {
        try
        {
            if (Game1.NPCGiftTastes == null)
            {
                return null;
            }

            bool foundEntry = false;
            foreach (string candidate in SveContentRules.GetGameDataLookupNames(npcName))
            {
                if (!Game1.NPCGiftTastes.TryGetValue(candidate, out string? data))
                {
                    continue;
                }

                foundEntry = true;
                string[] segments = (data ?? string.Empty).Split('/');
                if (segments.Length < 8)
                {
                    // A malformed preferred entry should not prevent the next alias/canonical
                    // candidate from supplying valid data.
                    continue;
                }

                return (ResolveItemNames(segments[1]), ResolveItemNames(segments[7]));
            }

            // Many non-social, temporary, or content-pack NPCs intentionally have no personal
            // entry. That is a valid empty topic source; a present but malformed entry stays null.
            return foundEntry
                ? null
                : (Array.Empty<string>(), Array.Empty<string>());
        }
        catch
        {
            return null;
        }
    }

    public WorldSummary? LoadSveWorldDelta(bool optimized)
    {
        string relative = $"{ContentAssetNames.WorldSveDir}/{(optimized ? "GameSummaryOptimized" : "GameSummary")}.json";
        return this.ReadJsonFile<WorldSummary>(relative);
    }

    public Dictionary<string, Dictionary<string, BioListEntry>>? LoadSveRelationshipPatches()
    {
        string relative = this.CurrentLocale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? ContentAssetNames.SveRelationshipPatchesZhFile
            : ContentAssetNames.SveRelationshipPatchesFile;
        return this.ReadJsonFile<Dictionary<string, Dictionary<string, BioListEntry>>>(relative)
            ?? (relative == ContentAssetNames.SveRelationshipPatchesFile
                ? null
                : this.ReadJsonFile<Dictionary<string, Dictionary<string, BioListEntry>>>(ContentAssetNames.SveRelationshipPatchesFile));
    }

    /// <summary>读 mod 文件夹内 JSON；缺失/坏损返回 null（坏损记 Warn）。</summary>
    internal T? ReadJsonFile<T>(string relativePath)
        where T : class
    {
        string fullPath = Path.Combine(this.helper.DirectoryPath, relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(fullPath));
        }
        catch (Exception ex)
        {
            DialogueServices.Monitor?.Log(
                Util.GetConsoleString(
                    "dialogue.log.assetLoadFailed",
                    new { asset = relativePath, error = ex.Message },
                    $"Failed to load content asset '{relativePath}': {ex.Message}"),
                LogLevel.Warn);
            return null;
        }
    }

    /// <summary>
    /// 严格读取玩家/社区投放的完整传记：保留 Missing/Invalid/Valid 三态，
    /// 限制文件大小，并把 JSON 语法与契约严格校验交给纯逻辑加载器。
    /// </summary>
    internal CommunityBioLoader.BioFileReadResult ReadCommunityBioFile(string relativePath)
    {
        string fullPath = Path.Combine(this.helper.DirectoryPath, relativePath);
        if (!File.Exists(fullPath))
        {
            return CommunityBioLoader.BioFileReadResult.Missing();
        }

        try
        {
            long length = new FileInfo(fullPath).Length;
            if (length > CommunityBioLoader.MaxFileBytes)
            {
                return CommunityBioLoader.BioFileReadResult.Invalid(
                    $"file size {length} bytes exceeds {CommunityBioLoader.MaxFileBytes} bytes");
            }

            return CommunityBioLoader.ParseJson(File.ReadAllText(fullPath));
        }
        catch (FileNotFoundException)
        {
            // 文件可能在 File.Exists 与实际读取之间被移除；按 Missing 处理即可。
            return CommunityBioLoader.BioFileReadResult.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return CommunityBioLoader.BioFileReadResult.Missing();
        }
        catch (Exception ex)
        {
            return CommunityBioLoader.BioFileReadResult.Invalid($"cannot read file: {ex.Message}");
        }
    }

    /// <summary>
    /// 找出 <c>npc_bios/&lt;目标 Mod UniqueID&gt;/</c> 中已安装目标 Mod 的命名空间。
    /// Mod 列表与磁盘目录在一次游戏进程中都按“修改后重启”契约保持不变，因此结果可缓存。
    /// </summary>
    internal IReadOnlyList<string> GetActiveCommunityBioRoots()
    {
        lock (this.communityBioRootsLock)
        {
            if (this.activeCommunityBioRoots != null)
            {
                return this.activeCommunityBioRoots;
            }

            string rootPath = Path.Combine(this.helper.DirectoryPath, ContentAssetNames.CommunityBiosDir);
            if (!Directory.Exists(rootPath))
            {
                return this.activeCommunityBioRoots = Array.Empty<string>();
            }

            try
            {
                this.activeCommunityBioRoots = Directory.EnumerateDirectories(rootPath)
                    .Where(directory =>
                        Directory.Exists(Path.Combine(directory, "bios"))
                        || Directory.Exists(Path.Combine(directory, "locales")))
                    .Where(directory => this.helper.ModRegistry.IsLoaded(Path.GetFileName(directory)))
                    .Select(directory => Path.GetRelativePath(this.helper.DirectoryPath, directory).Replace('\\', '/'))
                    .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return this.activeCommunityBioRoots;
            }
            catch (Exception ex)
            {
                DialogueServices.Monitor?.Log(
                    Util.GetConsoleString(
                        "dialogue.log.assetLoadFailed",
                        new { asset = ContentAssetNames.CommunityBiosDir, error = ex.Message },
                        $"Failed to scan community biography directories: {ex.Message}"),
                    LogLevel.Warn);
                return this.activeCommunityBioRoots = Array.Empty<string>();
            }
        }
    }

    /// <summary>礼物段：空格分隔的物品 ID，负数是类别码（跳过），其余解析显示名。</summary>
    private static List<string> ResolveItemNames(string segment)
    {
        var names = new List<string>();
        foreach (string token in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (global::LivingNPCs.RsvAiPolicy.IsBlockedContentId(token))
            {
                continue;
            }

            if (int.TryParse(token, out int numeric) && numeric < 0)
            {
                continue;
            }

            string? name = ItemRegistry.GetData(token)?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }
}
