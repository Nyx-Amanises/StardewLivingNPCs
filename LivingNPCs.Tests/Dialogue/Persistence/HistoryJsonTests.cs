using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue.Persistence;

public sealed class HistoryJsonTests
{
    // 旧格式样本（§3.1.1 的逐字字段名）：DialogueLine 带 SideEffects/HasText、
    // ConversationElement 带 Id、时间字段小写开头、季节首字母大写字符串。
    private const string LegacySample = """
        {
          "EventHistory": [
            {
              "Item1": {"season": "Fall", "dayOfMonth": 16, "timeOfDay": 1000, "year": 2},
              "Item2": {"Dialogues": [{"Text": "Welcome to the fair!", "SideEffects": null, "HasText": true}], "Listeners": [], "EventName": "StardewValleyFair"}
            }
          ],
          "OverheardHistory": [
            {
              "Item1": {"season": "Spring", "dayOfMonth": 3, "timeOfDay": 900, "year": 3},
              "Item2": {"name": "Sam", "dialogues": [{"Text": "Hey Abby!", "SideEffects": null, "HasText": true}]}
            }
          ],
          "DialogueHistory": [
            {
              "Item1": {"season": "spring", "dayOfMonth": 5, "timeOfDay": 1100, "year": 3},
              "Item2": {"Dialogues": [{"Text": "Nice weather today.#$b#Isn't it?", "SideEffects": null, "HasText": true}]}
            }
          ],
          "ConversationHistory": [
            {
              "Item1": {"season": "Spring", "dayOfMonth": 6, "timeOfDay": 1300, "year": 3},
              "Item2": {"ConversationElements": [
                {"Text": "Hello!", "IsPlayerLine": true, "Id": "aaaa-bbbb"},
                {"Text": "Oh hi, farmer.", "IsPlayerLine": false, "Id": "cccc-dddd"}
              ]}
            }
          ]
        }
        """;

    [Fact]
    public void ParsesAllFourLegacyArrays()
    {
        var history = HistoryJson.Parse(JObject.Parse(LegacySample), "Abigail");

        Assert.Equal("Abigail", history.NpcName);
        Assert.Single(history.EventHistory);
        Assert.Equal("StardewValleyFair", history.EventHistory[0].Item2.EventName);
        Assert.Equal("Welcome to the fair!", history.EventHistory[0].Item2.Dialogues[0].Text);

        Assert.Single(history.OverheardHistory);
        Assert.Equal("Sam", history.OverheardHistory[0].Item2.SpeakerName);

        // 季节小写 "spring" 也能解析（大小写不敏感，§4.4）。
        Assert.Single(history.DialogueHistory);
        Assert.Equal(StardewValley.Season.Spring, history.DialogueHistory[0].Item1.season);

        Assert.Single(history.ConversationHistory);
        var elements = history.ConversationHistory[0].Item2.ConversationElements;
        Assert.Equal(2, elements.Count);
        Assert.True(elements[0].IsPlayerLine);
        Assert.False(elements[1].IsPlayerLine);
        // Id 不还原：每次反序列化重新生成（§3.1.1）。
        Assert.NotEqual("aaaa-bbbb", elements[0].Id);
    }

    [Fact]
    public void ChatHistoryFallbackAlternatesNpcAndPlayerLines()
    {
        var root = JObject.Parse("""
            {
              "ConversationHistory": [
                {
                  "Item1": {"season": "Winter", "dayOfMonth": 1, "timeOfDay": 800, "year": 1},
                  "Item2": {"chatHistory": ["Good morning.", "Morning!", "Cold today."]}
                }
              ]
            }
            """);

        var history = HistoryJson.Parse(root, "Abigail");

        var elements = history.ConversationHistory.Single().Item2.ConversationElements;
        Assert.Equal(3, elements.Count);
        Assert.False(elements[0].IsPlayerLine);  // 偶数下标为 NPC 台词
        Assert.True(elements[1].IsPlayerLine);   // 奇数下标为玩家台词
        Assert.False(elements[2].IsPlayerLine);
    }

    [Fact]
    public void BadEntriesAreDroppedWithoutAffectingSiblings()
    {
        var root = JObject.Parse("""
            {
              "DialogueHistory": [
                {"Item1": {"season": "NotASeason", "dayOfMonth": 5, "timeOfDay": 900, "year": 1}, "Item2": {"Dialogues": [{"Text": "bad season"}]}},
                {"Item1": {"season": "Summer", "dayOfMonth": -3, "timeOfDay": 900, "year": 1}, "Item2": {"Dialogues": [{"Text": "negative day"}]}},
                {"Item1": "not an object", "Item2": {"Dialogues": []}},
                {"Item1": {"season": "Summer", "dayOfMonth": 8, "timeOfDay": 900, "year": 1}, "Item2": {"Dialogues": [{"Text": "good"}]}}
              ]
            }
            """);

        var history = HistoryJson.Parse(root, "Abigail");

        Assert.Single(history.DialogueHistory);
        Assert.Equal("good", history.DialogueHistory[0].Item2.Dialogues[0].Text);
    }

    [Fact]
    public void ListenersToleratesArbitraryContent()
    {
        var root = JObject.Parse("""
            {
              "EventHistory": [
                {
                  "Item1": {"season": "Fall", "dayOfMonth": 16, "timeOfDay": 1000, "year": 2},
                  "Item2": {
                    "Dialogues": [{"Text": "line"}],
                    "Listeners": [ {"Name": "Sam", "Sprite": {"junk": true}}, "Penny", 42, null ],
                    "EventName": "Fair"
                  }
                }
              ]
            }
            """);

        var history = HistoryJson.Parse(root, "Abigail");

        var entry = history.EventHistory.Single().Item2;
        Assert.Equal(new[] { "Sam", "Penny" }, entry.Listeners);
    }

    [Fact]
    public void NullRootYieldsEmptyHistory()
    {
        var history = HistoryJson.Parse(null, "Abigail");
        Assert.False(history.Any());
        Assert.Equal("Abigail", history.NpcName);
    }

    [Fact]
    public void NewFormatRoundTripsThroughSerializerAndParser()
    {
        var original = new StardewEventHistory { NpcName = "Abigail" };
        original.Add(new StardewTime(3, StardewValley.Season.Summer, 2, 1400), new ConversationHistory(new()
        {
            new ConversationElement("Hi!", true),
            new ConversationElement("Hello.", false)
        }));
        original.Add(new StardewTime(3, StardewValley.Season.Summer, 2, 1500), new DialogueHistory(new() { new HistoryLine("A line.") }));
        original.Add(new StardewTime(3, StardewValley.Season.Summer, 3, 900), new DialogueEventHistory(new() { "Sam" }, new() { new HistoryLine("Event line") }, "EggFestival"));
        original.Add(new StardewTime(3, StardewValley.Season.Summer, 4, 1000), new OverheardHistory("Penny", new() { new HistoryLine("Overheard") }));
        original.Add(new StardewTime(3, StardewValley.Season.Summer, 4, 1100), new ThirdPartyHistory("Sam", new(), "Fair"));

        string json = JsonConvert.SerializeObject(original, Formatting.None, new StringEnumConverter());
        var root = JObject.Parse(json);

        // 新契约：顶层 schemaVersion/npcName，季节序列化为字符串（StringEnumConverter）。
        Assert.Equal(1, root.Value<int>("schemaVersion"));
        Assert.Equal("Abigail", root.Value<string>("npcName"));
        Assert.Equal("Summer", root["ConversationHistory"]![0]!["Item1"]!.Value<string>("season"));
        // 第三方目击记录不持久化。
        Assert.Null(root["ThirdPartyHistory"]);
        // 台词不再序列化游戏对象：行对象只有 Text。
        var lineObj = (JObject)root["DialogueHistory"]![0]!["Item2"]!["Dialogues"]![0]!;
        Assert.Equal(new[] { "Text" }, lineObj.Properties().Select(property => property.Name));

        var parsed = HistoryJson.Parse(root, "Abigail");
        Assert.Single(parsed.ConversationHistory);
        Assert.Single(parsed.DialogueHistory);
        Assert.Single(parsed.EventHistory);
        Assert.Single(parsed.OverheardHistory);
        Assert.Equal("EggFestival", parsed.EventHistory[0].Item2.EventName);
        Assert.Equal(new[] { "Sam" }, parsed.EventHistory[0].Item2.Listeners);
        Assert.Equal("Penny", parsed.OverheardHistory[0].Item2.SpeakerName);
        Assert.True(parsed.ConversationHistory[0].Item2.ConversationElements[0].IsPlayerLine);
    }

    [Fact]
    public void LegacyLedgerWithoutCacheWriteFieldDefaultsToZero()
    {
        // 老存档（0.1.5 之前）没有 CacheWritePromptTokens 字段（§3.1.2）。
        const string legacyLedger = """
            {
              "Totals": {"PromptTokens": 100, "CompletionTokens": 40, "TotalTokens": 140, "CachedPromptTokens": 10, "ReasoningTokens": 0, "OfficialTokens": 140, "EstimatedTokens": 0},
              "ByModel": {"OpenAI/gpt-4o": {"PromptTokens": 100, "CompletionTokens": 40, "TotalTokens": 140, "CachedPromptTokens": 10, "ReasoningTokens": 0, "OfficialTokens": 140, "EstimatedTokens": 0}},
              "ByNpc": {"Abigail": {"PromptTokens": 100, "CompletionTokens": 40, "TotalTokens": 140, "CachedPromptTokens": 10, "ReasoningTokens": 0, "OfficialTokens": 140, "EstimatedTokens": 0}},
              "RecentEntries": [
                {"NpcName": "Abigail", "Provider": "OpenAI", "ModelName": "gpt-4o", "Outcome": "success", "PromptTokens": 100, "CompletionTokens": 40, "TotalTokens": 140, "CachedPromptTokens": 10, "ReasoningTokens": 0, "IsEstimated": false, "Source": "provider usage", "Year": 2, "Season": "spring", "DayOfMonth": 20, "TimeOfDay": 1300, "RecordedAtUtc": "2026-06-20T05:36:16.7516098Z"}
              ]
            }
            """;

        var ledger = JsonConvert.DeserializeObject<TokenUsageLedger>(legacyLedger);

        Assert.NotNull(ledger);
        Assert.Equal(140, ledger!.Totals.TotalTokens);
        Assert.Equal(0, ledger.Totals.CacheWritePromptTokens);
        Assert.Single(ledger.RecentEntries);
        Assert.Equal(0, ledger.RecentEntries[0].CacheWritePromptTokens);
        Assert.Equal("spring", ledger.RecentEntries[0].Season);
    }
}
