using System.Collections.Generic;
using LivingNPCs.Dialogue.Content;
using Newtonsoft.Json;

namespace LivingNPCs.Tests.Dialogue.Content;

/// <summary>
/// Pins the defense against third-party biography JSON with explicit null fields: Json.NET
/// overwrites the property initializers with null (nullable reference types cannot prevent
/// that), which used to crash FinalizeBio / sample loading and take down that NPC's AI
/// dialogue entirely. NormalizeNullFields restores empty instances along the load chain.
/// </summary>
public sealed class NullFieldBioTests
{
    private const string NullFieldJson = """
    {
        "Biography": "A third-party biography.",
        "Relationships": null,
        "Traits": null,
        "BiographyEnd": null,
        "Gender": null,
        "Unique": null,
        "ExtraPortraits": null,
        "Preoccupations": null,
        "Dialogue": null,
        "PromptOverrides": null
    }
    """;

    private static NpcBio DeserializeNullFieldBio()
    {
        NpcBio? bio = JsonConvert.DeserializeObject<NpcBio>(NullFieldJson);
        Assert.NotNull(bio);
        return bio!;
    }

    [Fact]
    public void JsonNet_OverwritesInitializersWithExplicitNulls()
    {
        // The bug premise: deserialization really does null these out despite the initializers.
        NpcBio bio = DeserializeNullFieldBio();

        Assert.Null(bio.Relationships);
        Assert.Null(bio.Traits);
        Assert.Null(bio.ExtraPortraits);
        Assert.Null(bio.Preoccupations);
        Assert.Null(bio.Dialogue);
        Assert.Null(bio.PromptOverrides);
    }

    [Fact]
    public void NormalizeNullFields_RestoresEmptyInstancesAndIsIdempotent()
    {
        NpcBio bio = DeserializeNullFieldBio();

        bio.NormalizeNullFields();
        bio.NormalizeNullFields();

        Assert.Equal("A third-party biography.", bio.Biography);
        Assert.NotNull(bio.Relationships);
        Assert.NotNull(bio.Traits);
        Assert.Equal(string.Empty, bio.BiographyEnd);
        Assert.Equal(string.Empty, bio.Gender);
        Assert.Equal(string.Empty, bio.Unique);
        Assert.NotNull(bio.ExtraPortraits);
        Assert.NotNull(bio.Preoccupations);
        Assert.NotNull(bio.Dialogue);
        Assert.NotNull(bio.PromptOverrides);
    }

    [Fact]
    public void NormalizeNullFields_PreservesExistingValues()
    {
        var bio = new NpcBio
        {
            Biography = "keep",
            Preoccupations = new List<string> { "topic" },
            Dialogue = new Dictionary<string, string> { ["Mon"] = "line" }
        };

        bio.NormalizeNullFields();

        Assert.Equal("keep", bio.Biography);
        Assert.Equal(new[] { "topic" }, bio.Preoccupations);
        Assert.Equal("line", bio.Dialogue["Mon"]);
    }

    [Fact]
    public void GetBio_WithNullFieldBioAsset_DoesNotThrowAndDerivesDefaults()
    {
        var pipeline = new FakeContentPipeline();
        pipeline.Bios["NullNpc"] = DeserializeNullFieldBio();
        var service = new DialogueContentService(pipeline);

        NpcBio bio = service.GetBio("NullNpc");

        Assert.Equal("A third-party biography.", bio.Biography);
        Assert.NotNull(bio.TopicPool);
        Assert.Empty(bio.TopicPool);
        Assert.Contains("0", bio.ValidPortraits);
        Assert.NotNull(bio.Dialogue);
        Assert.NotNull(bio.Relationships);
    }

    [Fact]
    public void SampleLoaders_TolerateNullDialogueDictionary()
    {
        NpcBio bio = DeserializeNullFieldBio();

        var samples = DialogueSampleLoader.GetSamples(npc: null, bio, expansionCompatible: true);
        var bioOnly = DialogueSampleLoader.GetBioOnlySamples(bio);

        Assert.Empty(samples);
        Assert.Empty(bioOnly);
    }
}
