using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Persistence;

/// <summary>挂在 LlmLayer collection 串行执行：本组测试会设置 DialogueServices 静态门面。</summary>
[Collection("LlmLayer")]
public sealed class DialogueHistoryStoreTests : IDisposable
{
    private readonly FakeMonitor monitor = new();
    private readonly FakePersistenceEnvironment env = new();
    private readonly DialogueHistoryStore store;

    public DialogueHistoryStoreTests()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, this.monitor, new());
        this.store = new DialogueHistoryStore(this.env)
        {
            ArchiveSink = null
        };
    }

    public void Dispose()
    {
        LivingNPCs.Dialogue.DialogueServices.Initialize(null!, null!, new());
    }

    private static ExchangeRecord Conversation(StardewTime time, string id, params (string Text, bool Player)[] lines)
    {
        return new ExchangeRecord
        {
            Kind = ExchangeKind.Conversation,
            Time = time,
            ConversationId = id,
            Lines = lines.Select(line => new ExchangeLine(line.Text, line.Player)).ToList()
        };
    }

    [Fact]
    public void HostFlushWritesLowercaseSlugKeyOnSaving()
    {
        this.store.Append("Marlon Fay", Conversation(this.env.Now, "conv1", ("Hi", true), ("Hello", false)));

        // 写操作先进内存缓存，Saving 前不落盘。
        Assert.Empty(this.env.CustomData);

        this.store.FlushOnSaving();

        // F8：名字含非法字符（空格被删）→ v2 无碰撞键（合法头 + 全名 UTF-16 逐码元 hex）。
        string physical = FakePersistenceEnvironment.PhysicalKey(
            "dialogue.history.v1_marlonfay-u004d00610072006c006f006e0020004600610079");
        Assert.True(this.env.CustomData.ContainsKey(physical));
        var root = JObject.Parse(this.env.CustomData[physical]);
        Assert.Equal(1, root.Value<int>("schemaVersion"));
        Assert.Equal("Marlon Fay", root.Value<string>("npcName"));
    }

    [Fact]
    public void AppendAndRecentRoundTripAllFourKinds()
    {
        var t = this.env.Now;
        this.store.Append("Abigail", Conversation(t.AddDays(-3), "c1", ("Hey", true)));
        this.store.Append("Abigail", new ExchangeRecord
        {
            Kind = ExchangeKind.DialogueLines,
            Time = t.AddDays(-2),
            Lines = new() { new ExchangeLine("Shown line", false) }
        });
        this.store.Append("Abigail", new ExchangeRecord
        {
            Kind = ExchangeKind.EventLines,
            Time = t.AddDays(-1),
            EventName = "EggFestival",
            ListenerNames = new() { "Sam" },
            Lines = new() { new ExchangeLine("Event line", false) }
        });
        this.store.Append("Abigail", new ExchangeRecord
        {
            Kind = ExchangeKind.Overheard,
            Time = t,
            SpeakerName = "Penny",
            Lines = new() { new ExchangeLine("Overheard line", false) }
        });

        var recent = this.store.Recent("Abigail", 10);

        Assert.Equal(4, recent.Count);
        // 时间正序返回。
        Assert.Equal(ExchangeKind.Conversation, recent[0].Kind);
        Assert.Equal(ExchangeKind.DialogueLines, recent[1].Kind);
        Assert.Equal(ExchangeKind.EventLines, recent[2].Kind);
        Assert.Equal(ExchangeKind.Overheard, recent[3].Kind);
        Assert.Equal("EggFestival", recent[2].EventName);
        Assert.Equal(new[] { "Sam" }, recent[2].ListenerNames);
        Assert.Equal("Penny", recent[3].SpeakerName);

        // maxItems 取最近的 N 条。
        var lastTwo = this.store.Recent("Abigail", 2);
        Assert.Equal(new[] { ExchangeKind.EventLines, ExchangeKind.Overheard }, lastTwo.Select(record => record.Kind));
    }

    [Fact]
    public void ConversationUpsertsBySameId()
    {
        var t = this.env.Now;
        this.store.Append("Abigail", Conversation(t, "conv-1", ("Hi", true)));
        this.store.Append("Abigail", Conversation(t, "conv-1", ("Hi", true), ("Hello!", false)));
        this.store.Append("Abigail", Conversation(t, "conv-2", ("Another", true)));

        var history = this.store.GetHistory("Abigail");

        Assert.Equal(2, history.ConversationHistory.Count);
        Assert.Equal(2, history.ConversationHistory[0].Item2.ConversationElements.Count);
    }

    [Fact]
    public void LoadDropsEntriesLaterThanCurrentGameTime()
    {
        var future = new StardewEventHistory { NpcName = "Abigail" };
        future.Add(this.env.Now.AddDays(-1), new ConversationHistory(new() { new ConversationElement("past", true) }));
        future.Add(this.env.Now.AddDays(5), new ConversationHistory(new() { new ConversationElement("future", true) }));
        this.env.WriteSaveData(DialogueHistoryStore.HistoryLogicalKey("Abigail"), future);

        var history = this.store.GetHistory("Abigail");

        Assert.Single(history.ConversationHistory);
        Assert.Equal("past", history.ConversationHistory[0].Item2.ConversationElements[0].Text);
    }

    [Fact]
    public void ForgetWritesEmptyHistoryImmediately()
    {
        this.store.Append("Abigail", Conversation(this.env.Now, "c1", ("Hi", true)));

        bool cleared = this.store.Forget("Abigail");

        Assert.True(cleared);
        string physical = FakePersistenceEnvironment.PhysicalKey("dialogue.history.v1_abigail");
        Assert.True(this.env.CustomData.ContainsKey(physical));
        var root = JObject.Parse(this.env.CustomData[physical]);
        Assert.Empty((JArray)root["ConversationHistory"]!);
        Assert.False(this.store.Forget("Abigail")); // 已空：再遗忘返回 false
    }

    [Fact]
    public void FarmhandNeverTouchesSaveDataAndUsesPerSaveFile()
    {
        this.env.IsMainPlayer = false;
        this.env.SaveFolderName = "FarmA_1";

        this.store.Append("Abigail", Conversation(this.env.Now, "c1", ("Hi", true)));
        this.store.FlushOnSaving();

        Assert.Equal(0, this.env.SaveDataReads);
        Assert.Equal(0, this.env.SaveDataWrites);
        Assert.True(this.env.ModFiles.ContainsKey("multiplayer/FarmA_1.json"));
        var root = JObject.Parse(this.env.ModFiles["multiplayer/FarmA_1.json"]);
        Assert.NotNull(root["Abigail"]); // 键为 NPC 内部名原大小写

        // 换存档后文件名跟着换（修正旧实现定死文件名的怪癖）。
        this.store.OnReturnedToTitle();
        this.env.SaveFolderName = "FarmB_2";
        this.store.Append("Abigail", Conversation(this.env.Now, "c2", ("Yo", true)));
        this.store.FlushOnSaving();
        Assert.True(this.env.ModFiles.ContainsKey("multiplayer/FarmB_2.json"));
    }

    [Fact]
    public void FarmhandReadsHistoryFromMultiplayerFileIncludingLegacyFormat()
    {
        this.env.IsMainPlayer = false;
        // 从旧文件夹搬来的 multiplayer 文件是旧格式（无 schemaVersion），同一解析器接住。
        this.env.ModFiles[$"multiplayer/{this.env.SaveFolderName}.json"] = """
            {
              "Abigail": {
                "ConversationHistory": [
                  {"Item1": {"season": "Spring", "dayOfMonth": 10, "timeOfDay": 900, "year": 2},
                   "Item2": {"ConversationElements": [{"Text": "old chat", "IsPlayerLine": false, "Id": "x"}]}}
                ]
              }
            }
            """;

        var recent = this.store.Recent("Abigail", 5);

        Assert.Single(recent);
        Assert.Equal("old chat", recent[0].Lines[0].Text);
        Assert.Equal(0, this.env.SaveDataReads);
    }

    [Fact]
    public void OversizedHistoryTriggersForcedPruneAndWarn()
    {
        string bigText = new string('x', 6 * 1024);
        var history = this.store.GetHistory("Abigail");
        for (int i = 0; i < 60; i++)
        {
            // 全部落在过去的不同日期（非当日，可被修剪）。
            history.Add(this.env.Now.AddDays(-i - 1), new ConversationHistory(new() { new ConversationElement(bigText + i, false) }));
        }
        this.store.MarkDirty("Abigail");

        var archived = new List<Tuple<StardewTime, ConversationHistory>>();
        this.store.ArchiveSink = (_, dropped) => archived.AddRange(dropped);

        this.store.FlushOnSaving();

        string physical = FakePersistenceEnvironment.PhysicalKey("dialogue.history.v1_abigail");
        Assert.True(this.env.CustomData.ContainsKey(physical));
        Assert.True(this.env.CustomData[physical].Length <= DialogueHistoryStore.MaxSerializedNpcBytes);
        Assert.NotEmpty(archived);
        Assert.Contains(this.monitor.Entries, entry => entry.Level == StardewModdingAPI.LogLevel.Warn && entry.Message.Contains("forced a prune"));
    }

    [Fact]
    public void PruneAndArchiveSendsDroppedConversationsToSink()
    {
        var history = this.store.GetHistory("Abigail");
        int total = StardewEventHistory.MaxConversationEntries + 4;
        for (int i = 0; i < total; i++)
        {
            history.Add(this.env.Now.AddDays(-(total - i)), new ConversationHistory(new() { new ConversationElement($"c{i}", false) }));
        }

        var archived = new List<Tuple<StardewTime, ConversationHistory>>();
        this.store.ArchiveSink = (_, dropped) => archived.AddRange(dropped);

        this.store.PruneAndArchive("Abigail");

        Assert.Equal(4, archived.Count);
        Assert.Equal(StardewEventHistory.MaxConversationEntries, this.store.GetHistory("Abigail").ConversationHistory.Count);
    }
}
