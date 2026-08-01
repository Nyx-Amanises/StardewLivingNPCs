using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
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

        if (state.DailyGiftOpportunityTotalDays != Game1.Date.TotalDays)
        {
            reason = "no active daily gift opportunity authorized this conversation gift";
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
            this.feedback.ShowAfterDialogue(I18n.Get("gift.mailWhenInventoryFull", new { npc = npc.displayName, item = gift.DisplayName }));
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
                I18n.Get("log.gift.selectedSmall", new { npc = npc.Name, gift = selection.DebugName, item = selection.ItemId, reason = selection.Reason }),
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

        // Guard the upper bound: Math.Clamp throws when max < min, and the config allows values below 25.
        int maxAmount = Math.Max(25, this.config.MaxAiMoneyGiftAmount);
        int amount = Math.Clamp(action.Amount <= 0 ? 100 : action.Amount, 25, maxAmount);
        Game1.player.Money += amount;
        state.LastAiMoneyGiftTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveMoney",
            BuildWorldActionReason(action.Reason, $"they gave the farmer {amount}g after an AI conversation"),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they gave the farmer some money");
        this.feedback.ShowAfterDialogue(I18n.Get("gift.gaveMoney", new { npc = npc.displayName, amount }));
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

        if (state.DailyGiftOpportunityTotalDays != Game1.Date.TotalDays)
        {
            reason = "no active daily gift opportunity authorized this meaningful gift";
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
            this.feedback.ShowAfterDialogue(I18n.Get("gift.mailWhenInventoryFull", new { npc = npc.displayName, item = gift.DisplayName }));
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
                I18n.Get("log.gift.selectedMeaningful", new { npc = npc.Name, gift = selection.DebugName, item = selection.ItemId, reason = selection.Reason }),
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

            if (TryChooseExplicitRequestableGift(npc, action, tier, npcResponse, out selection, out reason))
            {
                return true;
            }

            reason = string.IsNullOrWhiteSpace(reason)
                ? $"requested gift item {action.ItemId} is not in the allowed {tier.ToString().ToLowerInvariant()} gift pool"
                : reason;
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

    private static bool TryChooseExplicitRequestableGift(
        NPC npc,
        ValleyTalkWorldActionRequest action,
        GiftTier tier,
        string npcResponse,
        out GiftSelection selection,
        out string reason)
    {
        selection = null!;
        reason = string.Empty;
        if (tier != GiftTier.Small)
        {
            reason = $"requested gift item {action.ItemId} is not in the allowed {tier.ToString().ToLowerInvariant()} gift pool";
            return false;
        }

        string normalizedItemId = NormalizeObjectItemId(action.ItemId);
        if (string.IsNullOrWhiteSpace(normalizedItemId))
        {
            reason = "gift action provided an invalid itemId";
            return false;
        }

        string requestableLabel = string.Empty;
        foreach ((string itemId, string label) in HelpRequestAdvisor.GetRequestableItems(npc))
        {
            if (string.Equals(itemId, normalizedItemId, StringComparison.OrdinalIgnoreCase))
            {
                requestableLabel = label;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(requestableLabel))
        {
            reason = $"requested gift item {action.ItemId} is not in the allowed small gift pool or current low-risk item list";
            return false;
        }

        SObject gift;
        try
        {
            gift = ItemRegistry.Create<SObject>(normalizedItemId);
        }
        catch
        {
            reason = $"requested gift item {action.ItemId} could not be created";
            return false;
        }

        string displayName = string.IsNullOrWhiteSpace(gift.DisplayName)
            ? requestableLabel
            : gift.DisplayName;
        string name = string.IsNullOrWhiteSpace(gift.Name)
            ? requestableLabel
            : gift.Name;
        string[] labels =
        [
            action.ItemLabel,
            requestableLabel,
            displayName,
            name
        ];
        if (!VisibleDialogueMentionsAnyLabel(npcResponse, labels))
        {
            reason = $"requested gift item {action.ItemId} is not visibly named in the NPC reply";
            return false;
        }

        selection = new GiftSelection(
            normalizedItemId,
            displayName,
            tier,
            "the AI named a concrete low-risk item that the visible dialogue offered",
            string.Empty
        );
        return true;
    }

    /// <summary>
    /// 主机裁决 farmhand 上报的经济动作，但不直接改远端 Farmer 的背包或金钱；成功时只
    /// 返回纯数据 grant，交由 farmhand 在 ExchangeAck 主线程回调里本地兑现。NPC 每日
    /// 上限、礼物池、可见台词否决后的选择和心智账本仍由主机唯一写入。
    /// </summary>
    public bool TryPlanRemoteGrant(
        NPC npc,
        Farmer recipient,
        ValleyTalkWorldActionRequest action,
        string playerText,
        string npcResponse,
        out RemoteWorldActionGrant? grant,
        out string reason)
    {
        grant = null;
        reason = string.Empty;
        if (!this.CanUseRemoteWorldAction(npc, recipient, action.Type, out reason))
        {
            return false;
        }

        return action.Type switch
        {
            "give_small_gift" => this.TryPlanRemoteGift(
                npc,
                recipient,
                action,
                GiftTier.Small,
                playerText,
                npcResponse,
                out grant,
                out reason),
            "give_meaningful_gift" => this.TryPlanRemoteGift(
                npc,
                recipient,
                action,
                GiftTier.Meaningful,
                playerText,
                npcResponse,
                out grant,
                out reason),
            "give_money" => this.TryPlanRemoteMoney(npc, action, out grant, out reason),
            _ => false
        };
    }

    private bool CanUseRemoteWorldAction(NPC npc, Farmer recipient, string actionType, out string reason)
    {
        reason = string.Empty;
        if (RsvAiPolicy.IsBlockedNpc(npc) || !this.config.EnableAiWorldActions)
        {
            reason = "AI world actions are disabled for this NPC";
            return false;
        }

        if (npc.currentLocation == null
            || recipient.currentLocation == null
            || npc.currentLocation != recipient.currentLocation
            || Vector2.Distance(npc.Tile, recipient.Tile) > this.config.MaxInteractionDistanceTiles + 2)
        {
            reason = "reporting farmer is not close enough to the NPC in the same location";
            return false;
        }

        LivingNpcState? state = this.memory.GetState(npc);
        if (state == null)
        {
            reason = "there is no NPC state yet";
            return false;
        }

        if (state.HighestUnresolvedConflictSeverity >= 30)
        {
            reason = $"{actionType} is blocked by unresolved conflict";
            return false;
        }

        bool requiresFriendly = actionType is "give_meaningful_gift" or "give_money";
        bool relationshipReady = requiresFriendly
            ? state.InteractionComfortTier is "Friendly" or "Trusted" or "Intimate"
            : state.InteractionComfortTier is "Familiar" or "Friendly" or "Trusted" or "Intimate";
        if (!relationshipReady && actionType != "give_small_gift")
        {
            reason = $"{actionType} requires a closer relationship";
            return false;
        }

        return true;
    }

    private bool TryPlanRemoteGift(
        NPC npc,
        Farmer recipient,
        ValleyTalkWorldActionRequest action,
        GiftTier tier,
        string playerText,
        string npcResponse,
        out RemoteWorldActionGrant? grant,
        out string reason)
    {
        grant = null;
        reason = string.Empty;
        LivingNpcState? state = this.memory.GetState(npc);
        if (state == null
            || GiftActionRules.HasAiGiftToday(state)
            || state.DailyGiftOpportunityTotalDays != Game1.Date.TotalDays)
        {
            reason = "no active daily gift opportunity or another AI gift was already used today";
            return false;
        }

        int friendshipHearts = recipient.getFriendshipHeartLevelForNPC(npc.Name);
        if (tier == GiftTier.Small)
        {
            if (!this.config.AllowAiSmallGifts
                || (friendshipHearts < GiftActionRules.SmallGiftMinFriendshipHearts
                    && state.Familiarity < GiftActionRules.SmallGiftMinFamiliarity))
            {
                reason = "small gift relationship requirements are not met";
                return false;
            }
        }
        else
        {
            if (!this.config.AllowAiMeaningfulGifts
                || friendshipHearts < GiftActionRules.MeaningfulGiftMinFriendshipHearts)
            {
                reason = "meaningful gift relationship requirements are not met";
                return false;
            }

            int daysSinceLastMeaningfulGift = state.LastAiMeaningfulGiftTotalDays < 0
                ? int.MaxValue
                : Game1.Date.TotalDays - state.LastAiMeaningfulGiftTotalDays;
            bool bypassCooldown = friendshipHearts >= GiftActionRules.MeaningfulGiftNoCooldownFriendshipHearts;
            if (!bypassCooldown && daysSinceLastMeaningfulGift < this.config.AiMeaningfulGiftCooldownDays)
            {
                reason = "meaningful gift cooldown is still active";
                return false;
            }
        }

        if (!this.TrySelectGiftForConversationAction(
                action,
                tier,
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
        string motive = GiftActionRules.DetermineGiftMotive(action, selection, tier);
        if (tier == GiftTier.Meaningful)
        {
            state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
        }
        else
        {
            state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        }

        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        GiftActionRules.ClearGiftOpportunities(state);
        string actionName = tier == GiftTier.Meaningful ? "GaveMeaningfulGift" : "GaveSmallGift";
        string tierLabel = tier == GiftTier.Meaningful ? "a meaningful " : string.Empty;
        this.memory.RecordNpcWorldAction(
            npc,
            actionName,
            BuildWorldActionReason(
                action.Reason,
                GiftActionRules.BuildGiftSelectionReason(
                    $"they gave the farmhand {tierLabel}{gift.DisplayName} after an AI conversation",
                    selection)),
            this.config.MaxMemoryEntriesPerNpc);
        MarkStateAfterWorldAction(state, $"they gave the farmhand {tierLabel}gift");
        grant = new RemoteWorldActionGrant(
            selection.ItemId,
            1,
            0,
            0,
            GiftActionRules.BuildGiftHudMessage(npc, gift.DisplayName, motive));
        return true;
    }

    private bool TryPlanRemoteMoney(
        NPC npc,
        ValleyTalkWorldActionRequest action,
        out RemoteWorldActionGrant? grant,
        out string reason)
    {
        grant = null;
        reason = string.Empty;
        LivingNpcState? state = this.memory.GetState(npc);
        if (!this.config.AllowAiMoneyGifts
            || state == null
            || state.LastAiMoneyGiftTotalDays == Game1.Date.TotalDays)
        {
            reason = "money gifts are disabled or already used today";
            return false;
        }

        int maxAmount = Math.Max(25, this.config.MaxAiMoneyGiftAmount);
        int amount = Math.Clamp(action.Amount <= 0 ? 100 : action.Amount, 25, maxAmount);
        state.LastAiMoneyGiftTotalDays = Game1.Date.TotalDays;
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveMoney",
            BuildWorldActionReason(action.Reason, $"they gave the farmhand {amount}g after an AI conversation"),
            this.config.MaxMemoryEntriesPerNpc);
        MarkStateAfterWorldAction(state, "they gave the farmhand some money");
        grant = new RemoteWorldActionGrant(
            string.Empty,
            0,
            0,
            amount,
            I18n.Get("gift.gaveMoney", new { npc = npc.displayName, amount }));
        return true;
    }

    private static bool VisibleDialogueMentionsAnyLabel(string npcResponse, IEnumerable<string> labels)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return false;
        }

        foreach (string label in labels)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            string trimmed = label.Trim();
            if (trimmed.Length < 2)
            {
                continue;
            }

            if (npcResponse.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeObjectItemId(string itemId)
    {
        string trimmed = itemId.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
        {
            return $"(O){trimmed.Substring(3)}";
        }

        return int.TryParse(trimmed, out int rawId)
            ? $"(O){rawId}"
            : trimmed;
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

internal sealed record RemoteWorldActionGrant(
    string ItemId,
    int Stack,
    int Quality,
    int Money,
    string HudMessage);
