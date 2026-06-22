using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal delegate bool TryFindNpcForInteractionHandler(ICursorPosition cursor, out NPC? npc);

internal sealed class ConversationStartRecorder
{
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly ValleyTalkPromptBridge valleyTalkBridge;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;
    private readonly GiftOpportunityService giftOpportunities;
    private readonly HelpRequestRewardService helpRequestRewards;
    private readonly HelpRequestQuestLogService helpRequestQuestLog;
    private readonly TryFindNpcForInteractionHandler tryFindNpcForInteraction;
    private readonly Action<NPC, string, string> pushInteractionContext;
    private readonly Dictionary<string, int> lastConversationMemoryTimeByNpc = new();

    public ConversationStartRecorder(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        ValleyTalkPromptBridge valleyTalkBridge,
        BehaviorFeedbackService feedback,
        CommunityRippleRuntime communityRipples,
        GiftOpportunityService giftOpportunities,
        HelpRequestRewardService helpRequestRewards,
        HelpRequestQuestLogService helpRequestQuestLog,
        TryFindNpcForInteractionHandler tryFindNpcForInteraction,
        Action<NPC, string, string> pushInteractionContext)
    {
        this.helper = helper;
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.valleyTalkBridge = valleyTalkBridge;
        this.feedback = feedback;
        this.communityRipples = communityRipples;
        this.giftOpportunities = giftOpportunities;
        this.helpRequestRewards = helpRequestRewards;
        this.helpRequestQuestLog = helpRequestQuestLog;
        this.tryFindNpcForInteraction = tryFindNpcForInteraction;
        this.pushInteractionContext = pushInteractionContext;
    }

    public void Clear()
    {
        this.lastConversationMemoryTimeByNpc.Clear();
    }

    public void TryRecord(ButtonPressedEventArgs e)
    {
        if (!this.config.EnableConversationMemory || !e.Button.IsActionButton())
        {
            return;
        }

        if (!this.tryFindNpcForInteraction(e.Cursor, out NPC? npc) || npc == null)
        {
            return;
        }

        SObject? heldGift = Game1.player.ActiveObject;
        int timeMarker = (Game1.Date.TotalDays * 10000) + Game1.timeOfDay;
        if (this.lastConversationMemoryTimeByNpc.TryGetValue(npc.Name, out int lastTimeMarker) && lastTimeMarker == timeMarker)
        {
            return;
        }

        this.lastConversationMemoryTimeByNpc[npc.Name] = timeMarker;
        this.TryRecordObservedRomanticInteraction(npc);
        if (heldGift != null)
        {
            var gift = GiftMemoryDetailsFactory.Build(npc, heldGift);
            if (this.config.EnableHelpRequests && this.HasPendingItemHelpRequest(npc, gift))
            {
                this.helper.Input.Suppress(e.Button);
                this.DeliverHelpRequestItem(npc, heldGift, gift);
                return;
            }

            this.memory.RecordGiftOffered(npc, gift, this.config.MaxMemoryEntriesPerNpc);
            if (this.config.EnableNpcState)
            {
                LivingNpcState state = this.memory.UpdateStateForGift(npc, gift);
                this.giftOpportunities.TryScheduleReciprocalGiftOpportunity(npc, state, gift);
            }

            if (this.config.EnableHelpRequests)
            {
                IReadOnlyList<NpcHelpRequestFact> changedHelpRequests = this.memory.TryCompleteItemHelpRequests(npc, gift, this.config.MaxMemoryEntriesPerNpc);
                if (changedHelpRequests.Count > 0)
                {
                    this.helpRequestRewards.RewardFulfilled(npc, changedHelpRequests);
                    this.helpRequestQuestLog.Sync();
                    int fulfilledCount = changedHelpRequests.Count(request => request.Status == "Fulfilled");
                    int advancedCount = changedHelpRequests.Count - fulfilledCount;
                    this.PushInteractionContext(npc, $"Updated {changedHelpRequests.Count} help request(s) for {npc.Name} through a gifted item: {fulfilledCount} fulfilled, {advancedCount} advanced.");
                }
            }

            this.PushInteractionContext(npc, $"Recorded gift interaction for {npc.Name}: {gift.ItemName} ({gift.TastePromptLabel}).");
            return;
        }

        if (Game1.eventUp)
        {
            string eventContext = DescribeCurrentEventContext();
            this.memory.RecordEventInteraction(npc, eventContext, this.config.MaxMemoryEntriesPerNpc);
            if (this.config.EnableNpcState)
            {
                this.memory.UpdateStateForEventInteraction(npc, eventContext);
            }

            this.PushInteractionContext(npc, $"Recorded event interaction for {npc.Name}: {eventContext}.");
            return;
        }

        this.memory.RecordConversationStart(npc, this.config.MaxMemoryEntriesPerNpc);
        if (this.config.EnableNpcState)
        {
            LivingNpcState state = this.memory.UpdateStateForConversationStart(npc);
            this.giftOpportunities.TryPrepareDailyGiftOpportunity(npc, state);
            if (this.config.EnableHelpRequests)
            {
                this.giftOpportunities.TryPrepareDailyHelpRequestOpportunity(npc, state);
            }

            this.communityRipples.TrySpreadConversationSocialRipple(npc, state);
        }

        this.PushInteractionContext(npc, $"Recorded conversation start for {npc.Name}.");
        this.MarkConflictFollowUpsMentionedAfterPrompt(npc);
    }

    private bool HasPendingItemHelpRequest(NPC npc, GiftMemoryDetails gift)
    {
        var state = this.memory.GetState(npc);
        return state?.HelpRequests.Any(request =>
            request.Status == "Pending"
            && request.Type == "item_request"
            && string.Equals(request.RequestedItemId, gift.ItemId, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string DescribeCurrentEventContext()
    {
        string location = Game1.currentLocation?.DisplayName ?? Game1.currentLocation?.Name ?? "当前地点";
        return $"event or festival moment at {location} on {Game1.season} {Game1.dayOfMonth}, {Game1.timeOfDay}";
    }

    private void DeliverHelpRequestItem(NPC npc, SObject heldItem, GiftMemoryDetails gift)
    {
        SObject deliveredItem = ItemRegistry.Create<SObject>(gift.ItemId);
        deliveredItem.Quality = heldItem.Quality;
        Game1.player.reduceActiveItemByOne();

        IReadOnlyList<NpcHelpRequestFact> changedHelpRequests = this.memory.TryCompleteItemHelpRequests(
            npc,
            gift,
            this.config.MaxMemoryEntriesPerNpc
        );
        if (changedHelpRequests.Count == 0)
        {
            if (this.config.Debug)
            {
                this.monitor.Log(
                    I18n.Get("log.help.suppressedGiftNoMatch", new { npc = npc.Name, item = gift.ItemId }),
                    LogLevel.Debug
                );
            }

            return;
        }

        this.helpRequestRewards.RewardFulfilled(npc, changedHelpRequests, queueAmbientThanks: false);
        this.helpRequestQuestLog.Sync();

        int fulfilledCount = changedHelpRequests.Count(request => request.Status == "Fulfilled");
        int advancedCount = changedHelpRequests.Count - fulfilledCount;
        this.PushInteractionContext(
            npc,
            $"Delivered {gift.ItemName} for {changedHelpRequests.Count} help request(s): {fulfilledCount} fulfilled, {advancedCount} advanced.",
            BuildHelpRequestDeliveryPrompt(npc, gift, changedHelpRequests)
        );

        if (!this.valleyTalkBridge.TryRequestGiftDialogue(npc, deliveredItem, gift.TasteScore))
        {
            this.feedback.QueueAmbientRemark(
                npc,
                fulfilledCount > 0 ? I18n.Get("help.thanksFulfilled") : I18n.Get("help.thanksReceived"),
                0
            );
        }

        if (fulfilledCount == 0)
        {
            this.feedback.Show(I18n.Get("hud.itemDelivered", new { item = gift.ItemName, npc = npc.displayName }));
        }
    }

    private static string BuildHelpRequestDeliveryPrompt(
        NPC npc,
        GiftMemoryDetails gift,
        IReadOnlyList<NpcHelpRequestFact> changedHelpRequests
    )
    {
        var lines = new List<string>
        {
            "## LivingNPCs Immediate Help Request Delivery",
            $"- The farmer just handed {npc.displayName} {gift.ItemName} ({gift.ItemId}) for a LivingNPCs help request.",
            "- This is a task hand-in, not an ordinary daily gift. Acknowledge the requested item even if the farmer has already given a normal gift today.",
            "- Do not judge this item by ordinary gift taste; do not say it is unwanted, neutral, poor taste, or not a favorite.",
            "- Respond now with a natural thank-you or reaction to the completed request/step. Do not mention the game's daily gift limit."
        };

        foreach (var request in changedHelpRequests)
        {
            lines.Add($"- Help request status: {request.Status}; summary: {request.Summary}; resolution: {request.Resolution}");
            if (request.Status == "Fulfilled" && request.RewardGranted)
            {
                lines.Add($"- LivingNPCs already granted the configured friendship reward (+{request.RewardFriendship}).");
            }

            if (request.Status == "Fulfilled" && request.RewardMoneyGranted)
            {
                lines.Add(request.RewardMoneyByMail
                    ? $"- LivingNPCs scheduled a system mail reward of {request.RewardMoney}g for tomorrow."
                    : $"- LivingNPCs already granted a system money reward of {request.RewardMoney}g.");
            }

            if (request.RewardGiftGiven)
            {
                lines.Add("- LivingNPCs scheduled a small thank-you item by mail for tomorrow; mention it only if it feels natural, and do not imply the farmer already received it.");
            }

            if (request.SpecialFollowUpPlanned)
            {
                lines.Add("- A later in-person follow-up may happen; do not promise it as guaranteed.");
            }
        }

        return string.Join("\n", lines);
    }

    private void TryRecordObservedRomanticInteraction(NPC targetNpc)
    {
        if (Game1.currentLocation == null
            || Game1.player == null
            || !IsRomanticallyAttachedToFarmer(targetNpc))
        {
            return;
        }

        foreach (var observer in Game1.currentLocation.characters.Where(candidate =>
                     candidate.Name != targetNpc.Name
                     && !string.IsNullOrWhiteSpace(candidate.Name)
                     && Vector2.Distance(candidate.Tile, Game1.player.Tile) <= 6
                     && IsRomanticallyAttachedToFarmer(candidate)))
        {
            var state = this.memory.GetState(observer);
            if (state?.CurrentEmotion == "Jealous"
                && state.LastEmotionUpdatedTotalDays == Game1.Date.TotalDays)
            {
                continue;
            }

            this.memory.UpdateStateForObservedRomanticInteraction(observer, targetNpc);
            this.memory.RecordNpcWorldAction(
                observer,
                "ObservedRomanticInteraction",
                $"they noticed the farmer being close with {targetNpc.displayName}",
                this.config.MaxMemoryEntriesPerNpc
            );
            this.PushInteractionContext(observer, $"Observed romantic interaction involving {targetNpc.Name}.");
        }

        this.communityRipples.Spread(
            targetNpc,
            "romantic_attention",
            $"the farmer has been giving romantic attention to {targetNpc.displayName}",
            importance: 70
        );
    }

    private static bool IsRomanticallyAttachedToFarmer(NPC npc)
    {
        return Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship friendship)
            && (friendship.IsDating() || friendship.IsEngaged() || friendship.IsMarried());
    }

    private void MarkConflictFollowUpsMentionedAfterPrompt(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return;
        }

        foreach (var conflict in state.Conflicts.Where(conflict =>
                     conflict.Status == "Resolved"
                     && conflict.ResolvedTotalDays >= Game1.Date.TotalDays - 3
                     && conflict.RecoveryMentionedTotalDays < 0))
        {
            conflict.RecoveryMentionedTotalDays = Game1.Date.TotalDays;
            conflict.RecoveryMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var request in state.HelpRequests.Where(request =>
                     request.Status == "Expired"
                     && request.LastMentionedTotalDays < Game1.Date.TotalDays))
        {
            request.LastMentionedTotalDays = Game1.Date.TotalDays;
            request.LastMentionedTimeOfDay = Game1.timeOfDay;
        }

        foreach (var request in state.HelpRequests.Where(request =>
                     request.Status == "Fulfilled"
                     && request.FulfilledTotalDays >= Game1.Date.TotalDays - 3
                     && request.LastMentionedTotalDays < 0))
        {
            request.LastMentionedTotalDays = Game1.Date.TotalDays;
            request.LastMentionedTimeOfDay = Game1.timeOfDay;
        }
    }

    private void PushInteractionContext(NPC npc, string debugMessage, string immediatePromptContext = "")
    {
        this.pushInteractionContext(npc, debugMessage, immediatePromptContext);
    }
}
