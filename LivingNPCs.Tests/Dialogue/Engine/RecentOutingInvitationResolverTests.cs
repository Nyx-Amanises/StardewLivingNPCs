using System.Collections.Generic;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class RecentOutingInvitationResolverTests
{
    [Fact]
    public void PendingQuestionDoesNotRecoverTargetOrCloseAgreementEarly()
    {
        ConversationAnalysis analysis = AcceptedOuting(target: string.Empty, delayMinutes: 10);
        DialogueContext context = Context(
            Player("不会的呀，对了潘妮，你现在要不要和我一起去我的农场玩？"),
            Npc("今天可能不太方便。明天或者改天可以吗？"),
            Player("不嘛不嘛，陪我去吧，花不了多少时间的"));

        RecentOutingInvitationResolver.NormalizeCompanionOutingActions(
            analysis,
            context,
            "不过，既然小刚这么想让我去，那我只去一会儿……这样可以吗？");

        ConversationWorldActionRequest action = Assert.Single(analysis.Actions);
        Assert.Equal(string.Empty, action.TargetLocation);
        Assert.Equal("tentative", action.TravelConsent);
        Assert.Equal(0, action.DelayMinutes);
        Assert.False(RecentOutingInvitationResolver.IsDecisiveDepartureCommitment(
            "不过，既然小刚这么想让我去，那我只去一会儿……这样可以吗？"));
    }

    [Fact]
    public void FinalDepartureRecoversFarmFromRecentExplicitInvitation()
    {
        ConversationAnalysis analysis = AcceptedOuting(target: string.Empty, delayMinutes: 10);
        DialogueContext context = PennyFarmNegotiation();

        RecentOutingInvitationResolver.NormalizeCompanionOutingActions(
            analysis,
            context,
            "那我们走吧，小刚。就去一会儿……我正好也想多陪陪你。");

        ConversationWorldActionRequest action = Assert.Single(analysis.Actions);
        Assert.Equal("Farm", action.TargetLocation);
        Assert.Equal(0, action.DelayMinutes);
        Assert.Contains("recent explicit player invitation", action.Reason);
    }

    [Fact]
    public void BackgroundFarmMentionDoesNotBecomeDestination()
    {
        ConversationAnalysis analysis = AcceptedOuting(target: string.Empty);
        DialogueContext context = Context(
            Player("我刚从农场回来，今天真累。"),
            Npc("那你应该先休息一下。"),
            Player("太好了，我们走吧。"));

        RecentOutingInvitationResolver.NormalizeCompanionOutingActions(
            analysis,
            context,
            "那我们走吧。现在出发。");

        Assert.Equal(string.Empty, Assert.Single(analysis.Actions).TargetLocation);
    }

    [Fact]
    public void MultipleDestinationsDoNotRecoverAnAmbiguousTarget()
    {
        ConversationAnalysis analysis = AcceptedOuting(target: string.Empty);
        DialogueContext context = Context(
            Player("你要不要和我一起去农场，还是去海边？"),
            Npc("你决定就好。"),
            Player("好，那我们走吧。"));

        RecentOutingInvitationResolver.NormalizeCompanionOutingActions(
            analysis,
            context,
            "那我们走吧。现在出发。");

        Assert.Equal(string.Empty, Assert.Single(analysis.Actions).TargetLocation);
    }

    [Fact]
    public void ExistingSupportedTargetIsNormalizedAndNeverOverwrittenByHistory()
    {
        ConversationAnalysis analysis = AcceptedOuting(target: "Beach", delayMinutes: 10);
        DialogueContext context = PennyFarmNegotiation();

        RecentOutingInvitationResolver.NormalizeCompanionOutingActions(
            analysis,
            context,
            "那我们走吧。现在出发。");

        ConversationWorldActionRequest action = Assert.Single(analysis.Actions);
        Assert.Equal("Beach", action.TargetLocation);
        Assert.Equal(0, action.DelayMinutes);
    }

    [Fact]
    public void EnglishFarmAliasDoesNotMatchFarmerWord()
    {
        Assert.Empty(TravelLocationRules.FindMentionedPublicOutingTargets(
            "The farmer is ready, so let's go."));
        Assert.Equal(
            new[] { "Farm" },
            TravelLocationRules.FindMentionedPublicOutingTargets(
                "Would you come with me to the farm?"));
    }

    private static DialogueContext PennyFarmNegotiation()
    {
        return Context(
            Player("不会的呀，对了潘妮，你现在要不要和我一起去我的农场玩？"),
            Npc("去你的农场？我很想去……不过今天可能不太方便。明天或者改天可以吗？"),
            Player("不嘛不嘛，陪我去吧，花不了多少时间的"),
            Npc("不过，既然小刚这么想让我去，那我只去一会儿……这样可以吗？"),
            Player("太好了，我们走吧！"));
    }

    private static ConversationAnalysis AcceptedOuting(string target, int delayMinutes = 0)
    {
        return new ConversationAnalysis
        {
            Actions = new List<ConversationWorldActionRequest>
            {
                new()
                {
                    Type = "companion_outing",
                    TravelConsent = "accepted_now",
                    TargetLocation = target,
                    DelayMinutes = delayMinutes,
                    DurationMinutes = 60,
                    Reason = "the NPC accepted the outing"
                }
            }
        };
    }

    private static DialogueContext Context(params ConversationElement[] turns)
    {
        return new DialogueContext { ChatHistory = new List<ConversationElement>(turns) };
    }

    private static ConversationElement Player(string text) => new(text, true);

    private static ConversationElement Npc(string text) => new(text, false);
}
