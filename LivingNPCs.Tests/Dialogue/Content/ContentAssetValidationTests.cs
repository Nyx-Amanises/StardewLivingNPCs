using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LivingNPCs.Dialogue.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            Assert.All(
                bio.ExtraPortraits.Keys,
                key => Assert.True(
                    key == "!" || PortraitMarkerRules.NormalizeExtraMarker(key) != null,
                    $"{file} contains unsupported ExtraPortraits key '{key}'"));
            if (bio.ExtraPortraits.TryGetValue("u", out string? uniquePortrait)
                && !string.IsNullOrWhiteSpace(uniquePortrait)
                && !string.IsNullOrWhiteSpace(bio.Unique))
            {
                Assert.False(
                    string.Equals(uniquePortrait.Trim(), bio.Unique.Trim(), StringComparison.OrdinalIgnoreCase),
                    $"{file} appears to synthesize ExtraPortraits.u from Unique");
            }
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
    public void Prompts_DefaultAndZh_HaveIdenticalBaseKeys_AndZhKeepsOnlyMeaningfulGenderVariants()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));
        var zh = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "zh.json"));

        static bool IsGenderVariant(string key) =>
            key.EndsWith(".MaleNpc", StringComparison.Ordinal)
            || key.EndsWith(".FemaleNpc", StringComparison.Ordinal);

        Assert.Equal(
            en.Keys.Where(key => !IsGenderVariant(key)).OrderBy(key => key, StringComparer.Ordinal),
            zh.Keys.Where(key => !IsGenderVariant(key)).OrderBy(key => key, StringComparer.Ordinal));

        foreach (string maleKey in zh.Keys.Where(key => key.EndsWith(".MaleNpc", StringComparison.Ordinal)))
        {
            string baseKey = maleKey[..^".MaleNpc".Length];
            string femaleKey = baseKey + ".FemaleNpc";
            Assert.Contains(baseKey, zh.Keys);
            Assert.Contains(femaleKey, zh.Keys);
            Assert.NotEqual(zh[maleKey], zh[femaleKey]);
        }

        foreach (string femaleKey in zh.Keys.Where(key => key.EndsWith(".FemaleNpc", StringComparison.Ordinal)))
        {
            Assert.Contains(femaleKey[..^".FemaleNpc".Length] + ".MaleNpc", zh.Keys);
        }
    }

    [Fact]
    public void Prompts_PortraitSelection_PrioritizesStrongHappinessAndFinalTextureSemantics()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));
        var zh = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "zh.json"));

        string englishRule = en["instructionsEmotion"];
        Assert.Contains("strong happiness", englishRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("takes priority", englishRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("means only what its description", englishRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never assume $a is angry", englishRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("records, not recommendations", englishRule, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normally $h", englishRule, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gentle grateful markers such as $7", englishRule, StringComparison.OrdinalIgnoreCase);

        string chineseRule = zh["instructionsEmotion"];
        Assert.Contains("真的很开心", chineseRule, StringComparison.Ordinal);
        Assert.Contains("优先", chineseRule, StringComparison.Ordinal);
        Assert.Contains("只代表当前最终肖像贴图", chineseRule, StringComparison.Ordinal);
        Assert.Contains("不得假定$a就是怒脸", chineseRule, StringComparison.Ordinal);
        Assert.Contains("只是记录，不是当前推荐", chineseRule, StringComparison.Ordinal);
        Assert.DoesNotContain("通常是列出的$h", chineseRule, StringComparison.Ordinal);
        Assert.DoesNotContain("$7等温柔感激", chineseRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompts_PrioritizeActualRelationshipProgressDialogueAndSpatialGrounding()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));
        var zh = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "zh.json"));

        Assert.Contains("current hearts are authoritative", en["dateTimeResidencyToday"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("move the topic forward", en["currentConversationIntro"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not prove visibility", en["instructionsGrounding"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("polite personal or family question", en["instructionsLivingNpcEmotionDepth"], StringComparison.OrdinalIgnoreCase);

        Assert.Contains("关系状态和爱心数始终优先", zh["dateTimeResidencyToday"], StringComparison.Ordinal);
        Assert.Contains("推进话题", zh["currentConversationIntro"], StringComparison.Ordinal);
        Assert.Contains("不能据此推断门窗外能看见什么", zh["instructionsGrounding"], StringComparison.Ordinal);
        Assert.Contains("一次礼貌的私人或家庭提问", zh["instructionsLivingNpcEmotionDepth"], StringComparison.Ordinal);
    }

    [Fact]
    public void Prompts_HighOrModdedHeartsDoNotInventMutualRomance()
    {
        var en = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "default.json"));
        var zh = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", "zh.json"));

        foreach (string key in new[]
                 {
                     "nonSpouseFriendshipWantToDate",
                     "nonSpouseFriendshipWantToDate.MaleNpc",
                     "nonSpouseFriendshipWantToDate.FemaleNpc"
                 })
        {
            Assert.Contains("modded hearts", en[key], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("never", en[key], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("explicit relationship status", en[key], StringComparison.OrdinalIgnoreCase);

            Assert.Contains("Mod 修改的爱心", zh[key], StringComparison.Ordinal);
            Assert.Contains("绝不", zh[key], StringComparison.Ordinal);
            Assert.Contains("明确", zh[key], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PortraitFrameSemantics_CatalogHasReviewedFullFrameSchema()
    {
        string catalogPath = Path.Combine(AssetRoot, "portrait-frame-semantics.json");
        Assert.Equal(
            "AAE63F678A4171402B51F851BE267EC28FB8F5E5CD37BDC621D01A8ED2EF75E1",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(catalogPath))));

        JObject catalog = JObject.Parse(File.ReadAllText(catalogPath));
        Assert.Equal(2, catalog.Value<int>("Version"));
        Assert.Equal(64, catalog.Value<int>("TileSize"));
        Assert.Null(catalog["FrameIndex"]);
        Assert.Null(catalog["Frames"]);
        Assert.Null(catalog["Profiles"]);

        var frames = Assert.IsType<JArray>(catalog["FrameEntries"]);
        Assert.NotEmpty(frames);

        var indexedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var representedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowedMeaningsByAssetHash = new Dictionary<string, (int Index, string English, string Chinese)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (JToken frameToken in frames)
        {
            var frame = Assert.IsType<JObject>(frameToken);
            int index = frame.Value<int>("index");
            string marker = Assert.IsType<string>(frame["marker"]?.ToObject<string>());
            string hash = Assert.IsType<string>(frame["hash"]?.ToObject<string>());
            bool enabled = Assert.IsType<bool>(frame["enabled"]?.ToObject<bool>());
            string decision = Assert.IsType<string>(frame["decision"]?.ToObject<string>());

            Assert.InRange(index, 0, PortraitMarkerRules.MaxSupportedFrameIndex);
            Assert.Equal(PortraitMarkerRules.MarkerForIndex(index), marker);
            Assert.Matches("^[0-9A-F]{64}$", hash);
            Assert.True(indexedHashes.Add($"{index}:{hash}"), $"duplicate portrait index/hash entry: {index}:{hash}");
            Assert.Contains(decision, new[] { "allow", "deny", "unknown" });
            Assert.Equal(decision == "allow", enabled);
            Assert.False(string.IsNullOrWhiteSpace(frame.Value<string>("en")));
            Assert.False(string.IsNullOrWhiteSpace(frame.Value<string>("zh")));
            decisions.Add(decision);

            string[] frameProfiles = frame["profiles"]?.Values<string>().OfType<string>().ToArray() ?? Array.Empty<string>();
            string[] frameAssets = frame["assets"]?.Values<string>().OfType<string>().ToArray() ?? Array.Empty<string>();
            Assert.NotEmpty(frameProfiles);
            Assert.NotEmpty(frameAssets);
            Assert.Equal(frameProfiles.Length, frameProfiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(frameAssets.Length, frameAssets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (string asset in frameAssets)
            {
                representedSources.Add(asset.Split('/')[0]);
                if (enabled)
                {
                    string assetHash = asset + "\n" + hash;
                    var meaning = (
                        Index: index,
                        English: frame.Value<string>("en") ?? string.Empty,
                        Chinese: frame.Value<string>("zh") ?? string.Empty);
                    if (allowedMeaningsByAssetHash.TryGetValue(assetHash, out var existing))
                    {
                        Assert.True(
                            string.Equals(existing.English, meaning.English, StringComparison.Ordinal)
                            && string.Equals(existing.Chinese, meaning.Chinese, StringComparison.Ordinal),
                            $"same pixels have conflicting enabled meanings in {asset}: "
                            + $"frame {existing.Index} ({existing.English}) vs frame {index} ({meaning.English})");
                    }
                    else
                    {
                        allowedMeaningsByAssetHash[assetHash] = meaning;
                    }
                }
            }

            if (enabled)
            {
                Assert.True(string.IsNullOrWhiteSpace(frame.Value<string>("reason")));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(frame.Value<string>("reason")));
            }
        }

        Assert.Contains("allow", decisions);
        Assert.Contains("deny", decisions);
        Assert.Contains("unknown", decisions);
        Assert.Equal(3048, frames.Count);
        Assert.Equal(2730, frames.Count(token => token.Value<string>("decision") == "allow"));
        Assert.Equal(226, frames.Count(token => token.Value<string>("decision") == "deny"));
        Assert.Equal(92, frames.Count(token => token.Value<string>("decision") == "unknown"));
        Assert.DoesNotContain(frames, token =>
            token.Value<int>("index") >= 6
            && token.Value<string>("decision") == "unknown");
        Assert.DoesNotContain(frames, token =>
            token.Value<bool>("enabled")
            && token["assets"]?.Values<string>().Any(asset =>
                asset != null
                && (asset.Contains("/NoPortraits/", StringComparison.OrdinalIgnoreCase)
                    || asset.Contains("404", StringComparison.OrdinalIgnoreCase))) == true);
        Assert.Contains(frames, token => token.Value<bool>("enabled") && token.Value<int>("index") == 0);
        Assert.Contains(frames, token => token.Value<bool>("enabled") && token.Value<string>("marker") == "h");
        Assert.Contains(frames, token => token.Value<bool>("enabled") && token.Value<string>("marker") == "s");
        Assert.Contains(frames, token => token.Value<bool>("enabled") && token.Value<string>("marker") == "u");
        Assert.Contains(frames, token => token.Value<bool>("enabled") && token.Value<string>("marker") == "l");
        Assert.Contains(frames, token =>
            token.Value<bool>("enabled")
            && token.Value<string>("marker") == "a"
            && token.Value<string>("en")?.Contains("anger", StringComparison.OrdinalIgnoreCase) == true
            && token.Value<string>("zh")?.Contains("生气", StringComparison.Ordinal) == true);
        Assert.Contains(frames, token =>
            token.Value<int>("index") >= 6
            && token.Value<string>("marker") == token.Value<int>("index").ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(frames, token => token.Value<bool>("enabled") && token.Value<int>("index") >= 6);
        Assert.Contains(frames, token =>
            token.Value<bool>("enabled")
            && token.Value<string>("marker") == "u"
            && (token.Value<string>("en")?.Contains("angry", StringComparison.OrdinalIgnoreCase) == true
                || token.Value<string>("en")?.Contains("anger", StringComparison.OrdinalIgnoreCase) == true
                || token.Value<string>("en")?.Contains("scowl", StringComparison.OrdinalIgnoreCase) == true));
        Assert.Contains("Vanilla", representedSources);
        Assert.Contains("OhoDavi", representedSources);
        Assert.Contains("SVE", representedSources);
        Assert.Contains("SeasonalCuteSVE", representedSources);
        Assert.Contains("OhoDaviSVEAddon", representedSources);
        Assert.Contains("RomanceableRasmodia", representedSources);
        Assert.DoesNotContain(frames, token =>
            token["assets"]?.Values<string>().Any(asset =>
                asset != null
                && (asset.EndsWith("/Sophia_older_overlay.png", StringComparison.OrdinalIgnoreCase)
                    || asset.EndsWith("/Sophia_older_mu_overlay.png", StringComparison.OrdinalIgnoreCase))) == true);
        Assert.Contains(frames, token =>
            token.Value<bool>("enabled")
            && token["assets"]?.Values<string>().Any(asset =>
                asset?.Contains("+Sophia_older", StringComparison.OrdinalIgnoreCase) == true) == true);
    }

    [Theory]
    [InlineData("default.json")]
    [InlineData("zh.json")]
    public void Prompts_MetadataSchemas_UseCanonicalTopLevelAndActionFields(string fileName)
    {
        var prompts = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", fileName));
        string[] expectedTopLevelFields =
        {
            "rapportDelta", "endConversation", "ambientFollowUp", "emotionImpact",
            "behaviorInfluences", "actions", "helpRequests", "helpRequestUpdates",
            "conflicts", "memories"
        };
        string[] expectedActionFields =
        {
            "type", "amount", "durationMinutes", "delayMinutes", "targetLocation",
            "travelConsent", "itemId", "itemLabel", "reason"
        };

        foreach (string key in new[] { "instructionsLivingNpcMetadata", "instructionsLivingNpcMetadataOptimized" })
        {
            string schemaJson = ExtractFirstBalancedJson(prompts[key]);
            Assert.False(string.IsNullOrWhiteSpace(schemaJson), $"{fileName}:{key} must include a complete JSON schema");
            var schema = JObject.Parse(schemaJson);
            Assert.Equal(expectedTopLevelFields, schema.Properties().Select(property => property.Name));

            Match actionSchema = Regex.Match(
                schemaJson,
                "\\\"actions\\\":\\[\\{(?<body>.*?)\\}\\]",
                RegexOptions.Singleline);
            Assert.True(actionSchema.Success, $"{fileName}:{key} must include an explicit actions schema");

            string[] actualFields = Regex.Matches(actionSchema.Groups["body"].Value, "\\\"(?<name>[A-Za-z][A-Za-z0-9]*)\\\"\\s*:")
                .Select(match => match.Groups["name"].Value)
                .ToArray();
            Assert.Equal(expectedActionFields, actualFields);
        }
    }

    [Theory]
    [InlineData("default.json", "output formats", "role changes", "schemas", "data blocks", "hidden prompt")]
    [InlineData("zh.json", "输出格式", "角色变更", "schema", "数据区块", "隐藏提示词")]
    public void Prompts_ExplicitlyRejectInstructionsEmbeddedInRuntimeData(
        string fileName,
        string outputFormats,
        string roleChanges,
        string schemas,
        string dataBlocks,
        string hiddenPrompt)
    {
        var prompts = Deserialize<Dictionary<string, string>>(Path.Combine(AssetRoot, "prompts", fileName));

        string systemRule = prompts["systemUntrustedData"];
        Assert.Contains("<untrusted_data>", systemRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(outputFormats, systemRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(roleChanges, systemRule, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(schemas, systemRule, StringComparison.OrdinalIgnoreCase);

        string instructionReminder = prompts["instructionsUntrustedData"];
        Assert.Contains(dataBlocks, instructionReminder, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(hiddenPrompt, instructionReminder, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractFirstBalancedJson(string text)
    {
        int start = text.IndexOf('{');
        int depth = 0;
        bool inString = false;
        bool escaping = false;
        for (int i = start; i >= 0 && i < text.Length; i++)
        {
            char ch = text[i];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (inString && ch == '\\')
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
            }
            else if (!inString && ch == '{')
            {
                depth++;
            }
            else if (!inString && ch == '}' && --depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        return string.Empty;
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
