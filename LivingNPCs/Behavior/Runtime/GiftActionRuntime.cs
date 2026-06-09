using System;
using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal sealed class GiftActionRuntime
{
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly GiftSelector giftSelector;
    private readonly BehaviorMailService mailService;
    private readonly BehaviorFeedbackService feedback;
    private readonly CanUseWorldActionHandler canUseWorldAction;

    public GiftActionRuntime(
        ModConfig config,
        IMonitor monitor,
        BehaviorMemory memory,
        GiftSelector giftSelector,
        BehaviorMailService mailService,
        BehaviorFeedbackService feedback,
        CanUseWorldActionHandler canUseWorldAction)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.giftSelector = giftSelector;
        this.mailService = mailService;
        this.feedback = feedback;
        this.canUseWorldAction = canUseWorldAction;
    }

    public bool TryGiveSmallGift(
        NPC npc,
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse,
        out string reason
    )
    {
        reason = string.Empty;
        if (!this.canUseWorldAction(npc, "small_gift", requireFriendly: false, out reason, allowDuringEvents: false, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiSmallGifts
            || state == null
            || GiftActionRules.HasAiGiftToday(state))
        {
            reason = "small gifts are disabled or another AI gift was already used today";
            return false;
        }

        if (!GiftActionRules.IsEligibleForSmallGift(npc, state))
        {
            reason = $"small gifts require at least {GiftActionRules.SmallGiftMinFriendshipHearts} hearts or familiarity {GiftActionRules.SmallGiftMinFamiliarity}";
            return false;
        }

        if (!this.TrySelectGiftForConversationAction(
                action,
                GiftTier.Small,
                npc,
                state,
                playerText,
                npcResponse,
                out GiftSelection selection,
                out reason))
        {
            return false;
        }

        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        string motive = GiftActionRules.DetermineGiftMotive(action, selection, GiftTier.Small);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            string giftReason = BuildWorldActionReason(
                action.Reason,
                GiftActionRules.BuildGiftSelectionReason(
                    $"they tried to give the farmer {gift.DisplayName} after an AI conversation, but the farmer's inventory was full",
                    selection
                )
            );
            if (!this.mailService.ScheduleGiftMail(npc, state, selection, "inventory_full", giftReason, dueInDays: 1))
            {
                reason = "player inventory is full";
                return false;
            }

            state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
            GiftActionRules.ClearGiftOpportunities(state);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledSmallGiftMail",
                giftReason,
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(state, "they mailed the farmer a small gift after the farmer's inventory was full");
            this.feedback.ShowAfterDialogue($"LivingNPCs：你的背包满了，{npc.displayName} 会把 {gift.DisplayName} 明天寄给你。");
            return true;
        }

        state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        GiftActionRules.ClearGiftOpportunities(state);
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveSmallGift",
            BuildWorldActionReason(
                action.Reason,
                GiftActionRules.BuildGiftSelectionReason(
                    $"they gave the farmer {gift.DisplayName} after an AI conversation",
                    selection
                )
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they gave the farmer a small gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Selected AI gift for {npc.Name}: {selection.DebugName} ({selection.ItemId}); {selection.Reason}.",
                LogLevel.Debug
            );
        }

        this.feedback.ShowAfterDialogue(GiftActionRules.BuildGiftHudMessage(npc, gift.DisplayName, motive));
        return true;
    }

    public bool TryGiveMoney(NPC npc, ValleyTalkWorldActionRequest action, out string reason)
    {
        reason = string.Empty;
        if (!this.canUseWorldAction(npc, "money", requireFriendly: true, out reason, allowDuringEvents: false, allowDistantWhenExplicit: false))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiMoneyGifts || state == null || state.LastAiMoneyGiftTotalDays == Game1.Date.TotalDays)
        {
            reason = "money gifts are disabled or already used today";
            return false;
        }

        int amount = Math.Clamp(action.Amount <= 0 ? 100 : action.Amount, 25, this.config.MaxAiMoneyGiftAmount);
        Game1.player.Money += amount;
        state.LastAiMoneyGiftTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveMoney",
            BuildWorldActionReason(action.Reason, $"they gave the farmer {amount}g after an AI conversation"),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they gave the farmer some money");
        this.feedback.ShowAfterDialogue($"LivingNPCs：{npc.displayName} 给了你 {amount}g。");
        return true;
    }

    public bool TryGiveMeaningfulGift(
        NPC npc,
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse,
        out string reason
    )
    {
        reason = string.Empty;
        if (!this.canUseWorldAction(npc, "meaningful_gift", requireFriendly: true, out reason, allowDuringEvents: false, allowDistantWhenExplicit: true))
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (!this.config.AllowAiMeaningfulGifts || state == null)
        {
            reason = "meaningful gifts are disabled";
            return false;
        }

        if (GiftActionRules.HasAiGiftToday(state))
        {
            reason = "another AI gift was already used today";
            return false;
        }

        if (!GiftActionRules.IsEligibleForMeaningfulGift(npc, out reason))
        {
            return false;
        }

        int daysSinceLastMeaningfulGift = state.LastAiMeaningfulGiftTotalDays < 0
            ? int.MaxValue
            : Game1.Date.TotalDays - state.LastAiMeaningfulGiftTotalDays;
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        bool bypassCooldown = friendshipHearts >= GiftActionRules.MeaningfulGiftNoCooldownFriendshipHearts;
        if (!bypassCooldown && daysSinceLastMeaningfulGift < this.config.AiMeaningfulGiftCooldownDays)
        {
            reason = "meaningful gift cooldown is still active";
            return false;
        }

        if (!this.TrySelectGiftForConversationAction(
                action,
                GiftTier.Meaningful,
                npc,
                state,
                playerText,
                npcResponse,
                out GiftSelection selection,
                out reason))
        {
            return false;
        }

        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        string motive = GiftActionRules.DetermineGiftMotive(action, selection, GiftTier.Meaningful);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            string giftReason = BuildWorldActionReason(
                action.Reason,
                GiftActionRules.BuildGiftSelectionReason(
                    $"they tried to give the farmer a meaningful {gift.DisplayName}, but the farmer's inventory was full",
                    selection
                )
            );
            if (!this.mailService.ScheduleGiftMail(npc, state, selection, "inventory_full", giftReason, dueInDays: 1))
            {
                reason = "player inventory is full";
                return false;
            }

            state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
            GiftActionRules.ClearGiftOpportunities(state);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledMeaningfulGiftMail",
                giftReason,
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(state, "they mailed the farmer a meaningful gift after the farmer's inventory was full");
            this.feedback.ShowAfterDialogue($"LivingNPCs：你的背包满了，{npc.displayName} 会把 {gift.DisplayName} 明天寄给你。");
            return true;
        }

        state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        GiftActionRules.ClearGiftOpportunities(state);
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveMeaningfulGift",
            BuildWorldActionReason(
                action.Reason,
                GiftActionRules.BuildGiftSelectionReason(
                    $"they gave the farmer a meaningful {gift.DisplayName}",
                    selection
                )
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they gave the farmer a meaningful gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Selected meaningful AI gift for {npc.Name}: {selection.DebugName} ({selection.ItemId}); {selection.Reason}.",
                LogLevel.Debug
            );
        }

        this.feedback.ShowAfterDialogue(GiftActionRules.BuildGiftHudMessage(npc, gift.DisplayName, motive));
        return true;
    }

    private bool TrySelectGiftForConversationAction(
        ValleyTalkWorldActionRequest action,
        GiftTier tier,
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse,
        out GiftSelection selection,
        out string reason
    )
    {
        selection = null!;
        reason = string.Empty;
        if (!string.IsNullOrWhiteSpace(action.ItemId))
        {
            if (this.giftSelector.TryChooseRequested(npc, action.ItemId, tier, out GiftSelection? requestedSelection)
                && requestedSelection != null)
            {
                selection = requestedSelection;
                return true;
            }

            reason = $"requested gift item {action.ItemId} is not in the allowed {tier.ToString().ToLowerInvariant()} gift pool";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(action.ItemLabel))
        {
            reason = $"gift action named {action.ItemLabel}, but did not provide a valid itemId";
            return false;
        }

        if (GiftActionRules.VisibleDialoguePromisesUnsupportedGift(npcResponse))
        {
            reason = "visible dialogue promised a specific unsupported gift without a valid itemId";
            return false;
        }

        selection = tier == GiftTier.Meaningful
            ? this.giftSelector.ChooseMeaningful(npc, state, playerText, npcResponse)
            : this.giftSelector.Choose(npc, state, playerText, npcResponse);
        return true;
    }

    private static string BuildWorldActionReason(string requestedReason, string fallback)
    {
        return string.IsNullOrWhiteSpace(requestedReason)
            ? fallback
            : $"{requestedReason.Trim()}; {fallback}";
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
