using ValleyTalk;
using Xunit;

namespace ValleyTalk.Tests;

public sealed class ConversationAnalysisTests
{
    [Fact]
    public void EmptyParseResultsDoNotShareMutableMetadata()
    {
        var firstEmpty = ConversationAnalysis.Parse(string.Empty);
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"actions":[{"type":"companion_outing","targetLocation":"Beach","travelConsent":"accepted_now","durationMinutes":60}]}
        """);

        Assert.True(firstEmpty.MergeSupplementalActionMetadata(supplemental));

        var secondEmpty = ConversationAnalysis.Parse("plain dialogue with no metadata");

        Assert.Empty(secondEmpty.Actions);
        Assert.False(secondEmpty.HasWorldActionOrHelpMetadata);
    }

    [Fact]
    public void ActionDecisionFilterRemovesLocalCompanyOuting()
    {
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"actions":[{"type":"companion_outing","targetLocation":"Town","travelConsent":"accepted_now","durationMinutes":60,"reason":"同意一起在公共长椅待一会"}]}
        """);

        bool filtered = LivingNpcActionDecisionPass.FilterNonInvitationOutingActionsForTesting(
            supplemental,
            "哈，随便你怎么说了，不过，我能和你一起在这待一会吗？",
            "嗯……可以。这里是公共长椅，又不是我家的客厅。#$b#不过你要是刚从农场过来，最好别把泥蹭到我鞋边。今天这双颜色很难配的。",
            out string detail);

        Assert.True(filtered);
        Assert.Empty(supplemental.Actions);
        Assert.Contains("filtered 1 companion_outing", detail);
    }

    [Fact]
    public void ActionDecisionFilterKeepsExplicitFarmOuting()
    {
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"actions":[{"type":"companion_outing","targetLocation":"Farm","travelConsent":"accepted_now","durationMinutes":60,"reason":"同意去农场冒险"}]}
        """);

        bool filtered = LivingNpcActionDecisionPass.FilterNonInvitationOutingActionsForTesting(
            supplemental,
            "阿比盖尔，现在想来我的农场冒险吗？",
            "好啊，去你的农场冒险听起来比站在店里有意思多了。我们走吧。",
            out _);

        Assert.False(filtered);
        var action = Assert.Single(supplemental.Actions);
        Assert.Equal("Farm", action.TargetLocation);
    }

    [Fact]
    public void ActionDecisionFilterRemovesTravelExperienceQuestionOuting()
    {
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"actions":[{"type":"companion_outing","targetLocation":"Farm","travelConsent":"accepted_now","durationMinutes":60,"reason":"误把去过农场的问题当成出游"}]}
        """);

        bool filtered = LivingNpcActionDecisionPass.FilterNonInvitationOutingActionsForTesting(
            supplemental,
            "海莉，你以前去过我的农场吗？",
            "去过一次，不过那边的泥巴真的不太适合这双鞋。",
            out string detail);

        Assert.True(filtered);
        Assert.Empty(supplemental.Actions);
        Assert.Contains("did not ask to leave", detail);
    }

    [Fact]
    public void ActionDecisionGiftDecisionAddsImmediateGiftAction()
    {
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"travelDecision":{"isTravelReply":false,"consent":"none"},"giftDecision":{"isGiftReply":true,"timing":"now","tier":"small","itemId":"(O)20","itemLabel":"韭葱","reason":"NPC visibly offers a leek now"},"actions":[]}
        """);

        bool added = LivingNpcActionDecisionPass.TryAddActionFromGiftDecisionForTesting(
            supplemental,
            """{"giftDecision":{"isGiftReply":true,"timing":"now","tier":"small","itemId":"(O)20","itemLabel":"韭葱","reason":"NPC visibly offers a leek now"}}""",
            "我这里还有一些多出来的韭葱。你要是正好需要的话，我可以先送给你。",
            out string detail);

        Assert.True(added);
        var action = Assert.Single(supplemental.Actions);
        Assert.Equal("give_small_gift", action.Type);
        Assert.Equal("(O)20", action.ItemId);
        Assert.Equal("韭葱", action.ItemLabel);
        Assert.Contains("converted giftDecision", detail);
    }

    [Fact]
    public void ActionDecisionGiftDecisionDoesNotAddLaterMailGift()
    {
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"travelDecision":{"isTravelReply":false,"consent":"none"},"giftDecision":{"isGiftReply":true,"timing":"mail","tier":"meaningful","itemId":"","itemLabel":"","reason":"NPC promises to mail a return gift later"},"actions":[]}
        """);

        bool added = LivingNpcActionDecisionPass.TryAddActionFromGiftDecisionForTesting(
            supplemental,
            """{"giftDecision":{"isGiftReply":true,"timing":"mail","tier":"meaningful","reason":"NPC promises to mail a return gift later"}}""",
            "作为回礼，我想晚点寄给你一些我自己做的东西，希望你会喜欢。",
            out string detail);

        Assert.False(added);
        Assert.Empty(supplemental.Actions);
        Assert.Contains("not immediate", detail);
    }

    [Fact]
    public void ActionDecisionGiftDecisionRequiresNamedItemInVisibleReply()
    {
        var supplemental = ConversationAnalysis.Parse("""
        !LIVINGNPCS_META {"giftDecision":{"isGiftReply":true,"timing":"now","tier":"small","itemId":"(O)20","itemLabel":"韭葱"},"actions":[]}
        """);

        bool added = LivingNpcActionDecisionPass.TryAddActionFromGiftDecisionForTesting(
            supplemental,
            """{"giftDecision":{"isGiftReply":true,"timing":"now","tier":"small","itemId":"(O)20","itemLabel":"韭葱"}}""",
            "这个小东西给你，希望你会喜欢。",
            out string detail);

        Assert.False(added);
        Assert.Empty(supplemental.Actions);
        Assert.Contains("visible reply did not", detail);
    }
}
