using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Dialogue.Persistence;

/// <summary>
/// 持久化层对 SMAPI/游戏静态的唯一出口，便于单元测试注入假环境。
/// 存档数据读写统一走 SMAPI IDataHelper（自带 StringEnumConverter，§5.2）；
/// CustomData 枚举/移除仅供迁移器与 purge 命令使用（新 mod 的 IDataHelper 读不到旧 modID 的键）。
/// </summary>
internal interface IPersistenceEnvironment
{
    bool IsWorldReady { get; }

    /// <summary>主机/farmhand 分流按 IsMainPlayer（§3.0：与 SMAPI 的 IsOnHostComputer 限制不完全重合，
    /// 但分屏玩家在主机电脑上不受限，统一按 IsMainPlayer 更保守且旧数据语义一致）。</summary>
    bool IsMainPlayer { get; }

    /// <summary>旧对话引擎 dandm1.ValleyTalk 是否仍在加载（01 §5：在则迁移与引擎均不启动）。</summary>
    bool IsLegacyValleyTalkLoaded { get; }

    string? SaveFolderName { get; }

    StardewTime Now { get; }

    /// <summary>当前存档的全部 NPC 内部名（迁移反查小写键用）。</summary>
    IEnumerable<string> KnownNpcNames { get; }

    string ModDirectoryPath { get; }

    JObject? ReadSaveJson(string key);

    void WriteSaveData(string key, object? model);

    JObject? ReadModJsonFile(string relativePath);

    void WriteModJsonFile(string relativePath, object model);

    /// <summary>存档 CustomData 字典的只读视图（物理键 → JSON 字符串值）；未载入存档时为 null。</summary>
    IReadOnlyDictionary<string, string>? GetSaveCustomData();

    /// <summary>从存档 CustomData 中移除一个物理键（purge 命令用），返回是否确有移除。</summary>
    bool RemoveSaveCustomData(string physicalKey);
}

internal sealed class SmapiPersistenceEnvironment : IPersistenceEnvironment
{
    private readonly IModHelper helper;

    public SmapiPersistenceEnvironment(IModHelper helper)
    {
        this.helper = helper;
    }

    public bool IsWorldReady => Context.IsWorldReady;

    public bool IsMainPlayer => Context.IsMainPlayer;

    public bool IsLegacyValleyTalkLoaded => this.helper.ModRegistry.IsLoaded("dandm1.ValleyTalk");

    public string? SaveFolderName => Constants.SaveFolderName;

    public StardewTime Now => new(Game1.year, Game1.season, Game1.dayOfMonth, Game1.timeOfDay);

    public IEnumerable<string> KnownNpcNames => Game1.characterData?.Keys ?? (IEnumerable<string>)System.Array.Empty<string>();

    public string ModDirectoryPath => this.helper.DirectoryPath;

    public JObject? ReadSaveJson(string key)
    {
        return this.helper.Data.ReadSaveData<JObject>(key);
    }

    public void WriteSaveData(string key, object? model)
    {
        this.helper.Data.WriteSaveData(key, model);
    }

    public JObject? ReadModJsonFile(string relativePath)
    {
        return this.helper.Data.ReadJsonFile<JObject>(relativePath);
    }

    public void WriteModJsonFile(string relativePath, object model)
    {
        this.helper.Data.WriteJsonFile(relativePath, model);
    }

    public IReadOnlyDictionary<string, string>? GetSaveCustomData()
    {
        // 运行时 Game1.CustomData 与 SaveGame.loaded.CustomData 内容一致（§4.2.1），读其一即可。
        return Game1.CustomData;
    }

    public bool RemoveSaveCustomData(string physicalKey)
    {
        bool removed = Game1.CustomData?.Remove(physicalKey) == true;
        var loaded = SaveGame.loaded?.CustomData;
        if (loaded != null && !ReferenceEquals(loaded, Game1.CustomData))
        {
            removed |= loaded.Remove(physicalKey);
        }

        return removed;
    }
}
