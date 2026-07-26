using System.Text.RegularExpressions;
using LivingNPCs.Behavior;

namespace LivingNPCs.Tests;

/// <summary>求助候选目录的结构不变式：ID 形态、去重、季节值、链对成员合法、旧存档兼容。</summary>
public sealed class HelpRequestAdvisorCatalogTests
{
    private static readonly Regex QualifiedObjectId = new(@"^\(O\)\d+$", RegexOptions.Compiled);

    private static readonly string[] LegacyItemIds =
    [
        "(O)16", "(O)18", "(O)20", "(O)22", "(O)66", "(O)80", "(O)216", "(O)223",
        "(O)395", "(O)396", "(O)398", "(O)402", "(O)404", "(O)406", "(O)408",
        "(O)410", "(O)412", "(O)414", "(O)416", "(O)418"
    ];

    [Fact]
    public void CatalogEntriesAreWellFormedAndDistinct()
    {
        var entries = HelpRequestAdvisor.CatalogForTests.ToList();

        // 扩容后目录规模：六大类合计（原 20 件的三倍以上）。
        Assert.True(entries.Count >= 65, $"catalog too small: {entries.Count}");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string itemId, string label, var tags, var seasons) in entries)
        {
            Assert.Matches(QualifiedObjectId, itemId);
            Assert.True(seenIds.Add(itemId), $"duplicate item id {itemId}");
            Assert.False(string.IsNullOrWhiteSpace(label), $"empty label for {itemId}");
            Assert.NotEmpty(tags);
            if (seasons != null)
            {
                Assert.NotEmpty(seasons);
                Assert.All(seasons, season =>
                    Assert.Contains(season, new[] { "spring", "summer", "fall", "winter" }));
            }
        }
    }

    [Fact]
    public void LegacyWhitelistItemsRemainInCatalog()
    {
        // 旧存档中已存在的求助步骤在读档规整时会重新过白名单：原 20 件必须继续在目录里。
        foreach (string legacyId in LegacyItemIds)
        {
            Assert.Contains(legacyId, HelpRequestAdvisor.AllItemIds);
        }
    }

    [Fact]
    public void ChainPairsReferenceCatalogItemsOnly()
    {
        foreach ((string first, string second) in HelpRequestAdvisor.ChainPairsForTests)
        {
            Assert.Contains(first, HelpRequestAdvisor.AllItemIds);
            Assert.Contains(second, HelpRequestAdvisor.AllItemIds);
            Assert.NotEqual(first, second);
        }
    }

    [Fact]
    public void NoLegendaryFishInCatalog()
    {
        // 设计约束（用户拍板）：鱼类不设钓鱼等级门，但绝不请求鱼王/传说鱼。
        string[] legendaryFishIds =
        [
            "(O)159", // Crimsonfish
            "(O)160", // Angler
            "(O)163", // Legend
            "(O)682", // Mutant Carp
            "(O)775", // Glacierfish
            "(O)898", "(O)899", "(O)900", "(O)901", "(O)902" // Qi 挑战变体
        ];
        foreach (string legendaryId in legendaryFishIds)
        {
            Assert.DoesNotContain(legendaryId, HelpRequestAdvisor.AllItemIds);
        }
    }
}
