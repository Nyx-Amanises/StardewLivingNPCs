using LivingNPCs.Behavior.Ui;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;
using StardewValley;

namespace LivingNPCs.Tests;

public sealed class MemoryBookConversationNameTests
{
    [Theory]
    [InlineData("小刚", "早呀，@。", "早呀，小刚。")]
    [InlineData("Alex", "Hello, @.", "Hello, Alex.")]
    [InlineData("小刚", "早呀，@小刚。", "早呀，小刚。")]
    [InlineData("小刚", "早呀，小刚@。", "早呀，小刚。")]
    [InlineData("小刚", "早呀，@  小刚。", "早呀，小刚。")]
    [InlineData("小刚", "早呀，小刚  @。", "早呀，小刚。")]
    [InlineData("Alex", "Hello, @Alex.", "Hello, Alex.")]
    [InlineData("Alex", "Hello, Alex @.", "Hello, Alex.")]
    [InlineData("Alex", "@Alex今天也很有精神呢。", "Alex今天也很有精神呢。")]
    public void NpcPlaceholderUsesTheLocalPlayerNameExactlyOnce(string farmerName, string raw, string expected)
    {
        var history = HistoryWith(new ConversationElement(raw, false));

        var lines = MemoryBookData.BuildConversationLines(history, "潘妮", farmerName, Echo);

        MemoryBookLine npcLine = Assert.Single(lines, line => line.Kind == MemoryBookLineKind.NpcLine);
        Assert.Equal($"潘妮: {expected}", npcLine.Text);
    }

    [Theory]
    [InlineData("Mary Jane", "Hello, @Mary Jane.", "Hello, Mary Jane.")]
    [InlineData("A.*[雪](1)+?", "你好，A.*[雪](1)+? @。", "你好，A.*[雪](1)+?。")]
    public void PlayerNamesWithSpacesOrRegexCharactersAreMatchedLiterally(string farmerName, string raw, string expected)
    {
        var history = HistoryWith(new ConversationElement(raw, false));

        var lines = MemoryBookData.BuildConversationLines(history, "潘妮", farmerName, Echo);

        Assert.Equal($"潘妮: {expected}", Assert.Single(lines, line => line.Kind == MemoryBookLineKind.NpcLine).Text);
    }

    [Fact]
    public void NameAtTheStartOfAnEnglishWordDoesNotConsumeThePlaceholder()
    {
        var history = HistoryWith(new ConversationElement("Great work, @ Same time tomorrow?", false));

        var lines = MemoryBookData.BuildConversationLines(history, "Penny", "Sam", Echo);

        Assert.Equal("Penny: Great work, Sam Same time tomorrow?", Assert.Single(lines, line => line.Kind == MemoryBookLineKind.NpcLine).Text);
    }

    [Fact]
    public void ExplicitNicknameIsPreservedInsteadOfBeingReplacedWithThePlayerName()
    {
        var history = HistoryWith(new ConversationElement("小刚，谢谢你陪我散步。", false));

        var lines = MemoryBookData.BuildConversationLines(history, "潘妮", "测试mod", Echo);

        Assert.Equal("潘妮: 小刚，谢谢你陪我散步。", Assert.Single(lines, line => line.Kind == MemoryBookLineKind.NpcLine).Text);
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@Alex")]
    [InlineData("Write to alex@example.com.")]
    public void PlayerInputDoesNotExpandAtSignsMentionsOrEmailAddresses(string raw)
    {
        var history = HistoryWith(new ConversationElement(raw, true));

        var lines = MemoryBookData.BuildConversationLines(history, "潘妮", "Alex", Echo);

        Assert.Equal($"Alex: {raw}", Assert.Single(lines, line => line.Kind == MemoryBookLineKind.PlayerLine).Text);
    }

    [Fact]
    public void RenderingForDifferentPlayersDoesNotMutateHistoryOrReuseAnEarlierName()
    {
        const string rawNpc = "skip#早呀，@。$h";
        const string rawPlayer = "@，邮箱是 farmer@example.com。";
        var npc = new ConversationElement(rawNpc, false);
        var player = new ConversationElement(rawPlayer, true);
        var history = HistoryWith(player, npc);

        var first = MemoryBookData.BuildConversationLines(history, "潘妮", "小刚", Echo);
        var second = MemoryBookData.BuildConversationLines(history, "潘妮", "Alex", Echo);

        Assert.Equal("潘妮: 早呀，小刚。", Assert.Single(first, line => line.Kind == MemoryBookLineKind.NpcLine).Text);
        Assert.Equal("潘妮: 早呀，Alex。", Assert.Single(second, line => line.Kind == MemoryBookLineKind.NpcLine).Text);
        Assert.Equal($"小刚: {rawPlayer}", Assert.Single(first, line => line.Kind == MemoryBookLineKind.PlayerLine).Text);
        Assert.Equal($"Alex: {rawPlayer}", Assert.Single(second, line => line.Kind == MemoryBookLineKind.PlayerLine).Text);
        Assert.Equal(rawNpc, npc.Text);
        Assert.Equal(rawPlayer, player.Text);
    }

    private static StardewEventHistory HistoryWith(params ConversationElement[] elements)
    {
        var history = new StardewEventHistory { NpcName = "Penny" };
        history.Add(new StardewTime(1, Season.Summer, 6, 900), new ConversationHistory(elements.ToList()));
        return history;
    }

    private static string Echo(string key, object? tokens = null) => key;
}
