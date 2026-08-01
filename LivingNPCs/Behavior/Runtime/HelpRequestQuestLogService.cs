using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Behavior.Multiplayer;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Quests;

namespace LivingNPCs.Behavior;

internal sealed class HelpRequestQuestLogService
{
    internal const string HelpRequestQuestMarkerKey = "LivingNPCs/HelpRequestQuest";
    internal const string HelpRequestQuestIdKey = "LivingNPCs/HelpRequestQuestId";

    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorMailService mailService;
    private readonly HashSet<string> postedClaimableQuestIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> reportedClaimedQuestIds = new(StringComparer.Ordinal);
    private string localProjectionSessionKey = string.Empty;

    public HelpRequestQuestLogService(ModConfig config, BehaviorMemory memory, BehaviorMailService mailService)
    {
        this.config = config;
        this.memory = memory;
        this.mailService = mailService;
    }

    /// <summary>主机按玩家装配其当前求助任务的完整纯数据投影。</summary>
    public QuestProjectionMessage BuildProjection(long assignedPlayerId)
    {
        return BuildProjection(
            this.memory.GetTrackedStates(),
            assignedPlayerId,
            this.config.EnableHelpRequests);
    }

    /// <summary>纯装配路径：不读取游戏对象，也不修改权威记忆。</summary>
    internal static QuestProjectionMessage BuildProjection(
        IEnumerable<LivingNpcState> states,
        long assignedPlayerId,
        bool enableHelpRequests)
    {
        var projections = new List<HelpRequestQuestProjection>();
        foreach (LivingNpcState state in states)
        {
            foreach (NpcHelpRequestFact request in state.HelpRequests)
            {
                bool belongsToPlayer = assignedPlayerId < 0
                    ? request.AssignedPlayerId < 0
                    : request.AssignedPlayerId == assignedPlayerId;
                bool claimable = IsClaimableMoneyRequest(request);
                if (!belongsToPlayer
                    || string.IsNullOrWhiteSpace(request.QuestLogId)
                    || (!(enableHelpRequests && request.Status == "Pending") && !claimable))
                {
                    continue;
                }

                projections.Add(new HelpRequestQuestProjection
                {
                    NpcName = state.NpcName,
                    NpcDisplayName = string.IsNullOrWhiteSpace(request.NpcDisplayName)
                        ? state.NpcName
                        : request.NpcDisplayName,
                    QuestLogId = request.QuestLogId,
                    Type = request.Type,
                    Summary = request.Summary,
                    Steps = request.Steps
                        .Select(step => new HelpRequestQuestStepProjection
                        {
                            Type = step.Type,
                            Summary = step.Summary,
                            RequestedItemId = step.RequestedItemId,
                            RequestedItemLabel = step.RequestedItemLabel,
                            QuestionTopic = step.QuestionTopic,
                            Status = step.Status
                        })
                        .ToList(),
                    CurrentStepIndex = request.CurrentStepIndex,
                    RequestedItemId = request.RequestedItemId,
                    RequestedItemLabel = request.RequestedItemLabel,
                    QuestionTopic = request.QuestionTopic,
                    DueTotalDays = request.DueTotalDays,
                    Reason = request.Reason,
                    Status = request.Status,
                    RewardMoney = claimable
                        ? Math.Clamp(request.RewardMoney, 200, 10000)
                        : 0,
                    MoneyRewardClaimable = claimable
                });
            }
        }

        return new QuestProjectionMessage
        {
            Quests = projections
                .OrderBy(projection => projection.DueTotalDays)
                .ThenBy(projection => projection.NpcName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(projection => projection.QuestLogId, StringComparer.Ordinal)
                .ToList()
        };
    }

    /// <summary>farmhand：把主机完整投影同步到本地任务栏，并返回本轮检测到的领取 ID。</summary>
    public IReadOnlyList<string> ApplyProjection(HelpRequestQuestProjectionStore store)
    {
        if (!Context.IsWorldReady || Game1.player?.questLog == null)
        {
            return Array.Empty<string>();
        }

        this.EnsureLocalProjectionSession();
        return this.ApplyProjection(
            Game1.player.questLog,
            store.Export(),
            Game1.Date.TotalDays);
    }

    /// <summary>farmhand：轮询本地可领钱代理是否已被原版任务页领取。</summary>
    public IReadOnlyList<string> PollClaimedRewards()
    {
        if (!Context.IsWorldReady || Game1.player?.questLog == null)
        {
            return Array.Empty<string>();
        }

        this.EnsureLocalProjectionSession();
        return this.PollClaimedRewards(Game1.player.questLog);
    }

    internal IReadOnlyList<string> ApplyProjection(
        IList<Quest> questLog,
        IEnumerable<HelpRequestQuestProjection> projections,
        int currentTotalDays)
    {
        Dictionary<string, HelpRequestQuestProjection> projected = projections
            .Where(projection => projection != null
                && !string.IsNullOrWhiteSpace(projection.QuestLogId)
                && (projection.Status == "Pending" || IsClaimableMoneyProjection(projection)))
            .GroupBy(projection => projection.QuestLogId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToDictionary(projection => projection.QuestLogId, StringComparer.Ordinal);
        var projectedClaimableIds = projected.Values
            .Where(IsClaimableMoneyProjection)
            .Select(projection => projection.QuestLogId)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<string> claimedQuestIds = this.PollClaimedRewards(
            questLog,
            projectedClaimableIds);
        Dictionary<string, HelpRequestQuestProjection> desired = projected.Values
            // A claim notification may race a host rebroadcast. Never recreate a locally claimed
            // reward while the host is still processing that notification.
            .Where(projection => !IsClaimableMoneyProjection(projection)
                || !this.reportedClaimedQuestIds.Contains(projection.QuestLogId))
            .ToDictionary(projection => projection.QuestLogId, StringComparer.Ordinal);

        var existingById = new Dictionary<string, Quest>(StringComparer.Ordinal);
        foreach (Quest quest in questLog.Where(IsHelpRequestProxy).ToList())
        {
            if (!TryGetProxyQuestId(quest, out string questId)
                || !desired.ContainsKey(questId)
                || existingById.ContainsKey(questId))
            {
                questLog.Remove(quest);
                continue;
            }

            existingById[questId] = quest;
        }

        var activeClaimableIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (HelpRequestQuestProjection projection in desired.Values)
        {
            if (!existingById.TryGetValue(projection.QuestLogId, out Quest? quest))
            {
                quest = new Quest();
                quest.accept();
                quest.modData[HelpRequestQuestMarkerKey] = "true";
                quest.modData[HelpRequestQuestIdKey] = projection.QuestLogId;
                questLog.Add(quest);
                existingById[projection.QuestLogId] = quest;
            }

            UpdateQuestText(quest, projection, currentTotalDays);
            if (IsClaimableMoneyProjection(projection))
            {
                activeClaimableIds.Add(projection.QuestLogId);
                this.postedClaimableQuestIds.Add(projection.QuestLogId);
            }
        }

        this.postedClaimableQuestIds.RemoveWhere(questId => !activeClaimableIds.Contains(questId));
        return claimedQuestIds;
    }

    internal IReadOnlyList<string> PollClaimedRewards(IList<Quest> questLog)
    {
        return this.PollClaimedRewards(questLog, eligibleQuestIds: null);
    }

    private IReadOnlyList<string> PollClaimedRewards(
        IList<Quest> questLog,
        IReadOnlySet<string>? eligibleQuestIds)
    {
        var proxyQuestsById = questLog
            .Where(IsHelpRequestProxy)
            .Select(quest => new
            {
                Quest = quest,
                QuestId = TryGetProxyQuestId(quest, out string questId) ? questId : string.Empty
            })
            .Where(pair => !string.IsNullOrWhiteSpace(pair.QuestId))
            .GroupBy(pair => pair.QuestId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.Quest).ToList(),
                StringComparer.Ordinal);
        var claimed = new List<string>();
        foreach (string questId in this.postedClaimableQuestIds.OrderBy(id => id, StringComparer.Ordinal).ToList())
        {
            if (this.reportedClaimedQuestIds.Contains(questId)
                || (eligibleQuestIds != null && !eligibleQuestIds.Contains(questId)))
            {
                continue;
            }

            bool stillClaimable = proxyQuestsById.TryGetValue(questId, out List<Quest>? matches)
                && matches.Any(quest => !quest.destroy.Value && quest.moneyReward.Value > 0);
            if (stillClaimable)
            {
                continue;
            }

            this.postedClaimableQuestIds.Remove(questId);
            this.reportedClaimedQuestIds.Add(questId);
            claimed.Add(questId);
        }

        return claimed;
    }

    private void EnsureLocalProjectionSession()
    {
        string sessionKey = $"{Game1.uniqueIDForThisGame}:{Game1.player.UniqueMultiplayerID}";
        if (string.Equals(this.localProjectionSessionKey, sessionKey, StringComparison.Ordinal))
        {
            return;
        }

        this.localProjectionSessionKey = sessionKey;
        this.postedClaimableQuestIds.Clear();
        this.reportedClaimedQuestIds.Clear();
    }

    public void Sync()
    {
        if (!Context.IsWorldReady || Game1.player?.questLog == null)
        {
            return;
        }

        // 多人 v1：这里仅维护主机玩家自己的代理；分配给 farmhand 的求助由主机按 owner
        // 装配纯数据投影，再由对应 farmhand 写入自己的本地任务栏。
        if (!Context.IsMainPlayer)
        {
            return;
        }

        var trackedRequests = this.memory.GetTrackedStates()
            .SelectMany(state => state.HelpRequests
                // With the feature turned off mid-save, stop tracking Pending proxies (they could
                // never be delivered) but keep already-earned money rewards claimable.
                .Where(request => request.AssignedPlayerId < 0
                    && ((this.config.EnableHelpRequests && request.Status == "Pending")
                        || IsClaimableMoneyRequest(request)))
                .Select(request => new
                {
                    State = state,
                    Request = request
                }))
            .ToList();
        var proxyQuests = Game1.player.questLog
            .OfType<Quest>()
            .Where(IsHelpRequestProxy)
            .ToList();
        var existingProxyQuestIds = proxyQuests
            .Select(quest => quest.modData.TryGetValue(HelpRequestQuestIdKey, out string? questId)
                ? questId
                : string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var pair in trackedRequests.Where(pair => IsClaimedMoneyReward(pair.Request, existingProxyQuestIds)))
        {
            MarkMoneyRewardClaimed(pair.Request);
        }

        trackedRequests = trackedRequests
            .Where(pair => pair.Request.Status == "Pending" || IsClaimableMoneyRequest(pair.Request))
            .ToList();
        var activeQuestIds = trackedRequests
            .Select(pair => pair.Request.QuestLogId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var quest in proxyQuests)
        {
            if (!quest.modData.TryGetValue(HelpRequestQuestIdKey, out string? questId)
                || string.IsNullOrWhiteSpace(questId)
                || !activeQuestIds.Contains(questId))
            {
                Game1.player.questLog.Remove(quest);
            }
        }

        foreach (var pair in trackedRequests)
        {
            Quest? quest = Game1.player.questLog
                .OfType<Quest>()
                .Where(IsHelpRequestProxy)
                .FirstOrDefault(candidate =>
                    candidate.modData.TryGetValue(HelpRequestQuestIdKey, out string? questId)
                    && questId == pair.Request.QuestLogId);
            if (quest == null)
            {
                quest = new Quest();
                quest.accept();
                quest.modData[HelpRequestQuestMarkerKey] = "true";
                quest.modData[HelpRequestQuestIdKey] = pair.Request.QuestLogId;
                Game1.player.questLog.Add(quest);
            }

            this.UpdateQuestText(quest, pair.State, pair.Request);
        }
    }

    private void UpdateQuestText(Quest quest, LivingNpcState state, NpcHelpRequestFact request)
    {
        string npcDisplayName = string.IsNullOrWhiteSpace(request.NpcDisplayName)
            ? state.NpcName
            : request.NpcDisplayName;
        string due = BuildDueText(request);
        string stepText = BuildStepProgressText(request);
        var tokens = new
        {
            npc = npcDisplayName,
            step = stepText,
            detail = BuildDetailText(request),
            due,
            item = GetItemLabel(request),
            question = GetQuestionLabel(request)
        };
        quest.questTitle = I18n.Get("help.quest.title", tokens);
        quest.questDescription = request.Type == "item_request"
            ? I18n.Get("help.quest.description.item", tokens)
            : I18n.Get("help.quest.description.question", tokens);
        quest.currentObjective = request.Type == "item_request"
            ? I18n.Get("help.quest.objective.item", tokens)
            : I18n.Get("help.quest.objective.question", tokens);
        bool claimable = IsClaimableMoneyRequest(request);
        quest.completed.Value = claimable;
        quest.moneyReward.Value = claimable
            ? Math.Clamp(request.RewardMoney <= 0 ? 200 : request.RewardMoney, 200, 10000)
            : 0;
        quest.rewardDescription.Value = "-1";
        if (claimable)
        {
            request.RewardMoney = quest.moneyReward.Value;
            request.RewardMoneyQuestPosted = true;
        }
    }

    private static void UpdateQuestText(
        Quest quest,
        HelpRequestQuestProjection projection,
        int currentTotalDays)
    {
        HelpRequestQuestStepProjection? currentStep = projection.Steps.Count == 0
            ? null
            : projection.Steps[Math.Clamp(projection.CurrentStepIndex, 0, projection.Steps.Count - 1)];
        string type = string.IsNullOrWhiteSpace(currentStep?.Type)
            ? projection.Type
            : currentStep.Type;
        string itemLabel = !string.IsNullOrWhiteSpace(currentStep?.RequestedItemLabel)
            ? currentStep.RequestedItemLabel
            : (!string.IsNullOrWhiteSpace(projection.RequestedItemLabel)
                ? projection.RequestedItemLabel
                : projection.Summary);
        string questionLabel = !string.IsNullOrWhiteSpace(currentStep?.QuestionTopic)
            ? currentStep.QuestionTopic
            : (!string.IsNullOrWhiteSpace(projection.QuestionTopic)
                ? projection.QuestionTopic
                : projection.Summary);
        string stepText = BuildStepProgressText(projection);
        var tokens = new
        {
            npc = string.IsNullOrWhiteSpace(projection.NpcDisplayName)
                ? projection.NpcName
                : projection.NpcDisplayName,
            step = stepText,
            detail = type == "item_request"
                ? I18n.Get("help.quest.detail.item", new { item = itemLabel })
                : I18n.Get("help.quest.detail.question", new { question = questionLabel }),
            due = BuildDueText(projection.DueTotalDays, currentTotalDays),
            item = itemLabel,
            question = questionLabel
        };
        quest.questTitle = I18n.Get("help.quest.title", tokens);
        quest.questDescription = type == "item_request"
            ? I18n.Get("help.quest.description.item", tokens)
            : I18n.Get("help.quest.description.question", tokens);
        quest.currentObjective = type == "item_request"
            ? I18n.Get("help.quest.objective.item", tokens)
            : I18n.Get("help.quest.objective.question", tokens);
        bool claimable = IsClaimableMoneyProjection(projection);
        quest.destroy.Value = false;
        quest.completed.Value = claimable;
        quest.moneyReward.Value = claimable
            ? Math.Clamp(projection.RewardMoney, 200, 10000)
            : 0;
        quest.rewardDescription.Value = "-1";
    }

    private static bool IsClaimableMoneyRequest(NpcHelpRequestFact request)
    {
        return request.Status == "Fulfilled"
            && request.RewardMoneyClaimQueued
            && request.RewardMoney > 0
            && !request.RewardMoneyGranted;
    }

    private static bool IsClaimableMoneyProjection(HelpRequestQuestProjection projection)
    {
        return projection.Status == "Fulfilled"
            && projection.MoneyRewardClaimable
            && projection.RewardMoney > 0;
    }

    private static bool IsClaimedMoneyReward(
        NpcHelpRequestFact request,
        System.Collections.Generic.IReadOnlySet<string> existingProxyQuestIds)
    {
        return IsClaimableMoneyRequest(request)
            && request.RewardMoneyQuestPosted
            && !string.IsNullOrWhiteSpace(request.QuestLogId)
            && !existingProxyQuestIds.Contains(request.QuestLogId);
    }

    private static void MarkMoneyRewardClaimed(NpcHelpRequestFact request)
    {
        request.RewardMoneyGranted = true;
        request.RewardMoneyClaimQueued = false;
        request.RewardMoneyQuestPosted = false;
        request.LastUpdatedTotalDays = Game1.Date.TotalDays;
        request.LastUpdatedTimeOfDay = Game1.timeOfDay;
    }

    private static string BuildStepProgressText(NpcHelpRequestFact request)
    {
        int totalSteps = Math.Max(1, request.Steps.Count);
        if (totalSteps <= 1)
        {
            return string.Empty;
        }

        int currentStep = Math.Clamp(request.CurrentStepIndex + 1, 1, totalSteps);
        return I18n.Get("help.quest.step", new { current = currentStep, total = totalSteps });
    }

    private static string BuildStepProgressText(HelpRequestQuestProjection projection)
    {
        int totalSteps = Math.Max(1, projection.Steps.Count);
        if (totalSteps <= 1)
        {
            return string.Empty;
        }

        int currentStep = Math.Clamp(projection.CurrentStepIndex + 1, 1, totalSteps);
        return I18n.Get("help.quest.step", new { current = currentStep, total = totalSteps });
    }

    private static string BuildDetailText(NpcHelpRequestFact request)
    {
        return request.Type == "item_request"
            ? I18n.Get("help.quest.detail.item", new { item = GetItemLabel(request) })
            : I18n.Get("help.quest.detail.question", new { question = GetQuestionLabel(request) });
    }

    private static string GetItemLabel(NpcHelpRequestFact request)
    {
        return string.IsNullOrWhiteSpace(request.RequestedItemLabel)
            ? request.Summary
            : request.RequestedItemLabel;
    }

    private static string GetQuestionLabel(NpcHelpRequestFact request)
    {
        return string.IsNullOrWhiteSpace(request.QuestionTopic)
            ? request.Summary
            : request.QuestionTopic;
    }

    private static string BuildDueText(NpcHelpRequestFact request)
    {
        return BuildDueText(request.DueTotalDays, Game1.Date.TotalDays);
    }

    private static string BuildDueText(int dueTotalDays, int currentTotalDays)
    {
        int daysRemaining = dueTotalDays - currentTotalDays;
        return daysRemaining switch
        {
            < 0 => I18n.Get("help.quest.due.overdue", new { days = -daysRemaining }),
            0 => I18n.Get("help.quest.due.today"),
            1 => I18n.Get("help.quest.due.tomorrow"),
            _ => I18n.Get("help.quest.due.remaining", new { days = daysRemaining })
        };
    }

    private static bool IsHelpRequestProxy(Quest quest)
    {
        return quest.modData.TryGetValue(HelpRequestQuestMarkerKey, out string? marker)
            && marker == "true";
    }

    private static bool TryGetProxyQuestId(Quest quest, out string questId)
    {
        if (quest.modData.TryGetValue(HelpRequestQuestIdKey, out string? value)
            && !string.IsNullOrWhiteSpace(value))
        {
            questId = value;
            return true;
        }

        questId = string.Empty;
        return false;
    }
}
