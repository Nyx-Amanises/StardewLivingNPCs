using LivingNPCs.Behavior;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Engine;

public sealed class SceneMetadataContractTests
{
    [Fact]
    public void CommonAndFullSceneReferencesPreserveTheCompleteRuntimeContract()
    {
        JObject full = Reference(LivingNpcMetadataExtractionPass.BuildInlineInstructions());
        JObject combined = Reference(LivingNpcMetadataContract.BuildInlineCoreInstructions());
        foreach (JProperty field in Reference(LivingNpcMetadataContract.BuildSceneInstructions(SceneActionContractPlan.Full())).Properties())
        {
            Assert.Null(combined[field.Name]);
            combined[field.Name] = field.Value.DeepClone();
        }

        // The existing reflection test ties the full schema to every runtime DTO property.
        // This equality makes the modular prompt inherit that same coverage, including steps.
        Assert.True(JToken.DeepEquals(full, combined));
    }

    [Fact]
    public void OrdinaryConversationKeepsCommonEffectsAndExplicitRecoveryWithoutActionDetails()
    {
        string common = LivingNpcMetadataContract.BuildInlineCoreInstructions();
        string scene = LivingNpcMetadataContract.BuildSceneInstructions(new SceneActionContractPlan());
        JObject fields = Reference(common);

        Assert.Equal(new[] { "ambientFollowUp", "behaviorInfluences", "complete", "conflicts", "emotionImpact", "endConversation", "memories", "rapportDelta" },
            fields.Properties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Contains("importance 0-100 (stored at >=40)", common);
        Assert.Contains("Options are hypothetical future player choices", common);
        Assert.Contains("not a boundary violation", common);
        Assert.Contains("return complete:false for full classification", common);
        Assert.Contains("does not authorize actions", scene);
        Assert.Contains("Any actual action effect requires complete:false", scene);
        Assert.DoesNotContain("{", scene);
        Assert.True(common.Length + scene.Length < LivingNpcMetadataExtractionPass.BuildInlineInstructions().Length - 1000);
    }

    [Fact]
    public void GiftReferencePreservesTimingAndIdentityWithoutUnrelatedActionFields()
    {
        string scene = LivingNpcMetadataContract.BuildSceneInstructions(new SceneActionContractPlan { IncludeGifts = true });
        JObject fields = Reference(scene);
        JObject action = (JObject)Assert.Single((JArray)fields["actions"]!);

        Assert.Equal("give_small_gift|give_meaningful_gift", action.Value<string>("type"));
        Assert.NotNull(action["itemId"]);
        Assert.NotNull(action["itemLabel"]);
        Assert.Null(action["amount"]);
        Assert.Null(action["travelConsent"]);
        Assert.Equal("now|later|mail|promise|none", fields["giftDecision"]!.Value<string>("timing"));
        Assert.Null(fields["travelDecision"]);
        Assert.Null(fields["helpRequests"]);
        Assert.Contains("exact item ID", scene);
        Assert.Contains("mail, later, and promises create no gift action", scene);
    }

    [Fact]
    public void TravelReferenceKeepsConsentAndImmediateDepartureSemantics()
    {
        string scene = LivingNpcMetadataContract.BuildSceneInstructions(new SceneActionContractPlan { IncludeTravel = true });
        JObject fields = Reference(scene);
        JObject action = (JObject)Assert.Single((JArray)fields["actions"]!);

        Assert.Equal("companion_outing", action.Value<string>("type"));
        Assert.NotNull(action["targetLocation"]);
        Assert.NotNull(action["travelConsent"]);
        Assert.NotNull(action["delayMinutes"]);
        Assert.NotNull(action["durationMinutes"]);
        Assert.NotNull(fields["travelDecision"]);
        Assert.Null(fields["giftDecision"]);
        Assert.Contains("invitation to leave + visible accepted_now consent + supported destination", scene);
        Assert.Contains("delayMinutes=0; leave when dialogue closes", scene);
        Assert.Contains("staying here is not travel", scene);
    }

    [Fact]
    public void NewHelpKeepsAllOrderedStepsAndDisallowsOptionalItems()
    {
        string scene = LivingNpcMetadataContract.BuildSceneInstructions(new SceneActionContractPlan { IncludeNewHelp = true });
        JObject fields = Reference(scene);
        JObject request = (JObject)Assert.Single((JArray)fields["helpRequests"]!);
        JObject step = (JObject)Assert.Single((JArray)request["steps"]!);

        Assert.Equal("item_request", step.Value<string>("type"));
        Assert.NotNull(step["requestedItemId"]);
        Assert.NotNull(step["requestedItemLabel"]);
        Assert.True(request.Value<bool>("requiresAcceptance"));
        Assert.Contains("exact spoken order; no splitting/omitting/reordering", scene);
        Assert.Contains("outside the reasonable-item list, emit no request", scene);
        Assert.Contains("never append an optional or bonus item", scene);
        Assert.Null(fields["helpRequestUpdates"]);
        Assert.Null(fields["actions"]);
    }

    [Fact]
    public void ExistingHelpCanBeUpdatedWithoutAdvertisingANewRequest()
    {
        string scene = LivingNpcMetadataContract.BuildSceneInstructions(new SceneActionContractPlan { IncludeHelpUpdates = true });
        JObject fields = Reference(scene);

        Assert.Equal("helpRequestUpdates", Assert.Single(fields.Properties()).Name);
        Assert.Equal("accepted|declined|advanced|fulfilled", fields["helpRequestUpdates"]![0]!.Value<string>("status"));
        Assert.DoesNotContain("Ask for specific items", scene);
        Assert.Contains("preserve every required item's identity and step order", scene);
        Assert.Contains("never add optional or bonus items", scene);
        Assert.Contains("Restating an existing help request alone is no new request or status update", scene);
    }

    [Fact]
    public void MixedActionTypesKeepTheirDistinctPayloads()
    {
        JObject fields = Reference(LivingNpcMetadataContract.BuildSceneInstructions(new SceneActionContractPlan
        {
            IncludeGifts = true,
            IncludeTravel = true,
            IncludeMoney = true,
            IncludeFestival = true
        }));
        JObject action = (JObject)Assert.Single((JArray)fields["actions"]!);

        Assert.Equal("give_small_gift|give_meaningful_gift|give_money|companion_outing|festival_interaction", action.Value<string>("type"));
        Assert.NotNull(action["amount"]);
        Assert.NotNull(action["itemId"]);
        Assert.NotNull(action["targetLocation"]);
        Assert.NotNull(action["travelConsent"]);
    }

    [Fact]
    public void ChangedActionSceneKeepsTheCommonInstructionPrefix()
    {
        AssembledPrompt greeting = Assemble("Good morning!");
        AssembledPrompt outing = Assemble("Would you like to go to the beach with me now?");

        Assert.False(greeting.ActionContract.IsFallback, greeting.ActionContract.Reason);
        Assert.False(greeting.ActionContract.IncludeTravel);
        Assert.True(outing.ActionContract.IncludeTravel, outing.ActionContract.Reason);
        Assert.Equal(greeting.CacheableNpcContext, outing.CacheableNpcContext);
        Assert.Contains("companion_outing", outing.Tail);
        Assert.DoesNotContain("companion_outing", greeting.Tail);
        Assert.DoesNotContain("travelDecision", outing.CacheableNpcContext);
        Assert.True(outing.SectionLengths["SceneActionContract"] > greeting.SectionLengths["SceneActionContract"]);
        Assert.Equal(outing.TotalCharacters, outing.CreateLlmRequest().SystemPrompt.Length
            + outing.CreateLlmRequest().ConcatenatedUserContent().Length);
    }

    [Fact]
    public void SceneContractCannotBeOmittedByContextRoutingOrOverwrittenAsGameData()
    {
        var input = Input("Would you like to go to the beach with me now?", new ContextRoutingPlan());
        AssembledPrompt prompt = new PromptAssembler(input).Assemble();

        Assert.Contains("travelDecision", prompt.Tail);
        Assert.DoesNotContain("UNTRUSTED REPLACEMENT", prompt.Tail);
        Assert.True(prompt.SectionLengths["SceneActionContract"] > 0);
    }

    [Fact]
    public void ActiveNonDesertEventIncludesFestivalFieldsForAShortReply()
    {
        var prompt = new PromptAssembler(Input("好呀。", ContextRoutingPlan.Full(), new GameStateSnapshot
        {
            FarmerName = "Farmer",
            LocationName = "Forest",
            IsEventActive = true
        })).Assemble();

        Assert.False(prompt.ActionContract.IsFallback, prompt.ActionContract.Reason);
        Assert.True(prompt.ActionContract.IncludeFestival);
        Assert.Contains("festival_interaction", prompt.Tail);
        Assert.DoesNotContain("festival_interaction", prompt.CacheableNpcContext);
    }

    internal static string RestrictedContext => string.Join("\n",
        PromptFragments.Context.Header("Penny"),
        PromptFragments.Context.CurrentStateHeading,
        PromptFragments.Context.HelpRequestReadinessLine(PromptFragments.Context.HelpRequestReadinessBlocked("not ready")),
        PromptFragments.Context.EmptyStoresLine(new[] { "help requests" }),
        PromptFragments.GiftOpportunity.NoOpportunitySection());

    private static AssembledPrompt Assemble(string playerText) => new PromptAssembler(Input(playerText, ContextRoutingPlan.Full())).Assemble();

    private static PromptAssemblyInput Input(string playerText, ContextRoutingPlan routing, GameStateSnapshot? snapshot = null) => new()
    {
        Request = new GenerationRequest
        {
            NpcName = "Penny",
            Trigger = GenerationTrigger.Conversation,
            CurrentPlayerText = playerText,
            BehaviorContext = RestrictedContext,
            Snapshot = snapshot ?? new GameStateSnapshot { FarmerName = "Farmer", LocationName = "Town" }
        },
        Conversation = new[] { new ConversationTurn(playerText, true, "current-turn") },
        NpcName = "Penny",
        NpcDisplayName = "Penny",
        Bio = new NpcBio { Biography = "Penny is a kind villager who likes books." },
        Plan = routing,
        Locale = "en",
        SectionOverrides = new Dictionary<string, string> { ["SceneActionContract"] = "UNTRUSTED REPLACEMENT" },
        Lookup = (key, _, _) => "[" + key + "]"
    };

    private static JObject Reference(string prompt) => JObject.Parse(prompt.Split('\n').Single(line => line.StartsWith('{')));
}
