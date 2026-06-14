using System;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class GiftOpportunityService
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly GiftSelector giftSelector;
    private readonly BehaviorMailService mailService;
    private readonly Random random;

    public GiftOpportunityService(
        ModConfig config,
        BehaviorMemory memory,
        GiftSelector giftSelector,
        BehaviorMailService mailService,
        Random random)
    {
        this.config = config;
        this.memory = memory;
        this.giftSelector = giftSelector;
        this.mailService = mailService;
        this.random = random;
    }

    public void TryPrepareDailyGiftOpportunity(NPC npc, LivingNpcState state)
    {
        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || GiftActionRules.HasAiGiftToday(state)
            || state.HighestUnresolvedConflictSeverity >= 30
            || state.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        if (WorldContext.For(npc).FriendshipHearts < GiftActionRules.MeaningfulGiftMinFriendshipHearts)
        {
            return;
        }

        int minChance = Math.Clamp(
            Math.Min(this.config.AiDailyGiftChanceMinPercent, this.config.AiDailyGiftChanceMaxPercent),
            0,
            100
        );
        int maxChance = Math.Clamp(
            Math.Max(this.config.AiDailyGiftChanceMinPercent, this.config.AiDailyGiftChanceMaxPercent),
            0,
            100
        );
        if (state.LastDailyGiftOpportunityRollTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        state.LastDailyGiftOpportunityRollTotalDays = Game1.Date.TotalDays;
        int chance = this.random.Next(minChance, maxChance + 1);
        if (this.random.Next(100) >= chance)
        {
            return;
        }

        state.DailyGiftOpportunityTotalDays = Game1.Date.TotalDays;
        state.DailyGiftOpportunityChancePercent = chance;
        state.DailyGiftOpportunityReason = $"{npc.displayName} is at {WorldContext.For(npc).FriendshipHearts} hearts and may naturally offer a small everyday gift during this conversation";
    }

    /// <summary>
    /// Once per day, on the first conversation with an eligible NPC, rolls a chance for the NPC to
    /// feel inclined to ask the farmer for a small favor during this chat (mirrors the daily gift
    /// opportunity). The readiness gate (trust/familiarity/cooldown/active request) still applies.
    /// </summary>
    public void TryPrepareDailyHelpRequestOpportunity(NPC npc, LivingNpcState state)
    {
        if (!this.config.EnableHelpRequests
            || this.config.HelpRequestDailyOfferChancePercent <= 0
            || state.DailyHelpRequestOpportunityTotalDays == Game1.Date.TotalDays
            || state.LastDailyHelpRequestOpportunityRollTotalDays == Game1.Date.TotalDays)
        {
            return;
        }

        state.LastDailyHelpRequestOpportunityRollTotalDays = Game1.Date.TotalDays;

        var readiness = BehaviorMemory.EvaluateHelpRequestReadiness(
            state,
            WorldContext.For(npc).FriendshipHearts,
            this.config.MaxPendingHelpRequestsPerNpc,
            this.config.HelpRequestCooldownDays,
            this.config.MinRelationshipTrustForHelpRequests,
            Game1.Date.TotalDays
        );
        if (!readiness.Allowed)
        {
            return;
        }

        int chance = Math.Clamp(this.config.HelpRequestDailyOfferChancePercent, 0, 100);
        if (this.random.Next(100) >= chance)
        {
            return;
        }

        state.DailyHelpRequestOpportunityTotalDays = Game1.Date.TotalDays;
    }

    public bool TryScheduleReciprocalGiftOpportunity(NPC npc, LivingNpcState state, GiftMemoryDetails gift)
    {
        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || !GiftActionRules.IsEligibleForSmallGift(npc, state)
            || state.HighestUnresolvedConflictSeverity >= 30
            || gift.TasteScore is 4 or 6
            || this.mailService.HasPendingGiftMail(state, "reciprocal"))
        {
            return false;
        }

        int chance = gift.TasteScore switch
        {
            0 => 75,
            2 => 45,
            8 => 10,
            _ => 0
        };
        if (chance <= 0 || this.random.Next(100) >= chance)
        {
            return false;
        }

        int delayDays = gift.TasteScore switch
        {
            0 => this.random.Next(1, 3),
            2 => this.random.Next(1, 4),
            _ => this.random.Next(1, 4)
        };
        GiftTier reciprocalTier = this.ShouldUseMeaningfulReciprocalGift(npc, state, gift)
            ? GiftTier.Meaningful
            : GiftTier.Small;
        GiftSelection selection = reciprocalTier == GiftTier.Meaningful
            ? this.giftSelector.ChooseMeaningful(npc, state, gift.ItemName, gift.TastePromptLabel)
            : this.giftSelector.Choose(npc, state, gift.ItemName, gift.TastePromptLabel);
        string mailReason = GiftActionRules.BuildGiftSelectionReason(
            $"they planned a delayed return gift because the farmer recently gave {npc.displayName} {gift.ItemName}, a {gift.TastePromptLabel}",
            selection
        );
        if (!this.mailService.ScheduleGiftMail(npc, state, selection, "reciprocal", mailReason, delayDays, gift.ItemName))
        {
            return false;
        }

        if (reciprocalTier == GiftTier.Meaningful)
        {
            state.LastAiMeaningfulGiftTotalDays = Game1.Date.TotalDays;
        }
        else
        {
            state.LastAiSmallGiftTotalDays = Game1.Date.TotalDays;
        }

        GiftActionRules.ClearGiftOpportunities(state);
        this.memory.RecordNpcWorldAction(
            npc,
            "ScheduledReciprocalGiftMail",
            mailReason,
            this.config.MaxMemoryEntriesPerNpc
        );
        return true;
    }

    private bool ShouldUseMeaningfulReciprocalGift(NPC npc, LivingNpcState state, GiftMemoryDetails gift)
    {
        if (gift.TasteScore != 0
            || !this.config.AllowAiMeaningfulGifts
            || !GiftActionRules.IsEligibleForMeaningfulGift(npc, out _))
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
            return false;
        }

        return this.random.Next(100) < 25;
    }
}
