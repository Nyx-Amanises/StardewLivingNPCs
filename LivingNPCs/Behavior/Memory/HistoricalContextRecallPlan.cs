using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

/// <summary>
/// One read-only selection shared by the durable summary, continuity cues and their post-render
/// marks. Current obligations never compete with optional history for a similarity budget.
/// </summary>
internal sealed record HistoricalContextRecallPlan(
    IReadOnlyList<DialogueBehaviorInfluenceFact> BehaviorInfluences,
    IReadOnlyList<SharedExperienceFact> SharedExperiences,
    IReadOnlyList<NpcHelpRequestFact> HelpRequests,
    IReadOnlyList<NpcConflictFact> Conflicts,
    NpcHelpRequestFact? ActiveHelpRequest,
    NpcHelpRequestFact? RecentlyFulfilledHelpRequest,
    NpcHelpRequestFact? ExpiredHelpRequest,
    SharedExperienceFact? SharedExperienceFollowUp,
    NpcConflictFact? ActiveConflict,
    NpcConflictFact? RecentlyResolvedConflict)
{
    private const int OptionalItemsPerStore = 2;

    public static HistoricalContextRecallPlan Empty { get; } = new(
        Array.Empty<DialogueBehaviorInfluenceFact>(), Array.Empty<SharedExperienceFact>(),
        Array.Empty<NpcHelpRequestFact>(), Array.Empty<NpcConflictFact>(),
        null, null, null, null, null, null);

    public static HistoricalContextRecallPlan Build(
        LivingNpcState state,
        int currentTotalDays,
        string? currentPlayerText = null)
    {
        if (RsvAiPolicy.IsBlockedNpcName(state.NpcName))
        {
            return Empty;
        }

        var query = MemoryTopicQuery.Create(currentPlayerText);
        // Filter individual facts before both cue selection and ranking. Filtering a joined line
        // later would discard allowed neighbors too, after ineligible cues had already been marked.
        var allowedExperiences = state.SharedExperiences.Where(IsAllowed).ToArray();
        var allowedRequests = state.HelpRequests.Where(IsAllowed).ToArray();
        var allowedConflicts = state.Conflicts.Where(IsAllowed).ToArray();
        var behaviorInfluences = state.GetActiveDialogueBehaviorInfluences(currentTotalDays).Where(IsAllowed).ToArray();
        var activeHelpRequest = allowedRequests.FirstOrDefault(request => request.Status is "Offered" or "Pending");
        var fulfilledHelpRequest = allowedRequests.FirstOrDefault(request =>
            request.Status == "Fulfilled"
            && request.FulfilledTotalDays >= currentTotalDays - 3
            && request.LastMentionedTotalDays < 0);
        var expiredHelpRequest = allowedRequests.FirstOrDefault(request =>
            request.Status == "Expired" && request.LastMentionedTotalDays < currentTotalDays);
        // A repeated outing re-arms its follow-up using LastUpdatedTotalDays. -1 means none planned.
        var sharedExperience = allowedExperiences.FirstOrDefault(experience =>
            experience.FollowUpEligibleTotalDays >= 0
            && experience.FollowUpEligibleTotalDays <= currentTotalDays
            && experience.FollowUpShownTotalDays < 0
            && experience.LastUpdatedTotalDays >= currentTotalDays - 7);
        var activeConflict = allowedConflicts
            .Where(conflict => conflict.Status is "Active" or "Recovering")
            .OrderByDescending(conflict => conflict.Severity)
            .ThenByDescending(conflict => conflict.LastUpdatedTotalDays)
            .FirstOrDefault();
        var resolvedConflict = allowedConflicts.FirstOrDefault(conflict =>
            conflict.Status == "Resolved"
            && conflict.ResolvedTotalDays >= currentTotalDays - 3
            && conflict.RecoveryMentionedTotalDays < 0);

        var pinnedExperiences = new List<SharedExperienceFact>();
        AddIfMissing(pinnedExperiences, sharedExperience);
        var experiences = WithOptionalHistory(
            pinnedExperiences, state.GetTopSharedExperiences(int.MaxValue).Where(allowedExperiences.Contains),
            experience => query.Score(
                $"{experience.Type} {experience.LocationName} {experience.LocationLabel}", experience.Summary));

        var pinnedRequests = state.GetTopHelpRequests(int.MaxValue)
            .Where(allowedRequests.Contains)
            .Where(request => request.Status is "Offered" or "Pending" || HasUnclaimedReward(request))
            .ToList();
        AddIfMissing(pinnedRequests, fulfilledHelpRequest);
        AddIfMissing(pinnedRequests, expiredHelpRequest);
        var requests = WithOptionalHistory(
            pinnedRequests, state.GetTopHelpRequests(int.MaxValue).Where(allowedRequests.Contains),
            request => query.Score(
                $"{request.Type} {request.RequestedItemLabel} {request.RequestedItemId} {request.QuestionTopic}",
                BuildHelpRequestSearchText(request)));

        var pinnedConflicts = state.GetTopConflicts(int.MaxValue)
            .Where(allowedConflicts.Contains)
            .Where(conflict => conflict.Status is "Active" or "Recovering")
            .ToList();
        AddIfMissing(pinnedConflicts, resolvedConflict);
        var conflicts = WithOptionalHistory(
            pinnedConflicts, state.GetTopConflicts(int.MaxValue).Where(allowedConflicts.Contains),
            conflict => query.Score(conflict.CauseKind, conflict.Summary));

        return new HistoricalContextRecallPlan(
            behaviorInfluences, experiences, requests, conflicts, activeHelpRequest,
            fulfilledHelpRequest, expiredHelpRequest, sharedExperience, activeConflict, resolvedConflict);
    }

    private static IReadOnlyList<T> WithOptionalHistory<T>(
        IReadOnlyList<T> pinned,
        IEnumerable<T> orderedHistory,
        Func<T, int> score)
        where T : class
    {
        var pinnedSet = new HashSet<T>(pinned, ReferenceEqualityComparer.Instance);
        var candidates = orderedHistory.Where(item => !pinnedSet.Contains(item))
            .Select(item => (Item: item, Score: score(item))).ToArray();
        // Stable LINQ ordering preserves each store's salience/date/status tie-breakers. Empty or
        // unmatched input retains two familiar facts; a match need not drag unrelated history along.
        bool hasMatch = candidates.Any(candidate => candidate.Score > 0) || pinned.Any(item => score(item) > 0);
        IEnumerable<T> selected = hasMatch
            ? candidates.Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score).Select(candidate => candidate.Item)
            : candidates.Select(candidate => candidate.Item);
        return pinned.Concat(selected.Take(OptionalItemsPerStore)).ToArray();
    }

    private static void AddIfMissing<T>(List<T> items, T? item)
        where T : class
    {
        if (item != null && !items.Any(existing => ReferenceEquals(existing, item)))
        {
            items.Add(item);
        }
    }

    private static bool HasUnclaimedReward(NpcHelpRequestFact request) =>
        request.Status == "Fulfilled" && request.RewardMoney > 0
        && request.RewardMoneyClaimQueued && !request.RewardMoneyGranted;

    private static bool IsAllowed(SharedExperienceFact experience) =>
        !RsvAiPolicy.IsBlockedLocationName(experience.LocationName)
        && AllowsText(experience.Type, experience.Summary, experience.LocationName, experience.LocationLabel);

    private static bool IsAllowed(DialogueBehaviorInfluenceFact influence) =>
        !RsvAiPolicy.IsBlockedLocationName(influence.TargetLocation)
        && AllowsText(influence.Type, influence.Summary, influence.TargetLocation,
            influence.TargetLocationLabel, influence.Status);

    private static bool IsAllowed(NpcConflictFact conflict) =>
        AllowsText(conflict.CauseKind, conflict.Summary, conflict.Status, conflict.RepairStage);

    private static bool IsAllowed(NpcHelpRequestFact request) =>
        AllowsText(request.Type, request.Summary, request.RequestedItemId, request.RequestedItemLabel,
            request.QuestionTopic, request.Reason, request.Status, request.Resolution,
            request.FollowUpPotential, request.FailureReaction)
        && request.Steps.All(step => AllowsText(step.Type, step.Summary, step.RequestedItemId,
            step.RequestedItemLabel, step.QuestionTopic, step.Status, step.Resolution));

    private static bool AllowsText(params string?[] fields) =>
        !fields.Any(RsvAiPolicy.ContainsBlockedReference);

    private static string BuildHelpRequestSearchText(NpcHelpRequestFact request) =>
        string.Join(" ", new[] { request.Summary, request.Reason, request.Resolution }
            .Concat(request.Steps.Select(step =>
                $"{step.Summary} {step.RequestedItemLabel} {step.RequestedItemId} {step.QuestionTopic} {step.Resolution}")));
}
