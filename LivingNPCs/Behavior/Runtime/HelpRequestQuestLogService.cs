using System;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Quests;

namespace LivingNPCs.Behavior;

internal sealed class HelpRequestQuestLogService
{
    private const string HelpRequestQuestMarkerKey = "LivingNPCs/HelpRequestQuest";
    private const string HelpRequestQuestIdKey = "LivingNPCs/HelpRequestQuestId";

    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly BehaviorMailService mailService;

    public HelpRequestQuestLogService(ModConfig config, BehaviorMemory memory, BehaviorMailService mailService)
    {
        this.config = config;
        this.memory = memory;
        this.mailService = mailService;
    }

    public void Sync()
    {
        if (!Context.IsWorldReady || Game1.player?.questLog == null)
        {
            return;
        }

        var trackedRequests = this.memory.GetTrackedStates()
            .SelectMany(state => state.HelpRequests
                .Where(request => request.Status == "Pending" || IsClaimableMoneyRequest(request))
                .Select(request => new
                {
                    State = state,
                    Request = request
                }))
            .ToList();
        var proxyQuests = Game1.player.questLog
            .OfType<Quest>()
            .Where(quest => quest.modData.TryGetValue(HelpRequestQuestMarkerKey, out string? marker)
                && marker == "true")
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

    private static bool IsClaimableMoneyRequest(NpcHelpRequestFact request)
    {
        return request.Status == "Fulfilled"
            && request.RewardMoneyClaimQueued
            && request.RewardMoney > 0
            && !request.RewardMoneyGranted;
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
        int daysRemaining = request.DueTotalDays - Game1.Date.TotalDays;
        return daysRemaining switch
        {
            < 0 => I18n.Get("help.quest.due.overdue", new { days = -daysRemaining }),
            0 => I18n.Get("help.quest.due.today"),
            1 => I18n.Get("help.quest.due.tomorrow"),
            _ => I18n.Get("help.quest.due.remaining", new { days = daysRemaining })
        };
    }
}
