using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Assembles the hidden LivingNPCs context that gets pushed to ValleyTalk before a conversation:
/// the base continuity summary from memory, plus the situational sections (gift opportunity,
/// help-request opportunity, companion outing, immediate cues) and the gift-response prompts.
/// Everything that turns LivingNPCs state into prompt text for the bridge lives here.
/// </summary>
internal sealed class ValleyTalkContextService
{
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly GiftSelector giftSelector;
    private readonly BehaviorMailService mailService;
    private readonly ValleyTalkPromptBridge bridge;
    private readonly Func<NPC, string> buildCompanionOutingContext;

    public ValleyTalkContextService(
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        GiftSelector giftSelector,
        BehaviorMailService mailService,
        ValleyTalkPromptBridge bridge,
        Func<NPC, string> buildCompanionOutingContext)
    {
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.giftSelector = giftSelector;
        this.mailService = mailService;
        this.bridge = bridge;
        this.buildCompanionOutingContext = buildCompanionOutingContext;
    }

    public void PushInteractionContext(NPC npc, string debugMessage, string immediatePromptContext = "")
    {
        if (RsvAiPolicy.IsBlockedNpc(npc))
        {
            return;
        }

        string promptContext = this.BuildPromptContext(npc, immediatePromptContext);
        bool pushedToValleyTalk = this.bridge.PushBehaviorContext(npc, promptContext);

        if (this.config.Debug)
        {
            this.monitor.Log(
                pushedToValleyTalk
                    ? debugMessage
                    : I18n.Get("log.context.debugNotPushed", new { message = debugMessage }),
                LogLevel.Debug
            );
            if (pushedToValleyTalk)
            {
                this.monitor.Log(I18n.Get("log.context.primed", new { npc = npc.Name, context = promptContext }), LogLevel.Trace);
            }
        }
    }

    public string BuildPromptContext(NPC npc, string immediatePromptContext = "")
    {
        string promptContext = this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays
        );
        string giftOpportunityContext = this.BuildGiftOpportunityPromptContext(npc);
        if (!string.IsNullOrWhiteSpace(giftOpportunityContext))
        {
            promptContext = $"{promptContext}\n{giftOpportunityContext}";
        }

        string helpRequestOpportunityContext = this.BuildHelpRequestOpportunityPromptContext(npc);
        if (!string.IsNullOrWhiteSpace(helpRequestOpportunityContext))
        {
            promptContext = $"{promptContext}\n{helpRequestOpportunityContext}";
        }

        string companionOutingContext = this.buildCompanionOutingContext(npc);
        if (!string.IsNullOrWhiteSpace(companionOutingContext))
        {
            promptContext = $"{promptContext}\n{companionOutingContext}";
        }

        if (!string.IsNullOrWhiteSpace(immediatePromptContext))
        {
            promptContext = $"{promptContext}\n{immediatePromptContext}";
        }

        return promptContext;
    }

    private string BuildGiftOpportunityPromptContext(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null
            || !this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || state.HighestUnresolvedConflictSeverity >= 30
            || GiftActionRules.HasAiGiftToday(state))
        {
            return string.Empty;
        }

        string cue = string.Empty;
        if (state.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays)
        {
            cue = string.IsNullOrWhiteSpace(state.DailyGiftOpportunityReason)
                ? "The relationship is warm enough that the NPC may offer a small everyday gift today."
                : state.DailyGiftOpportunityReason;
        }

        if (string.IsNullOrWhiteSpace(cue))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            "## LivingNPCs Gift Opportunity",
            $"- Gift cue: {cue}.",
            "- This is an opportunity, not an obligation. If it fits the visible reply, have the NPC naturally offer a small in-game gift now and include exactly one hidden action with type give_small_gift.",
            "- If you include give_small_gift, the visible dialogue must explicitly offer the gift before the hidden metadata, using natural wording such as 'I brought you a small thing' or 'this is for you'. If the visible reply does not offer a gift, do not include the hidden action.",
            $"- Shared small gift IDs: {this.giftSelector.BuildCommonPromptList(GiftTier.Small)}.",
            $"- {npc.displayName}'s personalized small gift IDs: {this.giftSelector.BuildPersonalizedPromptList(npc, GiftTier.Small)}.",
            "- If naming a specific gift, use an itemId from the two lists above and the matching itemLabel. Generic wording such as 'a small thing' is fine when no specific item is named.",
            "- If the moment feels emotionally wrong, crowded, or abrupt, skip the gift rather than forcing it."
        );
    }

    private string BuildHelpRequestOpportunityPromptContext(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null
            || !this.config.EnableHelpRequests
            || state.DailyHelpRequestOpportunityTotalDays != Game1.Date.TotalDays
            || state.HighestUnresolvedConflictSeverity >= 30
            || state.HelpRequests.Any(request => request.Status is "Offered" or "Pending"))
        {
            return string.Empty;
        }

        return string.Join(
            "\n",
            "## LivingNPCs Help Request Opportunity",
            $"- Today {npc.displayName} is inclined to ask the farmer for one small favor during this conversation.",
            "- If the visible reply allows, naturally bring up needing one concrete item from the help-request fit list, and include exactly one hidden helpRequests entry (item_request) with that itemId — do not leave the favor only in the spoken text.",
            "- Keep it brief and in character; if the moment genuinely does not fit, it is fine to wait for another day rather than forcing it."
        );
    }

    /// <summary>
    /// The hidden context for an immediate gift reaction: hand-ins for pending help requests
    /// override normal taste guidance, and queued reciprocal/birthday thank-you mail lets the NPC
    /// hint at a later return gift.
    /// </summary>
    public string BuildGiftResponseContext(NPC npc, string giftItemId, string giftName, int taste)
    {
        var labels = GiftMemoryDetailsFactory.DescribeTaste(taste);
        var gift = new GiftMemoryDetails(
            giftItemId ?? string.Empty,
            string.IsNullOrWhiteSpace(giftName) ? "a gift" : giftName,
            labels.DebugLabel,
            labels.PromptLabel,
            taste,
            GiftMemoryDetailsFactory.IsBirthdayGift(npc)
        );
        LivingNpcState state = this.memory.GetOrCreateState(npc);
        var matchingHelpRequests = FindMatchingItemHelpRequestGiftContexts(state, gift).ToList();
        if (matchingHelpRequests.Count > 0)
        {
            return BuildHelpRequestGiftResponsePrompt(npc, gift, matchingHelpRequests);
        }

        if (taste is 4 or 6)
        {
            return string.Empty;
        }

        // Read-only: the reciprocal-gift roll happens once, in ConversationStartRecorder, after the
        // gift is confirmed accepted. Rolling here too would stack a second chance per gift.
        bool hasResponseMail = this.mailService.HasPendingGiftMail(state, "reciprocal")
            || this.mailService.HasPendingGiftMail(state, "birthday");
        return hasResponseMail
            ? BuildGiftResponseMailPrompt(npc, gift)
            : string.Empty;
    }

    private static IEnumerable<NpcHelpRequestFact> FindMatchingItemHelpRequestGiftContexts(
        LivingNpcState state,
        GiftMemoryDetails gift)
    {
        return state.HelpRequests.Where(request =>
            request.Type == "item_request"
            && (HelpRequestCurrentlyAsksForItem(request, gift.ItemId)
                || HelpRequestJustReceivedItem(request, gift.ItemId)));
    }

    private static bool HelpRequestCurrentlyAsksForItem(NpcHelpRequestFact request, string giftItemId)
    {
        return request.Status == "Pending"
            && string.Equals(request.RequestedItemId, giftItemId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HelpRequestJustReceivedItem(NpcHelpRequestFact request, string giftItemId)
    {
        if (request.LastUpdatedTotalDays != Game1.Date.TotalDays
            || request.LastUpdatedTimeOfDay != Game1.timeOfDay)
        {
            return false;
        }

        if (request.Status == "Fulfilled"
            && string.Equals(request.RequestedItemId, giftItemId, StringComparison.OrdinalIgnoreCase)
            && request.FulfilledTotalDays == Game1.Date.TotalDays
            && request.FulfilledTimeOfDay == Game1.timeOfDay)
        {
            return true;
        }

        return request.Steps.Any(step =>
            step.Type == "item_request"
            && step.Status == "Fulfilled"
            && step.CompletedTotalDays == Game1.Date.TotalDays
            && step.CompletedTimeOfDay == Game1.timeOfDay
            && string.Equals(step.RequestedItemId, giftItemId, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildHelpRequestGiftResponsePrompt(
        NPC npc,
        GiftMemoryDetails gift,
        IReadOnlyList<NpcHelpRequestFact> deliveredHelpRequests)
    {
        var lines = new List<string>
        {
            "## LivingNPCs Help Request Gift Response",
            $"- The farmer just handed {npc.displayName} {gift.ItemName} ({gift.ItemId}) for a LivingNPCs help request.",
            "- This is a requested task hand-in, not an unexpected daily gift.",
            "- Override ordinary gift taste guidance: do not say the item is unwanted, neutral, poor taste, or not a favorite.",
            "- Thank the farmer for following through and connect the item to the request in a natural, in-character way."
        };

        foreach (var request in deliveredHelpRequests)
        {
            lines.Add($"- Help request status after hand-in: {request.Status}; summary: {request.Summary}; resolution: {request.Resolution}");
            if (request.Status == "Pending")
            {
                lines.Add($"- The request advanced to another step. Current next step: {request.CurrentStepPromptLabel}");
            }
            else if (request.Status == "Fulfilled")
            {
                lines.Add("- The request is now complete; the immediate reply should sound grateful and complete, not like a normal gift reaction.");
            }

            if (request.RewardGranted)
            {
                lines.Add($"- LivingNPCs already granted the configured friendship reward (+{request.RewardFriendship}); mention rewards only if it sounds natural.");
            }

            if (request.RewardMoneyGranted)
            {
                lines.Add($"- LivingNPCs already granted the configured money reward ({request.RewardMoney}g); do not promise extra payment beyond the system reward.");
            }
            else if (request.RewardMoneyClaimQueued)
            {
                lines.Add($"- LivingNPCs added the configured money reward ({request.RewardMoney}g) to the quest journal for the farmer to claim; do not promise extra payment beyond the system reward.");
            }

            if (request.RewardGiftGiven)
            {
                lines.Add("- LivingNPCs scheduled a small thank-you item by mail for tomorrow; mention it only if it feels natural, and do not imply the farmer already received it.");
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildGiftResponseMailPrompt(NPC npc, GiftMemoryDetails gift)
    {
        string itemLabel = string.IsNullOrWhiteSpace(gift.ItemName)
            ? "the farmer's gift"
            : gift.ItemName.Trim();
        string header = gift.IsBirthdayGift
            ? "## LivingNPCs Birthday Gift Mail"
            : "## LivingNPCs Reciprocal Gift Mail";
        string reason = gift.IsBirthdayGift
            ? $"LivingNPCs has scheduled a later birthday thank-you mail from {npc.displayName} because the farmer remembered their birthday with {itemLabel}."
            : $"LivingNPCs has scheduled a later mailbox return gift from {npc.displayName} because the farmer gave them {itemLabel}.";
        return string.Join(
            "\n",
            header,
            $"- {reason}",
            "- In this immediate gift reaction, the NPC may briefly imply they will send something later if it sounds natural.",
            "- Do not give the farmer an item now, do not include a hidden give_small_gift or give_meaningful_gift action, and do not promise a specific item."
        );
    }
}
