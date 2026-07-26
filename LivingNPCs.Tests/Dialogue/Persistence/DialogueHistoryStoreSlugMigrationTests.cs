using System;
using Newtonsoft.Json.Linq;
using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue.Persistence;

/// <summary>
/// F8 修复验证（读取端）：主机读取时 v2 无碰撞键优先；未命中且 v1 旧键存在时按
/// npcName 归属回退——属于本 NPC 则加载并当场迁移改名（写 v2、删 v1），
/// 撞键槽属于别的 NPC 则视为无数据且绝不动那个槽（现有存档历史不丢）。
/// </summary>
[Collection("LlmLayer")]
public sealed class DialogueHistoryStoreSlugMigrationTests : IDisposable
{
    private readonly FakePersistenceEnvironment env = new();
    private readonly DialogueHistoryStore store;

    public DialogueHistoryStoreSlugMigrationTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        this.store = new DialogueHistoryStore(this.env) { ArchiveSink = null };
    }

    public void Dispose()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    private static string LegacyHistoryJson(string npcName)
    {
        return """
            {"schemaVersion": 1, "npcName": "NPC_NAME",
             "DialogueHistory": [
               {"Item1": {"year": 1, "season": "Spring", "dayOfMonth": 5, "timeOfDay": 900},
                "Item2": {"Dialogues": [{"Text": "旧台词"}]}}
             ]}
            """.Replace("NPC_NAME", npcName);
    }

    [Fact]
    public void Legacy_Slug_Owned_By_Npc_Is_Loaded_And_Renamed_To_V2()
    {
        // "雪" 的 v1 键：低字节截断 U+96EA → "ea"。
        string v1Physical = FakePersistenceEnvironment.PhysicalKey(DialogueHistoryStore.LegacyHistoryLogicalKey("雪"));
        string v2Physical = FakePersistenceEnvironment.PhysicalKey(DialogueHistoryStore.HistoryLogicalKey("雪"));
        Assert.NotEqual(v1Physical, v2Physical);
        this.env.CustomData[v1Physical] = LegacyHistoryJson("雪");

        var history = this.store.GetHistory("雪");

        // 旧数据读到了，且已当场迁移：v2 键就位、v1 键移除。
        Assert.Single(history.DialogueHistory);
        Assert.Equal("旧台词", history.DialogueHistory[0].Item2.Dialogues[0].Text);
        Assert.True(this.env.CustomData.ContainsKey(v2Physical));
        Assert.False(this.env.CustomData.ContainsKey(v1Physical));
        Assert.Equal("雪", JObject.Parse(this.env.CustomData[v2Physical]).Value<string>("npcName"));
    }

    [Fact]
    public void Colliding_Legacy_Slot_Owned_By_Someone_Else_Is_Left_Untouched()
    {
        // "阿"(U+963F) 与 "吿"(U+543F) 的 v1 键同为 "3f"；槽里记录的主人是 "阿"。
        Assert.Equal(
            DialogueHistoryStore.LegacyHistoryLogicalKey("阿"),
            DialogueHistoryStore.LegacyHistoryLogicalKey("吿"));
        string v1Physical = FakePersistenceEnvironment.PhysicalKey(DialogueHistoryStore.LegacyHistoryLogicalKey("阿"));
        this.env.CustomData[v1Physical] = LegacyHistoryJson("阿");

        // 撞键的另一位 NPC：不得继承他人历史，旧槽原样保留。
        var other = this.store.GetHistory("吿");
        Assert.False(other.Any());
        Assert.True(this.env.CustomData.ContainsKey(v1Physical));

        // 真正的主人来读：正常加载并迁移改名。
        var owner = this.store.GetHistory("阿");
        Assert.Single(owner.DialogueHistory);
        Assert.False(this.env.CustomData.ContainsKey(v1Physical));
        Assert.True(this.env.CustomData.ContainsKey(
            FakePersistenceEnvironment.PhysicalKey(DialogueHistoryStore.HistoryLogicalKey("阿"))));
    }

    [Fact]
    public void Vanilla_Names_Never_Trigger_The_Fallback_Path()
    {
        // 合法名字 v1 == v2：既有存档键原样命中，零迁移。
        Assert.Equal(
            DialogueHistoryStore.LegacyHistoryLogicalKey("Abigail"),
            DialogueHistoryStore.HistoryLogicalKey("Abigail"));

        string physical = FakePersistenceEnvironment.PhysicalKey("dialogue.history.v1_abigail");
        this.env.CustomData[physical] = LegacyHistoryJson("Abigail");

        int writesBefore = this.env.SaveDataWrites;
        var history = this.store.GetHistory("Abigail");

        Assert.Single(history.DialogueHistory);
        Assert.Equal(writesBefore, this.env.SaveDataWrites);
        Assert.True(this.env.CustomData.ContainsKey(physical));
    }
}
