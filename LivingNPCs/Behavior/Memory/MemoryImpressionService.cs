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
/// Builds an early relationship impression from meaningful interactions, then refreshes it
/// when the evidence or relationship changes. Evicted memories still join the same durable
/// queue. In-flight evidence is a snapshot; live memories stay available for normal recall.
/// Only a successful result marks evidence as covered, and failed batches stay queued.
/// </summary>
internal sealed class MemoryImpressionService
{
    public const int TriggerBacklogCount = 8;
    public const int StaleBacklogAgeDays = 28;
    public const int MaxAttempts = 3;
    public const int FailureCooldownDays = 3;
    private const int MaxConcurrentRequests = 2;

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly BehaviorMemory memory;
    private readonly DialogueEngineLink dialogueLink;

    /// <summary>NPC names with a live generation task; only touched on the main thread.</summary>
    private readonly Dictionary<string, string> generationsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource sessionCancellation = new();
    private int sessionVersion;

    public event Action<string>? ImpressionUpdated;

    public MemoryImpressionService(ModConfig config, IMonitor monitor, BehaviorMemory memory, DialogueEngineLink dialogueLink)
    {
        this.config = config;
        this.monitor = monitor;
        this.memory = memory;
        this.dialogueLink = dialogueLink;
    }

    private bool Enabled => this.config.EnableMemoryImpressions && this.dialogueLink != null && this.dialogueLink.IsConnected;

    public void ProcessDayStart() => this.ProcessPending();

    /// <summary>Cancel runtime work without discarding the persisted batch needed after a reload.</summary>
    public void ResetSession()
    {
        this.sessionVersion++;
        this.generationsInFlight.Clear();
        this.sessionCancellation.Cancel();
        this.sessionCancellation.Dispose();
        this.sessionCancellation = new CancellationTokenSource();
    }

    /// <summary>Runs on day start and game-time changes, with at most two requests pending at once.</summary>
    public void ProcessPending()
    {
        if (!this.Enabled)
        {
            return;
        }

        int today = Game1.Date.TotalDays;
        foreach (LivingNpcState state in this.memory.GetTrackedStates().ToList())
        {
            if (this.generationsInFlight.Count >= MaxConcurrentRequests)
            {
                break;
            }

            if (RsvAiPolicy.IsBlockedNpcName(state.NpcName))
            {
                continue;
            }

            if (HasInFlightRequest(state))
            {
                // A live task will apply or fail on its own; an orphaned batch (game restarted
                // mid-generation) is re-sent with its attempt count intact.
                if (!this.generationsInFlight.ContainsKey(state.NpcName))
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

            if (PrepareRequest(state, today))
            {
                this.SendRequest(state, today);
            }
        }
    }

    internal static bool HasInFlightRequest(LivingNpcState state)
    {
        return !string.IsNullOrWhiteSpace(state.ImpressionRequestId)
            && state.ImpressionInFlight is { Count: > 0 };
    }

    /// <summary>
    /// A first impression needs three meaningful memories. Later changes can refresh it once
    /// per game day; unchanged facts and small daily fluctuations do not consume another call.
    /// The old backlog threshold remains as a fallback for less important archived memories.
    /// </summary>
    internal static bool ShouldRequest(LivingNpcState state, int today)
    {
        if (RsvAiPolicy.IsBlockedNpcName(state.NpcName)
            || HasInFlightRequest(state)
            || (!string.IsNullOrWhiteSpace(state.RelationshipImpression)
                && state.RelationshipImpressionUpdatedTotalDays == today))
        {
            return false;
        }

        if (state.LastImpressionFailureTotalDays >= 0
            && today - state.LastImpressionFailureTotalDays < FailureCooldownDays)
        {
            return false;
        }

        List<LongTermMemoryFact> important = MemoryImpressionSources.GetImportantMemories(state);
        var covered = GetCoveredKeys(state);
        List<LongTermMemoryFact> backlog = GetPendingBacklog(state, covered, important);
        bool hasImpression = !string.IsNullOrWhiteSpace(state.RelationshipImpression);
        if (!hasImpression
            && MemoryImpressionSources.LatestVersions(important.Concat(backlog.Where(MemoryImpressionSources.IsImportant)))
                .Count() >= MemoryImpressionSources.InitialMemoryCount)
        {
            return true;
        }

        if (hasImpression
            && (important.Any(memoryFact => !covered.Contains(MemoryImpressionSources.GetKey(memoryFact)))
                || backlog.Any(MemoryImpressionSources.IsImportant)
                || !string.Equals(state.RelationshipImpressionContext,
                    MemoryImpressionSources.GetRelationshipContext(state), StringComparison.Ordinal)))
        {
            return true;
        }

        if (backlog.Count == 0)
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

    /// <summary>Capture exactly the evidence submitted to the model, without consuming live memories.</summary>
    internal static bool PrepareRequest(LivingNpcState state, int today)
    {
        if (!ShouldRequest(state, today))
        {
            return false;
        }

        var covered = GetCoveredKeys(state);
        List<LongTermMemoryFact> important = MemoryImpressionSources.GetImportantMemories(state);
        List<LongTermMemoryFact> backlog = GetPendingBacklog(state, covered, important);
        bool firstImpression = string.IsNullOrWhiteSpace(state.RelationshipImpression);
        int liveLimit = backlog.Count > 0 ? LongTermMemoryStore.MaxImpressionBatch - 4 : LongTermMemoryStore.MaxImpressionBatch;
        var batch = MemoryImpressionSources.DistinctEligible(
                important.Where(memoryFact => firstImpression || !covered.Contains(MemoryImpressionSources.GetKey(memoryFact)))
                    .Take(liveLimit)
                    .Concat(backlog))
            .Take(LongTermMemoryStore.MaxImpressionBatch)
            .Select(LivingNpcState.CloneLongTermMemoryFact)
            .ToList();

        string relationshipContext = MemoryImpressionSources.GetRelationshipContext(state);
        if (batch.Count == 0)
        {
            // A changed trust/comfort tier, friendship level, or address preference can matter
            // even when every remembered event has already been summarized.
            batch.Add(new LongTermMemoryFact
            {
                Kind = "relationship",
                Subject = "current_relationship_state",
                Summary = relationshipContext,
                Importance = MemoryImpressionSources.ImportantMemoryThreshold,
                CreatedTotalDays = today,
                LastUpdatedTotalDays = today
            });
        }

        var selectedKeys = batch.Select(MemoryImpressionSources.GetKey).ToHashSet(StringComparer.Ordinal);
        state.ImpressionBacklog = backlog
            .Where(memoryFact => !selectedKeys.Contains(MemoryImpressionSources.GetKey(memoryFact)))
            .ToList();
        state.ImpressionInFlight = batch;
        state.ImpressionContextInFlight = relationshipContext;
        state.ImpressionRequestId = $"impression-{state.NpcName}-{Guid.NewGuid():N}";
        state.ImpressionRequestTotalDays = today;
        state.ImpressionRequestAttempts = 0;

        return true;
    }

    private static HashSet<string> GetCoveredKeys(LivingNpcState state) =>
        (state.RelationshipImpressionSourceKeys ?? new List<string>()).ToHashSet(StringComparer.Ordinal);

    private static List<LongTermMemoryFact> GetPendingBacklog(
        LivingNpcState state,
        HashSet<string> covered,
        List<LongTermMemoryFact>? important = null)
    {
        var currentVersions = (important ?? MemoryImpressionSources.GetImportantMemories(state))
            .ToDictionary(MemoryImpressionSources.GetIdentity, MemoryImpressionSources.GetKey, StringComparer.Ordinal);
        return MemoryImpressionSources.LatestVersions(state.ImpressionBacklog ?? new List<LongTermMemoryFact>())
            .Where(memoryFact => !covered.Contains(MemoryImpressionSources.GetKey(memoryFact))
                && (!currentVersions.TryGetValue(MemoryImpressionSources.GetIdentity(memoryFact), out string? currentKey)
                    || string.Equals(currentKey, MemoryImpressionSources.GetKey(memoryFact), StringComparison.Ordinal)))
            .OrderByDescending(MemoryImpressionSources.IsImportant)
            .ThenByDescending(memoryFact => memoryFact.LastUpdatedTotalDays)
            .ThenByDescending(memoryFact => memoryFact.LastUpdatedTimeOfDay)
            .ToList();
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
            state.ImpressionContextInFlight = string.Empty;
            return;
        }

        if (!this.generationsInFlight.TryAdd(state.NpcName, state.ImpressionRequestId))
        {
            return;
        }

        state.ImpressionRequestTotalDays = today;
        if (string.IsNullOrWhiteSpace(state.ImpressionContextInFlight))
        {
            // An older save may contain a pending archive batch without a context snapshot.
            state.ImpressionContextInFlight = MemoryImpressionSources.GetRelationshipContext(state);
        }
        string requestId = state.ImpressionRequestId;
        MemoryImpressionRequest request = BuildRequest(state, today, this.config.MemoryImpressionTimeoutSeconds);
        _ = this.RunGenerationAsync(state.NpcName, requestId, request, this.sessionVersion, this.sessionCancellation.Token);
    }

    private async Task RunGenerationAsync(string npcName, string requestId, MemoryImpressionRequest request, int version, CancellationToken ct)
    {
        string? text = null;
        try
        {
            text = await this.dialogueLink.GenerateMemoryImpressionAsync(request, ct).ConfigureAwait(false);
        }
        catch
        {
            // The link already logs; a null result is a failed attempt below.
        }

        if (!ct.IsCancellationRequested)
        {
            MainThreadDispatcher.Post(() => this.ApplyGenerationResult(npcName, requestId, text, version));
        }
    }

    private void ApplyGenerationResult(string npcName, string requestId, string? text, int version)
    {
        // Check the session before touching the per-NPC slot: an old save can have the same
        // NPC and persisted request id as the newly loaded save.
        if (version != this.sessionVersion
            || !this.generationsInFlight.TryGetValue(npcName, out string? activeRequestId)
            || !string.Equals(activeRequestId, requestId, StringComparison.Ordinal))
        {
            return;
        }

        this.generationsInFlight.Remove(npcName);
        if (!Context.IsWorldReady)
        {
            return;
        }
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
            this.ImpressionUpdated?.Invoke(npcName);
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

    /// <summary>Mark only the captured evidence as covered; changes made during generation stay pending.</summary>
    internal static void ApplyResult(LivingNpcState state, string impressionText, int today)
    {
        state.RelationshipImpression = impressionText.Trim();
        state.RelationshipImpressionUpdatedTotalDays = today;
        state.RelationshipImpressionMemoryCount += state.ImpressionInFlight.Count;
        state.RelationshipImpressionSourceKeys = MemoryImpressionSources.NormalizeCoveredKeys(
            (state.RelationshipImpressionSourceKeys ?? new List<string>())
                .Concat(state.ImpressionInFlight.Select(MemoryImpressionSources.GetKey)));
        state.RelationshipImpressionContext = state.ImpressionContextInFlight;
        state.ImpressionBacklog = GetPendingBacklog(state, GetCoveredKeys(state));
        state.ImpressionInFlight = new List<LongTermMemoryFact>();
        state.ImpressionContextInFlight = string.Empty;
        state.ImpressionRequestId = string.Empty;
        state.ImpressionRequestTotalDays = -1;
        state.ImpressionRequestAttempts = 0;
        state.LastImpressionFailureTotalDays = -1;
    }

    /// <summary>Gives up on the current request: the batch returns to the backlog untouched.</summary>
    internal static void FailRequest(LivingNpcState state, int today)
    {
        state.ImpressionBacklog = state.ImpressionInFlight
            .Concat(state.ImpressionBacklog)
            .DistinctBy(MemoryImpressionSources.GetKey, StringComparer.Ordinal)
            .ToList();
        state.ImpressionInFlight = new List<LongTermMemoryFact>();
        state.ImpressionContextInFlight = string.Empty;
        state.ImpressionRequestId = string.Empty;
        state.ImpressionRequestTotalDays = -1;
        state.ImpressionRequestAttempts = 0;
        state.LastImpressionFailureTotalDays = today;
    }

    internal static MemoryImpressionRequest BuildRequest(LivingNpcState state, int today, int timeoutSeconds)
    {
        string displayName = Game1.getCharacterFromName(state.NpcName)?.displayName ?? state.NpcName;
        List<string> memories = state.ImpressionInFlight
            .Where(memoryFact => !RsvAiPolicy.ContainsBlockedReference(memoryFact.Summary))
            .Select(memoryFact => FormatMemory(memoryFact, today))
            .ToList();
        if (!string.IsNullOrWhiteSpace(state.ImpressionContextInFlight))
        {
            memories.Add($"Current relationship state (supersedes older state descriptions): {state.ImpressionContextInFlight}");
        }

        return new MemoryImpressionRequest(
            state.NpcName,
            displayName,
            RsvAiPolicy.ContainsBlockedReference(state.RelationshipImpression)
                ? string.Empty
                : state.RelationshipImpression,
            memories,
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
