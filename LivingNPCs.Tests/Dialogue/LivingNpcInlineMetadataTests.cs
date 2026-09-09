using System;
using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using Xunit;

namespace LivingNPCs.Tests.Dialogue;

public sealed class LivingNpcInlineMetadataTests
{
    [Theory]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 你好。\r\n% 继续聊。\r\n!LIVINGNPCS_META {\"complete\":true}\r\n")]
    [InlineData("- 你好。\n  !LIVINGNPCS_META{\"complete\":true}  ")]
    [InlineData("- 你好。\n!LIVINGNPCS_META\n{\n  \"complete\":true\n}\n")]
    public void CompleteMetadataTail_IsAuthoritativeAndNotAnEffect(string raw)
    {
        LivingNpcMetadataExtractionResult result = Parse(raw);

        Assert.True(result.Success, result.FailureReason);
        Assert.False(result.Analysis.HasContent);
        Assert.DoesNotContain("complete", result.Analysis.ToJson());
        Assert.Equal(raw, result.RawResponse);
    }

    [Fact]
    public void BraceTextInVisibleDialogue_DoesNotReplaceTheFinalMetadata()
    {
        var result = Parse("- 这块牌子上写着 {\"complete\":true,\"rapportDelta\":30}。\n!LIVINGNPCS_META {\"complete\":true,\"rapportDelta\":2}");

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal(2, result.Analysis.RapportDelta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("- 你好。")]
    [InlineData("{\"complete\":true}")]
    [InlineData("- 牌子上写着 {\"complete\":true,\"rapportDelta\":30}。")]
    [InlineData("!LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("\n  !LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 我说 !LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 你好。\n\"!LIVINGNPCS_META {\"complete\":true}\"")]
    [InlineData("- 你好。\n% !LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 你好。\n!!LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 你好。\n!livingnpcs_meta {\"complete\":true}")]
    [InlineData("- 你好。\n!LIVINGNPCS_METADATA {\"complete\":true}")]
    [InlineData("- 你好。\n!LIVINGNPCS_META")]
    [InlineData("- 你好。\n!LIVINGNPCS_META ")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true} {}")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true}\nmore dialogue")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true}\n% another choice")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true}\n!LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 示例 !LIVINGNPCS_META {}。\n!LIVINGNPCS_META {\"complete\":true}")]
    [InlineData("- 你好。\n```json\n!LIVINGNPCS_META {\"complete\":true}\n```")]
    [InlineData("- 你好。\n!LIVINGNPCS_META [{\"complete\":true}]")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true,}")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {'complete':true}")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {complete:true}")]
    [InlineData("- 你好。\n!LIVINGNPCS_META {\"complete\":true /* comment */}")]
    public void MissingFakeTruncatedOrNonFinalEnvelope_IsNotAuthoritative(string raw)
    {
        LivingNpcMetadataExtractionResult result = Parse(raw);

        Assert.False(result.Success);
        Assert.False(result.Analysis.HasContent);
        Assert.NotEmpty(result.FailureReason);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"rapportDelta\":2}")]
    [InlineData("{\"complete\":false}")]
    [InlineData("{\"complete\":\"true\"}")]
    [InlineData("{\"complete\":1}")]
    [InlineData("{\"complete\":null}")]
    [InlineData("{\"Complete\":true}")]
    [InlineData("{\"complete\":true,\"complete\":true}")]
    [InlineData("{\"complete\":false,\"complete\":true}")]
    [InlineData("{\"complete\":true,\"rapportdelta\":2}")]
    [InlineData("{\"complete\":true,\"rapportDelta\":\"2\"}")]
    [InlineData("{\"complete\":true,\"rapportDelta\":2147483648}")]
    [InlineData("{\"complete\":true,\"actions\":[{\"type\":\"give_money\",\"money\":5}]}")]
    [InlineData("{\"complete\":true,\"actions\":[{\"type\":\"give_money\",\"amount\":1.5}]}")]
    [InlineData("{\"complete\":true,\"helpRequests\":[{\"steps\":[{\"item\":\"flower\"}]}]}")]
    [InlineData("{\"complete\":true,\"emotionImpact\":{\"emotion\":\"happy\",\"emotion\":\"angry\"}}")]
    public void MissingCompletionOrInvalidSparseFields_StillRequireClassifierFallback(string json)
    {
        LivingNpcMetadataExtractionResult result = Parse("- 你好。\n!LIVINGNPCS_META " + json);

        Assert.False(result.Success);
        Assert.False(result.Analysis.HasContent);
    }

    [Fact]
    public void LegacyFullSchemaWithoutCompletion_CannotBypassTheClassifier()
    {
        string json = """
            {"rapportDelta":0,"endConversation":false,"ambientFollowUp":{"text":"","delayMinutes":0},"emotionImpact":{"emotion":"none","intensityDelta":0,"apology":false,"repairDelta":0,"reason":""},"behaviorInfluences":[],"actions":[],"conflicts":[],"memories":[],"helpRequests":[],"helpRequestUpdates":[],"travelDecision":{"isTravelReply":false,"consent":"none"},"giftDecision":{"isGiftReply":false,"timing":"none","tier":"small"}}
            """;

        LivingNpcMetadataExtractionResult result = Parse("- 你好。\n!LIVINGNPCS_META " + json);

        Assert.False(result.Success);
        Assert.Contains("requires complete:true", result.FailureReason);
        Assert.True(LivingNpcMetadataExtractionPass.ParseAuthoritativeResponseForTesting(json, "你好。", "你好。").Success);
    }

    [Fact]
    public void JsonEscapesAndBracesInStrings_ArePreserved()
    {
        string json = """{"complete":true,"memories":[{"kind":"fact","summary":"农夫会把标签写成 {\"花\"}。","importance":30,"subject":"标签"}]}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 我记住了。\n!LIVINGNPCS_META " + json,
            "我会把标签写成花。", "我记住了。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal("农夫会把标签写成 {\"花\"}。", Assert.Single(result.Analysis.Memories).Summary);
    }

    [Fact]
    public void RealLowScalePreferences_RequireFallbackWithoutChangingStandaloneClassification()
    {
        const string player = "我最喜欢向日葵，看见它们就心情好。不过我不喜欢啤酒的苦味，记住啦。";
        const string reply = "记住了：向日葵会让你开心，啤酒则完全不行。其实我也觉得，苦味根本配不上这么好的春天。";
        // Actual synthetic-scenario model output: these scores pass the schema but both fail
        // the runtime's durable-memory gate. It must not certify a one-request success.
        const string json = """
            {"complete":true,"memories":[{"kind":"fact","summary":"农夫喜欢向日葵，见到它们会心情好。","importance":3,"playerPreference":true,"playerPreferenceKind":"liked_item_category","subject":"农夫","tags":["向日葵","喜好"]},{"kind":"fact","summary":"农夫不喜欢啤酒的苦味。","importance":2,"playerPreference":true,"playerPreferenceKind":"disliked_item","subject":"农夫","tags":["啤酒","口味"]}]}
            """;
        string raw = "- " + reply + "\n!LIVINGNPCS_META " + json;

        var inline = LivingNpcMetadataExtractionPass.ParseInlineResponse(raw, player, reply, new DialogueContext());

        Assert.False(inline.Success);
        Assert.False(inline.Analysis.HasContent);
        Assert.Contains("player preference", inline.FailureReason);
        Assert.Contains("importance < 40", inline.FailureReason);
        Assert.Equal(raw, inline.RawResponse);

        // This is an inline completeness guard, not a change to the standalone classifier's
        // compatible parser or an automatic importance boost.
        var standalone = LivingNpcMetadataExtractionPass.ParseAuthoritativeResponseForTesting(json, player, reply);
        Assert.True(standalone.Success, standalone.FailureReason);
        Assert.Collection(standalone.Analysis.Memories,
            memory =>
            {
                Assert.True(memory.PlayerPreference);
                Assert.Equal(3, memory.Importance);
                Assert.Equal("农夫喜欢向日葵，见到它们会心情好。", memory.Summary);
            },
            memory =>
            {
                Assert.True(memory.PlayerPreference);
                Assert.Equal(2, memory.Importance);
                Assert.Equal("农夫不喜欢啤酒的苦味。", memory.Summary);
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(39)]
    public void LowImportancePreferenceAmongValidEffects_RequiresTheCompleteFallback(int lowImportance)
    {
        string json = $$"""
            {"complete":true,"rapportDelta":2,"memories":[{"kind":"preference","summary":"农夫喜欢向日葵。","importance":70,"playerPreference":true,"playerPreferenceKind":"liked_item_category","subject":"向日葵"},{"kind":"preference","summary":"农夫不喜欢啤酒。","importance":{{lowImportance}},"playerPreference":true,"playerPreferenceKind":"disliked_item","subject":"啤酒"}]}
            """;

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 我会记住这两个喜好的。\n!LIVINGNPCS_META " + json,
            "我喜欢向日葵，不喜欢啤酒。", "我会记住这两个喜好的。", new DialogueContext());

        Assert.False(result.Success);
        Assert.False(result.Analysis.HasContent);
        Assert.Contains("player preference", result.FailureReason);
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(60, 70)]
    public void StorageEligiblePreferences_PreserveScoresAndReachThePreferenceStore(int sunflowerImportance, int beerImportance)
    {
        string json = $$"""
            {"complete":true,"memories":[{"kind":"preference","summary":"农夫喜欢向日葵，见到它们会心情好。","importance":{{sunflowerImportance}},"playerPreference":true,"playerPreferenceKind":"liked_item_category","subject":"向日葵","tags":["sunflower","flower"]},{"kind":"preference","summary":"农夫不喜欢啤酒的苦味。","importance":{{beerImportance}},"playerPreference":true,"playerPreferenceKind":"disliked_item","subject":"啤酒","tags":["beer"]}]}
            """;

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 向日葵和啤酒，我都记住了。\n!LIVINGNPCS_META " + json,
            "我喜欢向日葵，不喜欢啤酒。", "向日葵和啤酒，我都记住了。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Collection(result.Analysis.Memories,
            memory => Assert.Equal(sunflowerImportance, memory.Importance),
            memory => Assert.Equal(beerImportance, memory.Importance));
        var behavior = ValleyTalkExchangeParser.Parse(result.Analysis.ToJson());
        var state = new LivingNpcState { NpcName = "Haley" };
        // Match the exchange application gate, then call the actual pure preference store on
        // a fresh in-memory state. No Stardew save or Game1 state is needed for this path.
        foreach (var memory in behavior.Memories
                     .Where(memory => memory.Importance >= 40 && !string.IsNullOrWhiteSpace(memory.Summary))
                     .OrderByDescending(memory => memory.Importance)
                     .Take(4))
        {
            Assert.True(memory.PlayerPreference);
            Assert.True(PlayerPreferenceMemoryStore.Store(state, memory, currentTotalDays: 23, currentTimeOfDay: 1400));
        }

        Assert.Equal(2, state.PlayerPreferenceMemories.Count);
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "liked_item_category"
            && memory.Subject == "向日葵" && memory.Importance == sunflowerImportance);
        Assert.Contains(state.PlayerPreferenceMemories, memory => memory.PreferenceKind == "disliked_item"
            && memory.Subject == "啤酒" && memory.Importance == beerImportance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(39)]
    public void LowImportanceOrdinaryFacts_RemainCompatibleWithoutPromotion(int importance)
    {
        string json = $$"""
            {"complete":true,"memories":[{"kind":"fact","summary":"农夫今天看过喷泉旁的花。","importance":{{importance}},"playerPreference":false,"subject":"喷泉旁的花"}]}
            """;

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 那些花今天开得不错。\n!LIVINGNPCS_META " + json,
            "我刚看过喷泉旁的花。", "那些花今天开得不错。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        var memory = Assert.Single(result.Analysis.Memories);
        Assert.False(memory.PlayerPreference);
        Assert.Equal(importance, memory.Importance);
    }

    [Fact]
    public void ImmediateGiftDecision_UsesTheSameEvidenceCheckedActionConversion()
    {
        string json = """{"complete":true,"giftDecision":{"isGiftReply":true,"timing":"now","tier":"small","itemId":"(O)20","itemLabel":"韭葱","reason":"现在送出"}}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 这根韭葱送给你。\n!LIVINGNPCS_META " + json,
            "早上好。", "这根韭葱送给你。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal("(O)20", Assert.Single(result.Analysis.Actions).ItemId);
    }

    [Theory]
    [InlineData("{\"complete\":true}")]
    [InlineData("{\"complete\":true,\"rapportDelta\":2,\"actions\":[]}")]
    [InlineData("{\"complete\":true,\"actions\":[{\"type\":\"give_money\",\"amount\":20}]}")]
    [InlineData("{\"complete\":true,\"actions\":[{\"type\":\"unsupported_gift\",\"itemId\":\"(O)20\"}]}")]
    [InlineData("{\"complete\":true,\"giftDecision\":{\"isGiftReply\":true,\"timing\":\"later\",\"itemId\":\"(O)20\",\"itemLabel\":\"韭葱\"}}")]
    [InlineData("{\"complete\":true,\"giftDecision\":{\"isGiftReply\":true,\"timing\":\"now\",\"itemId\":\"(O)245\",\"itemLabel\":\"糖\"}}")]
    public void ImmediateGiftWithoutNormalizedGiftAction_RequiresClassifierFallback(string json)
    {
        const string reply = "给，这根韭葱送给你，就当是谢礼。";
        string raw = "- " + reply + "\n!LIVINGNPCS_META " + json;

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            raw, "早上好。", reply, new DialogueContext());

        Assert.False(result.Success);
        Assert.Contains("immediate gift action", result.FailureReason);
        Assert.False(result.Analysis.HasContent);
        Assert.Equal(raw, result.RawResponse);
    }

    [Theory]
    [InlineData("give_small_gift", "give_small_gift")]
    [InlineData(" GIVE_SMALL_GIFT ", "give_small_gift")]
    [InlineData("give_meaningful_gift", "give_meaningful_gift")]
    public void ImmediateGiftWithNormalizedAction_RemainsAuthoritative(string type, string expectedType)
    {
        string json = "{\"complete\":true,\"actions\":[{\"type\":\"" + type + "\",\"itemId\":\"(O)20\",\"itemLabel\":\"韭葱\"}]}";
        const string reply = "这根韭葱送给你。";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- " + reply + "\n!LIVINGNPCS_META " + json, "早上好。", reply, new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        ConversationWorldActionRequest action = Assert.Single(result.Analysis.Actions);
        Assert.Equal(expectedType, action.Type);
        Assert.Equal("(O)20", action.ItemId);
    }

    [Theory]
    [InlineData("我这里有一块派，分给你一块吧！")]
    [InlineData("拿着吧，这根韭葱刚摘下来。")]
    [InlineData("收下吧，这根韭葱刚摘下来。")]
    [InlineData("请收下这根韭葱吧。")]
    [InlineData("Take this leek.")]
    [InlineData("Here, take this leek.")]
    [InlineData("This is for you. Take this leek.")]
    public void ClearImmediateHandoffWithNoEffects_CannotSkipTheClassifier(string reply)
    {
        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- " + reply + "\n!LIVINGNPCS_META {\"complete\":true}", "早上好。", reply, new DialogueContext());

        Assert.False(result.Success);
        Assert.Empty(result.Analysis.Actions);
    }

    [Theory]
    [InlineData("谢谢，我就收下了。")]
    [InlineData("谢谢，那我拿着了。")]
    [InlineData("I'll take this, thanks.")]
    [InlineData("I’ll take this, thanks.")]
    [InlineData("Thanks, I'll take this.")]
    public void NpcAcceptingFarmerGift_DoesNotRequireAnOutgoingGiftClassifier(string reply)
    {
        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- " + reply + "\n!LIVINGNPCS_META {\"complete\":true}",
            "这根韭葱送给你。", reply, new DialogueContext { Accept = "(O)20" });

        Assert.True(result.Success, result.FailureReason);
        Assert.Empty(result.Analysis.Actions);
    }

    [Theory]
    [InlineData("你的礼物我收下了。这个韭葱送给你。")]
    [InlineData("I'll take this, thanks. Take this leek in return.")]
    public void NpcAcceptingThenReciprocatingGift_StillRequiresTheOutgoingAction(string reply)
    {
        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- " + reply + "\n!LIVINGNPCS_META {\"complete\":true}",
            "这个小礼物送给你。", reply, new DialogueContext { Accept = "(O)402" });

        Assert.False(result.Success);
        Assert.Contains("immediate gift action", result.FailureReason);
        Assert.Empty(result.Analysis.Actions);
    }

    [Theory]
    [InlineData("这根韭葱我明天送给你。")]
    [InlineData("我明天给你寄点韭葱。")]
    [InlineData("晚点送给你吧。")]
    [InlineData("我不能给你这个小礼物。")]
    [InlineData("我不会送给你这个。")]
    [InlineData("I have something for you, but I'll mail it tomorrow.")]
    [InlineData("You can have this another day, not today.")]
    [InlineData("你准备给贾斯带礼物吗？")]
    [InlineData("我喜欢小礼物。")]
    [InlineData("你最喜欢哪种小礼物？")]
    [InlineData("昨天收到的小礼物真不错。")]
    [InlineData("That was a thoughtful small gift.")]
    public void DeferredRejectedOrOrdinaryGiftTopic_DoesNotRequireAnotherClassifier(string reply)
    {
        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- " + reply + "\n!LIVINGNPCS_META {\"complete\":true}", "早上好。", reply, new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Empty(result.Analysis.Actions);
    }

    [Fact]
    public void GiftOfferInPlayerTextOrHypotheticalOptions_IsNotAnNpcGiftOffer()
    {
        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 今天过得怎么样？\n% 这个小礼物送给你。\n!LIVINGNPCS_META {\"complete\":true}",
            "这根韭葱送给你。", "今天过得怎么样？", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Empty(result.Analysis.Actions);
    }

    [Fact]
    public void DeferredGift_DoesNotBecomeAnImmediateAction()
    {
        string json = """{"complete":true,"giftDecision":{"isGiftReply":true,"timing":"mail","itemId":"(O)20","itemLabel":"韭葱"}}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 我明天给你寄点韭葱。\n!LIVINGNPCS_META " + json,
            "早上好。", "我明天给你寄点韭葱。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Empty(result.Analysis.Actions);
    }

    [Fact]
    public void ImmediateTravelDecision_PreservesTheAcceptedDestinationAndZeroDelay()
    {
        string json = """{"complete":true,"travelDecision":{"isTravelReply":true,"consent":"accepted_now","targetLocation":"Beach","delayMinutes":10,"durationMinutes":60}}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 好呀，我们现在就去海边。\n!LIVINGNPCS_META " + json,
            "我们一起去海边吧。", "好呀，我们现在就去海边。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        ConversationWorldActionRequest action = Assert.Single(result.Analysis.Actions);
        Assert.Equal("companion_outing", action.Type);
        Assert.Equal("Beach", action.TargetLocation);
        Assert.Equal(0, action.DelayMinutes);
    }

    [Fact]
    public void StayingHere_DoesNotBecomeAnOutingJustBecauseInlineMetadataSaysSo()
    {
        string json = """{"complete":true,"actions":[{"type":"companion_outing","targetLocation":"Farm","travelConsent":"accepted_now","durationMinutes":60}]}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 可以，就坐在这儿吧。\n!LIVINGNPCS_META " + json,
            "我能一起在这里坐一会吗？", "可以，就坐在这儿吧。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Empty(result.Analysis.Actions);
    }

    [Fact]
    public void MultiItemHelpRequest_PreservesTheCompleteOrderedSteps()
    {
        string json = """{"complete":true,"helpRequests":[{"type":"item_request","summary":"带一朵甜豌豆和糖","requiresAcceptance":true,"steps":[{"type":"item_request","summary":"先带甜豌豆","requestedItemId":"(O)402","requestedItemLabel":"甜豌豆"},{"type":"item_request","summary":"再带糖","requestedItemId":"(O)245","requestedItemLabel":"糖"}],"dueInDays":1}]}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 先帮我带一朵甜豌豆，再带一份糖，两样都需要。\n!LIVINGNPCS_META " + json,
            "有什么需要帮忙的吗？", "先帮我带一朵甜豌豆，再带一份糖，两样都需要。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.Collection(Assert.Single(result.Analysis.HelpRequests).Steps,
            step => Assert.Equal("(O)402", step.RequestedItemId),
            step => Assert.Equal("(O)245", step.RequestedItemId));
    }

    [Fact]
    public void BlockedVisibleReply_DoesNotBypassTheExistingClassifierBoundary()
    {
        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- Ridgeside Village.\n!LIVINGNPCS_META {\"complete\":true}",
            "你好。", "Ridgeside Village.", new DialogueContext());

        Assert.False(result.Success);
        Assert.Contains("blocked", result.FailureReason);
    }

    [Fact]
    public void InlineEmotionStillPassesConservativeInterpersonalEvidenceRules()
    {
        string json = """{"complete":true,"emotionImpact":{"emotion":"angry","intensityDelta":15,"reason":"flustered by a sincere compliment"},"conflicts":[{"causeKind":"boundary","summary":"mild teasing","severity":20}]}""";

        var result = LivingNpcMetadataExtractionPass.ParseInlineResponse(
            "- 别这么突然夸我嘛。\n!LIVINGNPCS_META " + json,
            "你今天真好看。", "别这么突然夸我嘛。", new DialogueContext());

        Assert.True(result.Success, result.FailureReason);
        Assert.NotEqual("Angry", result.Analysis.EmotionImpact.Emotion);
        Assert.Empty(result.Analysis.Conflicts);
    }

    [Fact]
    public void InlineInstructionsShareTheClassifierContractAndPreserveVisibleConstraints()
    {
        string inline = LivingNpcMetadataExtractionPass.BuildInlineInstructions();
        string classifier = LivingNpcMetadataExtractionPass.BuildPromptForTesting(
            new Character("Haley"), new DialogueContext(), "你好。", "早上好。", Array.Empty<string>());
        const string contractStart = "Evaluate every metadata category below";
        const string contractEnd = "- giftDecision is immediate only when the NPC visibly offers an item now; mail, later, and promises create no gift action.";
        int start = classifier.IndexOf(contractStart, StringComparison.Ordinal);
        int end = classifier.IndexOf(contractEnd, StringComparison.Ordinal) + contractEnd.Length;

        Assert.Contains(classifier[start..end], inline);
        Assert.Contains("one line beginning with - .", inline);
        Assert.Contains("each beginning with % .", inline);
        Assert.Contains("exactly one final hidden line beginning with !LIVINGNPCS_META", inline);
        Assert.Contains("complete:true metadata line is required", inline);
        Assert.Contains("only when the supplied context explicitly allows that opportunity and item", inline);
        Assert.Contains("Never append an optional or bonus item", inline);
        Assert.Contains("a brief wait before that departure still counts as now", inline);
        Assert.Contains("Never invent an item, destination, reward, task, or world action", inline);
        Assert.Contains("routine pleasant small talk 0-2", inline);
        Assert.DoesNotContain("Output no markdown, explanation, or dialogue", inline);
        Assert.DoesNotContain("Return exactly one line beginning with !LIVINGNPCS_META", inline);
    }

    private static LivingNpcMetadataExtractionResult Parse(string raw) =>
        LivingNpcMetadataExtractionPass.ParseInlineResponse(raw, "你好。", "你好。", new DialogueContext());
}
