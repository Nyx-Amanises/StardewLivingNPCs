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

    /// <summary>
    /// 主机为远程 farmhand 完成权威奖励裁决。好感和物品只形成纯数据结果，不能调用
    /// Game1.player（否则会把奖励给主机）；金钱仍进入可领取的任务投影闭环。
    /// </summary>
    public RemoteHelpRequestRewardOutcome RewardFulfilledRemote(
        NPC npc,
        IReadOnlyList<NpcHelpRequestFact> requests)
    {
        int friendshipDelta = 0;
        var itemGrants = new List<RemoteHelpRequestItemGrant>();
        var feedbackMessages = new List<string>();
        foreach (NpcHelpRequestFact request in requests.Where(request => request.Status == "Fulfilled"))
        {
            if (!request.RewardGranted)
            {
                int minReward = Math.Min(this.config.MinHelpRequestFriendshipReward, this.config.MaxHelpRequestFriendshipReward);
                int maxReward = Math.Max(this.config.MinHelpRequestFriendshipReward, this.config.MaxHelpRequestFriendshipReward);
                int reward = Math.Clamp(request.RewardFriendship, minReward, maxReward);
                request.RewardFriendship = reward;
                request.RewardGranted = true;
                friendshipDelta += reward;
                this.memory.RecordNpcWorldAction(
                    npc,
                    "CompletedHelpRequest",
                    $"the farmhand completed a personal help request and earned {reward} friendship: {request.Summary}",
                    this.config.MaxMemoryEntriesPerNpc);
                this.communityRipples.Spread(
                    npc,
                    "helped",
                    $"the farmhand helped {npc.displayName} with a personal request",
                    importance: 78);
                feedbackMessages.Add(I18n.Get("help.reward.friendshipHud", new { npc = npc.displayName }));
            }

            if (!request.RewardMoneyGranted && !request.RewardMoneyClaimQueued)
            {
                this.QueueMoneyRewardClaim(npc, request);
            }

            if (!request.RewardGiftGiven
                && this.TryPlanRemoteRewardGift(npc, request, out RemoteHelpRequestItemGrant? itemGrant)
                && itemGrant != null)
            {
                itemGrants.Add(itemGrant);
            }
        }

        return new RemoteHelpRequestRewardOutcome(friendshipDelta, itemGrants, feedbackMessages);
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

            this.feedback.Show(I18n.Get("help.reward.friendshipHud", new { npc = npc.displayName }));
            this.communityRipples.Spread(
                npc,
                "helped",
                $"the farmer helped {npc.displayName} with a personal request",
                importance: 78
            );
        }

        if (!request.RewardMoneyGranted
            && !request.RewardMoneyClaimQueued)
        {
            this.QueueMoneyRewardClaim(npc, request);
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

        // 谢礼按求助主题加权：把 NPC 的求助主题词并入话题文本（关键词表认英文 tag 词）。
        string themeHint = HelpRequestAdvisor.BuildRewardGiftThemeHint(npc);
        GiftSelection selection = this.giftSelector.Choose(npc, state, $"{request.Summary} {themeHint}", request.Resolution);
        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        string sourceGiftName = BuildHelpRequestRewardSourceGift(request);
        string giftReason = GiftActionRules.BuildGiftSelectionReason(
            $"they scheduled {gift.DisplayName} by mail after the farmer completed a personal help request: {request.Summary}",
            selection
        );
        if (!this.mailService.ScheduleGiftMail(
                npc,
                state,
                selection,
                "help_request_reward",
                giftReason,
                dueInDays: 1,
                sourceGiftName: sourceGiftName))
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
        MarkStateAfterWorldAction(state, "they mailed the farmer a help request reward gift");
        if (this.config.Debug)
        {
            this.monitor.Log(
                I18n.Get("log.help.selectedRewardGift", new { npc = npc.Name, gift = selection.DebugName, item = selection.ItemId, reason = selection.Reason }),
                LogLevel.Debug
            );
        }
        return true;
    }

    private bool TryPlanRemoteRewardGift(
        NPC npc,
        NpcHelpRequestFact request,
        out RemoteHelpRequestItemGrant? itemGrant)
    {
        itemGrant = null;
        if (!this.config.AllowAiSmallGifts)
        {
            return false;
        }

        LivingNpcState? state = this.memory.GetState(npc);
        if (state == null || GiftActionRules.HasAiGiftToday(state))
        {
            return false;
        }

        string themeHint = HelpRequestAdvisor.BuildRewardGiftThemeHint(npc);
        GiftSelection selection = this.giftSelector.Choose(
            npc,
            state,
            $"{request.Summary} {themeHint}",
            request.Resolution);
        SObject gift = ItemRegistry.Create<SObject>(selection.ItemId);
        string giftReason = GiftActionRules.BuildGiftSelectionReason(
            $"they gave the farmhand {gift.DisplayName} after a completed personal help request: {request.Summary}",
            selection);
        request.RewardGiftGiven = true;
        state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        BehaviorMailService.RememberAiGiftItem(state, selection.ItemId);
        GiftActionRules.ClearGiftOpportunities(state);
        this.memory.RecordNpcWorldAction(
            npc,
            "GaveHelpRequestRewardGift",
            giftReason,
            this.config.MaxMemoryEntriesPerNpc);
        MarkStateAfterWorldAction(state, "they gave the farmhand a help request reward gift");
        itemGrant = new RemoteHelpRequestItemGrant(
            selection.ItemId,
            1,
            0,
            GiftActionRules.BuildGiftHudMessage(npc, gift.DisplayName, "thanks"));
        return true;
    }

    private static string BuildHelpRequestRewardSourceGift(NpcHelpRequestFact request)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedItemLabel))
        {
            return request.RequestedItemLabel.Trim();
        }

        var completedStep = request.Steps
            .LastOrDefault(step => step.Status == "Fulfilled" && !string.IsNullOrWhiteSpace(step.RequestedItemLabel));
        if (completedStep != null)
        {
            return completedStep.RequestedItemLabel.Trim();
        }

        return string.IsNullOrWhiteSpace(request.Summary)
            ? string.Empty
            : request.Summary.Trim();
    }

    private void QueueMoneyRewardClaim(NPC npc, NpcHelpRequestFact request)
    {
        int amount = Math.Clamp(request.RewardMoney <= 0 ? 200 : request.RewardMoney, 200, 10000);
        request.RewardMoney = amount;
        request.RewardMoneyGranted = false;
        request.RewardMoneyClaimQueued = true;
        request.RewardMoneyQuestPosted = false;
        this.memory.RecordNpcWorldAction(
            npc,
            "QueuedHelpRequestMoneyReward",
            $"the help request system added a {amount}g claimable quest reward: {request.Summary}",
            this.config.MaxMemoryEntriesPerNpc
        );
    }

    private static void MarkStateAfterWorldAction(LivingNpcState state, string lastInteraction)
    {
        state.LastInteraction = lastInteraction;
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }
}

internal sealed record RemoteHelpRequestRewardOutcome(
    int FriendshipDelta,
    IReadOnlyList<RemoteHelpRequestItemGrant> ItemGrants,
    IReadOnlyList<string> FeedbackMessages);

internal sealed record RemoteHelpRequestItemGrant(
    string ItemId,
    int Stack,
    int Quality,
    string HudMessage);
