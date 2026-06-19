using System;
using System.Linq;

namespace ValleyTalk;

/// <summary>
/// Shared conversational-cue vocabulary. The semantic routing fallback and the future-schedule
/// heuristic both need to spot "where are you going" style player lines; keeping the overlapping
/// terms here stops the two call sites from drifting apart when one is edited.
/// </summary>
public static class ConversationCues
{
    // Asking where the NPC is or is going. Shared by routing (location/companion promotion) and the
    // future-schedule heuristic.
    private static readonly string[] Whereabouts = { "去哪", "去哪里", "where" };

    // Routing-only: explicit travel / "bring me along" intent.
    private static readonly string[] TravelAndCompanion = { "go", "一起去", "带我", "带你", "陪我", "陪你", "come with", "show me" };

    // Schedule-only: asking about plans, what is next, or how busy the NPC is.
    private static readonly string[] ScheduleInquiry = { "接下来", "之后", "等下", "待会", "忙什么", "在做什么", "计划", "日程", "going", "next", "plan", "schedule" };

    /// <summary>Cues that promote Location/LivingNpc context to full in the routing fallback.</summary>
    public static readonly string[] LocationPromotion = Whereabouts.Concat(TravelAndCompanion).ToArray();

    /// <summary>Cues that justify surfacing the NPC's upcoming schedule.</summary>
    public static readonly string[] FutureSchedule = Whereabouts.Concat(ScheduleInquiry).ToArray();

    public static bool ContainsAny(string text, string[] cues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return cues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }
}
