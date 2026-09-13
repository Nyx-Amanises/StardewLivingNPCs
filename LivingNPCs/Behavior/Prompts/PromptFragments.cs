using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;
using LivingNPCs.Dialogue.Engine;

namespace LivingNPCs.Behavior;

/// <summary>
/// Single home for every English prompt fragment LivingNPCs sends to the language model.
/// Tune AI wording here; the classes that call these members only decide *when* a fragment
/// is included, never *how it is worded*.
///
/// Layout mirrors what the model sees:
/// - <see cref="State"/>: descriptions computed from a NPC's persisted <see cref="LivingNpcState"/>.
/// - <see cref="Facts"/>: one-line descriptions of individual memory fact records.
/// - <see cref="Recall"/>: memory-recall focus lines built from recall plans.
/// - <see cref="Context"/>: the main hidden context assembled by <see cref="BehaviorPromptContextBuilder"/>.
/// - <see cref="GiftOpportunity"/> / <see cref="GiftResponseMail"/> / <see cref="HelpRequestOpportunity"/> /
///   <see cref="HelpRequestHandIn"/> / <see cref="HelpRequestDelivery"/>: the situational "## LivingNPCs …"
///   sections pushed to ValleyTalk before or during a conversation.
/// - <see cref="Outing"/>: the active companion-outing section.
/// - <see cref="Planner"/>: the micro-behavior planner request sent by <see cref="AiBehaviorClient"/>.
///
/// Keep game logic out of this file: members are string constants or small format helpers, so
/// wording can be tuned without touching behavior code. Strings that are *stored* in the save
/// (memory summaries, LastInteraction values, skip reasons) intentionally stay with their writers.
/// </summary>
internal static class PromptFragments
{
    /// <summary>Prompt descriptions derived from a NPC's persisted state.</summary>
    internal static class State
    {
        // Empty-form fallbacks, named so Context.IsEmptyStateValue can match them exactly.
        public const string EmptyGiftMemory = "no recent LivingNPCs gift memory";
        public const string EmptyEventMemory = "no recent LivingNPCs event memory";
        public const string EmptyLongTermMemories = "no durable personal memory has been recorded";
        public const string EmptyPlayerPreferences = "no durable farmer preference memory has been recorded";
        public const string EmptyCommunityImpressions = "no community impression about the farmer has been recorded";
        public const string EmptySharedExperiences = "no durable shared experiences are recorded";
        public const string EmptyBehaviorInfluences = "no active conversation-driven behavior tendency";
        public const string EmptyHelpRequests = "no durable help requests are recorded";
        public const string EmptyConflicts = "no durable conflict memory has been recorded";
        public const string EmptyNickname = "no personal name preference has been recorded";

        public static string Emotion(LivingNpcState state) => state.CurrentEmotion switch
        {
            "Happy" => $"happy, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Jealous" => $"jealous, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Worried" => $"worried, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Grateful" => $"grateful, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Disappointed" => $"disappointed, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Uneasy" => $"uneasy, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Upset" => $"upset, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Angry" => $"angry, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            "Sad" => $"sad, intensity {state.EmotionIntensity}/100; latest reason: {state.LastEmotionReason}",
            _ => $"calm, intensity {state.EmotionIntensity}/100"
        };

        public static string Familiarity(LivingNpcState state) => state.Familiarity switch
        {
            >= 75 => "close and comfortable",
            >= 45 => "familiar",
            >= 18 => "recognizes the farmer",
            _ => "new or barely familiar"
        };

        public static string InteractionComfortTier(LivingNpcState state) => state.InteractionComfortTier switch
        {
            "Intimate" => $"very close; {state.LastFriendshipHearts} hearts; up to {state.DailyConversationComfortLimit} short conversations today can feel normal",
            "Trusted" => $"trusted; {state.LastFriendshipHearts} hearts; up to {state.DailyConversationComfortLimit} short conversations today can still feel natural",
            "Friendly" => $"friendly; {state.LastFriendshipHearts} hearts; repeated conversations are acceptable but should not feel endlessly eager",
            "Familiar" => $"familiar; {state.LastFriendshipHearts} hearts; repeated conversations should stay polite and modest",
            _ => $"distant; {state.LastFriendshipHearts} hearts; repeated conversations should feel more cautious or formal"
        };

        public static string InteractionRhythm(LivingNpcState state) => state.InteractionRhythm switch
        {
            "AfterLongGap" => $"they are speaking again after {state.LastConversationGapDays} days without a recorded conversation",
            "AtComfortLimit" => $"this is conversation {state.ConversationsToday} today, around the normal comfort limit for this relationship",
            "BuildingRoutine" => $"the farmer has checked in for {state.ConsecutiveConversationDays} consecutive days",
            "CheckedInAgain" => $"this is conversation {state.ConversationsToday} with the farmer today",
            "ComfortableRepeat" => $"this is conversation {state.ConversationsToday} today, but the relationship is close enough that it can still feel natural",
            "CrowdedToday" => $"the farmer has already spoken with them {state.ConversationsToday} times today, so the attention may feel repetitive",
            "DailyRoutine" => $"the farmer has spoken with them for {state.ConsecutiveConversationDays} consecutive days, forming a familiar daily rhythm",
            "FirstConversation" => "this is the first recorded LivingNPCs conversation with the farmer",
            "FreshToday" => "this is the first recorded conversation with the farmer today",
            "LongQuietGap" => $"there has been no recorded conversation with the farmer for {state.LastConversationGapDays} days",
            "NoConversationToday" => state.LastConversationGapDays <= 1
                ? "there has been no recorded conversation with the farmer today, but they spoke yesterday"
                : $"there has been no recorded conversation with the farmer today; the last one was {state.LastConversationGapDays} days ago",
            "PoliteRepeat" => $"this is conversation {state.ConversationsToday} today, and the relationship is not close enough for repeated chats to feel fully casual",
            _ => "no stable interaction rhythm yet"
        };

        public static string LastGift(LivingNpcState state) => string.IsNullOrWhiteSpace(state.LastGiftName)
            ? EmptyGiftMemory
            : $"last recorded gift: the farmer offered {state.LastGiftName}; gift taste: {state.LastGiftTaste}; gifts recorded today: {state.GiftsToday}";

        public static string LastEvent(LivingNpcState state) => string.IsNullOrWhiteSpace(state.LastEventContext)
            ? EmptyEventMemory
            : $"last recorded event context: {state.LastEventContext}";

        public static string LongTermMemories(LivingNpcState state)
        {
            var memories = state.GetTopLongTermMemories(4).ToList();
            return memories.Count == 0
                ? EmptyLongTermMemories
                : string.Join("; ", memories.Select(memory => memory.Summary));
        }

        public static string PlayerPreferences(LivingNpcState state)
        {
            var preferences = state.GetTopPlayerPreferences(6).ToList();
            return preferences.Count == 0
                ? EmptyPlayerPreferences
                : string.Join("; ", preferences.Select(memory => memory.Summary));
        }

        public static string SharedExperiences(LivingNpcState state, int currentTotalDays)
        {
            var experiences = state.GetTopSharedExperiences(4).ToList();
            return experiences.Count == 0
                ? EmptySharedExperiences
                : string.Join("; ", experiences.Select(experience => Facts.SharedExperience(experience, currentTotalDays)));
        }

        public static string DialogueBehaviorInfluences(LivingNpcState state, int currentTotalDays)
        {
            var influences = state.GetActiveDialogueBehaviorInfluences(currentTotalDays).Take(4).ToList();
            return influences.Count == 0
                ? EmptyBehaviorInfluences
                : string.Join("; ", influences.Select(influence => Facts.DialogueBehaviorInfluence(influence, currentTotalDays)));
        }

        public static string HelpRequests(LivingNpcState state, int currentTotalDays)
        {
            var requests = state.GetTopHelpRequests(4).ToList();
            return requests.Count == 0
                ? EmptyHelpRequests
                : string.Join("; ", requests.Select(request => Facts.HelpRequest(request, currentTotalDays)));
        }

        public static string RelationshipTrust(LivingNpcState state) => state.RelationshipTrust switch
        {
            >= 80 => $"deep interpersonal trust ({state.RelationshipTrust}/100)",
            >= 60 => $"steady interpersonal trust ({state.RelationshipTrust}/100)",
            >= 35 => $"tentative interpersonal trust ({state.RelationshipTrust}/100)",
            _ => $"low interpersonal trust ({state.RelationshipTrust}/100)"
        };

        public static string SecretSharing(LivingNpcState state) => state.RelationshipTrust switch
        {
            >= 80 => "deep trust; private hopes, fears, and history may surface naturally when the scene truly supports it",
            >= 60 => "steady trust; some vulnerable personal details may be shared when relevant",
            >= 35 => "limited trust; light personal details are fine, but deeper secrets should stay mostly guarded",
            _ => "low trust; keep disclosures surface-level and avoid volunteering private secrets"
        };

        public static string Conflicts(LivingNpcState state)
        {
            var conflicts = state.GetTopConflicts(4).ToList();
            return conflicts.Count == 0
                ? EmptyConflicts
                : string.Join("; ", conflicts.Select(Facts.Conflict));
        }

        public static string FarmerNickname(LivingNpcState state)
        {
            if (string.IsNullOrWhiteSpace(state.FarmerNickname))
            {
                return EmptyNickname;
            }

            return state.FarmerNicknameStatus switch
            {
                "Accepted" => $"the farmer asked to be called {state.FarmerNickname}, and this NPC accepted; when choosing to address the farmer, use that name instead of @ or the save-file name, and never combine both names in one reply",
                "Rejected" => $"the farmer asked to be called {state.FarmerNickname}, but this NPC did not accept; do not use that name unless the relationship later changes",
                _ => $"the farmer asked to be called {state.FarmerNickname}; acceptance is unclear, so the NPC may decide whether to use it based on personality and relationship"
            };
        }
    }

    /// <summary>
    /// One-line prompt descriptions of individual memory fact records. Dates render as relative
    /// time ("due tomorrow", "shared 3 days ago") because the model cannot reason about internal
    /// total-day counters.
    /// </summary>
    internal static class Facts
    {
        public static string CommunityImpression(CommunityImpressionFact memory, int currentTotalDays)
        {
            string stage = CommunityImpressionStore.GetFreshnessStage(memory, currentTotalDays);
            return memory.Source switch
            {
                "Witnessed" => $"directly witnessed, {stage} ({memory.Visibility.ToLowerInvariant()}): {memory.Summary}",
                "CloseCircle" => $"heard through a close connection after {memory.TransmissionDepth} retelling(s), {stage} ({memory.Visibility.ToLowerInvariant()}): {memory.Summary}",
                _ => $"picked up as a faint public impression after {memory.TransmissionDepth} retelling(s), {stage} ({memory.Visibility.ToLowerInvariant()}): {memory.Summary}"
            };
        }

        public static string SharedExperience(SharedExperienceFact experience, int currentTotalDays) =>
            $"{experience.Type} at {experience.LocationLabel}; shared {Age(experience.CreatedTotalDays, currentTotalDays)}; summary: {experience.Summary}";

        public static string DialogueBehaviorInfluence(DialogueBehaviorInfluenceFact influence, int currentTotalDays) =>
            $"{influence.Type}, intensity {influence.Intensity}/100, target {influence.TargetLocationLabel}, status {influence.Status}, {ExpiryLabel(influence.ExpiresTotalDays, currentTotalDays)}; summary: {influence.Summary}";

        public static string HelpRequest(NpcHelpRequestFact request, int currentTotalDays) =>
            $"{request.Type}, {DueLabel(request.DueTotalDays, currentTotalDays)}, status {request.Status}, step {System.Math.Min(request.CurrentStepIndex + 1, System.Math.Max(1, request.Steps.Count))}/{System.Math.Max(1, request.Steps.Count)}; current step: {HelpRequestCurrentStep(request)}; summary: {request.Summary}";

        public static string HelpRequestFulfilled(NpcHelpRequestFact request, int currentTotalDays) =>
            $"{request.Type} was fulfilled {Age(request.FulfilledTotalDays, currentTotalDays)}; summary: {request.Summary}; follow-up potential: {request.FollowUpPotential}";

        public static string HelpRequestCurrentStep(NpcHelpRequestFact request)
        {
            var step = request.CurrentStep;
            if (step == null)
            {
                return request.Type == "item_request"
                    ? $"bring {request.RequestedItemLabel} {request.RequestedItemId}".Trim()
                    : request.QuestionTopic;
            }

            return HelpRequestStep(step);
        }

        public static string HelpRequestStep(NpcHelpRequestStepFact step) => step.Type == "item_request"
            ? $"item step: {step.Summary}; needs {step.RequestedItemLabel} {step.RequestedItemId}; status {step.Status}"
            : $"conversation step: {step.Summary}; topic {step.QuestionTopic}; status {step.Status}";

        public static string Conflict(NpcConflictFact conflict) =>
            $"{conflict.Status.ToLowerInvariant()} conflict, severity {conflict.Severity}/100, cause {conflict.CauseKind}, repair stage {conflict.RepairStage}: {conflict.Summary}";

        public static string ConflictResolved(NpcConflictFact conflict, int currentTotalDays) =>
            $"resolved conflict from {Age(conflict.CreatedTotalDays, currentTotalDays)}, cause {conflict.CauseKind}: {conflict.Summary}";

        private static string Age(int totalDays, int currentTotalDays) =>
            Context.MemoryAge(totalDays < 0 ? int.MaxValue : System.Math.Max(0, currentTotalDays - totalDays));

        private static string DueLabel(int dueTotalDays, int currentTotalDays)
        {
            if (dueTotalDays < 0)
            {
                return "no due date";
            }

            int remaining = dueTotalDays - currentTotalDays;
            return remaining switch
            {
                < 0 => "past due",
                0 => "due today",
                1 => "due tomorrow",
                _ => $"due in {remaining} days"
            };
        }

        private static string ExpiryLabel(int expiresTotalDays, int currentTotalDays)
        {
            if (expiresTotalDays < 0)
            {
                return "no expiry";
            }

            int remaining = expiresTotalDays - currentTotalDays;
            return remaining switch
            {
                < 0 => "already expired",
                0 => "expires today",
                1 => "expires tomorrow",
                _ => $"expires in {remaining} days"
            };
        }
    }

    /// <summary>Recall-focus lines built from the per-reply memory recall plan.</summary>
    internal static class Recall
    {
        // Empty-form fallbacks, named so Context.IsEmptyStateValue can match them exactly.
        public const string EmptyLongTermRecall = "no durable personal memory is especially relevant right now";
        public const string EmptyPreferenceRecall = "no durable farmer preference memory is especially relevant right now";
        public const string EmptyCommunityRecall = "no community impression is especially relevant right now";

        public static string LongTermMemories(IReadOnlyList<LongTermMemorySelection> selections)
        {
            return selections.Count == 0
                ? EmptyLongTermRecall
                : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
        }

        public static string PlayerPreferences(IReadOnlyList<PlayerPreferenceSelection> selections)
        {
            return selections.Count == 0
                ? EmptyPreferenceRecall
                : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
        }

        public static string CommunityImpressions(NPC npc, IReadOnlyList<CommunityImpressionSelection> selections, int currentTotalDays)
        {
            CommunityReactionCue reaction = CommunityReactionStyle.For(npc);
            return selections.Count == 0
                ? EmptyCommunityRecall
                : $"observer tendency: {reaction.PromptLabel}; retelling tendency: {reaction.RetellingPromptLabel}; {string.Join("; ", selections.Select(selection => Facts.CommunityImpression(selection.Memory, currentTotalDays)))}";
        }
    }

    /// <summary>
    /// The main hidden context (full and concise variants) assembled by
    /// <see cref="BehaviorPromptContextBuilder"/>. Members appear in output order.
    /// </summary>
    internal static class Context
    {
        // ---- header and rules ----
        public static string Header(string npcDisplayName) => $"## LivingNPCs Context: {npcDisplayName}";
        public const string Purpose = "Purpose: hidden continuity for ValleyTalk's next in-character reply.";
        public const string RulesHeading = "Rules:";
        public static readonly string[] Rules =
        {
            "- Use at most one or two relevant details for body language, tone or pacing.",
            "- Stay in character: no status report, quoting these notes, or mentioning LivingNPCs/mods/prompts/AI/JSON."
        };
        public const string ConciseRules = "Rules: hidden body language and memory for the next in-character reply; do not quote it or mention LivingNPCs/AI/JSON; use at most one or two details naturally.";

        // ---- conversation stance ----
        public const string StanceHeading = "Conversation stance:";

        public static string ConversationStance(
            NPC npc,
            NpcDispositionProfile disposition,
            EmotionalExpressionCue emotionalStyle)
        {
            return $"{npc.displayName}; temperament: {disposition.PromptLabel}; expression: {emotionalStyle.PromptLabel}; source: {disposition.SourceLabel}.";
        }

        // ---- "Current state:" section ----
        public const string CurrentStateHeading = "Current state:";
        public static string ProfileSourceLine(string sourceLabel) => $"- Character profile source: {sourceLabel}.";
        public static string DispositionLine(string dispositionLabel) => $"- Disposition: {dispositionLabel}.";
        public static string BackgroundLine(string backgroundPrompt) => $"- Character background: {backgroundPrompt}";
        public static string DialogueCueLine(string dialoguePrompt) => $"- Character dialogue cue: {dialoguePrompt}";
        public static string SceneLine(WorldContextSnapshot world) => $"- Scene: {world.PromptLabel}; location: {world.LocationDisplayName}; date: {world.Season} {world.DayOfMonth}; time: {world.TimeOfDay}.";
        public static string SceneLineConcise(WorldContextSnapshot world) => $"- Scene: {world.PromptLabel}; location: {world.LocationDisplayName}; {world.Season} {world.DayOfMonth}, {world.TimeOfDay}.";
        public static string WorldKnowledgeLine(string knowledgeLabel) => $"- World knowledge available to this NPC: {knowledgeLabel}.";
        public static string MoodLine(LivingNpcState state) => $"- Mood: {state.Mood}; attention to farmer: {state.Attention}/100 ({AttentionCue(state.Attention)}); openness: {state.Openness}/100 ({OpennessCue(state.Openness)}); response inclination: {state.CurrentInclination}.";
        public static string MoodLineConcise(LivingNpcState state) => $"- Mood: {state.Mood}; emotion: {State.Emotion(state)}; inclination: {state.CurrentInclination}.";
        public static string EmotionLine(LivingNpcState state) => $"- Interpersonal emotion: {State.Emotion(state)}.";
        public static string ExpressionStyleLine(string styleLabel) => $"- Emotional expression style: {styleLabel}.";
        public static string FamiliarityLine(LivingNpcState state) => $"- Recorded interaction familiarity with the farmer: {state.Familiarity}/100 ("
            + (state.Familiarity < 18
                ? "limited familiarity recorded); current friendship and explicit relationship status take precedence."
                : $"{State.Familiarity(state)}).");
        public static string TrustLine(LivingNpcState state) => $"- Relationship trust in the farmer: {State.RelationshipTrust(state)}.";
        public static string FamiliarityTrustRhythmLineConcise(LivingNpcState state) => $"- Familiarity {state.Familiarity}/100; trust: {State.RelationshipTrust(state)}; rhythm: {State.InteractionRhythm(state)}.";
        public static string InteractionRhythmLine(LivingNpcState state) => $"- Last interaction rhythm: {State.InteractionRhythm(state)}; repeated conversation pressure: {state.RepeatedConversationPressure}/100.";
        public static string GiftContextLine(string lastGift) => $"- Recent gift context: {lastGift}.";
        public static string EventContextLine(string lastEvent) => $"- Recent event context: {lastEvent}.";
        public static string MemoryStoreLine(int longTermCount) => $"- Durable memory store: {longTermCount} long-term memories tracked; relevant ones, if any, appear under high-priority continuity.";
        public static string RelationshipImpressionLine(string impression) => $"- Long-term relationship impression (historical summary; current facts/statuses override stale details; not all events are recent): {impression}";
        public static string KnownPreferencesLine(int preferenceCount) => $"- Farmer preference memories tracked: {preferenceCount}; relevant ones, if any, appear under high-priority continuity.";
        public static string BehaviorTendenciesLine(string tendencies) => $"- Conversation-driven behavior tendencies (conversation stance): {tendencies}.";
        public static string SharedExperiencesLine(string sharedExperiences) => $"- Shared experiences with the farmer: {sharedExperiences}.";
        public static string HelpRequestsLine(string helpRequests) => $"- Help requests involving the farmer: {helpRequests}.";
        public static string CommunityImpressionsLine(int impressionCount) => $"- Community impressions about the farmer's ties with other NPCs: {impressionCount} tracked; background only, selected facts below.";

        // ---- durable-store empty labels (collapsed into one line by the full context) ----
        public const string StoreLabelGifts = "recent gifts";
        public const string StoreLabelEvents = "recent events";
        public const string StoreLabelLongTermMemories = "long-term memories";
        public const string StoreLabelPreferences = "farmer preferences";
        public const string StoreLabelBehaviorTendencies = "conversation-driven behavior tendencies";
        public const string StoreLabelSharedExperiences = "shared experiences";
        public const string StoreLabelHelpRequests = "help requests";
        public const string StoreLabelCommunityImpressions = "community impressions";
        public const string StoreLabelConflicts = "conflicts";
        public const string StoreLabelNickname = "a preferred name for the farmer";

        /// <summary>
        /// One line covering every empty durable store, so the "there is no such shared history"
        /// anti-hallucination signal survives without spending a full line per store.
        /// </summary>
        public static string EmptyStoresLine(IReadOnlyCollection<string> emptyStoreLabels) =>
            $"- Nothing recorded yet for: {string.Join(", ", emptyStoreLabels)}; do not invent such shared history.";

        public static string SocialCirclesLine(IReadOnlyCollection<string> circleLabels) =>
            $"- Stable community circles this NPC belongs to: {(circleLabels.Count == 0 ? "no stable small-circle affiliation is currently tracked" : string.Join(", ", circleLabels))}.";

        public const string HelpRequestLifecycleLine = "- Help-request lifecycle: Offered = asked but not accepted; Pending = accepted/active; only Pending is a task.";
        public const string HelpRequestLifecycleLineConcise = HelpRequestLifecycleLine;
        public static string HelpRequestReadinessLine(string readiness) => $"- Help-request readiness: {readiness}.";

        public static string HelpRequestReadinessAllowed(string reason) =>
            $"may naturally ask for one modest favor now; {reason}; ask once, clearly, then await the farmer's reply; do not withdraw it or answer for them";

        public static string HelpRequestReadinessBlocked(string reason) =>
            $"should not open a new help request now ({reason}); even if the farmer offers to help, gently decline/deflect; never name, accept or commit to a new favor";

        public static string HelpRequestFitLine(string fitLabel) => $"- Help-request fit: {fitLabel}";
        public static string ConflictMemoryLine(string conflicts) => $"- Conflict memory: {conflicts}.";
        public static string SceneInfluenceLine(string reason) => $"- Last applied scene influence on mood: {reason}.";
        public static string CurrentSceneInfluenceLine(WorldStateInfluence influence, LivingNpcState? state)
        {
            string mood = state != null && string.Equals(state.Mood, influence.Mood, System.StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"; suggested mood: {influence.Mood}";
            string inclination = state != null && string.Equals(state.CurrentInclination, influence.Inclination, System.StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $"; suggested response inclination: {influence.Inclination}";
            return $"- Scene: {influence.Reason}{mood}{inclination}.";
        }
        public static string LastInteractionLine(string lastInteraction) => $"- Last interaction: {lastInteraction}.";
        public const string NoStateLine = "- No persistent LivingNPCs state exists yet; use disposition and scene context conservatively.";
        public const string NoStateLineConcise = "- No persistent LivingNPCs state yet; use disposition and scene conservatively.";

        // ---- concise-mode field labels (rendered as "- {label}: {value}") ----
        public const string LabelRecallFocus = "Recall focus";
        public const string LabelRelationshipImpression = "Long-term relationship impression";
        public const string LabelKnownPreferences = "Known farmer preferences";
        public const string LabelBehaviorTendencies = "Behavior tendencies";
        public const string LabelRecentGift = "Recent gift";
        public const string LabelRecentEvent = "Recent event";
        public const string LabelSharedExperiences = "Shared experiences";
        public const string LabelConflict = "Conflict";
        public const string LabelPersonalMemory = "Personal memory";
        public const string LabelHelpRequests = "Help requests";
        public const string LabelLastInteraction = "Last interaction";

        // ---- "High-priority continuity:" section ----
        public const string PriorityHeading = "High-priority continuity:";

        public static string GiftMemoryCue(string giftName, string freshness, string taste, int giftsToday) =>
            $"Gift memory: the farmer offered {giftName} {freshness}; taste was {taste}; gifts recorded today: {giftsToday}; if relevant, briefly acknowledge or adjust warmth, surprise or distance.";

        public static string EventMemoryCue(string eventContext, string freshness) =>
            $"Event memory: {eventContext} ({freshness}); acknowledge only if the conversation naturally continues it.";

        public static string NicknameCue(LivingNpcState state) => $"Personal name memory: {State.FarmerNickname(state)}.";

        public static string LongTermRecallCue(string recallText) =>
            $"Relevant long-term memories for this reply: {recallText}; use at most one if it naturally matters now.";

        public static string PreferenceRecallCue(string recallText) =>
            $"Relevant farmer preference memories for this reply: {recallText}; acknowledge briefly if a gift/topic matches; do not recite a profile.";

        public static string CommunityImpressionCue(string recallText) =>
            $"Community impressions: {recallText}; at most one passing remark, never the opening subject; keep indirect reports tentative, and do not reveal knowledge the NPC would not plausibly have.";

        public static string BehaviorTendencyCue(DialogueBehaviorInfluenceFact influence, int currentTotalDays) =>
            $"Conversation-driven behavior tendency: {Facts.DialogueBehaviorInfluence(influence, currentTotalDays)}; use this as a conversation stance cue for body language and follow-through, not quoted dialogue.";

        public static string ActiveHelpRequestCue(NpcHelpRequestFact request, int currentTotalDays)
        {
            string lifecycle = request.Status == "Offered"
                ? "the NPC has asked, but the farmer has not clearly accepted or declined yet; do not treat it as an active task until accepted"
                : "the farmer accepted this ask; it is now an active personal task";
            return $"Active help request: {Facts.HelpRequest(request, currentTotalDays)}; {lifecycle}; remember that this is an unfinished ask, not a vague topic.";
        }

        public static string FulfilledHelpRequestCue(NpcHelpRequestFact request, int currentTotalDays) =>
            $"Recently fulfilled help request: {Facts.HelpRequest(request, currentTotalDays)}; fulfilled {MemoryAge(BehaviorMemory.GetMemoryAge(request.FulfilledTotalDays, currentTotalDays))}; follow-up potential: {request.FollowUpPotential}; if it fits, the NPC may briefly thank the farmer for following through.";

        public const string DefaultExpiredHelpRequestReaction = "the NPC noticed the request did not work out";

        public static string ExpiredHelpRequestCue(NpcHelpRequestFact request, string reaction, int currentTotalDays) =>
            $"Unfinished help request: {Facts.HelpRequest(request, currentTotalDays)}; reaction: {reaction}; the next conversation may acknowledge this according to personality, without over-punishing if the farmer never accepted.";

        public static string SharedExperienceCue(SharedExperienceFact experience, int currentTotalDays) =>
            $"Shared experiences: {Facts.SharedExperience(experience, currentTotalDays)}; if it fits, briefly acknowledge this completed time together without formally reciting the memory or treating it as a future plan.";

        public static string UnresolvedConflictCue(NpcConflictFact conflict, string conflictStyle) =>
            $"Unresolved conflict: {Facts.Conflict(conflict)}; reduce warmth; a brief, cool reply or declining closeness may fit; if severe, a friendly invitation may be refused; expression style: {conflictStyle}.";

        public const string ComplexRepairCue =
            "Complex conflict repair: a pleasant line alone cannot erase serious hurt; let apology, a meaningful gesture, time, and a specific restorative conversation accumulate before full repair.";

        public static string ResolvedConflictCue(NpcConflictFact conflict, string repairStyle, int currentTotalDays) =>
            $"Recently resolved conflict: {Facts.Conflict(conflict)}; began {MemoryAge(BehaviorMemory.GetMemoryAge(conflict.CreatedTotalDays, currentTotalDays))}; if it fits naturally, the NPC may briefly make clear that the earlier issue is past now; recovery style: {repairStyle}.";

        public static string WorldStageCue(string replyGuidance) => $"World-stage continuity: {replyGuidance}";

        public static string NearbyNpcsCue(IReadOnlyList<string> nearbyNpcNames) =>
            $"Social cue: nearby NPCs include {string.Join(", ", nearbyNpcNames)}; keep the reply aware of public company.";

        // ---- "Recent tracked moments" section ----
        public const string RecentMomentsHeading = "Recent tracked moments, oldest to newest:";

        public static string PromptEntry(BehaviorMemoryEntry entry)
        {
            string location = string.IsNullOrWhiteSpace(entry.LocationDisplayName) ? entry.LocationName : entry.LocationDisplayName;
            string locationSuffix = string.IsNullOrWhiteSpace(location) ? string.Empty : $" at {location}";
            string kind = entry.Kind.ToLowerInvariant() switch
            {
                "conversation" => "conversation",
                "gift" => "gift",
                "event" => "event",
                "longtermmemory" => "long-term memory",
                "conflict" => "conflict",
                "npcaction" => "npc action",
                "socialmemory" => "social memory",
                _ => "behavior"
            };

            return $"{entry.Season} {entry.Day}, {entry.TimeOfDay}{locationSuffix}: {kind} - {entry.Action}; reason: {entry.Reason}";
        }

        // ---- "Next reply guidance:" section ----
        public const string GuidanceHeading = "Next reply guidance:";
        public const string GuidanceNoStateSubtle = "Keep continuity subtle; do not invent strong feelings from weak context.";
        public static string GuidanceExpressionStyle(string replyGuidance) => $"Emotion expression style: {replyGuidance}.";
        public static string GuidanceRelationshipPacing(LivingNpcState state) => $"Relationship pacing (last recorded interaction): {State.InteractionComfortTier(state)}."
            + (state.Familiarity >= 18 || state.LastFriendshipHearts > 0
                ? " Let current friendship and recorded familiarity guide warmth instead of defaulting to stranger-level politeness."
                : string.Empty);
        public static string GuidanceDisclosurePacing(LivingNpcState state) => $"Disclosure pacing: {State.SecretSharing(state)}."
            + (state.RelationshipTrust < 35 ? " Avoid sudden emotional intimacy." : string.Empty);
        public static string GuidanceInteractionRhythm(LivingNpcState state) => state.RepeatedConversationPressure >= 20
            ? "Last interaction rhythm should shape pacing: a brief, amused, busy, or gently bounded reply may fit; do not make every repeated talk equally eager."
            : "Let the last interaction rhythm shape pacing; do not make every repeated talk equally eager.";
        public static string GuidanceInvitationPolicy(LivingNpcState state) => $"Invitation policy: {TravelInvitationPolicy(state)}.";

        public static string TravelInvitationPolicy(LivingNpcState state)
        {
            string relationshipPolicy = state.InteractionComfortTier switch
            {
                "Intimate" or "Trusted" =>
                    "shared outings and private visits may be accepted when the scene and schedule allow",
                "Friendly" =>
                    "public outings are natural; private invitations such as visiting the farmer's farm should still need a good in-character reason",
                "Familiar" =>
                    "brief public company may be acceptable, but private or extended outings should usually be declined or deferred",
                _ =>
                    "the relationship is still distant, so private invitations such as visiting the farmer's farm or home should usually be declined politely; at most, brief public company may fit"
            };
            return $"{relationshipPolicy}; ordinary daily schedule stops are soft constraints, not sole reasons to decline; a matching current/upcoming destination favors going together or showing the way; still refuse during events, sleep, severe conflict, unsafe scenes, or truly story-critical obligations";
        }

        public const string GuidanceSceneNudge = "Let the current scene pressure tint tone and pacing without explicitly explaining the scene mechanics.";

        public static string GuidanceWorldStage(string replyGuidance) =>
            $"World, household and residency references must fit this NPC's knowledge: {replyGuidance}";

        public const string ConciseReplyGuidanceLine = "- Reply guidance: let the mood, emotion, and relationship pace above shape tone and word choice; surface at most one or two details, and keep references to town progress, the farmer's household, and shared history consistent with what this NPC plausibly knows.";

        // ---- shared helpers ----
        private static string AttentionCue(int attention) => attention switch
        {
            >= 75 => "attentive",
            >= 45 => "aware",
            _ => "lightly distracted"
        };

        private static string OpennessCue(int openness) => openness switch
        {
            >= 75 => "open",
            >= 45 => "measured",
            _ => "reserved"
        };

        public static string MemoryAge(int ageDays) => ageDays switch
        {
            0 => "today",
            1 => "yesterday",
            int.MaxValue => "at an unknown time",
            _ => $"{ageDays} days ago"
        };

        /// <summary>
        /// True when a state/recall value is one of the known empty-form fallbacks ("no recent…",
        /// "no durable…") or an initializer placeholder. The concise context drops such lines.
        /// Exact matching — not a "no " prefix sniff — so real content that happens to start with
        /// "No …" (a memory summary, a relationship impression) is never silently dropped.
        /// </summary>
        public static bool IsEmptyStateValue(string value)
        {
            return EmptyStateValues.Contains(value.Trim());
        }

        private static readonly HashSet<string> EmptyStateValues = new(System.StringComparer.Ordinal)
        {
            State.EmptyGiftMemory,
            State.EmptyEventMemory,
            State.EmptyLongTermMemories,
            State.EmptyPlayerPreferences,
            State.EmptyCommunityImpressions,
            State.EmptySharedExperiences,
            State.EmptyBehaviorInfluences,
            State.EmptyHelpRequests,
            State.EmptyConflicts,
            State.EmptyNickname,
            Recall.EmptyLongTermRecall,
            Recall.EmptyPreferenceRecall,
            Recall.EmptyCommunityRecall,
            // Stored initializer placeholders written by BehaviorMemory/state writers.
            "none yet",
            "none"
        };
    }

    /// <summary>The "## LivingNPCs Gift Opportunity" section.</summary>
    internal static class GiftOpportunity
    {
        public const string DefaultDailyCue = "The relationship is warm enough that the NPC may offer a small everyday gift today.";

        public static string Section(string npcDisplayName, string cue, string sharedGiftIds, string personalizedGiftIds)
        {
            return string.Join(
                "\n",
                "## LivingNPCs Gift Opportunity",
                $"- Gift cue: {cue}.",
                "- This authorization applies to this one reply only. Optional: at most one small in-game gift. Do not upgrade it to give_meaningful_gift without separate explicit authorization.",
                "- A visible immediate offer permits exactly one give_small_gift action; no visible offer means no gift action.",
                $"- Shared small gift IDs: {sharedGiftIds}.",
                $"- {npcDisplayName}'s personalized small gift IDs: {personalizedGiftIds}.",
                "- To name a gift, copy both itemId and its matching itemLabel from these lists; otherwise say 'a small thing' and leave both fields empty.",
                "- Never invent jewelry, clothing, keepsakes, notes, handmade props or other items outside these lists.",
                "- Skip the gift if the moment feels emotionally wrong, crowded or abrupt."
            );
        }

        public static string NoOpportunitySection()
        {
            return "## LivingNPCs Gift Restriction: no NPC gift is authorized for this reply — do not offer or give the farmer any item, include no give_small_gift or give_meaningful_gift action, and do not claim any earlier unsupported gift offer was received or kept.";
        }
    }

    /// <summary>The "## LivingNPCs Birthday/Reciprocal Gift Mail" section for immediate gift reactions.</summary>
    internal static class GiftResponseMail
    {
        public static string Section(string npcDisplayName, string giftItemName, bool isBirthdayGift)
        {
            string itemLabel = string.IsNullOrWhiteSpace(giftItemName)
                ? "the farmer's gift"
                : giftItemName.Trim();
            string header = isBirthdayGift
                ? "## LivingNPCs Birthday Gift Mail"
                : "## LivingNPCs Reciprocal Gift Mail";
            string reason = isBirthdayGift
                ? $"LivingNPCs has scheduled a later birthday thank-you mail from {npcDisplayName} because the farmer remembered their birthday with {itemLabel}."
                : $"LivingNPCs has scheduled a later mailbox return gift from {npcDisplayName} because the farmer gave them {itemLabel}.";
            return string.Join(
                "\n",
                header,
                $"- {reason}",
                "- In this immediate gift reaction, the NPC may briefly imply they will send something later if it sounds natural.",
                "- Do not give the farmer an item now, do not include a hidden give_small_gift or give_meaningful_gift action, and do not promise a specific item."
            );
        }
    }

    /// <summary>The "## LivingNPCs Help Request Opportunity" section.</summary>
    internal static class HelpRequestOpportunity
    {
        public static string Section(string npcDisplayName)
        {
            return string.Join(
                "\n",
                "## LivingNPCs Help Request Opportunity",
                $"- Today {npcDisplayName} may naturally ask one small item favor from the help-request fit list.",
                "- State every required item clearly and in order, with no optional/bonus items. Await the farmer's reply; do not answer for them.",
                "- Keep it brief and in character; if no listed item or moment fits, wait for another day."
            );
        }
    }

    /// <summary>
    /// The "## LivingNPCs Help Request Gift Response" section: a requested item was handed in
    /// through the normal gift flow while a conversation reply is being generated.
    /// </summary>
    internal static class HelpRequestHandIn
    {
        public const string Header = "## LivingNPCs Help Request Gift Response";
        public const string CapabilityLine = "- This hand-in section describes captured task progress; it does not authorize new NPC gifts or help requests.";

        public static string HandInLine(string npcDisplayName, string giftItemName, string giftItemId) =>
            $"- The farmer just handed {npcDisplayName} {giftItemName} ({giftItemId}) for a LivingNPCs help request.";

        public const string NotDailyGiftLine = "- This is a requested task hand-in, not an unexpected daily gift.";
        public const string OverrideTasteLine = "- Override ordinary gift taste guidance: do not say the item is unwanted, neutral, poor taste, or not a favorite.";
        public const string ThankNaturallyLine = "- Thank the farmer for following through and connect the item to the request in a natural, in-character way.";

        public static string StatusLine(NpcHelpRequestFact request) =>
            $"- Help request status after hand-in: {request.Status}; summary: {request.Summary}; resolution: {request.Resolution}";

        public static string AdvancedStepLine(NpcHelpRequestFact request) =>
            $"- The request advanced to another step. Current next step: {Facts.HelpRequestCurrentStep(request)}";

        public const string CompletedLine = "- The request is now complete; the immediate reply should sound grateful and complete, not like a normal gift reaction.";

        public static string FriendshipRewardLine(int rewardFriendship) =>
            $"- LivingNPCs already granted the configured friendship reward (+{rewardFriendship}); mention rewards only if it sounds natural.";

        public static string MoneyRewardGrantedLine(int rewardMoney) =>
            $"- LivingNPCs already granted the configured money reward ({rewardMoney}g); do not promise extra payment beyond the system reward.";

        public static string MoneyRewardQueuedLine(int rewardMoney) =>
            $"- LivingNPCs added the configured money reward ({rewardMoney}g) to the quest journal for the farmer to claim; do not promise extra payment beyond the system reward.";

        public const string ThankYouMailLine = "- LivingNPCs scheduled a small thank-you item by mail for tomorrow; mention it only if it feels natural, and do not imply the farmer already received it.";
    }

    /// <summary>
    /// The "## LivingNPCs Immediate Help Request Delivery" section: a requested item was delivered
    /// outside the gift flow (e.g. when the daily gift limit was already used).
    /// </summary>
    internal static class HelpRequestDelivery
    {
        public const string Header = "## LivingNPCs Immediate Help Request Delivery";
        public const string CapabilityLine = HelpRequestHandIn.CapabilityLine;

        public static string HandInLine(string npcDisplayName, string giftItemName, string giftItemId) =>
            $"- The farmer just handed {npcDisplayName} {giftItemName} ({giftItemId}) for a LivingNPCs help request.";

        public const string NotDailyGiftLine = "- This is a task hand-in, not an ordinary daily gift. Acknowledge the requested item even if the farmer has already given a normal gift today.";
        public const string OverrideTasteLine = "- Do not judge this item by ordinary gift taste; do not say it is unwanted, neutral, poor taste, or not a favorite.";
        public const string RespondNowLine = "- Respond now with a natural thank-you or reaction to the completed request/step. Do not mention the game's daily gift limit.";

        public static string StatusLine(NpcHelpRequestFact request) =>
            $"- Help request status: {request.Status}; summary: {request.Summary}; resolution: {request.Resolution}";

        public static string FriendshipRewardLine(int rewardFriendship) =>
            $"- LivingNPCs already granted the configured friendship reward (+{rewardFriendship}).";

        public static string MoneyRewardGrantedLine(int rewardMoney) =>
            $"- LivingNPCs already granted a system money reward of {rewardMoney}g.";

        public static string MoneyRewardQueuedLine(int rewardMoney) =>
            $"- LivingNPCs added a system money reward of {rewardMoney}g to the quest journal for the farmer to claim.";

        public const string ThankYouMailLine = HelpRequestHandIn.ThankYouMailLine;
    }

    /// <summary>The "## Active Companion Outing" section.</summary>
    internal static class Outing
    {
        public static string Section(string npcDisplayName, PendingCompanionOuting outing, bool farmerPresent)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("## Active Companion Outing");
            prompt.AppendLine(outing.IsShortVisit
                ? $"- {npcDisplayName} is briefly accompanying the farmer to {outing.TargetLocationLabel}."
                : $"- {npcDisplayName} and the farmer have an active shared outing to {outing.TargetLocationLabel}.");
            prompt.AppendLine($"- Phase: {PhaseText(outing.Phase)}.");
            prompt.AppendLine($"- Shared activity: {CompanionOutingRules.GetActivityPromptLabel(outing.ActivityStyle)}.");
            prompt.AppendLine($"- The farmer is {(farmerPresent ? "currently present with the NPC" : "temporarily elsewhere")}.");
            if (outing.Phase == CompanionOutingPhase.AtDestination)
            {
                prompt.AppendLine($"- They arrived at {BehaviorTimeMath.FormatTime(outing.ArrivalTimeOfDay)}.");
                prompt.AppendLine($"- The NPC is settled {outing.AnchorLabel}.");
                prompt.AppendLine($"- Shared time together at the destination so far: about {outing.SharedMinutesAtDestination} game minutes.");
                if (outing.IsShortVisit)
                {
                    prompt.AppendLine("- This is a brief escort or short visit, not a full outing.");
                }
            }
            else if (CompanionOutingRules.IsTravelingPhase(outing.Phase))
            {
                prompt.AppendLine(outing.Phase == CompanionOutingPhase.TravelingToVehicleGateway
                    ? "- The NPC is walking to where they will ride across together (bus, boat, or entrance); the ride itself is instant, like the farmer's own trip."
                    : "- The NPC is walking there through normal doors and map exits; this is not a teleport or an escort task.");
            }
            else
            {
                prompt.AppendLine("- The planned stay has ended and the NPC is naturally returning to the day's schedule.");
            }

            prompt.AppendLine("- Let the place, time, weather, nearby people, and shared company shape the reply naturally.");
            prompt.AppendLine("- Do not announce travel status, give route instructions, repeat the agreement, or describe game mechanics.");
            return prompt.ToString().TrimEnd();
        }

        public static string PhaseText(CompanionOutingPhase phase) => phase switch
        {
            _ when CompanionOutingRules.IsTravelingPhase(phase) => "traveling naturally toward the destination",
            CompanionOutingPhase.AtDestination => "spending time together at the destination",
            _ => "returning to the normal daily schedule"
        };

        /// <summary>
        /// 多人 v1：远程 farmhand 的会话里出游不可安排（出游由主机驱动、farmhand 上报的
        /// 出游动作会被主机丢弃）。显式告知模型婉拒，避免口头答应却无人出发。
        /// </summary>
        public static string UnavailableSection() =>
            "## Companion Outing Unavailable\n"
            + "- Going somewhere together cannot be arranged in this conversation. If the farmer "
            + "invites you out, warmly decline for now without promising a specific later time.";
    }

    /// <summary>The micro-behavior planner request sent by <see cref="AiBehaviorClient"/>.</summary>
    internal static class Planner
    {
        public const string SystemMessage =
            "You choose tiny, safe Stardew Valley NPC behavior intents. Return only JSON. "
            + PromptDataBoundary.SystemRule;

        public static string UserPrompt(
            NPC npc,
            BehaviorTrigger trigger,
            IReadOnlyList<string> allowedIntents,
            WorldContextSnapshot world,
            NpcDispositionProfile disposition,
            int year,
            double distanceToFarmerTiles)
        {
            string nearby = string.Join(", ", world.NearbyNpcNames);

            var scene = new StringBuilder();
            scene.AppendLine($"NPC: {npc.displayName} ({npc.Name})");
            scene.AppendLine($"Profile source: {disposition.SourceLabel}");
            scene.AppendLine($"Disposition: {disposition.PromptLabel}");
            if (disposition.HasProfileContext)
            {
                scene.AppendLine($"Profile context: {disposition.BackgroundPrompt} {disposition.DialoguePrompt}");
            }

            scene.AppendLine($"Trigger: {trigger}");
            scene.AppendLine($"Location: {world.LocationDisplayName} ({world.LocationName})");
            scene.AppendLine($"Date: year {year}, {world.Season} {world.DayOfMonth}");
            scene.AppendLine($"Time: {world.TimeOfDay}");
            scene.AppendLine($"Scene context: {world.PromptLabel}");
            scene.AppendLine($"World knowledge available to this NPC: {world.ProgressionKnowledge.PromptLabel}");
            scene.AppendLine($"Distance to farmer in tiles: {System.Math.Round(distanceToFarmerTiles, 1)}");
            scene.AppendLine($"Nearby NPCs: {(string.IsNullOrWhiteSpace(nearby) ? "none" : nearby)}");

            var prompt = new StringBuilder();
            prompt.AppendLine(PromptDataBoundary.InstructionReminder);
            prompt.AppendLine(PromptDataBoundary.Wrap("behavior_planner_scene", scene.ToString()));
            prompt.AppendLine();
            prompt.AppendLine($"Allowed intents: {string.Join(", ", allowedIntents)}");
            prompt.AppendLine();
            prompt.AppendLine("Choose one tiny behavior that makes the NPC feel alive without disrupting schedules.");
            prompt.AppendLine("Return exactly this JSON shape:");
            prompt.AppendLine("{\"intent\":\"FacePlayer|Emote|ApproachPlayer|Pause|LookAround|StepAway\",\"reason\":\"short in-world reason\",\"emoteId\":16}");
            return prompt.ToString();
        }
    }
}
