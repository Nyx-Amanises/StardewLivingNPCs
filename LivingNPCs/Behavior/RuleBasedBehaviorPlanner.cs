using System;
using System.Collections.Generic;
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
                _ => new BehaviorIntent(BehaviorIntentType.FacePlayer, npc.Name, "the farmer manually asked for a behavior test")
            };
        }

        float distance = Vector2.Distance(npc.Tile, Game1.player.Tile);
        LivingNpcState? state = this.config.EnableNpcState ? this.memory.GetState(npc) : null;
        var disposition = NpcDisposition.For(npc);
        var world = WorldContext.For(npc);
        var influence = StateInfluence.From(state, disposition, world);

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

    private sealed record StateInfluence(bool HasContext, double ApproachBonus, double EmoteBonus, string Reason)
    {
        public static StateInfluence From(LivingNpcState? state, NpcDispositionProfile disposition, WorldContextSnapshot world)
        {
            double approachBonus = disposition.ApproachModifier + world.ApproachModifier;
            double emoteBonus = disposition.EmoteModifier + world.EmoteModifier;
            var reasons = new List<string> { disposition.Reason, world.Reason };

            if (state?.Attention >= 75)
            {
                approachBonus += 0.18;
                emoteBonus += 0.12;
                reasons.Add("high attention");
            }
            else if (state?.Attention >= 50)
            {
                approachBonus += 0.08;
                emoteBonus += 0.05;
                reasons.Add("moderate attention");
            }
            else if (state?.Attention <= 20)
            {
                approachBonus -= 0.12;
                emoteBonus -= 0.08;
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

            if (state?.ConversationsToday >= 4)
            {
                approachBonus -= 0.18;
                emoteBonus -= 0.04;
                reasons.Add("many conversations today");
            }
            else if (state?.ConversationsToday >= 2)
            {
                approachBonus -= 0.05;
                emoteBonus += 0.02;
                reasons.Add("already checked in today");
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
                    reasons.Add("curious mood");
                    break;

                case "Overloaded":
                    approachBonus -= 0.16;
                    emoteBonus -= 0.05;
                    reasons.Add("needs a little space");
                    break;
            }

            string reason = reasons.Count > 0
                ? string.Join(", ", reasons)
                : "steady neutral state";

            return new StateInfluence(true, approachBonus, emoteBonus, reason);
        }
    }
}
