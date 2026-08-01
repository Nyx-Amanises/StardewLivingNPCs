using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Behavior.Multiplayer;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Assembles the hidden LivingNPCs context that the dialogue engine pulls at generation time
/// (via <see cref="BuildPromptContext"/>, wired into GenerationRequest.BehaviorContext by WP12):
/// the base continuity summary from memory, plus the situational sections (gift opportunity,
/// help-request opportunity, companion outing, immediate cues) and the gift-response prompts.
/// This class decides which sections apply; their wording lives in <see cref="PromptFragments"/>.
/// Immediate cues ("the player just handed over the requested item") are staged per NPC by
/// <see cref="PushInteractionContext"/> and consumed by the next build (day end clears leftovers).
/// </summary>
internal sealed class ValleyTalkContextService
{
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly GiftSelector giftSelector;
    private readonly BehaviorMailService mailService;
    private readonly Func<NPC, string> buildCompanionOutingContext;
    private readonly NpcRelationshipViewStore relationshipViews;
    private readonly Func<bool>? suppressOpportunitySections;

    /// <summary>Per-NPC one-shot immediate cues; consumed by the next BuildPromptContext.</summary>
    private readonly Dictionary<string, string> immediateContexts = new(StringComparer.OrdinalIgnoreCase);

    public ValleyTalkContextService(
        IMonitor monitor,
        ModConfig config,
        BehaviorMemory memory,
        GiftSelector giftSelector,
        BehaviorMailService mailService,
        Func<NPC, string> buildCompanionOutingContext,
        NpcRelationshipViewStore relationshipViews,
        Func<bool>? suppressOpportunitySections = null)
    {
        this.monitor = monitor;
        this.config = config;
        this.memory = memory;
        this.giftSelector = giftSelector;
        this.mailService = mailService;
        this.buildCompanionOutingContext = buildCompanionOutingContext;
        this.relationshipViews = relationshipViews;
        this.suppressOpportunitySections = suppressOpportunitySections;
    }

    /// <summary>
    /// 多人 v1（主机权威下的远程 farmhand）：机会段引导模型发起世界动作（送礼/求助/出游），
    /// 而世界动作只属于主机玩家自己的会话——farmhand 上报的动作会被主机剥除。抑制注入并
    /// 显式告知模型"不可送礼 / 出游不可安排"，避免 NPC 口头答应却无事发生。
    /// </summary>
    private bool SuppressOpportunitySections => this.suppressOpportunitySections?.Invoke() == true;

    public void PushInteractionContext(NPC npc, string debugMessage, string immediatePromptContext = "")
    {
        if (RsvAiPolicy.IsBlockedNpc(npc))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(immediatePromptContext))
        {
            this.immediateContexts[npc.Name] = immediatePromptContext;
        }

        if (this.config.Debug)
        {
            this.monitor.Log(debugMessage, LogLevel.Debug);
        }
    }

    /// <summary>Drops all staged immediate cues (day start / returned to title / manual memory clear).</summary>
    public void ClearImmediateContexts()
    {
        this.immediateContexts.Clear();
    }

    public string BuildPromptContext(NPC npc)
    {
        if (npc == null || RsvAiPolicy.IsBlockedNpc(npc))
        {
            return string.Empty;
        }

        string promptContext = ResolveBasePromptContext(
            this.SuppressOpportunitySections,
            npc.Name,
            this.relationshipViews,
            () => this.BuildRelationshipSummary(npc, markRecalled: true),
            out bool hasAuthoritativeContext);
        if (this.SuppressOpportunitySections && !hasAuthoritativeContext)
        {
            return string.Empty;
        }
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

        string companionOutingContext = this.SuppressOpportunitySections
            ? PromptFragments.Outing.UnavailableSection()
            : this.buildCompanionOutingContext(npc);
        if (!string.IsNullOrWhiteSpace(companionOutingContext))
        {
            promptContext = $"{promptContext}\n{companionOutingContext}";
        }

        // Consume-once (WP16 ruling 1): the staged immediate cue only colors the very next
        // generation; day end clears leftovers via ClearImmediateContexts.
        if (this.immediateContexts.Remove(npc.Name, out string? immediatePromptContext)
            && !string.IsNullOrWhiteSpace(immediatePromptContext))
        {
            promptContext = $"{promptContext}\n{immediatePromptContext}";
        }

        return RsvAiPolicy.RemoveBlockedLines(promptContext);
    }

    internal static string ResolveBasePromptContext(
        bool useHostRelationshipView,
        string npcName,
        NpcRelationshipViewStore relationshipViews,
        Func<string> buildLocalContext,
        out bool hasAuthoritativeContext)
    {
        if (!useHostRelationshipView)
        {
            hasAuthoritativeContext = true;
            return buildLocalContext();
        }

        hasAuthoritativeContext = relationshipViews.TryGetBehaviorContext(npcName, out string context);
        return hasAuthoritativeContext ? context : string.Empty;
    }

    /// <summary>
    /// Builds the host-owned continuity summary stored in a relationship view. Publishing a view is
    /// observational, so it must not consume recall counters that should advance only for dialogue.
    /// </summary>
    public string BuildRelationshipSummary(NPC npc)
    {
        return this.BuildRelationshipSummary(npc, markRecalled: false);
    }

    private string BuildRelationshipSummary(NPC npc, bool markRecalled)
    {
        return this.memory.BuildPromptContext(
            npc,
            this.config.PromptMemoryEntries,
            this.config.EnableNpcState,
            this.config.EnableHelpRequests ? this.config.MaxPendingHelpRequestsPerNpc : 0,
            this.config.HelpRequestCooldownDays,
            markRecalled
        );
    }

    private string BuildGiftOpportunityPromptContext(NPC npc)
    {
        if (this.SuppressOpportunitySections)
        {
            return PromptFragments.GiftOpportunity.NoOpportunitySection();
        }

        var state = this.memory.GetState(npc);
        if (state == null)
        {
            return string.Empty;
        }

        if (!this.config.EnableAiWorldActions
            || !this.config.AllowAiSmallGifts
            || state.HighestUnresolvedConflictSeverity >= 30
            || GiftActionRules.HasAiGiftToday(state))
        {
            return PromptFragments.GiftOpportunity.NoOpportunitySection();
        }

        string cue = string.Empty;
        if (state.DailyGiftOpportunityTotalDays == Game1.Date.TotalDays)
        {
            cue = string.IsNullOrWhiteSpace(state.DailyGiftOpportunityReason)
                ? PromptFragments.GiftOpportunity.DefaultDailyCue
                : state.DailyGiftOpportunityReason;
        }

        if (string.IsNullOrWhiteSpace(cue))
        {
            return PromptFragments.GiftOpportunity.NoOpportunitySection();
        }

        return PromptFragments.GiftOpportunity.Section(
            npc.displayName,
            cue,
            this.giftSelector.BuildCommonPromptList(GiftTier.Small),
            this.giftSelector.BuildPersonalizedPromptList(npc, GiftTier.Small));
    }

    private string BuildHelpRequestOpportunityPromptContext(NPC npc)
    {
        var state = this.memory.GetState(npc);
        if (state == null
            || this.SuppressOpportunitySections
            || !this.config.EnableHelpRequests
            || state.DailyHelpRequestOpportunityTotalDays != Game1.Date.TotalDays
            || state.HighestUnresolvedConflictSeverity >= 30
            || state.HelpRequests.Any(request => request.Status is "Offered" or "Pending"))
        {
            return string.Empty;
        }

        return PromptFragments.HelpRequestOpportunity.Section(npc.displayName);
    }

    /// <summary>
    /// The hidden context for an immediate gift reaction: hand-ins for pending help requests
    /// override normal taste guidance, and queued reciprocal/birthday thank-you mail lets the NPC
    /// hint at a later return gift.
    /// </summary>
    public string BuildGiftResponseContext(NPC npc, string giftItemId, string giftName, int taste)
    {
        if (this.SuppressOpportunitySections)
        {
            return string.Empty;
        }

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
            PromptFragments.HelpRequestHandIn.Header,
            PromptFragments.HelpRequestHandIn.HandInLine(npc.displayName, gift.ItemName, gift.ItemId),
            PromptFragments.HelpRequestHandIn.NotDailyGiftLine,
            PromptFragments.HelpRequestHandIn.OverrideTasteLine,
            PromptFragments.HelpRequestHandIn.ThankNaturallyLine
        };

        foreach (var request in deliveredHelpRequests)
        {
            lines.Add(PromptFragments.HelpRequestHandIn.StatusLine(request));
            if (request.Status == "Pending")
            {
                lines.Add(PromptFragments.HelpRequestHandIn.AdvancedStepLine(request));
            }
            else if (request.Status == "Fulfilled")
            {
                lines.Add(PromptFragments.HelpRequestHandIn.CompletedLine);
            }

            if (request.RewardGranted)
            {
                lines.Add(PromptFragments.HelpRequestHandIn.FriendshipRewardLine(request.RewardFriendship));
            }

            if (request.RewardMoneyGranted)
            {
                lines.Add(PromptFragments.HelpRequestHandIn.MoneyRewardGrantedLine(request.RewardMoney));
            }
            else if (request.RewardMoneyClaimQueued)
            {
                lines.Add(PromptFragments.HelpRequestHandIn.MoneyRewardQueuedLine(request.RewardMoney));
            }

            if (request.RewardGiftGiven)
            {
                lines.Add(PromptFragments.HelpRequestHandIn.ThankYouMailLine);
            }
        }

        return string.Join("\n", lines);
    }

    private static string BuildGiftResponseMailPrompt(NPC npc, GiftMemoryDetails gift)
    {
        return PromptFragments.GiftResponseMail.Section(npc.displayName, gift.ItemName, gift.IsBirthdayGift);
    }
}
