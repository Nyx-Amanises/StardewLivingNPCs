using System;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class EmotionalExpressionStyle
{
    public static EmotionalExpressionCue For(NPC npc)
    {
        return For(npc.Name, NpcDisposition.For(npc));
    }

    public static EmotionalExpressionCue For(string npcName, NpcDispositionProfile disposition)
    {
        string name = npcName ?? string.Empty;
        if (IsAny(name, "Flor"))
        {
            return new EmotionalExpressionCue(
                "ReflectiveCareful",
                "express hurt quietly and thoughtfully; becomes more measured than cold, and may need time before naming the feeling directly",
                "细腻克制，受伤时会更安静、更谨慎",
                "if hurt or disappointed, soften outward emotion into careful distance, reflective pauses, and small guarded wording rather than blunt accusation",
                "after repair, acknowledge recovery gently and with emotional precision, without suddenly becoming bubbly",
                "let negative emotion show as quiet caution, not generic anger",
                18,
                72,
                0.9,
                0.8,
                0.95,
                1
            );
        }

        if (IsAny(name, "Shane"))
        {
            return new EmotionalExpressionCue(
                "GuardedBlunt",
                "express hurt through guarded bluntness, dry deflection, or clipped replies; warmth returns slowly and must feel earned",
                "嘴硬防备，容易直接但不轻易袒露",
                "if upset, he may sound curt or defensive; do not make him politely serene when he is actually hurt",
                "after repair, let him soften awkwardly or briefly rather than giving a polished apology scene",
                "let negative emotion show through guarded directness and reluctance to be vulnerable",
                76,
                78,
                0.95,
                0.75,
                0.85,
                1
            );
        }

        if (IsAny(name, "Haley"))
        {
            return new EmotionalExpressionCue(
                "SharpQuick",
                "express displeasure visibly and sharply, but smaller slights fade faster once the moment passes",
                "外露锋利，小摩擦消得快",
                "if upset, she can be visibly annoyed, proud, or cutting; keep it characterful rather than generically cruel",
                "after repair, let her pivot back with a small proud remark or softened confidence instead of heavy emotional processing",
                "let negative emotion show clearly, then allow quick recovery when the issue is minor",
                84,
                42,
                1.3,
                1.2,
                1.15,
                0
            );
        }

        if (IsAny(name, "Harvey"))
        {
            return new EmotionalExpressionCue(
                "PoliteAnxious",
                "express concern or hurt politely, often through hesitation, careful wording, and worry rather than confrontation",
                "礼貌焦虑，不太正面冲突",
                "if hurt, keep him composed and a little anxious; he is more likely to set a careful boundary than accuse",
                "after repair, let relief show in a modest, sincere way, with professional restraint still present",
                "let negative emotion show as worry, careful distance, or gentle boundaries",
                24,
                60,
                1.0,
                0.95,
                1.0,
                0
            );
        }

        if (IsAny(name, "Claire", "Sophia", "Shiro", "Penny"))
        {
            return new EmotionalExpressionCue(
                "SoftWithdrawn",
                "express hurt by withdrawing, becoming quieter, or losing ease; direct confrontation should be rare unless trust is high",
                "柔软退缩，受伤时更安静",
                "if hurt, reduce ease and openness; use hesitance, small boundaries, and guarded softness before direct confrontation",
                "after repair, let warmth return slowly through small signs rather than immediate full trust",
                "let negative emotion show through reduced openness and careful wording",
                22,
                70,
                0.9,
                0.82,
                0.9,
                1
            );
        }

        if (IsAny(name, "Sebastian", "Zayne", "Jio", "Isaac"))
        {
            return new EmotionalExpressionCue(
                "QuietGuarded",
                "express hurt by getting sparse, cool, and guarded; may remember slights without volunteering the reason",
                "寡言防备，记得久但未必说",
                "if upset, make replies shorter and more guarded; do not overexplain feelings unless trust or the scene justifies it",
                "after repair, show change through less resistance and slightly warmer brevity, not sudden openness",
                "let negative emotion show as distance, brevity, and controlled boundaries",
                38,
                82,
                0.9,
                0.72,
                0.85,
                1
            );
        }

        if (IsAny(name, "George", "Pam", "Morris", "Maive"))
        {
            return new EmotionalExpressionCue(
                "BluntDirect",
                "express displeasure plainly, sometimes brusquely; small repairs are possible, but pride may slow full softness",
                "直接强硬，不开心会说出来",
                "if upset, allow bluntness, impatience, or a firm boundary; keep it grounded rather than melodramatic",
                "after repair, let acceptance be practical and brief rather than sentimental",
                "let negative emotion show directly but proportionately",
                82,
                62,
                1.05,
                0.9,
                0.9,
                0
            );
        }

        if (IsAny(name, "Emily", "Sam", "Sandy", "Maddie", "Sean"))
        {
            return new EmotionalExpressionCue(
                "OpenExpressive",
                "express feelings visibly and recover relatively easily when the farmer responds kindly",
                "外露，好哄但会表现出来",
                "if upset, let the feeling show openly but avoid making it vindictive",
                "after repair, warmth can return with visible relief or renewed playfulness if the scene supports it",
                "let emotion be visible and human, then move on more readily after kindness",
                68,
                38,
                1.25,
                1.15,
                1.2,
                0
            );
        }

        string label = $"{disposition.PromptLabel} {disposition.BackgroundPrompt} {disposition.DialoguePrompt} {disposition.Reason}".ToLowerInvariant();
        if (ContainsAny(label, "blunt", "direct", "sharp", "strong-willed", "confident", "intense"))
        {
            return new EmotionalExpressionCue(
                "Direct",
                "express negative emotion plainly and with visible boundaries",
                "直接表达",
                "if hurt, allow direct wording or a firm boundary before warmth returns",
                "after repair, keep the recovery concrete and not overly sentimental",
                "let negative emotion be stated plainly when it matters",
                72,
                58,
                1.05,
                0.95,
                1.0,
                0
            );
        }

        if (ContainsAny(label, "guarded", "reserved", "slow to trust", "wary", "shy", "anxious", "timid"))
        {
            return new EmotionalExpressionCue(
                "Reserved",
                "express hurt indirectly through distance, shorter replies, and lowered openness",
                "含蓄疏远",
                "if hurt, avoid cheerful default warmth; use careful distance and guarded brevity",
                "after repair, show gradual softening before open warmth",
                "let negative emotion show as restraint rather than overt confrontation",
                28,
                72,
                0.95,
                0.82,
                0.9,
                1
            );
        }

        if (ContainsAny(label, "formal", "professional", "polished", "courteous", "composed", "measured"))
        {
            return new EmotionalExpressionCue(
                "Measured",
                "express difficult feelings through controlled politeness and precise boundaries",
                "礼貌克制",
                "if upset, keep wording controlled and socially careful, but reduce warmth",
                "after repair, acknowledge it with composed sincerity",
                "let negative emotion show under politeness rather than disappearing",
                34,
                58,
                1.0,
                0.95,
                1.0,
                0
            );
        }

        if (ContainsAny(label, "warm", "gentle", "caring", "kind", "approachable", "motherly"))
        {
            return new EmotionalExpressionCue(
                "WarmCareful",
                "express hurt with care and reluctance, often trying to preserve the relationship while naming a boundary",
                "温和但会在意",
                "if hurt, keep empathy present but do not erase the hurt; a gentle boundary can fit",
                "after repair, let warmth return naturally with a small acknowledgement",
                "let negative emotion stay kind but real",
                36,
                55,
                1.05,
                1.0,
                1.05,
                0
            );
        }

        if (ContainsAny(label, "expressive", "dramatic", "lively", "outgoing", "energetic", "playful"))
        {
            return new EmotionalExpressionCue(
                "Expressive",
                "express feelings visibly and may shift emotional color faster than more guarded NPCs",
                "情绪外露",
                "if upset, show it clearly in voice and pacing, then scale it to the actual severity",
                "after repair, visible relief or renewed energy is allowed",
                "let emotion be visible, but keep it proportionate",
                66,
                44,
                1.2,
                1.1,
                1.1,
                0
            );
        }

        if (ContainsAny(label, "thoughtful", "observant", "scholarly", "curious", "perceptive"))
        {
            return new EmotionalExpressionCue(
                "Reflective",
                "process feelings thoughtfully; may name the pattern more than the raw emotion",
                "会思考和复盘",
                "if hurt, express it through thoughtful observation, careful questions, or measured distance",
                "after repair, a brief reflective line can make the recovery feel earned",
                "let negative emotion be thoughtful and specific rather than generic",
                42,
                62,
                1.0,
                0.95,
                1.0,
                0
            );
        }

        return new EmotionalExpressionCue(
            "Balanced",
            "express emotion naturally without a strong special pattern",
            "自然平衡",
            "if hurt, reduce warmth and let the reply fit the severity without exaggeration",
            "after repair, return warmth gradually and naturally",
            "let emotion color the line without taking over",
            48,
            50,
            1.0,
            1.0,
            1.0,
            0
        );
    }

    private static bool IsAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record EmotionalExpressionCue(
    string Key,
    string PromptLabel,
    string DebugLabel,
    string ConflictPromptLabel,
    string RepairPromptLabel,
    string ReplyGuidance,
    int Directness,
    int MemoryHold,
    double EmotionDecayMultiplier,
    double ConflictDecayMultiplier,
    double RepairResponsivenessMultiplier,
    int ComplexRepairDelayAdjustmentDays
)
{
    public int AdjustEmotionDecay(int baseDecay)
    {
        return AdjustPositiveAmount(baseDecay, this.EmotionDecayMultiplier);
    }

    public int AdjustConflictDecay(int baseDecay)
    {
        return AdjustPositiveAmount(baseDecay, this.ConflictDecayMultiplier);
    }

    public int AdjustRepairAmount(int baseRepair)
    {
        return AdjustPositiveAmount(baseRepair, this.RepairResponsivenessMultiplier);
    }

    public int AdjustComplexRepairDelay(int baseDelayDays)
    {
        return System.Math.Max(1, baseDelayDays + this.ComplexRepairDelayAdjustmentDays);
    }

    public string DebugSummaryLabel =>
        $"{this.DebugLabel}（直接度 {this.Directness}/100，记仇/记痛 {this.MemoryHold}/100）";

    private static int AdjustPositiveAmount(int amount, double multiplier)
    {
        if (amount <= 0)
        {
            return amount;
        }

        return System.Math.Max(1, (int)System.Math.Round(amount * multiplier));
    }
}
