using System;
using System.Collections.Generic;
using System.Linq;
using LivingNPCs.Dialogue.Content;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.Content;

public sealed class PortraitFrameSemanticsTests
{
    private const string FriendlyHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OtherHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void OnlyEnabledReviewedHashesResolve()
    {
        string json = $$"""
        {
          "Profiles": {
            "Friendly": { "enabled": true, "en": "friendly", "zh": "友好", "hashes": ["{{FriendlyHash}}"] },
            "Disabled": { "enabled": false, "en": "disabled", "zh": "禁用", "hashes": ["{{OtherHash}}"] }
          }
        }
        """;

        PortraitFrameSemantics semantics = PortraitFrameSemantics.FromJson(json);

        Assert.NotNull(semantics.ResolveHash(FriendlyHash, "en"));
        Assert.Equal("友好", semantics.ResolveHash(FriendlyHash, "zh-CN")?.Description);
        Assert.Null(semantics.ResolveHash(OtherHash, "en"));
    }

    [Fact]
    public void ConflictingDescriptionsDisableHash()
    {
        string json = $$"""
        {
          "Profiles": {
            "A": { "enabled": true, "en": "one", "zh": "一", "hashes": ["{{FriendlyHash}}"] },
            "B": { "enabled": true, "en": "two", "zh": "二", "hashes": ["{{FriendlyHash}}"] }
          }
        }
        """;

        PortraitFrameSemantics semantics = PortraitFrameSemantics.FromJson(json);

        Assert.Null(semantics.ResolveHash(FriendlyHash, "en"));
    }

    [Fact]
    public void FrameReadRejectsMissingAndSolidTiles()
    {
        var pixels = new byte[64 * 64 * 4];
        Assert.False(PortraitFrameSemantics.HasUsableRgba(pixels));

        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 0;
            pixels[i + 2] = 255;
            pixels[i + 3] = 255;
        }
        Assert.False(PortraitFrameSemantics.HasUsableRgba(pixels));

        pixels[0] = 0;
        Assert.True(PortraitFrameSemantics.HasUsableRgba(pixels));
    }

    [Fact]
    public void HashUsesCanonicalRgbaByteOrder()
    {
        byte[] pixels = { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Equal(
            "66840DDA154E8A113C31DD0AD32F7F3A366A80E8136979D8F5A101D3D29D6F72",
            PortraitFrameSemantics.ComputeRgbaHash(pixels));
    }

    [Fact]
    public void HashIgnoresHiddenRgbInTransparentPixels()
    {
        byte[] hiddenRgb = { 255, 64, 32, 0, 10, 20, 30, 255 };
        byte[] canonical = { 0, 0, 0, 0, 10, 20, 30, 255 };

        Assert.Equal(
            PortraitFrameSemantics.ComputeRgbaHash(canonical),
            PortraitFrameSemantics.ComputeRgbaHash(hiddenRgb));
    }

    [Fact]
    public void V2CatalogKeysMatchesByFrameIndexAndHash()
    {
        string sharedHash = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        string json = $$"""
        {
          "Version": 2,
          "FrameEntries": [
            {
              "index": 0,
              "marker": "0",
              "hash": "{{sharedHash}}",
              "enabled": true,
              "decision": "allow",
              "en": "neutral",
              "zh": "中性"
            },
            {
              "index": 7,
              "marker": "7",
              "hash": "{{sharedHash}}",
              "enabled": true,
              "decision": "allow",
              "en": "custom numeric",
              "zh": "自定义数字帧"
            },
            {
              "index": 3,
              "marker": "u",
              "hash": "{{OtherHash}}",
              "enabled": false,
              "decision": "unknown",
              "en": "unknown",
              "zh": "未知",
              "reason": "not reviewed"
            }
          ]
        }
        """.Replace("{{OtherHash}}", OtherHash, StringComparison.Ordinal);

        PortraitFrameSemantics semantics = PortraitFrameSemantics.FromJson(json);

        Assert.Equal("0", semantics.ResolveHash(0, sharedHash, "en")?.Marker);
        Assert.Equal("neutral", semantics.ResolveHash(0, sharedHash, "en")?.Description);
        Assert.Equal("7", semantics.ResolveHash(7, sharedHash, "en")?.Marker);
        Assert.Equal("自定义数字帧", semantics.ResolveHash(7, sharedHash, "zh-CN")?.Description);
        Assert.Null(semantics.ResolveHash(3, OtherHash, "en"));
    }

    [Theory]
    [InlineData(3, 64)]
    [InlineData(2, 32)]
    public void IncompatibleCatalogVersionOrTileSizeFailsClosed(int version, int tileSize)
    {
        string json = $$"""
        {
          "Version": {{version}},
          "TileSize": {{tileSize}},
          "FrameEntries": [
            { "index": 0, "marker": "0", "hash": "{{FriendlyHash}}", "enabled": true, "decision": "allow", "en": "neutral", "zh": "中性" }
          ]
        }
        """;

        PortraitFrameSemantics semantics = PortraitFrameSemantics.FromJson(json);

        Assert.Null(semantics.ResolveHash(0, FriendlyHash, "en"));
    }

    [Fact]
    public void DenyAndConflictAreScopedToTheSameIndexAndHash()
    {
        string json = $$"""
        {
          "Version": 2,
          "FrameEntries": [
            { "index": 0, "marker": "0", "hash": "{{FriendlyHash}}", "enabled": true, "decision": "allow", "en": "one", "zh": "一" },
            { "index": 0, "marker": "0", "hash": "{{FriendlyHash}}", "enabled": false, "decision": "deny", "en": "blocked", "zh": "禁用", "reason": "explicit" },
            { "index": 7, "marker": "7", "hash": "{{FriendlyHash}}", "enabled": true, "decision": "allow", "en": "numeric", "zh": "数字" }
          ]
        }
        """;

        PortraitFrameSemantics semantics = PortraitFrameSemantics.FromJson(json);

        Assert.Null(semantics.ResolveHash(0, FriendlyHash, "en"));
        Assert.Equal("7", semantics.ResolveHash(7, FriendlyHash, "en")?.Marker);
    }

    [Fact]
    public void ResolveHashesReturnsReviewedFramesInIndexOrder()
    {
        string hash0 = "0000000000000000000000000000000000000000000000000000000000000001";
        string hash7 = "0000000000000000000000000000000000000000000000000000000000000007";
        string json = $$"""
        {
          "Version": 2,
          "FrameEntries": [
            { "index": 7, "marker": "7", "hash": "{{hash7}}", "enabled": true, "decision": "allow", "en": "seven", "zh": "七" },
            { "index": 0, "marker": "0", "hash": "{{hash0}}", "enabled": true, "decision": "allow", "en": "zero", "zh": "零" }
          ]
        }
        """;
        PortraitFrameSemantics semantics = PortraitFrameSemantics.FromJson(json);

        IReadOnlyList<PortraitFrameSemantics.Match> matches = semantics.ResolveHashes(
            new Dictionary<int, string> { [7] = hash7, [0] = hash0 },
            "en");

        Assert.Equal(new[] { "0", "7" }, matches.Select(match => match.Marker));
        Assert.Equal(new[] { 0, 7 }, matches.Select(match => match.FrameIndex));
    }

    [Fact]
    public void RgbaFrameReaderUsesRowMajorTilesAndRejectsOutOfBoundsFrames()
    {
        const int width = 128;
        const int height = 64;
        byte[] rgba = new byte[width * height * 4];
        SetPixel(rgba, width, 0, 0, 255, 0, 0, 255);
        SetPixel(rgba, width, 1, 0, 0, 255, 0, 255);
        SetPixel(rgba, width, 64, 0, 0, 0, 255, 255);
        SetPixel(rgba, width, 65, 0, 255, 255, 0, 255);

        Assert.True(PortraitFrameSemantics.TryReadRgbaFrame(rgba, width, height, 0, out byte[] first));
        Assert.True(PortraitFrameSemantics.TryReadRgbaFrame(rgba, width, height, 1, out byte[] second));
        Assert.Equal((byte)255, first[0]);
        Assert.Equal((byte)255, first[7]);
        Assert.Equal((byte)255, second[2]);
        Assert.Equal((byte)255, second[5]);
        Assert.False(PortraitFrameSemantics.TryReadRgbaFrame(rgba, width, height, 2, out _));
        Assert.False(PortraitFrameSemantics.TryReadRgbaFrame(rgba, width, height, -1, out _));
    }

    [Fact]
    public void PhysicalFrameCountRejectsPartialTilesAndSupportsMoreThan256Frames()
    {
        Assert.Equal(0, PortraitFrameSemantics.GetPhysicalFrameCount(127, 192));
        Assert.Equal(0, PortraitFrameSemantics.GetPhysicalFrameCount(128, 191));
        Assert.Equal(300, PortraitFrameSemantics.GetPhysicalFrameCount(64, 64 * 300));
        Assert.Equal(
            PortraitMarkerRules.MaxSupportedFrameIndex + 1,
            PortraitFrameSemantics.GetPhysicalFrameCount(64, 64 * 5000));
    }

    [Fact]
    public void ExplicitOverridesRequireASuccessfulWholeTextureRead()
    {
        Assert.Equal(8, PortraitFrameSemantics.GetTrustedOverrideFrameCount(8, wholeTextureReadSucceeded: true));
        Assert.Equal(0, PortraitFrameSemantics.GetTrustedOverrideFrameCount(8, wholeTextureReadSucceeded: false));
        Assert.Equal(0, PortraitFrameSemantics.GetTrustedOverrideFrameCount(-1, wholeTextureReadSucceeded: true));
    }

    [Fact]
    public void SignatureObservesEveryPhysicalFrameWhenWholeTextureReadSucceeds()
    {
        Assert.Equal(
            Enumerable.Range(0, 300),
            PortraitFrameSemantics.GetObservedFrameIndexes(
                300,
                wholeTextureReadSucceeded: true,
                new[] { 0, 3, 19 }));

        Assert.Equal(
            new[] { 0, 3, 19 },
            PortraitFrameSemantics.GetObservedFrameIndexes(
                300,
                wholeTextureReadSucceeded: false,
                new[] { 19, -1, 3, 0, 300, 3 }));
    }

    [Fact]
    public void RgbaFrameReaderRejectsNonTileAlignedSheets()
    {
        const int width = 127;
        const int height = 64;
        byte[] rgba = new byte[width * height * 4];
        SetPixel(rgba, width, 0, 0, 255, 0, 0, 255);
        SetPixel(rgba, width, 1, 0, 0, 255, 0, 255);

        Assert.False(PortraitFrameSemantics.TryReadRgbaFrame(rgba, width, height, 0, out _));
    }

    [Fact]
    public void MarkerRulesKeepStandardAliasesDistinctFromCustomNumericFrames()
    {
        Assert.Equal("h", PortraitMarkerRules.NormalizeGameMarker("H"));
        Assert.Equal("a", PortraitMarkerRules.NormalizeGameMarker("a"));
        Assert.Equal("7", PortraitMarkerRules.NormalizeGameMarker("7"));
        Assert.Equal(7, GetFrameIndex("7"));
        Assert.False(PortraitMarkerRules.TryGetFrameIndex("07", out _));
        Assert.True(PortraitMarkerRules.TryGetFrameIndex("1", out int standardNumericIndex));
        Assert.Equal(1, standardNumericIndex);
        Assert.Null(PortraitMarkerRules.NormalizeExtraMarker("1"));
        Assert.Equal("7", PortraitMarkerRules.NormalizeExtraMarker("7"));
        Assert.Null(PortraitMarkerRules.NormalizeExtraMarker("h"));
    }

    private static int GetFrameIndex(string marker)
    {
        Assert.True(PortraitMarkerRules.TryGetFrameIndex(marker, out int index));
        return index;
    }

    private static void SetPixel(
        byte[] rgba,
        int width,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = (y * width + x) * 4;
        rgba[offset] = red;
        rgba[offset + 1] = green;
        rgba[offset + 2] = blue;
        rgba[offset + 3] = alpha;
    }
}
