using System.Linq;

namespace LivingNPCs.Behavior;

internal sealed record HelpRequestReadinessResult(
    bool Allowed,
    string Reason
);

internal static class HelpRequestReadinessRules
{
    public static HelpRequestReadinessResult Evaluate(
        LivingNpcState state,
        int friendshipHearts,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests,
        int currentTotalDays)
    {
        if (maxPendingHelpRequestsPerNpc <= 0)
        {
            return new HelpRequestReadinessResult(false, "help requests are disabled");
        }

        if (state.HelpRequests.Count(request => request.Status is "Offered" or "Pending") >= maxPendingHelpRequestsPerNpc)
        {
            return new HelpRequestReadinessResult(false, "an active help request is already pending");
        }

        if (state.HighestUnresolvedConflictSeverity >= 30)
        {
            return new HelpRequestReadinessResult(false, "unresolved conflict makes asking for help feel wrong");
        }

        if (friendshipHearts < 2)
        {
            return new HelpRequestReadinessResult(false, "the relationship is not close enough yet");
        }

        if (state.CurrentEmotion is "Angry" or "Upset")
        {
            return new HelpRequestReadinessResult(false, "their current emotion is too strained");
        }

        if (state.LastHelpRequestTotalDays >= 0
            && currentTotalDays - state.LastHelpRequestTotalDays < helpRequestCooldownDays)
        {
            return new HelpRequestReadinessResult(false, "a recent help request is still too fresh");
        }

        return new HelpRequestReadinessResult(true, "one modest favor would be natural if the conversation genuinely leads there");
    }
}
