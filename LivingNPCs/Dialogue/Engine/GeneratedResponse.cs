using LivingNPCs.Dialogue.Diagnostics;
using LivingNPCs.Dialogue.Llm;
using LivingNPCs.Dialogue.Persistence;
namespace LivingNPCs.Dialogue.Engine;

internal sealed record GeneratedResponse(
    string FormattedLine,
    string[] ParsedLines
);
