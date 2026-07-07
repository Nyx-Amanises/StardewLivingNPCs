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

    /// <summary>PromptKeyAudit：源码里的提示词键字面量与 §3.6 最小集合都必须在 default.json 里。</summary>
    [Fact]
    public void PromptKeyAudit_AllReferencedKeysExist()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));

        foreach (string key in PromptTable.KeysReferencedByContentCode)
        {
            Assert.True(en.ContainsKey(key), $"required prompt key missing: {key}");
        }

        var referenced = new SortedSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(
            "(?:GetPromptSkeleton|LookupPrompt|\\.Lookup|getPrompt)\\(\\s*\"(?<key>[A-Za-z][A-Za-z0-9_.]*)\"",
            RegexOptions.Compiled);
        string sourceDir = Path.Combine(RepoSourceRoot, "Dialogue");
        foreach (string file in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                referenced.Add(match.Groups["key"].Value);
            }
        }

        Assert.NotEmpty(referenced);
        var missing = referenced.Where(key => !en.ContainsKey(key)).ToList();
        Assert.True(missing.Count == 0, $"prompt keys referenced in source but missing from prompts/default.json: {string.Join(", ", missing)}");
    }

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
