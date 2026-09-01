using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

/// <summary>
/// Applies one parsed ValleyTalk exchange to an NPC's state and memory: long-term/preference
/// memories, help requests and their updates (plus the deterministic dialogue fallbacks),
/// conflicts, behavior influences, emotion impact, and the capped AI-dialogue friendship bonus.
/// Storage and cross-cutting effects remain owned by <see cref="BehaviorMemory"/> and arrive as
/// delegates, mirroring how <see cref="HelpRequestMemoryService"/> is wired.
/// </summary>
internal sealed class ExchangeApplicationService
{
    private readonly HelpRequestMemoryService helpRequests;
    private readonly Func<NPC, string, string, string, BehaviorMemoryEntry> createEntry;
    private readonly Action<BehaviorMemoryEntry, int> addEntry;
    private readonly Func<LivingNpcState, ValleyTalkMemoryCandidate, bool> storePlayerPreferenceMemory;
    private readonly Func<LivingNpcState, ValleyTalkMemoryCandidate, bool> storeLongTermMemory;
    private readonly Func<LivingNpcState, ValleyTalkConflictCandidate, bool> storeConflict;
    private readonly Func<NPC, LivingNpcState, ValleyTalkBehaviorInfluenceCandidate, int, bool> storeDialogueBehaviorInfluence;
    private readonly Func<LivingNpcState, ValleyTalkEmotionImpact, bool> applyDialogueEmotionImpact;
    private readonly Func<LivingNpcState, int, bool, bool, int> applyConflictRepair;
    private readonly Func<LivingNpcState, int, int, int> applyAiDialogueFriendship;
    private readonly Func<(int TotalDays, int TimeOfDay)> getGameTime;

    public ExchangeApplicationService(
        HelpRequestMemoryService helpRequests,
        Func<NPC, string, string, string, BehaviorMemoryEntry> createEntry,
        Action<BehaviorMemoryEntry, int> addEntry,
        Func<LivingNpcState, ValleyTalkMemoryCandidate, bool> storePlayerPreferenceMemory,
        Func<LivingNpcState, ValleyTalkMemoryCandidate, bool> storeLongTermMemory,
        Func<LivingNpcState, ValleyTalkConflictCandidate, bool> storeConflict,
        Func<NPC, LivingNpcState, ValleyTalkBehaviorInfluenceCandidate, int, bool> storeDialogueBehaviorInfluence,
        Func<LivingNpcState, ValleyTalkEmotionImpact, bool> applyDialogueEmotionImpact,
        Func<LivingNpcState, int, bool, bool, int> applyConflictRepair,
        Func<LivingNpcState, int, int, int> applyAiDialogueFriendship,
        Func<(int TotalDays, int TimeOfDay)>? getGameTime = null)
    {
        this.helpRequests = helpRequests;
        this.createEntry = createEntry;
        this.addEntry = addEntry;
        this.storePlayerPreferenceMemory = storePlayerPreferenceMemory;
        this.storeLongTermMemory = storeLongTermMemory;
        this.storeConflict = storeConflict;
        this.storeDialogueBehaviorInfluence = storeDialogueBehaviorInfluence;
        this.applyDialogueEmotionImpact = applyDialogueEmotionImpact;
        this.applyConflictRepair = applyConflictRepair;
        this.applyAiDialogueFriendship = applyAiDialogueFriendship;
        this.getGameTime = getGameTime ?? (() => (Game1.Date.TotalDays, Game1.timeOfDay));
    }

    /// <param name="allowHelpRequestProgress">
    /// 为 false 时求助系统整体不推进（不存新请求、不合成、不应用更新、不因玩家肯定语接受
    /// Offered 请求）。多人 v1 里 farmhand 上报的远程交换走 false：任务栏投影与奖励只属于
    /// 主机玩家自己的会话（见 Multiplayer/RemoteExchangePolicy）。
    /// </param>
    public ValleyTalkExchangeResult Apply(
        NPC npc,
        LivingNpcState state,
        string playerText,
        string npcResponse,
        string analysisJson,
        int maxEntriesPerNpc,
        int maxPendingHelpRequestsPerNpc,
        int helpRequestCooldownDays,
        int maxExtraFriendshipPerDay,
        int maxDialogueBehaviorInfluenceDays,
        bool allowHelpRequestProgress = true,
        long helpRequestPlayerId = -1,
        string helpRequestPlayerName = "",
        int helpRequestFriendshipHearts = -1)
    {
        (int totalDays, int timeOfDay) = this.getGameTime();
        var analysis = ValleyTalkExchangeParser.Parse(analysisJson);
        NicknamePreferenceService.TryUpdateStateFromDialogue(
            state,
            playerText,
            npcResponse,
            totalDays,
            timeOfDay);
        var pendingHelpRequestIdsBefore = state.HelpRequests
            .Where(request => request.Status == "Pending")
            .Select(request => request.QuestLogId)
            .ToHashSet(StringComparer.Ordinal);
        int storedMemories = 0;
        int storedPlayerPreferences = 0;
        int storedHelpRequests = 0;
        int updatedHelpRequests = 0;
        int storedConflicts = 0;
        int storedBehaviorInfluences = 0;
        int resolvedConflicts = 0;
        bool emotionChanged = false;
        var fulfilledHelpRequests = new List<NpcHelpRequestFact>();

        foreach (var candidate in analysis.Memories
                     .Where(memory => memory.Importance >= 40 && !string.IsNullOrWhiteSpace(memory.Summary))
                     .OrderByDescending(memory => memory.Importance)
                     .Take(4))
        {
            if (candidate.PlayerPreference && this.storePlayerPreferenceMemory(state, candidate))
            {
                storedPlayerPreferences++;
                var entry = this.createEntry(
                    npc,
                    "PlayerPreferenceMemory",
                    candidate.PlayerPreferenceKind,
                    candidate.Summary
                );
                this.addEntry(entry, maxEntriesPerNpc);
                continue;
            }

            if (this.storeLongTermMemory(state, candidate))
            {
                storedMemories++;
                var entry = this.createEntry(
                    npc,
                    "LongTermMemory",
                    candidate.Kind,
                    candidate.Summary
                );
                this.addEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.HelpRequests
                     .Where(request =>
                         allowHelpRequestProgress
                         && maxPendingHelpRequestsPerNpc > 0
                         && !string.IsNullOrWhiteSpace(request.Summary)
                         && BehaviorValueNormalizer.NormalizeHelpRequestType(request.Type) == "item_request"
                         && this.helpRequests.IsCandidateConsistentWithVisibleDialogue(
                             npc,
                             request,
                             npcResponse))
                     .Take(1))
        {
            if (this.helpRequests.Store(
                    npc,
                    state,
                    candidate,
                    playerText,
                    maxPendingHelpRequestsPerNpc,
                    helpRequestCooldownDays,
                    helpRequestPlayerId,
                    helpRequestPlayerName,
                    helpRequestFriendshipHearts))
            {
                storedHelpRequests++;
                var entry = this.createEntry(
                    npc,
                    "HelpRequest",
                    candidate.Type,
                    candidate.Summary
                );
                this.addEntry(entry, maxEntriesPerNpc);
            }
        }

        // Creation safety net: if the AI mentioned an item favor in the visible reply but omitted
        // the structured helpRequests field, synthesize it from the dialogue (still gated by Store).
        if (allowHelpRequestProgress
            && storedHelpRequests == 0
            && this.helpRequests.TrySynthesizeItemRequestFromDialogue(
                npc,
                state,
                playerText,
                npcResponse,
                maxPendingHelpRequestsPerNpc,
                helpRequestCooldownDays,
                helpRequestPlayerId,
                helpRequestPlayerName,
                helpRequestFriendshipHearts))
        {
            storedHelpRequests++;
            this.addEntry(
                this.createEntry(npc, "HelpRequest", "item_request", "the visible conversation raised an item favor"),
                maxEntriesPerNpc
            );
        }

        foreach (var candidate in analysis.HelpRequestUpdates
                     .Where(update => allowHelpRequestProgress && !string.IsNullOrWhiteSpace(update.Summary))
                     .Take(2))
        {
            if (this.helpRequests.ApplyUpdate(
                    state,
                    candidate,
                    playerText,
                    out NpcHelpRequestFact? fulfilledRequest,
                    helpRequestPlayerId))
            {
                updatedHelpRequests++;
                if (fulfilledRequest != null)
                {
                    fulfilledHelpRequests.Add(fulfilledRequest);
                }
                var entry = this.createEntry(
                    npc,
                    "HelpRequestUpdate",
                    candidate.Status,
                    string.IsNullOrWhiteSpace(candidate.Resolution) ? candidate.Summary : candidate.Resolution
                );
                this.addEntry(entry, maxEntriesPerNpc);
            }
        }

        // Deterministic fallback: if the farmer's reply clearly agrees to help and an offered
        // request is still waiting, accept it even when the AI did not emit an accepted update.
        if (allowHelpRequestProgress
            && this.helpRequests.TryAcceptOfferedFromPlayerAffirmation(state, playerText, helpRequestPlayerId))
        {
            updatedHelpRequests++;
            this.addEntry(
                this.createEntry(npc, "HelpRequestUpdate", "accepted", "the farmer agreed to help"),
                maxEntriesPerNpc
            );
        }

        foreach (var candidate in analysis.Conflicts
                     .Where(conflict => !string.IsNullOrWhiteSpace(conflict.Summary) && conflict.Severity > 0)
                     .Take(2))
        {
            if (this.storeConflict(state, candidate))
            {
                storedConflicts++;
                var entry = this.createEntry(
                    npc,
                    "Conflict",
                    candidate.CauseKind,
                    candidate.Summary
                );
                this.addEntry(entry, maxEntriesPerNpc);
            }
        }

        foreach (var candidate in analysis.BehaviorInfluences
                     .Where(influence => !string.IsNullOrWhiteSpace(influence.Summary))
                     .Take(2))
        {
            if (this.storeDialogueBehaviorInfluence(npc, state, candidate, maxDialogueBehaviorInfluenceDays))
            {
                storedBehaviorInfluences++;
                var entry = this.createEntry(
                    npc,
                    "DialogueBehavior",
                    candidate.Type,
                    candidate.Summary
                );
                this.addEntry(entry, maxEntriesPerNpc);
            }
        }

        if (analysis.EmotionImpact.HasEffect)
        {
            emotionChanged = this.applyDialogueEmotionImpact(state, analysis.EmotionImpact);
            resolvedConflicts += this.applyConflictRepair(
                state,
                analysis.EmotionImpact.RepairDelta,
                analysis.EmotionImpact.Apology,
                analysis.EmotionImpact.RepairDelta > 0
            );
        }

        // Keep one deterministic fallback for nickname requests if the model forgets metadata.
        if (storedMemories == 0
            && storedPlayerPreferences == 0
            && storedHelpRequests == 0
            && updatedHelpRequests == 0
            && storedConflicts == 0
            && storedBehaviorInfluences == 0
            && NicknamePreferenceService.TryCreateFallbackMemory(playerText, npcResponse, out var fallbackMemory))
        {
            if (this.storeLongTermMemory(state, fallbackMemory))
            {
                storedMemories++;
                var entry = this.createEntry(npc, "LongTermMemory", "preference", fallbackMemory.Summary);
                this.addEntry(entry, maxEntriesPerNpc);
            }
        }

        int appliedFriendship = this.applyAiDialogueFriendship(state, analysis.RapportDelta, maxExtraFriendshipPerDay);
        if (storedMemories > 0
            || storedPlayerPreferences > 0
            || storedHelpRequests > 0
            || updatedHelpRequests > 0
            || storedConflicts > 0
            || storedBehaviorInfluences > 0
            || resolvedConflicts > 0
            || emotionChanged
            || appliedFriendship > 0)
        {
            if (storedMemories > 0 || storedPlayerPreferences > 0 || storedHelpRequests > 0)
            {
                state.LastInteraction = "the farmer shared something worth remembering";
            }
            else if (updatedHelpRequests > 0)
            {
                state.LastInteraction = "they updated a personal help request with the farmer";
            }
            else if (storedConflicts > 0)
            {
                state.LastInteraction = "the farmer caused interpersonal friction";
            }
            else if (storedBehaviorInfluences > 0)
            {
                state.LastInteraction = "the latest conversation changed how they may behave around the farmer";
            }
            else if (resolvedConflicts > 0)
            {
                state.LastInteraction = "the farmer helped repair a conflict";
            }
            else
            {
                state.LastInteraction = "the farmer had an AI conversation";
            }

            state.LastUpdatedTotalDays = totalDays;
            state.LastUpdatedTimeOfDay = timeOfDay;
        }

        var activatedHelpRequests = state.HelpRequests
            .Where(request => request.Status == "Pending"
                && !pendingHelpRequestIdsBefore.Contains(request.QuestLogId))
            .ToList();

        return new ValleyTalkExchangeResult(
            storedMemories,
            storedPlayerPreferences,
            storedHelpRequests,
            updatedHelpRequests,
            storedConflicts,
            storedBehaviorInfluences,
            resolvedConflicts,
            emotionChanged,
            appliedFriendship,
            analysis.RapportDelta,
            analysis.EndConversation,
            analysis.AmbientFollowUp.Text,
            analysis.AmbientFollowUp.DelayMinutes,
            analysis.Actions,
            fulfilledHelpRequests,
            activatedHelpRequests
        );
    }
}
