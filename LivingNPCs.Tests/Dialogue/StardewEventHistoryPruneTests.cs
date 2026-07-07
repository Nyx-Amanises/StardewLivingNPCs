using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue;

public sealed class StardewEventHistoryPruneTests
{
    private static readonly StardewTime Now = new(3, StardewValley.Season.Spring, 14, 1200);

    private static StardewTime DaysAgo(int days, int timeOfDay = 900)
    {
        var shifted = Now.AddDays(-days);
        return new StardewTime(shifted.year, shifted.season, shifted.dayOfMonth, timeOfDay);
    }

    private static ConversationHistory MakeConversation(string text)
    {
        return new ConversationHistory(new List<ConversationElement> { new(text, true) });
    }

    [Fact]
    public void ConversationsBeyondCapArePrunedOldestFirstAndReturned()
    {
        var history = new StardewEventHistory();
        int total = StardewEventHistory.MaxConversationEntries + 5;
        for (int i = 0; i < total; i++)
        {
            // Oldest first: entry i happened (total - i) days ago, all within the age limit.
            history.Add(DaysAgo(total - i), MakeConversation($"conversation {i}"));
        }

        var dropped = history.Prune(Now);

        Assert.Equal(5, dropped.Count);
        Assert.Equal(StardewEventHistory.MaxConversationEntries, history.ConversationHistory.Count);

        // The five oldest conversations are returned, oldest first.
        for (int i = 0; i < dropped.Count; i++)
        {
            Assert.Equal($"conversation {i}", dropped[i].Item2.ConversationElements[0].Text);
        }
        for (int i = 1; i < dropped.Count; i++)
        {
            Assert.True(dropped[i - 1].Item1.CompareTo(dropped[i].Item1) <= 0);
        }

        // The retained list keeps the newest entries.
        Assert.DoesNotContain(history.ConversationHistory, entry => entry.Item2.ConversationElements[0].Text == "conversation 0");
        Assert.Contains(history.ConversationHistory, entry => entry.Item2.ConversationElements[0].Text == $"conversation {total - 1}");
    }

    [Fact]
    public void ConversationsBeyondAgeLimitArePrunedAndReturnedForArchiving()
    {
        var history = new StardewEventHistory();
        history.Add(DaysAgo(StardewEventHistory.MaxAgeDays + 5), MakeConversation("ancient"));
        history.Add(DaysAgo(3), MakeConversation("recent"));

        var dropped = history.Prune(Now);

        Assert.Single(dropped);
        Assert.Equal("ancient", dropped[0].Item2.ConversationElements[0].Text);
        Assert.Single(history.ConversationHistory);
    }

    [Fact]
    public void DialogueEntriesOlderThanOneYearArePruned()
    {
        var history = new StardewEventHistory();
        for (int i = 0; i < 3; i++)
        {
            history.Add(DaysAgo(StardewEventHistory.MaxAgeDays + 10 + i), new DialogueHistory(new List<HistoryLine>()));
        }
        history.Add(DaysAgo(10), new DialogueHistory(new List<HistoryLine>()));
        history.Add(DaysAgo(5), new DialogueHistory(new List<HistoryLine>()));

        history.Prune(Now);

        Assert.Equal(2, history.DialogueHistory.Count);
    }

    [Fact]
    public void EventEntriesIgnoreTheAgeLimitButHonorTheCap()
    {
        var history = new StardewEventHistory();
        int total = StardewEventHistory.MaxEventEntries + 2;
        for (int i = 0; i < total; i++)
        {
            history.Add(
                DaysAgo(400 + total - i),
                new DialogueEventHistory(new List<string>(), new List<HistoryLine>(), $"Festival {i}"));
        }

        history.Prune(Now);

        Assert.Equal(StardewEventHistory.MaxEventEntries, history.EventHistory.Count);
        // Age alone never removes event entries; only the cap dropped the two oldest.
        Assert.DoesNotContain(history.EventHistory, entry => entry.Item2.EventName == "Festival 0");
        Assert.Contains(history.EventHistory, entry => entry.Item2.EventName == $"Festival {total - 1}");
    }

    [Fact]
    public void OverheardEntriesHonorCapAndAgeLimit()
    {
        var history = new StardewEventHistory();
        history.Add(DaysAgo(StardewEventHistory.MaxAgeDays + 1), new OverheardHistory("Abigail", new List<HistoryLine> { new("old line") }));
        int total = StardewEventHistory.MaxOverheardEntries + 3;
        for (int i = 0; i < total; i++)
        {
            history.Add(DaysAgo(total - i), new OverheardHistory("Sam", new List<HistoryLine> { new($"line {i}") }));
        }

        history.Prune(Now);

        Assert.Equal(StardewEventHistory.MaxOverheardEntries, history.OverheardHistory.Count);
        Assert.DoesNotContain(history.OverheardHistory, entry => entry.Item2.SpeakerName == "Abigail");
    }

    [Fact]
    public void TodayConversationsAreProtectedEvenBeyondCap()
    {
        var history = new StardewEventHistory();
        int total = StardewEventHistory.MaxConversationEntries + 5;
        for (int i = 0; i < total; i++)
        {
            var time = new StardewTime(Now.year, Now.season, Now.dayOfMonth, 600 + i * 10);
            history.Add(time, MakeConversation($"today {i}"));
        }

        var dropped = history.Prune(Now);

        Assert.Empty(dropped);
        Assert.Equal(total, history.ConversationHistory.Count);
    }

    [Fact]
    public void PruneIsIdempotent()
    {
        var history = new StardewEventHistory();
        int total = StardewEventHistory.MaxConversationEntries + 3;
        for (int i = 0; i < total; i++)
        {
            history.Add(DaysAgo(total - i), MakeConversation($"conversation {i}"));
        }

        var firstPass = history.Prune(Now);
        var secondPass = history.Prune(Now);

        Assert.Equal(3, firstPass.Count);
        Assert.Empty(secondPass);
        Assert.Equal(StardewEventHistory.MaxConversationEntries, history.ConversationHistory.Count);
    }

    [Fact]
    public void AddKeepsThirdPartyHistoryInMemoryOnlyWithoutAffectingPersistableBuckets()
    {
        var history = new StardewEventHistory();

        // Third-party sightings have no persistable bucket (WP10 §4.14): they stay in memory for
        // same-day sampling and never count as persistable history.
        history.Add(DaysAgo(1), new ThirdPartyHistory("Sam", new List<HistoryLine>(), "FairFestival"));

        Assert.False(history.Any());
        Assert.Single(history.ThirdPartyHistory);
    }

    [Fact]
    public void RemoveAfterDropsEntriesLaterThanTheCurrentTime()
    {
        var history = new StardewEventHistory();
        history.Add(DaysAgo(2), MakeConversation("past"));
        history.Add(new StardewTime(Now.year, Now.season, Now.dayOfMonth, Now.timeOfDay + 100), MakeConversation("future same day"));
        history.Add(Now.AddDays(3), new DialogueHistory(new List<HistoryLine> { new("future line") }));

        int removed = history.RemoveAfter(Now);

        Assert.Equal(2, removed);
        Assert.Single(history.ConversationHistory);
        Assert.Empty(history.DialogueHistory);
    }
}
