using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

public sealed class MetadataParsingTests
{
    [Fact]
    public void AiMetadataWritesLongTermMemory()
    {
        const string json = """
        {
          "memories": [
            {
              "kind": "promise",
              "subject": "library",
              "summary": "The farmer promised to bring Emily quartz for the library display.",
              "importance": 88,
              "tags": ["scholarly", "mineral"]
            }
          ]
        }
        """;
        var state = TestScenarios.TrustedState();

        int stored = new BehaviorMemory().StoreLongTermMemoriesFromAnalysisForTesting(
            state,
            json,
            TestScenarios.Today
        );

        Assert.Equal(1, stored);
        var memory = Assert.Single(state.LongTermMemories);
        Assert.Equal("promise", memory.Kind);
        Assert.Equal("library", memory.Subject);
        Assert.Equal(88, memory.Importance);
        Assert.Equal(TestScenarios.Today, memory.CreatedTotalDays);
        Assert.Contains("quartz", memory.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scholarly", memory.Tags);
    }

    [Fact]
    public void EmptyArraysAndBadFieldTypesDoNotMutateState()
    {
        var state = TestScenarios.TrustedState();
        state.LongTermMemories.Add(TestScenarios.Memory("Existing memory.", importance: 55));

        int emptyStored = new BehaviorMemory().StoreLongTermMemoriesFromAnalysisForTesting(
            state,
            """{ "memories": [] }""",
            TestScenarios.Today
        );
        int badTypeStored = new BehaviorMemory().StoreLongTermMemoriesFromAnalysisForTesting(
            state,
            """{ "memories": [{ "summary": "Bad importance", "importance": "very" }] }""",
            TestScenarios.Today
        );

        Assert.Equal(0, emptyStored);
        Assert.Equal(0, badTypeStored);
        Assert.Single(state.LongTermMemories);
        Assert.Equal("Existing memory.", state.LongTermMemories[0].Summary);
    }

    [Fact]
    public void InvalidAndOutOfRangeMetadataIsNormalizedBeforeStorage()
    {
        const string json = """
        {
          "rapportDelta": 999,
          "ambientFollowUp": { "text": "  later  ", "delayMinutes": 999 },
          "emotionImpact": { "emotion": "angry", "intensityDelta": 999, "repairDelta": 999 },
          "memories": [
            {
              "kind": "made_up_kind",
              "subject": "  coffee  ",
              "summary": "The farmer mentioned coffee before sunrise.",
              "importance": 999,
              "tags": ["drink", "forbidden_tag"]
            }
          ],
          "conflicts": [
            { "causeKind": "strange", "summary": "  rude line  ", "severity": 999 }
          ],
          "helpRequests": [
            { "type": "item_request", "summary": "  bring coffee  ", "dueInDays": 999 }
          ]
        }
        """;

        var analysis = ValleyTalkExchangeParser.Parse(json);

        Assert.Equal(30, analysis.RapportDelta);
        Assert.Equal("later", analysis.AmbientFollowUp.Text);
        Assert.Equal(120, analysis.AmbientFollowUp.DelayMinutes);
        Assert.Equal("Angry", analysis.EmotionImpact.Emotion);
        Assert.Equal(100, analysis.EmotionImpact.IntensityDelta);
        Assert.Equal(100, analysis.EmotionImpact.RepairDelta);

        var parsedMemory = Assert.Single(analysis.Memories);
        Assert.Equal("fact", parsedMemory.Kind);
        Assert.Equal("coffee", parsedMemory.Subject);
        Assert.Equal(100, parsedMemory.Importance);
        Assert.Contains("drink", parsedMemory.Tags);
        Assert.DoesNotContain("forbidden_tag", parsedMemory.Tags);

        var conflict = Assert.Single(analysis.Conflicts);
        Assert.Equal("dialogue", conflict.CauseKind);
        Assert.Equal("rude line", conflict.Summary);
        Assert.Equal(100, conflict.Severity);

        var request = Assert.Single(analysis.HelpRequests);
        Assert.Equal("item_request", request.Type);
        Assert.Equal("bring coffee", request.Summary);
        Assert.Equal(7, request.DueInDays);
    }
}
