using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class CompanionOutingRulesTests
{
    [Theory]
    [InlineData("companion_outing", "companion_outing")]
    [InlineData("escort_to_location", "companion_outing")]
    [InlineData("walk_together", "none")]
    public void WorldActionNormalizationMigratesOnlyDestinationOutings(string input, string expected)
    {
        Assert.Equal(expected, BehaviorValueNormalizer.NormalizeWorldActionType(input));
    }

    [Fact]
    public void MetadataParserEnforcesTwoHourMinimumForOutings()
    {
        var analysis = ValleyTalkExchangeParser.Parse(
            """
            {
              "actions": [
                {
                  "type": "companion_outing",
                  "targetLocation": "Beach",
                  "durationMinutes": 15
                }
              ]
            }
            """
        );

        var action = Assert.Single(analysis.Actions);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal(120, action.DurationMinutes);
    }

    [Theory]
    [InlineData("accepted_now", "accepted_now")]
    [InlineData("deferred", "accepted_later")]
    [InlineData("maybe", "tentative")]
    [InlineData("rejected", "declined")]
    public void MetadataParserNormalizesTravelConsent(string input, string expected)
    {
        var analysis = ValleyTalkExchangeParser.Parse(
            $$"""
            {
              "actions": [
                {
                  "type": "companion_outing",
                  "targetLocation": "Beach",
                  "travelConsent": "{{input}}"
                }
              ]
            }
            """
        );

        var action = Assert.Single(analysis.Actions);
        Assert.Equal(expected, action.TravelConsent);
    }

    [Fact]
    public void FallbackOutingDoesNotTriggerForDeferredTextbookReply()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "潘妮，现在要不要去海滩走走？",
            "现在就去吗？你真有精神……不过我得先把文森特和贾斯今天要用的课本整理好。如果你只是想认路，从镇子往南走就能到海边；清晨的海风应该很安静。等我忙完，也许可以在路上碰见你。",
            out _
        );

        Assert.False(created);
    }

    [Fact]
    public void FallbackOutingDoesNotTriggerForRainyTomorrowPlan()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "那我明天再来邀请你。",
            "今天下雨不太合适，明天如果天气好，我们再去海边吧。",
            out _
        );

        Assert.False(created);
    }

    [Fact]
    public void FallbackOutingTriggersForClearImmediateAcceptance()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "潘妮，现在要不要去海滩走走？",
            "好啊，那我们现在去海边走走吧。",
            out ValleyTalkWorldActionRequest? action
        );

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("Beach", action.TargetLocation);
        Assert.Equal("accepted_now", action.TravelConsent);
    }

    [Fact]
    public void ExplicitDeferredTravelConsentBlocksAiOutingAction()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Beach",
                TravelConsent = "accepted_later"
            }
        };

        var filtered = ConversationActionCueRules.FilterTravelActionsContradictedByVisibleDialogue(
            actions,
            "那我明天再来邀请你。",
            "今天下雨不太合适，明天如果天气好，我们再去海边吧。"
        );

        Assert.Empty(filtered);
    }

    [Fact]
    public void LegacyAiOutingActionRequiresVisibleImmediateAcceptance()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Beach"
            }
        };

        var filtered = ConversationActionCueRules.FilterTravelActionsContradictedByVisibleDialogue(
            actions,
            "潘妮，现在要不要去海滩走走？",
            "现在就去吗？我得先把课本整理好，等我忙完也许可以在路上碰见你。"
        );

        Assert.Empty(filtered);
    }

    [Fact]
    public void AcceptedNowTravelConsentKeepsAiOutingAction()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Beach",
                TravelConsent = "accepted_now"
            }
        };

        var filtered = ConversationActionCueRules.FilterTravelActionsContradictedByVisibleDialogue(
            actions,
            "潘妮，现在要不要去海滩走走？",
            "好啊，那我们现在去海边走走吧。"
        );

        var action = Assert.Single(filtered);
        Assert.Equal("companion_outing", action.Type);
    }

    [Fact]
    public void HiddenGiftActionRequiresVisibleGiftOffer()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "give_small_gift",
                ItemId = "(O)233",
                ItemLabel = "Ice Cream",
                Reason = "AI metadata requested a gift"
            }
        };

        var filtered = ConversationActionCueRules.FilterActionsContradictedByVisibleDialogue(
            actions,
            "is there anything i can help with?",
            "That's kind of you. I don't need anything heavy lifted right now."
        );

        Assert.Empty(filtered);
    }

    [Fact]
    public void HiddenGiftActionStaysWhenVisibleDialogueOffersGift()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "give_small_gift",
                Reason = "AI metadata requested a gift"
            }
        };

        var filtered = ConversationActionCueRules.FilterActionsContradictedByVisibleDialogue(
            actions,
            "Thanks for walking with me.",
            "I brought you a small gift. This is for you."
        );

        var action = Assert.Single(filtered);
        Assert.Equal("give_small_gift", action.Type);
    }

    [Fact]
    public void HiddenMoneyActionRequiresVisibleMoneyOffer()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "give_money",
                Amount = 100,
                Reason = "AI metadata requested money"
            }
        };

        var filtered = ConversationActionCueRules.FilterActionsContradictedByVisibleDialogue(
            actions,
            "is there anything i can help with?",
            "That's kind of you. I don't need anything heavy lifted right now."
        );

        Assert.Empty(filtered);
    }

    [Theory]
    [InlineData(2000, 120, true)]
    [InlineData(2100, 120, true)]
    [InlineData(2110, 120, false)]
    [InlineData(2200, 120, false)]
    public void OutingMustFitTravelAndTwoHourStay(int startTime, int stayMinutes, bool expected)
    {
        Assert.Equal(expected, CompanionOutingRules.CanFitMinimumStay(startTime, stayMinutes));
    }

    [Fact]
    public void StableAnchorChoiceIsDeterministicAndLimitedToTopThree()
    {
        int first = CompanionOutingRules.SelectStableTopCandidateIndex("Penny", "SeedShop", 42, 12);
        int second = CompanionOutingRules.SelectStableTopCandidateIndex("Penny", "SeedShop", 42, 12);

        Assert.Equal(first, second);
        Assert.InRange(first, 0, 2);
    }

    [Fact]
    public void SettledEmoteChoiceIsDeterministicAndSuppressedByNegativeContext()
    {
        int first = CompanionOutingRules.SelectSettledEmoteId(
            "Penny",
            "Beach",
            "scenic",
            42,
            warmRelationship: true,
            emotionallyComfortable: true
        );
        int second = CompanionOutingRules.SelectSettledEmoteId(
            "Penny",
            "Beach",
            "scenic",
            42,
            warmRelationship: true,
            emotionallyComfortable: true
        );
        int suppressed = CompanionOutingRules.SelectSettledEmoteId(
            "Penny",
            "Beach",
            "scenic",
            42,
            warmRelationship: true,
            emotionallyComfortable: false
        );

        Assert.Equal(first, second);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void SettledEmotesStaySparseAndMatchTheActivity()
    {
        int scenicEmotes = 0;
        int browseEmotes = 0;
        for (int day = 0; day < 200; day++)
        {
            int scenic = CompanionOutingRules.SelectSettledEmoteId(
                "Penny",
                "Beach",
                "scenic",
                day,
                warmRelationship: true,
                emotionallyComfortable: true
            );
            int browse = CompanionOutingRules.SelectSettledEmoteId(
                "Penny",
                "SeedShop",
                "browse",
                day,
                warmRelationship: true,
                emotionallyComfortable: true
            );

            Assert.Contains(scenic, new[] { 0, CompanionOutingRules.HeartEmoteId });
            Assert.Contains(browse, new[] { 0, CompanionOutingRules.ExclamationEmoteId });
            scenicEmotes += scenic == 0 ? 0 : 1;
            browseEmotes += browse == 0 ? 0 : 1;
        }

        Assert.InRange(scenicEmotes, 1, 80);
        Assert.InRange(browseEmotes, 1, 70);
    }

    [Theory]
    [InlineData("SeedShop", "Let's browse the shop.", "browse")]
    [InlineData("Beach", "Let's look at the scenery.", "scenic")]
    [InlineData("Saloon", "Let's spend some time there.", "social")]
    [InlineData("Beach", "我们去看浪吧。", "scenic")]
    [InlineData("ArchaeologyHouse", "去图书馆翻书。", "browse")]
    [InlineData("Saloon", "靠吧台聊一会。", "social")]
    [InlineData("Mountain", "在山湖边聊天。", "quiet")]
    [InlineData("FlowerDance", "沿着花舞节边缘散步。", "festival")]
    public void ActivityStyleReflectsDestinationAndConversation(string target, string reason, string expected)
    {
        Assert.Equal(expected, CompanionOutingRules.DetermineActivityStyle(target, reason));
    }

    [Fact]
    public void LibraryOutingsPreferReadingTableAnchors()
    {
        var anchors = CompanionOutingAnchorSelector.GetAuthoredAnchorPreview(
            "Penny",
            "ArchaeologyHouse",
            "browse",
            "去图书馆看书。"
        );

        var first = anchors.First();
        Assert.Contains((first.X, first.Y), new[] { (18, 14), (20, 14) });
        Assert.Contains("library", first.SemanticLabel);
    }

    [Fact]
    public void MuseumOutingsPreferExhibitAnchors()
    {
        var anchors = CompanionOutingAnchorSelector.GetAuthoredAnchorPreview(
            "Gunther",
            "ArchaeologyHouse",
            "browse",
            "Let's look at the museum exhibits."
        );

        var first = anchors.First();
        Assert.Contains((first.X, first.Y), new[] { (11, 9), (17, 9) });
        Assert.Contains("museum", first.SemanticLabel);
    }

    [Theory]
    [InlineData("Gus", "Saloon", "social", "靠吧台喝一杯。", "bar")]
    [InlineData("Emily", "Saloon", "quiet", "找张桌子坐一会。", "table")]
    [InlineData("Elliott", "Beach", "scenic", "去海边看浪。", "shore")]
    [InlineData("Willy", "Beach", "scenic", "去码头边看看。", "pier")]
    [InlineData("Linus", "Mountain", "scenic", "去山湖边聊聊。", "lake")]
    [InlineData("Harvey", "Hospital", "quiet", "去诊所候诊区坐一下。", "clinic")]
    [InlineData("Clint", "Blacksmith", "browse", "去铁匠铺看看熔炉。", "forge")]
    [InlineData("Willy", "FishShop", "browse", "去鱼店柜台看看。", "counter")]
    [InlineData("Penny", "Town", "social", "去镇中心喷泉附近走走。", "fountain")]
    public void AuthoredAnchorsPreferRequestedDestinationFocus(
        string npc,
        string target,
        string style,
        string reason,
        string expectedLabelFragment)
    {
        var anchors = CompanionOutingAnchorSelector.GetAuthoredAnchorPreview(npc, target, style, reason);

        var first = anchors.First();
        Assert.Contains(expectedLabelFragment, first.SemanticLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SveTownStrollUsesSveTownCenterAnchors()
    {
        var anchors = CompanionOutingAnchorSelector.GetAuthoredAnchorPreview(
            "Penny",
            "Town",
            "quiet",
            "Would you like to take a stroll around town with me?",
            useSveTownAnchors: true
        );

        var first = anchors.First();
        Assert.Contains((first.X, first.Y), new[] { (66, 60), (72, 54), (76, 54), (59, 47) });
        Assert.Contains("SVE", first.SemanticLabel);
    }

    [Fact]
    public void TimeMathTracksTwoGameHours()
    {
        Assert.Equal(1520, BehaviorTimeMath.AddMinutesToTime(1320, 120));
        Assert.Equal(120, BehaviorTimeMath.GetElapsedMinutes(1320, 1520));
    }
}
