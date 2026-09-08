using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Ui;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Persistence;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Tests;

public sealed class MemoryBookMenuStateTests
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void RemoteMenuLoadingTimeoutAndSuccessfulEmptySnapshotRemainSafe()
    {
        MemoryBookMenu menu = CreateLoadingMenu();

        Assert.True(menu.IsRemoteLoading);
        Assert.False(menu.IsRemoteTimedOut);
        Assert.False(menu.HasBrowsableContent);
        Assert.Null(menu.SelectedNpcName);
        Assert.Equal("book.remote.loading", menu.StatusText);
        ExerciseNonClosingInput(menu);

        menu.MarkTimedOut();

        Assert.False(menu.IsRemoteLoading);
        Assert.True(menu.IsRemoteTimedOut);
        Assert.False(menu.HasBrowsableContent);
        Assert.Null(menu.SelectedNpcName);
        Assert.Equal("book.remote.timeout", menu.StatusText);
        ExerciseNonClosingInput(menu);

        menu.ApplySnapshot(Source(Array.Empty<string>()));

        Assert.False(menu.IsRemoteLoading);
        Assert.False(menu.IsRemoteTimedOut);
        Assert.False(menu.HasBrowsableContent);
        Assert.Null(menu.SelectedNpcName);
        Assert.Equal("book.remote.empty", menu.StatusText);
        ExerciseNonClosingInput(menu);
    }

    [Fact]
    public void ApplyingReplacementSnapshotPreservesSelectionAndClearsTransientState()
    {
        MemoryBookMenu menu = CreateLoadingMenu();
        menu.ApplySnapshot(Source(new[] { "Abigail", "Leah" }));
        menu.SelectRoster(1, playSound: false);
        _ = menu.GetPageLines("Leah", MemoryBookTab.Relationship);

        SetField(menu, "contentScroll", 123f);
        SetField(menu, "contentHeight", 999f);
        SetField(menu, "hoverText", "old hover");
        SetField(menu, "wrappedForWidth", 640);
        IList wrappedLines = GetField<IList>(menu, "wrappedLines");
        wrappedLines.Add((new MemoryBookLine(MemoryBookLineKind.Body, "old"), "old", 12f));

        Assert.Equal("Leah", menu.SelectedNpcName);
        Assert.Equal(1, menu.CachedPageCount);
        Assert.Equal(1, GetField<int>(menu, "rosterScrollIndex"));

        menu.ApplySnapshot(Source(new[] { "Leah", "Penny" }));

        Assert.Equal("Leah", menu.SelectedNpcName);
        Assert.True(menu.HasBrowsableContent);
        Assert.Equal(string.Empty, menu.StatusText);
        Assert.Equal(0, menu.CachedPageCount);
        Assert.Empty(GetField<IList>(menu, "wrappedLines"));
        Assert.Equal(-1, GetField<int>(menu, "wrappedForWidth"));
        Assert.Null(GetField<object?>(menu, "wrappedForPage"));
        Assert.Equal(0, GetField<int>(menu, "rosterScrollIndex"));
        Assert.Equal(0f, GetField<float>(menu, "contentScroll"));
        Assert.Equal(0f, GetField<float>(menu, "contentHeight"));
        Assert.Equal(string.Empty, GetField<string>(menu, "hoverText"));

        menu.ApplySnapshot(Source(new[] { "Maru", "Emily" }));

        Assert.Equal("Maru", menu.SelectedNpcName);
    }

    [Fact]
    public void ConversationsPageAlwaysUsesFarmhandLocalHistoryWithoutRemoteState()
    {
        int historyReads = 0;
        MemoryBookMenu menu = MemoryBookMenu.CreateRemoteLoading(
            npcName =>
            {
                historyReads++;
                return new StardewEventHistory { NpcName = npcName };
            },
            () => "Yuki",
            Echo);
        var roster = new[]
        {
            new MemoryBookNpcSummary("Abigail", "Abigail", 4, 12, "recent")
        };
        menu.ApplySnapshot(new MemoryBookSnapshotSource(roster, Array.Empty<LivingNpcState>(), 12));

        List<MemoryBookLine> conversations = menu.GetPageLines("Abigail", MemoryBookTab.Conversations);

        Assert.Equal(1, historyReads);
        Assert.Equal("book.conversations.empty", Assert.Single(conversations).Text);

        List<MemoryBookLine> memories = menu.GetPageLines("Abigail", MemoryBookTab.Memories);
        Assert.Equal(1, historyReads);
        Assert.Equal("book.memories.empty", Assert.Single(memories).Text);
    }

    [Fact]
    public void ConversationsPageShowsTheFullRetainedSessionLimit()
    {
        var history = new StardewEventHistory { NpcName = "Penny" };
        for (int i = 0; i < StardewEventHistory.MaxConversationEntries; i++)
        {
            history.Add(
                new StardewTime(1, Season.Spring, 24, 600 + i * 10),
                new ConversationHistory([new ConversationElement($"message {i}", false)]));
        }

        MemoryBookMenu menu = MemoryBookMenu.CreateRemoteLoading(_ => history, () => "Yuki", Echo);
        menu.ApplySnapshot(Source(new[] { "Penny" }));

        List<MemoryBookLine> lines = menu.GetPageLines("Penny", MemoryBookTab.Conversations);

        Assert.Equal(StardewEventHistory.MaxConversationEntries, lines.Count(line => line.Kind == MemoryBookLineKind.NpcLine));
        Assert.Equal("Penny: message 0", lines[1].Text);
        Assert.Equal($"Penny: message {StardewEventHistory.MaxConversationEntries - 1}", lines[^1].Text);
    }

    [Fact]
    public void ConversationsStartAtLatestAndLeaveTheReadersScrollPositionAlone()
    {
        MemoryBookMenu menu = CreateLoadingMenu();
        menu.ApplySnapshot(Source(new[] { "Penny", "Leah" }));
        SetField(menu, "contentBounds", new Rectangle(0, 0, 700, 400));
        menu.SelectTab(MemoryBookTab.Conversations, playSound: false);

        menu.UpdateContentMetrics(1400f);
        Assert.Equal(1000f, GetField<float>(menu, "contentScroll"));

        menu.ScrollContent(-84f);
        menu.UpdateContentMetrics(1400f);
        Assert.Equal(916f, GetField<float>(menu, "contentScroll"));

        // A resize can reflow history, but must not yank a reader back to the last message.
        SetField(menu, "contentBounds", new Rectangle(0, 0, 620, 320));
        menu.UpdateContentMetrics(1580f);
        Assert.Equal(916f, GetField<float>(menu, "contentScroll"));

        menu.SelectRoster(1, playSound: false);
        menu.UpdateContentMetrics(980f);
        Assert.Equal(660f, GetField<float>(menu, "contentScroll"));

        menu.SelectTab(MemoryBookTab.Moments, playSound: false);
        menu.UpdateContentMetrics(1100f);
        Assert.Equal(0f, GetField<float>(menu, "contentScroll"));

        menu.SelectTab(MemoryBookTab.Conversations, playSound: false);
        menu.UpdateContentMetrics(980f);
        Assert.Equal(660f, GetField<float>(menu, "contentScroll"));
    }

    [Fact]
    public void ConversationsKeepTheLastMessageVisibleWhenTheViewportChanges()
    {
        MemoryBookMenu menu = CreateLoadingMenu();
        menu.ApplySnapshot(Source(new[] { "Penny" }));
        SetField(menu, "contentBounds", new Rectangle(0, 0, 700, 400));
        menu.SelectTab(MemoryBookTab.Conversations, playSound: false);
        menu.UpdateContentMetrics(900f);

        SetField(menu, "contentBounds", new Rectangle(0, 0, 640, 320));
        menu.UpdateContentMetrics(1000f);
        Assert.Equal(680f, GetField<float>(menu, "contentScroll"));

        SetField(menu, "contentBounds", new Rectangle(0, 0, 780, 500));
        menu.UpdateContentMetrics(920f);
        Assert.Equal(420f, GetField<float>(menu, "contentScroll"));

        // A short or empty history has no scroll range.
        menu.UpdateContentMetrics(200f);
        Assert.Equal(0f, GetField<float>(menu, "contentScroll"));
    }

    [Fact]
    public void ConversationsKeepTheirVisibleMessageWhenAWiderPageShortensHistory()
    {
        MemoryBookMenu menu = CreateLoadingMenu();
        menu.ApplySnapshot(Source(new[] { "Penny" }));
        SetField(menu, "contentBounds", new Rectangle(0, 0, 480, 300));
        menu.SelectTab(MemoryBookTab.Conversations, playSound: false);
        MemoryBookLine[] messages =
        [
            new(MemoryBookLineKind.DateSeparator, "Spring 24"),
            new(MemoryBookLineKind.PlayerLine, "Yuki: Earlier question"),
            new(MemoryBookLineKind.NpcLine, "Penny: The message being read"),
            new(MemoryBookLineKind.PlayerLine, "Yuki: Later question"),
            new(MemoryBookLineKind.NpcLine, "Penny: Latest reply")
        ];
        menu.ApplyWrappedLayout(Layout([50f, 500f, 600f, 500f, 450f]), preserveReadingPosition: false);
        Assert.Equal(1856f, GetField<float>(menu, "contentScroll"));

        // Read halfway through the third message, with newer exchanges still below it.
        menu.ScrollContent(-990f);
        Assert.Equal(866f, GetField<float>(menu, "contentScroll"));
        SetField(menu, "contentBounds", new Rectangle(0, 0, 880, 300));
        var widerLayout = Layout([50f, 180f, 240f, 180f, 160f]);
        menu.ApplyWrappedLayout(widerLayout, preserveReadingPosition: true);

        // Keeping 866 pixels would clamp to the new bottom at 566. Instead, the
        // clipped viewport still begins halfway through the same third message.
        Assert.Equal(366f, GetField<float>(menu, "contentScroll"));

        SetField(menu, "contentBounds", new Rectangle(0, 0, 880, 360));
        menu.ApplyWrappedLayout(widerLayout, preserveReadingPosition: true);
        Assert.Equal(366f, GetField<float>(menu, "contentScroll"));

        List<(MemoryBookLine Line, string Wrapped, float Height)> Layout(float[] heights)
            => messages.Select((line, index) => (line, line.Text, heights[index])).ToList();
    }

    private static MemoryBookMenu CreateLoadingMenu()
    {
        return MemoryBookMenu.CreateRemoteLoading(
            npcName => new StardewEventHistory { NpcName = npcName },
            () => "Yuki",
            Echo);
    }

    private static MemoryBookSnapshotSource Source(IEnumerable<string> npcNames)
    {
        var roster = new List<MemoryBookNpcSummary>();
        var states = new List<LivingNpcState>();
        int index = 0;
        foreach (string npcName in npcNames)
        {
            roster.Add(new MemoryBookNpcSummary(npcName, npcName, index + 1, 20 - index, "recent"));
            states.Add(new LivingNpcState
            {
                NpcName = npcName,
                LastConversationTotalDays = 20 - index,
                LastUpdatedTotalDays = 20 - index
            });
            index++;
        }

        return new MemoryBookSnapshotSource(roster, states, capturedTotalDays: 20);
    }

    private static void ExerciseNonClosingInput(MemoryBookMenu menu)
    {
        menu.receiveLeftClick(0, 0);
        menu.receiveScrollWheelAction(1);
        menu.performHoverAction(0, 0);
    }

    private static string Echo(string key, object? tokens = null)
    {
        return key;
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, InstanceFields)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        return (T)field.GetValue(target)!;
    }

    private static void SetField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, InstanceFields)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        field.SetValue(target, value);
    }
}
