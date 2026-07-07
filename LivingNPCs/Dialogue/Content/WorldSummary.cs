using System.Collections.Generic;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// GameSummary / GameSummaryOptimized 资产形状（WP15 §3.3，字段名为兼容契约）。
/// 七节固定：Intro、FarmerBackground、Seasons、Locations、Festivals、Villagers、Outro。
/// SectionOrder 的键序决定输出顺序（Json.NET 反序列化保持文档序），布尔值 = 是否输出该节标题行。
/// </summary>
internal sealed class WorldSummary
{
    public Dictionary<string, bool> SectionOrder { get; set; } = new();

    public WorldSummarySection? Intro { get; set; }

    public WorldSummarySection? FarmerBackground { get; set; }

    public WorldSummarySection? Seasons { get; set; }

    public WorldSummarySection? Locations { get; set; }

    public WorldSummarySection? Festivals { get; set; }

    public WorldSummarySection? Villagers { get; set; }

    public WorldSummarySection? Outro { get; set; }

    /// <summary>按节名取节（SectionOrder 渲染用）；未知节名返回 null（记错误日志跳过）。</summary>
    public WorldSummarySection? GetSection(string name)
    {
        return name switch
        {
            "Intro" => this.Intro,
            "FarmerBackground" => this.FarmerBackground,
            "Seasons" => this.Seasons,
            "Locations" => this.Locations,
            "Festivals" => this.Festivals,
            "Villagers" => this.Villagers,
            "Outro" => this.Outro,
            _ => null
        };
    }
}

internal sealed class WorldSummarySection
{
    public string Text { get; set; } = string.Empty;

    public Dictionary<string, WorldSummaryEntry> Entries { get; set; } = new();
}

/// <summary>
/// 分节条目。通用条目只有 id/Name/Description；Seasons 条目附 Crops/Forage；
/// Locations 条目附 Region（渲染时按 Region 分组）。统一为一个类型，缺席字段为 null。
/// </summary>
internal sealed class WorldSummaryEntry
{
    [Newtonsoft.Json.JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string>? Crops { get; set; }

    public List<string>? Forage { get; set; }

    public string? Region { get; set; }
}
