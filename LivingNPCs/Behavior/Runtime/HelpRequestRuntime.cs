using System;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class HelpRequestRuntime
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly Func<string, NPC?> findNpcInCurrentLocation;
    private readonly Func<NPC, string, bool> tryShowNpcSpeechBubble;
    private readonly Action<NPC, string> pushInteractionContext;
    private readonly Action syncQuestLog;

    public HelpRequestRuntime(
        ModConfig config,
        BehaviorMemory memory,
        Func<string, NPC?> findNpcInCurrentLocation,
        Func<NPC, string, bool> tryShowNpcSpeechBubble,
        Action<NPC, string> pushInteractionContext,
        Action syncQuestLog)
    {
        this.config = config;
        this.memory = memory;
        this.findNpcInCurrentLocation = findNpcInCurrentLocation;
        this.tryShowNpcSpeechBubble = tryShowNpcSpeechBubble;
        this.pushInteractionContext = pushInteractionContext;
        this.syncQuestLog = syncQuestLog;
    }

    public void UpdateTimers()
    {
        if (!this.config.EnableHelpRequests)
        {
            return;
        }

        bool changed = false;
        foreach (var state in this.memory.GetTrackedStates())
        {
            foreach (var request in state.HelpRequests.Where(request => request.Status is "Offered" or "Pending"))
            {
                if (request.DueTotalDays >= Game1.Date.TotalDays)
                {
                    continue;
                }

                request.Status = "Expired";
                request.LastUpdatedTotalDays = Game1.Date.TotalDays;
                request.LastUpdatedTimeOfDay = Game1.timeOfDay;
                changed = true;
                this.memory.UpdateStateForExpiredHelpRequest(state, request);

                NPC? npc = this.findNpcInCurrentLocation(state.NpcName);
                if (npc == null)
                {
                    continue;
                }

                this.memory.RecordNpcWorldAction(
                    npc,
                    "ExpiredHelpRequest",
                    $"a personal help request went unanswered: {request.Summary}",
                    this.config.MaxMemoryEntriesPerNpc
                );
                this.pushInteractionContext(npc, $"Expired help request for {npc.Name}: {request.Summary}.");
            }
        }

        if (changed)
        {
            this.syncQuestLog();
        }
    }

    public void ShowFollowUps()
    {
        if (!this.config.EnableDialogueFollowUps
            || Game1.currentLocation == null
            || Game1.player == null
            || Game1.activeClickableMenu != null
            || Game1.eventUp)
        {
            return;
        }

        foreach (var npc in Game1.currentLocation.characters.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Name)))
        {
            var state = this.memory.GetState(npc);
            if (state == null
                || Vector2.Distance(npc.Tile, Game1.player.Tile) > this.config.MaxInteractionDistanceTiles)
            {
                continue;
            }

            var request = state.HelpRequests.FirstOrDefault(candidate =>
                candidate.Status == "Fulfilled"
                && candidate.SpecialFollowUpPlanned
                && candidate.FollowUpEligibleTotalDays <= Game1.Date.TotalDays
                && candidate.FollowUpShownTotalDays < 0
                && candidate.FulfilledTotalDays >= Game1.Date.TotalDays - 7
            );
            if (request == null)
            {
                continue;
            }

            if (!this.tryShowNpcSpeechBubble(npc, BuildHelpRequestFollowUp(request)))
            {
                continue;
            }

            request.FollowUpShownTotalDays = Game1.Date.TotalDays;
            request.FollowUpShownTimeOfDay = Game1.timeOfDay;
        }
    }

    private static string BuildHelpRequestFollowUp(NpcHelpRequestFact request)
    {
        if (request.RewardMoneyByMail)
        {
            return request.Type == "question_request"
                ? "上次那件事我后来又想了想。信应该也送到了吧？"
                : "上次你带来的东西很有用，信应该也送到了吧？";
        }

        if (request.RewardMoney >= 1000)
        {
            return request.Type == "question_request"
                ? "上次那件事真的帮我理清了不少。"
                : "上次你带来的东西，真的解了我的急。";
        }

        return request.Type switch
        {
            "question_request" => "上次你说的那些，我后来还想了想。",
            _ => "上次你带来的东西，正好派上了用场。"
        };
    }
}
