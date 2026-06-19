using System;
using System.Collections.Generic;
using System.Linq;

namespace ValleyTalk;

public enum ContextDetail
{
    None,
    Brief,
    Full
}

public enum ContextModule
{
    World,
    NpcProfile,
    GameState,
    SampleDialogue,
    EventHistory,
    DateTime,
    Weather,
    NearbyNpcs,
    Relationship,
    Farm,
    Location,
    Trinkets,
    RecentEvents,
    SpecialDates,
    Gift,
    LivingNpc,
    SpouseAction,
    Preoccupation,
    CurrentConversation
}

public sealed class ContextRoutingPlan
{
    private readonly Dictionary<ContextModule, ContextDetail> details = new();

    public static ContextRoutingPlan Full()
    {
        var plan = new ContextRoutingPlan();
        foreach (ContextModule module in Enum.GetValues(typeof(ContextModule)))
        {
            plan.Set(module, ContextDetail.Full);
        }

        return plan;
    }

    public static ContextRoutingPlan ConservativeBrief()
    {
        var plan = new ContextRoutingPlan();
        foreach (ContextModule module in Enum.GetValues(typeof(ContextModule)))
        {
            plan.Set(module, ContextDetail.Brief);
        }

        plan.Set(ContextModule.SampleDialogue, ContextDetail.None);
        plan.Set(ContextModule.EventHistory, ContextDetail.Brief);
        plan.Set(ContextModule.GameState, ContextDetail.Brief);
        plan.Set(ContextModule.Farm, ContextDetail.None);
        plan.Set(ContextModule.Trinkets, ContextDetail.None);
        plan.Set(ContextModule.Preoccupation, ContextDetail.None);
        plan.ApplyHardBoundaries();
        return plan;
    }

    public string RoutingOutcome { get; private set; } = "not-run";
    public long RoutingMilliseconds { get; private set; }
    public int RoutingTimeoutSeconds { get; private set; }

    public ContextRoutingPlan WithRoutingDiagnostics(string outcome, long milliseconds, int timeoutSeconds)
    {
        this.RoutingOutcome = outcome;
        this.RoutingMilliseconds = milliseconds;
        this.RoutingTimeoutSeconds = timeoutSeconds;
        return this;
    }

    public IReadOnlyDictionary<ContextModule, ContextDetail> Details => this.details;

    public ContextDetail Get(ContextModule module)
    {
        return this.details.TryGetValue(module, out ContextDetail detail)
            ? detail
            : ContextDetail.None;
    }

    public bool Include(ContextModule module)
    {
        return this.Get(module) != ContextDetail.None;
    }

    public bool IsFull(ContextModule module)
    {
        return this.Get(module) == ContextDetail.Full;
    }

    public void Set(ContextModule module, ContextDetail detail)
    {
        this.details[module] = detail;
    }

    public void Promote(ContextModule module, ContextDetail minimum)
    {
        if (this.Get(module) < minimum)
        {
            this.Set(module, minimum);
        }
    }

    public void ApplyHardBoundaries()
    {
        this.Promote(ContextModule.NpcProfile, ContextDetail.Brief);
        this.Promote(ContextModule.DateTime, ContextDetail.Brief);
        this.Promote(ContextModule.Location, ContextDetail.Brief);
        this.Promote(ContextModule.LivingNpc, ContextDetail.Brief);
        this.Promote(ContextModule.CurrentConversation, ContextDetail.Full);
        this.Promote(ContextModule.Relationship, ContextDetail.Brief);
    }

    public void ApplyDependencies()
    {
        if (this.Include(ContextModule.LivingNpc))
        {
            this.Promote(ContextModule.DateTime, ContextDetail.Brief);
            this.Promote(ContextModule.Location, ContextDetail.Brief);
            this.Promote(ContextModule.Relationship, ContextDetail.Brief);
        }

        if (this.Include(ContextModule.Gift))
        {
            this.Promote(ContextModule.LivingNpc, ContextDetail.Brief);
            this.Promote(ContextModule.Relationship, ContextDetail.Brief);
        }

        if (this.IsFull(ContextModule.Location))
        {
            this.Promote(ContextModule.DateTime, ContextDetail.Brief);
            this.Promote(ContextModule.Weather, ContextDetail.Brief);
        }

        if (this.Include(ContextModule.EventHistory) || this.Include(ContextModule.RecentEvents))
        {
            this.Promote(ContextModule.World, ContextDetail.Brief);
        }

        this.ApplyHardBoundaries();
    }

    public string DebugLabel()
    {
        return string.Join(
            ", ",
            this.details
                .OrderBy(pair => pair.Key.ToString())
                .Where(pair => pair.Value != ContextDetail.None)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }
}