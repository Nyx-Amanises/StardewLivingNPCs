using System.Collections.Generic;
using System.Linq;
using System.Text;
using StardewValley;

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
            ? "no recent LivingNPCs gift memory"
            : $"last recorded gift: the farmer offered {state.LastGiftName}; gift taste: {state.LastGiftTaste}; gifts recorded today: {state.GiftsToday}";

        public static string LastEvent(LivingNpcState state) => string.IsNullOrWhiteSpace(state.LastEventContext)
            ? "no recent LivingNPCs event memory"
            : $"last recorded event context: {state.LastEventContext}";

        public static string LongTermMemories(LivingNpcState state)
        {
            var memories = state.GetTopLongTermMemories(4).ToList();
            return memories.Count == 0
                ? "no durable personal memory has been recorded"
                : string.Join("; ", memories.Select(memory => memory.Summary));
        }

        public static string PlayerPreferences(LivingNpcState state)
        {
            var preferences = state.GetTopPlayerPreferences(6).ToList();
            return preferences.Count == 0
                ? "no durable farmer preference memory has been recorded"
                : string.Join("; ", preferences.Select(memory => memory.Summary));
        }

        public static string CommunityImpressions(LivingNpcState state)
        {
            var memories = state.GetTopCommunityImpressions(4).ToList();
            return memories.Count == 0
                ? "no community impression about the farmer has been recorded"
                : string.Join("; ", memories.Select(Facts.CommunityImpression));
        }

        public static string SharedExperiences(LivingNpcState state)
        {
            var experiences = state.GetTopSharedExperiences(4).ToList();
            return experiences.Count == 0
                ? "no durable shared experiences are recorded"
                : string.Join("; ", experiences.Select(Facts.SharedExperience));
        }

        public static string DialogueBehaviorInfluences(LivingNpcState state)
        {
            var influences = state.ActiveDialogueBehaviorInfluences.Take(4).ToList();
            return influences.Count == 0
                ? "no active conversation-driven behavior tendency"
                : string.Join("; ", influences.Select(Facts.DialogueBehaviorInfluence));
        }

        public static string HelpRequests(LivingNpcState state)
        {
            var requests = state.GetTopHelpRequests(4).ToList();
            return requests.Count == 0
                ? "no durable help requests are recorded"
                : string.Join("; ", requests.Select(Facts.HelpRequest));
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
                ? "no durable conflict memory has been recorded"
                : string.Join("; ", conflicts.Select(Facts.Conflict));
        }

        public static string FarmerNickname(LivingNpcState state)
        {
            if (string.IsNullOrWhiteSpace(state.FarmerNickname))
            {
                return "no personal name preference has been recorded";
            }

            return state.FarmerNicknameStatus switch
            {
                "Accepted" => $"the farmer asked to be called {state.FarmerNickname}, and this NPC accepted; when choosing to address the farmer, use that name instead of @ or the save-file name, and never combine both names in one reply",
                "Rejected" => $"the farmer asked to be called {state.FarmerNickname}, but this NPC did not accept; do not use that name unless the relationship later changes",
                _ => $"the farmer asked to be called {state.FarmerNickname}; acceptance is unclear, so the NPC may decide whether to use it based on personality and relationship"
            };
        }
    }

    /// <summary>One-line prompt descriptions of individual memory fact records.</summary>
    internal static class Facts
    {
        public static string CommunityImpression(CommunityImpressionFact memory) => memory.Source switch
        {
            "Witnessed" => $"directly witnessed, {memory.FreshnessStage} ({memory.Visibility.ToLowerInvariant()}): {memory.Summary}",
            "CloseCircle" => $"heard through a close connection after {memory.TransmissionDepth} retelling(s), {memory.FreshnessStage} ({memory.Visibility.ToLowerInvariant()}): {memory.Summary}",
            _ => $"picked up as a faint public impression after {memory.TransmissionDepth} retelling(s), {memory.FreshnessStage} ({memory.Visibility.ToLowerInvariant()}): {memory.Summary}"
        };

        public static string SharedExperience(SharedExperienceFact experience) =>
            $"{experience.Type} at {experience.LocationLabel}; shared on total day {experience.CreatedTotalDays}; summary: {experience.Summary}";

        public static string DialogueBehaviorInfluence(DialogueBehaviorInfluenceFact influence) =>
            $"{influence.Type}, intensity {influence.Intensity}/100, target {influence.TargetLocationLabel}, status {influence.Status}, expires total day {influence.ExpiresTotalDays}; summary: {influence.Summary}";

        public static string HelpRequest(NpcHelpRequestFact request) =>
            $"{request.Type}, due on total day {request.DueTotalDays}, status {request.Status}, step {System.Math.Min(request.CurrentStepIndex + 1, System.Math.Max(1, request.Steps.Count))}/{System.Math.Max(1, request.Steps.Count)}; current step: {HelpRequestCurrentStep(request)}; summary: {request.Summary}";

        public static string HelpRequestFulfilled(NpcHelpRequestFact request) =>
            $"{request.Type} was fulfilled on total day {request.FulfilledTotalDays}; summary: {request.Summary}; follow-up potential: {request.FollowUpPotential}";

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

        public static string ConflictResolved(NpcConflictFact conflict) =>
            $"resolved conflict from total day {conflict.CreatedTotalDays}, cause {conflict.CauseKind}: {conflict.Summary}";
    }

    /// <summary>Recall-focus lines built from the per-reply memory recall plan.</summary>
    internal static class Recall
    {
        public static string LongTermMemories(IReadOnlyList<LongTermMemorySelection> selections)
        {
            return selections.Count == 0
                ? "no durable personal memory is especially relevant right now"
                : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
        }

        public static string PlayerPreferences(IReadOnlyList<PlayerPreferenceSelection> selections)
        {
            return selections.Count == 0
                ? "no durable farmer preference memory is especially relevant right now"
                : string.Join("; ", selections.Select(selection => selection.Memory.Summary));
        }

        public static string CommunityImpressions(NPC npc, IReadOnlyList<CommunityImpressionSelection> selections)
        {
            CommunityReactionCue reaction = CommunityReactionStyle.For(npc);
            return selections.Count == 0
                ? "no community impression is especially relevant right now"
                : $"observer tendency: {reaction.PromptLabel}; retelling tendency: {reaction.RetellingPromptLabel}; {string.Join("; ", selections.Select(selection => Facts.CommunityImpression(selection.Memory)))}";
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
            "- Treat this as current body language, relationship memory, and scene pressure, not text to quote.",
            "- Stay in character; do not mention LivingNPCs, mods, prompts, AI, JSON, or context notes.",
            "- Use at most one or two relevant details naturally. If none fit, let this only shape tone and pacing.",
            "- The next reply should be a normal Stardew Valley NPC line, not a status report."
        };
        public const string ConciseRules = "Rules: hidden body language and memory for the next in-character reply; do not quote it or mention LivingNPCs/AI/JSON; use at most one or two details naturally.";

        // ---- conversation stance ----
        public const string StanceHeading = "Conversation stance:";

        public static string ConversationStance(
            NPC npc,
            LivingNpcState? state,
            NpcDispositionProfile disposition,
            WorldContextSnapshot world,
            EmotionalExpressionCue emotionalStyle)
        {
            if (state == null)
            {
                return $"{npc.displayName} should sound {disposition.PromptLabel}, with emotional expression style: {emotionalStyle.PromptLabel}; shaped by {disposition.SourceLabel} profile context and the current scene: {world.PromptLabel}.";
            }

            string tone = ToneCue(state);
            string rhythm = RhythmCue(state);
            string scenePressure = world.StateInfluence.HasMood
                ? $"scene pressure suggests {world.StateInfluence.Mood}/{world.StateInfluence.Inclination}"
                : "scene pressure is mild";

            return $"{npc.displayName} should sound {tone}; temperament is {disposition.PromptLabel}; emotional expression style is {emotionalStyle.PromptLabel}; profile source is {disposition.SourceLabel}; relationship is {State.Familiarity(state)}; {rhythm}; {scenePressure}.";
        }

        public static string ToneCue(LivingNpcState state)
        {
            string emotion = state.CurrentEmotion switch
            {
                "Happy" => "genuinely pleased",
                "Jealous" => "a little jealous",
                "Worried" => "worried",
                "Grateful" => "grateful",
                "Disappointed" => "disappointed",
                "Uneasy" => "slightly uneasy",
                "Upset" => "not entirely pleased",
                "Angry" => "angry",
                "Sad" => "sad",
                _ => "calm"
            };

            string attention = state.Attention switch
            {
                >= 75 => "attentive",
                >= 45 => "aware",
                _ => "lightly distracted"
            };

            string openness = state.Openness switch
            {
                >= 75 => "open",
                >= 45 => "measured",
                _ => "reserved"
            };

            return $"{state.Mood.ToLowerInvariant()}, {emotion}, {attention}, and {openness}";
        }

        public static string RhythmCue(LivingNpcState state)
        {
            return state.InteractionRhythm switch
            {
                "CrowdedToday" => "the farmer's attention is starting to feel repetitive today",
                "AtComfortLimit" => "the farmer is near today's comfortable conversation limit",
                "ComfortableRepeat" => "repeated conversation still feels natural because the relationship is close",
                "PoliteRepeat" => "repeated conversation should stay polite rather than overly warm",
                "DailyRoutine" or "BuildingRoutine" => "the farmer checking in has become part of a familiar routine",
                "LongQuietGap" or "AfterLongGap" => "there is a noticeable gap since the last recorded conversation",
                "FreshToday" => "this is the first recorded conversation today",
                "FirstConversation" => "this is the first recorded LivingNPCs conversation",
                _ => State.InteractionRhythm(state)
            };
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
        public static string MoodLine(LivingNpcState state) => $"- Mood: {state.Mood}; attention to farmer: {state.Attention}/100; response inclination: {state.CurrentInclination}.";
        public static string MoodLineConcise(LivingNpcState state) => $"- Mood: {state.Mood}; emotion: {State.Emotion(state)}; inclination: {state.CurrentInclination}.";
        public static string EmotionLine(LivingNpcState state) => $"- Interpersonal emotion: {State.Emotion(state)}.";
        public static string ExpressionStyleLine(string styleLabel) => $"- Emotional expression style: {styleLabel}.";
        public static string FamiliarityLine(LivingNpcState state) => $"- Long-term familiarity with the farmer: {state.Familiarity}/100 ({State.Familiarity(state)}).";
        public static string TrustLine(LivingNpcState state) => $"- Relationship trust in the farmer: {State.RelationshipTrust(state)}.";
        public static string SecretSharingLine(LivingNpcState state) => $"- Secret-sharing depth: {State.SecretSharing(state)}.";
        public static string RhythmComfortLine(LivingNpcState state) => $"- Relationship-aware interaction rhythm: {State.InteractionRhythm(state)}; comfort tier: {State.InteractionComfortTier(state)}.";
        public static string FamiliarityTrustRhythmLineConcise(LivingNpcState state) => $"- Familiarity {state.Familiarity}/100; trust: {State.RelationshipTrust(state)}; rhythm: {State.InteractionRhythm(state)}.";
        public static string GiftContextLine(LivingNpcState state) => $"- Recent gift context: {State.LastGift(state)}.";
        public static string EventContextLine(LivingNpcState state) => $"- Recent event context: {State.LastEvent(state)}.";
        public static string MemoryStoreLine(int longTermCount, string recallFocus) => $"- Durable memory store: {longTermCount} long-term memories tracked; recall focus for this reply: {recallFocus}.";
        public static string RelationshipImpressionLine(string impression) => $"- Long-term relationship impression (older memories compressed into background; treat as settled history, not recent events): {impression}";
        public static string KnownPreferencesLine(string recallFocus) => $"- Known farmer preferences: {recallFocus}.";
        public static string BehaviorTendenciesLine(LivingNpcState state) => $"- Conversation-driven behavior tendencies: {State.DialogueBehaviorInfluences(state)}.";
        public static string SharedExperiencesLine(LivingNpcState state) => $"- Shared experiences with the farmer: {State.SharedExperiences(state)}.";
        public static string HelpRequestsLine(LivingNpcState state) => $"- Help requests involving the farmer: {State.HelpRequests(state)}.";
        public static string CommunityImpressionsLine(string recallText) => $"- Community impressions about the farmer's ties with other NPCs: {recallText}.";

        public static string SocialCirclesLine(IReadOnlyCollection<string> circleLabels) =>
            $"- Stable community circles this NPC belongs to: {(circleLabels.Count == 0 ? "no stable small-circle affiliation is currently tracked" : string.Join(", ", circleLabels))}.";

        public const string HelpRequestLifecycleLine = "- Help-request lifecycle: Offered means the NPC has asked but the farmer has not accepted; Pending means accepted and active; only Pending requests should be treated like tasks.";
        public const string HelpRequestLifecycleLineConcise = "- Help-request lifecycle: Offered = asked but not accepted; Pending = accepted/active; only Pending is a task.";
        public static string HelpRequestReadinessLine(string readiness) => $"- Help-request readiness: {readiness}.";

        public static string HelpRequestReadinessAllowed(string reason) =>
            $"may naturally ask for one modest favor now; {reason}; if you do ask, ask once and clearly, then let the farmer reply — do not also withdraw it or answer for them in the same message";

        public static string HelpRequestReadinessBlocked(string reason) =>
            $"should not open a new help request now ({reason}); even if the farmer offers to help, gently decline or deflect rather than naming a task, accepting the favor, or committing to one";

        public static string HelpRequestFitLine(string fitLabel) => $"- Help-request fit: {fitLabel}";
        public static string ConflictMemoryLine(LivingNpcState state) => $"- Conflict memory: {State.Conflicts(state)}.";
        public static string NicknameMemoryLine(LivingNpcState state) => $"- Personal memory context: {State.FarmerNickname(state)}.";
        public static string SceneInfluenceLine(string reason) => $"- Scene influence on mood: {reason}.";
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

        public static string GiftMemoryCue(string giftName, string freshness, string taste) =>
            $"Gift memory: the farmer offered {giftName} {freshness}; taste was {taste}; let this affect warmth, surprise, or distance only if relevant.";

        public static string EventMemoryCue(string eventContext, string freshness) =>
            $"Event memory: {eventContext} ({freshness}); acknowledge only if the conversation naturally continues it.";

        public static string NicknameCue(LivingNpcState state) => $"Personal name memory: {State.FarmerNickname(state)}.";

        public static string LongTermRecallCue(string recallText) =>
            $"Relevant long-term memories for this reply: {recallText}; use at most one if it naturally matters now.";

        public static string PreferenceRecallCue(string recallText) =>
            $"Relevant farmer preference memories for this reply: {recallText}; when a gift or topic naturally matches one, it is okay to acknowledge remembering it briefly.";

        public static string CommunityImpressionCue(string recallText) =>
            $"Community impressions: {recallText}; use at most one, keep indirect reports tentative, and do not reveal knowledge the NPC would not plausibly have.";

        public static string BehaviorTendencyCue(DialogueBehaviorInfluenceFact influence) =>
            $"Conversation-driven behavior tendency: {Facts.DialogueBehaviorInfluence(influence)}; this should shape body language and follow-through, not be quoted as dialogue.";

        public static string ActiveHelpRequestCue(NpcHelpRequestFact request)
        {
            string lifecycle = request.Status == "Offered"
                ? "the NPC has asked, but the farmer has not clearly accepted or declined yet; do not treat it as an active task until accepted"
                : "the farmer accepted this ask; it is now an active personal task";
            return $"Active help request: {Facts.HelpRequest(request)}; {lifecycle}; remember that this is an unfinished ask, not a vague topic.";
        }

        public static string FulfilledHelpRequestCue(NpcHelpRequestFact request) =>
            $"Recently fulfilled help request: {Facts.HelpRequestFulfilled(request)}; if it fits, the NPC may briefly thank the farmer for following through.";

        public const string DefaultExpiredHelpRequestReaction = "the NPC noticed the request did not work out";

        public static string ExpiredHelpRequestCue(NpcHelpRequestFact request, string reaction) =>
            $"Unfinished help request: {Facts.HelpRequest(request)}; reaction: {reaction}; the next conversation may acknowledge this according to personality, without over-punishing if the farmer never accepted.";

        public static string SharedExperienceCue(SharedExperienceFact experience) =>
            $"Shared experience: {Facts.SharedExperience(experience)}; if it fits, the NPC may acknowledge the time they spent together without formally reciting the memory.";

        public static string LowTrustCue(int trust) =>
            $"Relationship trust is only {trust}/100; keep disclosures surface-level and avoid sudden emotional intimacy.";

        public static string HighTrustCue(int trust) =>
            $"Relationship trust is {trust}/100; deeper private honesty is allowed when the moment genuinely supports it.";

        public static string UnresolvedConflictCue(NpcConflictFact conflict, string conflictStyle) =>
            $"Unresolved conflict: {Facts.Conflict(conflict)}; while this remains unresolved, warmth should be reduced and the NPC may be brief, cool, or decline closeness; expression style: {conflictStyle}.";

        public static string ComplexRepairCue(string repairStage) =>
            $"Complex repair chain: stage {repairStage}; serious hurt may need apology, a meaningful gesture, time, and a specific restorative conversation before it is fully repaired.";

        public static string ResolvedConflictCue(NpcConflictFact conflict, string repairStyle) =>
            $"Recently resolved conflict: {Facts.ConflictResolved(conflict)}; if it fits naturally, the NPC may briefly make clear that the earlier issue is past now; recovery style: {repairStyle}.";

        public static string RhythmReminderCue(LivingNpcState state) =>
            $"Interaction rhythm: {State.InteractionRhythm(state)}; do not make every repeated talk sound equally eager.";

        public static string BoundaryCue(int repeatedConversationPressure) =>
            $"Boundary cue: repeated conversation pressure is {repeatedConversationPressure}/100; a short or gently bounded reply may fit.";

        public static string RelationshipWarmthCue(int familiarity, int hearts) =>
            $"Relationship cue: familiarity {familiarity}/100 and {hearts} hearts; match warmth to this instead of defaulting to stranger-level politeness.";

        public static string SceneCue(string reason, string mood, string inclination) =>
            $"Scene cue: {reason}; this can tint mood toward {mood} and reply style toward {inclination}.";

        public static string WorldStageCue(string replyGuidance) => $"World-stage continuity: {replyGuidance}";

        public static string NearbyNpcsCue(IReadOnlyList<string> nearbyNpcNames) =>
            $"Social cue: nearby NPCs include {string.Join(", ", nearbyNpcNames)}; keep the reply aware of public company.";

        public static string RecentMomentCue(string action, string reason) =>
            $"Most recent non-conversation moment: {action}; {reason}.";

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
        public const string GuidanceNoStateModest = "Let the reply be scene-aware and modest because there is no persistent state yet.";
        public const string GuidanceNoStateSubtle = "Keep continuity subtle; do not invent strong feelings from weak context.";
        public static string GuidanceExpressionStyle(string replyGuidance) => $"Emotion expression style: {replyGuidance}.";
        public static string GuidanceToneTarget(LivingNpcState state) => $"Tone target: {ToneCue(state)}.";
        public static string GuidanceRelationshipPacing(LivingNpcState state) => $"Relationship pacing: {State.InteractionComfortTier(state)}.";
        public static string GuidanceDisclosurePacing(LivingNpcState state) => $"Disclosure pacing: {State.SecretSharing(state)}.";
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
            return $"{relationshipPolicy}; ordinary daily schedule stops are soft constraints for LivingNPCs companion outings, so do not decline only because of a normal future destination; if the requested destination matches a current or upcoming ordinary schedule stop, treating it as going together or showing the farmer the way is especially natural; still refuse during events, sleep, severe conflict, unsafe scenes, or truly story-critical obligations";
        }

        public const string GuidanceFreshGift = "If the gift is conversationally relevant, a brief natural acknowledgement is allowed; avoid turning the whole reply into gift analysis.";
        public const string GuidancePreferenceMention = "If choosing to mention a remembered farmer preference, keep it short and human, not like reciting a profile.";
        public const string GuidanceSharedExperience = "Completed shared experiences can deepen continuity; acknowledge them naturally without turning them into a formal recap or future plan.";

        public static string GuidanceUnresolvedConflict(string conflictStyle) =>
            $"An unresolved conflict is still shaping the relationship; do not answer with default warmth, and if the conflict is severe it is okay to keep the reply short or refuse a friendly invitation; express it through this NPC's style: {conflictStyle}.";

        public const string GuidanceComplexRepair = "For a serious conflict, let repair feel gradual: a single pleasant line should not erase hurt before apology, gesture, time, and a real repair conversation have accumulated.";

        public static string GuidanceResolvedConflict(string repairStyle) =>
            $"If a recently resolved conflict is relevant, it is okay to say in a natural in-character way that the earlier matter is behind you now; recovery style: {repairStyle}.";

        public const string GuidanceRepeatedPressure = "Because the farmer has been checking in repeatedly, it is okay to sound brief, amused, busy, or gently boundary-setting.";

        public static string GuidanceSceneNudge(string reason) =>
            $"Let the scene nudge tone through {reason}, without explicitly explaining the scene mechanics.";

        public static string GuidanceWorldStage(string replyGuidance) =>
            $"Keep references to town progress, the farmer's household, and how long she has lived here consistent with what this NPC could plausibly know: {replyGuidance}";

        public const string ConciseReplyGuidanceLine = "- Reply guidance: let the mood, emotion, and relationship pace above shape tone and word choice; surface at most one or two details, and keep references to town progress, the farmer's household, and shared history consistent with what this NPC plausibly knows.";

        // ---- shared helpers ----
        public static string MemoryAge(int ageDays) => ageDays switch
        {
            0 => "today",
            1 => "yesterday",
            int.MaxValue => "at an unknown time",
            _ => $"{ageDays} days ago"
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
                "- This is an opportunity, not an obligation. If it fits the visible reply, have the NPC naturally offer a small in-game gift now and include exactly one hidden action with type give_small_gift.",
                "- If you include give_small_gift, the visible dialogue must explicitly offer the gift before the hidden metadata, using natural wording such as 'I brought you a small thing' or 'this is for you'. If the visible reply does not offer a gift, do not include the hidden action.",
                $"- Shared small gift IDs: {sharedGiftIds}.",
                $"- {npcDisplayName}'s personalized small gift IDs: {personalizedGiftIds}.",
                "- If naming a specific gift, use an itemId from the two lists above and the matching itemLabel. Generic wording such as 'a small thing' is fine when no specific item is named.",
                "- If the moment feels emotionally wrong, crowded, or abrupt, skip the gift rather than forcing it."
            );
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
                $"- Today {npcDisplayName} is inclined to ask the farmer for one small favor during this conversation.",
                "- If the visible reply allows, naturally bring up needing one concrete item from the help-request fit list, and include exactly one hidden helpRequests entry (item_request) with that itemId — do not leave the favor only in the spoken text.",
                "- Keep it brief and in character; if the moment genuinely does not fit, it is fine to wait for another day rather than forcing it."
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
        public const string FollowUpLine = "- A later in-person follow-up may happen; do not promise it as guaranteed.";
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
            else if (outing.Phase is CompanionOutingPhase.Traveling
                or CompanionOutingPhase.TravelingToFarmBoundary
                or CompanionOutingPhase.TravelingFromFarmBoundary)
            {
                prompt.AppendLine("- The NPC is walking there through normal doors and map exits; this is not a teleport or an escort task.");
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
            CompanionOutingPhase.Traveling
                or CompanionOutingPhase.TravelingToFarmBoundary
                or CompanionOutingPhase.TravelingFromFarmBoundary => "traveling naturally toward the destination",
            CompanionOutingPhase.AtDestination => "spending time together at the destination",
            _ => "returning to the normal daily schedule"
        };
    }

    /// <summary>The micro-behavior planner request sent by <see cref="AiBehaviorClient"/>.</summary>
    internal static class Planner
    {
        public const string SystemMessage = "You choose tiny, safe Stardew Valley NPC behavior intents. Return only JSON.";

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

            var prompt = new StringBuilder();
            prompt.AppendLine($"NPC: {npc.displayName} ({npc.Name})");
            prompt.AppendLine($"Profile source: {disposition.SourceLabel}");
            prompt.AppendLine($"Disposition: {disposition.PromptLabel}");
            if (disposition.HasProfileContext)
            {
                prompt.AppendLine($"Profile context: {disposition.BackgroundPrompt} {disposition.DialoguePrompt}");
            }

            prompt.AppendLine($"Trigger: {trigger}");
            prompt.AppendLine($"Location: {world.LocationDisplayName} ({world.LocationName})");
            prompt.AppendLine($"Date: year {year}, {world.Season} {world.DayOfMonth}");
            prompt.AppendLine($"Time: {world.TimeOfDay}");
            prompt.AppendLine($"Scene context: {world.PromptLabel}");
            prompt.AppendLine($"World knowledge available to this NPC: {world.ProgressionKnowledge.PromptLabel}");
            prompt.AppendLine($"Distance to farmer in tiles: {System.Math.Round(distanceToFarmerTiles, 1)}");
            prompt.AppendLine($"Nearby NPCs: {(string.IsNullOrWhiteSpace(nearby) ? "none" : nearby)}");
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
