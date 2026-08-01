using LivingNPCs.Behavior;
using LivingNPCs.Behavior.Multiplayer;
using StardewValley.Quests;

namespace LivingNPCs.Tests.Multiplayer;

public sealed class HelpRequestQuestLogProjectionTests
{
    [Fact]
    public void BuildProjectionStrictlySeparatesAssignedPlayers()
    {
        LivingNpcState state = BuildState(
            "Emily",
            BuildRequest("host", assignedPlayerId: -1),
            BuildRequest("farmhand-a", assignedPlayerId: 101),
            BuildRequest("farmhand-b", assignedPlayerId: 202));

        QuestProjectionMessage host = HelpRequestQuestLogService.BuildProjection(
            new[] { state },
            assignedPlayerId: -1,
            enableHelpRequests: true);
        QuestProjectionMessage farmhandA = HelpRequestQuestLogService.BuildProjection(
            new[] { state },
            assignedPlayerId: 101,
            enableHelpRequests: true);
        QuestProjectionMessage farmhandB = HelpRequestQuestLogService.BuildProjection(
            new[] { state },
            assignedPlayerId: 202,
            enableHelpRequests: true);

        Assert.Equal("host", Assert.Single(host.Quests).QuestLogId);
        Assert.Equal("farmhand-a", Assert.Single(farmhandA.Quests).QuestLogId);
        Assert.Equal("farmhand-b", Assert.Single(farmhandB.Quests).QuestLogId);
        Assert.Equal(SyncProtocol.Version, host.SchemaVersion);
    }

    [Fact]
    public void BuildProjectionCopiesCompleteMultiStepState()
    {
        NpcHelpRequestFact request = BuildRequest("multi-step", assignedPlayerId: 101);
        request.NpcDisplayName = "艾米丽";
        request.Summary = "Help with two errands.";
        request.Type = "question_request";
        request.CurrentStepIndex = 1;
        request.RequestedItemId = string.Empty;
        request.RequestedItemLabel = string.Empty;
        request.QuestionTopic = "What color should the sign be?";
        request.Reason = "The shop needs a new sign.";
        request.Steps =
        [
            new NpcHelpRequestStepFact
            {
                Type = "item_request",
                Summary = "Bring cloth.",
                RequestedItemId = "(O)428",
                RequestedItemLabel = "Cloth",
                Status = "Fulfilled"
            },
            new NpcHelpRequestStepFact
            {
                Type = "question_request",
                Summary = "Choose a color.",
                QuestionTopic = "What color should the sign be?",
                Status = "Pending"
            }
        ];

        HelpRequestQuestProjection projection = Assert.Single(
            HelpRequestQuestLogService.BuildProjection(
                new[] { BuildState("Emily", request) },
                assignedPlayerId: 101,
                enableHelpRequests: true).Quests);

        Assert.Equal("Emily", projection.NpcName);
        Assert.Equal("艾米丽", projection.NpcDisplayName);
        Assert.Equal("multi-step", projection.QuestLogId);
        Assert.Equal("question_request", projection.Type);
        Assert.Equal(1, projection.CurrentStepIndex);
        Assert.Equal("What color should the sign be?", projection.QuestionTopic);
        Assert.Equal("The shop needs a new sign.", projection.Reason);
        Assert.Equal(2, projection.Steps.Count);
        Assert.Equal("Fulfilled", projection.Steps[0].Status);
        Assert.Equal("(O)428", projection.Steps[0].RequestedItemId);
        Assert.Equal("Pending", projection.Steps[1].Status);
        Assert.Equal("What color should the sign be?", projection.Steps[1].QuestionTopic);
        Assert.False(projection.MoneyRewardClaimable);
        Assert.Equal(0, projection.RewardMoney);
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(5000, 5000)]
    [InlineData(50000, 10000)]
    public void FeatureDisabledDropsPendingButKeepsAndClampsClaimableReward(
        int rewardMoney,
        int expectedRewardMoney)
    {
        NpcHelpRequestFact pending = BuildRequest("pending", assignedPlayerId: 101);
        NpcHelpRequestFact claimable = BuildRequest("claimable", assignedPlayerId: 101);
        claimable.Status = "Fulfilled";
        claimable.RewardMoney = rewardMoney;
        claimable.RewardMoneyClaimQueued = true;
        NpcHelpRequestFact alreadyGranted = BuildRequest("granted", assignedPlayerId: 101);
        alreadyGranted.Status = "Fulfilled";
        alreadyGranted.RewardMoney = 800;
        alreadyGranted.RewardMoneyClaimQueued = true;
        alreadyGranted.RewardMoneyGranted = true;

        HelpRequestQuestProjection projection = Assert.Single(
            HelpRequestQuestLogService.BuildProjection(
                new[] { BuildState("Emily", pending, claimable, alreadyGranted) },
                assignedPlayerId: 101,
                enableHelpRequests: false).Quests);

        Assert.Equal("claimable", projection.QuestLogId);
        Assert.Equal("Fulfilled", projection.Status);
        Assert.True(projection.MoneyRewardClaimable);
        Assert.Equal(expectedRewardMoney, projection.RewardMoney);
    }

    [Fact]
    public void ApplyProjectionUpsertsMarkersAndPreservesOriginalQuests()
    {
        var service = CreateService();
        var original = new Quest();
        original.modData[HelpRequestQuestLogService.HelpRequestQuestIdKey] = "pending";
        var staleProxy = BuildProxyQuest("stale");
        var questLog = new List<Quest> { original, staleProxy };
        HelpRequestQuestProjection pending = BuildProjection("pending");

        Assert.Empty(service.ApplyProjection(questLog, new[] { pending }, currentTotalDays: 100));
        Assert.Contains(original, questLog);
        Assert.DoesNotContain(staleProxy, questLog);
        Quest proxy = Assert.Single(questLog, IsProxy);
        Assert.NotSame(original, proxy);
        Assert.False(proxy.completed.Value);
        Assert.Equal(0, proxy.moneyReward.Value);

        service.ApplyProjection(questLog, new[] { pending }, currentTotalDays: 100);
        Assert.Single(questLog, IsProxy);

        service.ApplyProjection(questLog, Array.Empty<HelpRequestQuestProjection>(), currentTotalDays: 100);
        Assert.Contains(original, questLog);
        Assert.DoesNotContain(questLog, IsProxy);
    }

    [Fact]
    public void ApplyProjectionUsesTheCurrentMultiStepObjective()
    {
        var service = CreateService();
        var questLog = new List<Quest>();
        HelpRequestQuestProjection projection = BuildProjection("multi-step");
        projection.Type = "item_request";
        projection.CurrentStepIndex = 1;
        projection.Steps =
        [
            new HelpRequestQuestStepProjection
            {
                Type = "item_request",
                Summary = "Bring cloth.",
                RequestedItemLabel = "Cloth",
                Status = "Fulfilled"
            },
            new HelpRequestQuestStepProjection
            {
                Type = "question_request",
                Summary = "Choose a color.",
                QuestionTopic = "Which color?",
                Status = "Pending"
            }
        ];

        service.ApplyProjection(questLog, new[] { projection }, currentTotalDays: 100);

        Quest proxy = Assert.Single(questLog);
        Assert.Equal("help.quest.description.question", proxy.questDescription);
        Assert.Equal("help.quest.objective.question", proxy.currentObjective);
    }

    [Fact]
    public void ClaimedRewardIsReportedOnceAndNotRecreatedByAStaleProjection()
    {
        var service = CreateService();
        var questLog = new List<Quest>();
        HelpRequestQuestProjection claimable = BuildProjection("claimable");
        claimable.Status = "Fulfilled";
        claimable.RewardMoney = 50000;
        claimable.MoneyRewardClaimable = true;

        service.ApplyProjection(questLog, new[] { claimable }, currentTotalDays: 100);
        Quest proxy = Assert.Single(questLog);
        Assert.True(proxy.completed.Value);
        Assert.Equal(10000, proxy.moneyReward.Value);

        proxy.moneyReward.Value = 0;
        proxy.destroy.Value = true;

        Assert.Equal("claimable", Assert.Single(service.PollClaimedRewards(questLog)));
        Assert.Empty(service.PollClaimedRewards(questLog));

        Assert.Empty(service.ApplyProjection(questLog, new[] { claimable }, currentTotalDays: 100));
        Assert.DoesNotContain(questLog, IsProxy);
        Assert.Empty(service.PollClaimedRewards(questLog));
    }

    [Fact]
    public void MissingClaimableProxyIsReportedDuringAStaleHostRebroadcast()
    {
        var service = CreateService();
        var questLog = new List<Quest>();
        HelpRequestQuestProjection claimable = BuildProjection("removed-claimable");
        claimable.Status = "Fulfilled";
        claimable.RewardMoney = 750;
        claimable.MoneyRewardClaimable = true;

        service.ApplyProjection(questLog, new[] { claimable }, currentTotalDays: 100);
        questLog.Clear();

        IReadOnlyList<string> claimed = service.ApplyProjection(
            questLog,
            new[] { claimable },
            currentTotalDays: 100);

        Assert.Equal("removed-claimable", Assert.Single(claimed));
        Assert.Empty(questLog);
        Assert.Empty(service.PollClaimedRewards(questLog));
    }

    private static HelpRequestQuestLogService CreateService()
    {
        return new HelpRequestQuestLogService(new ModConfig(), new BehaviorMemory(), null!);
    }

    private static LivingNpcState BuildState(string npcName, params NpcHelpRequestFact[] requests)
    {
        return new LivingNpcState
        {
            NpcName = npcName,
            HelpRequests = requests.ToList()
        };
    }

    private static NpcHelpRequestFact BuildRequest(string questLogId, long assignedPlayerId)
    {
        return new NpcHelpRequestFact
        {
            AssignedPlayerId = assignedPlayerId,
            NpcDisplayName = "Emily",
            QuestLogId = questLogId,
            Type = "item_request",
            Summary = "Bring cloth.",
            Steps =
            [
                new NpcHelpRequestStepFact
                {
                    Type = "item_request",
                    Summary = "Bring cloth.",
                    RequestedItemId = "(O)428",
                    RequestedItemLabel = "Cloth",
                    Status = "Pending"
                }
            ],
            RequestedItemId = "(O)428",
            RequestedItemLabel = "Cloth",
            DueTotalDays = 105,
            Status = "Pending"
        };
    }

    private static HelpRequestQuestProjection BuildProjection(string questLogId)
    {
        return new HelpRequestQuestProjection
        {
            NpcName = "Emily",
            NpcDisplayName = "Emily",
            QuestLogId = questLogId,
            Type = "item_request",
            Summary = "Bring cloth.",
            Steps =
            [
                new HelpRequestQuestStepProjection
                {
                    Type = "item_request",
                    Summary = "Bring cloth.",
                    RequestedItemId = "(O)428",
                    RequestedItemLabel = "Cloth",
                    Status = "Pending"
                }
            ],
            RequestedItemId = "(O)428",
            RequestedItemLabel = "Cloth",
            DueTotalDays = 105,
            Status = "Pending"
        };
    }

    private static Quest BuildProxyQuest(string questLogId)
    {
        var quest = new Quest();
        quest.modData[HelpRequestQuestLogService.HelpRequestQuestMarkerKey] = "true";
        quest.modData[HelpRequestQuestLogService.HelpRequestQuestIdKey] = questLogId;
        return quest;
    }

    private static bool IsProxy(Quest quest)
    {
        return quest.modData.TryGetValue(
                HelpRequestQuestLogService.HelpRequestQuestMarkerKey,
                out string? marker)
            && marker == "true";
    }
}
