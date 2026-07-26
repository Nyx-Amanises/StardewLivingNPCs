using System.Collections.Generic;
using LivingNPCs.Behavior;
using Xunit;

namespace LivingNPCs.Tests;

/// <summary>
/// The accepted_now travel veto and its coherence with the dialogue-close verdict.
///
/// Regression background (Haley, 2026-07-27 smoke session): the model answered an outing
/// invitation with companion_outing + accepted_now twice, the dialogue engine force-closed the
/// conversation on that verdict, and the behavior-side visible-dialogue filter then vetoed the
/// action because the cheerful reply happened to contain a broad rejection substring — the player
/// watched the NPC agree, the dialogue snap shut, and nothing happen. accepted_now now only
/// yields to explicit, phrase-anchored contradictions, both layers share one verdict, and every
/// drop reports which fragment vetoed it.
/// </summary>
public class TravelActionVetoCoherenceTests
{
    private static ValleyTalkWorldActionRequest OutingAction(
        string consent = "accepted_now",
        string target = "Beach")
    {
        return new ValleyTalkWorldActionRequest
        {
            Type = "companion_outing",
            TargetLocation = target,
            TravelConsent = consent,
            Reason = "test outing"
        };
    }

    private static IReadOnlyList<ValleyTalkWorldActionRequest> Filter(
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse,
        ICollection<string>? reasons = null)
    {
        return ConversationActionCueRules.FilterActionsContradictedByVisibleDialogue(
            new[] { action }, playerText, npcResponse, reasons);
    }

    [Theory]
    [InlineData("好呀！走吧，晚点海滩人就多了。")]
    [InlineData("太好了，说不定还能看到日落呢！")]
    [InlineData("我可不能错过这样的天气，出发！")]
    [InlineData("嗯，可能有点晒，记得带水哦。走吧！")]
    [InlineData("好啊！下次也带上艾米丽吧，现在就出发！")]
    public void AcceptedNowSurvivesHedgingDecoration(string npcResponse)
    {
        var kept = Filter(OutingAction(), "我们去海边走走吧", npcResponse);
        Assert.Single(kept);
    }

    [Theory]
    [InlineData("今天不行，改天吧。", "今天不行")]
    [InlineData("嗯……还是算了吧，我有点累。", "还是算了")]
    [InlineData("下次一定陪你去！", "下次一定")]
    [InlineData("Sorry, I can't go right now.", "can't go")]
    public void AcceptedNowYieldsToExplicitRefusal(string npcResponse, string expectedCue)
    {
        var reasons = new List<string>();
        var kept = Filter(OutingAction(), "我们去海边走走吧", npcResponse, reasons);

        Assert.Empty(kept);
        string reason = Assert.Single(reasons);
        Assert.Contains("companion_outing", reason);
        Assert.Contains(expectedCue, reason);
    }

    [Fact]
    public void AcceptedNowYieldsToPlayerFuturePlan()
    {
        var reasons = new List<string>();
        var kept = Filter(OutingAction(), "我们明天去海边吧", "好呀！", reasons);

        Assert.Empty(kept);
        Assert.Contains("明天去", Assert.Single(reasons));
    }

    [Fact]
    public void NonImmediateConsentIsDroppedWithReason()
    {
        var reasons = new List<string>();
        var kept = Filter(OutingAction(consent: "accepted_later"), "我们去海边吧", "好呀，回头一起去！", reasons);

        Assert.Empty(kept);
        Assert.Contains("accepted_later", Assert.Single(reasons));
    }

    [Fact]
    public void PreparationWordingDoesNotVetoAcceptedNow()
    {
        var kept = Filter(OutingAction(), "我们去海边吧", "稍等，我拿把伞就走！");
        Assert.Single(kept);
    }

    [Fact]
    public void ExplicitDeferralSurvivesPreparationStripping()
    {
        var reasons = new List<string>();
        var kept = Filter(OutingAction(), "我们去海边吧", "稍等……还是改天吧。", reasons);

        Assert.Empty(kept);
        Assert.Contains("改天吧", Assert.Single(reasons));
    }

    [Fact]
    public void LegacyPathWithoutConsentStillRequiresVisibleInvitation()
    {
        var reasons = new List<string>();
        var kept = Filter(
            OutingAction(consent: ""),
            "今天天气真好啊",
            "是呀，很适合出门。",
            reasons);

        Assert.Empty(kept);
        Assert.Contains("no visible immediate invitation", Assert.Single(reasons));
    }

    [Fact]
    public void LegacyPathWithoutConsentStillVetoesUncertainty()
    {
        var reasons = new List<string>();
        var kept = Filter(
            OutingAction(consent: ""),
            "一起去海边吧！",
            "也许吧……嗯，我们走吧。",
            reasons);

        Assert.Empty(kept);
        Assert.Contains("也许", Assert.Single(reasons));
    }

    [Fact]
    public void TravelExperienceQuestionStillDropsEvenWithAcceptedNow()
    {
        var reasons = new List<string>();
        var kept = Filter(OutingAction(), "你去过姜岛吗？", "去过一次，特别美！", reasons);

        Assert.Empty(kept);
        Assert.Contains("without inviting", Assert.Single(reasons));
    }

    [Theory]
    [InlineData("好呀！走吧，晚点海滩人就多了。", true)]
    [InlineData("太好了，说不定还能看到日落呢！", true)]
    [InlineData("今天不行，改天吧。", false)]
    [InlineData("稍等……还是改天吧。", false)]
    public void DialogueCloseVerdictMatchesExecutionVerdict(string npcResponse, bool expected)
    {
        bool close = ConversationActionCueRules.IsConfirmedImmediateOutingAcceptance(
            "Beach", "accepted_now", "我们去海边走走吧", npcResponse);
        var kept = Filter(OutingAction(), "我们去海边走走吧", npcResponse);

        Assert.Equal(expected, close);
        Assert.Equal(expected, kept.Count == 1);
    }

    [Theory]
    [InlineData("", "accepted_now")]
    [InlineData("NotARealPlace", "accepted_now")]
    [InlineData("Beach", "accepted_later")]
    public void CloseVerdictRejectsInvalidTargetOrConsent(string target, string consent)
    {
        Assert.False(ConversationActionCueRules.IsConfirmedImmediateOutingAcceptance(
            target, consent, "我们去海边吧", "好呀，我们走吧！"));
    }
}
