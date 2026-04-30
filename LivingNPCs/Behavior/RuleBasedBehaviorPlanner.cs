using System;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Behavior;

internal sealed class RuleBasedBehaviorPlanner : IBehaviorPlanner
{
    private readonly ModConfig config;
    private readonly Random random;

    public RuleBasedBehaviorPlanner(ModConfig config, Random random)
    {
        this.config = config;
        this.random = random;
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

        if (trigger == BehaviorTrigger.Manual && this.config.AllowApproachPlayer && distance is > 2.25f and <= 6f)
        {
            return new BehaviorIntent(
                BehaviorIntentType.ApproachPlayer,
                npc.Name,
                "they chose to step closer to the farmer before talking"
            );
        }

        if (this.config.AllowEmotes && this.random.NextDouble() < (trigger == BehaviorTrigger.Manual ? 0.35 : 0.25))
        {
            return new BehaviorIntent(
                BehaviorIntentType.Emote,
                npc.Name,
                trigger == BehaviorTrigger.Manual
                    ? "they noticed the farmer nearby and reacted naturally"
                    : "something about the moment caught their attention",
                trigger == BehaviorTrigger.Manual ? this.config.ManualEmoteId : 16
            );
        }

        return new BehaviorIntent(
            BehaviorIntentType.FacePlayer,
            npc.Name,
            trigger == BehaviorTrigger.Manual
                ? "they noticed the farmer nearby and turned toward them"
                : "they briefly acknowledged the farmer while going about their day"
        );
    }
}
