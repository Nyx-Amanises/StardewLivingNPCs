using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ValleyTalk;

namespace ValleyTalk.Tests;

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
    public void DialogueEntriesOlderThanOneYearArePruned()
    {
        var history = new StardewEventHistory();
        for (int i = 0; i < 3; i++)
        {
            history.Add(DaysAgo(StardewEventHistory.MaxAgeDays + 10 + i), new DialogueHistory(new List<StardewValley.DialogueLine>()));
        }
        history.Add(DaysAgo(10), new DialogueHistory(new List<StardewValley.DialogueLine>()));
        history.Add(DaysAgo(5), new DialogueHistory(new List<StardewValley.DialogueLine>()));

        history.Prune(Now);

        Assert.Equal(2, history.DialogueHistory.Count);
    }

    [Fact]
    public void EventEntriesIgnoreTheAgeLimit()
    {
        var history = new StardewEventHistory();
        for (int i = 0; i < 3; i++)
        {
            history.Add(
                DaysAgo(400 + i),
                new DialogueEventHistory(new List<StardewValley.NPC>(), new List<StardewValley.DialogueLine>(), "FlowerDance"));
        }

        history.Prune(Now);

        Assert.Equal(3, history.EventHistory.Count);
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
    public void AddIgnoresThirdPartyHistoryInsteadOfThrowing()
    {
        var history = new StardewEventHistory();

        // ThirdPartyHistory has no persistable bucket; recording it used to throw from inside the
        // dialogue pipeline. It must now be dropped silently.
        history.Add(DaysAgo(1), new ThirdPartyHistory(null, new List<StardewValley.DialogueLine>(), "FairFestival"));

        Assert.False(history.Any());
    }
}
