using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Tests.Dialogue.Content;

/// <summary>WP15 测试用假内容管线：全链（提示词表/传记/世界摘要/回退）不碰游戏。</summary>
internal sealed class FakeContentPipeline : IContentPipeline
{
    public string CurrentLocale { get; set; } = "en";

    public string PlayerGenderKey { get; set; } = "Male";

    public bool IsSveLoaded { get; set; }

    public Dictionary<string, string> PromptData { get; set; } = new();

    public Exception? PromptLoadError { get; set; }

    public int PromptLoadCount { get; private set; }

    public Func<string, string>? Preprocessor { get; set; }

    public Dictionary<string, NpcBio?> Bios { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int BioLoadCount { get; private set; }

    public Dictionary<string, CharacterData> Characters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, (IReadOnlyList<string> Loved, IReadOnlyList<string> Hated)> GiftTastes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public WorldSummary? FullSummary { get; set; }

    public WorldSummary? OptimizedSummary { get; set; }

    public int WorldLoadCount { get; private set; }

    public WorldSummary? SveDelta { get; set; }

    public Dictionary<string, Dictionary<string, BioListEntry>>? RelationshipPatches { get; set; }

    public Dictionary<string, string> LoadPromptDictionary()
    {
        this.PromptLoadCount++;
        if (this.PromptLoadError != null)
        {
            throw this.PromptLoadError;
        }

        return new Dictionary<string, string>(this.PromptData);
    }

    public string PreprocessString(string text)
    {
        return this.Preprocessor == null ? text : this.Preprocessor(text);
    }

    public WorldSummary? LoadWorldSummaryAsset(bool optimized)
    {
        this.WorldLoadCount++;
        return optimized ? this.OptimizedSummary : this.FullSummary;
    }

    public NpcBio? LoadBioAsset(string normalizedName)
    {
        this.BioLoadCount++;
        return this.Bios.TryGetValue(normalizedName, out NpcBio? bio) ? bio : null;
    }

    public CharacterData? GetCharacterData(string npcName)
    {
        return this.Characters.TryGetValue(npcName, out CharacterData? data) ? data : null;
    }

    public (IReadOnlyList<string> Loved, IReadOnlyList<string> Hated)? GetGiftTasteNames(string npcName)
    {
        return this.GiftTastes.TryGetValue(npcName, out var tastes) ? tastes : null;
    }

    public WorldSummary? LoadSveWorldDelta(bool optimized)
    {
        return this.SveDelta;
    }

    public Dictionary<string, Dictionary<string, BioListEntry>>? LoadSveRelationshipPatches()
    {
        return this.RelationshipPatches;
    }
}
