using System;
using System.Collections.Generic;

namespace LivingNPCs.Dialogue.Content;

/// <summary>Pure, per-request search inputs captured before dialogue generation leaves the game thread.</summary>
internal sealed class WorldRetrievalQuery
{
    public string PlayerText { get; init; } = string.Empty;
    public string RecentDialogue { get; init; } = string.Empty;
    public string NpcName { get; init; } = string.Empty;
    public string NpcDisplayName { get; init; } = string.Empty;
    public string LocationName { get; init; } = string.Empty;
    public string LocationDisplayName { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string CurrentDestination { get; init; } = string.Empty;
    public string FestivalName { get; init; } = string.Empty;
    public IReadOnlyList<string> NearbyNpcNames { get; init; } = Array.Empty<string>();
}
