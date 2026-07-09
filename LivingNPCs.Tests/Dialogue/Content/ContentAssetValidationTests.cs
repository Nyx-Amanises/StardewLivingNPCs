using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LivingNPCs.Dialogue.Content;
using Newtonsoft.Json;

namespace LivingNPCs.Tests.Dialogue.Content;

/// <summary>
/// WP20 交付资产的最小校验（WP15 §6.2）+ PromptKeyAudit（§5）：
/// 反序列化成功、GameSummary 七节齐全、每份传记 Biography 非空白、
/// 代码引用的提示词键在 prompts/default.json 里都存在、default 与 zh 键集一致。
/// </summary>
public sealed class ContentAssetValidationTests
{
    private static readonly Lazy<string> AssetRootLazy = new(FindAssetRoot);

    private static string AssetRoot => AssetRootLazy.Value;

    private static string RepoSourceRoot => Path.GetFullPath(Path.Combine(AssetRoot, "..", ".."));

    private static string FindAssetRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "LivingNPCs", "assets", "dialogue");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate LivingNPCs/assets/dialogue from the test output directory.");
    }

    private static T Deserialize<T>(string path)
    {
        T? result = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        Assert.NotNull(result);
        return result!;
    }

    public static IEnumerable<object[]> BioDirectories()
    {
        yield return new object[] { "bios", 33 };
        yield return new object[] { "bios-zh", 33 };
        yield return new object[] { "bios-sve", 23 };
        yield return new object[] { "bios-sve-zh", 23 };
    }

    [Theory]
    [MemberData(nameof(BioDirectories))]
    public void Bios_DeserializeWithNonBlankBiography(string directory, int expectedCount)
    {
        string[] files = Directory.GetFiles(Path.Combine(AssetRoot, directory), "*.json");
        Assert.Equal(expectedCount, files.Length);

        foreach (string file in files)
        {
            NpcBio bio = Deserialize<NpcBio>(file);
            Assert.False(string.IsNullOrWhiteSpace(bio.Biography), $"{file} has a blank Biography");
        }
    }

    [Theory]
    [InlineData("world/GameSummary.json")]
    [InlineData("world/GameSummaryOptimized.json")]
    public void WorldSummaries_HaveAllSevenSections(string relativePath)
    {
        WorldSummary summary = Deserialize<WorldSummary>(Path.Combine(AssetRoot, relativePath));

        Assert.NotEmpty(summary.SectionOrder);
        foreach (string section in new[] { "Intro", "FarmerBackground", "Seasons", "Locations", "Festivals", "Villagers", "Outro" })
        {
            Assert.NotNull(summary.GetSection(section));
        }

        foreach (string listed in summary.SectionOrder.Keys)
        {
            Assert.NotNull(summary.GetSection(listed));
        }

        Assert.Equal(4, summary.Seasons!.Entries.Count);
        Assert.All(summary.Seasons.Entries.Values, entry =>
        {
            Assert.NotNull(entry.Crops);
            Assert.NotNull(entry.Forage);
        });
        Assert.All(summary.Locations!.Entries.Values, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Region)));
    }

    [Theory]
    [InlineData("world-sve/GameSummary.json")]
    [InlineData("world-sve/GameSummaryOptimized.json")]
    public void SveWorldDeltas_ListedSectionsExist(string relativePath)
    {
        WorldSummary delta = Deserialize<WorldSummary>(Path.Combine(AssetRoot, relativePath));

        Assert.NotEmpty(delta.SectionOrder);
        foreach (string listed in delta.SectionOrder.Keys)
        {
            WorldSummarySection? section = delta.GetSection(listed);
            Assert.NotNull(section);
            Assert.NotEmpty(section!.Entries);
        }
    }

    [Theory]
    [InlineData("sve-relationship-patches.json")]
    [InlineData("sve-relationship-patches-zh.json")]
    public void SveRelationshipPatches_Deserialize(string relativePath)
    {
        var patches = Deserialize<Dictionary<string, Dictionary<string, BioListEntry>>>(Path.Combine(AssetRoot, relativePath));

        Assert.NotEmpty(patches);
        Assert.All(patches.Values.SelectMany(entries => entries.Values), entry =>
            Assert.False(string.IsNullOrWhiteSpace(entry.Description)));
    }

    [Fact]
    public void Prompts_DefaultAndZh_HaveIdenticalKeySets()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));
        var zh = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "zh.json"));

        Assert.Equal(en.Keys.OrderBy(k => k, StringComparer.Ordinal), zh.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Prompts_GenderVariants_ComeInPairs()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));

        foreach (string key in en.Keys.Where(k => k.EndsWith(".MaleNpc", StringComparison.Ordinal)))
        {
            Assert.Contains(key[..^".MaleNpc".Length] + ".FemaleNpc", en.Keys);
        }

        foreach (string key in en.Keys.Where(k => k.EndsWith(".FemaleNpc", StringComparison.Ordinal)))
        {
            Assert.Contains(key[..^".FemaleNpc".Length] + ".MaleNpc", en.Keys);
        }
    }

    /// <summary>PromptKeyAudit：源码里的提示词键字面量与 §3.6 最小集合都必须在 default.json 里。
    /// 覆盖 PromptAssembler 的 this.Text(...)（含三元分支与 "prefix" + suffix 动态键的排除）；
    /// 键存在性按变体规则判定（基础键或 .MaleNpc/.FemaleNpc 任一命中即可）。</summary>
    [Fact]
    public void PromptKeyAudit_AllReferencedKeysExist()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));

        bool Resolvable(string key) =>
            en.ContainsKey(key) || en.ContainsKey(key + ".MaleNpc") || en.ContainsKey(key + ".FemaleNpc");

        foreach (string key in PromptTable.KeysReferencedByContentCode)
        {
            Assert.True(en.ContainsKey(key), $"required prompt key missing: {key}");
        }

        // 变量键族（switch/数组里构造，字面量扫描抓不到）：显式列出。
        foreach (string key in new[]
                 {
                     "instructionsLivingNpcMetadata", "instructionsLivingNpcGiftIds",
                     "instructionsLivingNpcImmediateTravel", "instructionsLivingNpcTravelConsent",
                     "instructionsLivingNpcHelpRequests", "instructionsLivingNpcEmotionDepth",
                     "childrenNone", "childrenSingle", "childrenMultiple",
                     "childrenDescriptionBoy", "childrenDescriptionGirl",
                     "childrenPregnant.npcMale", "childrenPregnant.npcFemale",
                     "nonSpouseFriendshipFirstConversation", "nonSpouseFriendshipStrangers",
                     "nonSpouseFriendshipAcquaintances", "nonSpouseFriendshipFriends",
                     "nonSpouseFriendshipCloseFriends", "nonSpouseFriendshipWantToDate",
                     "nonSpouseFriendshipChild8Plus", "nonSpouseFriendshipNonSingleAdult10",
                     "nonSpouseFriendshipNonSingleAdult8",
                     "marriageSentimentGood", "marriageSentimentBad", "marriageSentimentNeutral",
                     "wealthPoor", "wealthMiddle", "wealthRich", "wealthVeryRich",
                     "giftLoved", "giftLiked", "giftDislike", "giftHate", "giftNeutral",
                     "spouseActionPatio", "spouseActionFunLeave", "spouseActionJobLeave",
                     "spouseActionFunReturn", "spouseActionJobReturn", "spouseActionSpouseRoom",
                     "specialDatesSpring1", "specialDatesSummer1", "specialDatesFall1", "specialDatesWinter1",
                     "recentEventsBoulder", "recentEventsQuarryBridge", "recentEventsBus",
                     "recentEventsGreenhouse", "recentEventsMinecarts", "recentEventsCommunityCenter",
                     "recentEventsMovieTheatre", "recentEventsPamHouse", "recentEventsPamHouseAnonymous",
                     "recentEventsJojaLightning", "recentEventsBabyBoy", "recentEventsBabyGirl",
                     "recentEventsMarried", "recentEventsLuauBest", "recentEventsLuauShorts",
                     "recentEventsLuauPoisoned", "recentEventsMovieInvited", "recentEventsDumpsterDive",
                     "recentEventsGreenRain"
                 })
        {
            Assert.True(Resolvable(key), $"required prompt key missing: {key}");
        }

        var referenced = new SortedSet<string>(StringComparer.Ordinal);
        var callPattern = new Regex(
            "(?:GetPromptSkeleton|LookupPrompt|\\.Lookup|getPrompt|\\.Text|TextOptimizable)\\(",
            RegexOptions.Compiled);
        // 键形字面量；后随 +（"prefix" + suffix 动态拼键）或前随 + 的排除。
        var literalPattern = new Regex(
            "\"(?<key>[a-z][A-Za-z0-9_.]{2,})\"(?!\\s*\\+)",
            RegexOptions.Compiled);
        string sourceDir = Path.Combine(RepoSourceRoot, "Dialogue");
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            foreach (string line in File.ReadLines(file))
            {
                if (!callPattern.IsMatch(line))
                {
                    continue;
                }

                foreach (Match match in literalPattern.Matches(line))
                {
                    int start = match.Index;
                    string before = line[..start].TrimEnd();
                    if (before.EndsWith("+", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    referenced.Add(match.Groups["key"].Value);
                }
            }
        }

        Assert.NotEmpty(referenced);
        var missing = referenced
            .Where(key => !Resolvable(key))
            // 非键字面量豁免：小节名/事件旗标/字符串参数等（键形但不查表）。
            .Where(key => !ExemptFromPromptAudit.Contains(key))
            .ToList();
        Assert.True(missing.Count == 0, $"prompt keys referenced in source but missing from prompts/default.json: {string.Join(", ", missing)}");
    }

    /// <summary>与 .Text(/.Lookup( 同行出现、但并非提示词键的字面量。</summary>
    private static readonly HashSet<string> ExemptFromPromptAudit = new(StringComparer.Ordinal)
    {
        "default", "skip", "coexistenceHud",
        "uiThinking", "uiStartConversation", "uiTypeYourResponse", "uiYourResponse",
        "transcriptGiftPlayerLine", "outputRespond", "outputStaySilent"
    };

    [Fact]
    public void I18n_DefaultAndZh_HaveIdenticalKeySets_AndCoverReferencedDialogueKeys()
    {
        string i18nDir = Path.Combine(RepoSourceRoot, "i18n");
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(i18nDir, "default.json"));
        var zh = Deserialize<Dictionary<string, string>>(Path.Combine(i18nDir, "zh.json"));

        Assert.Equal(en.Keys.OrderBy(k => k, StringComparer.Ordinal), zh.Keys.OrderBy(k => k, StringComparer.Ordinal));

        // 引擎源码里 Util.GetString/GetConsoleString 的字面量键（加 dialogue. 前缀后）都必须可达。
        var referenced = new SortedSet<string>(StringComparer.Ordinal);
        var pattern = new Regex("Get(?:Console)?String\\(\\s*\"(?<key>[A-Za-z][A-Za-z0-9_.]*)\"", RegexOptions.Compiled);
        string sourceDir = Path.Combine(RepoSourceRoot, "Dialogue");
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                string key = match.Groups["key"].Value;
                referenced.Add(key.StartsWith("dialogue.", StringComparison.Ordinal) ? key : "dialogue." + key);
            }
        }

        Assert.NotEmpty(referenced);
        var missing = referenced.Where(key => !en.ContainsKey(key)).ToList();
        Assert.True(missing.Count == 0, $"i18n keys referenced in source but missing from i18n/default.json: {string.Join(", ", missing)}");
    }
}
