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
                            AssignedPlayerId = 987654321,
                            AssignedPlayerName = "Farmhand",
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
        Assert.Equal(987654321, helpRequest.AssignedPlayerId);
        Assert.Equal("Farmhand", helpRequest.AssignedPlayerName);
        Assert.Equal("(O)80", helpRequest.RequestedItemId);
        Assert.Equal(240, helpRequest.RewardMoney);
        Assert.True(helpRequest.RewardMoneyClaimQueued);
        Assert.True(helpRequest.RewardMoneyQuestPosted);
    }

    [Fact]
    public void SaveDataCarriesSchemaVersionAndTreatsLegacyBlobsAsVersionOne()
    {
        string json = JsonSerializer.Serialize(new BehaviorMemorySaveData());
        Assert.Contains("\"SchemaVersion\":1", json);

        // Blobs written before the field existed have exactly the version-1 layout, so a missing
        // field must deserialize as version 1 rather than 0.
        var legacy = JsonSerializer.Deserialize<BehaviorMemorySaveData>("{\"LastStateDecayTotalDays\":5}");
        Assert.NotNull(legacy);
        Assert.Equal(BehaviorMemorySaveData.CurrentSchemaVersion, legacy!.SchemaVersion);
        Assert.Equal(5, legacy.LastStateDecayTotalDays);
    }

    [Fact]
    public void LegacyHelpRequestBubbleFieldsDoNotAffectRewardsOrSharedExperienceRoundTrip()
    {
        // Old saves can contain an unshown fixed bubble. Its retired fields must be ignored,
        // while the independent shared-experience cue remains available for normal dialogue.
        const string legacyJson = """
            {
              "StatesByNpc": {
                "Emily": {
                  "NpcName": "Emily",
                  "HelpRequests": [{
                    "AssignedPlayerId": 987654321,
                    "QuestLogId": "livingnpcs:emily:quartz",
                    "Type": "item_request",
                    "Summary": "Bring quartz.",
                    "RequestedItemId": "(O)80",
                    "Status": "Fulfilled",
                    "Steps": [{
                      "Type": "item_request",
                      "Summary": "Bring quartz.",
                      "RequestedItemId": "(O)80",
                      "Status": "Fulfilled",
                      "CompletedTotalDays": 100,
                      "CompletedTimeOfDay": 1200
                    }],
                    "FulfilledTotalDays": 100,
                    "FulfilledTimeOfDay": 1200,
                    "RewardFriendship": 75,
                    "RewardGranted": true,
                    "RewardMoney": 500,
                    "RewardMoneyClaimQueued": true,
                    "RewardMoneyQuestPosted": true,
                    "LastMentionedTotalDays": -1,
                    "FollowUpPotential": "deeper_relationship",
                    "SpecialFollowUpPlanned": true,
                    "FollowUpEligibleTotalDays": 101,
                    "FollowUpShownTotalDays": -1,
                    "FollowUpShownTimeOfDay": 0
                  }],
                  "SharedExperiences": [{
                    "Type": "help_request",
                    "Summary": "The farmer brought Emily quartz.",
                    "LastUpdatedTotalDays": 100,
                    "Importance": 82,
                    "FollowUpEligibleTotalDays": 102,
                    "FollowUpShownTotalDays": -1,
                    "FollowUpShownTimeOfDay": 0
                  }]
                }
              }
            }
            """;

        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<BehaviorMemorySaveData>(legacyJson);
        Assert.NotNull(restored);
        LivingNpcState state = restored.StatesByNpc["Emily"].Clone();

        var request = Assert.Single(state.HelpRequests);
        Assert.Equal("Fulfilled", request.Status);
        Assert.Equal("Fulfilled", Assert.Single(request.Steps).Status);
        Assert.Equal("(O)80", request.RequestedItemId);
        Assert.Equal(987654321, request.AssignedPlayerId);
        Assert.Equal("livingnpcs:emily:quartz", request.QuestLogId);
        Assert.Equal(100, request.FulfilledTotalDays);
        Assert.Equal(1200, request.FulfilledTimeOfDay);
        Assert.Equal(75, request.RewardFriendship);
        Assert.True(request.RewardGranted);
        Assert.Equal(500, request.RewardMoney);
        Assert.False(request.RewardMoneyGranted);
        Assert.True(request.RewardMoneyClaimQueued);
        Assert.True(request.RewardMoneyQuestPosted);
        Assert.Equal(-1, request.LastMentionedTotalDays);
        Assert.Equal("deeper_relationship", request.FollowUpPotential);

        var experience = Assert.Single(state.SharedExperiences);
        Assert.Equal("The farmer brought Emily quartz.", experience.Summary);
        Assert.Equal(102, experience.FollowUpEligibleTotalDays);
        Assert.Equal(-1, experience.FollowUpShownTotalDays);

        var savedRequest = Newtonsoft.Json.Linq.JObject.FromObject(request);
        Assert.Null(savedRequest["SpecialFollowUpPlanned"]);
        Assert.Null(savedRequest["FollowUpEligibleTotalDays"]);
        Assert.Null(savedRequest["FollowUpShownTotalDays"]);
        Assert.Null(savedRequest["FollowUpShownTimeOfDay"]);
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
