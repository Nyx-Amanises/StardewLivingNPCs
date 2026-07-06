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
