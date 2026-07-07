using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue.Persistence;

/// <summary>
/// WP14 测试假环境：按 SMAPI 的真实行为模拟存档数据层——WriteSaveData 校验逻辑键 slug、
/// 值序列化为单个 JSON 字符串（StringEnumConverter）、物理键 smapi/mod-data/&lt;modID&gt;/&lt;key&gt; 整体小写；
/// mod 文件读写走内存字典。
/// </summary>
internal sealed class FakePersistenceEnvironment : IPersistenceEnvironment
{
    public const string OwnModId = "Yuki.LivingNPCs";

    public bool IsWorldReady { get; set; } = true;
    public bool IsMainPlayer { get; set; } = true;
    public bool IsLegacyValleyTalkLoaded { get; set; }
    public string? SaveFolderName { get; set; } = "TestFarm_123";
    public StardewTime Now { get; set; } = new(3, StardewValley.Season.Spring, 14, 1200);
    public List<string> NpcNames { get; } = new() { "Abigail", "Marlon Fay" };
    public string ModDirectoryPath { get; set; } = "unused";

    /// <summary>物理键（已小写）→ JSON 字符串，模拟存档 CustomData。</summary>
    public Dictionary<string, string> CustomData { get; } = new();

    /// <summary>相对路径 → JSON 字符串，模拟 mod 文件夹内的数据文件。</summary>
    public Dictionary<string, string> ModFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int SaveDataReads { get; private set; }
    public int SaveDataWrites { get; private set; }

    public IEnumerable<string> KnownNpcNames => this.NpcNames;

    public JObject? ReadSaveJson(string key)
    {
        this.SaveDataReads++;
        return this.CustomData.TryGetValue(PhysicalKey(key), out string? json) ? JObject.Parse(json) : null;
    }

    public void WriteSaveData(string key, object? model)
    {
        this.SaveDataWrites++;
        AssertSlug(key);
        string physical = PhysicalKey(key);
        if (model == null)
        {
            this.CustomData.Remove(physical);
            return;
        }

        this.CustomData[physical] = JsonConvert.SerializeObject(model, Formatting.None, new StringEnumConverter());
    }

    public JObject? ReadModJsonFile(string relativePath)
    {
        return this.ModFiles.TryGetValue(Normalize(relativePath), out string? json) ? JObject.Parse(json) : null;
    }

    public void WriteModJsonFile(string relativePath, object model)
    {
        this.ModFiles[Normalize(relativePath)] = JsonConvert.SerializeObject(model, Formatting.Indented, new StringEnumConverter());
    }

    public IReadOnlyDictionary<string, string>? GetSaveCustomData()
    {
        return this.IsWorldReady ? this.CustomData : null;
    }

    public bool RemoveSaveCustomData(string physicalKey)
    {
        return this.CustomData.Remove(physicalKey);
    }

    public static string PhysicalKey(string logicalKey)
    {
        return $"smapi/mod-data/{OwnModId}/{logicalKey}".ToLower();
    }

    private static void AssertSlug(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_' && ch != '.' && ch != '-'))
        {
            throw new ArgumentException(
                "The data key is invalid (keys must only contain letters, numbers, underscores, periods, or hyphens).");
        }
    }

    private static string Normalize(string relativePath)
    {
        return relativePath.Replace('\\', '/');
    }
}
