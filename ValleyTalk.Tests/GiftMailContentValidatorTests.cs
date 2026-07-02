using Xunit;
using ValleyTalk;

namespace ValleyTalk.Tests;

public sealed class GiftMailContentValidatorTests
{
    [Fact]
    public void AcceptsCleanBodyAndConvertsNewlinesToCarets()
    {
        bool ok = GiftMailContentValidator.TryNormalize(
            "@，\n谢谢你送来的木头。\n我随信回赠一点心意。",
            out string body,
            out string reason);

        Assert.True(ok, reason);
        Assert.Contains("^", body);
        Assert.DoesNotContain("\n", body);
    }

    [Fact]
    public void TrimsWrappingQuotes()
    {
        bool ok = GiftMailContentValidator.TryNormalize(
            "\"Thank you for the wood. I am sending a little something back.\"",
            out string body,
            out _);

        Assert.True(ok);
        Assert.False(body.StartsWith("\""));
        Assert.False(body.EndsWith("\""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("太短")]
    [InlineData("Thanks %item id (O)388 1 %% for the help, here is a gift in return.")]
    [InlineData("Thank you [#] for the wood, I am returning the favor with a small gift.")]
    [InlineData("Here is your letter: {{item}} enclosed with thanks for the kind gift.")]
    [InlineData("As an AI language model, I cannot write a letter, but here is some text anyway.")]
    [InlineData("对不起，我无法完成这个请求，不过这里有一些文字内容可以参考使用。")]
    public void RejectsUnusableOutput(string raw)
    {
        bool ok = GiftMailContentValidator.TryNormalize(raw, out _, out string reason);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Theory]
    [InlineData("抱歉这么晚才回礼，前阵子实在太忙了。请收下这份小心意，希望你会喜欢。")]
    [InlineData("对不起，前几天说话重了些。这份礼物算是我的一点歉意，别往心里去。")]
    [InlineData("I'm sorry this letter took so long to reach you! The gift you sent reminded me of home.")]
    [InlineData("无法用言语表达我的感谢，只好挑了一件小东西随信寄给你，收下吧。")]
    [InlineData("I cannot thank you enough for the wood. Please accept this small token in return.")]
    public void AcceptsApologiesAndGratitudeProse(string raw)
    {
        bool ok = GiftMailContentValidator.TryNormalize(raw, out string body, out string reason);

        Assert.True(ok, reason);
        Assert.False(string.IsNullOrEmpty(body));
    }
}
