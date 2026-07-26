using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Dialogue.Engine;
using LivingNPCs.Dialogue.Persistence;
using LivingNPCs.Tests.Dialogue.Persistence;

namespace LivingNPCs.Tests.Dialogue.Content;

/// <summary>
/// Pins the cache-invalidation wiring that used to be missing: (a) a game-language switch must
/// invalidate the bio cache, all four merged/rendered world-summary slots, the prompt table and
/// the engine's dialogue-sample library (SMAPI raises no AssetsInvalidated for locale changes);
/// (b) invalidating Characters/Dialogue/* or a Bios/* asset must rebuild the sample library,
/// which previously stayed frozen at its first capture for the whole session.
/// </summary>
public sealed class ContentInvalidationWiringTests
{
    private const string TestNpc = "TestNpc";

    // ---- ClassifyInvalidatedAssets (pure rules) ----

    [Theory]
    [InlineData("Characters/Dialogue/Abigail")]
    [InlineData("Characters/Dialogue/MarriageDialogueAbigail")]
    [InlineData("characters/dialogue/abigail")]
    [InlineData(@"Characters\Dialogue\Abigail")]
    public void Classify_CharactersDialogueAssets_RequestSampleRebuild(string assetName)
    {
        var plan = DialogueContentSetup.ClassifyInvalidatedAssets(new[] { assetName });

        Assert.True(plan.DialogueSamples);
        Assert.False(plan.Portraits);
        Assert.False(plan.Prompts);
        Assert.False(plan.WorldSummaries);
        Assert.Empty(plan.BioNames);
    }

    [Theory]
    [InlineData("Portraits/Abigail")]
    [InlineData("portraits/abigail_beach")]
    public void Classify_PortraitAssets_SetPortraitsOnly(string assetName)
    {
        var plan = DialogueContentSetup.ClassifyInvalidatedAssets(new[] { assetName });

        Assert.True(plan.Portraits);
        Assert.False(plan.DialogueSamples);
    }

    [Fact]
    public void Classify_PromptAndWorldAssets_SetTheirFlags()
    {
        var plan = DialogueContentSetup.ClassifyInvalidatedAssets(new[]
        {
            ContentAssetNames.Prompts,
            ContentAssetNames.GameSummaryOptimized
        });

        Assert.True(plan.Prompts);
        Assert.True(plan.WorldSummaries);
        Assert.False(plan.DialogueSamples);
        Assert.False(plan.Portraits);
    }

    [Fact]
    public void Classify_BioAsset_InvalidatesBioAndSampleLibrary()
    {
        var plan = DialogueContentSetup.ClassifyInvalidatedAssets(new[]
        {
            ContentAssetNames.BiosPrefix + "Abigail"
        });

        // The sample library merges the bio's Dialogue dictionary, so a bio swap must also
        // rebuild the samples.
        Assert.Equal(new[] { "Abigail" }, plan.BioNames);
        Assert.True(plan.DialogueSamples);
    }

    [Theory]
    [InlineData("Data/Mail")]
    [InlineData("Characters/Abigail")]
    [InlineData("Characters/DialogueX")]
    [InlineData("Portraits2/Abigail")]
    [InlineData("")]
    public void Classify_UnrelatedAssets_ProduceEmptyPlan(string assetName)
    {
        var plan = DialogueContentSetup.ClassifyInvalidatedAssets(new[] { assetName });

        Assert.False(plan.Portraits);
        Assert.False(plan.Prompts);
        Assert.False(plan.WorldSummaries);
        Assert.False(plan.DialogueSamples);
        Assert.Empty(plan.BioNames);
    }

    [Fact]
    public void Classify_MixedBatch_AggregatesAllActions()
    {
        var plan = DialogueContentSetup.ClassifyInvalidatedAssets(new[]
        {
            "Portraits/Abigail",
            "Characters/Dialogue/Haley",
            ContentAssetNames.Prompts,
            ContentAssetNames.GameSummary,
            ContentAssetNames.BiosPrefix + "Penny",
            ContentAssetNames.BiosPrefix + "Sam",
            "Data/Mail"
        });

        Assert.True(plan.Portraits);
        Assert.True(plan.DialogueSamples);
        Assert.True(plan.Prompts);
        Assert.True(plan.WorldSummaries);
        Assert.Equal(new[] { "Penny", "Sam" }, plan.BioNames);
    }

    // ---- ApplyInvalidationPlan against a real DialogueContentService ----

    [Fact]
    public void Apply_BioNames_ForcesBioReloadOnNextAccess()
    {
        var (service, pipeline) = CreateService();
        _ = service.GetBio(TestNpc);
        _ = service.GetBio(TestNpc);
        Assert.Equal(1, pipeline.BioLoadCount);

        var plan = new DialogueContentSetup.ContentInvalidationPlan();
        plan.BioNames.Add(TestNpc);
        DialogueContentSetup.ApplyInvalidationPlan(plan, service, engine: null);

        _ = service.GetBio(TestNpc);
        Assert.Equal(2, pipeline.BioLoadCount);
    }

    [Fact]
    public void Apply_WorldSummaries_RerendersFromTheSwappedAsset()
    {
        var (service, pipeline) = CreateService();
        Assert.Contains("v1", service.GetWorldSummaryText(optimized: false));

        pipeline.FullSummary = Summary("v2");
        Assert.Contains("v1", service.GetWorldSummaryText(optimized: false)); // still cached

        DialogueContentSetup.ApplyInvalidationPlan(
            new DialogueContentSetup.ContentInvalidationPlan { WorldSummaries = true },
            service,
            engine: null);

        Assert.Contains("v2", service.GetWorldSummaryText(optimized: false));
    }

    [Fact]
    public void Apply_Prompts_RebuildsThePromptTable()
    {
        var (service, pipeline) = CreateService();
        Assert.Equal("and", service.GetPromptSkeleton("generalAnd"));
        Assert.Equal("and", service.GetPromptSkeleton("generalAnd"));
        Assert.Equal(1, pipeline.PromptLoadCount);

        DialogueContentSetup.ApplyInvalidationPlan(
            new DialogueContentSetup.ContentInvalidationPlan { Prompts = true },
            service,
            engine: null);

        Assert.Equal("and", service.GetPromptSkeleton("generalAnd"));
        Assert.Equal(2, pipeline.PromptLoadCount);
    }

    [Fact]
    public void Apply_WithNothingWired_DoesNotThrow()
    {
        var plan = new DialogueContentSetup.ContentInvalidationPlan
        {
            Portraits = true,
            Prompts = true,
            WorldSummaries = true,
            DialogueSamples = true
        };
        plan.BioNames.Add(TestNpc);

        DialogueContentSetup.ApplyInvalidationPlan(plan, service: null, engine: null);
    }

    [Fact]
    public void Apply_DialogueSamples_ReachesTheEngineInvalidator()
    {
        DialogueEngine engine = CreateEngine();

        DialogueContentSetup.ApplyInvalidationPlan(
            new DialogueContentSetup.ContentInvalidationPlan { DialogueSamples = true },
            service: null,
            engine: engine);
    }

    // ---- Locale-change invalidation ----

    [Fact]
    public void LocaleChange_ClearsBioWorldAndPromptCaches()
    {
        var (service, pipeline) = CreateService();
        _ = service.GetBio(TestNpc);
        Assert.Contains("v1", service.GetWorldSummaryText(optimized: false));
        Assert.Equal("and", service.GetPromptSkeleton("generalAnd"));
        Assert.Equal(1, pipeline.BioLoadCount);
        Assert.Equal(1, pipeline.WorldLoadCount);
        Assert.Equal(1, pipeline.PromptLoadCount);

        pipeline.FullSummary = Summary("v2");
        DialogueContentSetup.InvalidateLocaleSensitiveCaches(service, engine: null);

        _ = service.GetBio(TestNpc);
        Assert.Contains("v2", service.GetWorldSummaryText(optimized: false));
        Assert.Equal("and", service.GetPromptSkeleton("generalAnd"));
        Assert.Equal(2, pipeline.BioLoadCount);
        Assert.Equal(2, pipeline.WorldLoadCount);
        Assert.Equal(2, pipeline.PromptLoadCount);
    }

    [Fact]
    public void LocaleChange_WithEngine_InvalidatesSampleLibraryWithoutThrowing()
    {
        DialogueEngine engine = CreateEngine();

        DialogueContentSetup.InvalidateLocaleSensitiveCaches(service: null, engine: engine);
        DialogueContentSetup.InvalidateLocaleSensitiveCaches(service: null, engine: null);
    }

    // ---- helpers ----

    private static (DialogueContentService Service, FakeContentPipeline Pipeline) CreateService()
    {
        var pipeline = new FakeContentPipeline
        {
            PromptData = new Dictionary<string, string> { ["generalAnd"] = "and" },
            FullSummary = Summary("v1")
        };
        pipeline.Bios[TestNpc] = new NpcBio { Biography = "A test biography." };
        return (new DialogueContentService(pipeline), pipeline);
    }

    private static WorldSummary Summary(string introText)
    {
        return new WorldSummary
        {
            SectionOrder = new Dictionary<string, bool> { ["Intro"] = false },
            Intro = new WorldSummarySection { Text = introText }
        };
    }

    private static DialogueEngine CreateEngine()
    {
        var store = new DialogueHistoryStore(new FakePersistenceEnvironment()) { ArchiveSink = null };
        return new DialogueEngine(new DialogueEngineServices
        {
            History = new EngineHistoryWriter(store),
            GetHistory = store.GetHistory
        });
    }
}
