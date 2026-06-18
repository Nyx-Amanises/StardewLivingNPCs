using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using SObject = StardewValley.Object;

namespace LivingNPCs.Behavior;

internal sealed class HelpRequestRewardService
{
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly GiftSelector giftSelector;
    private readonly BehaviorMailService mailService;
    private readonly BehaviorFeedbackService feedback;
    private readonly CommunityRippleRuntime communityRipples;

    public HelpRequestRewardService(
        ModConfig config,
        IMonitor monitor,
        BehaviorMemory memory,
        GiftSelector giftSelector,
        BehaviorMailService mailService,
        BehaviorFeedbackService feedback,
        CommunityRippleRuntime communityRipples)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.giftSelector = giftSelector;
        this.mailService = mailService;
        this.feedback = feedback;
        this.communityRipples = communityRipples;
    }

    public void RewardFulfilled(NPC npc, IReadOnlyList<NpcHelpRequestFact> requests, bool queueAmbientThanks = true)
    {
        foreach (var request in requests.Where(request => request.Status == "Fulfilled"))
        {
            this.RewardFulfilled(npc, request, queueAmbientThanks);
        }
    }

    private void RewardFulfilled(NPC npc, NpcHelpRequestFact request, bool queueAmbientThanks)
    {
        if (!request.RewardGranted)
        {
            int minReward = Math.Min(this.config.MinHelpRequestFriendshipReward, this.config.MaxHelpRequestFriendshipReward);
            int maxReward = Math.Max(this.config.MinHelpRequestFriendshipReward, this.config.MaxHelpRequestFriendshipReward);
            int friendshipReward = Math.Clamp(request.RewardFriendship, minReward, maxReward);
            Game1.player.changeFriendship(friendshipReward, npc);
            request.RewardFriendship = friendshipReward;
            request.RewardGranted = true;

            this.memory.RecordNpcWorldAction(
                npc,
                "CompletedHelpRequest",
                $"the farmer completed a personal help request and earned {friendshipReward} friendship: {request.Summary}",
                this.config.MaxMemoryEntriesPerNpc
            );
            if (queueAmbientThanks)
            {
                this.feedback.QueueAmbientRemark(npc, I18n.Get("help.thanksFulfilled"), 0);
            }

            this.feedback.Show(I18n.Get("help.reward.friendshipHud", new { npc = npc.displayName, amount = friendshipReward }));
            this.communityRipples.Spread(
                npc,
                "helped",
                $"the farmer helped {npc.displayName} with a personal request",
                importance: 78
            );
        }

        if (!request.RewardMoneyGranted)
        {
            this.GrantOrScheduleMoneyReward(npc, request);
        }

        if (!request.RewardGiftGiven)
        {
            this.TryGiveRewardGift(npc, request);
        }
    }

    private bool TryGiveRewardGift(NPC npc, NpcHelpRequestFact request)
    {
        if (!this.config.AllowAiSmallGifts)
        {
            return false;
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return false;
        }

        if (GiftActionRules.HasAiGiftToday(state))
        {
            return false;
        }

        GiftSelection selection = this.giftSelector.Choose(npc, state, request.Summary, request.Resolution);
        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        if (!Game1.player.addItemToInventoryBool(gift))
        {
            string giftReason = GiftActionRules.BuildGiftSelectionReason(
                $"they tried to give the farmer {gift.DisplayName} after a fulfilled personal help request, but the farmer's inventory was full",
                selection
            );
            if (!this.mailService.ScheduleGiftMail(npc, state, selection, "inventory_full", giftReason, dueInDays: 1))
            {
                return false;
            }

            request.RewardGiftGiven = true;
            state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
            GiftActionRules.ClearGiftOpportunities(state);
            this.memory.RecordNpcWorldAction(
                npc,
                "ScheduledHelpRequestRewardGiftMail",
                giftReason,
                this.config.MaxMemoryEntriesPerNpc
            );
            MarkStateAfterWorldAction(state, "they mailed the farmer a help request reward gift after the farmer's inventory was full");
            this.feedback.ShowAfterDialogue(I18n.Get("help.reward.inventoryFullMail", new { npc = npc.displayName, item = gift.DisplayName }));
            return true;
        }

        request.RewardGiftGiven = true;
        state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        GiftActionRules.ClearGiftOpportunities(state);
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveHelpRequestRewardGift",
            GiftActionRules.BuildGiftSelectionReason(
                $"they gave the farmer {gift.DisplayName} after a fulfilled personal help request",
                selection
            ),
            this.config.MaxMemoryEntriesPerNpc
        );
        MarkStateAfterWorldAction(state, "they thanked the farmer with a small gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                $"Selected help request reward gift for {npc.Name}: {selection.DebugName} ({selection.ItemId}); {selection.Reason}.",
                LogLevel.Debug
            );
        }

        this.feedback.ShowAfterDialogue(GiftActionRules.BuildGiftHudMessage(npc, gift.DisplayName, "thanks"));
        return true;
    }

    private void GrantOrScheduleMoneyReward(NPC npc, NpcHelpRequestFact request)
    {
        if (Game1.player == null)
        {
            return;
        }

        // Vanilla item-delivery quests pay out immediately on hand-in, so grant the gold directly
        // rather than mailing it later.
        int amount = Math.Clamp(request.RewardMoney <= 0 ? 200 : request.RewardMoney, 200, 10000);
        request.RewardMoney = amount;
        Game1.player.Money += amount;
        request.RewardMoneyByMail = false;
        request.RewardMoneyMailKey = string.Empty;
        request.RewardMoneyMailTotalDays = -1;
        request.RewardMoneyGranted = true;
        this.memory.RecordNpcWorldAction(
            npc,
            "GrantedHelpRequestMoneyReward",
            $"the help request system granted a {amount}g reward: {request.Summary}",
            this.config.MaxMemoryEntriesPerNpc
        );
        this.feedback.ShowAfterDialogue(I18n.Get("help.reward.moneyHud", new { amount }));
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}
