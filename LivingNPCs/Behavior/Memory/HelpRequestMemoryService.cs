using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class HelpRequestMemoryService
{
    private static readonly HashSet<string> AllowedHelpRequestItemIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "(O)16",
        "(O)18",
        "(O)20",
        "(O)22",
        "(O)66",
        "(O)80",
        "(O)216",
        "(O)223",
        "(O)395",
        "(O)396",
        "(O)398",
        "(O)402",
        "(O)404",
        "(O)406",
        "(O)408",
        "(O)410",
        "(O)412",
        "(O)414",
        "(O)416",
        "(O)418"
    };

    private readonly Action<LivingNpcState, int, int> addFamiliarity;
    private readonly Action<LivingNpcState, int> applyRelationshipTrustDelta;
    private readonly Action<LivingNpcState, string, int, string> applyEmotion;
    private readonly Func<string, string, string, string, BehaviorMemoryEntry> createEntry;
    private readonly Action<BehaviorMemoryEntry, int> addEntry;

    public HelpRequestMemoryService(
        Action<LivingNpcState, int, int> addFamiliarity,
        Action<LivingNpcState, int> applyRelationshipTrustDelta,
        Action<LivingNpcState, string, int, string> applyEmotion,
        Func<string, string, string, string, BehaviorMemoryEntry> createEntry,
        Action<BehaviorMemoryEntry, int> addEntry)
    {
        this.addFamiliarity = addFamiliarity;
        this.applyRelationshipTrustDelta = applyRelationshipTrustDelta;
        this.applyEmotion = applyEmotion;
        this.createEntry = createEntry;
        this.addEntry = addEntry;
    }

    public IReadOnlyList<NpcHelpRequestFact> TryCompleteItemHelpRequests(
        NPC npc,
        LivingNpcState state,
        GiftMemoryDetails gift,
        int maxEntriesPerNpc)
    {
        // One handed-over item advances exactly one request (the most urgent match): the delivery
        // consumes a single item, so completing several matching requests with it would double
        // rewards. Repeated hand-ins serve any remaining requests one at a time.
        var fulfilled = state.HelpRequests
            .Where(request => request.Status == "Pending"
                && request.Type == "item_request"
                && string.Equals(request.RequestedItemId, gift.ItemId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(request => request.DueTotalDays)
            .Take(1)
            .ToList();

        foreach (var request in fulfilled)
        {
            this.CompleteCurrentStep(
                state,
                request,
                $"The farmer brought {gift.ItemName}.",
                out bool fullyFulfilled
            );

            var entry = this.createEntry(
                npc.Name,
                "HelpRequest",
                fullyFulfilled ? "Fulfilled" : "Advanced",
                request.Resolution
            );
            this.addEntry(entry, maxEntriesPerNpc);
        }

        return fulfilled;
    }

    public LivingNpcState UpdateExpiredRequest(LivingNpcState state, NpcHelpRequestFact request)
    {
        bool acceptedThenMissed = request.AcceptedTotalDays >= 0;
        request.FailureReaction = BuildFailureReaction(state, request, acceptedThenMissed);
        state.Mood = acceptedThenMissed ? "Disappointed" : "Polite";
        state.CurrentInclination = acceptedThenMissed ? "Reserved" : "Measured";
        state.Openness = LivingNpcState.ClampScore(state.Openness - (acceptedThenMissed ? 8 : 3));
        if (acceptedThenMissed)
        {
            this.applyRelationshipTrustDelta(state, -5);
        }

        this.applyEmotion(
            state,
            "Disappointed",
            acceptedThenMissed ? 14 : 6,
            $"{request.FailureReaction}: {request.Summary}"
        );
        state.LastInteraction = acceptedThenMissed
            ? $"the farmer accepted but did not finish a personal help request: {request.Summary}"
            : $"a personal help request went unanswered: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
        return state;
    }

    public bool Store(
        NPC npc,
        LivingNpcState state,
        ValleyTalkHelpRequestCandidate candidate,
        string playerText,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays)
    {
        string normalizedSummary = NormalizeMemorySummary(candidate.Summary);
        if (string.IsNullOrWhiteSpace(normalizedSummary))
        {
            return false;
        }

        var steps = BuildSteps(npc, candidate);
        if (steps.Count == 0)
        {
            return false;
        }

        var firstStep = steps[0];
        string normalizedType = firstStep.Type;
        var existing = state.HelpRequests.FirstOrDefault(request =>
            request.Status is "Offered" or "Pending"
            && IsSameOpenRequest(request, normalizedSummary, firstStep.RequestedItemId));
        if (existing != null)
        {
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            return true;
        }

        if (!HelpRequestMemoryRules.CanOpen(
                npc,
                state,
                maxPendingHelpRequestsPerNpc,
                helpRequestCooldownDays,
                out _))
        {
            return false;
        }

        // A request may only start out accepted (Pending) when the farmer's own words in this
        // exchange deterministically agreed or offered to help. The model's requiresAcceptance
        // flag is not trusted on its own: a hallucinated "no confirmation needed" would otherwise
        // put an unagreed task in the journal, and letting it expire punishes the farmer as a
        // broken promise.
        bool farmerAgreedHere = IsFarmerExplicitlyOfferingHelp(playerText)
            || LooksLikeFarmerAcceptingHelp(playerText);
        bool requiresAcceptance = !farmerAgreedHere;

        state.HelpRequests.Add(new NpcHelpRequestFact
        {
            NpcDisplayName = npc.displayName,
            QuestLogId = Guid.NewGuid().ToString("N"),
            Type = normalizedType,
            Summary = candidate.Summary.Trim(),
            Steps = steps,
            CurrentStepIndex = 0,
            RequestedItemId = firstStep.RequestedItemId,
            RequestedItemLabel = firstStep.RequestedItemLabel,
            QuestionTopic = firstStep.QuestionTopic,
            DueTotalDays = Game1.Date.TotalDays + candidate.DueInDays,
            Reason = candidate.Reason.Trim(),
            Status = requiresAcceptance ? "Offered" : "Pending",
            FollowUpPotential = BehaviorValueNormalizer.NormalizeHelpRequestFollowUpPotential(candidate.FollowUpPotential),
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            AcceptedTotalDays = requiresAcceptance ? -1 : Game1.Date.TotalDays,
            AcceptedTimeOfDay = requiresAcceptance ? 0 : Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            RewardFriendship = HelpRequestMemoryRules.DetermineFriendshipReward(
                npc,
                candidate,
                normalizedType
            ),
            RewardMoney = HelpRequestMemoryRules.DetermineMoneyReward(steps),
            TimesReinforced = 1
        });
        state.LastHelpRequestTotalDays = Game1.Date.TotalDays;
        state.LastHelpRequestTimeOfDay = Game1.timeOfDay;
        state.HelpRequests = state.HelpRequests
            .OrderBy(BehaviorMemory.HelpRequestRetentionRank)
            .ThenBy(request => request.DueTotalDays)
            .ThenByDescending(request => request.LastUpdatedTotalDays)
            .Take(12)
            .ToList();
        return true;
    }

    public bool ApplyUpdate(
        LivingNpcState state,
        ValleyTalkHelpRequestUpdateCandidate candidate,
        string playerText,
        out NpcHelpRequestFact? fulfilledRequest)
    {
        fulfilledRequest = null;
        string normalizedSummary = NormalizeMemorySummary(candidate.Summary);
        var openRequests = state.HelpRequests
            .Where(request => request.Status is "Offered" or "Pending")
            .OrderBy(request => request.DueTotalDays)
            .ToList();
        var existing = openRequests
            .FirstOrDefault(request => NormalizeMemorySummary(request.Summary) == normalizedSummary);
        if (existing == null && openRequests.Count == 1)
        {
            // The AI's update summary often does not line up word-for-word with the original ask;
            // when there is exactly one open request, treat the update as referring to it.
            existing = openRequests[0];
        }

        if (existing == null)
        {
            return false;
        }

        switch (candidate.Status)
        {
            case "accepted":
                // Offered→Pending needs the farmer's own affirmative words; a hallucinated
                // "accepted" must not put an unagreed task in the journal (and expiring an
                // "accepted" request later punishes the farmer as a broken promise).
                // Already-Pending requests pass through: Accept is a no-op for them.
                if (existing.Status == "Offered"
                    && !LooksLikeFarmerAcceptingHelp(playerText)
                    && !IsFarmerExplicitlyOfferingHelp(playerText))
                {
                    return false;
                }

                this.Accept(state, existing, candidate.Resolution);
                break;

            case "fulfilled":
            case "advanced":
                // Item requests are completed only when the farmer actually hands the item over
                // (TryCompleteItemHelpRequests consumes it on delivery); an AI "fulfilled" claim in
                // the dialogue must never complete the request or grant rewards on its own — that was
                // exploitable, since the model could "deliver" an item in text alone. At most, treat
                // it as acceptance when the request is still only offered and the farmer agrees here.
                if (existing.Status == "Offered" && LooksLikeFarmerAcceptingHelp(playerText))
                {
                    this.Accept(state, existing, string.IsNullOrWhiteSpace(candidate.Resolution)
                        ? "The farmer agreed to help."
                        : candidate.Resolution);
                    return true;
                }

                return false;

            case "declined":
                // Only decline (with its trust/mood penalty) when the farmer's words actually
                // refused; otherwise leave the request open — expiring unaccepted is the mild path.
                if (!LooksLikeFarmerDecliningHelp(playerText))
                {
                    return false;
                }

                this.Decline(state, existing, candidate.Resolution);
                break;
        }

        return true;
    }

    private static bool IsSameOpenRequest(NpcHelpRequestFact request, string normalizedSummary, string requestedItemId)
    {
        if (NormalizeMemorySummary(request.Summary) == normalizedSummary)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(requestedItemId)
            && string.Equals(request.RequestedItemId, requestedItemId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deterministic safety net: when the farmer's reply clearly agrees to help and an offered
    /// request is still waiting, accept it without relying on the AI to emit an accepted update.
    /// </summary>
    public bool TryAcceptOfferedFromPlayerAffirmation(LivingNpcState state, string playerText)
    {
        if (!LooksLikeFarmerAcceptingHelp(playerText))
        {
            return false;
        }

        var request = state.HelpRequests
            .Where(candidate => candidate.Status == "Offered")
            .OrderBy(candidate => candidate.DueTotalDays)
            .FirstOrDefault();
        if (request == null)
        {
            return false;
        }

        this.Accept(state, request, "The farmer agreed to help.");
        return true;
    }

    /// <summary>
    /// Creation safety net: when the AI omits the structured helpRequests field but the visible
    /// dialogue clearly has the NPC ask the farmer for one of its currently-requestable items,
    /// synthesize that request and store it through the normal gate (Offered, or Pending if the
    /// farmer already agreed in this reply).
    /// </summary>
    public bool TrySynthesizeItemRequestFromDialogue(
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return false;
        }

        string combined = $"{playerText} {npcResponse}";
        // The favor phrasing must come from the NPC's own reply: judging the combined text let a
        // farmer's "能不能给我带点面包" synthesize a reversed request where the NPC asks the farmer.
        if (LooksLikeNpcOfferingItemToFarmer(npcResponse) || !LooksLikeItemFavorRequested(npcResponse))
        {
            return false;
        }

        foreach (var item in HelpRequestAdvisor.GetRequestableItems(npc))
        {
            string localizedName = ResolveItemDisplayName(item.ItemId, item.Label);
            if (!ContainsAny(combined, localizedName, item.Label))
            {
                continue;
            }

            bool farmerOnBoard = LooksLikeFarmerAcceptingHelp(playerText)
                || IsFarmerExplicitlyOfferingHelp(playerText);
            var candidate = new ValleyTalkHelpRequestCandidate
            {
                Type = "item_request",
                Summary = localizedName,
                RequestedItemId = item.ItemId,
                RequestedItemLabel = localizedName,
                DueInDays = 3,
                RequiresAcceptance = !farmerOnBoard,
                FollowUpPotential = "none"
            };

            // Store applies the readiness gate; this is only a synthesis of the candidate the AI
            // failed to emit, never a bypass of vanilla hearts/cooldown rules.
            return this.Store(
                npc,
                state,
                candidate,
                playerText,
                maxPendingHelpRequestsPerNpc,
                helpRequestCooldownDays);
        }

        return false;
    }

    internal static bool LooksLikeItemFavorRequested(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return ContainsAny(
            text,
            "帮我找",
            "帮我带",
            "帮我弄",
            "帮我留意",
            "帮我捎",
            "能帮我",
            "能不能帮",
            "可以帮我",
            "帮忙找",
            "帮忙带",
            "给我带",
            "给我找",
            "带给我",
            "找点",
            "找些",
            "找一些",
            "弄点",
            "需要一些",
            "需要点",
            "缺一些",
            "缺点",
            "能不能给我",
            "可以给我带",
            "can you bring",
            "can you find",
            "could you bring",
            "could you find",
            "bring me",
            "find me",
            "get me some",
            "i need some",
            "looking for some",
            "could use some");
    }

    internal static bool LooksLikeNpcOfferingItemToFarmer(string npcResponse)
    {
        if (string.IsNullOrWhiteSpace(npcResponse))
        {
            return false;
        }

        return ContainsAny(
            npcResponse,
            "我早上刚烤",
            "我刚烤",
            "我这就去拿",
            "我这里正好有",
            "给你",
            "带上吧",
            "带着吧",
            "你要不要顺便带点",
            "你要不要带点",
            "边走边吃",
            "还热着",
            "还热乎",
            "for you",
            "take this",
            "have some");
    }

    private static string ResolveItemDisplayName(string itemId, string fallback)
    {
        try
        {
            var obj = ItemRegistry.Create<StardewValley.Object>(itemId);
            return string.IsNullOrWhiteSpace(obj?.DisplayName) ? fallback : obj.DisplayName;
        }
        catch
        {
            return fallback;
        }
    }

    private static List<NpcHelpRequestStepFact> BuildSteps(NPC npc, ValleyTalkHelpRequestCandidate candidate)
    {
        candidate.Steps ??= new List<ValleyTalkHelpRequestStepCandidate>();
        var rawSteps = candidate.Steps.Count > 0
            ? candidate.Steps
            : new List<ValleyTalkHelpRequestStepCandidate>
            {
                new()
                {
                    Type = candidate.Type,
                    Summary = candidate.Summary,
                    RequestedItemId = candidate.RequestedItemId,
                    RequestedItemLabel = candidate.RequestedItemLabel,
                    QuestionTopic = candidate.QuestionTopic
                }
            };

        var steps = new List<NpcHelpRequestStepFact>();
        foreach (var rawStep in rawSteps.Take(HelpRequestMemoryRules.GetMaxStepsForCurrentWorldStage()))
        {
            string type = BehaviorValueNormalizer.NormalizeHelpRequestType(rawStep.Type);
            if (type == "item_request")
            {
                if (!AllowedHelpRequestItemIds.Contains(rawStep.RequestedItemId)
                    || !HelpRequestAdvisor.IsCurrentlyRequestableItem(rawStep.RequestedItemId, npc))
                {
                    continue;
                }
            }
            else
            {
                continue;
            }

            steps.Add(new NpcHelpRequestStepFact
            {
                Type = type,
                Summary = string.IsNullOrWhiteSpace(rawStep.Summary)
                    ? candidate.Summary.Trim()
                    : rawStep.Summary.Trim(),
                RequestedItemId = rawStep.RequestedItemId.Trim(),
                RequestedItemLabel = rawStep.RequestedItemLabel.Trim(),
                QuestionTopic = rawStep.QuestionTopic.Trim(),
                Status = "Pending"
            });
        }

        return steps;
    }

    private static bool IsFarmerExplicitlyOfferingHelp(string playerText)
    {
        if (string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        string text = playerText.ToLowerInvariant();
        if (ContainsAny(text, "不需要帮", "不用帮", "不必帮", "don't need help", "do not need help"))
        {
            return false;
        }

        return ContainsAny(
            text,
            "需要帮忙",
            "需要帮",
            "有什么要帮",
            "有什么需要",
            "我能帮",
            "需要我",
            "帮得上",
            "可以帮",
            "要不要我帮",
            "anything i can help",
            "need help",
            "can i help",
            "help you",
            "what can i do"
        );
    }

    /// <summary>Deterministic refusal check, shared as the negative gate of acceptance.</summary>
    internal static bool LooksLikeFarmerDecliningHelp(string playerText)
    {
        if (string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        return ContainsAny(
            playerText.ToLowerInvariant(),
            "不用了",
            "不用帮",
            "不帮",
            "不行",
            "不可以",
            "算了",
            "下次再",
            "下次吧",
            "改天",
            "以后再",
            "以后吧",
            "没办法",
            "做不到",
            "不想",
            "no thanks",
            "not now",
            "maybe later",
            "can't help",
            "won't help",
            "i can't",
            "i won't");
    }

    internal static bool LooksLikeFarmerAcceptingHelp(string playerText)
    {
        if (string.IsNullOrWhiteSpace(playerText))
        {
            return false;
        }

        if (LooksLikeFarmerDecliningHelp(playerText))
        {
            return false;
        }

        string text = playerText.ToLowerInvariant();
        return ContainsAny(
            text,
            "好的",
            "好啊",
            "好呀",
            "好吧",
            "可以",
            "当然",
            "没问题",
            "我帮你",
            "帮你找",
            "帮你留意",
            "帮你弄",
            "我来帮",
            "我去找",
            "我找找",
            "我找给你",
            "留意",
            "我看看",
            "带给你",
            "给你带",
            "交给我",
            "包在我身上",
            "愿意",
            "成交",
            "sure",
            "okay",
            "of course",
            "i'll help",
            "i can help",
            "i'll get",
            "i'll find",
            "i'll keep an eye",
            "no problem",
            "you got it",
            "will do",
            "happy to");
    }

    internal static bool LooksLikeFarmerDeliveredHelpRequestItem(
        NpcHelpRequestFact request,
        string playerText,
        string resolution)
    {
        if (request.Type != "item_request")
        {
            return false;
        }

        string text = $"{playerText} {resolution}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (ContainsAny(
                text,
                "之后",
                "以后",
                "回头",
                "改天",
                "待会",
                "待会儿",
                "等会",
                "等会儿",
                "到了",
                "到农场",
                "家里有",
                "在家里",
                "will bring",
                "bring it later",
                "later",
                "at home"))
        {
            return false;
        }

        return ContainsAny(
            text,
            "给你",
            "拿去",
            "收下",
            "带来了",
            "我带来",
            "我拿来",
            "交给你",
            "递给",
            "就在这里",
            "这就是",
            "这是你要",
            "here you go",
            "here it is",
            "take it",
            "brought it",
            "i brought",
            "handed");
    }

    private void Accept(LivingNpcState state, NpcHelpRequestFact request, string resolution)
    {
        if (request.Status == "Pending")
        {
            return;
        }

        request.Status = "Pending";
        request.AcceptedTotalDays = Game1.Date.TotalDays;
        request.AcceptedTimeOfDay = Game1.timeOfDay;
        request.Resolution = string.IsNullOrWhiteSpace(resolution)
            ? "The farmer accepted the request."
            : resolution.Trim();
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
        state.Openness = LivingNpcState.ClampScore(state.Openness + 2);
        this.applyRelationshipTrustDelta(state, 2);
        state.LastInteraction = $"the farmer accepted a personal help request: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private void Decline(LivingNpcState state, NpcHelpRequestFact request, string resolution)
    {
        request.Status = "Declined";
        request.Resolution = string.IsNullOrWhiteSpace(resolution)
            ? "The farmer declined the request."
            : resolution.Trim();
        request.DeclinedTotalDays = Game1.Date.TotalDays;
        request.DeclinedTimeOfDay = Game1.timeOfDay;
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
        request.FailureReaction = BuildFailureReaction(state, request, acceptedThenMissed: false);
        this.ApplyDeclineEffects(state, request);
    }

    private bool CompleteCurrentStep(
        LivingNpcState state,
        NpcHelpRequestFact request,
        string resolution,
        out bool fullyFulfilled)
    {
        fullyFulfilled = false;
        if (request.Status != "Pending")
        {
            return false;
        }

        var step = GetCurrentStep(request);
        if (step != null)
        {
            step.Status = "Fulfilled";
            step.Resolution = string.IsNullOrWhiteSpace(resolution)
                ? "The farmer completed this part of the request."
                : resolution.Trim();
            step.CompletedTotalDays = Game1.Date.TotalDays;
            step.CompletedTimeOfDay = Game1.timeOfDay;
        }

        if (request.CurrentStepIndex + 1 < request.Steps.Count)
        {
            request.CurrentStepIndex += 1;
            ApplyCurrentStep(request);
            request.Resolution = string.IsNullOrWhiteSpace(resolution)
                ? $"The farmer completed step {request.CurrentStepIndex}."
                : resolution.Trim();
            request.LastUpdatedTotalDays = Game1.Date.TotalDays;
            request.LastUpdatedTimeOfDay = Game1.timeOfDay;
            request.TimesReinforced += 1;
            state.Mood = "Encouraged";
            state.CurrentInclination = "OpenToTalk";
            state.Attention = LivingNpcState.ClampScore(state.Attention + 4);
            state.Openness = LivingNpcState.ClampScore(state.Openness + 3);
            this.applyRelationshipTrustDelta(state, 2);
            state.LastInteraction = $"the farmer completed part of a personal help request: {request.Summary}";
            state.LastUpdatedTotalDays = Game1.Date.TotalDays;
            state.LastUpdatedTimeOfDay = Game1.timeOfDay;
            return true;
        }

        request.Status = "Fulfilled";
        request.Resolution = string.IsNullOrWhiteSpace(resolution)
            ? "The farmer completed the request."
            : resolution.Trim();
        request.FulfilledTotalDays = Game1.Date.TotalDays;
        request.FulfilledTimeOfDay = Game1.timeOfDay;
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
        this.ApplyFulfillmentEffects(state, request);
        fullyFulfilled = true;
        return true;
    }

    private static NpcHelpRequestStepFact? GetCurrentStep(NpcHelpRequestFact request)
    {
        if (request.Steps.Count == 0)
        {
            return null;
        }

        return request.Steps[Math.Clamp(request.CurrentStepIndex, 0, request.Steps.Count - 1)];
    }

    private static void ApplyCurrentStep(NpcHelpRequestFact request)
    {
        var step = GetCurrentStep(request);
        if (step == null)
        {
            return;
        }

        request.Type = step.Type;
        request.RequestedItemId = step.RequestedItemId;
        request.RequestedItemLabel = step.RequestedItemLabel;
        request.QuestionTopic = step.QuestionTopic;
    }

    private void ApplyFulfillmentEffects(LivingNpcState state, NpcHelpRequestFact request)
    {
        state.Mood = "Pleased";
        state.CurrentInclination = "OpenToTalk";
        state.Attention = LivingNpcState.ClampScore(state.Attention + 8);
        state.Openness = LivingNpcState.ClampScore(state.Openness + 6);
        this.addFamiliarity(state, 2, 8);
        this.applyRelationshipTrustDelta(state, 6);
        this.applyEmotion(
            state,
            "Grateful",
            16,
            $"the farmer helped with a personal request: {request.Summary}"
        );
        request.SpecialFollowUpPlanned = HelpRequestMemoryRules.ShouldPlanFollowUp(state, request);
        request.FollowUpEligibleTotalDays = request.SpecialFollowUpPlanned
            ? Game1.Date.TotalDays + 1
            : -1;
        StoreSharedExperience(state, request);
        state.LastInteraction = $"the farmer helped with a personal request: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private static void StoreSharedExperience(LivingNpcState state, NpcHelpRequestFact request)
    {
        string summary = $"the farmer helped with a personal request: {request.Summary}";
        if (request.FollowUpPotential != "none")
        {
            summary += $"; this could naturally grow into {request.FollowUpPotential.Replace('_', ' ')} if the next conversation supports it";
        }

        string key = $"help_request:{NormalizeMemorySummary(summary)}";
        var existing = state.SharedExperiences.FirstOrDefault(experience => experience.Key == key);
        if (existing != null)
        {
            existing.LastUpdatedTotalDays = Game1.Date.TotalDays;
            existing.LastUpdatedTimeOfDay = Game1.timeOfDay;
            existing.TimesReinforced += 1;
            existing.Importance = Math.Min(100, existing.Importance + 5);
            return;
        }

        state.SharedExperiences.Add(new SharedExperienceFact
        {
            Key = key,
            Type = "help_request",
            Summary = summary,
            LocationName = string.Empty,
            LocationLabel = "a personal favor",
            CreatedTotalDays = Game1.Date.TotalDays,
            CreatedTimeOfDay = Game1.timeOfDay,
            LastUpdatedTotalDays = Game1.Date.TotalDays,
            LastUpdatedTimeOfDay = Game1.timeOfDay,
            Importance = request.FollowUpPotential == "none" ? 70 : 82,
            TimesReinforced = 1,
            FollowUpEligibleTotalDays = Game1.Date.TotalDays + 2
        });

        state.SharedExperiences = state.SharedExperiences
            .OrderByDescending(experience => experience.Importance)
            .ThenByDescending(experience => experience.LastUpdatedTotalDays)
            .ThenByDescending(experience => experience.LastUpdatedTimeOfDay)
            .Take(12)
            .ToList();
    }

    private void ApplyDeclineEffects(LivingNpcState state, NpcHelpRequestFact request)
    {
        bool wasAccepted = request.AcceptedTotalDays >= 0;
        state.Mood = wasAccepted ? "Disappointed" : "Polite";
        state.CurrentInclination = wasAccepted ? "Reserved" : "Measured";
        state.Openness = LivingNpcState.ClampScore(state.Openness - (wasAccepted ? 6 : 2));
        this.applyRelationshipTrustDelta(state, wasAccepted ? -4 : -1);
        this.applyEmotion(
            state,
            wasAccepted ? "Disappointed" : "Calm",
            wasAccepted ? 10 : 2,
            $"the farmer declined a personal help request: {request.Summary}"
        );
        state.LastInteraction = $"the farmer declined a personal help request: {request.Summary}";
        state.LastUpdatedTotalDays = Game1.Date.TotalDays;
        state.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private static string BuildFailureReaction(LivingNpcState state, NpcHelpRequestFact request, bool acceptedThenMissed)
    {
        string name = state.NpcName.ToLowerInvariant();
        string baseline = acceptedThenMissed
            ? "they feel let down because the farmer had accepted the request"
            : "they let the unanswered request fade with mild disappointment";

        if (ContainsAny(name, "shane", "george", "haley", "zayne", "maive", "morris"))
        {
            return acceptedThenMissed
                ? "they respond bluntly and become more guarded"
                : "they brush it off, but a little sharply";
        }

        if (ContainsAny(name, "penny", "sophia", "flor", "shiro", "harvey", "sebastian", "claire"))
        {
            return acceptedThenMissed
                ? "they take it quietly and become more hesitant to ask again"
                : "they assume the farmer was busy and withdraw the request softly";
        }

        if (ContainsAny(name, "gus", "emily", "evelyn", "marnie", "robin", "lenny", "keahi", "pika"))
        {
            return acceptedThenMissed
                ? "they stay kind, but the missed help clearly matters"
                : "they remain warm and do not press the matter";
        }

        if (request.Type == "question_request")
        {
            return acceptedThenMissed
                ? "they are disappointed that the conversation never followed through"
                : "they move on from the unanswered question";
        }

        return baseline;
    }

    private static string NormalizeMemorySummary(string summary)
    {
        return BehaviorValueNormalizer.NormalizeMemorySummary(summary);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
