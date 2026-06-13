using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class HelpRequestAcceptanceTests
{
    [Theory]
    [InlineData("好的，我帮你找找石英。")]
    [InlineData("可以，交给我。")]
    [InlineData("没问题，我看看。")]
    [InlineData("我会帮你留意的，但今天可能找不到。")] // hedged agreement: timing caveat must not read as refusal
    [InlineData("Sure, I'll keep an eye out for it.")]
    public void RecognizesAcceptance(string playerText)
    {
        Assert.True(HelpRequestMemoryService.LooksLikeFarmerAcceptingHelp(playerText));
    }

    [Theory]
    [InlineData("算了，这次就不用了。")]
    [InlineData("下次再说吧。")]
    [InlineData("我现在没办法。")]
    [InlineData("不用了，谢谢。")]
    [InlineData("Maybe later.")]
    [InlineData("")]
    public void RejectsNonAcceptance(string playerText)
    {
        Assert.False(HelpRequestMemoryService.LooksLikeFarmerAcceptingHelp(playerText));
    }
}
