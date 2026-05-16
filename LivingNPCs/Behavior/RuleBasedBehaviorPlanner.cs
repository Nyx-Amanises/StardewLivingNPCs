using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class RuleBasedBehaviorPlanner : IBehaviorPlanner
{
    private readonly ModConfig config;
    private readonly Random random;
    private readonly BehaviorMemory memory;

    public RuleBasedBehaviorPlanner(ModConfig config, Random random, BehaviorMemory memory)
    {
        this.config = config;
        this.random = random;
        this.memory = memory;
    }

    public BehaviorIntent ChooseIntent(NPC npc, BehaviorTrigger trigger)
    {
        if (trigger == BehaviorTrigger.Manual && !string.Equals(this.config.ManualBehaviorMode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return this.config.ManualBehaviorMode switch
            {
                "FacePlayer" => new BehaviorIntent(BehaviorIntentType.FacePlayer, npc.Name, "the farmer manually asked for a face-player test"),
                "Emote" => new BehaviorIntent(BehaviorIntentType.Emote, npc.Name, "the farmer manually asked for an emote test", this.config.ManualEmoteId),
                "ApproachPlayer" => new BehaviorIntent(BehaviorIntentType.ApproachPlayer, npc.Name, "the farmer manually asked for an approach-player test"),
                "Pause" => new BehaviorIntent(BehaviorIntentType.Pause, npc.Name, "the farmer manually asked for a pause-and-notice test"),
                "LookAround" => new BehaviorIntent(BehaviorIntentType.LookAround, npc.Name, "the farmer manually asked for a look-around test"),
                "StepAway" => new BehaviorIntent(BehaviorIntentType.StepAway, npc.Name, "the farmer manually asked for a step-away test"),
                _ => new BehaviorIntent(BehaviorIntentType.FacePlayer, npc.Name, "the farmer manually asked for a behavior test")
            };
        }

        float distance = Vector2.Distance(npc.Tile, Game1.player.Tile);
        LivingNpcState? state = this.config.EnableNpcState ? this.memory.GetState(npc) : null;
        var disposition = NpcDisposition.For(npc);
        var world = WorldContext.For(npc);
        var influence = StateInfluence.From(state, disposition, world);

        if (trigger == BehaviorTrigger.Manual
            && this.config.AllowApproachPlayer
            && distance <= 1.75f
            && this.ShouldStepAway(state)
            && this.random.NextDouble() < Math.Clamp(0.45 + influence.StepAwayBonus, 0.1, 0.85))
        {
            return new BehaviorIntent(
                BehaviorIntentType.StepAway,
                npc.Name,
                influence.HasContext
                    ? $"they kept a little more personal space ({influence.Reason})"
                    : "they gave the farmer a little more room"
            );
        }

        if (trigger == BehaviorTrigger.Manual && this.config.AllowApproachPlayer && distance > 2.25f && distance <= this.config.MaxInteractionDistanceTiles)
        {
            double approachChance = 0.55 + influence.ApproachBonus;
            if (state == null)
            {
                approachChance += 0.15;
            }

            if (this.random.NextDouble() < Math.Clamp(approachChance, 0.2, 0.95))
            {
                return new BehaviorIntent(
                    BehaviorIntentType.ApproachPlayer,
                    npc.Name,
                    influence.HasContext
                        ? $"their current context made them more willing to step closer ({influence.Reason})"
                        : "they chose to step closer to the farmer before talking"
                );
            }
        }

        double pauseChance = (trigger == BehaviorTrigger.Manual ? 0.18 : 0.1) + influence.PauseBonus;
        if (this.config.AllowFacePlayer && this.random.NextDouble() < Math.Clamp(pauseChance, 0.03, 0.5))
        {
            return new BehaviorIntent(
                BehaviorIntentType.Pause,
                npc.Name,
                influence.HasContext
                    ? $"they paused for a beat because of the current moment ({influence.Reason})"
                    : "they briefly paused when the farmer came near"
            );
        }

        double lookAroundChance = (trigger == BehaviorTrigger.Manual ? 0.16 : 0.12) + influence.LookAroundBonus;
        if (this.config.AllowFacePlayer && this.random.NextDouble() < Math.Clamp(lookAroundChance, 0.02, 0.55))
        {
            return new BehaviorIntent(
                BehaviorIntentType.LookAround,
                npc.Name,
                influence.HasContext
                    ? $"their attention drifted to the surroundings ({influence.Reason})"
                    : "they took a quick look around the area"
            );
        }

        double emoteChance = (trigger == BehaviorTrigger.Manual ? 0.35 : 0.25) + influence.EmoteBonus;
        if (this.config.AllowEmotes && this.random.NextDouble() < Math.Clamp(emoteChance, 0.05, 0.85))
        {
            return new BehaviorIntent(
                BehaviorIntentType.Emote,
                npc.Name,
                influence.HasContext
                    ? $"they reacted through their current context ({influence.Reason})"
                    : trigger == BehaviorTrigger.Manual
                        ? "they noticed the farmer nearby and reacted naturally"
                        : "something about the moment caught their attention",
                this.ChooseEmoteId(trigger, state, disposition)
            );
        }

        return new BehaviorIntent(
            BehaviorIntentType.FacePlayer,
            npc.Name,
            influence.HasContext
                ? $"their attention shifted toward the farmer ({influence.Reason})"
                : trigger == BehaviorTrigger.Manual
                    ? "they noticed the farmer nearby and turned toward them"
                    : "they briefly acknowledged the farmer while going about their day"
        );
    }

    private bool ShouldStepAway(LivingNpcState? state)
    {
        if (state == null)
        {
            return false;
        }

        return state.CurrentInclination is "NeedsSpace" or "GentleBoundary" or "Reserved"
            || state.Mood is "Overloaded" or "CrowdedButWarm" or "Guarded"
            || state.CurrentEmotion is "Jealous" or "Disappointed" or "Upset" or "Angry"
            || state.HighestUnresolvedConflictSeverity >= 25
            || state.RepeatedConversationPressure >= 40
            || state.Openness <= 25;
    }

    private int ChooseEmoteId(BehaviorTrigger trigger, LivingNpcState? state, NpcDispositionProfile disposition)
    {
        if (trigger == BehaviorTrigger.Manual)
        {
            return this.config.ManualEmoteId;
        }

        if (state == null)
        {
            return disposition.PassiveEmoteId;
        }

        return state.Mood switch
        {
            "Curious" => 8,
            "Engaged" => 32,
            "Expressive" => this.config.ManualEmoteId,
            "Warm" => 20,
            _ => disposition.PassiveEmoteId
        };
    }

    private sealed record StateInfluence(bool HasContext, double ApproachBonus, double EmoteBonus, double PauseBonus, double LookAroundBonus, double StepAwayBonus, string Reason)
    {
        public static StateInfluence From(LivingNpcState? state, NpcDispositionProfile disposition, WorldContextSnapshot world)
        {
            double approachBonus = disposition.ApproachModifier + world.ApproachModifier;
            double emoteBonus = disposition.EmoteModifier + world.EmoteModifier;
            double pauseBonus = 0;
            double lookAroundBonus = 0;
            double stepAwayBonus = 0;
            var reasons = new List<string> { disposition.Reason, world.Reason };

            if (state?.Attention >= 75)
            {
                approachBonus += 0.18;
                emoteBonus += 0.12;
                pauseBonus += 0.03;
                reasons.Add("high attention");
            }
            else if (state?.Attention >= 50)
            {
                approachBonus += 0.08;
                emoteBonus += 0.05;
                pauseBonus += 0.02;
                reasons.Add("moderate attention");
            }
            else if (state?.Attention <= 20)
            {
                approachBonus -= 0.12;
                emoteBonus -= 0.08;
                lookAroundBonus += 0.08;
                reasons.Add("low attention");
            }

            if (state?.Openness >= 70)
            {
                approachBonus += 0.2;
                reasons.Add("open to talking");
            }
            else if (state?.Openness <= 30)
            {
                approachBonus -= 0.18;
                reasons.Add("reserved mood");
            }

            if (state?.Familiarity >= 75)
            {
                approachBonus += 0.14;
                emoteBonus += 0.07;
                reasons.Add("close long-term familiarity");
            }
            else if (state?.Familiarity >= 45)
            {
                approachBonus += 0.09;
                emoteBonus += 0.04;
                reasons.Add("familiar with the farmer");
            }
            else if (state?.Familiarity >= 18)
            {
                approachBonus += 0.04;
                reasons.Add("recognizes the farmer");
            }

            if (state != null)
            {
                foreach (var influence in state.ActiveDialogueBehaviorInfluences.Take(2))
                {
                    double weight = Math.Clamp(influence.Intensity / 100d, 0.1, 1);
                    switch (influence.Type)
                    {
                        case "companion_walk":
                        case "stay_near":
                            approachBonus += 0.08 + (0.14 * weight);
                            emoteBonus += 0.02 + (0.04 * weight);
                            reasons.Add("recent conversation invited closeness");
                            break;

                        case "comforted":
                            approachBonus += 0.06 + (0.12 * weight);
                            emoteBonus += 0.03 + (0.05 * weight);
                            reasons.Add("recent reassurance");
                            break;

                        case "visit_location":
                            pauseBonus += 0.02 + (0.05 * weight);
                            lookAroundBonus += 0.05 + (0.08 * weight);
                            reasons.Add("recent place-focused conversation");
                            break;

                        case "offended":
                            approachBonus -= 0.1 + (0.18 * weight);
                            pauseBonus += 0.04 + (0.04 * weight);
                            stepAwayBonus += 0.12 + (0.18 * weight);
                            reasons.Add("recent hurtful conversation");
                            break;

                        case "give_space":
                            approachBonus -= 0.07 + (0.12 * weight);
                            stepAwayBonus += 0.1 + (0.14 * weight);
                            reasons.Add("recent request for space");
                            break;

                        case "pause_to_talk":
                            pauseBonus += 0.08 + (0.08 * weight);
                            reasons.Add("recent invitation to linger");
                            break;
                    }
                }

                if (state.HighestUnresolvedConflictSeverity > 0)
                {
                    double conflictPressure = Math.Clamp(state.HighestUnresolvedConflictSeverity / 100d, 0.05, 1);
                    approachBonus -= 0.12 + (0.2 * conflictPressure);
                    emoteBonus -= 0.03 + (0.06 * conflictPressure);
                    pauseBonus += 0.05 + (0.08 * conflictPressure);
                    stepAwayBonus += 0.18 + (0.3 * conflictPressure);
                    reasons.Add("unresolved conflict");
                }

                switch (state.InteractionRhythm)
                {
                    case "CrowdedToday":
                        double pressure = Math.Clamp((state.RepeatedConversationPressure + 20) / 100d, 0.05, 1);
                        approachBonus -= 0.08 + (0.14 * pressure);
                        emoteBonus -= 0.02 + (0.04 * pressure);
                        stepAwayBonus += 0.15 + (0.25 * pressure);
                        reasons.Add($"{state.InteractionComfortTier.ToLowerInvariant()} relationship repeat pressure");
                        break;

                    case "AtComfortLimit":
                        if (state.InteractionComfortTier is "Intimate" or "Trusted")
                        {
                            approachBonus += 0.02;
                            emoteBonus += 0.01;
                            reasons.Add("close relationship can handle another short check-in");
                        }
                        else
                        {
                            approachBonus -= 0.04;
                            reasons.Add("near today's comfort limit");
                        }

                        break;

                    case "PoliteRepeat":
                        approachBonus -= 0.07;
                        emoteBonus += 0.01;
                        pauseBonus += 0.05;
                        reasons.Add("repeated chat with low relationship");
                        break;

                    case "CheckedInAgain":
                        approachBonus += state.InteractionComfortTier == "Friendly" ? 0.01 : -0.02;
                        emoteBonus += 0.02;
                        reasons.Add("already checked in today");
                        break;

                    case "ComfortableRepeat":
                        approachBonus += 0.06;
                        emoteBonus += 0.03;
                        reasons.Add("comfortable repeated chat");
                        break;
                }
            }

            if (state?.ConsecutiveConversationDays >= 5)
            {
                approachBonus += 0.08;
                emoteBonus += 0.03;
                reasons.Add("familiar daily routine");
            }
            else if (state?.ConsecutiveConversationDays >= 3)
            {
                approachBonus += 0.04;
                reasons.Add("building a daily routine");
            }

            if (state?.LastConversationGapDays >= 7)
            {
                approachBonus -= 0.02;
                emoteBonus += 0.06;
                reasons.Add("speaking again after time apart");
            }

            switch (state?.Mood)
            {
                case "Engaged":
                case "Warm":
                case "Comfortable":
                case "Familiar":
                    approachBonus += 0.12;
                    reasons.Add(state.Mood.ToLowerInvariant());
                    break;

                case "Expressive":
                case "Surprised":
                    emoteBonus += 0.25;
                    reasons.Add("expressive mood");
                    break;

                case "Curious":
                    approachBonus += 0.05;
                    emoteBonus += 0.08;
                    lookAroundBonus += 0.06;
                    reasons.Add("curious mood");
                    break;

                case "Overloaded":
                    approachBonus -= 0.16;
                    emoteBonus -= 0.05;
                    stepAwayBonus += 0.24;
                    reasons.Add("needs a little space");
                    break;

                case "CrowdedButWarm":
                    approachBonus -= 0.04;
                    emoteBonus += 0.01;
                    stepAwayBonus += 0.08;
                    reasons.Add("close but repeated attention");
                    break;

                case "Polite":
                    approachBonus -= 0.04;
                    pauseBonus += 0.05;
                    reasons.Add("polite reserve");
                    break;

                case "Guarded":
                case "Focused":
                    pauseBonus += 0.08;
                    lookAroundBonus += 0.08;
                    reasons.Add("watchful mood");
                    break;
            }

            switch (state?.CurrentEmotion)
            {
                case "Happy":
                    approachBonus += 0.07;
                    emoteBonus += 0.04;
                    reasons.Add("happy emotion");
                    break;

                case "Jealous":
                    approachBonus -= 0.08;
                    pauseBonus += 0.04;
                    stepAwayBonus += 0.08;
                    reasons.Add("jealous emotion");
                    break;

                case "Worried":
                    approachBonus += 0.03;
                    emoteBonus += 0.03;
                    pauseBonus += 0.05;
                    reasons.Add("worried emotion");
                    break;

                case "Grateful":
                    approachBonus += 0.08;
                    emoteBonus += 0.05;
                    reasons.Add("grateful emotion");
                    break;

                case "Disappointed":
                    approachBonus -= 0.1;
                    pauseBonus += 0.06;
                    stepAwayBonus += 0.1;
                    reasons.Add("disappointed emotion");
                    break;

                case "Uneasy":
                    approachBonus -= 0.05;
                    pauseBonus += 0.04;
                    stepAwayBonus += 0.04;
                    reasons.Add("uneasy emotion");
                    break;

                case "Upset":
                    approachBonus -= 0.12;
                    pauseBonus += 0.06;
                    stepAwayBonus += 0.12;
                    reasons.Add("upset emotion");
                    break;

                case "Angry":
                    approachBonus -= 0.22;
                    emoteBonus -= 0.08;
                    pauseBonus += 0.08;
                    stepAwayBonus += 0.24;
                    reasons.Add("angry emotion");
                    break;

                case "Sad":
                    approachBonus -= 0.08;
                    pauseBonus += 0.08;
                    reasons.Add("sad emotion");
                    break;
            }

            string reason = reasons.Count > 0
                ? string.Join(", ", reasons)
                : "steady neutral state";

            return new StateInfluence(true, approachBonus, emoteBonus, pauseBonus, lookAroundBonus, stepAwayBonus, reason);
        }
    }
}
