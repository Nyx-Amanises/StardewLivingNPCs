using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using StardewValley;
using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue.Persistence;

/// <summary>
/// F6 修复验证：帮工历史文件损坏后的隔离恢复（.corrupt-时间戳 备份 + 从空重建）、
/// 写失败时脏标记保留重试、主机路径单 NPC 失败不连坐、遗忘写失败重新标脏。
/// 使用本文件私有的假环境（可注入读/写失败），不动共享测试基建。
/// </summary>
[Collection("LlmLayer")]
public sealed class DialogueHistoryStoreFarmhandRecoveryTests : IDisposable
{
    private readonly RecoveryFakeEnv env;
    private readonly DialogueHistoryStore store;
    private readonly string tempDir;

    public DialogueHistoryStoreFarmhandRecoveryTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        this.tempDir = Path.Combine(Path.GetTempPath(), "livingnpcs-f6-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
        this.env = new RecoveryFakeEnv { ModDirectoryPath = this.tempDir };
        this.store = new DialogueHistoryStore(this.env) { ArchiveSink = null };
    }

    public void Dispose()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        try
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响断言。
        }
    }

    private const string RelativePath = "multiplayer/TestFarm.json";

    private ExchangeRecord Conversation(string id)
    {
        return new ExchangeRecord
        {
            Kind = ExchangeKind.Conversation,
            Time = this.env.Now,
            ConversationId = id,
            Lines = new List<ExchangeLine> { new("Hi", true), new("Hello", false) }
        };
    }

    private string PhysicalPath()
    {
        return Path.Combine(this.tempDir, RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private void WritePhysicalCorruptFile()
    {
        string path = this.PhysicalPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json");
    }

    [Fact]
    public void Corrupt_File_Is_Quarantined_And_Next_Save_Writes_Fresh_Root()
    {
        this.WritePhysicalCorruptFile();
        this.env.ThrowOnModFileRead = true;

        this.store.Append("Abigail", this.Conversation("c1"));
        this.store.FlushOnSaving();

        // 写入已恢复：新根里有 Abigail。
        Assert.True(this.env.ModFiles.ContainsKey(RelativePath));
        var root = JObject.Parse(this.env.ModFiles[RelativePath]);
        Assert.NotNull(root["Abigail"]);

        // 坏文件被改名隔离（原名消失、.corrupt- 备份保留字节）。
        Assert.False(File.Exists(this.PhysicalPath()));
        string[] backups = Directory.GetFiles(Path.GetDirectoryName(this.PhysicalPath())!, "*.corrupt-*");
        Assert.Single(backups);
        Assert.Contains("this is not valid json", File.ReadAllText(backups[0]));

        // 写成功后脏标记已清：再次保存不重复写文件。
        int writesAfterFirstFlush = this.env.ModFileWrites;
        this.store.FlushOnSaving();
        Assert.Equal(writesAfterFirstFlush, this.env.ModFileWrites);
    }

    [Fact]
    public void Corrupt_File_Load_Returns_Empty_History_Without_Throwing()
    {
        this.WritePhysicalCorruptFile();
        this.env.ThrowOnModFileRead = true;

        var history = this.store.GetHistory("Abigail");

        Assert.False(history.Any());
        Assert.False(File.Exists(this.PhysicalPath()));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(this.PhysicalPath())!, "*.corrupt-*"));
    }

    [Fact]
    public void Failed_Write_Keeps_Dirty_And_Retries_On_Next_Save()
    {
        this.env.FailModFileWrites = true;
        this.store.Append("Abigail", this.Conversation("c1"));

        this.store.FlushOnSaving();
        Assert.False(this.env.ModFiles.ContainsKey(RelativePath));

        // 故障恢复后同一批脏数据重试成功。
        this.env.FailModFileWrites = false;
        this.store.FlushOnSaving();
        var root = JObject.Parse(this.env.ModFiles[RelativePath]);
        Assert.NotNull(root["Abigail"]);
    }

    [Fact]
    public void Farmhand_Forget_With_Failed_Write_Stays_Dirty_Until_Persisted()
    {
        this.store.Append("Abigail", this.Conversation("c1"));
        this.store.FlushOnSaving();

        this.env.FailModFileWrites = true;
        this.store.Forget("Abigail");

        this.env.FailModFileWrites = false;
        this.store.FlushOnSaving();

        var root = JObject.Parse(this.env.ModFiles[RelativePath]);
        var conversations = root["Abigail"]? ["ConversationHistory"] as JArray;
        Assert.NotNull(conversations);
        Assert.Empty(conversations!);
    }

    [Fact]
    public void Host_Flush_Isolates_Single_Npc_Failure_And_Retries_It()
    {
        this.env.IsMainPlayer = true;
        string failingKey = DialogueHistoryStore.HistoryLogicalKey("Marlon Fay");
        this.env.FailSaveWriteKeys.Add(failingKey);

        this.store.Append("Abigail", this.Conversation("c1"));
        this.store.Append("Marlon Fay", this.Conversation("c2"));
        this.store.FlushOnSaving();

        // Abigail 已落盘；Marlon 失败但不连坐。
        Assert.True(this.env.SaveData.ContainsKey(DialogueHistoryStore.HistoryLogicalKey("Abigail")));
        Assert.False(this.env.SaveData.ContainsKey(failingKey));

        // 故障恢复后仅重试遗留的脏 NPC。
        this.env.FailSaveWriteKeys.Clear();
        this.store.FlushOnSaving();
        Assert.True(this.env.SaveData.ContainsKey(failingKey));
    }

    /// <summary>本文件私有假环境：帮工默认；可注入 mod 文件读抛错 / 写抛错 / 指定存档键写抛错。</summary>
    private sealed class RecoveryFakeEnv : IPersistenceEnvironment
    {
        public bool IsWorldReady { get; set; } = true;
        public bool IsMainPlayer { get; set; }
        public bool IsLegacyValleyTalkLoaded => false;
        public string? SaveFolderName { get; set; } = "TestFarm";
        public StardewTime Now { get; set; } = new(3, Season.Spring, 14, 1200);
        public IEnumerable<string> KnownNpcNames => new[] { "Abigail", "Marlon Fay" };
        public string ModDirectoryPath { get; set; } = "unused";

        public bool ThrowOnModFileRead { get; set; }
        public bool FailModFileWrites { get; set; }
        public HashSet<string> FailSaveWriteKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> ModFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> SaveData { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int ModFileWrites { get; private set; }

        public JObject? ReadSaveJson(string key)
        {
            return this.SaveData.TryGetValue(key, out string? json) ? JObject.Parse(json) : null;
        }

        public void WriteSaveData(string key, object? model)
        {
            if (this.FailSaveWriteKeys.Contains(key))
            {
                throw new IOException($"injected save-write failure for {key}");
            }

            if (model == null)
            {
                this.SaveData.Remove(key);
                return;
            }

            this.SaveData[key] = JsonConvert.SerializeObject(model, Formatting.None, new StringEnumConverter());
        }

        public JObject? ReadModJsonFile(string relativePath)
        {
            if (this.ThrowOnModFileRead)
            {
                throw new JsonReaderException("injected corrupt farmhand file");
            }

            return this.ModFiles.TryGetValue(relativePath, out string? json) ? JObject.Parse(json) : null;
        }

        public void WriteModJsonFile(string relativePath, object model)
        {
            if (this.FailModFileWrites)
            {
                throw new IOException("injected farmhand write failure");
            }

            this.ModFileWrites++;
            this.ModFiles[relativePath] = JsonConvert.SerializeObject(model, Formatting.Indented, new StringEnumConverter());
        }

        public IReadOnlyDictionary<string, string>? GetSaveCustomData()
        {
            return this.SaveData;
        }

        public bool RemoveSaveCustomData(string physicalKey)
        {
            return this.SaveData.Remove(physicalKey);
        }
    }
}
