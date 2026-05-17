using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class WorldContext
{
    public static WorldContextSnapshot For(NPC npc)
    {
        var location = npc.currentLocation ?? Game1.currentLocation;
        string locationName = location?.Name ?? string.Empty;
        string locationDisplayName = location?.DisplayName ?? locationName;
        bool isOutdoors = location?.IsOutdoors ?? false;
        var locationKind = DetermineLocationKind(locationName, isOutdoors);
        var weather = DetermineWeather(isOutdoors);
        var time = DetermineTime(Game1.timeOfDay);
        int friendshipHearts = GetFriendshipHearts(npc);
        var relationship = DetermineRelationship(friendshipHearts);
        var social = DetermineSocialContext(npc);
        var progression = WorldProgression.Current();
        var progressionKnowledge = WorldProgression.ForNpc(npc, friendshipHearts, locationName, progression);
        var stateInfluence = BuildStateInfluence([
            time.StateCue,
            weather.StateCue,
            locationKind.StateCue,
            relationship.StateCue,
            social.StateCue
        ]);

        double approachModifier = time.ApproachModifier
            + weather.ApproachModifier
            + locationKind.ApproachModifier
            + relationship.ApproachModifier
            + social.ApproachModifier;

        double emoteModifier = time.EmoteModifier
            + weather.EmoteModifier
            + locationKind.EmoteModifier
            + relationship.EmoteModifier
            + social.EmoteModifier;

        var promptParts = new List<string>
        {
            $"{time.PromptLabel}",
            $"{weather.PromptLabel}",
            $"{locationKind.PromptLabel}",
            $"relationship: {relationship.PromptLabel}",
            social.PromptLabel
        };

        var debugParts = new List<string>
        {
            $"时间：{time.DebugLabel}",
            $"天气：{weather.DebugLabel}",
            $"地点：{locationKind.DebugLabel}",
            $"关系：{relationship.DebugLabel}",
            social.DebugLabel
        };

        var reasons = new List<string>
        {
            time.Reason,
            weather.Reason,
            locationKind.Reason,
            relationship.Reason,
            social.Reason
        };

        return new WorldContextSnapshot(
            locationName,
            locationDisplayName,
            Game1.season.ToString(),
            Game1.dayOfMonth,
            Game1.timeOfDay,
            friendshipHearts,
            progression,
            progressionKnowledge,
            social.NearbyNpcNames,
            stateInfluence,
            string.Join("; ", promptParts),
            string.Join("；", debugParts),
            approachModifier,
            emoteModifier,
            string.Join(", ", reasons)
        );
    }

    private static SocialContextFactor DetermineSocialContext(NPC npc)
    {
        var location = npc.currentLocation ?? Game1.currentLocation;
        if (location == null)
        {
            return new SocialContextFactor("nearby NPCs unknown", "附近：未知", Array.Empty<string>(), 0, 0, "unknown nearby company");
        }

        var nearbyNames = location.characters
            .Where(other => other.Name != npc.Name && !string.IsNullOrWhiteSpace(other.Name) && !other.IsInvisible)
            .Select(other => new
            {
                Npc = other,
                Distance = Vector2.Distance(other.Tile, npc.Tile)
            })
            .Where(pair => pair.Distance <= 6)
            .OrderBy(pair => pair.Distance)
            .Take(4)
            .Select(pair => pair.Npc.displayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        if (nearbyNames.Length == 0)
        {
            return new SocialContextFactor("no nearby NPCs", "附近：无其他 NPC", nearbyNames, 0.02, -0.01, "quiet nearby company")
            {
                StateCue = new WorldStateCue("Quiet", "Quiet", 25, -1, 1, "quiet nearby company", "附近没人，气氛更安静")
            };
        }

        string names = string.Join(", ", nearbyNames);
        if (nearbyNames.Length == 1)
        {
            return new SocialContextFactor($"one nearby NPC: {names}", $"附近：{names}", nearbyNames, 0, 0.01, "one nearby person")
            {
                StateCue = new WorldStateCue("Aware", "Acknowledging", 20, 1, 0, "one nearby person", "附近有人，NPC 会留意周围")
            };
        }

        return new SocialContextFactor($"several nearby NPCs: {names}", $"附近：{names}", nearbyNames, -0.02, 0.04, "public nearby company")
        {
            StateCue = new WorldStateCue("Public", "Public", 45, 4, -1, "public nearby company", "多人场景让反应更公开")
        };
    }

    private static WorldContextFactor DetermineTime(int timeOfDay)
    {
        return timeOfDay switch
        {
            < 1000 => new WorldContextFactor("early morning", "清晨", 0.03, 0, "early morning")
            {
                StateCue = new WorldStateCue("Fresh", "Aware", 20, 3, 1, "early morning freshness", "清晨精神还不错")
            },
            < 1400 => new WorldContextFactor("midday", "白天", 0.01, 0, "daytime"),
            < 1800 => new WorldContextFactor("afternoon", "下午", 0.02, 0.01, "afternoon"),
            < 2200 => new WorldContextFactor("evening", "傍晚/夜间", 0.01, 0.03, "evening")
            {
                StateCue = new WorldStateCue("Calm", "Quiet", 25, -1, 2, "evening calm", "傍晚气氛更安静")
            },
            _ => new WorldContextFactor("late night", "深夜", -0.12, -0.05, "late night")
            {
                StateCue = new WorldStateCue("Tired", "Reserved", 90, -8, -6, "late-night fatigue", "深夜让人更疲惫谨慎")
            }
        };
    }

    private static WorldContextFactor DetermineWeather(bool isOutdoors)
    {
        if (Game1.IsRainingHere())
        {
            return isOutdoors
                ? new WorldContextFactor("rainy weather outdoors", "室外下雨", -0.06, 0.04, "rainy outdoors")
                {
                    StateCue = new WorldStateCue("Hurried", "Sheltering", 75, 4, -5, "standing in the rain", "站在雨里显得匆忙")
                }
                : new WorldContextFactor("rain outside while indoors", "室内避雨", 0.03, 0.02, "sheltering from rain")
                {
                    StateCue = new WorldStateCue("Calm", "Comfortable", 35, 1, 4, "safe indoors from rain", "室内避雨让气氛放松")
                };
        }

        if (Game1.IsSnowingHere())
        {
            return isOutdoors
                ? new WorldContextFactor("snowy weather outdoors", "室外下雪", -0.03, 0.03, "snowy outdoors")
                {
                    StateCue = new WorldStateCue("Chilly", "Sheltering", 65, 2, -3, "cold snowy weather", "雪天让人有些怕冷")
                }
                : new WorldContextFactor("snow outside while indoors", "室内避雪", 0.02, 0.01, "sheltering from snow")
                {
                    StateCue = new WorldStateCue("Calm", "Comfortable", 30, 0, 3, "warm indoors during snow", "室内避雪让气氛平稳")
                };
        }

        return isOutdoors
            ? new WorldContextFactor("clear weather outdoors", "室外天气平稳", 0.01, 0, "clear outdoors")
            : new WorldContextFactor("indoors", "室内", 0, 0, "indoors");
    }

    private static WorldContextFactor DetermineLocationKind(string locationName, bool isOutdoors)
    {
        string normalized = locationName.ToLowerInvariant();
        if (normalized.Contains("saloon", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("social place", "社交场所", 0.07, 0.03, "social location")
            {
                StateCue = new WorldStateCue("Sociable", "OpenToTalk", 45, 4, 6, "being in a social place", "社交场所让人更容易开口")
            };
        }

        if (normalized.Contains("mine", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skull", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("adventure", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("tense or adventurous place", "紧张/冒险地点", -0.05, 0.07, "adventurous location")
            {
                StateCue = new WorldStateCue("Guarded", "Focused", 85, 8, -8, "dangerous or tense location", "紧张地点让人更警觉")
            };
        }

        if (normalized.Contains("shop", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("hospital", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("blacksmith", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("animal", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fish", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("workplace or shop", "工作/商店场所", 0.02, 0.01, "workplace location")
            {
                StateCue = new WorldStateCue("Focused", "Businesslike", 40, 3, -1, "being at work", "工作场所让人更专注")
            };
        }

        if (normalized.Contains("farm", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("the farmer's farm", "玩家农场", -0.02, 0.01, "farmer's farm")
            {
                StateCue = new WorldStateCue("Aware", "Careful", 35, 2, -2, "visiting the farmer's farm", "在玩家农场会稍微谨慎")
            };
        }

        return isOutdoors
            ? new WorldContextFactor("public outdoor area", "公共室外区域", 0.01, 0, "public outdoors")
            : new WorldContextFactor("quiet indoor area", "安静室内区域", -0.02, 0, "quiet indoors");
    }

    private static int GetFriendshipHearts(NPC npc)
    {
        try
        {
            return Game1.player?.getFriendshipHeartLevelForNPC(npc.Name) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static WorldContextFactor DetermineRelationship(int friendshipHearts)
    {
        return friendshipHearts switch
        {
            >= 8 => new WorldContextFactor($"{friendshipHearts} hearts, close", $"{friendshipHearts} 心，亲近", 0.1, 0.05, "close relationship")
            {
                StateCue = new WorldStateCue("Comfortable", "OpenToTalk", 50, 2, 7, "close relationship with the farmer", "亲近关系让回应更自然")
            },
            >= 4 => new WorldContextFactor($"{friendshipHearts} hearts, friendly", $"{friendshipHearts} 心，友好", 0.05, 0.03, "friendly relationship")
            {
                StateCue = new WorldStateCue("Warm", "OpenToTalk", 35, 1, 4, "friendly relationship with the farmer", "友好关系让态度更温和")
            },
            >= 2 => new WorldContextFactor($"{friendshipHearts} hearts, familiar", $"{friendshipHearts} 心，认识", 0.02, 0.01, "some friendship")
            {
                StateCue = new WorldStateCue("Aware", "Acknowledging", 20, 1, 1, "some friendship with the farmer", "有些熟悉")
            },
            _ => new WorldContextFactor($"{friendshipHearts} hearts, distant", $"{friendshipHearts} 心，不熟", -0.02, 0, "distant relationship")
        };
    }

    private static WorldStateInfluence BuildStateInfluence(IEnumerable<WorldStateCue> cues)
    {
        var activeCues = cues
            .Where(cue => cue != WorldStateCue.None)
            .ToList();

        if (activeCues.Count == 0)
        {
            return WorldStateInfluence.None;
        }

        var dominant = activeCues
            .OrderByDescending(cue => cue.Priority)
            .First();

        int attentionDelta = activeCues.Sum(cue => cue.AttentionDelta);
        int opennessDelta = activeCues.Sum(cue => cue.OpennessDelta);
        string reason = string.Join(", ", activeCues.Select(cue => cue.Reason));

        return new WorldStateInfluence(
            dominant.Mood,
            dominant.Inclination,
            dominant.Priority,
            attentionDelta,
            opennessDelta,
            reason,
            dominant.DebugLabel
        );
    }
}

internal sealed record WorldContextSnapshot(
    string LocationName,
    string LocationDisplayName,
    string Season,
    int DayOfMonth,
    int TimeOfDay,
    int FriendshipHearts,
    WorldProgressSnapshot Progression,
    WorldProgressKnowledgeSnapshot ProgressionKnowledge,
    IReadOnlyList<string> NearbyNpcNames,
    WorldStateInfluence StateInfluence,
    string PromptLabel,
    string DebugLabel,
    double ApproachModifier,
    double EmoteModifier,
    string Reason
);

internal sealed record SocialContextFactor(
    string PromptLabel,
    string DebugLabel,
    IReadOnlyList<string> NearbyNpcNames,
    double ApproachModifier,
    double EmoteModifier,
    string Reason
)
{
    public WorldStateCue StateCue { get; init; } = WorldStateCue.None;
}

internal sealed record WorldContextFactor(
    string PromptLabel,
    string DebugLabel,
    double ApproachModifier,
    double EmoteModifier,
    string Reason
)
{
    public WorldStateCue StateCue { get; init; } = WorldStateCue.None;
}

internal sealed record WorldStateCue(
    string Mood,
    string Inclination,
    int Priority,
    int AttentionDelta,
    int OpennessDelta,
    string Reason,
    string DebugLabel
)
{
    public static readonly WorldStateCue None = new(string.Empty, string.Empty, 0, 0, 0, string.Empty, string.Empty);
}

internal sealed record WorldStateInfluence(
    string Mood,
    string Inclination,
    int Priority,
    int AttentionDelta,
    int OpennessDelta,
    string Reason,
    string DebugLabel
)
{
    public static readonly WorldStateInfluence None = new(string.Empty, string.Empty, 0, 0, 0, string.Empty, string.Empty);
    public bool HasMood => !string.IsNullOrWhiteSpace(this.Mood);
}
