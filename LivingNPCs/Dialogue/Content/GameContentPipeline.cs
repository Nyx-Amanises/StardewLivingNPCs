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
            DialogueServices.Monitor?.Log($"[LivingNPCs] Failed to load world summary asset '{assetName}': {ex.Message}", LogLevel.Error);
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
            return Game1.characterData != null && Game1.characterData.TryGetValue(npcName, out CharacterData? data)
                ? data
                : null;
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
            if (Game1.NPCGiftTastes == null || !Game1.NPCGiftTastes.TryGetValue(npcName, out string? data))
            {
                return null;
            }

            string[] segments = data.Split('/');
            if (segments.Length < 8)
            {
                return null;
            }

            return (ResolveItemNames(segments[1]), ResolveItemNames(segments[7]));
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
            DialogueServices.Monitor?.Log($"[LivingNPCs] Failed to parse '{relativePath}': {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    /// <summary>礼物段：空格分隔的物品 ID，负数是类别码（跳过），其余解析显示名。</summary>
    private static List<string> ResolveItemNames(string segment)
    {
        var names = new List<string>();
        foreach (string token in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
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
