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
                            Status = "Fulfilled",
                            RewardMoney = 240,
                            RewardMoneyClaimQueued = true,
                            RewardMoneyQuestPosted = true
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
        var helpRequest = Assert.Single(state.HelpRequests);
        Assert.Equal("(O)80", helpRequest.RequestedItemId);
        Assert.Equal(240, helpRequest.RewardMoney);
        Assert.True(helpRequest.RewardMoneyClaimQueued);
        Assert.True(helpRequest.RewardMoneyQuestPosted);
    }

    [Fact]
    public void CloneCopiesEveryGiftMailField()
    {
        // Save data is produced via LivingNpcState.Clone(), so any NpcGiftMailFact property the
        // clone forgets to copy is silently reset on every save/load (this happened to the
        // AI-generated letter body). Set every writable property to a non-default value and make
        // sure the clone keeps all of them.
        var mail = new NpcGiftMailFact();
        var properties = typeof(NpcGiftMailFact).GetProperties()
            .Where(property => property.CanWrite)
            .ToList();
        for (int i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            object value = property.PropertyType == typeof(string) ? $"value-{i}"
                : property.PropertyType == typeof(int) ? 100 + i
                : property.PropertyType == typeof(bool) ? (object)true
                : throw new InvalidOperationException($"Add a sample value for {property.PropertyType} {property.Name}");
            property.SetValue(mail, value);
        }

        var state = new LivingNpcState { NpcName = "Emily", GiftMails = [mail] };

        var clonedMail = Assert.Single(state.Clone().GiftMails);

        foreach (var property in properties)
        {
            Assert.True(
                Equals(property.GetValue(mail), property.GetValue(clonedMail)),
                $"LivingNpcState.Clone() dropped NpcGiftMailFact.{property.Name}");
        }
    }
}
