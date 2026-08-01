using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>
/// Regression tests for the preparation-phrase exemption in
/// <see cref="ConversationActionCueRules.LooksLikeDeferredOrRejectedTravel"/> (#7): preparation
/// wording ("坐一会儿再走吧" / "稍等，我拿把伞") overlaps rejection fragments such as "一会儿再"
/// and used to cancel an outing the NPC had just agreed to. Brief preparation still counts as
/// accepted_now, but no longer creates a game-time delay; explicit deferrals ("改天吧") remain
/// rejections.
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
    public void PreparationWordingNoLongerCreatesTravelDelay()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "要不要现在一起去海边？",
            "稍等，我拿把伞，那我们走吧。",
            out ValleyTalkWorldActionRequest? action);

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal(0, action.DelayMinutes);
    }

    [Fact]
    public void GoingForAWhileIsDurationWordingNotPreparation()
    {
        Assert.False(ConversationActionCueRules.HasBriefPreparationWording(
            "那我们走吧，只去一会儿就回来。"));

        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "现在陪我去农场吧。",
            "好啊，那我们走吧，只去一会儿。",
            out ValleyTalkWorldActionRequest? action);

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("Farm", action.TargetLocation);
        Assert.Equal(0, action.DelayMinutes);
    }
}
