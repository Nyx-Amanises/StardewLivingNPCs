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
            social.NearbyNpcNames,
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
            return new SocialContextFactor("no nearby NPCs", "附近：无其他 NPC", nearbyNames, 0.02, -0.01, "quiet nearby company");
        }

        string names = string.Join(", ", nearbyNames);
        if (nearbyNames.Length == 1)
        {
            return new SocialContextFactor($"one nearby NPC: {names}", $"附近：{names}", nearbyNames, 0, 0.01, "one nearby person");
        }

        return new SocialContextFactor($"several nearby NPCs: {names}", $"附近：{names}", nearbyNames, -0.02, 0.04, "public nearby company");
    }

    private static WorldContextFactor DetermineTime(int timeOfDay)
    {
        return timeOfDay switch
        {
            < 1000 => new WorldContextFactor("early morning", "清晨", 0.03, 0, "early morning"),
            < 1400 => new WorldContextFactor("midday", "白天", 0.01, 0, "daytime"),
            < 1800 => new WorldContextFactor("afternoon", "下午", 0.02, 0.01, "afternoon"),
            < 2200 => new WorldContextFactor("evening", "傍晚/夜间", 0.01, 0.03, "evening"),
            _ => new WorldContextFactor("late night", "深夜", -0.12, -0.05, "late night")
        };
    }

    private static WorldContextFactor DetermineWeather(bool isOutdoors)
    {
        if (Game1.IsRainingHere())
        {
            return isOutdoors
                ? new WorldContextFactor("rainy weather outdoors", "室外下雨", -0.06, 0.04, "rainy outdoors")
                : new WorldContextFactor("rain outside while indoors", "室内避雨", 0.03, 0.02, "sheltering from rain");
        }

        if (Game1.IsSnowingHere())
        {
            return isOutdoors
                ? new WorldContextFactor("snowy weather outdoors", "室外下雪", -0.03, 0.03, "snowy outdoors")
                : new WorldContextFactor("snow outside while indoors", "室内避雪", 0.02, 0.01, "sheltering from snow");
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
            return new WorldContextFactor("social place", "社交场所", 0.07, 0.03, "social location");
        }

        if (normalized.Contains("mine", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("skull", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("adventure", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("tense or adventurous place", "紧张/冒险地点", -0.05, 0.07, "adventurous location");
        }

        if (normalized.Contains("shop", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("hospital", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("blacksmith", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("animal", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("fish", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("workplace or shop", "工作/商店场所", 0.02, 0.01, "workplace location");
        }

        if (normalized.Contains("farm", StringComparison.OrdinalIgnoreCase))
        {
            return new WorldContextFactor("the farmer's farm", "玩家农场", -0.02, 0.01, "farmer's farm");
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
            >= 8 => new WorldContextFactor($"{friendshipHearts} hearts, close", $"{friendshipHearts} 心，亲近", 0.1, 0.05, "close relationship"),
            >= 4 => new WorldContextFactor($"{friendshipHearts} hearts, friendly", $"{friendshipHearts} 心，友好", 0.05, 0.03, "friendly relationship"),
            >= 2 => new WorldContextFactor($"{friendshipHearts} hearts, familiar", $"{friendshipHearts} 心，认识", 0.02, 0.01, "some friendship"),
            _ => new WorldContextFactor($"{friendshipHearts} hearts, distant", $"{friendshipHearts} 心，不熟", -0.02, 0, "distant relationship")
        };
    }
}

internal sealed record WorldContextSnapshot(
    string LocationName,
    string LocationDisplayName,
    string Season,
    int DayOfMonth,
    int TimeOfDay,
    int FriendshipHearts,
    IReadOnlyList<string> NearbyNpcNames,
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
);

internal sealed record WorldContextFactor(
    string PromptLabel,
    string DebugLabel,
    double ApproachModifier,
    double EmoteModifier,
    string Reason
);
