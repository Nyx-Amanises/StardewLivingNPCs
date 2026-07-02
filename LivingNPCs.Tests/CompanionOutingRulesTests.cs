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
    public void MetadataParserAllowsShortEscortOutings()
    {
        var analysis = ValleyTalkExchangeParser.Parse(
            """
            {
              "actions": [
                {
                  "type": "companion_outing",
                  "targetLocation": "Beach",
                  "durationMinutes": 5
                }
              ]
            }
            """
        );

        var action = Assert.Single(analysis.Actions);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal(CompanionOutingRules.MinimumShortVisitMinutes, action.DurationMinutes);
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
        Assert.Equal(CompanionOutingRules.MinimumStayMinutes, action.DurationMinutes);
    }

    [Fact]
    public void FallbackOutingTriggersForBeachAcceptanceWithSideFarewell()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "潘妮，现在要不要去海滩？",
            "好啊……去海滩。卡门，我们先走啦，改天再聊。好了，那我们走吧，去海边。",
            out ValleyTalkWorldActionRequest? action
        );

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("Beach", action.TargetLocation);
        Assert.Equal("accepted_now", action.TravelConsent);
    }

    [Fact]
    public void FallbackOutingTriggersForApologeticBeachAcceptance()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "昨天的事，我很抱歉。现在你愿意陪我去看海吗？",
            "既然你说抱歉，也愿意陪我去看海，那我想，我愿意再信你一次。我们走吧。",
            out ValleyTalkWorldActionRequest? action
        );

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("Beach", action.TargetLocation);
        Assert.Equal("accepted_now", action.TravelConsent);
    }

    [Theory]
    [InlineData("潘妮，现在去图书馆吗？", "好啊，那我们现在去图书馆吧。", "ArchaeologyHouse")]
    [InlineData("要不要去酒吧坐坐？", "可以呀，那我们现在去沙龙。", "Saloon")]
    [InlineData("你愿意陪我去森林看看吗？", "当然可以，我们走吧。", "Forest")]
    public void FallbackOutingTriggersForTargetedSupportedLocations(string playerText, string npcResponse, string expectedTarget)
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            playerText,
            npcResponse,
            out ValleyTalkWorldActionRequest? action
        );

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal(expectedTarget, action.TargetLocation);
        Assert.Equal("accepted_now", action.TravelConsent);
    }

    [Fact]
    public void FallbackOutingTriggersForFarmInvitationWithSneakingOutAcceptance()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "阿比盖尔，现在想来我的农场冒险吗？",
            "现在？一大早的就来邀请我去农场冒险，你可真是挑了个好时候。$h#$e#不过说真的，我一直挺想去看看你把那片荒地盘成什么样了。#$b#等我一下，我拿件外套，趁老爸还没叫我摆货架，咱们赶紧溜。",
            out ValleyTalkWorldActionRequest? action
        );

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("Farm", action.TargetLocation);
        Assert.Equal("accepted_now", action.TravelConsent);
        Assert.Equal(10, action.DelayMinutes);
    }

    [Fact]
    public void FallbackOutingUsesShortDurationForEscortRequests()
    {
        bool created = ConversationActionCueRules.TryBuildFallbackTravelActionForTesting(
            "潘妮，可以带我去图书馆吗？我有点认路。",
            "当然可以，那我们现在去图书馆吧。",
            out ValleyTalkWorldActionRequest? action
        );

        Assert.True(created);
        Assert.NotNull(action);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("ArchaeologyHouse", action.TargetLocation);
        Assert.Equal("accepted_now", action.TravelConsent);
        Assert.Equal(CompanionOutingRules.DefaultShortVisitMinutes, action.DurationMinutes);
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
    public void VisibleTargetCorrectionIgnoresFarmSourceAside()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Town",
                TravelConsent = "accepted_now",
                Reason = "同意在当前公共地点一起停留"
            }
        };

        ConversationActionCueRules.TryCorrectTravelActionTargetFromVisibleDialogueForTesting(
            actions,
            "哈，随便你怎么说了，不过，我能和你一起在这待一会吗？",
            "嗯……可以。这里是公共长椅，又不是我家的客厅。#$b#不过你要是刚从农场过来，最好别把泥蹭到我鞋边。今天这双颜色很难配的。"
        );

        var action = Assert.Single(actions);
        Assert.Equal("Town", action.TargetLocation);
        Assert.DoesNotContain("Farm", action.Reason);
    }

    [Fact]
    public void VisibleTargetCorrectionFillsMissingTargetFromExplicitFarmInvitation()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "",
                TravelConsent = "accepted_now",
                Reason = "同意一起离开当前地点"
            }
        };

        ConversationActionCueRules.TryCorrectTravelActionTargetFromVisibleDialogueForTesting(
            actions,
            "阿比盖尔，现在想来我的农场冒险吗？",
            "好啊，去你的农场冒险听起来比站在店里有意思多了。我们走吧。"
        );

        var action = Assert.Single(actions);
        Assert.Equal("Farm", action.TargetLocation);
        Assert.Contains("visible dialogue supplied missing destination as Farm", action.Reason);
    }

    [Fact]
    public void AcceptedNowTravelConsentDoesNotKeepLocalCompanyAsOuting()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Town",
                TravelConsent = "accepted_now",
                Reason = "同意一起在公共地点停留"
            }
        };

        var filtered = ConversationActionCueRules.FilterTravelActionsContradictedByVisibleDialogue(
            actions,
            "哈，随便你怎么说了，不过，我能和你一起在这待一会吗？",
            "嗯……可以。这里是公共长椅，又不是我家的客厅。#$b#不过你要是刚从农场过来，最好别把泥蹭到我鞋边。今天这双颜色很难配的。"
        );

        Assert.Empty(filtered);
    }

    [Fact]
    public void AcceptedNowTravelConsentDoesNotKeepTravelExperienceQuestionAsOuting()
    {
        var actions = new[]
        {
            new ValleyTalkWorldActionRequest
            {
                Type = "companion_outing",
                TargetLocation = "Farm",
                TravelConsent = "accepted_now",
                Reason = "误把去过农场的问题当成出游"
            }
        };

        var filtered = ConversationActionCueRules.FilterTravelActionsContradictedByVisibleDialogue(
            actions,
            "海莉，你以前去过我的农场吗？",
            "去过一次，不过那边的泥巴真的不太适合这双鞋。"
        );

        Assert.Empty(filtered);
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
    [InlineData(2100, 60, true)]
    [InlineData(2200, 60, true)]
    [InlineData(2210, 60, false)]
    [InlineData(2300, 60, false)]
    public void OutingMustFitTravelAndConfiguredStay(int startTime, int stayMinutes, bool expected)
    {
        Assert.Equal(expected, CompanionOutingRules.CanFitMinimumStay(startTime, stayMinutes));
    }

    [Theory]
    [InlineData(2330, 20, true)]
    [InlineData(2400, 20, false)]
    public void ShortEscortOutingsUseShorterTimeWindow(int startTime, int stayMinutes, bool expected)
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
    public void FarmOutingsUseLiveMapScanInsteadOfVanillaAuthoredAnchors()
    {
        var anchors = CompanionOutingAnchorSelector.GetAuthoredAnchorPreview(
            "Abigail",
            "Farm",
            "scenic",
            "去我的农场看看。"
        );

        Assert.Empty(anchors);
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
            useSveAnchors: true
        );

        var first = anchors.First();
        Assert.Contains((first.X, first.Y), new[] { (66, 60), (72, 54), (76, 54), (59, 47) });
        Assert.Contains("SVE", first.SemanticLabel);
    }

    [Theory]
    [InlineData("Beach", "scenic", "Let's watch the waves.", "SVE")]
    [InlineData("Forest", "quiet", "Let's walk somewhere quiet in the forest.", "SVE")]
    [InlineData("Mountain", "scenic", "Let's go to the mountain lake.", "SVE")]
    [InlineData("BusStop", "quiet", "Let's wait near the bus stop.", "SVE")]
    public void SveOutdoorMapsUseSveSpecificAnchors(
        string target,
        string style,
        string reason,
        string expectedLabelFragment)
    {
        var anchors = CompanionOutingAnchorSelector.GetAuthoredAnchorPreview(
            "Penny",
            target,
            style,
            reason,
            useSveAnchors: true
        );

        var first = anchors.First();
        Assert.Contains(expectedLabelFragment, first.SemanticLabel);
    }

    [Fact]
    public void TimeMathTracksTwoGameHours()
    {
        Assert.Equal(1520, BehaviorTimeMath.AddMinutesToTime(1320, 120));
        Assert.Equal(120, BehaviorTimeMath.GetElapsedMinutes(1320, 1520));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(45, 60)]
    [InlineData(60, 60)]
    [InlineData(90, 90)]
    [InlineData(1200, 600)]
    public void FullOutingStayLengthComesFromConfigOnly(int configuredStayMinutes, int expected)
    {
        Assert.Equal(
            expected,
            CompanionOutingRules.GetFixedFullOutingStayMinutes(configuredStayMinutes));
    }
}
