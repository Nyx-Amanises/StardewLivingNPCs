using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

internal static class TestScenarios
{
    public const int Today = 100;

    public static LivingNpcState TrustedState(string npcName = "Emily")
    {
        return new LivingNpcState
        {
            NpcName = npcName,
            RelationshipTrust = 80,
            Familiarity = 45,
            InteractionComfortTier = "Trusted",
            CurrentEmotion = "Calm"
        };
    }

    public static WorldContextSnapshot World(
        string locationName = "Town",
        string locationDisplayName = "Town",
        int timeOfDay = 1200,
        int friendshipHearts = 6,
        string promptLabel = "")
    {
        return new WorldContextSnapshot(
            locationName,
            locationDisplayName,
            "spring",
            10,
            timeOfDay,
            friendshipHearts,
            Progression(),
            Knowledge(),
            Array.Empty<string>(),
            WorldStateInfluence.None,
            string.IsNullOrWhiteSpace(promptLabel)
                ? $"context at {locationDisplayName} around {timeOfDay}"
                : promptLabel,
            $"{locationDisplayName} {timeOfDay}",
            0,
            0,
            "test scene"
        );
    }

    public static LongTermMemoryFact Memory(
        string summary,
        int importance = 55,
        string kind = "fact",
        string subject = "",
        params string[] tags)
    {
        return new LongTermMemoryFact
        {
            Kind = kind,
            Subject = subject,
            Summary = summary,
            Importance = importance,
            Tags = tags.ToList(),
            CreatedTotalDays = Today - 1,
            CreatedTimeOfDay = 900,
            LastUpdatedTotalDays = Today - 1,
            LastUpdatedTimeOfDay = 900,
            TimesReinforced = 1
        };
    }

    public static NpcConflictFact SeriousConflict()
    {
        return new NpcConflictFact
        {
            CauseKind = "dialogue",
            Summary = "The farmer said something deeply hurtful.",
            Severity = 80,
            PeakSeverity = 80,
            Status = "Active",
            CreatedTotalDays = Today,
            CreatedTimeOfDay = 1000,
            LastUpdatedTotalDays = Today,
            LastUpdatedTimeOfDay = 1000,
            RequiresComplexRepair = true,
            RepairStage = "NeedsApology",
            MinimumRepairTotalDays = Today + BehaviorMemory.GetComplexRepairDelayDays(80),
            TimesReinforced = 1
        };
    }

    private static WorldProgressSnapshot Progression()
    {
        return new WorldProgressSnapshot(
            1,
            "undecided",
            "first_year_settling_in",
            false,
            false,
            false,
            false,
            false,
            Array.Empty<string>(),
            "small",
            0,
            0,
            0,
            Array.Empty<string>(),
            0,
            new ModWorldProgressSnapshot(
                SveWorldProgressSnapshot.NotInstalled,
                RsvWorldProgressSnapshot.NotInstalled,
                "no installed expansion progression",
                "无扩展进度",
                "no expansion guidance"
            ),
            "ordinary early-year town progression",
            "普通第一年进度",
            "keep requests modest"
        );
    }

    private static WorldProgressKnowledgeSnapshot Knowledge()
    {
        return new WorldProgressKnowledgeSnapshot(
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            new ModWorldProgressKnowledgeSnapshot(
                "no expansion knowledge",
                "无扩展认知",
                "no expansion guidance"
            ),
            Array.Empty<string>(),
            false,
            true,
            "knows ordinary early town context",
            "知道普通城镇背景",
            "no special guidance"
        );
    }
}
