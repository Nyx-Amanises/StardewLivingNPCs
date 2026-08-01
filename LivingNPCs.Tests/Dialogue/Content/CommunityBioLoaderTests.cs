using System;
using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;

namespace LivingNPCs.Tests.Dialogue.Content;

public sealed class CommunityBioLoaderTests
{
    private const string ValidCommunityBioJson = """
        {
          "Biography": "valid",
          "Relationships": {},
          "Traits": {},
          "BiographyEnd": "",
          "Gender": "",
          "Unique": "",
          "ExtraPortraits": {},
          "Preoccupations": [],
          "Dialogue": {},
          "HomeLocationBed": false,
          "UsePatchedDialogue": false,
          "PromptOverrides": {}
        }
        """;

    [Fact]
    public void CandidatePaths_UsesExactLocaleThenParentsThenDefault()
    {
        IReadOnlyList<string> paths = CommunityBioLoader.CandidatePaths(
            "npc_bios",
            "ExampleNpc",
            "zh-Hans-CN");

        Assert.Equal(
            new[]
            {
                "npc_bios/zh-Hans-CN/ExampleNpc.json",
                "npc_bios/zh-Hans/ExampleNpc.json",
                "npc_bios/zh/ExampleNpc.json",
                "npc_bios/ExampleNpc.json",
            },
            paths);
    }

    [Fact]
    public void CandidatePaths_NormalizesUnderscoreLocaleSeparator()
    {
        Assert.Equal(
            new[]
            {
                "npc_bios/pt-BR/ExampleNpc.json",
                "npc_bios/pt/ExampleNpc.json",
                "npc_bios/ExampleNpc.json",
            },
            CommunityBioLoader.CandidatePaths("npc_bios", "ExampleNpc", "pt_BR"));
    }

    [Fact]
    public void CandidatePaths_CanonicalizesLocaleCasing()
    {
        Assert.Equal(
            new[]
            {
                "npc_bios/zh-Hans-CN/ExampleNpc.json",
                "npc_bios/zh-Hans/ExampleNpc.json",
                "npc_bios/zh/ExampleNpc.json",
                "npc_bios/ExampleNpc.json",
            },
            CommunityBioLoader.CandidatePaths("npc_bios", "ExampleNpc", "ZH_hANS_cn"));
    }

    [Fact]
    public void CandidatePaths_DoesNotUseNonAsciiLocaleDirectories()
    {
        Assert.Equal(
            new[] { "npc_bios/ExampleNpc.json" },
            CommunityBioLoader.CandidatePaths("npc_bios", "ExampleNpc", "zh-中文"));
    }

    [Theory]
    [InlineData("zh--CN")]
    [InlineData("-zh")]
    [InlineData("zh-")]
    public void CandidatePaths_DoesNotCollapseEmptyLocaleSegments(string locale)
    {
        Assert.Equal(
            new[] { "npc_bios/ExampleNpc.json" },
            CommunityBioLoader.CandidatePaths("npc_bios", "ExampleNpc", locale));
    }

    [Fact]
    public void ScopedCandidatePaths_UsesSourceNamespaceLayout()
    {
        Assert.Equal(
            new[]
            {
                "npc_bios/Author.CustomNpc/locales/zh-CN/ExampleNpc.json",
                "npc_bios/Author.CustomNpc/locales/zh/ExampleNpc.json",
                "npc_bios/Author.CustomNpc/bios/ExampleNpc.json",
            },
            CommunityBioLoader.ScopedCandidatePaths(
                "npc_bios/Author.CustomNpc",
                "ExampleNpc",
                "zh-CN"));
    }

    [Theory]
    [InlineData("../Npc")]
    [InlineData("folder/Npc")]
    [InlineData("folder\\Npc")]
    [InlineData("Npc:Variant")]
    [InlineData("Npc.")]
    [InlineData("Npc ")]
    [InlineData("CON")]
    [InlineData("con.profile")]
    [InlineData("LPT9")]
    [InlineData("   ")]
    public void CandidatePaths_RejectsUnsafeNpcNames(string npcName)
    {
        Assert.Empty(CommunityBioLoader.CandidatePaths("npc_bios", npcName, "zh-CN"));
    }

    [Fact]
    public void Load_PrefersExactLocale()
    {
        var files = new Dictionary<string, NpcBio>(StringComparer.OrdinalIgnoreCase)
        {
            ["npc_bios/ExampleNpc.json"] = ValidBio("default"),
            ["npc_bios/zh/ExampleNpc.json"] = ValidBio("parent"),
            ["npc_bios/zh-CN/ExampleNpc.json"] = ValidBio("exact"),
        };

        CommunityBioLoader.LoadResult result = CommunityBioLoader.Load(
            "npc_bios",
            "ExampleNpc",
            "zh-CN",
            path => ReadFrom(files, path));

        Assert.NotNull(result.Bio);
        Assert.Equal("exact", result.Bio!.Biography);
        Assert.Equal("npc_bios/zh-CN/ExampleNpc.json", result.RelativePath);
        Assert.Empty(result.InvalidCandidates);
    }

    [Fact]
    public void Load_InvalidLocalizedFileFallsBackToDefault()
    {
        var files = new Dictionary<string, NpcBio>(StringComparer.OrdinalIgnoreCase)
        {
            ["npc_bios/zh/ExampleNpc.json"] = ValidBio("   "),
            ["npc_bios/ExampleNpc.json"] = ValidBio("default"),
        };

        CommunityBioLoader.LoadResult result = CommunityBioLoader.Load(
            "npc_bios",
            "ExampleNpc",
            "zh",
            path => ReadFrom(files, path));

        Assert.NotNull(result.Bio);
        Assert.Equal("default", result.Bio!.Biography);
        CommunityBioLoader.ValidationFailure failure = Assert.Single(result.InvalidCandidates);
        Assert.Equal("npc_bios/zh/ExampleNpc.json", failure.RelativePath);
        Assert.Contains("Biography must not be blank", failure.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsReaderSuppliedNullCollections()
    {
        var bio = new NpcBio
        {
            Biography = "valid",
            Relationships = null!,
            Traits = null!,
            ExtraPortraits = null!,
            Preoccupations = null!,
            Dialogue = null!,
            PromptOverrides = null!,
        };

        CommunityBioLoader.LoadResult result = CommunityBioLoader.Load(
            "npc_bios",
            "ExampleNpc",
            "en",
            path => path == "npc_bios/ExampleNpc.json"
                ? CommunityBioLoader.BioFileReadResult.Valid(bio)
                : CommunityBioLoader.BioFileReadResult.Missing());

        Assert.Null(result.Bio);
        CommunityBioLoader.ValidationFailure failure = Assert.Single(result.InvalidCandidates);
        Assert.Contains("Relationships must be an object", failure.Problem, StringComparison.Ordinal);
        Assert.Contains("Preoccupations must be an array", failure.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadScopedSources_ReturnsUniqueActiveSource()
    {
        var files = new Dictionary<string, NpcBio>(StringComparer.OrdinalIgnoreCase)
        {
            ["npc_bios/Author.One/bios/ExampleNpc.json"] = ValidBio("one"),
        };

        CommunityBioLoader.ScopedLoadResult result = CommunityBioLoader.LoadScopedSources(
            new[] { "npc_bios/Author.One", "npc_bios/Author.Two" },
            "ExampleNpc",
            "en",
            path => ReadFrom(files, path));

        Assert.NotNull(result.Bio);
        Assert.Equal("one", result.Bio!.Biography);
        Assert.Equal("npc_bios/Author.One/bios/ExampleNpc.json", result.RelativePath);
        Assert.Empty(result.ConflictingPaths);
    }

    [Fact]
    public void LoadScopedSources_FailsClosedOnDuplicateActiveSources()
    {
        var files = new Dictionary<string, NpcBio>(StringComparer.OrdinalIgnoreCase)
        {
            ["npc_bios/Author.One/bios/ExampleNpc.json"] = ValidBio("one"),
            ["npc_bios/Author.Two/bios/ExampleNpc.json"] = ValidBio("two"),
        };

        CommunityBioLoader.ScopedLoadResult result = CommunityBioLoader.LoadScopedSources(
            new[] { "npc_bios/Author.Two", "npc_bios/Author.One" },
            "ExampleNpc",
            "en",
            path => ReadFrom(files, path));

        Assert.Null(result.Bio);
        Assert.Equal(
            new[]
            {
                "npc_bios/Author.One/bios/ExampleNpc.json",
                "npc_bios/Author.Two/bios/ExampleNpc.json",
            },
            result.ConflictingPaths);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("{ /* comment */ \"Biography\": \"valid\" }")]
    [InlineData("{ \"Biography\": \"valid\", }")]
    public void ParseJson_RejectsEmptyNonObjectAndNonStandardSyntax(string json)
    {
        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(json);

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Invalid, result.Status);
        Assert.Null(result.Bio);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
    }

    [Fact]
    public void ParseJson_RejectsDuplicateProperties()
    {
        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(
            "{ \"Biography\": \"first\", \"Biography\": \"last\" }");

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Invalid, result.Status);
        Assert.Contains("duplicate JSON property 'Biography'", result.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"Biography\": \"valid\", \"$type\": \"Example.Type\" }", "reserved JSON metadata")]
    [InlineData("{ \"Biography\": \"valid\", \"$id\": \"1\" }", "reserved JSON metadata")]
    [InlineData("{ \"biography\": \"valid\" }", "incorrect casing")]
    [InlineData("{ \"Biography\": \"valid\", \"Unknown\": true }", "unknown field 'Unknown'")]
    [InlineData("{ \"Biography\": 123 }", "Biography must be a string")]
    [InlineData("{ \"Biography\": null }", "Biography must be a string")]
    [InlineData("{ \"Biography\": \"valid\", \"HomeLocationBed\": \"false\" }", "HomeLocationBed must be a boolean")]
    [InlineData("{ \"Biography\": \"valid\", \"Relationships\": [] }", "Relationships must be an object")]
    [InlineData("{ \"Biography\": \"valid\", \"Relationships\": null }", "Relationships must be an object")]
    [InlineData("{ \"Biography\": \"valid\", \"Preoccupations\": [1] }", "Preoccupations[0] must be a string")]
    [InlineData("{ \"Biography\": \"valid\", \"Preoccupations\": null }", "Preoccupations must be an array")]
    [InlineData("{ \"Biography\": \"valid\", \"Relationships\": { \"Friend\": { \"id\": \"Friend\", \"heading\": \"Friend\", \"Description\": \"desc\" } } }", "incorrect casing")]
    public void ParseJson_RejectsMetadataWrongCasingUnknownFieldsAndWrongTypes(
        string json,
        string expectedProblem)
    {
        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(json);

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Invalid, result.Status);
        Assert.Contains(expectedProblem, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_UsesExactTypesWithoutStringOrBooleanCoercion()
    {
        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(
            "{ \"Biography\": true, \"UsePatchedDialogue\": 0 }");

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Invalid, result.Status);
        Assert.Contains("Biography must be a string", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseJson_AcceptsValidDocument()
    {
        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(ValidCommunityBioJson);

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Valid, result.Status);
        Assert.Equal("valid", result.Bio!.Biography);
        Assert.Null(result.Problem);
    }

    [Fact]
    public void ParseJson_AllowsNonMetadataDollarPrefixedDataKeys()
    {
        string json = ValidCommunityBioJson.Replace(
            "\"Dialogue\": {}",
            "\"Dialogue\": { \"$custom\": \"sample\" }",
            StringComparison.Ordinal);

        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(json);

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Valid, result.Status);
        Assert.Equal("sample", result.Bio!.Dialogue["$custom"]);
    }

    [Theory]
    [InlineData("{ \"Biography\": \"valid\" }", "missing top-level fields")]
    [InlineData("{ \"Biography\": \"valid\", \"Relationships\": { \"Friend\": { \"id\": \"Friend\", \"Heading\": \"Friend\" } } }", "Relationships['Friend'] is missing: Description")]
    public void ParseJson_RejectsMissingRequiredFields(string json, string expectedProblem)
    {
        CommunityBioLoader.BioFileReadResult result = CommunityBioLoader.ParseJson(json);

        Assert.Equal(CommunityBioLoader.BioFileReadStatus.Invalid, result.Status);
        Assert.Contains(expectedProblem, result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_InvalidJsonCandidateIsRecordedAndFallsBack()
    {
        CommunityBioLoader.LoadResult result = CommunityBioLoader.Load(
            "npc_bios",
            "ExampleNpc",
            "zh-CN",
            path => path switch
            {
                "npc_bios/zh-CN/ExampleNpc.json" => CommunityBioLoader.BioFileReadResult.Invalid("duplicate JSON property 'Biography'"),
                "npc_bios/ExampleNpc.json" => CommunityBioLoader.BioFileReadResult.Valid(ValidBio("default")),
                _ => CommunityBioLoader.BioFileReadResult.Missing(),
            });

        Assert.Equal("default", result.Bio!.Biography);
        CommunityBioLoader.ValidationFailure failure = Assert.Single(result.InvalidCandidates);
        Assert.Equal("npc_bios/zh-CN/ExampleNpc.json", failure.RelativePath);
        Assert.Contains("duplicate JSON property", failure.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingCandidatesAreNotReportedAsInvalid()
    {
        CommunityBioLoader.LoadResult result = CommunityBioLoader.Load(
            "npc_bios",
            "ExampleNpc",
            "en",
            _ => CommunityBioLoader.BioFileReadResult.Missing());

        Assert.Null(result.Bio);
        Assert.Empty(result.InvalidCandidates);
    }

    [Fact]
    public void LoadScopedSources_InvalidLocalizedCandidateFallsBackWithinSameSource()
    {
        CommunityBioLoader.ScopedLoadResult result = CommunityBioLoader.LoadScopedSources(
            new[] { "npc_bios/Author.One" },
            "ExampleNpc",
            "zh-CN",
            path => path switch
            {
                "npc_bios/Author.One/locales/zh-CN/ExampleNpc.json" => CommunityBioLoader.BioFileReadResult.Invalid("trailing comma"),
                "npc_bios/Author.One/bios/ExampleNpc.json" => CommunityBioLoader.BioFileReadResult.Valid(ValidBio("default")),
                _ => CommunityBioLoader.BioFileReadResult.Missing(),
            });

        Assert.Equal("default", result.Bio!.Biography);
        Assert.Empty(result.ConflictingPaths);
        Assert.Single(result.InvalidCandidates);
    }

    [Fact]
    public void LoadScopedSources_InvalidSourceDoesNotConflictWithUniqueValidSource()
    {
        CommunityBioLoader.ScopedLoadResult result = CommunityBioLoader.LoadScopedSources(
            new[] { "npc_bios/Author.One", "npc_bios/Author.Two" },
            "ExampleNpc",
            "en",
            path => path switch
            {
                "npc_bios/Author.One/bios/ExampleNpc.json" => CommunityBioLoader.BioFileReadResult.Invalid("wrong JSON type"),
                "npc_bios/Author.Two/bios/ExampleNpc.json" => CommunityBioLoader.BioFileReadResult.Valid(ValidBio("two")),
                _ => CommunityBioLoader.BioFileReadResult.Missing(),
            });

        Assert.Equal("two", result.Bio!.Biography);
        Assert.Equal("npc_bios/Author.Two/bios/ExampleNpc.json", result.RelativePath);
        Assert.Empty(result.ConflictingPaths);
        Assert.Single(result.InvalidCandidates);
    }

    [Fact]
    public void Validate_RejectsMalformedEntriesAndPortraitMarkers()
    {
        NpcBio bio = ValidBio("valid");
        bio.Relationships["Friend"] = new BioListEntry
        {
            Id = string.Empty,
            Heading = "Friend",
            Description = string.Empty,
        };
        bio.ExtraPortraits["h"] = "cannot redefine a base portrait";
        bio.Preoccupations.Add(" ");
        bio.Dialogue[""] = "sample";
        bio.PromptOverrides["systemPrompt"] = "";
        bio.UsePatchedDialogue = true;

        IReadOnlyList<string> problems = CommunityBioLoader.Validate(bio);

        Assert.Contains(problems, problem => problem.Contains(".id must not be blank", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains(".Description must not be blank", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("must be 'u' or a decimal frame", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Preoccupations[0] must not be blank", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Dialogue key must not be blank", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("PromptOverrides['systemPrompt'] must not be blank", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("UsePatchedDialogue must be false", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("PromptOverrides must be empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CountsUnicodeScalarValuesLikePython()
    {
        NpcBio bio = ValidBio("valid");
        bio.Unique = string.Concat(Enumerable.Repeat("😀", 512));

        Assert.DoesNotContain(
            CommunityBioLoader.Validate(bio),
            problem => problem.Contains("Unique is longer", StringComparison.Ordinal));

        bio.Unique += "😀";
        Assert.Contains(
            CommunityBioLoader.Validate(bio),
            problem => problem.Contains("Unique is longer than 512 characters", StringComparison.Ordinal));
    }

    private static NpcBio ValidBio(string biography)
    {
        return new NpcBio
        {
            Biography = biography,
            Relationships = new Dictionary<string, BioListEntry>(),
            Traits = new Dictionary<string, BioListEntry>(),
            BiographyEnd = string.Empty,
            Gender = string.Empty,
            Unique = string.Empty,
            ExtraPortraits = new Dictionary<string, string>(),
            Preoccupations = new List<string>(),
            Dialogue = new Dictionary<string, string>(),
            PromptOverrides = new Dictionary<string, string>(),
        };
    }

    private static CommunityBioLoader.BioFileReadResult ReadFrom(
        IReadOnlyDictionary<string, NpcBio> files,
        string path)
    {
        return files.TryGetValue(path, out NpcBio? bio)
            ? CommunityBioLoader.BioFileReadResult.Valid(bio)
            : CommunityBioLoader.BioFileReadResult.Missing();
    }
}
