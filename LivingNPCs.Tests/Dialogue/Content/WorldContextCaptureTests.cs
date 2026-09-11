using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Tests.Dialogue.Llm;

namespace LivingNPCs.Tests.Dialogue.Content;

[Collection("LlmLayer")]
public sealed class WorldContextCaptureTests : IDisposable
{
    private readonly FakeContentPipeline pipeline = new()
    {
        FullSummary = WorldRetrievalFixtures.Summary("FULL ORIGINAL"),
        OptimizedSummary = WorldRetrievalFixtures.Summary("OPTIMIZED ORIGINAL"),
        PromptData = new() { ["generalAnd"] = "and", ["gameSummaryTranslations"] = "ORIGINAL TRANSLATIONS" }
    };
    private readonly DialogueConfig config = new() { EnableSveCompatibility = true };

    public WorldContextCaptureTests()
        => DialogueServices.Initialize(null!, new FakeMonitor(), this.config);

    public void Dispose()
    {
        Util.PromptFallback = null;
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
    }

    [Fact]
    public void DirectAccessCachesIndexUntilWorldInvalidation()
    {
        var service = new DialogueContentService(this.pipeline);
        var query = new WorldRetrievalQuery { PlayerText = "图书馆" };
        WorldRetrievalResult before = service.GetWorldContext(false, query);
        this.pipeline.FullSummary!.Locations!.Entries["PelicanTown_LibraryMuseum"].Description = "RELOADED LIBRARY";

        Assert.Equal(before.RetrievedText, service.GetWorldContext(false, query).RetrievedText);
        Assert.Equal(1, this.pipeline.WorldLoadCount);
        service.InvalidateWorldSummaries();

        Assert.Contains("RELOADED LIBRARY", service.GetWorldContext(false, query).RetrievedText);
        Assert.Equal(2, this.pipeline.WorldLoadCount);
    }

    [Fact]
    public async Task OldCaptureSurvivesInvalidationAndWorkerUseWithoutAnyContentReads()
    {
        var service = new DialogueContentService(this.pipeline);
        var query = new WorldRetrievalQuery { PlayerText = "图书馆", Season = "Spring" };
        Func<bool, WorldRetrievalQuery, WorldRetrievalResult> oldCapture = service.CaptureWorldContext();
        WorldRetrievalResult oldFull = oldCapture(false, query);
        WorldRetrievalResult oldOptimized = oldCapture(true, query);
        int promptLoads = this.pipeline.PromptLoadCount;
        Assert.Equal(2, this.pipeline.WorldLoadCount);

        this.pipeline.FullSummary!.Intro!.Text = "RELOADED FULL INTRO";
        this.pipeline.FullSummary.Locations!.Entries["PelicanTown_LibraryMuseum"].Description = "RELOADED FULL LIBRARY";
        this.pipeline.OptimizedSummary!.Intro!.Text = "RELOADED OPTIMIZED INTRO";
        this.pipeline.OptimizedSummary.Locations!.Entries["PelicanTown_LibraryMuseum"].Description = "RELOADED OPTIMIZED LIBRARY";
        this.pipeline.PromptData["gameSummaryTranslations"] = "RELOADED TRANSLATIONS";
        this.pipeline.Preprocessor = _ => throw new InvalidOperationException("Late game content access");
        service.InvalidateAll();

        WorldRetrievalResult retainedFull = await Task.Run(() => oldCapture(false, query));
        WorldRetrievalResult retainedOptimized = await Task.Run(() => oldCapture(true, query));

        Assert.Equal(oldFull.CoreText, retainedFull.CoreText);
        Assert.Equal(oldFull.RetrievedText, retainedFull.RetrievedText);
        Assert.Equal(oldOptimized.CoreText, retainedOptimized.CoreText);
        Assert.Equal(oldOptimized.RetrievedText, retainedOptimized.RetrievedText);
        Assert.Equal(2, this.pipeline.WorldLoadCount);
        Assert.Equal(promptLoads, this.pipeline.PromptLoadCount);

        this.pipeline.Preprocessor = null;
        Func<bool, WorldRetrievalQuery, WorldRetrievalResult> freshCapture = service.CaptureWorldContext();
        Assert.Contains("RELOADED FULL INTRO", freshCapture(false, query).CoreText);
        Assert.Contains("RELOADED FULL LIBRARY", freshCapture(false, query).RetrievedText);
        Assert.Contains("RELOADED OPTIMIZED INTRO", freshCapture(true, query).CoreText);
        Assert.Contains("RELOADED OPTIMIZED LIBRARY", freshCapture(true, query).RetrievedText);
        Assert.Contains("RELOADED TRANSLATIONS", freshCapture(false, query).CoreText);
        Assert.Equal(4, this.pipeline.WorldLoadCount);
    }

    [Fact]
    public void CapturesKeepFourSeparateVariantsAndFreezeSveCompatibility()
    {
        this.pipeline.SveDelta = new WorldSummary
        {
            Villagers = new() { Entries = { ["Sophia"] = new() { Id = "Sophia", Name = "Sophia", Description = "SVE resident." } } }
        };
        var service = new DialogueContentService(this.pipeline);
        var query = new WorldRetrievalQuery { PlayerText = "索菲亚" };

        var vanilla = service.CaptureWorldContext();
        this.pipeline.IsSveLoaded = true;
        var sve = service.CaptureWorldContext();
        Assert.Equal(4, this.pipeline.WorldLoadCount);
        Assert.DoesNotContain("SVE resident", vanilla(false, query).RetrievedText);
        Assert.DoesNotContain("SVE resident", vanilla(true, query).RetrievedText);
        Assert.Contains("SVE resident", sve(false, query).RetrievedText);
        Assert.Contains("SVE resident", sve(true, query).RetrievedText);

        this.config.EnableSveCompatibility = false;
        var disabled = service.CaptureWorldContext();
        Assert.DoesNotContain("SVE resident", disabled(false, query).RetrievedText);
        Assert.Contains("SVE resident", sve(false, query).RetrievedText);
        Assert.Equal(4, this.pipeline.WorldLoadCount);
        service.InvalidateWorldSummaries();
        service.CaptureWorldContext();
        Assert.Equal(6, this.pipeline.WorldLoadCount);
        this.config.EnableSveCompatibility = true;
        service.CaptureWorldContext();
        Assert.Equal(8, this.pipeline.WorldLoadCount);
    }

    [Fact]
    public void PromptInvalidationRefreshesLocalizedCoreWithoutChangingOldCapture()
    {
        var service = new DialogueContentService(this.pipeline);
        var query = new WorldRetrievalQuery { PlayerText = "你好" };
        var oldCapture = service.CaptureWorldContext();
        this.pipeline.PromptData["gameSummaryTranslations"] = "NEW TRANSLATIONS";
        service.InvalidatePrompts();

        Assert.Contains("ORIGINAL TRANSLATIONS", oldCapture(false, query).CoreText);
        Assert.Contains("NEW TRANSLATIONS", service.CaptureWorldContext()(false, query).CoreText);
    }
}
