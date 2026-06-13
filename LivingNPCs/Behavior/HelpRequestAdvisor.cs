using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;

namespace LivingNPCs.Behavior;

internal static class HelpRequestAdvisor
{
    private static readonly IReadOnlyDictionary<string, HelpRequestProfile> ExplicitProfiles =
        new Dictionary<string, HelpRequestProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gus"] = new("kitchen and hospitality", ["food", "practical", "comfort"]),
            ["Harvey"] = new("care and health", ["drink", "scholarly", "comfort"]),
            ["Penny"] = new("books, children, and simple teaching examples", ["scholarly", "comfort", "flower", "forage", "mineral"]),
            ["Demetrius"] = new("field study and observation", ["scholarly", "nature", "forage"]),
            ["Maru"] = new("technical curiosity", ["scholarly", "practical", "mineral"]),
            ["Robin"] = new("building and repairs", ["practical", "work", "forage"]),
            ["Clint"] = new("smithing and materials", ["mineral", "practical"]),
            ["Linus"] = new("foraging and living outdoors", ["forage", "nature", "food"]),
            ["Leah"] = new("art and nature", ["flower", "nature", "artistic"]),
            ["Elliott"] = new("writing and aesthetics", ["flower", "artistic", "comfort"]),
            ["Abigail"] = new("exploration", ["mineral", "adventurous"]),
            ["Wizard"] = new("arcane study", ["mineral", "magical", "scholarly"]),
            ["Claire"] = new("everyday fatigue and small comforts", ["drink", "practical", "comfort"]),
            ["Sophia"] = new("vineyard life and small comforts", ["flower", "comfort", "food"]),
            ["Susan"] = new("farm work", ["food", "nature", "practical", "forage"]),
            ["Victor"] = new("engineering and careful planning", ["scholarly", "practical", "mineral"]),
            ["Flor"] = new("study and emotional insight", ["scholarly", "comfort", "flower"]),
            ["Kenneth"] = new("electrical work and problem solving", ["scholarly", "practical", "mineral"]),
            ["Pika"] = new("restaurant work", ["food", "comfort"]),
            ["Carmen"] = new("fishing and water life", ["nature", "practical", "forage"]),
            ["June"] = new("performance and taste", ["flower", "artistic", "comfort"])
        };

    private static readonly IReadOnlyList<HelpRequestItem> ProgressionItems =
    [
        new("(O)80", "Quartz", ["mineral", "scholarly", "magical"], HelpRequestAvailability.MinesOpen),
        new("(O)66", "Amethyst", ["mineral", "adventurous", "magical", "artistic"], HelpRequestAvailability.MineLevel40)
    ];

    private static readonly IReadOnlyList<HelpRequestItem> RelationshipItems =
    [
        new("(O)216", "Bread", ["food", "comfort", "hospitality", "practical"], HelpRequestAvailability.Always, 2),
        new("(O)395", "Coffee", ["drink", "comfort", "work", "hospitality"], HelpRequestAvailability.Always, 4),
        new("(O)223", "Cookie", ["food", "comfort", "sweet", "family"], HelpRequestAvailability.Always, 6)
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<HelpRequestItem>> SeasonalItems =
        new Dictionary<string, IReadOnlyList<HelpRequestItem>>(StringComparer.OrdinalIgnoreCase)
        {
            ["spring"] =
            [
                new("(O)16", "Wild Horseradish", ["forage", "nature", "food", "practical"]),
                new("(O)18", "Daffodil", ["flower", "nature", "artistic"]),
                new("(O)20", "Leek", ["forage", "food", "practical"]),
                new("(O)22", "Dandelion", ["flower", "nature"])
            ],
            ["summer"] =
            [
                new("(O)396", "Spice Berry", ["forage", "food", "sweet"]),
                new("(O)398", "Grape", ["forage", "food", "sweet"]),
                new("(O)402", "Sweet Pea", ["flower", "artistic"])
            ],
            ["fall"] =
            [
                new("(O)404", "Common Mushroom", ["forage", "nature", "practical"]),
                new("(O)406", "Wild Plum", ["forage", "food", "sweet"]),
                new("(O)408", "Hazelnut", ["forage", "food", "practical"]),
                new("(O)410", "Blackberry", ["forage", "food", "sweet"])
            ],
            ["winter"] =
            [
                new("(O)412", "Winter Root", ["forage", "food", "practical"]),
                new("(O)414", "Crystal Fruit", ["forage", "food", "magical"]),
                new("(O)416", "Snow Yam", ["forage", "food", "practical"]),
                new("(O)418", "Crocus", ["flower", "artistic"])
            ]
        };

    public static string BuildPromptLabel(NPC npc, WorldProgressSnapshot progression)
    {
        var profile = GetProfile(npc);
        var world = WorldContext.For(npc);
        var preferredItems = GetPreferredItems(npc, progression);
        string itemText = preferredItems.Count == 0
            ? "no currently reasonable item request; do not open a help request now"
            : string.Join(", ", preferredItems.Select(item => $"{item.Label} {item.ItemId}"));
        string stageText = BuildRequestDepthGuidance(progression);
        string routeText = BuildRouteGuidance(progression);
        string relationshipText = BuildRelationshipGuidance(world.FriendshipHearts);

        return $"theme {profile.Theme}; currently reasonable item requests: {itemText}; allowed help request type: item_request only; never create question_request; request relationship tier: {relationshipText}; request depth: {stageText}; world-stage constraint: {routeText}; whenever the visible reply has this NPC ask the farmer to bring or find one of the listed items — whether the farmer offered first or the NPC raised it — you MUST also include exactly one hidden helpRequests entry with a concrete itemId from this list, and never leave the favor only in the spoken text; when the farmer agrees to such a favor, include a hidden helpRequestUpdates entry with status accepted; if no listed item naturally fits, keep the visible reply as ordinary conversation and do not open a hidden request.";
    }

    public static string BuildDebugLabel(NPC npc, WorldProgressSnapshot progression)
    {
        var profile = GetProfile(npc);
        var world = WorldContext.For(npc);
        var preferredItems = GetPreferredItems(npc, progression);
        string itemText = preferredItems.Count == 0
            ? "当前不建议物品求助"
            : string.Join("、", preferredItems.Select(item => $"{item.Label} {item.ItemId}"));
        string depthText = progression.ResidentStage switch
        {
            "first_spring_newcomer" => "第一年春，新人阶段：只适合低负担一步物品求助",
            "first_year_settling_in" => "第一年安顿中：以一步物品求助为主",
            "second_year_established" => "第二年已融入：可出现适度个人化物品求助",
            _ => "老住户阶段：高信任时可出现更贴角色的物品求助"
        };
        string routeText = progression.Route switch
        {
            "community_center" => "社区中心路线已完成",
            "joja" => "Joja 路线已完成",
            _ => "路线未定"
        };
        string relationshipText = world.FriendshipHearts switch
        {
            < 2 => "0-1 心：只允许很轻的当季物品",
            < 4 => "2-3 心：可加入便宜日常物品",
            < 6 => "4-5 心：可加入更贴生活习惯的物品",
            _ => "6 心以上：可加入更有人情味的物品"
        };

        return $"主题：{profile.Theme}；候选物品：{itemText}；类型：仅物品求助；关系：{relationshipText}；深度：{depthText}；路线：{routeText}";
    }

    public static bool IsCurrentlyRequestableItem(string itemId)
    {
        return GetSeasonalItems()
            .Concat(GetProgressionItems())
            .Any(item => item.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsCurrentlyRequestableItem(string itemId, NPC npc)
    {
        return GetPreferredItems(npc, WorldProgression.Current())
            .Any(item => item.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Currently reasonable item requests for this NPC, as (itemId, English label) pairs.</summary>
    public static IReadOnlyList<(string ItemId, string Label)> GetRequestableItems(NPC npc)
    {
        return GetPreferredItems(npc, WorldProgression.Current())
            .Select(item => (item.ItemId, item.Label))
            .ToList();
    }

    private static List<HelpRequestItem> GetPreferredItems(NPC npc, WorldProgressSnapshot progression)
    {
        var profile = GetProfile(npc);
        var tags = new HashSet<string>(profile.Tags, StringComparer.OrdinalIgnoreCase);
        int friendshipHearts = WorldContext.For(npc).FriendshipHearts;
        var seasonal = GetSeasonalItems().ToList();
        var matchingSeasonal = seasonal
            .Where(item => item.Tags.Any(tags.Contains))
            .Take(3);
        var matchingProgression = GetProgressionItems()
            .Where(item => item.Tags.Any(tags.Contains))
            .Take(friendshipHearts >= 4 || progression.Year > 1 ? 2 : 1);
        var matchingRelationship = RelationshipItems
            .Where(item => friendshipHearts >= item.MinFriendshipHearts && item.Tags.Any(tags.Contains))
            .Take(friendshipHearts >= 6 ? 2 : 1);

        var preferred = matchingSeasonal
            .Concat(matchingProgression)
            .Concat(matchingRelationship)
            .DistinctBy(item => item.ItemId)
            .Take(friendshipHearts >= 6 || progression.Year > 1 ? 5 : 4)
            .ToList();

        if (preferred.Count == 0)
        {
            preferred = seasonal
                .Take(friendshipHearts >= 2 ? 2 : 1)
                .ToList();
        }

        return preferred;
    }

    private static HelpRequestProfile GetProfile(NPC npc)
    {
        if (ExplicitProfiles.TryGetValue(npc.Name, out var explicitProfile))
        {
            return explicitProfile;
        }

        var disposition = NpcDisposition.For(npc);
        string profileText = $"{disposition.PromptLabel} {disposition.BackgroundPrompt} {disposition.DialoguePrompt}".ToLowerInvariant();
        if (ContainsAny(profileText, "magical", "arcane", "adventurer", "danger"))
        {
            return new HelpRequestProfile("exploration and unusual findings", ["mineral", "adventurous", "magical"]);
        }

        if (ContainsAny(profileText, "artist", "creative", "performer", "stylish"))
        {
            return new HelpRequestProfile("aesthetics and expression", ["flower", "artistic", "comfort"]);
        }

        if (ContainsAny(profileText, "scholarly", "educated", "student", "technical", "engineering"))
        {
            return new HelpRequestProfile("study and careful thinking", ["scholarly", "practical", "mineral"]);
        }

        if (ContainsAny(profileText, "farm", "rural", "work", "practical"))
        {
            return new HelpRequestProfile("practical daily work", ["practical", "forage", "food"]);
        }

        if (ContainsAny(profileText, "warm", "family", "gentle", "community"))
        {
            return new HelpRequestProfile("everyday care and comfort", ["comfort", "food", "flower"]);
        }

        return new HelpRequestProfile("small everyday favors", ["practical", "comfort", "forage"]);
    }

    private static IEnumerable<HelpRequestItem> GetSeasonalItems()
    {
        string season = Game1.season.ToString().ToLowerInvariant();
        return SeasonalItems.TryGetValue(season, out var items)
            ? items
            : Array.Empty<HelpRequestItem>();
    }

    private static IEnumerable<HelpRequestItem> GetProgressionItems()
    {
        return ProgressionItems.Where(item => IsAvailable(item.Availability));
    }

    private static bool IsAvailable(HelpRequestAvailability availability)
    {
        return availability switch
        {
            HelpRequestAvailability.MinesOpen => AreMinesOpen(),
            HelpRequestAvailability.MineLevel40 => Game1.player?.deepestMineLevel >= 40,
            _ => true
        };
    }

    private static bool AreMinesOpen()
    {
        if (Game1.player?.deepestMineLevel > 0)
        {
            return true;
        }

        return Game1.year > 1
            || Game1.currentSeason != "spring"
            || Game1.dayOfMonth >= 5;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildRequestDepthGuidance(WorldProgressSnapshot progression)
    {
        return progression.ResidentStage switch
        {
            "first_spring_newcomer" =>
                "prefer one-step, low-stakes item requests; avoid requests that assume long shared history",
            "first_year_settling_in" =>
                "small one-step item requests fit best; a modest two-step item request is okay only when the current conversation strongly supports it",
            "second_year_established" =>
                "modest personal item requests and occasional two-step item favors can fit when trust and conversation support them",
            _ =>
                "deeper personal item requests or fuller multi-step item favors can fit when trust is high and the conversation earns them"
        };
    }

    private static string BuildRelationshipGuidance(int friendshipHearts)
    {
        return friendshipHearts switch
        {
            < 2 => "distant; choose only very easy seasonal forage or flowers",
            < 4 => "familiar; cheap everyday items may fit if character-specific",
            < 6 => "friendly; modest comfort items can fit",
            _ => "trusted; more personal low-value comfort items can fit"
        };
    }

    private static string BuildRouteGuidance(WorldProgressSnapshot progression)
    {
        string facilities = progression switch
        {
            { BusRepaired: false, GingerIslandUnlocked: false, MovieTheaterOpen: false } =>
                "do not rely on unrepaired travel or late-game facilities",
            _ =>
                "already-unlocked public facilities may be treated as ordinary parts of life"
        };

        string route = progression.Route switch
        {
            "community_center" => "the community center is already restored",
            "joja" => "the town followed the Joja route",
            _ => "the town route is still unresolved"
        };

        return $"{route}; {facilities}";
    }

    private sealed record HelpRequestProfile(
        string Theme,
        IReadOnlyCollection<string> Tags
    );

    private sealed record HelpRequestItem(
        string ItemId,
        string Label,
        IReadOnlyCollection<string> Tags,
        HelpRequestAvailability Availability = HelpRequestAvailability.CurrentSeason,
        int MinFriendshipHearts = 0
    );

    private enum HelpRequestAvailability
    {
        Always,
        CurrentSeason,
        MinesOpen,
        MineLevel40
    }
}
