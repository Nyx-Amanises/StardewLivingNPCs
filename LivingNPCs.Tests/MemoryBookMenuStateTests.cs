using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Ui;
using LivingNPCs.Dialogue.Persistence;

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
