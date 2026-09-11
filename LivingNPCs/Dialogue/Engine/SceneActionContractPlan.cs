namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Selects the action documentation needed by a reply. These flags never authorize effects;
/// the captured permissions, metadata validation and runtime action rules still decide those.
/// </summary>
internal sealed record SceneActionContractPlan
{
    public bool IncludeTravel { get; init; }
    public bool IncludeGifts { get; init; }
    public bool IncludeNewHelp { get; init; }
    public bool IncludeHelpUpdates { get; init; }
    public bool IncludeMoney { get; init; }
    public bool IncludeFestival { get; init; }
    public bool IsFallback { get; init; }
    public string Reason { get; init; } = string.Empty;

    public static SceneActionContractPlan Full(string reason = "full-contract-requested") => new()
    {
        IncludeTravel = true,
        IncludeGifts = true,
        IncludeNewHelp = true,
        IncludeHelpUpdates = true,
        IncludeMoney = true,
        IncludeFestival = true,
        IsFallback = true,
        Reason = reason
    };
}
