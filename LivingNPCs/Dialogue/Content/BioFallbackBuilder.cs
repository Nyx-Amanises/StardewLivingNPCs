using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// 从游戏 Data/Characters 生成轻量回退传记（WP15 §4.3.3）：传记资产缺失/空白、
/// 或角色扩展兼容判 false 时使用。纯函数，可单测。
/// </summary>
internal static class BioFallbackBuilder
{
    /// <summary>固定收尾声明：告知模型这是本地生成的基线，勿当完整设定。</summary>
    public const string FallbackDisclaimer =
        "This biography is a lightweight locally generated baseline from game data, not full lore; keep claims modest and avoid inventing detailed backstory.";

    public static NpcBio Build(string npcName, CharacterData data)
    {
        var bio = new NpcBio();

        List<string> friends = (data.FriendsAndFamily ?? new Dictionary<string, string>())
            .Keys
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        string age = DescribeAge(data.Age);
        string home = DescribeHome(data.HomeRegion);
        string manner = DescribeManner(data.Manner);
        string social = DescribeSocial(data.SocialAnxiety);
        string outlook = DescribeOutlook(data.Optimism);

        var sentences = new List<string>
        {
            $"{npcName} is {Article(age)} {age} who lives {home}.",
            $"People know {npcName} as {manner}, {social}, and generally {outlook}."
        };
        if (data.CanBeRomanced)
        {
            sentences.Add($"{npcName} is single and open to romance.");
        }

        if (friends.Count > 0)
        {
            sentences.Add($"Close people in {npcName}'s life include {string.Join(", ", friends.Take(6))}.");
        }

        bio.Biography = string.Join(" ", sentences);
        bio.BiographyEnd = FallbackDisclaimer;

        bio.Traits["Manner"] = new BioListEntry { Id = "Manner", Heading = "Manner", Description = Capitalize(manner) + "." };
        bio.Traits["Social"] = new BioListEntry { Id = "Social", Heading = "Social", Description = Capitalize(social) + "." };
        bio.Traits["Outlook"] = new BioListEntry { Id = "Outlook", Heading = "Outlook", Description = Capitalize(outlook) + "." };

        foreach (string friend in friends.Take(8))
        {
            bio.Relationships[friend] = new BioListEntry
            {
                Id = friend,
                Heading = friend,
                Description = $"{friend} is close to {npcName}."
            };
        }

        bio.Preoccupations = new List<string> { home, manner }
            .Concat(friends.Take(6))
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return bio;
    }

    private static string DescribeAge(NpcAge age)
    {
        return age switch
        {
            NpcAge.Child => "child",
            NpcAge.Teen => "teenager",
            _ => "adult"
        };
    }

    private static string DescribeHome(string? homeRegion)
    {
        return homeRegion switch
        {
            "Town" => "in or around Pelican Town",
            "Desert" => "out in the Calico Desert area",
            _ => "somewhere beyond Pelican Town"
        };
    }

    private static string DescribeManner(NpcManner manner)
    {
        return manner switch
        {
            NpcManner.Polite => "polite and considerate",
            NpcManner.Rude => "blunt and sometimes rude",
            _ => "plainspoken and even-tempered"
        };
    }

    private static string DescribeSocial(NpcSocialAnxiety social)
    {
        return social switch
        {
            NpcSocialAnxiety.Outgoing => "outgoing around others",
            NpcSocialAnxiety.Shy => "shy in crowds",
            _ => "comfortable in most company"
        };
    }

    private static string DescribeOutlook(NpcOptimism optimism)
    {
        return optimism switch
        {
            NpcOptimism.Positive => "upbeat about life",
            NpcOptimism.Negative => "prone to seeing the downside",
            _ => "level-headed about life"
        };
    }

    private static string Article(string noun)
    {
        return "aeiou".Contains(char.ToLowerInvariant(noun.FirstOrDefault())) ? "an" : "a";
    }

    private static string Capitalize(string text)
    {
        return string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
