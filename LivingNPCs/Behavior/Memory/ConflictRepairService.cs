using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior;

internal sealed record ConflictRepairUpdate(
    int ResolvedCount,
    int ComplexRepairGrowthAwards
);

internal static class ConflictRepairService
{
    public static ConflictRepairUpdate ApplyRepair(
        LivingNpcState state,
        int repairDelta,
        bool apology,
        bool specificRepairTalk,
        EmotionalExpressionCue emotionalStyle,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        int totalRepair = emotionalStyle.AdjustRepairAmount(Math.Clamp(repairDelta + (apology ? 12 : 0), 0, 100));
        if (totalRepair <= 0 && !apology && !specificRepairTalk)
        {
            return new ConflictRepairUpdate(0, 0);
        }

        int resolved = 0;
        int growthAwards = 0;
        foreach (var conflict in ActiveConflicts(state.Conflicts))
        {
            conflict.RepairScore = LivingNpcState.ClampScore(conflict.RepairScore + totalRepair);
            conflict.LastUpdatedTotalDays = currentTotalDays;
            conflict.LastUpdatedTimeOfDay = currentTimeOfDay;
            if (apology)
            {
                conflict.ApologyCount += 1;
                conflict.ApologyReceived = true;
            }

            if (specificRepairTalk)
            {
                conflict.SpecificRepairTalkReceived = true;
            }

            if (conflict.RequiresComplexRepair)
            {
                RefreshStage(conflict, currentTotalDays);
                int floor = GetComplexRepairSeverityFloor(conflict);
                conflict.Severity = LivingNpcState.ClampScore(Math.Max(floor, conflict.Severity - totalRepair));
                RefreshStage(conflict, currentTotalDays);
                if (CanResolveComplexConflict(conflict) && conflict.Severity == 0)
                {
                    if (ResolveConflict(conflict, currentTotalDays, currentTimeOfDay))
                    {
                        growthAwards++;
                    }

                    resolved++;
                }
                else
                {
                    conflict.Status = conflict.RepairStage is "NeedsApology" or "NeedsGesture"
                        ? "Active"
                        : "Recovering";
                }

                continue;
            }

            conflict.Severity = LivingNpcState.ClampScore(conflict.Severity - totalRepair);
            if (conflict.Severity == 0)
            {
                if (ResolveConflict(conflict, currentTotalDays, currentTimeOfDay))
                {
                    growthAwards++;
                }

                resolved++;
            }
            else
            {
                conflict.Status = GetConflictStatus(conflict.Severity);
            }
        }

        return new ConflictRepairUpdate(resolved, growthAwards);
    }

    public static void MarkRepairGiftReceived(
        LivingNpcState state,
        string giftName,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        foreach (var conflict in ActiveConflicts(state.Conflicts).Where(conflict => conflict.RequiresComplexRepair))
        {
            conflict.MeaningfulGiftReceived = true;
            conflict.LastRepairGiftName = giftName;
            conflict.LastUpdatedTotalDays = currentTotalDays;
            conflict.LastUpdatedTimeOfDay = currentTimeOfDay;
            RefreshStage(conflict, currentTotalDays);
        }
    }

    public static ConflictRepairUpdate DecayEmotionAndConflicts(
        LivingNpcState state,
        int emotionDailyDecay,
        int conflictDailyDecay,
        EmotionalExpressionCue emotionalStyle,
        int currentTotalDays,
        int currentTimeOfDay)
    {
        int adjustedEmotionDailyDecay = emotionalStyle.AdjustEmotionDecay(emotionDailyDecay);
        int adjustedConflictDailyDecay = emotionalStyle.AdjustConflictDecay(conflictDailyDecay);
        if (adjustedEmotionDailyDecay > 0 && state.EmotionIntensity > 0)
        {
            state.EmotionIntensity = LivingNpcState.MoveToward(state.EmotionIntensity, 0, adjustedEmotionDailyDecay);
            if (state.EmotionIntensity == 0)
            {
                state.CurrentEmotion = "Calm";
            }
        }

        if (adjustedConflictDailyDecay <= 0)
        {
            return new ConflictRepairUpdate(0, 0);
        }

        int resolved = 0;
        int growthAwards = 0;
        foreach (var conflict in ActiveConflicts(state.Conflicts))
        {
            if (conflict.RequiresComplexRepair)
            {
                RefreshStage(conflict, currentTotalDays);
                int floor = GetComplexRepairSeverityFloor(conflict);
                conflict.Severity = Math.Max(
                    floor,
                    LivingNpcState.MoveToward(conflict.Severity, 0, adjustedConflictDailyDecay)
                );
                conflict.LastUpdatedTotalDays = currentTotalDays;
                conflict.LastUpdatedTimeOfDay = currentTimeOfDay;
                RefreshStage(conflict, currentTotalDays);
                if (CanResolveComplexConflict(conflict) && conflict.Severity == 0)
                {
                    if (ResolveConflict(conflict, currentTotalDays, currentTimeOfDay))
                    {
                        growthAwards++;
                    }

                    resolved++;
                }
                else
                {
                    conflict.Status = conflict.RepairStage is "NeedsApology" or "NeedsGesture"
                        ? "Active"
                        : "Recovering";
                }

                continue;
            }

            conflict.Severity = LivingNpcState.MoveToward(conflict.Severity, 0, adjustedConflictDailyDecay);
            conflict.LastUpdatedTotalDays = currentTotalDays;
            conflict.LastUpdatedTimeOfDay = currentTimeOfDay;
            if (conflict.Severity == 0)
            {
                if (ResolveConflict(conflict, currentTotalDays, currentTimeOfDay))
                {
                    growthAwards++;
                }

                resolved++;
            }
            else
            {
                conflict.Status = GetConflictStatus(conflict.Severity);
            }
        }

        return new ConflictRepairUpdate(resolved, growthAwards);
    }

    public static void RefreshStage(NpcConflictFact conflict, int currentTotalDays)
    {
        if (!conflict.RequiresComplexRepair)
        {
            conflict.RepairStage = conflict.Status == "Resolved" ? "Resolved" : "Simple";
            return;
        }

        if (conflict.Status == "Resolved")
        {
            conflict.RepairStage = "Resolved";
            return;
        }

        if (!conflict.ApologyReceived)
        {
            conflict.RepairStage = "NeedsApology";
            return;
        }

        if (!conflict.MeaningfulGiftReceived)
        {
            conflict.RepairStage = "NeedsGesture";
            return;
        }

        if (currentTotalDays < conflict.MinimumRepairTotalDays)
        {
            conflict.RepairStage = "NeedsTime";
            return;
        }

        if (!conflict.SpecificRepairTalkReceived)
        {
            conflict.RepairStage = "NeedsConversation";
            return;
        }

        conflict.RepairStage = "ReadyToResolve";
    }

    public static string GetConflictStatus(int severity)
    {
        return severity switch
        {
            >= 30 => "Active",
            > 0 => "Recovering",
            _ => "Resolved"
        };
    }

    public static int GetComplexRepairDelayDays(int severity)
    {
        return severity >= 80 ? 5 : 3;
    }

    public static int GetComplexRepairDelayDays(LivingNpcState state, int severity)
    {
        var emotionalStyle = EmotionalExpressionStyle.For(state.NpcName, NpcDisposition.ForName(state.NpcName));
        return emotionalStyle.AdjustComplexRepairDelay(GetComplexRepairDelayDays(severity));
    }

    public static int GetConflictTrustLoss(int severity)
    {
        return severity switch
        {
            >= 70 => 18,
            >= 50 => 12,
            >= 30 => 8,
            _ => 4
        };
    }

    private static IEnumerable<NpcConflictFact> ActiveConflicts(IEnumerable<NpcConflictFact> conflicts)
    {
        return conflicts.Where(conflict => conflict.Status is "Active" or "Recovering");
    }

    private static bool ResolveConflict(NpcConflictFact conflict, int currentTotalDays, int currentTimeOfDay)
    {
        bool shouldGrantGrowth = conflict.RequiresComplexRepair && !conflict.RepairGrowthGranted;
        conflict.Status = "Resolved";
        conflict.ResolvedTotalDays = currentTotalDays;
        conflict.ResolvedTimeOfDay = currentTimeOfDay;
        conflict.RepairStage = "Resolved";
        if (shouldGrantGrowth)
        {
            conflict.RepairGrowthGranted = true;
        }

        return shouldGrantGrowth;
    }

    private static bool CanResolveComplexConflict(NpcConflictFact conflict)
    {
        return !conflict.RequiresComplexRepair
            || conflict.RepairStage == "ReadyToResolve";
    }

    private static int GetComplexRepairSeverityFloor(NpcConflictFact conflict)
    {
        if (!conflict.RequiresComplexRepair)
        {
            return 0;
        }

        return conflict.RepairStage switch
        {
            "NeedsApology" => 45,
            "NeedsGesture" => 30,
            "NeedsTime" => 20,
            "NeedsConversation" => 10,
            _ => 0
        };
    }
}
