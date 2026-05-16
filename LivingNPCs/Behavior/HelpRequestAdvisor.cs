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
            ["Gus"] = new("kitchen and hospitality", ["food", "practical"], ["a small ingredient", "what food the farmer has enjoyed lately"]),
            ["Harvey"] = new("care and health", ["drink", "scholarly"], ["a simple observation", "how the farmer has been feeling"]),
            ["Penny"] = new("books and children", ["scholarly", "comfort"], ["a small reading suggestion", "a thoughtful opinion"]),
            ["Demetrius"] = new("field study and observation", ["scholarly", "nature"], ["a sample from outdoors", "a practical observation"]),
            ["Maru"] = new("technical curiosity", ["scholarly", "practical"], ["a small material", "a practical explanation"]),
            ["Robin"] = new("building and repairs", ["practical", "work"], ["a practical material", "a quick judgment call"]),
            ["Clint"] = new("smithing and materials", ["mineral", "practical"], ["a common mineral", "a practical question"]),
            ["Linus"] = new("foraging and living outdoors", ["forage", "nature"], ["a seasonal forage item", "an outdoor observation"]),
            ["Leah"] = new("art and nature", ["flower", "nature", "artistic"], ["a seasonal flower", "an opinion about a natural detail"]),
            ["Elliott"] = new("writing and aesthetics", ["flower", "artistic"], ["a flower", "a reflective answer"]),
            ["Abigail"] = new("exploration", ["mineral", "adventurous"], ["a common mineral", "something about the mines"]),
            ["Wizard"] = new("arcane study", ["mineral", "magical", "scholarly"], ["a quartz sample", "an unusual observation"]),
            ["Claire"] = new("everyday fatigue and small comforts", ["drink", "practical"], ["coffee", "a grounded practical answer"]),
            ["Sophia"] = new("vineyard life and small comforts", ["flower", "comfort"], ["a seasonal flower", "a gentle opinion"]),
            ["Susan"] = new("farm work", ["food", "nature", "practical"], ["a seasonal forage item", "farm experience"]),
            ["Victor"] = new("engineering and careful planning", ["scholarly", "practical"], ["a simple material", "a thoughtful answer"]),
            ["Flor"] = new("study and emotional insight", ["scholarly", "comfort"], ["a thoughtful answer", "a small everyday comfort"]),
            ["Kenneth"] = new("electrical work and problem solving", ["scholarly", "practical"], ["a practical material", "a clear explanation"]),
            ["Pika"] = new("restaurant work", ["food", "comfort"], ["a small ingredient", "what flavors the farmer prefers"]),
            ["Carmen"] = new("fishing and water life", ["nature", "practical"], ["an outdoor observation", "a practical answer"]),
            ["June"] = new("performance and taste", ["flower", "artistic"], ["a flower", "an opinion about style or mood"])
        };

    private static readonly IReadOnlyList<HelpRequestItem> ProgressionItems =
    [
        new("(O)80", "Quartz", ["mineral", "scholarly", "magical"], HelpRequestAvailability.MinesOpen),
        new("(O)66", "Amethyst", ["mineral", "adventurous", "magical", "artistic"], HelpRequestAvailability.MineLevel40)
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

    public static string BuildPromptLabel(NPC npc)
    {
        var profile = GetProfile(npc);
        var tags = new HashSet<string>(profile.Tags, StringComparer.OrdinalIgnoreCase);
        var seasonal = GetSeasonalItems()
            .Where(item => item.Tags.Any(tags.Contains))
            .Take(3)
            .ToList();
        var progression = GetProgressionItems()
            .Where(item => item.Tags.Any(tags.Contains))
            .Take(2)
            .ToList();
        var preferredItems = seasonal
            .Concat(progression)
            .DistinctBy(item => item.ItemId)
            .ToList();

        string itemText = preferredItems.Count == 0
            ? "no currently reasonable item request; prefer a question request if the current conversation supports one"
            : string.Join(", ", preferredItems.Select(item => $"{item.Label} {item.ItemId}"));
        string questionText = string.Join(", ", profile.QuestionPrompts);

        return $"theme {profile.Theme}; currently reasonable item requests: {itemText}; fitting question requests: {questionText}; only ask if the current conversation naturally creates a reason.";
    }

    public static bool IsCurrentlyRequestableItem(string itemId)
    {
        return GetSeasonalItems()
            .Concat(GetProgressionItems())
            .Any(item => item.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase));
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
            return new HelpRequestProfile("exploration and unusual findings", ["mineral", "adventurous", "magical"], ["a common mineral", "something the farmer noticed while exploring"]);
        }

        if (ContainsAny(profileText, "artist", "creative", "performer", "stylish"))
        {
            return new HelpRequestProfile("aesthetics and expression", ["flower", "artistic"], ["a seasonal flower", "the farmer's opinion about mood or beauty"]);
        }

        if (ContainsAny(profileText, "scholarly", "educated", "student", "technical", "engineering"))
        {
            return new HelpRequestProfile("study and careful thinking", ["scholarly", "practical"], ["a thoughtful answer", "a small observation relevant to their work"]);
        }

        if (ContainsAny(profileText, "farm", "rural", "work", "practical"))
        {
            return new HelpRequestProfile("practical daily work", ["practical", "forage", "food"], ["a useful everyday item", "a grounded practical answer"]);
        }

        if (ContainsAny(profileText, "warm", "family", "gentle", "community"))
        {
            return new HelpRequestProfile("everyday care and comfort", ["comfort", "food", "flower"], ["a small comfort item", "a thoughtful personal answer"]);
        }

        return new HelpRequestProfile("small everyday favors", ["practical", "comfort"], ["a modest practical answer", "a small everyday item"]);
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

    private sealed record HelpRequestProfile(
        string Theme,
        IReadOnlyCollection<string> Tags,
        IReadOnlyCollection<string> QuestionPrompts
    );

    private sealed record HelpRequestItem(
        string ItemId,
        string Label,
        IReadOnlyCollection<string> Tags,
        HelpRequestAvailability Availability = HelpRequestAvailability.CurrentSeason
    );

    private enum HelpRequestAvailability
    {
        CurrentSeason,
        MinesOpen,
        MineLevel40
    }
}
