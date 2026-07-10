using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Diagnostics;

namespace LivingNPCs.Tests.Dialogue;

public sealed class ConversationTextPostProcessorTests
{
    [Fact]
    public void CollapsesEmilyFullNpcLineFollowedByExpandedDisplayPages()
    {
        var lines = new[]
        {
            new ConversationTurn("谢谢你的祝福，@！$h#$b#在这个特别的日子里，能在一大早收到你的问候，真是一个充满积极能量的开始。海莉还在屋里睡觉呢。", false, "full"),
            new ConversationTurn("谢谢你的祝福，测试mod！", false, "page1"),
            new ConversationTurn("在这个特别的日子里，能在一大早收到你的问候，真是一个充满积极能量的开始。海莉还在屋里睡觉呢。", false, "page2"),
            new ConversationTurn("我准备去农场干活。", true, "player")
        };

        var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
            lines,
            turn => turn.Text,
            turn => turn.IsPlayerLine);

        Assert.Equal(new[] { "full", "player" }, cleaned.Select(turn => turn.Id));
    }

    [Fact]
    public void DoesNotCollapseSimilarNpcLinesWhenPagesDoNotExactlyMatch()
    {
        var lines = new[]
        {
            new ConversationTurn("第一句。#$b#第二句。", false, "full"),
            new ConversationTurn("第一句。", false, "page1"),
            new ConversationTurn("不同的第二句。", false, "different")
        };

        var cleaned = ConversationTurnDeduplicator.CollapseExpandedNpcPages(
            lines,
            turn => turn.Text,
            turn => turn.IsPlayerLine);

        Assert.Equal(3, cleaned.Count);
    }

    [Theory]
    [InlineData("听你这么说$h#$b看来我没看错人。#$e对了，你发现石英了吗？", "听你这么说$h#$b#看来我没看错人。#$e#对了，你发现石英了吗？")]
    [InlineData("#$b开头也要正常", "#$b#开头也要正常")]
    public void NormalizesUnclosedDialogueBreakCommands(string input, string expected)
    {
        Assert.Equal(expected, ConversationTextPostProcessor.NormalizeStardewDialogueCommands(input));
    }
    [Theory]
    [InlineData("zh-CN", "That pink cake you gave me was seriously the cutest thing ever. I found this gorgeous sunflower and thought of you.", true)]
    [InlineData("zh-CN", "那块粉红蛋糕真的太可爱了。我给你寄了这朵 sunflower，希望它能让房间亮一点。", false)]
    [InlineData("en-US", "这封信应该是中文，所以英文环境要拒绝。", true)]
    [InlineData("en-US", "That pink cake was lovely, so I sent this sunflower back.", false)]
    public void DetectsWrongLanguageForConfiguredLocale(string locale, string text, bool expected)
    {
        Assert.Equal(expected, ConversationTextPostProcessor.LooksLikeWrongLanguage(text, locale));
    }
}
