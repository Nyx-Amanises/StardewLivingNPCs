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
}
