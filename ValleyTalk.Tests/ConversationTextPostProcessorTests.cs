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
}