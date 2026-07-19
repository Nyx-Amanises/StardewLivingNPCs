using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;

using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Behavior;

/// <summary>
/// Turns evicted long-term memories into a biographical "relationship impression" via one LLM
/// call through the in-process dialogue engine link (WP16: true async, no more request-id
/// polling). A batch is moved from the backlog to <see cref="LivingNpcState.ImpressionInFlight"/>
/// when requested, and only cleared after a validated result is applied — a failed or lost call
/// keeps every memory queued, so model failures never lose data. Attempts are counted per
/// completed-but-failed generation; after <see cref="MaxAttempts"/> failures the batch returns to
/// the backlog and the NPC backs off for <see cref="FailureCooldownDays"/> days. If the game
/// exits while a task is in flight, the persisted in-flight batch is re-initiated at the next
/// day start. <see cref="LivingNpcState.ImpressionRequestId"/> is kept for save compatibility
/// and as a log identifier.
/// </summary>
internal sealed class MemoryImpressionService
{
    public const int TriggerBacklogCount = 8;
    public const int StaleBacklogAgeDays = 28;
    public const int MaxAttempts = 3;
    public const int FailureCooldownDays = 3;
    private const int MaxRequestsPerDay = 2;

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly DialogueEngineLink dialogueLink;

    /// <summary>NPC names with a live generation task; only touched on the main thread.</summary>
    private readonly HashSet<string> generationsInFlight = new(StringComparer.OrdinalIgnoreCase);

    public MemoryImpressionService(ModConfig config, IMonitor monitor, BehaviorMemory memory, DialogueEngineLink dialogueLink)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.dialogueLink = dialogueLink;
    }

    private bool Enabled => this.config.EnableMemoryImpressions && this.dialogueLink != null && this.dialogueLink.IsConnected;

    /// <summary>Day-start pass: re-initiate orphaned in-flight batches (lost tasks) and start new ones.</summary>
    public void ProcessDayStart()
    {
        if (!this.Enabled)
        {
            return;
        }

        int today = Game1.Date.TotalDays;
        int started = 0;
        foreach (LivingNpcState state in this.memory.GetTrackedStates().ToList())
        {
            if (RsvAiPolicy.IsBlockedNpcName(state.NpcName))
            {
                continue;
            }

            if (HasInFlightRequest(state))
            {
                // A live task will apply or fail on its own; an orphaned batch (game restarted
                // mid-generation) is re-sent with its attempt count intact.
                if (!this.generationsInFlight.Contains(state.NpcName))
                {
                    if (state.ImpressionRequestAttempts >= MaxAttempts)
                    {
                        FailRequest(state, today);
                        this.monitor.Log(I18n.Get("log.impression.gaveUp", new { npc = state.NpcName }), LogLevel.Trace);
                    }
                    else
                    {
                        this.SendRequest(state, today);
                    }
                }

                continue;
            }

            if (started < MaxRequestsPerDay && ShouldRequest(state, today))
            {
                this.StartRequest(state, today);
                started++;
            }
        }
    }

    internal static bool HasInFlightRequest(LivingNpcState state)
    {
        return !string.IsNullOrWhiteSpace(state.ImpressionRequestId)
            && state.ImpressionInFlight is { Count: > 0 };
    }

    /// <summary>
    /// A compression is worth a model call once enough memories piled up, or once a small backlog
    /// has been sitting for a whole season; failed requests back off for a few days first.
    /// </summary>
    internal static bool ShouldRequest(LivingNpcState state, int today)
    {
        List<LongTermMemoryFact> backlog = state.ImpressionBacklog ?? new List<LongTermMemoryFact>();
        if (backlog.Count == 0)
        {
            return false;
        }

        if (state.LastImpressionFailureTotalDays >= 0
            && today - state.LastImpressionFailureTotalDays < FailureCooldownDays)
        {
            return false;
        }

        if (backlog.Count >= TriggerBacklogCount)
        {
            return true;
        }

        int oldestCreated = backlog
            .Where(memoryFact => memoryFact.CreatedTotalDays >= 0)
            .Select(memoryFact => memoryFact.CreatedTotalDays)
            .DefaultIfEmpty(today)
            .Min();
        return today - oldestCreated >= StaleBacklogAgeDays;
    }

    private void StartRequest(LivingNpcState state, int today)
    {
        state.ImpressionBacklog.RemoveAll(memoryFact => RsvAiPolicy.ContainsBlockedReference(memoryFact.Summary));
        List<LongTermMemoryFact> batch = state.ImpressionBacklog
            .Take(LongTermMemoryStore.MaxImpressionBatch)
            .ToList();
        if (batch.Count == 0)
        {
            return;
        }

        state.ImpressionBacklog.RemoveRange(0, batch.Count);
        state.ImpressionInFlight = batch;
        state.ImpressionRequestId = $"impression-{state.NpcName}-{Guid.NewGuid():N}";
        state.ImpressionRequestTotalDays = today;
        state.ImpressionRequestAttempts = 0;

        this.SendRequest(state, today);
    }

    /// <summary>Launches one generation task for the current in-flight batch (main thread only).</summary>
    private void SendRequest(LivingNpcState state, int today)
    {
        state.ImpressionInFlight.RemoveAll(memoryFact => RsvAiPolicy.ContainsBlockedReference(memoryFact.Summary));
        if (state.ImpressionInFlight.Count == 0)
        {
            state.ImpressionRequestId = string.Empty;
            state.ImpressionRequestTotalDays = -1;
            state.ImpressionRequestAttempts = 0;
            return;
        }

        if (!this.generationsInFlight.Add(state.NpcName))
        {
            return;
        }

        state.ImpressionRequestTotalDays = today;
        string requestId = state.ImpressionRequestId;
        MemoryImpressionRequest request = BuildRequest(state, today, this.config.MemoryImpressionTimeoutSeconds);
        _ = this.RunGenerationAsync(state.NpcName, requestId, request);
    }

    private async Task RunGenerationAsync(string npcName, string requestId, MemoryImpressionRequest request)
    {
        string? text = null;
        try
        {
            text = await this.dialogueLink.GenerateMemoryImpressionAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The link already logs; a null result is a failed attempt below.
        }

        MainThreadDispatcher.Post(() => this.ApplyGenerationResult(npcName, requestId, text));
    }

    private void ApplyGenerationResult(string npcName, string requestId, string? text)
    {
        this.generationsInFlight.Remove(npcName);
        LivingNpcState? state = this.memory.GetTrackedStates()
            .FirstOrDefault(tracked => string.Equals(tracked.NpcName, npcName, StringComparison.OrdinalIgnoreCase));
        if (state == null
            || !HasInFlightRequest(state)
            || !string.Equals(state.ImpressionRequestId, requestId, StringComparison.Ordinal))
        {
            // The batch was cleared or superseded (manual memory wipe, save switch); drop silently.
            return;
        }

        int today = Context.IsWorldReady ? Game1.Date.TotalDays : state.ImpressionRequestTotalDays;
        if (!string.IsNullOrWhiteSpace(text))
        {
            ApplyResult(state, text, today);
            this.monitor.Log(
                I18n.Get("log.impression.applied", new { npc = state.NpcName, total = state.RelationshipImpressionMemoryCount }),
                LogLevel.Trace);
            return;
        }

        state.ImpressionRequestAttempts++;
        if (state.ImpressionRequestAttempts >= MaxAttempts)
        {
            FailRequest(state, today);
            this.monitor.Log(I18n.Get("log.impression.gaveUp", new { npc = state.NpcName }), LogLevel.Trace);
            return;
        }

        // Retry the same batch right away under a fresh log id (attempt count carries over).
        state.ImpressionRequestId = $"impression-{state.NpcName}-{Guid.NewGuid():N}";
        this.SendRequest(state, today);
    }

    /// <summary>Applies a successful compression: only now is the in-flight batch discarded.</summary>
    internal static void ApplyResult(LivingNpcState state, string impressionText, int today)
    {
        state.RelationshipImpression = impressionText.Trim();
        state.RelationshipImpressionUpdatedTotalDays = today;
        state.RelationshipImpressionMemoryCount += state.ImpressionInFlight.Count;
        state.ImpressionInFlight = new List<LongTermMemoryFact>();
        state.ImpressionRequestId = string.Empty;
        state.ImpressionRequestTotalDays = -1;
        state.ImpressionRequestAttempts = 0;
        state.LastImpressionFailureTotalDays = -1;
    }

    /// <summary>Gives up on the current request: the batch returns to the backlog untouched.</summary>
    internal static void FailRequest(LivingNpcState state, int today)
    {
        state.ImpressionBacklog.InsertRange(0, state.ImpressionInFlight);
        state.ImpressionInFlight = new List<LongTermMemoryFact>();
        state.ImpressionRequestId = string.Empty;
        state.ImpressionRequestTotalDays = -1;
        state.ImpressionRequestAttempts = 0;
        state.LastImpressionFailureTotalDays = today;
    }

    internal static MemoryImpressionRequest BuildRequest(LivingNpcState state, int today, int timeoutSeconds)
    {
        string displayName = Game1.getCharacterFromName(state.NpcName)?.displayName ?? state.NpcName;
        return new MemoryImpressionRequest(
            state.NpcName,
            displayName,
            RsvAiPolicy.ContainsBlockedReference(state.RelationshipImpression)
                ? string.Empty
                : state.RelationshipImpression,
            state.ImpressionInFlight
                .Where(memoryFact => !RsvAiPolicy.ContainsBlockedReference(memoryFact.Summary))
                .Select(memoryFact => FormatMemory(memoryFact, today))
                .ToList(),
            timeoutSeconds);
    }

    private static string FormatMemory(LongTermMemoryFact memoryFact, int today)
    {
        if (memoryFact.CreatedTotalDays < 0)
        {
            return memoryFact.Summary;
        }

        int age = Math.Max(0, today - memoryFact.CreatedTotalDays);
        return $"[-{age}d] {memoryFact.Summary}";
    }
}
