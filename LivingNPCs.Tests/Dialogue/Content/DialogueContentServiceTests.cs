using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Tests.Dialogue.Llm;
using StardewModdingAPI;
using StardewValley.GameData.Characters;

namespace LivingNPCs.Tests.Dialogue.Content;

[Collection("LlmLayer")]
public sealed class DialogueContentServiceTests : IDisposable
{
    private readonly FakeMonitor monitor = new();
    private readonly FakeContentPipeline pipeline = new();
    private readonly DialogueConfig config = new() { EnableSveCompatibility = true, UseOptimizedPrompts = false };

    public DialogueContentServiceTests()
    {
        DialogueServices.Initialize(null!, this.monitor, this.config);
        DialogueEngineGate.RuntimeDisabled = false;
        this.pipeline.PromptData["generalMale"] = "male";
        this.pipeline.PromptData["generalFemale"] = "female";
        this.pipeline.PromptData["generalHe"] = "he";
        this.pipeline.PromptData["generalShe"] = "she";
        this.pipeline.PromptData["generalHim"] = "him";
        this.pipeline.PromptData["generalHer"] = "her";
        this.pipeline.PromptData["generalHis"] = "his";
        this.pipeline.PromptData["generalHers"] = "hers";
        this.pipeline.PromptData["generalAnd"] = "and";
    }

    public void Dispose()
    {
        Util.PromptFallback = null;
        DialogueEngineGate.RuntimeDisabled = false;
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    private DialogueContentService CreateService()
    {
        return new DialogueContentService(this.pipeline);
    }

    private static CharacterData Character(StardewValley.Gender gender = StardewValley.Gender.Female, params string[] friends)
    {
        return new CharacterData
        {
            Age = NpcAge.Adult,
            Manner = NpcManner.Polite,
            SocialAnxiety = NpcSocialAnxiety.Outgoing,
            Optimism = NpcOptimism.Positive,
            Gender = gender,
            HomeRegion = "Town",
            CanBeRomanced = true,
            FriendsAndFamily = friends.ToDictionary(name => name, _ => string.Empty)
        };
    }

    [Fact]
    public void GetBio_LoadsAsset_DerivesPortraitsTopicsAndGender()
    {
        this.pipeline.Bios["Abigail"] = new NpcBio
        {
            Biography = "bio text",
            Unique = "special portrait",
            ExtraPortraits = { ["7"] = "wink" },
            Preoccupations = { "the mines", "music" }
        };
        this.pipeline.Characters["Abigail"] = Character(StardewValley.Gender.Female);
        this.pipeline.GiftTastes["Abigail"] = (new[] { "Amethyst", "music" }, new[] { "Clay" });
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Abigail");

        Assert.False(bio.Missing);
        Assert.DoesNotContain("u", bio.ExtraPortraits.Keys);
        Assert.Superset(new HashSet<string> { "0", "h", "s", "l", "a", "7" }, bio.ValidPortraits);
        Assert.DoesNotContain("u", bio.ValidPortraits);
        // 话题池 = 心事 ∪ 礼物名，忽略大小写去重。
        Assert.Equal(new[] { "the mines", "music", "Amethyst", "Clay" }, bio.TopicPool);
        Assert.Equal(PromptGender.Female, bio.ResolvedGender);
    }

    [Fact]
    public void GetBio_UniqueAloneDoesNotBecomePortraitAlias()
    {
        this.pipeline.Bios["Abigail"] = new NpcBio
        {
            Biography = "bio text",
            Unique = "softly distinctive persona"
        };
        this.pipeline.Characters["Abigail"] = Character(StardewValley.Gender.Female);
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Abigail");

        Assert.Equal("softly distinctive persona", bio.Unique);
        Assert.DoesNotContain("u", bio.ValidPortraits);
        Assert.DoesNotContain("u", bio.ExtraPortraits.Keys);
    }

    [Fact]
    public void GetBio_AllowsGamePortraitsAndNumbers_ButRejectsUnknownKeys()
    {
        this.pipeline.Bios["Abigail"] = new NpcBio
        {
            Biography = "bio text",
            Unique = "softly distinctive persona",
            ExtraPortraits =
            {
                ["u"] = "explicit unique frame",
                ["7"] = "wink",
                ["11"] = "alternate frame",
                ["x"] = "unsupported letter",
                ["smile"] = "unsupported word",
                ["!"] = "disable teaching"
            }
        };
        this.pipeline.Characters["Abigail"] = Character(StardewValley.Gender.Female);
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Abigail");

        Assert.Contains("u", bio.ValidPortraits);
        Assert.Equal("explicit unique frame", bio.ExtraPortraits["u"]);
        Assert.Contains("7", bio.ValidPortraits);
        Assert.Contains("11", bio.ValidPortraits);
        Assert.DoesNotContain("x", bio.ValidPortraits);
        Assert.DoesNotContain("smile", bio.ValidPortraits);
        Assert.DoesNotContain("!", bio.ValidPortraits);
    }

    [Fact]
    public void GetBio_GenderAssetField_OverridesOnlyWhenMatchingLocalizedWord()
    {
        this.pipeline.Bios["Abigail"] = new NpcBio { Biography = "b", Gender = "MALE" };
        this.pipeline.Bios["Alex"] = new NpcBio { Biography = "b", Gender = "not-a-word" };
        this.pipeline.Characters["Abigail"] = Character(StardewValley.Gender.Female);
        this.pipeline.Characters["Alex"] = Character(StardewValley.Gender.Male);
        DialogueContentService service = this.CreateService();

        Assert.Equal(PromptGender.Male, service.GetBio("Abigail").ResolvedGender);
        // 不匹配本地化词 → 忽略覆盖口，用游戏数据。
        Assert.Equal(PromptGender.Male, service.GetBio("Alex").ResolvedGender);
    }

    [Fact]
    public void GetBio_BlankBiography_FallsBackToCharacterData()
    {
        this.pipeline.Bios["Haley"] = new NpcBio { Biography = "   " };
        this.pipeline.Characters["Haley"] = Character(StardewValley.Gender.Female, "Emily", "Sam");
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Haley");

        Assert.False(bio.Missing);
        Assert.Contains("Haley", bio.Biography);
        Assert.Equal(BioFallbackBuilder.FallbackDisclaimer, bio.BiographyEnd);
        Assert.Equal(3, bio.Traits.Count);
        Assert.Equal(2, bio.Relationships.Count);
    }

    [Fact]
    public void GetBio_NoAssetNoCharacterData_MissingWithWarning()
    {
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Nobody");

        Assert.True(bio.Missing);
        Assert.Contains(this.monitor.MessagesAt(LogLevel.Warn), m => m.Contains("Nobody"));
    }

    [Fact]
    public void GetBio_RsvBlockedNpc_MissingWithoutAssetOrFallback()
    {
        this.pipeline.Bios["Bert"] = new NpcBio { Biography = "should not load" };
        this.pipeline.Characters["Bert"] = Character();
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Bert");

        Assert.True(bio.Missing);
        Assert.Equal(0, this.pipeline.BioLoadCount);
    }

    [Fact]
    public void GetBio_SveCharacterWithSwitchOff_ForcesFallback()
    {
        this.pipeline.Bios["Olivia"] = new NpcBio { Biography = "sve bio" };
        this.pipeline.Characters["Olivia"] = Character(StardewValley.Gender.Female, "Victor");
        this.config.EnableSveCompatibility = false;
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("Olivia");

        Assert.NotEqual("sve bio", bio.Biography);
        Assert.Equal(BioFallbackBuilder.FallbackDisclaimer, bio.BiographyEnd);
        Assert.Equal(0, this.pipeline.BioLoadCount);
    }

    [Fact]
    public void GetBio_SveActive_AppliesRelationshipPatch()
    {
        this.pipeline.IsSveLoaded = true;
        this.pipeline.Bios["Marlon"] = new NpcBio { Biography = "guild man" };
        this.pipeline.RelationshipPatches = new Dictionary<string, Dictionary<string, BioListEntry>>
        {
            ["Marlon"] = new() { ["Alesia"] = new BioListEntry { Id = "Alesia", Heading = "Alesia", Description = "fellow veteran" } }
        };
        DialogueContentService service = this.CreateService();

        NpcBio bio = service.GetBio("MarlonFay");

        Assert.Equal("fellow veteran", bio.Relationships["Alesia"].Description);
    }

    [Fact]
    public void GetBio_CachedUntilSveSwitchFlips()
    {
        this.pipeline.Bios["Abigail"] = new NpcBio { Biography = "b" };
        DialogueContentService service = this.CreateService();

        service.GetBio("Abigail");
        service.GetBio("Abigail");
        Assert.Equal(1, this.pipeline.BioLoadCount);

        this.config.EnableSveCompatibility = false;
        service.GetBio("Abigail");
        Assert.Equal(2, this.pipeline.BioLoadCount);
    }

    [Fact]
    public void GetBio_InvalidateBio_ReloadsNormalizedName()
    {
        this.pipeline.Bios["Abigail"] = new NpcBio { Biography = "b" };
        DialogueContentService service = this.CreateService();

        service.GetBio("Abigail·");
        service.InvalidateBio("Abigail");
        service.GetBio("Abigail");
        Assert.Equal(2, this.pipeline.BioLoadCount);
    }

    [Fact]
    public void GetWorldSummary_MergesSveDeltaOnlyWhenActive()
    {
        this.pipeline.FullSummary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Villagers"] = true },
            Villagers = new WorldSummarySection
            {
                Entries = { ["Abigail"] = new WorldSummaryEntry { Name = "Abigail", Description = "x" } }
            }
        };
        this.pipeline.SveDelta = new WorldSummary
        {
            Villagers = new WorldSummarySection
            {
                Entries = { ["Sophia"] = new WorldSummaryEntry { Name = "Sophia", Description = "sve" } }
            }
        };

        this.pipeline.IsSveLoaded = true;
        DialogueContentService service = this.CreateService();
        Assert.Contains("Sophia", service.GetWorldSummary().Villagers!.Entries.Keys);

        this.config.EnableSveCompatibility = false;
        Assert.DoesNotContain("Sophia", service.GetWorldSummary().Villagers!.Entries.Keys);
    }

    [Fact]
    public void GetWorldSummaryText_CachesPerVariant_InvalidateClears()
    {
        this.pipeline.FullSummary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Intro"] = false },
            Intro = new WorldSummarySection { Text = "full intro" }
        };
        this.pipeline.OptimizedSummary = new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Intro"] = false },
            Intro = new WorldSummarySection { Text = "optimized intro" }
        };
        DialogueContentService service = this.CreateService();

        Assert.Contains("full intro", service.GetWorldSummaryText(optimized: false));
        Assert.Contains("optimized intro", service.GetWorldSummaryText(optimized: true));
        service.GetWorldSummaryText(optimized: false);
        Assert.Equal(2, this.pipeline.WorldLoadCount);

        service.InvalidateWorldSummaries();
        service.GetWorldSummaryText(optimized: false);
        Assert.Equal(3, this.pipeline.WorldLoadCount);
    }

    [Fact]
    public void GetPromptSkeleton_MissingKey_ReturnsEmptyAndWarns()
    {
        DialogueContentService service = this.CreateService();

        Assert.Equal(string.Empty, service.GetPromptSkeleton("noSuchKey"));
        Assert.Contains(this.monitor.MessagesAt(LogLevel.Warn), m => m.Contains("noSuchKey"));
    }

    [Fact]
    public void GetPromptSkeleton_UsesGenderVariantFromPromptVariant()
    {
        this.pipeline.PromptData["mood"] = "base";
        this.pipeline.PromptData["mood.FemaleNpc"] = "female";
        DialogueContentService service = this.CreateService();

        Assert.Equal("female", service.GetPromptSkeleton("mood", new PromptVariant(PromptGender.Female, Optimized: false)));
        Assert.Equal("base", service.GetPromptSkeleton("mood"));
    }

    [Fact]
    public void Pronouns_ComeFromPromptTable()
    {
        DialogueContentService service = this.CreateService();

        Assert.Equal(("she", "her", "hers"), service.GetPronouns(PromptGender.Female));
        Assert.Equal(("he", "him", "his"), service.GetPronouns(PromptGender.Male));
        Assert.Equal(("", "", ""), service.GetPronouns(PromptGender.None));
        Assert.Equal("female", service.GetGenderNoun(PromptGender.Female));
    }

    [Fact]
    public void UtilPromptFallback_IsWiredToPromptTable()
    {
        this.pipeline.PromptData["outputRespond"] = "Respond";
        this.CreateService();

        Assert.Equal("Respond", Util.GetString("outputRespond", returnNull: true));
    }
}
