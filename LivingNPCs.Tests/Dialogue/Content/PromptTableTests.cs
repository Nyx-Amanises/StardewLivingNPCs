using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.Content;
using LivingNPCs.Tests.Dialogue.Llm;
using StardewModdingAPI;

namespace LivingNPCs.Tests.Dialogue.Content;

[Collection("LlmLayer")]
public sealed class PromptTableTests : IDisposable
{
    private readonly FakeMonitor monitor = new();
    private readonly FakeContentPipeline pipeline = new();

    public PromptTableTests()
    {
        DialogueServices.Initialize(null!, this.monitor, new());
        DialogueEngineGate.RuntimeDisabled = false;
    }

    public void Dispose()
    {
        DialogueEngineGate.RuntimeDisabled = false;
        DialogueServices.Initialize(null!, null!, new());
    }

    [Fact]
    public void Lookup_BaseKey_AppliesTokens()
    {
        this.pipeline.PromptData["greeting"] = "Hello {{Name}}!";
        var table = new PromptTable(this.pipeline);

        Assert.Equal("Hello Abigail!", table.Lookup("greeting", tokens: new { Name = "Abigail" }));
    }

    [Fact]
    public void Lookup_GenderVariant_PreferredOverBase()
    {
        this.pipeline.PromptData["mood"] = "base";
        this.pipeline.PromptData["mood.MaleNpc"] = "male";
        this.pipeline.PromptData["mood.FemaleNpc"] = "female";
        var table = new PromptTable(this.pipeline);

        Assert.Equal("male", table.Lookup("mood", PromptGender.Male));
        Assert.Equal("female", table.Lookup("mood", PromptGender.Female));
        Assert.Equal("base", table.Lookup("mood"));
        // 没有变体键时按性别查找也落回基础键。
        this.pipeline.PromptData["only"] = "base-only";
        table.Invalidate();
        Assert.Equal("base-only", table.Lookup("only", PromptGender.Female));
    }

    [Fact]
    public void Lookup_PromptOverrides_WinOverEverything_BlankIgnored()
    {
        this.pipeline.PromptData["mood"] = "base";
        this.pipeline.PromptData["mood.MaleNpc"] = "male";
        var table = new PromptTable(this.pipeline);
        var overrides = new Dictionary<string, string> { ["mood"] = "custom {{Name}}", ["blank"] = "  " };

        Assert.Equal("custom X", table.Lookup("mood", PromptGender.Male, overrides, new { Name = "X" }));
        // 空白覆盖被忽略，继续走正常链。
        Assert.Null(table.Lookup("blank", PromptGender.None, overrides, null, returnNull: true));
    }

    [Fact]
    public void Lookup_MissingKey_WarnsOncePerKey()
    {
        var table = new PromptTable(this.pipeline);

        Assert.Null(table.Lookup("nope"));
        Assert.Null(table.Lookup("nope"));
        Assert.Null(table.Lookup("alsoNope"));

        var warnings = this.monitor.MessagesAt(LogLevel.Warn).Where(m => m.Contains("Prompt skeleton key")).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, m => m.Contains("'nope'"));
    }

    [Fact]
    public void Lookup_MissingKeyWithReturnNull_DoesNotWarn()
    {
        var table = new PromptTable(this.pipeline);

        Assert.Null(table.Lookup("nope", returnNull: true));
        Assert.Empty(this.monitor.MessagesAt(LogLevel.Warn));
    }

    [Fact]
    public void BuildCache_FiltersNoTranslationAndBlankValues_AppliesPreprocess()
    {
        this.pipeline.PromptData["ok"] = "value";
        this.pipeline.PromptData["missing"] = "(no translation:missing)";
        this.pipeline.PromptData["blankAfter"] = "@@";
        this.pipeline.PromptData["gendered"] = "raw";
        this.pipeline.Preprocessor = text => text switch
        {
            "@@" => "  ",
            "raw" => "processed",
            _ => text
        };
        var table = new PromptTable(this.pipeline);

        Assert.Equal("value", table.Lookup("ok"));
        Assert.Equal("processed", table.Lookup("gendered"));
        Assert.Null(table.Lookup("missing", returnNull: true));
        Assert.Null(table.Lookup("blankAfter", returnNull: true));
    }

    [Fact]
    public void BuildCache_LoadFailure_TwoErrorsAndRuntimeDisable()
    {
        this.pipeline.PromptLoadError = new InvalidOperationException("boom");
        var table = new PromptTable(this.pipeline);

        Assert.Null(table.Lookup("anything", returnNull: true));
        Assert.True(DialogueEngineGate.RuntimeDisabled);
        Assert.Equal(2, this.monitor.MessagesAt(LogLevel.Error).Count);
        // 空缓存已被缓存住，不会每次查找都重抛。
        Assert.Null(table.Lookup("anything", returnNull: true));
        Assert.Equal(1, this.pipeline.PromptLoadCount);
    }

    [Fact]
    public void Cache_RebuildsOnLocaleOrGenderChange()
    {
        this.pipeline.PromptData["k"] = "v";
        var table = new PromptTable(this.pipeline);

        table.Lookup("k");
        table.Lookup("k");
        Assert.Equal(1, this.pipeline.PromptLoadCount);

        this.pipeline.CurrentLocale = "zh-CN";
        table.Lookup("k");
        Assert.Equal(2, this.pipeline.PromptLoadCount);

        this.pipeline.PlayerGenderKey = "Female";
        table.Lookup("k");
        Assert.Equal(3, this.pipeline.PromptLoadCount);
    }

    [Fact]
    public void Cache_RebuildsAfterInvalidate()
    {
        this.pipeline.PromptData["k"] = "v";
        var table = new PromptTable(this.pipeline);

        table.Lookup("k");
        table.Invalidate();
        table.Lookup("k");
        Assert.Equal(2, this.pipeline.PromptLoadCount);
    }

    [Fact]
    public void ReplaceTokens_CaseInsensitive_UnknownTokenPreserved()
    {
        Assert.Equal("Hi Abigail, {{Unknown}}!", PromptTable.ReplaceTokens("Hi {{name}}, {{Unknown}}!", new { Name = "Abigail" }));
        Assert.Equal("no tokens", PromptTable.ReplaceTokens("no tokens", new { Name = "x" }));
        Assert.Equal("keep {{a}}", PromptTable.ReplaceTokens("keep {{a}}", null));
    }
}
