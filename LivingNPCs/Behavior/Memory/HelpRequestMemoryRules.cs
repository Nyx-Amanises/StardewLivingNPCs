using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class HelpRequestMemoryRules
{
    public static int DetermineFriendshipReward(
        NPC npc,
        ValleyTalkHelpRequestCandidate candidate,
        string normalizedType)
    {
        unchecked
        {
            string seed = $"{npc.Name}:{normalizedType}:{candidate.Summary}:{Game1.Date.TotalDays}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return 50 + System.Math.Abs(hash % 51);
        }
    }

    public static int DetermineMoneyReward(IReadOnlyList<NpcHelpRequestStepFact> steps)
    {
        int total = 0;
        foreach (var step in steps.Where(step => step.Type == "item_request"))
        {
            total += DetermineItemMoneyReward(step.RequestedItemId);
        }

        return System.Math.Clamp(total <= 0 ? 200 : total, 200, 10000);
    }

    public static bool ShouldPlanFollowUp(LivingNpcState state, NpcHelpRequestFact request)
    {
        int chance = request.FollowUpPotential switch
        {
            "deeper_relationship" => 75,
            _ => 45
        };

        if (request.RewardMoney >= 1000)
        {
            chance += 10;
        }

        unchecked
        {
            string seed = $"{state.NpcName}:{request.Summary}:{request.RequestedItemId}:{request.FulfilledTotalDays}:{request.FollowUpPotential}";
            int hash = 17;
            foreach (char character in seed)
            {
                hash = (hash * 31) + character;
            }

            return System.Math.Abs(hash % 100) < System.Math.Clamp(chance, 0, 90);
        }
    }

    public static int GetMaxStepsForCurrentWorldStage()
    {
        return WorldProgression.Current().ResidentStage switch
        {
            "first_spring_newcomer" => 1,
            "first_year_settling_in" => 2,
            _ => 3
        };
    }

    public static bool CanOpen(
        NPC npc,
        LivingNpcState state,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests,
        out string reason)
    {
        var world = WorldContext.For(npc);
        var result = EvaluateReadiness(
            state,
            world.FriendshipHearts,
            maxPendingHelpRequestsPerNpc,
            helpRequestCooldownDays,
            minRelationshipTrustForHelpRequests,
            Game1.Date.TotalDays
        );
        reason = result.Reason;
        return result.Allowed;
    }

    public static HelpRequestReadinessResult EvaluateReadiness(
        LivingNpcState state,
        int friendshipHearts,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int minRelationshipTrustForHelpRequests,
        int currentTotalDays)
    {
        return HelpRequestReadinessRules.Evaluate(
            state,
            friendshipHearts,
            maxPendingHelpRequestsPerNpc,
            helpRequestCooldownDays,
            minRelationshipTrustForHelpRequests,
            currentTotalDays
        );
    }

    private static int DetermineItemMoneyReward(string itemId)
    {
        string normalized = NormalizeQualifiedObjectId(itemId);
        if (normalized is "(O)16" or "(O)20")
        {
            return 200;
        }

        if (normalized == "(O)74")
        {
            return 10000;
        }

        int basePrice = TryGetObjectBasePrice(normalized);
        if (basePrice <= 80)
        {
            return 200;
        }

        int reward = basePrice * 5;
        reward = ((reward + 24) / 25) * 25;
        return System.Math.Clamp(reward, 200, 10000);
    }

    private static int TryGetObjectBasePrice(string itemId)
    {
        string objectId = NormalizeQualifiedObjectId(itemId);
        if (objectId.StartsWith("(O)", System.StringComparison.OrdinalIgnoreCase))
        {
            objectId = objectId.Substring(3);
        }

        if (Game1.objectData != null && Game1.objectData.TryGetValue(objectId, out var data))
        {
            return data.Price;
        }

        try
        {
            var item = ItemRegistry.Create<StardewValley.Object>(NormalizeQualifiedObjectId(itemId));
            return item.salePrice(false);
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeQualifiedObjectId(string itemId)
    {
        string value = itemId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("(O)", System.StringComparison.OrdinalIgnoreCase))
        {
            return $"(O){value.Substring(3)}";
        }

        return $"(O){value}";
    }
}
