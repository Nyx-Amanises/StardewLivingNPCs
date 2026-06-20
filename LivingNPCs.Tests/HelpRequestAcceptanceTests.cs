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

    [Theory]
    [InlineData("你能帮我找点黄水仙吗？")]
    [InlineData("能不能给我带些面包。")]
    [InlineData("帮我留意一下石英好吗？")]
    [InlineData("Could you bring me some quartz?")]
    public void RecognizesItemFavorRequest(string text)
    {
        Assert.True(HelpRequestMemoryService.LooksLikeItemFavorRequested(text));
    }

    [Theory]
    [InlineData("今天天气真好。")]
    [InlineData("我很喜欢黄水仙。")]
    [InlineData("你要不要顺便带点面包？我早上刚烤了一些，还热着呢。")]
    [InlineData("")]
    public void IgnoresNonFavorChatter(string text)
    {
        Assert.False(HelpRequestMemoryService.LooksLikeItemFavorRequested(text));
    }

    [Theory]
    [InlineData("你要不要顺便带点面包？我早上刚烤了一些，还热着呢。")]
    [InlineData("给你，还热乎乎的呢。")]
    public void RecognizesNpcOfferingItemToFarmer(string text)
    {
        Assert.True(HelpRequestMemoryService.LooksLikeNpcOfferingItemToFarmer(text));
    }
    [Theory]
    [InlineData("我之后会带给你的，我家里有一块。", "farmer confirms having Quartz at home")]
    [InlineData("到了农场我再拿给你。", "NPC says that is all they need")]
    [InlineData("I'll bring it later; I have one at home.", "farmer will bring Quartz later")]
    public void DoesNotTreatFutureItemPromiseAsDelivered(string playerText, string resolution)
    {
        var request = new NpcHelpRequestFact
        {
            Type = "item_request",
            RequestedItemId = "(O)80",
            RequestedItemLabel = "Quartz"
        };

        Assert.False(HelpRequestMemoryService.LooksLikeFarmerDeliveredHelpRequestItem(request, playerText, resolution));
    }

    [Theory]
    [InlineData("给你，这就是你要的石英。")]
    [InlineData("Here you go, I brought it.")]
    public void RecognizesImmediateItemDelivery(string playerText)
    {
        var request = new NpcHelpRequestFact
        {
            Type = "item_request",
            RequestedItemId = "(O)80",
            RequestedItemLabel = "Quartz"
        };

        Assert.True(HelpRequestMemoryService.LooksLikeFarmerDeliveredHelpRequestItem(request, playerText, string.Empty));
    }
}
