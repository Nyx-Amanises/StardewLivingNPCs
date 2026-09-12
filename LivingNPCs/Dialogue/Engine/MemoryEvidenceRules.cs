namespace LivingNPCs.Dialogue.Engine;

/// <summary>Shared factual boundaries for replies and both metadata extraction paths.</summary>
internal static class MemoryEvidenceRules
{
    internal const string Dialogue =
        "For a pure recall/status check, give only a concise answer from the relevant records and, if needed, one natural clarification. "
        + "End the spoken line once the requested facts or missing information are stated; do not append an epilogue about related belongings, habits or scenes. "
        + "Do not add farmer options, anecdotes or new scene details; this overrides the usual option/novelty guidance. "
        + "Keep the mandatory metadata line; absent new effects, declare complete:true with no effect fields. "
        + "Use the latest explicit correction for the same fact/agreement, even across languages, without transferring changes to another. "
        + "Preserve who promised what to whom; distinguish proposals, promises, revisions, cancellations and completions. "
        + "Apply an explicit relative reschedule to its original date; the new date need not be repeated to establish that offset. "
        + "Anchor relative dates to evidence for that utterance's date, never automatically to today. Earlier undated turns and whole-conversation archive/update times do not establish that date. "
        + "If the original date is missing, preserve the known offset and confirm the date; summary updates create no fresh deadlines. "
        + "Each agreement detail needs its own evidence: its day does not establish a clock time; today's scene time or place cannot fill missing details. "
        + "Preserve each claim's scope: disliking is not abstaining; delivered materials do not prove subsequent use or finished work. "
        + "Do not add unsupported past dialogue, precise locations, neighboring objects, object form, damage, repairs, motives, quantities, sensory details or how events unfolded. "
        + "Traits and usual habits do not prove a specific past event. These records are excerpts, not an exhaustive account. Evidence for a different event does not disprove the event being asked about. "
        + "When the requested event or detail lacks evidence, state only your own uncertainty and invite a reminder. Do not substitute another memory, deny the event or suggest the farmer meant someone else. "
        + "If a referent, date, latest status or requested detail truly remains ambiguous, briefly state uncertainty or ask one natural clarification. "
        + "In other exchanges, show personality through a present reaction or an explicit future proposal. Options may express present attitudes, clarify or propose future plans; "
        + "do not offer confessions of past mistakes or other unrecorded past premises. Metadata obeys the same factual limits. "
        + "For a pure recall/status question, omit memories unless this exchange establishes a real change; copying or paraphrasing existing evidence is no new fact or reinforcement. "
        + "Before answering, remove every unsupported historical detail from the dialogue, options and metadata. "
        + "Current structured relationship, help-request and outing state governs game actions; old quotes and relationship impressions cannot override it or reactivate completed/cancelled actions."
        + "\nPure recall output layout with no new effects: both lines are mandatory; replace the answer placeholder with your reply."
        + "\n- <concise NPC answer, with at most one clarification>"
        + "\n!LIVINGNPCS_META {\"complete\":true}";

    internal const string Metadata =
        "- Recall/status alone: omit memories; no reinforcement. Store new disclosures/changes, not repeated context or NPC-invented past details. "
        + "Correct using exact kind/subject/language; keep latest obligations, rescheduling/cancellation. "
        + "Item preferences keep subject but update liked_item_category/disliked_item. Distinct facts/agreements need distinct subjects; no updates from ambiguous references. "
        + "Promises are not completions; recall triggers no actions.";
}
