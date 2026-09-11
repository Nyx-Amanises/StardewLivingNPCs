namespace LivingNPCs.Dialogue.Content;

/// <summary>Stable world background and complete, dynamically selected reference entries.</summary>
internal sealed class WorldRetrievalResult
{
    public string CoreText { get; init; } = string.Empty;
    public string RetrievedText { get; init; } = string.Empty;
    public int SelectedEntryCount { get; init; }
    public int TotalEntryCount { get; init; }

    /// <summary>Empty on a normal match; otherwise describes a retrieval decision, never NPC knowledge.</summary>
    public string FallbackReason { get; init; } = string.Empty;
}
