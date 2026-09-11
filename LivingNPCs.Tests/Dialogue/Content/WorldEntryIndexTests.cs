using LivingNPCs.Dialogue.Content;
using Newtonsoft.Json;

namespace LivingNPCs.Tests.Dialogue.Content;

[Collection("LlmLayer")]
public sealed class WorldEntryIndexTests
{
    [Fact]
    public void CoreIsStableAndKeepsBackgroundAndTranslationsOutsideRetrievedEntries()
    {
        WorldEntryIndex index = WorldRetrievalFixtures.Index();
        WorldRetrievalResult library = index.Retrieve(new() { PlayerText = "图书馆" });
        WorldRetrievalResult mines = index.Retrieve(new() { PlayerText = "矿井" });

        Assert.Equal(library.CoreText, mines.CoreText);
        Assert.Contains("CORE INTRO", library.CoreText);
        Assert.Contains("FARMER BACKGROUND", library.CoreText);
        Assert.Contains("CORE OUTRO", library.CoreText);
        Assert.Contains("TRANSLATED NAMES", library.CoreText);
        Assert.DoesNotContain("Borrow books", library.CoreText);
        Assert.DoesNotContain("FARMER BACKGROUND", library.RetrievedText);
        Assert.DoesNotContain("TRANSLATED NAMES", library.RetrievedText);
        Assert.Contains("Borrow books", library.RetrievedText);
    }

    [Theory]
    [InlineData("我想和潘妮谈谈。", "Penny")]
    [InlineData("想去图书馆。", "Museum and Library")]
    [InlineData("花舞节怎么样？", "Flower Dance")]
    [InlineData("冬天呢？", "Winter")]
    public void ChineseEntityNamesResolveEnglishWorldEntries(string query, string entryName)
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new() { PlayerText = query });

        Assert.Contains("**" + entryName + "**", result.RetrievedText);
        Assert.Equal(string.Empty, result.FallbackReason);
    }

    [Theory]
    [InlineData("我想借书。", "Museum and Library")]
    [InlineData("我想钓鱼。", "Willy")]
    [InlineData("I want to borrow a book.", "Museum and Library")]
    public void TopicAliasesRetrieveEnglishFactsWithoutAnEntityName(string query, string entryName)
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new() { PlayerText = query });

        Assert.Contains("**" + entryName + "**", result.RetrievedText);
        Assert.True(result.SelectedEntryCount < result.TotalEntryCount);
    }

    [Fact]
    public void NewPlayerTopicWinsOverUnrelatedRecentDialogue()
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new()
        {
            PlayerText = "去矿井挖矿吧。",
            RecentDialogue = "We were at the Museum and Library borrowing books with Penny."
        });

        Assert.Contains("**The Mines**", result.RetrievedText);
        Assert.DoesNotContain("**Museum and Library**", result.RetrievedText);
        Assert.DoesNotContain("**Penny**", result.RetrievedText);
    }

    [Fact]
    public void ExplicitContinuationCanUseTheRecentTopic()
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new()
        {
            PlayerText = "还有呢？",
            RecentDialogue = "The library has books to borrow."
        });

        Assert.Contains("**Museum and Library**", result.RetrievedText);
        Assert.InRange(result.SelectedEntryCount, 1, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("你好，谢谢。")]
    [InlineData("量子真空的相位怎么测？")]
    public void UnrelatedOrEmptyQueryKeepsOnlyScenePins(string query)
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new()
        {
            PlayerText = query,
            RecentDialogue = "Willy discussed fishing at the ocean.",
            NpcName = "Penny·",
            LocationName = "ArchaeologyHouse",
            CurrentDestination = "Farm",
            Season = "spring",
            FestivalName = "spring24"
        });

        Assert.Equal("NoRelevantEntries", result.FallbackReason);
        Assert.Equal(5, result.SelectedEntryCount);
        Assert.Contains("**Penny**", result.RetrievedText);
        Assert.Contains("**Museum and Library**", result.RetrievedText);
        Assert.Contains("**The Farm**", result.RetrievedText);
        Assert.Contains("**Spring**", result.RetrievedText);
        Assert.Contains("**Flower Dance**", result.RetrievedText);
        Assert.DoesNotContain("**Willy**", result.RetrievedText);
        Assert.DoesNotContain("**The Mines**", result.RetrievedText);
        Assert.Contains("Parsnip and Potato", result.RetrievedText);
    }

    [Fact]
    public void UnrelatedQueryWithoutPinsDoesNotExpandTheWholeWorld()
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new() { PlayerText = "你好" });

        Assert.Equal(0, result.SelectedEntryCount);
        Assert.Equal(string.Empty, result.RetrievedText);
        Assert.Contains("FARMER BACKGROUND", result.CoreText);
        Assert.Equal("NoRelevantEntries", result.FallbackReason);
    }

    [Theory]
    [InlineData("pack.Librarian")]
    [InlineData("Civic Reading Hall")]
    [InlineData("星露书屋")]
    public void ContentPackKeyIdNameAndOptionalAliasesAreSearchable(string query)
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Locations!.Entries["custom-reading"] = new()
        {
            Id = "pack.Librarian", Name = "Civic Reading Hall", Description = "Unique community reference fact.",
            Aliases = new() { "星露书屋" }
        };

        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new() { PlayerText = query });

        Assert.Contains("Unique community reference fact.", result.RetrievedText);
    }

    [Fact]
    public void CustomLocalizedDisplayNamePinsAnEntryWithAnUnrelatedInternalId()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Villagers!.Entries["pack.caretaker"] = new() { Id = "pack.caretaker", Name = "林间看守人", Description = "Looks after the grove." };

        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new()
        {
            PlayerText = "你好", NpcName = "another-runtime-id", NpcDisplayName = "林间看守人"
        });

        Assert.Contains("Looks after the grove.", result.RetrievedText);
        Assert.Equal(1, result.SelectedEntryCount);
    }

    [Fact]
    public void ExplicitEntriesExceedSoftBudgetsAndPreserveLongFactsWhole()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        string longFact = new string('x', 9000) + " COMPLETE FACT END";
        summary.Locations!.Entries["LongPlace"] = new() { Name = "LongPlace", Description = longFact };

        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(
            new() { PlayerText = "Penny, Alex, LongPlace and 图书馆" }, entryBudget: 1, characterBudget: 1);

        Assert.Equal(4, result.SelectedEntryCount);
        Assert.Contains(longFact, result.RetrievedText);
        Assert.Contains("**Penny**", result.RetrievedText);
        Assert.Contains("**Alex**", result.RetrievedText);
        Assert.Contains("**Museum and Library**", result.RetrievedText);
    }

    [Fact]
    public void BroadSectionEnumerationKeepsAllRequestedEntriesBeyondTheDefaultBudget()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        for (int index = 0; index < 12; index++)
            summary.Villagers!.Entries["Extra" + index] = new() { Name = "Extra" + index, Description = "Full fact " + index };

        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new() { PlayerText = "列出所有村民。" });

        Assert.Equal("BroadSectionQuery", result.FallbackReason);
        Assert.Equal(summary.Villagers!.Entries.Count, result.SelectedEntryCount);
        Assert.Contains("Full fact 11", result.RetrievedText);
        Assert.DoesNotContain("**The Mines**", result.RetrievedText);
    }

    [Theory]
    [InlineData("请介绍一下星露谷。")]
    [InlineData("Give an overview of the valley.")]
    public void BroadWorldOverviewCanUseAllValidEntries(string query)
    {
        WorldRetrievalResult result = WorldRetrievalFixtures.Index().Retrieve(new() { PlayerText = query });

        Assert.Equal("BroadWorldQuery", result.FallbackReason);
        Assert.Equal(result.TotalEntryCount, result.SelectedEntryCount);
        Assert.Contains("**Winter**", result.RetrievedText);
        Assert.Contains("**Alex**", result.RetrievedText);
    }

    [Fact]
    public void InvalidInputFallsBackWithoutDroppingValidWorldFacts()
    {
        WorldEntryIndex index = WorldRetrievalFixtures.Index();
        foreach (WorldRetrievalQuery? query in new WorldRetrievalQuery?[]
                 { null, new() { PlayerText = "bad\0input" }, new() { PlayerText = new string('z', 8193) }, new() { PlayerText = "\ud800" } })
        {
            WorldRetrievalResult result = index.Retrieve(query);
            Assert.Equal("InvalidQuery", result.FallbackReason);
            Assert.Equal(result.TotalEntryCount, result.SelectedEntryCount);
            Assert.Contains("**Penny**", result.RetrievedText);
        }
    }

    [Fact]
    public void MissingOrInvalidSectionOrderSalvagesValidSectionsAndMarksTheFallback()
    {
        foreach (Dictionary<string, bool>? order in new Dictionary<string, bool>?[]
                 { null, new(), new() { ["Unknown"] = true } })
        {
            WorldSummary summary = WorldRetrievalFixtures.Summary();
            summary.SectionOrder = order!;
            WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new() { PlayerText = "你好" });

            Assert.Equal("InvalidWorldIndex", result.FallbackReason);
            Assert.Equal(result.TotalEntryCount, result.SelectedEntryCount);
            Assert.Contains("**Penny**", result.RetrievedText);
            Assert.Contains("FARMER BACKGROUND", result.CoreText);
        }
    }

    [Fact]
    public void NullSectionsAndEntriesDoNotDestroyOtherValidFacts()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Festivals = null;
        summary.Locations!.Entries["broken"] = null!;
        summary.Seasons!.Entries = null!;

        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new() { PlayerText = "你好" });

        Assert.Equal("InvalidWorldIndex", result.FallbackReason);
        Assert.Contains("**Penny**", result.RetrievedText);
        Assert.Contains("Borrow books", result.RetrievedText);
    }

    [Fact]
    public void CapturedEntriesListsAndAliasesDoNotChangeWhenTheSourceIsMutated()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Villagers!.Entries["Penny"].Aliases = new() { "OldNickname" };
        WorldEntryIndex index = WorldRetrievalFixtures.Index(summary);
        WorldRetrievalResult before = index.Retrieve(new() { PlayerText = "OldNickname", Season = "Spring" });

        summary.Intro!.Text = "CHANGED INTRO";
        summary.Villagers.Entries["Penny"].Description = "CHANGED PENNY";
        summary.Villagers.Entries["Penny"].Aliases!.Add("NewNickname");
        summary.Seasons!.Entries["Spring"].Crops!.Add("CHANGED CROP");
        WorldRetrievalResult after = index.Retrieve(new() { PlayerText = "OldNickname", Season = "Spring" });

        Assert.Equal(before.CoreText, after.CoreText);
        Assert.Equal(before.RetrievedText, after.RetrievedText);
        Assert.Equal(0, index.Retrieve(new() { PlayerText = "NewNickname" }).SelectedEntryCount);
    }

    [Fact]
    public void RetrievalDoesNotConsultTheRendererAfterIndexConstruction()
    {
        bool captured = false;
        var renderer = new WorldSummaryRenderer(_ => captured ? throw new InvalidOperationException("late renderer access") : "", null);
        var index = new WorldEntryIndex(WorldRetrievalFixtures.Summary(), renderer);
        captured = true;

        Assert.Contains("**Penny**", index.Retrieve(new() { PlayerText = "潘妮" }).RetrievedText);
        Assert.Equal(index.Retrieve(null).TotalEntryCount, index.Retrieve(null).SelectedEntryCount);
    }

    [Fact]
    public void RsvEntriesAndBlockedLinesStayFilteredIncludingFullFallback()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Intro!.Text = "Allowed introduction.\nRidgeside Village introduction.";
        summary.Villagers!.Entries["Helen"] = new() { Name = "Helen", Description = "Blocked structured identity." };
        summary.Villagers.Entries["ForeignName"] = new() { Name = "ForeignName", Description = "Safe beginning.\nRidgeside Village fact." };
        summary.Locations!.Entries["Ridge"] = new() { Name = "Distant village", Description = "Blocked legacy location." };
        WorldEntryIndex index = WorldRetrievalFixtures.Index(summary);

        foreach (WorldRetrievalResult result in new[] { index.Retrieve(null), index.Retrieve(new() { PlayerText = "Helen and Ridge" }) })
        {
            Assert.DoesNotContain("Ridgeside", result.CoreText + result.RetrievedText);
            Assert.DoesNotContain("Blocked structured", result.RetrievedText);
            Assert.DoesNotContain("Blocked legacy", result.RetrievedText);
            Assert.DoesNotContain("Safe beginning", result.RetrievedText);
            Assert.Contains("Allowed introduction.", result.CoreText);
        }
    }

    [Fact]
    public void EnglishNameDoesNotMatchInsideAnotherWord()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Villagers!.Entries["Ann"] = new() { Name = "Ann", Description = "A custom resident." };

        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new() { PlayerText = "annual" });

        Assert.DoesNotContain("**Ann**", result.RetrievedText);
        Assert.Equal("NoRelevantEntries", result.FallbackReason);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShippedWorldVariantsSupportChineseLookupWithoutSelectingEverything(bool optimized, bool sve)
    {
        WorldSummary summary = WorldRetrievalFixtures.LoadWorld("world", optimized);
        if (sve)
            summary = SveContentRules.MergeWorldDelta(summary, WorldRetrievalFixtures.LoadWorld("world-sve", optimized));

        WorldEntryIndex index = WorldRetrievalFixtures.Index(summary);
        WorldRetrievalResult result = index.Retrieve(new() { PlayerText = "我想找潘妮借书，再去图书馆。", NpcName = "Penny", Season = "spring" });

        Assert.Contains("**Penny**", result.RetrievedText);
        Assert.Contains("**Museum and Library**", result.RetrievedText);
        Assert.Contains("**Spring**", result.RetrievedText);
        Assert.InRange(result.SelectedEntryCount, 3, WorldEntryIndex.DefaultEntryBudget);
        Assert.True(result.SelectedEntryCount < result.TotalEntryCount);
        Assert.Equal(string.Empty, result.FallbackReason);
        if (sve)
        {
            WorldRetrievalResult sveResult = index.Retrieve(new() { PlayerText = "去蓝月葡萄园看看索菲亚，然后拜访冈瑟。" });
            Assert.Contains("**Blue Moon Vineyard**", sveResult.RetrievedText);
            Assert.Contains("**Sophia**", sveResult.RetrievedText);
            Assert.Contains("**Gunther**", sveResult.RetrievedText);
        }
    }

    [Theory]
    [InlineData(false, "那里怎么走？")]
    [InlineData(true, "那里怎么走？")]
    [InlineData(false, "怎么去那里？")]
    [InlineData(true, "How do I get there?")]
    public void ShippedBeachRegionIsRecalledForNavigationContinuationWithoutADestination(bool optimized, string query)
    {
        WorldEntryIndex index = WorldRetrievalFixtures.Index(WorldRetrievalFixtures.LoadWorld("world", optimized));
        WorldRetrievalResult result = index.Retrieve(new()
        {
            PlayerText = query,
            RecentDialogue = "我们刚才说，去海滩走走吧。",
            LocationName = "Town",
            CurrentDestination = string.Empty
        });

        Assert.Contains("**Beach**", result.RetrievedText);
        Assert.InRange(result.SelectedEntryCount, 1, 4);
        Assert.Equal(string.Empty, result.FallbackReason);
    }

    [Theory]
    [InlineData(false, "Beach", "Beach")]
    [InlineData(true, "Beach", "Beach")]
    [InlineData(false, "Town", "Pelican Town")]
    [InlineData(true, "Town", "Pelican Town")]
    public void ShippedRegionOnlyCurrentLocationsHaveBoundedSceneContext(bool optimized, string location, string region)
    {
        WorldEntryIndex index = WorldRetrievalFixtures.Index(WorldRetrievalFixtures.LoadWorld("world", optimized));
        WorldRetrievalResult result = index.Retrieve(new() { PlayerText = "你好", LocationName = location });

        Assert.Contains("**" + region + "**", result.RetrievedText);
        Assert.InRange(result.SelectedEntryCount, 1, 2);
        Assert.Equal("NoRelevantEntries", result.FallbackReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentLibraryQuestionBeatsPreviousBeachTopicInShippedWorld(bool optimized)
    {
        WorldEntryIndex index = WorldRetrievalFixtures.Index(WorldRetrievalFixtures.LoadWorld("world", optimized));
        WorldRetrievalResult result = index.Retrieve(new()
        {
            PlayerText = "那图书馆怎么走？",
            RecentDialogue = "我们刚才说，去海滩走走吧。",
            LocationName = "Town"
        });

        Assert.Contains("**Museum and Library**", result.RetrievedText);
        Assert.DoesNotContain("**Beach**", result.RetrievedText);
        Assert.DoesNotContain("**Willy's Fish Shop**", result.RetrievedText);
    }

    [Theory]
    [InlineData("请介绍一下这里的图书馆。", "Museum and Library")]
    [InlineData("介绍一下星露谷的花舞节。", "Flower Dance")]
    [InlineData("介绍一下花舞节这个节日。", "Flower Dance")]
    public void IntroducingASpecificEntryDoesNotBecomeAWholeWorldOrSectionOverview(string query, string name)
    {
        WorldEntryIndex index = WorldRetrievalFixtures.Index(WorldRetrievalFixtures.LoadWorld("world", optimized: false));
        WorldRetrievalResult result = index.Retrieve(new() { PlayerText = query });

        Assert.Contains("**" + name + "**", result.RetrievedText);
        Assert.InRange(result.SelectedEntryCount, 1, WorldEntryIndex.DefaultEntryBudget);
        Assert.True(result.SelectedEntryCount < result.TotalEntryCount);
        Assert.Equal(string.Empty, result.FallbackReason);
    }

    [Fact]
    public void NavigationTemplateWordsDoNotReplaceTheRecentTopicWithAnUnrelatedCustomFact()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Locations!.Entries["TheFarm"].Description = "Get fresh produce here.";
        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new()
        {
            PlayerText = "How do I get there?",
            RecentDialogue = "We were talking about the library."
        });

        Assert.Contains("**Museum and Library**", result.RetrievedText);
        Assert.DoesNotContain("**The Farm**", result.RetrievedText);
    }

    [Fact]
    public void NewTopicAfterAContinuationPhraseStillWinsOverThePreviousTopic()
    {
        WorldSummary summary = WorldRetrievalFixtures.Summary();
        summary.Locations!.Entries["TheFarm"].Description = "Tell visitors about fresh produce.";
        WorldRetrievalResult result = WorldRetrievalFixtures.Index(summary).Retrieve(new()
        {
            PlayerText = "Tell me more about mines.",
            RecentDialogue = "We were talking about the library."
        });

        Assert.Contains("**The Mines**", result.RetrievedText);
        Assert.DoesNotContain("**Museum and Library**", result.RetrievedText);
        Assert.DoesNotContain("**The Farm**", result.RetrievedText);
    }
}

internal static class WorldRetrievalFixtures
{
    internal static WorldEntryIndex Index(WorldSummary? summary = null) => new(summary ?? Summary(), new WorldSummaryRenderer(
        key => key switch { "generalAnd" => "and", "seasonCrops" => "Crops:", "seasonForage" => "Forage:", "gameSummaryTranslations" => "TRANSLATED NAMES", _ => "" }, null));

    internal static WorldSummary Summary(string label = "") => new()
    {
        SectionOrder = new() { ["Intro"] = false, ["FarmerBackground"] = false, ["Seasons"] = true, ["Locations"] = true, ["Festivals"] = true, ["Villagers"] = true, ["Outro"] = false },
        Intro = new() { Text = "CORE INTRO " + label },
        FarmerBackground = new() { Text = "FARMER BACKGROUND" },
        Outro = new() { Text = "CORE OUTRO" },
        Seasons = new()
        {
            Entries =
            {
                ["Spring"] = new() { Id = "Spring", Name = "Spring", Description = "A rainy growing season.", Crops = new() { "Parsnip", "Potato" }, Forage = new() { "Leek" } },
                ["Winter"] = new() { Id = "Winter", Name = "Winter", Description = "Snowy and quiet." }
            }
        },
        Locations = new()
        {
            Entries =
            {
                ["TheFarm"] = new() { Id = "TheFarm", Name = "The Farm", Description = "The inherited farm.", Region = "Farm" },
                ["PelicanTown_LibraryMuseum"] = new() { Id = "PelicanTown_LibraryMuseum", Name = "Museum and Library", Description = "Borrow books and read at the library. " + label, Region = "Pelican Town" },
                ["Mountain_TheMines"] = new() { Id = "Mountain_TheMines", Name = "The Mines", Description = "Mining ore and gems underground.", Region = "Mountain" }
            }
        },
        Festivals = new() { Entries = { ["spring24"] = new() { Id = "spring24", Name = "Flower Dance", Description = "Dancing together in spring." } } },
        Villagers = new()
        {
            Entries =
            {
                ["Penny"] = new() { Id = "Penny", Name = "Penny", Description = "A gentle teacher." },
                ["Alex"] = new() { Id = "Alex", Name = "Alex", Description = "Enjoys gridball." },
                ["Willy"] = new() { Id = "Willy", Name = "Willy", Description = "A fisherman selling fishing tackle and bait." }
            }
        }
    };

    internal static WorldSummary LoadWorld(string directory, bool optimized)
    {
        string? current = AppContext.BaseDirectory;
        for (int depth = 0; depth < 8 && current != null; depth++, current = Path.GetDirectoryName(current))
        {
            string candidate = Path.Combine(current, "LivingNPCs", "assets", "dialogue", directory, optimized ? "GameSummaryOptimized.json" : "GameSummary.json");
            if (File.Exists(candidate))
                return JsonConvert.DeserializeObject<WorldSummary>(File.ReadAllText(candidate))!;
        }
        throw new InvalidOperationException("Could not locate shipped world assets.");
    }
}
