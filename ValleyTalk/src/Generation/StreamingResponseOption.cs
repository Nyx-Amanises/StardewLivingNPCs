namespace ValleyTalk;

internal enum StreamingResponseOptionKind
{
    Silent,
    Generated,
    Typed
}

internal sealed record StreamingResponseOption(string Text, StreamingResponseOptionKind Kind);
