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
    /// <summary>
    /// A gift interaction observed on button-press, waiting to be confirmed against vanilla.
    /// Vanilla may refuse the offered item (weekly/daily gift limit reached, festival dialogue,
    /// non-giftable item, quest hand-in), in which case nothing should be recorded.
    /// </summary>
    private sealed class PendingGiftVerification
    {
        public NPC Npc = null!;
        public GiftMemoryDetails Gift = null!;
        public int TotalDays;
        public int TimeMarker;
        public int SnapshotGiftsToday;
        public int SnapshotLastGiftDateTotalDays;
    }

    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly DialogueEngineLink dialogueLink;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;
    private readonly GiftOpportunityService giftOpportunities;
    private readonly HelpRequestRewardService helpRequestRewards;
    private readonly HelpRequestQuestLogService helpRequestQuestLog;
    private readonly TryFindNpcForInteractionHandler tryFindNpcForInteraction;
    private readonly Action<NPC, string, string> pushInteractionContext;
    private readonly Dictionary<string, int> lastConversationMemoryTimeByNpc = new();
    private readonly List<PendingGiftVerification> pendingGiftVerifications = new();

    public ConversationStartRecorder(
        IModHelper helper,
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        DialogueEngineLink dialogueLink,
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
        this.dialogueLink = dialogueLink;
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
        this.pendingGiftVerifications.Clear();
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
        GiftMemoryDetails? heldGiftDetails = heldGift != null ? GiftMemoryDetailsFactory.Build(npc, heldGift) : null;
        if (heldGift != null
            && heldGiftDetails != null
            && this.config.EnableHelpRequests
            && this.HasPendingItemHelpRequest(npc, heldGiftDetails))
        {
            // Deliveries run BEFORE the per-10-minute interaction marker: they are idempotent
            // (the pending predicate stops matching once the step completes), and a second click
            // inside the same window must still be suppressed and delivered — otherwise vanilla
            // gifting consumes the quest item as an ordinary daily gift.
            this.helper.Input.Suppress(e.Button);
            this.DeliverHelpRequestItem(npc, heldGift, heldGiftDetails);
            return;
        }

        int timeMarker = (Game1.Date.TotalDays * 10000) + Game1.timeOfDay;
        if (this.lastConversationMemoryTimeByNpc.TryGetValue(npc.Name, out int lastTimeMarker) && lastTimeMarker == timeMarker)
        {
            return;
        }

        this.lastConversationMemoryTimeByNpc[npc.Name] = timeMarker;
        this.TryRecordObservedRomanticInteraction(npc);
        if (heldGift != null && heldGiftDetails != null)
        {
            GiftMemoryDetails gift = heldGiftDetails;

            // Defer the gift record until vanilla has processed this click: vanilla refuses gifts
            // past the weekly/daily limit, during festivals, or for quest hand-ins, and none of
            // those should update memory, mood, or roll a reciprocal gift. The verification runs
            // in the same tick's UpdateTicked, after vanilla's input handling.
            Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship);
            this.pendingGiftVerifications.RemoveAll(pending => pending.Npc.Name == npc.Name);
            this.pendingGiftVerifications.Add(new PendingGiftVerification
            {
                Npc = npc,
                Gift = gift,
                TotalDays = Game1.Date.TotalDays,
                TimeMarker = timeMarker,
                SnapshotGiftsToday = friendship?.GiftsToday ?? 0,
                SnapshotLastGiftDateTotalDays = friendship?.LastGiftDate?.TotalDays ?? -1
            });
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

            this.PushInteractionContext(
                npc,
                I18n.Get("log.interaction.eventRecorded", new { npc = npc.Name, context = eventContext }));
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

        this.PushInteractionContext(
            npc,
            I18n.Get("log.interaction.conversationStarted", new { npc = npc.Name }));
        this.MarkConflictFollowUpsMentionedAfterPrompt(npc);
    }

    /// <summary>
    /// Confirms pending gift interactions against vanilla and records the accepted ones. Runs from
    /// UpdateTicked, which fires after vanilla has handled the click that offered the gift, so the
    /// friendship gift counters already reflect whether the NPC actually took the item.
    /// </summary>
    public void ProcessPendingGiftVerifications()
    {
        if (this.pendingGiftVerifications.Count == 0)
        {
            return;
        }

        var pending = new List<PendingGiftVerification>(this.pendingGiftVerifications);
        this.pendingGiftVerifications.Clear();
        foreach (var candidate in pending)
        {
            if (candidate.TotalDays != Game1.Date.TotalDays)
            {
                continue;
            }

            if (!WasGiftAcceptedByVanilla(candidate))
            {
                // Vanilla refused the item, so this click was ordinary dialogue, not a gift. Drop
                // the interaction marker so the conversation can still be recorded in this window.
                if (this.lastConversationMemoryTimeByNpc.TryGetValue(candidate.Npc.Name, out int marker)
                    && marker == candidate.TimeMarker)
                {
                    this.lastConversationMemoryTimeByNpc.Remove(candidate.Npc.Name);
                }

                if (this.config.Debug)
                {
                    this.monitor.Log(
                        I18n.Get(
                            "log.interaction.giftRejectedByGame",
                            new { npc = candidate.Npc.Name, item = candidate.Gift.ItemName }),
                        LogLevel.Debug
                    );
                }

                continue;
            }

            this.RecordAcceptedGift(candidate.Npc, candidate.Gift);
        }
    }

    private static bool WasGiftAcceptedByVanilla(PendingGiftVerification candidate)
    {
        if (!Game1.player.friendshipData.TryGetValue(candidate.Npc.Name, out Friendship? friendship)
            || friendship == null)
        {
            return false;
        }

        int today = Game1.Date.TotalDays;
        if ((friendship.LastGiftDate?.TotalDays ?? -1) != today)
        {
            return false;
        }

        return candidate.SnapshotLastGiftDateTotalDays != today
            || friendship.GiftsToday != candidate.SnapshotGiftsToday;
    }

    private void RecordAcceptedGift(NPC npc, GiftMemoryDetails gift)
    {
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
                this.PushInteractionContext(
                    npc,
                    I18n.Get(
                        "log.interaction.helpGiftUpdated",
                        new
                        {
                            npc = npc.Name,
                            count = changedHelpRequests.Count,
                            fulfilled = fulfilledCount,
                            advanced = advancedCount
                        }));
            }
        }

        this.PushInteractionContext(
            npc,
            I18n.Get(
                "log.interaction.giftRecorded",
                new { npc = npc.Name, item = gift.ItemName, taste = gift.TastePromptLabel }));
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
            I18n.Get(
                "log.interaction.helpGiftDelivered",
                new
                {
                    item = gift.ItemName,
                    count = changedHelpRequests.Count,
                    fulfilled = fulfilledCount,
                    advanced = advancedCount
                }),
            BuildHelpRequestDeliveryPrompt(npc, gift, changedHelpRequests)
        );

        if (!this.dialogueLink.TryRequestGiftDialogue(npc, deliveredItem, gift.TasteScore))
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
            PromptFragments.HelpRequestDelivery.Header,
            PromptFragments.HelpRequestDelivery.HandInLine(npc.displayName, gift.ItemName, gift.ItemId),
            PromptFragments.HelpRequestDelivery.NotDailyGiftLine,
            PromptFragments.HelpRequestDelivery.OverrideTasteLine,
            PromptFragments.HelpRequestDelivery.RespondNowLine
        };

        foreach (var request in changedHelpRequests)
        {
            lines.Add(PromptFragments.HelpRequestDelivery.StatusLine(request));
            if (request.Status == "Fulfilled" && request.RewardGranted)
            {
                lines.Add(PromptFragments.HelpRequestDelivery.FriendshipRewardLine(request.RewardFriendship));
            }

            if (request.Status == "Fulfilled" && request.RewardMoneyGranted)
            {
                lines.Add(PromptFragments.HelpRequestDelivery.MoneyRewardGrantedLine(request.RewardMoney));
            }
            else if (request.Status == "Fulfilled" && request.RewardMoneyClaimQueued)
            {
                lines.Add(PromptFragments.HelpRequestDelivery.MoneyRewardQueuedLine(request.RewardMoney));
            }

            if (request.RewardGiftGiven)
            {
                lines.Add(PromptFragments.HelpRequestDelivery.ThankYouMailLine);
            }

            if (request.SpecialFollowUpPlanned)
            {
                lines.Add(PromptFragments.HelpRequestDelivery.FollowUpLine);
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
            this.PushInteractionContext(
                observer,
                I18n.Get("log.interaction.romanticObserved", new { npc = targetNpc.Name }));
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
