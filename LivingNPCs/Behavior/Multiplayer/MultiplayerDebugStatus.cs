namespace LivingNPCs.Behavior.Multiplayer;

/// <summary>
/// 一次性读取的多人同步诊断快照。只包含纯数据，不暴露运行时集合或可变服务状态。
/// </summary>
internal sealed record MultiplayerDebugStatus(
    bool IsMultiplayer,
    MultiplayerRole Role,
    int ProtocolVersion,
    ProtocolHandshakeState HandshakeState,
    bool HostAuthorityActive,
    int RelationshipViewCount,
    int PendingExchangeReportCount,
    int CompatiblePeerCount);
