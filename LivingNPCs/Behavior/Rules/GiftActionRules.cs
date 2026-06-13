using StardewValley;

namespace LivingNPCs.Behavior;

internal static class GiftActionRules
{
    public const int SmallGiftMinFriendshipHearts = 2;
    public const int SmallGiftMinFamiliarity = 15;
    public const int MeaningfulGiftMinFriendshipHearts = 5;
    public const int MeaningfulGiftNoCooldownFriendshipHearts = 8;

    public static bool HasAiGiftToday(LivingNpcState state)
    {
        return state.LastAiSmallGiftTotalDays == Game1.Date.TotalDays
            || state.LastAiMeaningfulGiftTotalDays == Game1.Date.TotalDays;
    }

    public static bool IsEligibleForSmallGift(NPC npc, LivingNpcState state)
    {
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        return friendshipHearts >= SmallGiftMinFriendshipHearts
            || state.Familiarity >= SmallGiftMinFamiliarity;
    }

    public static bool IsEligibleForMeaningfulGift(NPC npc, out string reason)
    {
        reason = string.Empty;
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        if (friendshipHearts < MeaningfulGiftMinFriendshipHearts)
        {
            reason = $"meaningful gifts require at least {MeaningfulGiftMinFriendshipHearts} hearts";
            return false;
        }

        return true;
    }

    public static void ClearGiftOpportunities(LivingNpcState state)
    {
        state.DailyGiftOpportunityTotalDays = -1;
        state.DailyGiftOpportunityChancePercent = 0;
        state.DailyGiftOpportunityReason = string.Empty;
        state.PendingReciprocalGiftDueTotalDays = -1;
        state.PendingReciprocalGiftSourceGiftName = string.Empty;
        state.PendingReciprocalGiftReason = string.Empty;
    }

    public static bool VisibleDialoguePromisesUnsupportedGift(string npcResponse)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return false;
        }

        string text = npcResponse.ToLowerInvariant();
        return ConversationActionCueRules.ContainsAny(
            text,
            "书签",
            "bookmark",
            "便签",
            "纸条",
            "手帕",
            "发夹",
            "小卡片"
        );
    }

    public static string DetermineGiftMotive(ValleyTalkWorldActionRequest action, GiftSelection selection, GiftTier tier, string fallback = "daily")
    {
        string reason = action.Reason ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selection.MatchedPlayerPreference))
        {
            return "preference";
        }

        if (ConversationActionCueRules.ContainsAny(reason, "recently gave", "return gift", "reciprocal", "回礼"))
        {
            return "reciprocal";
        }

        if (ConversationActionCueRules.ContainsAny(reason, "thank", "thanks", "谢礼", "感谢", "help request"))
        {
            return "thanks";
        }

        if (tier == GiftTier.Meaningful)
        {
            return "meaningful";
        }

        return fallback;
    }

    public static string BuildGiftHudMessage(NPC npc, string itemLabel, string motive)
    {
        return motive switch
        {
            "preference" => I18n.Get("gift.hud.preference", new { npc = npc.displayName, item = itemLabel }),
            "reciprocal" => I18n.Get("gift.hud.reciprocal", new { npc = npc.displayName, item = itemLabel }),
            "thanks" => I18n.Get("gift.hud.thanks", new { npc = npc.displayName, item = itemLabel }),
            "meaningful" => I18n.Get("gift.hud.meaningful", new { npc = npc.displayName, item = itemLabel }),
            _ => I18n.Get("gift.hud.default", new { npc = npc.displayName, item = itemLabel })
        };
    }

    public static string BuildGiftSelectionReason(string prefix, GiftSelection selection)
    {
        string rememberedPreference = string.IsNullOrWhiteSpace(selection.MatchedPlayerPreference)
            ? string.Empty
            : $"; remembered farmer preference: {selection.MatchedPlayerPreference}";
        return $"{prefix}; selection basis: {selection.Reason}{rememberedPreference}";
    }
}
