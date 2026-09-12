using System;
using System.Linq;

namespace LivingNPCs.Dialogue.Engine;

/// <summary>
/// Chinese/English cues for adding the NPC's upcoming schedule to a reply's context.
/// Missed cues only skip this extra hint; the normal current-activity context is unchanged.
/// </summary>
public static class ConversationCues
{
    /// <summary>Cues that justify surfacing the NPC's upcoming schedule.</summary>
    public static readonly string[] FutureSchedule =
    {
        "去哪", "去哪里", "where", "接下来", "之后", "等下", "待会", "忙什么", "在做什么", "计划", "日程",
        "going", "next", "plan", "schedule"
    };

    public static bool ContainsAny(string text, string[] cues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return cues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }
}
