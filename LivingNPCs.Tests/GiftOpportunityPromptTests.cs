using LivingNPCs.Behavior;
using Xunit;

namespace LivingNPCs.Tests;

public sealed class GiftOpportunityPromptTests
{
    [Fact]
    public void ActiveOpportunityIsSingleReplySmallGiftOnly()
    {
        string prompt = PromptFragments.GiftOpportunity.Section(
            "Haley",
            "Haley may offer a gift",
            "Bread (O)216",
            "Rhubarb Pie (O)222");

        Assert.Contains("this one reply only", prompt);
        Assert.Contains("at most one small in-game gift", prompt);
        Assert.Contains("Do not upgrade it to give_meaningful_gift", prompt);
        Assert.Contains("copy both itemId and its matching itemLabel", prompt);
        Assert.Contains("Never invent jewelry", prompt);
    }

    [Fact]
    public void MissingOpportunityExplicitlyForbidsImmediateGifts()
    {
        string prompt = PromptFragments.GiftOpportunity.NoOpportunitySection();

        Assert.Contains("No immediate NPC gift is authorized", prompt);
        Assert.Contains("no give_small_gift or give_meaningful_gift action", prompt);
        Assert.Contains("gifts for other people", prompt);
        Assert.Contains("failed gift offer", prompt);
    }

    [Theory]
    [InlineData("给你一条蓝玉髓手链。")]
    [InlineData("I made this bracelet for you.")]
    [InlineData("这对耳环很衬你，收下吧。")]
    public void UnsupportedWearableGiftPromisesAreRejected(string reply)
    {
        Assert.True(GiftActionRules.VisibleDialoguePromisesUnsupportedGift(reply));
    }
}
