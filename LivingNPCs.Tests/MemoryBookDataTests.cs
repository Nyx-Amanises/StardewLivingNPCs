using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Ui;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;
using Newtonsoft.Json;
using StardewValley;

namespace LivingNPCs.Tests;

/// <summary>记忆手册数据层：名册排序、四个标签页的组装与时间格式化（不触游戏运行时）。</summary>
public sealed class MemoryBookDataTests
{
    /// <summary>回显翻译：key + 序列化 tokens，便于断言键与令牌都被正确传递。</summary>
    private static string Echo(string key, object? tokens = null)
    {
        return tokens == null ? key : $"{key}{JsonConvert.SerializeObject(tokens)}";
    }

    private static LivingNpcState StateWith(string name, Action<LivingNpcState>? mutate = null)
    {
        var state = new LivingNpcState { NpcName = name };
        mutate?.Invoke(state);
        return state;
    }

    [Fact]
    public void Roster_Orders_By_Recency_Then_Hearts_And_Skips_Empty_States()
    {
        var states = new[]
        {
            StateWith("Abigail", s => { s.LastConversationTotalDays = 10; s.LastUpdatedTotalDays = 10; }),
            StateWith("Haley", s => { s.LastConversationTotalDays = 12; s.LastUpdatedTotalDays = 12; }),
            StateWith("Emily", s => { s.LastConversationTotalDays = 12; s.LastUpdatedTotalDays = 12; }),
            StateWith("Ghost")  // 无任何记录：不上册
        };

        var roster = MemoryBookData.BuildRoster(
            states,
            name => name,
            name => name == "Emily" ? 8 : 2,
            nowTotalDays: 14,
            Echo);

        Assert.Equal(3, roster.Count);
        Assert.Equal("Emily", roster[0].NpcName);   // 同天接触，心数高者优先
        Assert.Equal("Haley", roster[1].NpcName);
        Assert.Equal("Abigail", roster[2].NpcName);
        Assert.Contains("book.time.daysAgo", roster[2].SubtitleText);
    }

    [Fact]
    public void RelationshipCard_Includes_Nickname_Impression_And_Conflict()
    {
        var state = StateWith("Shane", s =>
        {
            s.CurrentEmotion = "Grateful";
            s.InteractionComfortTier = "Friendly";
            s.RelationshipTrust = 55;
            s.FarmerNickname = "老农";
            s.RelationshipImpression = "一段互相扶持的友情。";
            s.RelationshipImpressionUpdatedTotalDays = 20;
            s.LastConversationTotalDays = 21;
            s.Conflicts.Add(new NpcConflictFact { Status = "Active", Severity = 40, Summary = "上次的玩笑开过头了" });
        });

        var lines = MemoryBookData.BuildRelationshipCard(state, "谢恩", hearts: 6, nowTotalDays: 22, Echo);
        string all = string.Join("\n", lines.Select(line => line.Text));

        Assert.Contains("book.emotion.grateful", all);
        Assert.Contains("book.tier.friendly", all);
        Assert.Contains("book.trust.solid", all);
        Assert.Contains("老农", all);
        Assert.Contains("一段互相扶持的友情。", all);
        Assert.Contains("上次的玩笑开过头了", all);
        Assert.DoesNotContain("book.card.impressionEmpty", all);
    }

    [Fact]
    public void RelationshipCard_Without_Impression_Shows_Empty_Placeholder()
    {
        var lines = MemoryBookData.BuildRelationshipCard(StateWith("Penny"), "潘妮", 0, 5, Echo);
        Assert.Contains(lines, line => line.Kind == MemoryBookLineKind.Empty && line.Text.Contains("book.card.impressionEmpty"));
    }

    [Fact]
    public void Memories_Group_By_Kind_In_Canonical_Order()
    {
        var state = StateWith("Leah", s =>
        {
            s.LongTermMemories.Add(new LongTermMemoryFact { Kind = "relationship", Summary = "R", Importance = 10 });
            s.LongTermMemories.Add(new LongTermMemoryFact { Kind = "fact", Summary = "F1", Importance = 50 });
            s.LongTermMemories.Add(new LongTermMemoryFact { Kind = "fact", Summary = "F2", Importance = 90 });
            s.LongTermMemories.Add(new LongTermMemoryFact { Kind = "promise", Summary = "P", Importance = 30 });
            s.PlayerPreferenceMemories.Add(new PlayerPreferenceFact { Summary = "喜欢咖啡", Importance = 40 });
        });

        var lines = MemoryBookData.BuildMemoryLines(state, nowTotalDays: 30, Echo);
        var headers = lines.Where(line => line.Kind == MemoryBookLineKind.SectionHeader).Select(line => line.Text).ToList();

        Assert.Equal(
            ["book.memoryKind.fact", "book.memoryKind.promise", "book.memoryKind.relationship", "book.memoryKind.playerPreference"],
            headers);

        // 同组内按重要度降序。
        int f2 = lines.FindIndex(line => line.Text == "F2");
        int f1 = lines.FindIndex(line => line.Text == "F1");
        Assert.True(f2 >= 0 && f1 > f2);
        Assert.Contains(lines, line => line.Text == "喜欢咖啡");
        Assert.Contains(lines, line => line.HoverText.Contains("book.memory.hover"));
    }

    [Fact]
    public void Memories_Empty_State_Uses_Placeholder()
    {
        var lines = MemoryBookData.BuildMemoryLines(StateWith("Maru"), 5, Echo);
        MemoryBookLine single = Assert.Single(lines);
        Assert.Equal(MemoryBookLineKind.Empty, single.Kind);
        Assert.Contains("book.memories.empty", single.Text);
    }

    [Fact]
    public void Conversations_Are_Newest_First_With_Speaker_Prefixes()
    {
        var history = new StardewEventHistory { NpcName = "Abigail" };
        history.Add(
            new StardewTime(1, Season.Spring, 5, 900),
            new ConversationHistory(new List<ConversationElement>
            {
                new("早呀", true),
                new("早上好！", false)
            }));
        history.Add(
            new StardewTime(1, Season.Summer, 2, 1300),
            new ConversationHistory(new List<ConversationElement>
            {
                new("矿洞怎么样？", true),
                new("下次带你一起去。", false)
            }));

        var lines = MemoryBookData.BuildConversationLines(history, "阿比盖尔", "Yuki", Echo);

        int summerIndex = lines.FindIndex(line => line.Kind == MemoryBookLineKind.DateSeparator && line.Text.Contains("book.season.summer"));
        int springIndex = lines.FindIndex(line => line.Kind == MemoryBookLineKind.DateSeparator && line.Text.Contains("book.season.spring"));
        Assert.True(summerIndex >= 0 && springIndex > summerIndex);

        Assert.Contains(lines, line => line.Kind == MemoryBookLineKind.PlayerLine && line.Text == "Yuki: 矿洞怎么样？");
        Assert.Contains(lines, line => line.Kind == MemoryBookLineKind.NpcLine && line.Text == "阿比盖尔: 下次带你一起去。");
    }

    [Fact]
    public void Moments_Include_Experiences_Help_Requests_And_Last_Gift()
    {
        var state = StateWith("Willy", s =>
        {
            s.SharedExperiences.Add(new SharedExperienceFact
            {
                Summary = "一起去海边看了日落",
                LocationLabel = "the beach",
                LastUpdatedTotalDays = 40
            });
            s.HelpRequests.Add(new NpcHelpRequestFact { Status = "Fulfilled", Summary = "帮忙带一条沙丁鱼", CreatedTotalDays = 39 });
            s.LastGiftName = "海参";
            s.LastGiftTotalDays = 41;
        });

        var lines = MemoryBookData.BuildMomentLines(state, nowTotalDays: 42, Echo);
        string all = string.Join("\n", lines.Select(line => line.Text));

        Assert.Contains("一起去海边看了日落", all);
        Assert.Contains("book.help.fulfilled", all);
        Assert.Contains("帮忙带一条沙丁鱼", all);
        Assert.Contains("book.moments.lastGift", all);
        Assert.Contains("海参", all);
    }

    [Theory]
    [InlineData(-1, 10, "book.time.unknown")]
    [InlineData(10, 10, "book.time.today")]
    [InlineData(9, 10, "book.time.yesterday")]
    [InlineData(5, 10, "book.time.daysAgo")]
    [InlineData(0, 200, "book.time.longAgo")]
    public void DaysAgo_Formatting_Covers_Boundaries(int totalDays, int now, string expectedKey)
    {
        Assert.Contains(expectedKey, MemoryBookData.FormatDaysAgo(totalDays, now, Echo));
    }

    [Theory]
    [InlineData("fact", "fact")]
    [InlineData("Promise", "promise")]
    [InlineData("weird", "fact")]
    [InlineData(null, "fact")]
    public void MemoryKind_Normalization_Falls_Back_To_Fact(string? raw, string expected)
    {
        Assert.Equal(expected, MemoryBookData.NormalizeMemoryKind(raw));
    }
}
