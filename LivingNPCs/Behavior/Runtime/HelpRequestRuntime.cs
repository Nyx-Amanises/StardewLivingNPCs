using System;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class HelpRequestRuntime
{
    private readonly ModConfig config;
    private readonly BehaviorMemory memory;
    private readonly Func<string, NPC?> findNpcInCurrentLocation;
    private readonly Action<NPC, string> pushInteractionContext;
    private readonly Action syncQuestLog;

    public HelpRequestRuntime(
        ModConfig config,
        BehaviorMemory memory,
        Func<string, NPC?> findNpcInCurrentLocation,
        Action<NPC, string> pushInteractionContext,
        Action syncQuestLog)
    {
        this.config = config;
        this.memory = memory;
        this.findNpcInCurrentLocation = findNpcInCurrentLocation;
        this.pushInteractionContext = pushInteractionContext;
        this.syncQuestLog = syncQuestLog;
    }

    public void UpdateTimers()
    {
        // 多人 v1：求助账本（过期裁决）只在主机推进；farmhand 的状态是主机镜像。
        if (!this.config.EnableHelpRequests || !Context.IsMainPlayer)
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
}
