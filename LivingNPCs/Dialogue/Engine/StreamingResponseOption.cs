using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

internal enum StreamingResponseOptionKind
{
    Silent,
    Generated,
    Typed
}

internal sealed record StreamingResponseOption(string Text, StreamingResponseOptionKind Kind);

/// <summary>
/// In-band control tokens carried over <c>IStreamSink.OnToken</c>. The token pipeline between the
/// engine and the streaming window (sink → presenter → window → update queue) is a pass-through of
/// raw string deltas, so a control token travels the existing path without widening any interface;
/// the preview buffer (<c>StreamingDialogueUpdateQueue</c>) intercepts and consumes it before
/// anything reaches the screen. Sinks that render raw tokens themselves must treat these values as
/// control signals, never as display text.
/// </summary>
internal static class StreamingControlTokens
{
    /// <summary>
    /// Discard the accumulated preview: the engine rejected the previous attempt (unparseable or
    /// wrong language) and the retry must start from a clean window. NUL guards make an accidental
    /// collision with genuine model output practically impossible.
    /// </summary>
    public const string RetryReset = "\u0000LIVINGNPCS_RETRY_RESET\u0000";
}
