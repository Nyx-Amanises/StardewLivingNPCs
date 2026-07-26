using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Persistence;

[Collection("LlmLayer")]
public sealed class LegacySaveDataMigratorTests : IDisposable
{
    private readonly FakeMonitor monitor = new();
    private readonly FakePersistenceEnvironment env = new();
    private readonly TokenUsageTracker tracker = new();
    private readonly LegacySaveDataMigrator migrator;

    private const string LegacyHistoryJson = """
        {
          "ConversationHistory": [
            {"Item1": {"season": "Spring", "dayOfMonth": 10, "timeOfDay": 900, "year": 2},
             "Item2": {"ConversationElements": [{"Text": "hello", "IsPlayerLine": true, "Id": "x"}]}}
          ],
          "EventHistory": [], "OverheardHistory": [], "DialogueHistory": []
        }
        """;

    private const string LegacyLedgerJson = """
        {"Totals": {"PromptTokens": 10, "CompletionTokens": 5, "TotalTokens": 15, "CachedPromptTokens": 0, "ReasoningTokens": 0, "OfficialTokens": 15, "EstimatedTokens": 0},
         "ByModel": {}, "ByNpc": {}, "RecentEntries": []}
        """;

    public LegacySaveDataMigratorTests()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, this.monitor, new());
        this.migrator = new LegacySaveDataMigrator(this.env, this.tracker);
    }

    public void Dispose()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, null!, new());
    }

    private void SeedLegacyData()
    {
        this.env.CustomData["smapi/mod-data/dandm1.valleytalk/eventhistory_abigail"] = LegacyHistoryJson;
        this.env.CustomData["smapi/mod-data/dandm1.valleytalk/eventhistory_marlonfay"] = LegacyHistoryJson;
        this.env.CustomData["smapi/mod-data/dandm1.valleytalk/eventhistory_someunloadednpc"] = LegacyHistoryJson;
        this.env.CustomData["smapi/mod-data/dandm1.valleytalk/tokenusageledger"] = LegacyLedgerJson;
    }

    [Fact]
    public void MigratesHistoriesAndLedgerAndKeepsOldKeys()
    {
        this.SeedLegacyData();

        var result = this.migrator.RunOnSaveLoaded();

        Assert.True(result.Ran);
        Assert.Equal(2, result.MigratedNpcCount);
        Assert.True(result.LedgerMigrated);
        Assert.Single(result.PendingKeys); // 未匹配 NPC 的键留待补迁
        Assert.Empty(result.FailedKeys);

        // 小写物理键反查回原名：Marlon Fay → marlonfay；新键带原名冗余。
        // F8：含空格的名字落到 v2 无碰撞键（合法头 + 全名 UTF-16 逐码元 hex）。
        string newKey = FakePersistenceEnvironment.PhysicalKey(
            "dialogue.history.v1_marlonfay-u004d00610072006c006f006e0020004600610079");
        Assert.True(this.env.CustomData.ContainsKey(newKey));
        Assert.Equal("Marlon Fay", JObject.Parse(this.env.CustomData[newKey]).Value<string>("npcName"));

        // 旧键全部保留（裁决 1：先写后不删）。
        Assert.Equal(4, this.env.CustomData.Keys.Count(key => key.StartsWith("smapi/mod-data/dandm1.valleytalk/")));

        // 账本进了注入的 tracker，并写穿到新键。
        Assert.Equal(15, this.tracker.SaveLedgerForTests.Totals.TotalTokens);
        Assert.True(this.env.CustomData.ContainsKey(FakePersistenceEnvironment.PhysicalKey("dialogue.tokens.v1")));

        // 迁移标记已写入。
        Assert.True(this.env.CustomData.ContainsKey(FakePersistenceEnvironment.PhysicalKey("dialogue.migration.v1")));
    }

    [Fact]
    public void SecondRunIsIdempotent()
    {
        this.SeedLegacyData();
        this.migrator.RunOnSaveLoaded();
        int writesAfterFirstRun = this.env.SaveDataWrites;

        var second = this.migrator.RunOnSaveLoaded();

        Assert.False(second.Ran);
        Assert.Equal(writesAfterFirstRun, this.env.SaveDataWrites);
    }

    [Fact]
    public void LateInstalledNpcModGetsPickedUpOnALaterRun()
    {
        this.SeedLegacyData();
        this.migrator.RunOnSaveLoaded();

        // 玩家装回了那个 NPC mod：角色名重新出现在存档里。
        this.env.NpcNames.Add("SomeUnloadedNpc");
        var result = this.migrator.RunOnSaveLoaded();

        Assert.True(result.Ran);
        Assert.Equal(1, result.MigratedNpcCount);
        Assert.True(this.env.CustomData.ContainsKey(FakePersistenceEnvironment.PhysicalKey("dialogue.history.v1_someunloadednpc")));
    }

    [Fact]
    public void BadJsonGoesToFailedListAndIsNotRetried()
    {
        this.env.CustomData["smapi/mod-data/dandm1.valleytalk/eventhistory_abigail"] = "{not valid json";

        var result = this.migrator.RunOnSaveLoaded();

        Assert.Single(result.FailedKeys);
        // 坏键原样保留。
        Assert.True(this.env.CustomData.ContainsKey("smapi/mod-data/dandm1.valleytalk/eventhistory_abigail"));

        var second = this.migrator.RunOnSaveLoaded();
        Assert.False(second.Ran);
    }

    [Fact]
    public void SkipsEntirelyWhenLegacyModStillLoaded()
    {
        this.SeedLegacyData();
        this.env.IsLegacyValleyTalkLoaded = true;

        var result = this.migrator.RunOnSaveLoaded();

        Assert.False(result.Ran);
        Assert.Equal(0, this.env.SaveDataWrites);
    }

    [Fact]
    public void SkipsOnFarmhand()
    {
        this.SeedLegacyData();
        this.env.IsMainPlayer = false;

        var result = this.migrator.RunOnSaveLoaded();

        Assert.False(result.Ran);
        Assert.Equal(0, this.env.SaveDataWrites);
    }

    [Fact]
    public void PurgeRemovesOnlyLegacyKeys()
    {
        this.SeedLegacyData();
        this.migrator.RunOnSaveLoaded();
        int newKeysBefore = this.env.CustomData.Keys.Count(key => key.StartsWith("smapi/mod-data/yuki.livingnpcs/"));

        int removed = this.migrator.PurgeLegacyKeys();

        Assert.Equal(4, removed);
        Assert.Equal(0, this.migrator.CountLegacyKeys());
        Assert.Equal(newKeysBefore, this.env.CustomData.Keys.Count(key => key.StartsWith("smapi/mod-data/yuki.livingnpcs/")));
    }
}
