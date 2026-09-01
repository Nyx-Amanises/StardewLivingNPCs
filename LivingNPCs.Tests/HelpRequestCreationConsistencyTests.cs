using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Tests;

public sealed class HelpRequestCreationConsistencyTests
{
    private static readonly IReadOnlyList<HelpRequestItemAlias> HaleyItems =
    [
        new HelpRequestItemAlias("(O)402", "甜豌豆", ["甜豌豆", "Sweet Pea"]),
        new HelpRequestItemAlias("(O)245", "糖", ["糖", "Sugar"])
    ];

    [Fact]
    public void FindsEveryExplicitNpcRequestInVisibleOrder()
    {
        const string reply = "找一些甜豌豆花作为拍照背景；如果还能带一包糖烤甜饼就更完美。";

        IReadOnlyList<ExplicitHelpRequestItem> found =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems);

        Assert.Collection(
            found,
            item =>
            {
                Assert.Equal("(O)402", item.ItemId);
                Assert.False(item.IsOptionalAddOn);
            },
            item =>
            {
                Assert.Equal("(O)245", item.ItemId);
                Assert.True(item.IsOptionalAddOn);
            });
    }

    [Theory]
    [InlineData("糖烤甜饼很好吃。")]
    [InlineData("谢谢你带来的糖。")]
    [InlineData("我可以带糖给你。")]
    [InlineData("我喜欢甜豌豆，也喜欢糖。")]
    public void OrdinaryMentionsAndNpcOffersDoNotBecomeRequests(string reply)
    {
        Assert.Empty(HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems));
    }

    [Theory]
    [InlineData("不用帮我找甜豌豆了。")]
    [InlineData("你不要给我带糖。")]
    [InlineData("Don't bring me Sugar.")]
    [InlineData("You don't need to find a Sweet Pea.")]
    public void NegatedItemRequestsDoNotBecomeTasks(string reply)
    {
        Assert.Empty(HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems));
    }

    [Theory]
    [InlineData("不要忘了帮我带糖。", "(O)245")]
    [InlineData("Don't forget to bring me Sugar.", "(O)245")]
    public void PositiveReminderRequestsAreNotMistakenForNegation(string reply, string expectedItemId)
    {
        IReadOnlyList<ExplicitHelpRequestItem> found =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems);

        Assert.Single(found);
        Assert.Equal(expectedItemId, found[0].ItemId);
        Assert.False(found[0].IsOptionalAddOn);
    }

    [Theory]
    [InlineData("谢谢你帮我带糖。")]
    [InlineData("谢谢你帮我找到了甜豌豆。")]
    [InlineData("你之前帮我带来的糖很好。")]
    [InlineData("Thank you for bringing me Sugar.")]
    [InlineData("Thanks for helping me find a Sweet Pea.")]
    public void ThanksAndCompletedFavorsDoNotBecomeNewTasks(string reply)
    {
        Assert.Empty(HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems));
    }

    [Theory]
    [InlineData("谢谢，你能帮我带糖吗？", "(O)245")]
    [InlineData("Thanks, could you find a Sweet Pea?", "(O)402")]
    public void ThanksBeforeASeparateNewRequestDoesNotHideTheRequest(string reply, string expectedItemId)
    {
        IReadOnlyList<ExplicitHelpRequestItem> found =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems);

        Assert.Single(found);
        Assert.Equal(expectedItemId, found[0].ItemId);
        Assert.False(found[0].IsOptionalAddOn);
    }

    [Theory]
    [InlineData("请帮我找点蔓越莓糖果。")]
    [InlineData("请帮我带一根枫糖棒。")]
    [InlineData("请帮我带一瓶枫糖浆。")]
    public void SingleCharacterSugarAliasDoesNotMatchInsideAnotherItem(string reply)
    {
        Assert.Empty(HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems));
    }

    [Fact]
    public void SingleCharacterSugarAliasStillMatchesNaturalMeasuredRequest()
    {
        IReadOnlyList<ExplicitHelpRequestItem> found =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests("请帮我带一包糖来。", HaleyItems);

        Assert.Single(found);
        Assert.Equal("(O)245", found[0].ItemId);
    }

    [Fact]
    public void SingleStepMetadataCannotCoverTwoVisibleRequests()
    {
        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                "先找甜豌豆，再带糖给我。",
                HaleyItems);
        var candidate = SingleItemCandidate("(O)402", "甜豌豆");

        Assert.False(HelpRequestDialogueAnalyzer.IsCandidateSequenceConsistent(candidate, visible, maxSteps: 2));
    }

    [Fact]
    public void HiddenMetadataCannotCreateTaskWithoutVisibleRequest()
    {
        Assert.False(HelpRequestDialogueAnalyzer.IsCandidateSequenceConsistent(
            SingleItemCandidate("(O)402", "甜豌豆"),
            Array.Empty<ExplicitHelpRequestItem>(),
            maxSteps: 2));
    }

    [Fact]
    public void MultiStepMetadataMustMatchEveryVisibleItemInSpokenOrder()
    {
        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                "先找甜豌豆，再带糖给我。",
                HaleyItems);
        var correct = MultiItemCandidate(("(O)402", "甜豌豆"), ("(O)245", "糖"));
        var reversed = MultiItemCandidate(("(O)245", "糖"), ("(O)402", "甜豌豆"));

        Assert.True(HelpRequestDialogueAnalyzer.IsCandidateSequenceConsistent(correct, visible, maxSteps: 2));
        Assert.False(HelpRequestDialogueAnalyzer.IsCandidateSequenceConsistent(reversed, visible, maxSteps: 2));
    }

    [Fact]
    public void OptionalExtraItemIsNotSynthesizedIntoMandatoryQuest()
    {
        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                "找一些甜豌豆；如果还能带糖就更完美。",
                HaleyItems);

        Assert.False(HelpRequestDialogueAnalyzer.TryBuildSynthesisCandidate(
            visible,
            farmerAlreadyAgreed: false,
            maxSteps: 2,
            out _));
    }

    [Fact]
    public void OptionalVisibleItemCannotBecomeRequiredMetadataStep()
    {
        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                "找一些甜豌豆；如果还能带糖就更完美。",
                HaleyItems);

        Assert.False(HelpRequestDialogueAnalyzer.IsCandidateSequenceConsistent(
            MultiItemCandidate(("(O)402", "甜豌豆"), ("(O)245", "糖")),
            visible,
            maxSteps: 2));
    }

    [Theory]
    [InlineData("如果你能帮我带一包糖来，我会很感激。$h")]
    [InlineData("If you can bring me some Sugar, I would appreciate it.$h")]
    public void PoliteConditionalSingleRequestIsNotDeleted(string reply)
    {
        ConversationAnalysis analysis = AnalysisFor(
            ("(O)245", "糖"),
            ("(O)731", "枫糖棒"));

        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems);
        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            reply,
            analysis,
            requestableItems: HaleyItems);

        Assert.Single(visible);
        Assert.False(visible[0].IsOptionalAddOn);
        Assert.Equal(reply, cleaned);
    }

    [Fact]
    public void ExplicitlyRequiredAdditionalItemRemainsAMultiStepRequest()
    {
        const string reply = "请帮我找甜豌豆，另外帮我带糖，这两样我都需要。$h";
        ConversationAnalysis analysis = AnalysisFor(
            ("(O)402", "甜豌豆"),
            ("(O)245", "糖"));

        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(reply, HaleyItems);
        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            reply,
            analysis,
            requestableItems: HaleyItems);

        Assert.Collection(
            visible,
            item =>
            {
                Assert.Equal("(O)402", item.ItemId);
                Assert.False(item.IsOptionalAddOn);
            },
            item =>
            {
                Assert.Equal("(O)245", item.ItemId);
                Assert.False(item.IsOptionalAddOn);
            });
        Assert.True(HelpRequestDialogueAnalyzer.IsCandidateSequenceConsistent(
            MultiItemCandidate(("(O)402", "甜豌豆"), ("(O)245", "糖")),
            visible,
            maxSteps: 2));
        Assert.Equal(reply, cleaned);
    }

    [Fact]
    public void TwoRequiredItemsSynthesizeAsOneOrderedMultiStepRequest()
    {
        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                "先找甜豌豆，再带糖给我。",
                HaleyItems);

        Assert.True(HelpRequestDialogueAnalyzer.TryBuildSynthesisCandidate(
            visible,
            farmerAlreadyAgreed: false,
            maxSteps: 2,
            out ValleyTalkHelpRequestCandidate candidate));
        Assert.Collection(
            candidate.Steps,
            step => Assert.Equal("(O)402", step.RequestedItemId),
            step => Assert.Equal("(O)245", step.RequestedItemId));
    }

    [Fact]
    public void WorldStageLimitDoesNotTruncateMultiItemSynthesis()
    {
        IReadOnlyList<ExplicitHelpRequestItem> visible =
            HelpRequestDialogueAnalyzer.FindExplicitItemRequests(
                "先找甜豌豆，再带糖给我。",
                HaleyItems);

        Assert.False(HelpRequestDialogueAnalyzer.TryBuildSynthesisCandidate(
            visible,
            farmerAlreadyAgreed: false,
            maxSteps: 1,
            out _));
    }

    [Fact]
    public void RemovesOnlyUntrackedOptionalAddOnSentence()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "找一些甜豌豆花作为拍照背景；如果还能带一包糖烤甜饼就更完美。$h",
            analysis);

        Assert.Equal("找一些甜豌豆花作为拍照背景。$h", cleaned);
    }

    [Fact]
    public void RemovesOrphanedQuestionAfterHaleyOptionalSugarPage()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "唔……我最近正在找一些甜豌豆花作为拍照的背景道具。$13#$b#当然，如果你能帮我带一包糖来，让我烤点小甜饼，那就更完美了。你觉得呢？$6",
            analysis);

        Assert.Equal(
            "唔……我最近正在找一些甜豌豆花作为拍照的背景道具。$13",
            cleaned);
    }

    [Fact]
    public void RemovedHaleyAddOnAlsoReplacesCookieBoundResponseOption()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));
        HelpRequestDialogueCleanupResult cleanup = HelpRequestDialogueConsistency.Reconcile(
            "找一些甜豌豆花作为拍照背景；如果还能带一包糖烤甜饼就更完美。$h",
            analysis);
        string[] originalOptions =
        [
            "好，我会帮你找甜豌豆。",
            "我最近可能没空。",
            "烤甜饼？能分我一块吗？"
        ];

        List<string> safeOptions = HelpRequestDialogueConsistency.ReconcileOptionsAfterCleanup(
            originalOptions,
            cleanup,
            "zh-CN");

        Assert.True(cleanup.RemovedOptionalAddOn);
        Assert.Equal("找一些甜豌豆花作为拍照背景。$h", cleanup.DialogueLine);
        Assert.Equal(3, safeOptions.Count);
        Assert.DoesNotContain(safeOptions, option => option.Contains("糖", StringComparison.Ordinal));
        Assert.DoesNotContain(safeOptions, option => option.Contains("甜饼", StringComparison.Ordinal));
        Assert.Contains("抱歉，我现在做不到。", safeOptions);
        Assert.True(HelpRequestMemoryService.LooksLikeFarmerAcceptingHelp(safeOptions[0]));
        Assert.True(HelpRequestMemoryService.LooksLikeFarmerAcceptingHelp(safeOptions[1]));
        Assert.True(HelpRequestMemoryService.LooksLikeFarmerDecliningHelp(safeOptions[2]));
    }

    [Fact]
    public void EnglishReplacementOptionsAreRecognizedAsAcceptOrDecline()
    {
        HelpRequestDialogueCleanupResult cleanup = new(
            "Bring a Sweet Pea.",
            RemovedOptionalAddOn: true,
            IsRetraction: false,
            HasValidRequest: true);

        List<string> safeOptions = HelpRequestDialogueConsistency.ReconcileOptionsAfterCleanup(
            new[] { "Cookie?", "Maybe.", "No." },
            cleanup,
            "en");

        Assert.True(HelpRequestMemoryService.LooksLikeFarmerAcceptingHelp(safeOptions[0]));
        Assert.True(HelpRequestMemoryService.LooksLikeFarmerAcceptingHelp(safeOptions[1]));
        Assert.True(HelpRequestMemoryService.LooksLikeFarmerDecliningHelp(safeOptions[2]));
    }

    [Fact]
    public void RemovesWholeOptionalAddOnPageWithoutLeavingPageMarker()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "找一些甜豌豆花吧。$h#$b#如果还能带糖就更完美。$s",
            analysis);

        Assert.Equal("找一些甜豌豆花吧。$h", cleaned);
    }

    [Fact]
    public void RemovesOptionalContinuationPlacedOnFollowingPage()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我找甜豌豆。$h#$b#如果还能带糖，$s#$b#这样我能烤饼。$6",
            analysis,
            requestableItems: HaleyItems);

        Assert.Equal("请帮我找甜豌豆。$h", cleaned);
    }

    [Fact]
    public void KeepsSameRequiredItemExplanationOnFollowingPage()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));
        const string reply = "请帮我找甜豌豆。$h#$b#甜豌豆用来拍照效果会更好。$6";

        HelpRequestDialogueCleanupResult cleanup = HelpRequestDialogueConsistency.Reconcile(
            reply,
            analysis,
            requestableItems: HaleyItems);

        Assert.Equal(reply, cleanup.DialogueLine);
        Assert.False(cleanup.RemovedOptionalAddOn);
        Assert.Single(analysis.HelpRequests);
        Assert.Equal("(O)402", analysis.HelpRequests[0].RequestedItemId);
    }

    [Fact]
    public void RemovesDependentQuestionPlacedOnFollowingPage()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我找甜豌豆。$h#$b#如果还能带糖就更好了。$s#$b#你觉得呢？$6",
            analysis,
            requestableItems: HaleyItems);

        Assert.Equal("请帮我找甜豌豆。$h", cleaned);
    }

    [Fact]
    public void KeepsOrdinaryConversationAfterRemovedOptionalPage()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我找甜豌豆。$h#$b#如果还能带糖就更好了。$s#$b#今天阳光很好，我们之后去拍照吧。$6",
            analysis,
            requestableItems: HaleyItems);

        Assert.Equal(
            "请帮我找甜豌豆。$h#$b#今天阳光很好，我们之后去拍照吧。$6",
            cleaned);
    }

    [Fact]
    public void RemovesOptionalSentenceEvenWhenModelEncodedItAsRequiredStep()
    {
        ConversationAnalysis analysis = AnalysisFor(
            ("(O)402", "甜豌豆"),
            ("(O)245", "糖"));
        analysis.EmotionImpact.Reason = "讨论甜豌豆和糖";
        analysis.Memories.Add(new ConversationMemoryCandidate { Summary = "海莉想要甜豌豆和糖" });
        analysis.BehaviorInfluences.Add(new ConversationBehaviorInfluenceCandidate { Summary = "之后烤甜饼" });

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "找一些甜豌豆；如果还能带糖就更完美。$h",
            analysis);

        Assert.Equal("找一些甜豌豆。$h", cleaned);
        Assert.Single(analysis.HelpRequests);
        Assert.Empty(analysis.HelpRequests[0].Steps);
        Assert.Equal("(O)402", analysis.HelpRequests[0].RequestedItemId);
        Assert.Equal(string.Empty, analysis.EmotionImpact.Reason);
        Assert.Empty(analysis.Memories);
        Assert.Empty(analysis.BehaviorInfluences);
    }

    [Fact]
    public void RemovesOptionalAddOnWithoutMetadataAndLeavesRequiredRequest()
    {
        var analysis = new ConversationAnalysis();

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我找甜豌豆，如果还能带糖就更好了。$h",
            analysis);

        Assert.Equal("请帮我找甜豌豆。$h", cleaned);
    }

    [Fact]
    public void OptionalCommaContinuationStopsBeforeUnrelatedConversation()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我找甜豌豆，如果还能带糖就更好，今天阳光很好，我们之后去拍照吧。$h",
            analysis,
            requestableItems: HaleyItems);

        Assert.Equal("请帮我找甜豌豆，今天阳光很好，我们之后去拍照吧。$h", cleaned);
    }

    [Theory]
    [InlineData("请帮我找甜豌豆，不过糖会更好。$h", "请帮我找甜豌豆。$h")]
    [InlineData("请帮我找甜豌豆，如果有糖就更好了。$h", "请帮我找甜豌豆。$h")]
    [InlineData("Please bring a Sweet Pea, but Sugar would be better.$h", "Please bring a Sweet Pea.$h")]
    public void RemovesUnencodedItemComparisonWithoutRequestVerb(string reply, string expected)
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            reply,
            analysis,
            requestableItems: HaleyItems);

        Assert.Equal(expected, cleaned);
    }

    [Fact]
    public void OptionalOnlyRequestBecomesVisibleRetractionInsteadOfPhantomTask()
    {
        var analysis = new ConversationAnalysis();

        HelpRequestDialogueCleanupResult cleanup = HelpRequestDialogueConsistency.Reconcile(
            "如果还能帮我带一包糖来就更好了。$h",
            analysis);
        List<string> options = HelpRequestDialogueConsistency.ReconcileOptionsAfterCleanup(
            new[] { "好，我帮你找。", "抱歉。" },
            cleanup,
            "zh-CN");

        Assert.Equal("算了，暂时不用麻烦你了。$h", cleanup.DialogueLine);
        Assert.True(cleanup.IsRetraction);
        Assert.Empty(options);
        Assert.Empty(analysis.HelpRequests);
    }

    [Fact]
    public void OrdinaryDialogueBeforeRemovedOptionalRequestDoesNotKeepTaskOptions()
    {
        var analysis = new ConversationAnalysis();
        HelpRequestDialogueCleanupResult cleanup = HelpRequestDialogueConsistency.Reconcile(
            "今天天气不错。$h#$b#如果还能帮我带糖就更好了。$s",
            analysis,
            requestableItems: HaleyItems);

        List<string> options = HelpRequestDialogueConsistency.ReconcileOptionsAfterCleanup(
            new[] { "好，我帮你找。", "抱歉。" },
            cleanup,
            "zh-CN");

        Assert.Equal("今天天气不错。$h", cleanup.DialogueLine);
        Assert.False(cleanup.IsRetraction);
        Assert.False(cleanup.HasValidRequest);
        Assert.Empty(options);
    }

    [Fact]
    public void StepsRemainAuthoritativeOverStrayTopLevelItemLabel()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));
        analysis.HelpRequests[0].RequestedItemId = "(O)245";
        analysis.HelpRequests[0].RequestedItemLabel = "糖";

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "找一些甜豌豆；如果还能带糖就更完美。",
            analysis);

        Assert.Equal("找一些甜豌豆。", cleaned);
    }

    [Fact]
    public void RemovingMapleBarAddOnDoesNotRemoveTrackedSugarRequest()
    {
        IReadOnlyList<HelpRequestItemAlias> items = HaleyItems
            .Append(new HelpRequestItemAlias("(O)731", "枫糖棒", ["枫糖棒", "Maple Bar"]))
            .ToList();
        ConversationAnalysis analysis = AnalysisFor(("(O)245", "糖"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我带一包糖；如果还能带一根枫糖棒就更好了。$h",
            analysis,
            requestableItems: items);

        Assert.Equal("请帮我带一包糖。$h", cleaned);
        Assert.Single(analysis.HelpRequests);
        Assert.Equal("(O)245", analysis.HelpRequests[0].RequestedItemId);
        Assert.Single(analysis.HelpRequests[0].Steps);
        Assert.Equal("(O)245", analysis.HelpRequests[0].Steps[0].RequestedItemId);
    }

    [Fact]
    public void EncodedMapleBarAddOnIsRemovedWithoutDroppingRequiredSugar()
    {
        IReadOnlyList<HelpRequestItemAlias> items = HaleyItems
            .Append(new HelpRequestItemAlias("(O)731", "枫糖棒", ["枫糖棒", "Maple Bar"]))
            .ToList();
        ConversationAnalysis analysis = AnalysisFor(
            ("(O)245", "糖"),
            ("(O)731", "枫糖棒"));

        string cleaned = HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(
            "请帮我带一包糖；如果还能带一根枫糖棒就更好了。$h",
            analysis,
            requestableItems: items);

        Assert.Equal("请帮我带一包糖。$h", cleaned);
        Assert.Single(analysis.HelpRequests);
        Assert.Equal("(O)245", analysis.HelpRequests[0].RequestedItemId);
        Assert.Empty(analysis.HelpRequests[0].Steps);
    }

    [Fact]
    public void DoesNotDeleteOrdinaryOptionalConversation()
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));
        const string reply = "找一些甜豌豆吧。如果还能在这里坐一会儿就更完美。$h";

        Assert.Equal(
            reply,
            HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(reply, analysis));
    }

    [Theory]
    [InlineData("找一些甜豌豆吧。如果还能带着笑容来就更完美。$h")]
    [InlineData("找一些甜豌豆吧。如果还能带我去海边就更完美。$h")]
    public void DoesNotMistakeMoodOrTravelWordingForOptionalItem(string reply)
    {
        ConversationAnalysis analysis = AnalysisFor(("(O)402", "甜豌豆"));

        Assert.Equal(
            reply,
            HelpRequestDialogueConsistency.RemoveUntrackedOptionalItemAddOns(reply, analysis));
    }

    private static ValleyTalkHelpRequestCandidate SingleItemCandidate(string itemId, string label)
    {
        return new ValleyTalkHelpRequestCandidate
        {
            Type = "item_request",
            Summary = label,
            RequestedItemId = itemId,
            RequestedItemLabel = label
        };
    }

    private static ValleyTalkHelpRequestCandidate MultiItemCandidate(params (string ItemId, string Label)[] items)
    {
        return new ValleyTalkHelpRequestCandidate
        {
            Type = "item_request",
            Summary = items[0].Label,
            RequestedItemId = items[0].ItemId,
            RequestedItemLabel = items[0].Label,
            Steps = items.Select(item => new ValleyTalkHelpRequestStepCandidate
            {
                Type = "item_request",
                Summary = item.Label,
                RequestedItemId = item.ItemId,
                RequestedItemLabel = item.Label
            }).ToList()
        };
    }

    private static ConversationAnalysis AnalysisFor(params (string ItemId, string Label)[] items)
    {
        return new ConversationAnalysis
        {
            HelpRequests =
            [
                new ConversationHelpRequestCandidate
                {
                    Type = "item_request",
                    Summary = items[0].Label,
                    RequestedItemId = items[0].ItemId,
                    RequestedItemLabel = items[0].Label,
                    Steps = items.Select(item => new ConversationHelpRequestStepCandidate
                    {
                        Type = "item_request",
                        Summary = item.Label,
                        RequestedItemId = item.ItemId,
                        RequestedItemLabel = item.Label
                    }).ToList()
                }
            ]
        };
    }
}
