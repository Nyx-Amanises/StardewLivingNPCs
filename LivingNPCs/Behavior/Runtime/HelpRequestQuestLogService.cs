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

        var pendingRequests = this.memory.GetTrackedStates()
            .SelectMany(state => state.HelpRequests
                .Where(request => request.Status == "Pending")
                .Select(request => new
                {
                    State = state,
                    Request = request
                }))
            .ToList();
        var pendingQuestIds = pendingRequests
            .Select(pair => pair.Request.QuestLogId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var proxyQuests = Game1.player.questLog
            .OfType<Quest>()
            .Where(quest => quest.modData.TryGetValue(HelpRequestQuestMarkerKey, out string? marker)
                && marker == "true")
            .ToList();

        foreach (var quest in proxyQuests)
        {
            if (!quest.modData.TryGetValue(HelpRequestQuestIdKey, out string? questId)
                || string.IsNullOrWhiteSpace(questId)
                || !pendingQuestIds.Contains(questId))
            {
                Game1.player.questLog.Remove(quest);
            }
        }

        foreach (var pair in pendingRequests)
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
        quest.questTitle = $"求助：{npcDisplayName}";
        quest.questDescription = request.Type == "item_request"
            ? $"{npcDisplayName} 请你帮忙找一件东西。{stepText}{BuildDetailText(request)}\n{due}"
            : $"{npcDisplayName} 想就一件事请教你。{stepText}{BuildDetailText(request)}\n{due}";
        quest.currentObjective = request.Type == "item_request"
            ? $"{stepText}把 {GetItemLabel(request)} 交给 {npcDisplayName}。{due}"
            : $"{stepText}和 {npcDisplayName} 继续聊聊：{GetQuestionLabel(request)}。{due}";
        // Rewards are granted directly on hand-in (vanilla item-delivery style), so the quest entry
        // shows no reward hint or claimable reward box.
        quest.moneyReward.Value = 0;
        quest.rewardDescription.Value = "-1";
    }

    private static string BuildStepProgressText(NpcHelpRequestFact request)
    {
        int totalSteps = Math.Max(1, request.Steps.Count);
        if (totalSteps <= 1)
        {
            return string.Empty;
        }

        int currentStep = Math.Clamp(request.CurrentStepIndex + 1, 1, totalSteps);
        return $"第 {currentStep}/{totalSteps} 步：";
    }

    private static string BuildDetailText(NpcHelpRequestFact request)
    {
        return request.Type == "item_request"
            ? $"需要：{GetItemLabel(request)}。"
            : $"问题：{GetQuestionLabel(request)}。";
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
            < 0 => $"已逾期 {-daysRemaining} 天。",
            0 => "今天到期。",
            1 => "明天到期。",
            _ => $"还剩 {daysRemaining} 天。"
        };
    }
}
