using LivingNPCs.Behavior.Multiplayer;

namespace LivingNPCs.Tests.Multiplayer;

public sealed class HelpRequestQuestProjectionStoreTests
{
    [Fact]
    public void ApplyCompletelyReplacesTheCollectionAndClonesItsData()
    {
        var store = new HelpRequestQuestProjectionStore();
        store.Apply(new QuestProjectionMessage
        {
            Quests =
            {
                BuildProjection("emily-request", "Emily", "(O)80", dueTotalDays: 104),
                BuildProjection("haley-request", "Haley", "(O)396", dueTotalDays: 105)
            }
        });
        Assert.Equal(2, store.Count);

        HelpRequestQuestProjection replacement = BuildProjection(
            "leah-request",
            "Leah",
            "(O)388",
            dueTotalDays: 106);
        var replacementMessage = new QuestProjectionMessage { Quests = { replacement } };
        store.Apply(replacementMessage);

        replacement.Summary = "mutated input";
        replacement.Steps[0].RequestedItemLabel = "mutated input step";
        HelpRequestQuestProjection exported = Assert.Single(store.Export());
        Assert.Equal("leah-request", exported.QuestLogId);
        Assert.Equal("Bring the requested item.", exported.Summary);
        Assert.Equal("Requested item", Assert.Single(exported.Steps).RequestedItemLabel);

        exported.Summary = "mutated export";
        exported.Steps[0].RequestedItemLabel = "mutated export step";
        HelpRequestQuestProjection exportedAgain = Assert.Single(store.Export());
        Assert.Equal("Bring the requested item.", exportedAgain.Summary);
        Assert.Equal("Requested item", Assert.Single(exportedAgain.Steps).RequestedItemLabel);
    }

    [Fact]
    public void ApplyingAnEmptyProjectionClearsThePreviousCollection()
    {
        var store = new HelpRequestQuestProjectionStore();
        store.Apply(new QuestProjectionMessage
        {
            Quests = { BuildProjection("emily-request", "Emily", "(O)80", dueTotalDays: 104) }
        });

        store.Apply(new QuestProjectionMessage());

        Assert.Equal(0, store.Count);
        Assert.Empty(store.Export());
    }

    [Fact]
    public void TryFindPendingItemMatchesNpcAndItemCaseInsensitivelyAndChoosesEarliestDue()
    {
        var store = new HelpRequestQuestProjectionStore();
        HelpRequestQuestProjection later = BuildProjection(
            "later",
            "Emily",
            "(O)80",
            dueTotalDays: 110);
        HelpRequestQuestProjection earlier = BuildProjection(
            "earlier",
            "EMILY",
            "(o)80",
            dueTotalDays: 104);
        HelpRequestQuestProjection offered = BuildProjection(
            "offered",
            "Emily",
            "(O)80",
            dueTotalDays: 100);
        offered.Status = "Offered";
        HelpRequestQuestProjection wrongType = BuildProjection(
            "wrong-type",
            "Emily",
            "(O)80",
            dueTotalDays: 99);
        wrongType.Type = "question_request";
        store.Apply(new QuestProjectionMessage
        {
            Quests = { later, offered, wrongType, earlier }
        });

        Assert.True(store.TryFindPendingItem("emily", "(O)80", out HelpRequestQuestProjection? match));
        Assert.NotNull(match);
        Assert.Equal("earlier", match!.QuestLogId);

        match.Summary = "mutated result";
        Assert.True(store.TryFindPendingItem("Emily", "(o)80", out HelpRequestQuestProjection? second));
        Assert.Equal("Bring the requested item.", second!.Summary);
    }

    [Theory]
    [InlineData("Haley", "(O)80")]
    [InlineData("Emily", "(O)82")]
    public void TryFindPendingItemReturnsFalseWhenNpcOrItemDoesNotMatch(string npcName, string itemId)
    {
        var store = new HelpRequestQuestProjectionStore();
        store.Apply(new QuestProjectionMessage
        {
            Quests = { BuildProjection("emily-request", "Emily", "(O)80", dueTotalDays: 104) }
        });

        Assert.False(store.TryFindPendingItem(npcName, itemId, out HelpRequestQuestProjection? projection));
        Assert.Null(projection);
    }

    private static HelpRequestQuestProjection BuildProjection(
        string questLogId,
        string npcName,
        string itemId,
        int dueTotalDays)
    {
        return new HelpRequestQuestProjection
        {
            NpcName = npcName,
            NpcDisplayName = npcName,
            QuestLogId = questLogId,
            Type = "item_request",
            Summary = "Bring the requested item.",
            Steps =
            {
                new HelpRequestQuestStepProjection
                {
                    Type = "item_request",
                    Summary = "Bring the requested item.",
                    RequestedItemId = itemId,
                    RequestedItemLabel = "Requested item",
                    Status = "Pending"
                }
            },
            RequestedItemId = itemId,
            RequestedItemLabel = "Requested item",
            DueTotalDays = dueTotalDays,
            Status = "Pending"
        };
    }
}
