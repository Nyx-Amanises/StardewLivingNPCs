using System;
using System.Collections.Generic;
using System.Linq;

namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>farmhand 的只读求助任务投影；每条主机消息都是该玩家当前集合的完整替换。</summary>
internal sealed class HelpRequestQuestProjectionStore
{
    private readonly Dictionary<string, HelpRequestQuestProjection> quests = new(StringComparer.Ordinal);

    public int Count => this.quests.Count;

    public void Apply(QuestProjectionMessage? message)
    {
        this.quests.Clear();
        foreach (HelpRequestQuestProjection quest in message?.Quests ?? new List<HelpRequestQuestProjection>())
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.QuestLogId))
            {
                continue;
            }

            this.quests[quest.QuestLogId] = Clone(quest);
        }
    }

    public IReadOnlyList<HelpRequestQuestProjection> Export()
    {
        return this.quests.Values.Select(Clone).ToList();
    }

    public bool TryFindPendingItem(
        string npcName,
        string itemId,
        out HelpRequestQuestProjection? projection)
    {
        HelpRequestQuestProjection? match = this.quests.Values
            .Where(quest => quest.Status == "Pending"
                && quest.Type == "item_request"
                && string.Equals(quest.NpcName, npcName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(quest.RequestedItemId, itemId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(quest => quest.DueTotalDays)
            .FirstOrDefault();
        projection = match == null ? null : Clone(match);
        return projection != null;
    }

    public void Clear()
    {
        this.quests.Clear();
    }

    private static HelpRequestQuestProjection Clone(HelpRequestQuestProjection quest)
    {
        return new HelpRequestQuestProjection
        {
            NpcName = quest.NpcName,
            NpcDisplayName = quest.NpcDisplayName,
            QuestLogId = quest.QuestLogId,
            Type = quest.Type,
            Summary = quest.Summary,
            Steps = (quest.Steps ?? new List<HelpRequestQuestStepProjection>())
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
            CurrentStepIndex = quest.CurrentStepIndex,
            RequestedItemId = quest.RequestedItemId,
            RequestedItemLabel = quest.RequestedItemLabel,
            QuestionTopic = quest.QuestionTopic,
            DueTotalDays = quest.DueTotalDays,
            Reason = quest.Reason,
            Status = quest.Status,
            RewardMoney = quest.RewardMoney,
            MoneyRewardClaimable = quest.MoneyRewardClaimable
        };
    }
}
