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

    public void TryScheduleReciprocalGiftOpportunity(NPC npc, LivingNpcState state, GiftMemoryDetails gift)
    {
        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || !GiftActionRules.IsEligibleForSmallGift(npc, state)
            || state.HighestUnresolvedConflictSeverity >= 30
            || gift.TasteScore is 4 or 6
            || this.mailService.HasPendingGiftMail(state, "reciprocal")
            || state.PendingReciprocalGiftDueTotalDays >= Game1.Date.TotalDays)
        {
            return;
        }

        if (state.PendingReciprocalGiftDueTotalDays >= Game1.Date.TotalDays)
        {
            return;
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
            return;
        }

        int delayDays = gift.TasteScore switch
        {
            0 => this.random.Next(0, 3),
            2 => this.random.Next(0, 4),
            _ => this.random.Next(1, 4)
        };
        if (delayDays == 0 && GiftActionRules.HasAiGiftToday(state))
        {
            delayDays = 1;
        }

        if (delayDays > 0)
        {
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
            if (this.mailService.ScheduleGiftMail(npc, state, selection, "reciprocal", mailReason, delayDays, gift.ItemName))
            {
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
            }

            return;
        }

        state.PendingReciprocalGiftDueTotalDays = Game1.Date.TotalDays + delayDays;
        state.PendingReciprocalGiftSourceGiftName = gift.ItemName;
        state.PendingReciprocalGiftReason = $"the farmer recently gave {npc.displayName} {gift.ItemName}, a {gift.TastePromptLabel}; a small return gift would feel reciprocal";
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
