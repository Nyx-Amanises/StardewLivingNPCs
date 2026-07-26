using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for the preparation-phrase exemption in
/// <see cref="ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel"/> (#7): preparation
/// wording ("坐一会儿再走吧" / "稍等，我拿把伞") overlaps rejection fragments such as "一会儿再"
/// and used to cancel an outing the NPC had just agreed to; the exemption was dead code behind
/// an early return. The exemption only applies when stripping the preparation phrases removes
/// every rejection cue, so explicit deferrals ("改天吧") — with or without preparation wording —
/// still count as rejections.
/// </summary>
public sealed class TravelPreparationExemptionTests
{
    [Fact]
    public void PreparationWordingWithImmediatePlanIsNotARejection()
    {
        // "坐一会儿再走吧" contains the rejection fragment "一会儿再" purely through the
        // preparation phrase "一会儿"; the reply as a whole commits to going now.
        Assert.False(ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel(
            "要不要现在一起去海边？",
            "坐一会儿再走吧，我们这就去海边。"));
    }

    [Fact]
    public void PreparationWordingKeepsAcceptedOutingActionAlive()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Beach",
                TravelConsent = "accepted_now",
                Reason = "同意现在一起去海边"
            }
        };

        var filtered = ConversationActionCueRules.FilterTravelActionsContradictedByVisibleDialogue(
            actions,
            "要不要现在一起去海边？",
            "坐一会儿再走吧，我们这就去海边。");

        var action = Assert.Single(filtered);
        Assert.Equal("companion_outing", action.Type);
    }

    [Fact]
    public void PlainDeferralIsStillARejection()
    {
        Assert.True(ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel(
            "要不要现在一起去海边？",
            "改天吧。"));
    }

    [Fact]
    public void ExplicitDeferralAlongsidePreparationWordingStaysARejection()
    {
        // The preparation phrase must not whitewash a real deferral in the same reply.
        Assert.True(ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel(
            "要不要现在一起去海边？",
            "稍等……我想了想，还是改天再去吧。"));
    }

    [Fact]
    public void FuturePlanAlongsidePreparationWordingStaysARejection()
    {
        Assert.True(ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel(
            "要不要现在一起去海边？",
            "稍等，明天吧，明天我们再去。"));
    }

    [Fact]
    public void PreparationWordingStillYieldsTheDelay()
    {
        Assert.Equal(10, ConversationActionCueRules.DetectPreparationDelayMinutes("坐一会儿再走吧，我们这就去海边。"));
        Assert.Equal(0, ConversationActionCueRules.DetectPreparationDelayMinutes("改天吧。"));
    }
}
