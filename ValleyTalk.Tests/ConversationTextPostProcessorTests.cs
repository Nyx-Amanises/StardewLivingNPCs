using Xunit;
using ValleyTalk;

namespace ValleyTalk.Tests;

public sealed class ConversationTextPostProcessorTests
{
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
