using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Engine;
using Xunit;

namespace LivingNPCs.Tests;

public sealed class BehaviorPromptCapabilityCompressionTests
{
    [Fact]
    public void EmptyCandidatePoolDisablesNewHelpWithoutFallbackForNeutralConversation()
    {
        string fit = HelpRequestAdvisor.BuildPromptLabel(
            "books and teaching supplies",
            Array.Empty<(string ItemId, string Label)>(),
            TestScenarios.World().Progression,
            friendshipHearts: 6);
        string context = MachineContext(
            helpAllowed: true,
            extra: PromptFragments.Context.HelpRequestFitLine(fit));

        Assert.StartsWith("no currently reasonable item request; do not open a help request now", fit);
        foreach (string view in FullAndBrief(context))
        {
            Assert.Contains("Help-request fit: no currently reasonable item request", view);
            AssertOnlyCapabilities(SelectNeutralConversation(view));
        }
    }

    [Fact]
    public void SelectedCandidatePoolKeepsGroundingAndEnablesNewHelpWithoutTopicKeywords()
    {
        var items = new (string ItemId, string Label)[]
        {
            ("(O)80", "Quartz"),
            ("(O)388", "Wood"),
            ("(O)422", "Purple Mushroom")
        };
        var progression = TestScenarios.World().Progression with
        {
            Year = 2,
            Route = "joja",
            ResidentStage = "second_year_established",
            BusRepaired = true
        };
        const string theme = "books, children, and teaching supplies";
        const string chain = "; ordered classroom supplies: Quartz (O)80, then Wood (O)388";
        string fit = HelpRequestAdvisor.BuildPromptLabel(
            theme, items, progression, friendshipHearts: 5, chainText: chain);
        string context = MachineContext(
            helpAllowed: true,
            extra: PromptFragments.Context.HelpRequestFitLine(fit));

        foreach (string view in FullAndBrief(context))
        {
            foreach (var item in items)
            {
                Assert.Contains($"{item.Label} {item.ItemId}", view);
            }

            Assert.Contains($"theme {theme}", view);
            Assert.Contains("request relationship tier: friendly; modest comfort items can fit", view);
            Assert.Contains("request depth: modest personal item requests and occasional two-step item favors", view);
            Assert.Contains("when trust and conversation support them", view);
            Assert.Contains("world-stage constraint: the town followed the Joja route", view);
            Assert.Contains("already-unlocked public facilities may be treated as ordinary parts of life", view);
            Assert.Contains(chain, view);
            Assert.Contains("item_request only; never create question_request", view);
            Assert.Contains("every required item in order, no optional/bonus items", view);
            Assert.Contains("Await farmer acceptance; acceptance is not delivery", view);
            AssertOnlyCapabilities(SelectNeutralConversation(view), newHelp: true);
        }
    }

    [Fact]
    public void CompressedGiftOpportunityKeepsItsNarrowPermissionAndItemPairs()
    {
        const string shared = "(O)20 Leek, (O)18 Daffodil";
        const string personalized = "(O)216 Bread";
        string opportunity = PromptFragments.GiftOpportunity.Section(
            "Penny", "one small everyday gift today", shared, personalized);
        string context = MachineContext(helpAllowed: false, giftSection: opportunity);

        foreach (string view in FullAndBrief(context))
        {
            Assert.Contains("This authorization applies to this one reply only.", view);
            Assert.Contains("at most one small in-game gift", view);
            Assert.Contains("Do not upgrade it to give_meaningful_gift without separate explicit authorization", view);
            Assert.Contains("A visible immediate offer permits exactly one give_small_gift action", view);
            Assert.Contains("no visible offer means no gift action", view);
            Assert.Contains($"Shared small gift IDs: {shared}", view);
            Assert.Contains($"Penny's personalized small gift IDs: {personalized}", view);
            Assert.Contains("copy both itemId and its matching itemLabel from these lists", view);
            Assert.Contains("leave both fields empty", view);
            Assert.Contains("other items outside these lists", view);
            AssertOnlyCapabilities(SelectNeutralConversation(view), gifts: true);
        }

        // The next reply has no opportunity, even though its memory still mentions a past gift.
        string restricted = MachineContext(
            helpAllowed: false,
            extra: "- Relevant long-term memories for this reply: Penny once gave the farmer Bread.");
        foreach (string view in FullAndBrief(restricted))
        {
            Assert.Contains("no NPC gift is authorized for this reply", view);
            Assert.Contains("include no give_small_gift or give_meaningful_gift action", view);
            AssertOnlyCapabilities(SelectNeutralConversation(view));
        }
    }

    [Theory]
    [InlineData("Intimate", "shared outings and private visits may be accepted when the scene and schedule allow")]
    [InlineData("Trusted", "shared outings and private visits may be accepted when the scene and schedule allow")]
    [InlineData("Friendly", "public outings are natural; private invitations such as visiting the farmer's farm should still need a good in-character reason")]
    [InlineData("Familiar", "brief public company may be acceptable, but private or extended outings should usually be declined or deferred")]
    [InlineData("Distant", "the relationship is still distant, so private invitations such as visiting the farmer's farm or home should usually be declined politely; at most, brief public company may fit")]
    public void EveryRelationshipKeepsInvitationBoundariesInFullAndBriefContext(
        string relationshipTier, string expectedBoundary)
    {
        var state = TestScenarios.TrustedState("Penny");
        state.InteractionComfortTier = relationshipTier;
        string invitation = "- " + PromptFragments.Context.GuidanceInvitationPolicy(state);
        const string irrelevantLine = "- Decorative prose with no useful detail.";
        string context = MachineContext(
            helpAllowed: false,
            extra: invitation + "\n" + irrelevantLine);
        string brief = LivingNpcContextCompressor.BuildBriefContext(context);

        Assert.DoesNotContain(irrelevantLine, brief);
        foreach (string view in new[] { context, brief })
        {
            Assert.Contains(expectedBoundary, view);
            Assert.Contains("ordinary daily schedule stops are soft constraints, not sole reasons to decline", view);
            Assert.Contains("a matching current/upcoming destination favors going together or showing the way", view);
            Assert.Contains("still refuse during events, sleep, severe conflict, unsafe scenes, or truly story-critical obligations", view);
            AssertOnlyCapabilities(SelectNeutralConversation(view));
        }
    }

    private static string MachineContext(bool helpAllowed, string extra = "", string? giftSection = null)
        => string.Join("\n",
            PromptFragments.Context.Header("Penny"),
            PromptFragments.Context.CurrentStateHeading,
            "- Scene: a quiet morning; location: Town.",
            PromptFragments.Context.HelpRequestReadinessLine(helpAllowed
                ? PromptFragments.Context.HelpRequestReadinessAllowed("the relationship is ready")
                : PromptFragments.Context.HelpRequestReadinessBlocked("no new favor is appropriate")),
            // Keep memory lines before the gift heading, so brief-view checks do not accidentally
            // protect them as part of the whole gift-opportunity/restriction section.
            extra,
            giftSection ?? PromptFragments.GiftOpportunity.NoOpportunitySection());

    private static string[] FullAndBrief(string context)
        => new[] { context, LivingNpcContextCompressor.BuildBriefContext(context) };

    private static SceneActionContractPlan SelectNeutralConversation(string context)
        => SceneActionContractSelector.Select(
            "今天天气真好。", context, null, GenerationTrigger.Conversation, locale: "zh");

    private static void AssertOnlyCapabilities(
        SceneActionContractPlan plan, bool newHelp = false, bool gifts = false)
    {
        Assert.False(plan.IsFallback, plan.Reason);
        Assert.Equal(newHelp, plan.IncludeNewHelp);
        Assert.Equal(gifts, plan.IncludeGifts);
        Assert.False(plan.IncludeHelpUpdates);
        Assert.False(plan.IncludeTravel);
        Assert.False(plan.IncludeMoney);
        Assert.False(plan.IncludeFestival);
    }
}
