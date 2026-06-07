using System.Text.Json;
using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class SaveDataSerializationTests
{
    [Fact]
    public void BehaviorMemorySaveDataRoundTripsWithoutLosingNestedState()
    {
        var saveData = new BehaviorMemorySaveData
        {
            LastStateDecayTotalDays = TestScenarios.Today,
            EntriesByNpc =
            {
                ["Emily"] =
                [
                    new BehaviorMemoryEntry
                    {
                        NpcName = "Emily",
                        Kind = "Conversation",
                        Action = "talked",
                        TotalDays = TestScenarios.Today,
                        TimeOfDay = 1200
                    }
                ]
            },
            StatesByNpc =
            {
                ["Emily"] = new LivingNpcState
                {
                    NpcName = "Emily",
                    RelationshipTrustInitialized = true,
                    RelationshipTrust = 72,
                    RecentAiGiftItemIds = ["(O)395"],
                    LongTermMemories =
                    [
                        TestScenarios.Memory(
                            "The farmer promised to visit the library.",
                            importance: 80,
                            kind: "promise")
                    ],
                    HelpRequests =
                    [
                        new NpcHelpRequestFact
                        {
                            NpcDisplayName = "Emily",
                            Type = "item_request",
                            Summary = "Bring quartz.",
                            RequestedItemId = "(O)80",
                            Status = "Pending"
                        }
                    ]
                }
            }
        };

        string json = JsonSerializer.Serialize(saveData);
        var restored = JsonSerializer.Deserialize<BehaviorMemorySaveData>(json);

        Assert.NotNull(restored);
        Assert.Equal(TestScenarios.Today, restored.LastStateDecayTotalDays);
        Assert.Equal("talked", Assert.Single(restored.EntriesByNpc["Emily"]).Action);

        var state = restored.StatesByNpc["Emily"];
        Assert.Equal(72, state.RelationshipTrust);
        Assert.Equal("(O)395", Assert.Single(state.RecentAiGiftItemIds));
        Assert.Equal("promise", Assert.Single(state.LongTermMemories).Kind);
        Assert.Equal("(O)80", Assert.Single(state.HelpRequests).RequestedItemId);
    }
}
