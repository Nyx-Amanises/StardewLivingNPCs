using LivingNPCs.Behavior;
using LivingNPCs.Dialogue.Engine;
using Newtonsoft.Json;

namespace LivingNPCs.Tests;

public sealed class MemoryEvidenceAccuracyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void LatestBilingualPromiseRevisionSurvivesOlderLiteralMatches(int count)
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 6; i++)
        {
            state.LongTermMemories.Add(TestScenarios.Memory($"Quartz delivery arrangement {i} was agreed.", 100, "promise", $"quartz delivery {i}"));
        }
        var correction = TestScenarios.Memory("石英已经不需要带了，约定取消。", 40, "promise", "石英交付");
        correction.LastUpdatedTotalDays = TestScenarios.Today;
        state.LongTermMemories.Add(correction);
        foreach (var memory in state.LongTermMemories)
        {
            LongTermMemoryStore.NormalizeForStore(memory);
        }
        string before = JsonConvert.SerializeObject(state.LongTermMemories);

        var recall = Recall(state, count, 0, "quartz delivery");

        Assert.Contains(recall.LongTermMemories, selection => ReferenceEquals(selection.Memory, correction));
        Assert.Equal(count, recall.LongTermMemories.Count);
        Assert.Equal(before, JsonConvert.SerializeObject(state.LongTermMemories));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void NewPreferenceCorrectionHasASlotEvenWhenLanguageAndPreferenceKindChanged(int count)
    {
        var state = TestScenarios.TrustedState();
        for (int i = 0; i < 6; i++)
        {
            state.PlayerPreferenceMemories.Add(Preference($"coffee {i}", $"The farmer likes coffee variety {i}.", 100, "liked_item_category", 90, 1000));
        }
        var correction = Preference("咖啡", "现在不喜欢喝咖啡了。", 40, "disliked_item", 99, 1000);
        state.PlayerPreferenceMemories.Add(correction);

        var recall = Recall(state, 0, count, "coffee");

        Assert.Contains(recall.PlayerPreferences, selection => ReferenceEquals(selection.Memory, correction));
        Assert.Equal(count, recall.PlayerPreferences.Count);
        Assert.Equal(7, state.PlayerPreferenceMemories.Count);
        Assert.All(state.PlayerPreferenceMemories.Take(6), memory => Assert.Equal("liked_item_category", memory.PreferenceKind));
    }

    [Fact]
    public void LatestUpdateUsesTheClockAndDoesNotTreatRecallTimeAsARevision()
    {
        var state = TestScenarios.TrustedState();
        var earlier = Preference("coffee", "I like coffee.", 100, "liked_item_category", 99, 900);
        earlier.LastRecalledTotalDays = 100;
        earlier.LastRecalledTimeOfDay = 1200;
        var later = Preference("咖啡", "咖啡我不喝了。", 40, "disliked_item", 99, 1100);
        state.PlayerPreferenceMemories.AddRange(new[] { earlier, later });

        Assert.Same(later, Assert.Single(Recall(state, 0, 1, "coffee").PlayerPreferences).Memory);
    }

    [Fact]
    public void NewerInferredTagAloneCannotDisplaceDirectTopicEvidence()
    {
        var state = TestScenarios.TrustedState();
        var coffee = TestScenarios.Memory("The farmer drinks coffee.", 90, subject: "coffee");
        var unrelated = TestScenarios.Memory("The farmer tends a garden.", 50, subject: "garden", tags: "coffee");
        unrelated.LastUpdatedTotalDays = TestScenarios.Today;
        state.LongTermMemories.AddRange(new[] { coffee, unrelated });

        Assert.Same(coffee, Assert.Single(Recall(state, 1, 0, "coffee").LongTermMemories).Memory);
    }

    [Fact]
    public void NoCurrentTopicPreservesSalienceAndZeroLimitsRemainEmpty()
    {
        var state = TestScenarios.TrustedState();
        var important = Preference("coffee", "Coffee was a favorite.", 100, "liked_item_category", 90, 1000);
        state.PlayerPreferenceMemories.Add(important);
        state.PlayerPreferenceMemories.Add(Preference("berries", "Berries are also nice.", 40, "liked_item_category", 99, 1200));

        Assert.Same(important, Assert.Single(Recall(state, 0, 1, null).PlayerPreferences).Memory);
        Assert.Empty(Recall(state, 0, 0, "coffee").PlayerPreferences);
    }

    [Fact]
    public void ReadOnlyRecallDoesNotResurrectAnOlderOppositeItemPreferenceBeforeStoreRefresh()
    {
        var state = TestScenarios.TrustedState();
        var earlier = Preference("coffee", "Coffee was a favorite.", 100, "liked_item_category", 90, 1000);
        var latest = Preference("coffee", "Coffee is now disliked.", 40, "disliked_item", 99, 1200);
        state.PlayerPreferenceMemories.AddRange(new[] { earlier, latest });

        var recalled = Recall(state, 0, 4, null);

        Assert.Same(latest, Assert.Single(recalled.PlayerPreferences).Memory);
        Assert.Equal(2, state.PlayerPreferenceMemories.Count);
        Assert.Equal("Coffee was a favorite.", earlier.Summary);
    }

    [Fact]
    public void InvalidPreferenceKindsDoNotBecomeCurrentPreferenceEvidence()
    {
        var state = TestScenarios.TrustedState();
        state.PlayerPreferenceMemories.Add(Preference("coffee", "Invalid legacy preference.", 100, "unrecognized_kind", 99, 1200));

        Assert.Empty(Recall(state, 0, 4, "coffee").PlayerPreferences);
    }

    [Fact]
    public void RecalledRecordsExposeExactSubjectsAndRevisionTimesInChronologicalOrder()
    {
        var earlier = Preference("coffee", "Coffee was a favorite.", 90, "liked_item_category", 98, 900);
        var correction = Preference("咖啡", "咖啡我不喝了。", 40, "disliked_item", 99, 1100);
        var selections = new[] { new PlayerPreferenceSelection(correction, 80, "topic"), new PlayerPreferenceSelection(earlier, 100, "topic") };

        string evidence = MemoryEvidenceFormatter.PlayerPreferences(selections, 100);

        Assert.True(evidence.IndexOf(earlier.Summary, StringComparison.Ordinal) < evidence.IndexOf(correction.Summary, StringComparison.Ordinal));
        Assert.Contains("subject=\"coffee\"; record updated 2 days ago at 9:00", evidence);
        Assert.Contains("preferenceKind=disliked_item; subject=\"咖啡\"; record updated yesterday at 11:00", evidence);
        Assert.Equal("咖啡我不喝了。", correction.Summary);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void MissingOrFutureRevisionDateCannotBecomeAnEventDate(int date)
    {
        var memory = TestScenarios.Memory("I promised to help tomorrow.", kind: "promise", subject: "library help");
        memory.LastUpdatedTotalDays = date;

        string evidence = MemoryEvidenceFormatter.LongTermMemories(new[] { new LongTermMemorySelection(memory, 80, "topic") }, 100);

        Assert.Contains("record update time unknown", evidence);
        Assert.Contains(memory.Summary, evidence);
        Assert.DoesNotContain("updated today", evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BriefContextKeepsCompleteSelectedCorrectionsAndStructuredHelpState(bool concise)
    {
        var memory = TestScenarios.Memory("The quartz delivery was cancelled.", 80, "promise", "quartz delivery");
        var recalled = MemoryEvidenceFormatter.LongTermMemories(new[] { new LongTermMemorySelection(memory, 90, "topic") }, 100);
        var help = new NpcHelpRequestFact
        {
            Summary = "Bring quartz to the library.", Status = "Fulfilled", Type = "item_request",
            RequestedItemId = "(O)80", RequestedItemLabel = "Quartz", DueTotalDays = 99,
            Steps = new() { new() { Type = "item_request", Summary = "Bring quartz.", RequestedItemId = "(O)80", RequestedItemLabel = "Quartz", Status = "Fulfilled" } }
        };
        string helpFact = PromptFragments.Facts.HelpRequest(help, 100);
        string memoryLine = concise ? "- Recall focus: " + recalled : PromptFragments.Context.LongTermRecallCue(recalled);
        string helpLine = concise ? "- Help requests: " + helpFact : PromptFragments.Context.HelpRequestsLine(helpFact);
        string full = PromptFragments.Context.Header("Penny") + "\n"
            + string.Join("\n", Enumerable.Range(0, 25).Select(i => "- Mood: background " + i + new string('x', 90)))
            + "\n" + memoryLine + "\n" + helpLine;

        string brief = LivingNpcContextCompressor.BuildBriefContext(full, maxLines: 12, fallbackLines: 6, maxCharacters: 900);

        Assert.Contains(memoryLine, brief);
        Assert.Contains(helpLine, brief);
        Assert.Contains("status Fulfilled", brief);
        Assert.DoesNotContain("background 24", brief);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MultilineRevisionStaysTogetherInBriefContextWithoutChangingStoredText(bool concise, bool preference)
    {
        const string summary = "The farmer promised to bring quartz.\r\nCancelled later; nothing is owed.";
        const string subject = "quartz\n\"delivery\"";
        var memory = TestScenarios.Memory(summary, 80, "promise", subject);
        var pref = Preference(subject, summary, 80, "habit", 99, 1000);
        string before = JsonConvert.SerializeObject(new { memory, pref });
        string evidence = preference
            ? MemoryEvidenceFormatter.PlayerPreferences(new[] { new PlayerPreferenceSelection(pref, 90, "topic") }, 100)
            : MemoryEvidenceFormatter.LongTermMemories(new[] { new LongTermMemorySelection(memory, 90, "topic") }, 100);
        string line = concise
            ? (preference ? "- Known farmer preferences: " : "- Recall focus: ") + evidence
            : preference ? PromptFragments.Context.PreferenceRecallCue(evidence) : PromptFragments.Context.LongTermRecallCue(evidence);

        string brief = LivingNpcContextCompressor.BuildBriefContext(PromptFragments.Context.Header("Penny") + "\n" + line, maxCharacters: 80);

        Assert.Contains("The farmer promised to bring quartz. Cancelled later; nothing is owed.", brief);
        Assert.Contains("subject=\"quartz\\n\\\"delivery\\\"\"", brief);
        Assert.DoesNotContain('\n', evidence);
        Assert.Equal(before, JsonConvert.SerializeObject(new { memory, pref }));
    }

    private static MemoryRecallPlan Recall(LivingNpcState state, int memoryCount, int preferenceCount, string? query) =>
        MemoryRecallService.BuildPlan(state, TestScenarios.World(), Array.Empty<BehaviorMemoryEntry>(), memoryCount, preferenceCount, TestScenarios.Today, query);

    private static PlayerPreferenceFact Preference(string subject, string summary, int importance, string kind, int day, int time) => new()
    {
        Subject = subject, Summary = summary, Importance = importance, PreferenceKind = kind,
        CreatedTotalDays = day, CreatedTimeOfDay = time, LastUpdatedTotalDays = day, LastUpdatedTimeOfDay = time
    };
}
