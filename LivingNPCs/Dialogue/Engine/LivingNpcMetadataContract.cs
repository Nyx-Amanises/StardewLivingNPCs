using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// One field reference for the complete classifier and the locally selected inline contract.
/// Common instructions remain stable; only action details belong in the per-turn prompt tail.
/// </summary>
internal static class LivingNpcMetadataContract
{
    private static readonly JObject FullFieldReference = JObject.Parse("""
        {"complete":true,"rapportDelta":0,"endConversation":false,"ambientFollowUp":{"text":"","delayMinutes":0},"emotionImpact":{"emotion":"happy|calm|jealous|worried|grateful|disappointed|uneasy|upset|angry|sad|none","intensityDelta":0,"apology":false,"repairDelta":0,"reason":""},"behaviorInfluences":[{"type":"visit_location|comforted|offended|give_space|stay_near|pause_to_talk","summary":"","targetLocation":"","targetLocationLabel":"","durationDays":0,"intensity":0,"maxTriggers":0}],"actions":[{"type":"give_small_gift|give_meaningful_gift|give_money|companion_outing|festival_interaction","amount":0,"durationMinutes":0,"delayMinutes":0,"targetLocation":"","travelConsent":"accepted_now|accepted_later|declined|tentative|none","itemId":"","itemLabel":"","reason":""}],"conflicts":[{"causeKind":"dialogue|gift|boundary|promise","summary":"","severity":0}],"memories":[{"kind":"fact|preference|promise|boundary|relationship","summary":"","importance":0,"playerPreference":false,"playerPreferenceKind":"liked_item_category|disliked_item|habit|value|goal|none","subject":"","tags":[]}],"helpRequests":[{"type":"item_request","summary":"","requiresAcceptance":true,"steps":[{"type":"item_request","summary":"","requestedItemId":"","requestedItemLabel":"","questionTopic":""}],"requestedItemId":"","requestedItemLabel":"","questionTopic":"","dueInDays":1,"reason":"","followUpPotential":"none|deeper_relationship"}],"helpRequestUpdates":[{"summary":"","status":"accepted|declined|advanced|fulfilled","resolution":""}],"travelDecision":{"isTravelReply":false,"consent":"accepted_now|accepted_later|declined|tentative|none","targetLocation":"","delayMinutes":0,"durationMinutes":0,"reason":""},"giftDecision":{"isGiftReply":false,"timing":"now|later|mail|promise|none","tier":"small|meaningful","itemId":"","itemLabel":"","reason":""}}
        """);

    private static readonly HashSet<string> SceneFields = new(StringComparer.Ordinal)
    {
        "actions", "helpRequests", "helpRequestUpdates", "travelDecision", "giftDecision"
    };

    private const string HelpRule = "- Match every visibly requested item. A one-step request may name only its single requestedItemId/requestedItemLabel. Multiple items: ordered steps in one helpRequests entry, exact spoken order; no splitting/omitting/reordering. If outside the reasonable-item list, emit no request. Includes optional requests ('if you can also bring', 'while you're at it', 'another would be better', 'that would make it perfect').";
    private const string TravelRule = "- companion_outing: invitation to leave + visible accepted_now consent + supported destination. Short departure waits ('等会/等会儿再去') count as accepted_now; staying here is not travel. delayMinutes=0; leave when dialogue closes.";
    private const string GiftRule = "- giftDecision is immediate only when the NPC visibly offers an item now; mail, later, and promises create no gift action.";

    internal static void AppendFull(StringBuilder prompt)
    {
        prompt.AppendLine("Evaluate every metadata category below; certify with boolean complete:true.");
        AppendSparseRules(prompt);
        prompt.AppendLine("Field reference (optional effects, typed examples/enums; no placeholders):");
        prompt.AppendLine(FullFieldReference.ToString(Formatting.None, Array.Empty<JsonConverter>()));
        AppendCommonRules(prompt);
        prompt.AppendLine(HelpRule);
        prompt.AppendLine(TravelRule);
        prompt.AppendLine(GiftRule);
    }

    internal static string BuildInlineCoreInstructions()
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Villager dialogue: one line beginning with - . Farmer options, if any: each beginning with % .");
        prompt.AppendLine("Then append exactly one final hidden line beginning with !LIVINGNPCS_META and one compact JSON object; nothing after it.");
        prompt.AppendLine("Dialogue/options: requested game language. Metadata: exact schema keys/enums; never expose it in dialogue.");
        prompt.AppendLine(PromptDataBoundary.InstructionReminder);
        prompt.AppendLine("Never invent an item, destination, reward, task, or world action. Field availability grants no permission; supplied current opportunities and restrictions govern actions.");
        prompt.AppendLine("Evaluate every common category below and this turn's Scene action contract.");
        AppendSparseRules(prompt);
        prompt.AppendLine("Common field reference (optional effects, typed examples/enums; no placeholder records):");
        prompt.AppendLine(new JObject(FullFieldReference.Properties()
            .Where(property => !SceneFields.Contains(property.Name))
            .Select(property => new JProperty(property.Name, property.Value.DeepClone())))
            .ToString(Formatting.None, Array.Empty<JsonConverter>()));
        AppendCommonRules(prompt);
        prompt.AppendLine("Action families: NPC item gifts, money, companion outings, festival interactions, new help requests and help updates. If this turn needs a family missing from the Scene action contract, return complete:false for full classification; never silently omit its effect or invent its fields.");
        prompt.AppendLine("No markdown, analysis, or explanation. A metadata line is required even with no effects. Use complete:true only for a complete classification; complete:false requests recovery.");
        return prompt.ToString();
    }

    internal static string BuildSceneInstructions(SceneActionContractPlan plan)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("Scene action contract (extends the common reference; does not authorize actions):");
        JObject reference = BuildSceneFieldReference(plan);
        if (!reference.HasValues)
        {
            prompt.AppendLine("No action details selected. Any actual action effect requires complete:false for full classification.");
            return prompt.ToString();
        }

        prompt.AppendLine(reference.ToString(Formatting.None, Array.Empty<JsonConverter>()));
        if (plan.IncludeNewHelp)
        {
            prompt.AppendLine("- Ask for specific items only when the supplied context allows that opportunity and every item. State all required items clearly and in order; never append an optional or bonus item.");
            prompt.AppendLine(HelpRule);
        }

        if (plan.IncludeHelpUpdates)
        {
            prompt.AppendLine("- Restating an existing help request alone is no new request or status update. When restating its requirements, preserve every required item's identity and step order; never add optional or bonus items.");
        }

        if (plan.IncludeTravel)
        {
            prompt.AppendLine(TravelRule);
        }

        if (plan.IncludeGifts)
        {
            prompt.AppendLine("- Offer an item only when supplied context explicitly allows that gift opportunity and exact item ID.");
            prompt.AppendLine(GiftRule);
        }

        return prompt.ToString();
    }

    private static JObject BuildSceneFieldReference(SceneActionContractPlan plan)
    {
        var fields = new JObject();
        var actionTypes = new List<string>();
        if (plan.IncludeGifts)
        {
            actionTypes.Add("give_small_gift");
            actionTypes.Add("give_meaningful_gift");
        }

        if (plan.IncludeMoney)
        {
            actionTypes.Add("give_money");
        }

        if (plan.IncludeTravel)
        {
            actionTypes.Add("companion_outing");
        }

        if (plan.IncludeFestival)
        {
            actionTypes.Add("festival_interaction");
        }

        if (actionTypes.Count > 0)
        {
            var action = (JObject)FullFieldReference["actions"]![0]!.DeepClone();
            action["type"] = string.Join("|", actionTypes);
            if (!plan.IncludeMoney)
            {
                action.Remove("amount");
            }

            if (!plan.IncludeTravel && !plan.IncludeFestival)
            {
                action.Remove("durationMinutes");
                action.Remove("targetLocation");
            }

            if (!plan.IncludeTravel)
            {
                action.Remove("delayMinutes");
                action.Remove("travelConsent");
            }

            if (!plan.IncludeGifts)
            {
                action.Remove("itemId");
                action.Remove("itemLabel");
            }

            fields["actions"] = new JArray(action);
        }

        if (plan.IncludeNewHelp)
        {
            fields["helpRequests"] = FullFieldReference["helpRequests"]!.DeepClone();
        }

        if (plan.IncludeHelpUpdates)
        {
            fields["helpRequestUpdates"] = FullFieldReference["helpRequestUpdates"]!.DeepClone();
        }

        if (plan.IncludeTravel)
        {
            fields["travelDecision"] = FullFieldReference["travelDecision"]!.DeepClone();
        }

        if (plan.IncludeGifts)
        {
            fields["giftDecision"] = FullFieldReference["giftDecision"]!.DeepClone();
        }

        return fields;
    }

    private static void AppendSparseRules(StringBuilder prompt)
    {
        prompt.AppendLine("Use sparse JSON: include boolean complete and only top-level fields with non-default effects. Omitted fields mean no change, not skipped analysis.");
        prompt.AppendLine("Defaults: 0/false/empty, emotion=none; no travel/gift decision.");
        prompt.AppendLine("No effects: !LIVINGNPCS_META {\"complete\":true}.");
    }

    private static void AppendCommonRules(StringBuilder prompt)
    {
        prompt.AppendLine("Rules:");
        prompt.AppendLine("- Keep documented keys/types and all effects, item IDs, ordered help steps, consent, useful memories. Omit defaults; never duplicate effects across actions/decisions.");
        prompt.AppendLine("- Effects need this turn's input/reply; context constrains/de-duplicates. Options are hypothetical future player choices, never events.");
        prompt.AppendLine("- rapportDelta: routine pleasant small talk 0-2; new understanding 3-7; warmth 8-15; earned major moments 16-24; 25-30 exceptional.");
        prompt.AppendLine("- endConversation=true only for a visibly closing reply; discard its farmer options.");
        prompt.AppendLine("- Memories: importance 0-100 (stored at >=40); durable facts/preferences 60-80, promises/lasting explicit boundaries 70-90; no trivia.");
        prompt.AppendLine(MemoryEvidenceRules.Metadata);
        prompt.AppendLine("- Player preferences: kind=preference, playerPreference=true; specific subject, never 'the farmer'; separate up to two; short useful tags.");
        prompt.AppendLine("- Flustered, embarrassed, shy, playful-defensive or mildly teased = uneasy, not angry. offended/give_space/conflict/boundary memory needs an NPC stop/leave request, visible prior pressure or clear harm.");
        prompt.AppendLine("- One ordinary polite question about family/partner/personal life is not a boundary violation. Preserve explicit refusals/stop requests, insult, threat, humiliation, leaked privacy, malicious provocation, broken promises and repeated pressure.");
        prompt.AppendLine("- A location name does not prove visibility, adjacency, distance, or a route; infer no spatial facts.");
        prompt.AppendLine("- Do not store first meeting/conversation, first-day dates, routine chores or repeated thanks; keep concrete facts, promises, preferences, boundaries and goals disclosed then.");
        prompt.AppendLine("- Caps: actions 1, memories 2, behavior influences 2, conflicts 1, help requests 1, help updates 2.");
    }
}
