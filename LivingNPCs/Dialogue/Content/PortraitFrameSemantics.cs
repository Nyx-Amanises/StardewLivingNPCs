using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace LivingNPCs.Dialogue.Content;

/// <summary>
/// Resolves reviewed portrait semantics from the final texture Stardew will draw. Matching uses
/// both the row-major frame index and a canonical RGBA hash, since either value alone is ambiguous
/// across vanilla, seasonal portraits, SVE, and portrait replacers.
/// </summary>
internal sealed class PortraitFrameSemantics
{
    private const int LegacyFrameIndex = 3;
    private const int TileSize = 64;
    private const int MaxPhysicalFrames = PortraitMarkerRules.MaxSupportedFrameIndex + 1;
    private const int MaxWholeTexturePixels = 1024 * 1024;

    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, Entry>> byIndex;
    private readonly ConditionalWeakTable<Texture2D, CachedTexture> runtimeTextures = new();
    private readonly object cacheGate = new();
    private int cacheEpoch;

    private PortraitFrameSemantics(IReadOnlyDictionary<int, IReadOnlyDictionary<string, Entry>> byIndex)
    {
        this.byIndex = byIndex;
    }

    internal sealed record Match(string Marker, string Description, string Hash, int FrameIndex);

    private sealed record Entry(string English, string Chinese);

    private sealed record CachedTexture(
        int Width,
        int Height,
        int Epoch,
        int FrameCount,
        IReadOnlyDictionary<int, string> UsableHashes,
        string Signature);

    public static PortraitFrameSemantics Empty { get; } = new(
        new Dictionary<int, IReadOnlyDictionary<string, Entry>>());

    public static PortraitFrameSemantics FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        Catalog? catalog = JsonConvert.DeserializeObject<Catalog>(json);
        if (catalog == null)
        {
            return Empty;
        }

        if (catalog.Version is int version && version is not (1 or 2)
            || catalog.TileSize is int tileSize && tileSize != TileSize)
        {
            return Empty;
        }

        var entries = new Dictionary<int, Dictionary<string, Entry>>();
        var blocked = new HashSet<(int Index, string Hash)>();
        bool hasVersionTwoEntries = catalog.FrameEntries is { Count: > 0 };
        if (hasVersionTwoEntries)
        {
            foreach (FrameRecord frame in catalog.FrameEntries!)
            {
                AddFrame(entries, blocked, frame);
            }
        }
        else if (catalog.Frames is { Count: > 0 })
        {
            foreach ((string hash, LegacyFrame frame) in catalog.Frames)
            {
                AddLegacyFrame(entries, blocked, LegacyFrameIndex, hash, frame);
            }
        }
        else if (catalog.Profiles != null)
        {
            foreach (Profile profile in catalog.Profiles.Values)
            {
                if (!profile.Enabled || profile.Hashes == null)
                {
                    continue;
                }

                foreach (string hash in profile.Hashes)
                {
                    AddLegacyFrame(
                        entries,
                        blocked,
                        LegacyFrameIndex,
                        hash,
                        new LegacyFrame
                        {
                            Enabled = true,
                            English = profile.English,
                            Chinese = profile.Chinese
                        });
                }
            }
        }

        if (entries.Count == 0)
        {
            return Empty;
        }

        return new PortraitFrameSemantics(entries.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, Entry>)pair.Value,
            EqualityComparer<int>.Default));
    }

    public IReadOnlyList<Match> ResolveAll(Texture2D? portrait, string? locale, out string signature)
    {
        return this.ResolveAll(portrait, locale, out signature, out _);
    }

    public IReadOnlyList<Match> ResolveAll(
        Texture2D? portrait,
        string? locale,
        out string signature,
        out int frameCount)
    {
        signature = "missing";
        frameCount = 0;
        if (!TryGetTexture(portrait, out Texture2D? usablePortrait))
        {
            return Array.Empty<Match>();
        }

        CachedTexture cached = this.GetCachedTexture(usablePortrait);
        signature = cached.Signature;
        frameCount = cached.FrameCount;
        return this.ResolveHashes(cached.UsableHashes, locale);
    }

    /// <summary>
    /// Resolve from a new GPU read even when the Texture2D reference and content-cache epoch did
    /// not change. Presentation uses this to close the generation-to-display validation window.
    /// </summary>
    public IReadOnlyList<Match> ResolveAllFresh(
        Texture2D? portrait,
        string? locale,
        out string signature,
        out int frameCount)
    {
        signature = "missing";
        frameCount = 0;
        if (!TryGetTexture(portrait, out Texture2D? usablePortrait))
        {
            return Array.Empty<Match>();
        }

        CachedTexture cached = this.RefreshCachedTexture(usablePortrait);
        signature = cached.Signature;
        frameCount = cached.FrameCount;
        return this.ResolveHashes(cached.UsableHashes, locale);
    }

    internal IReadOnlyList<Match> ResolveHashes(IReadOnlyDictionary<int, string> hashes, string? locale)
    {
        var matches = new List<Match>();
        foreach ((int frameIndex, string hash) in hashes.OrderBy(pair => pair.Key))
        {
            if (this.ResolveHash(frameIndex, hash, locale) is { } match)
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    /// <summary>Compatibility wrapper for the old frame-3 catalog tests and callers.</summary>
    internal Match? ResolveHash(string? hash, string? locale)
    {
        return this.ResolveHash(LegacyFrameIndex, hash, locale);
    }

    internal Match? ResolveHash(int frameIndex, string? hash, string? locale)
    {
        if (NormalizeHash(hash) is not { } normalized
            || !this.byIndex.TryGetValue(frameIndex, out IReadOnlyDictionary<string, Entry>? byHash)
            || !byHash.TryGetValue(normalized, out Entry? entry)
            || PortraitMarkerRules.MarkerForIndex(frameIndex) is not { } marker)
        {
            return null;
        }

        string description = Localize(entry, locale);
        return string.IsNullOrWhiteSpace(description)
            ? null
            : new Match(marker, description.Trim(), normalized, frameIndex);
    }

    /// <summary>Invalidate cached texture reads after Content Patcher/game-content invalidation.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref this.cacheEpoch);
    }

    internal static bool TryReadRgbaFrame(
        IReadOnlyList<byte> rgba,
        int width,
        int height,
        int frameIndex,
        out byte[] frame)
    {
        frame = Array.Empty<byte>();
        if (frameIndex < 0
            || frameIndex >= GetPhysicalFrameCount(width, height)
            || (long)width * height * 4 > rgba.Count)
        {
            return false;
        }

        int columns = width / TileSize;
        int x = frameIndex % columns * TileSize;
        int y = frameIndex / columns * TileSize;
        if (x + TileSize > width || y + TileSize > height)
        {
            return false;
        }

        frame = new byte[TileSize * TileSize * 4];
        for (int row = 0; row < TileSize; row++)
        {
            int sourceOffset = ((y + row) * width + x) * 4;
            int destinationOffset = row * TileSize * 4;
            for (int columnByte = 0; columnByte < TileSize * 4; columnByte++)
            {
                frame[destinationOffset + columnByte] = rgba[sourceOffset + columnByte];
            }
        }

        return HasUsableRgba(frame);
    }

    internal static int GetPhysicalFrameCount(int width, int height)
    {
        if (width < TileSize
            || height < TileSize
            || width % TileSize != 0
            || height % TileSize != 0)
        {
            return 0;
        }

        long physicalFrameCount = (long)(width / TileSize) * (height / TileSize);
        return (int)Math.Min(MaxPhysicalFrames, physicalFrameCount);
    }

    internal static int GetTrustedOverrideFrameCount(int physicalFrameCount, bool wholeTextureReadSucceeded)
    {
        return wholeTextureReadSucceeded ? Math.Max(0, physicalFrameCount) : 0;
    }

    internal static bool HasUsablePixels(IReadOnlyList<Color> pixels)
    {
        if (pixels.Count == 0)
        {
            return false;
        }

        bool anyVisible = false;
        bool allSame = true;
        Color first = pixels[0];
        foreach (Color pixel in pixels)
        {
            anyVisible |= pixel.A > 0;
            allSame &= pixel == first;
        }

        return anyVisible && !allSame;
    }

    internal static bool HasUsableRgba(IReadOnlyList<byte> rgba)
    {
        if (rgba.Count < 4 || rgba.Count % 4 != 0)
        {
            return false;
        }

        bool anyVisible = false;
        bool allSame = true;
        byte r = rgba[0];
        byte g = rgba[1];
        byte b = rgba[2];
        byte a = rgba[3];
        for (int i = 0; i < rgba.Count; i += 4)
        {
            anyVisible |= rgba[i + 3] > 0;
            allSame &= rgba[i] == r
                && rgba[i + 1] == g
                && rgba[i + 2] == b
                && rgba[i + 3] == a;
        }

        return anyVisible && !allSame;
    }

    internal static string ComputeHash(IReadOnlyList<Color> pixels)
    {
        byte[] rgba = new byte[pixels.Count * 4];
        for (int i = 0; i < pixels.Count; i++)
        {
            Color pixel = pixels[i];
            int offset = i * 4;
            rgba[offset] = pixel.A == 0 ? (byte)0 : pixel.R;
            rgba[offset + 1] = pixel.A == 0 ? (byte)0 : pixel.G;
            rgba[offset + 2] = pixel.A == 0 ? (byte)0 : pixel.B;
            rgba[offset + 3] = pixel.A;
        }

        return Convert.ToHexString(SHA256.HashData(rgba));
    }

    internal static string ComputeRgbaHash(IReadOnlyList<byte> rgba)
    {
        byte[] canonical = rgba.ToArray();
        for (int offset = 0; offset + 3 < canonical.Length; offset += 4)
        {
            if (canonical[offset + 3] == 0)
            {
                canonical[offset] = 0;
                canonical[offset + 1] = 0;
                canonical[offset + 2] = 0;
            }
        }

        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private CachedTexture GetCachedTexture(Texture2D portrait)
    {
        int epoch = Volatile.Read(ref this.cacheEpoch);
        lock (this.cacheGate)
        {
            if (this.runtimeTextures.TryGetValue(portrait, out CachedTexture? cached)
                && cached.Width == portrait.Width
                && cached.Height == portrait.Height
                && cached.Epoch == epoch)
            {
                return cached;
            }

            this.runtimeTextures.Remove(portrait);
            cached = this.ReadTexture(portrait, epoch);
            this.runtimeTextures.Add(portrait, cached);
            return cached;
        }
    }

    private CachedTexture RefreshCachedTexture(Texture2D portrait)
    {
        int epoch = Volatile.Read(ref this.cacheEpoch);
        lock (this.cacheGate)
        {
            this.runtimeTextures.Remove(portrait);
            CachedTexture cached = this.ReadTexture(portrait, epoch);
            this.runtimeTextures.Add(portrait, cached);
            return cached;
        }
    }

    private CachedTexture ReadTexture(Texture2D portrait, int epoch)
    {
        int width = portrait.Width;
        int height = portrait.Height;
        int columns = width / TileSize;
        int frameCount = GetPhysicalFrameCount(width, height);
        long pixelCount = (long)width * height;
        if (frameCount <= 0 || pixelCount <= 0)
        {
            return new CachedTexture(
                width,
                height,
                epoch,
                0,
                new Dictionary<int, string>(),
                ComputeSignature($"{width}x{height}|invalid-size"));
        }

        Color[]? source = null;
        if (pixelCount <= MaxWholeTexturePixels && pixelCount <= int.MaxValue)
        {
            try
            {
                source = new Color[(int)pixelCount];
                portrait.GetData(source);
            }
            catch
            {
                source = null;
            }
        }

        bool wholeTextureReadSucceeded = source != null;
        var usableHashes = new Dictionary<int, string>();
        var signature = new StringBuilder(
            $"{width}x{height}|{frameCount}|{(wholeTextureReadSucceeded ? "whole-read" : "regional-read")}");
        var frame = new Color[TileSize * TileSize];
        // A successful whole-texture read is also what makes explicit ExtraPortraits trustworthy,
        // so include every physical frame in that signature. This catches a Content Patcher swap
        // even when it only changes a high numeric frame that isn't present in the reviewed catalog.
        // On the regional fallback path, only catalog-backed frames can be used, so reading those
        // remains sufficient and avoids thousands of GPU reads from a malformed oversized sheet.
        IEnumerable<int> observedFrameIndexes = GetObservedFrameIndexes(
            frameCount,
            wholeTextureReadSucceeded,
            this.byIndex.Keys);
        foreach (int frameIndex in observedFrameIndexes)
        {
            int x = frameIndex % columns * TileSize;
            int y = frameIndex / columns * TileSize;
            bool read = source != null;
            if (source != null)
            {
                for (int row = 0; row < TileSize; row++)
                {
                    Array.Copy(
                        source,
                        (y + row) * width + x,
                        frame,
                        row * TileSize,
                        TileSize);
                }
            }
            else
            {
                try
                {
                    portrait.GetData(
                        0,
                        new Rectangle(x, y, TileSize, TileSize),
                        frame,
                        0,
                        frame.Length);
                    read = true;
                }
                catch
                {
                    read = false;
                }
            }

            if (!read)
            {
                signature.Append('|').Append(frameIndex).Append(":read-failed");
                continue;
            }

            string hash = ComputeHash(frame);
            bool usable = HasUsablePixels(frame);
            signature.Append('|').Append(frameIndex).Append(usable ? ':' : '!').Append(hash);
            if (usable && this.byIndex.ContainsKey(frameIndex))
            {
                usableHashes[frameIndex] = hash;
            }
        }

        return new CachedTexture(
            width,
            height,
            epoch,
            GetTrustedOverrideFrameCount(frameCount, wholeTextureReadSucceeded),
            usableHashes,
            ComputeSignature(signature.ToString()));
    }

    internal static IReadOnlyList<int> GetObservedFrameIndexes(
        int frameCount,
        bool wholeTextureReadSucceeded,
        IEnumerable<int> catalogFrameIndexes)
    {
        if (frameCount <= 0)
        {
            return Array.Empty<int>();
        }

        return wholeTextureReadSucceeded
            ? Enumerable.Range(0, frameCount).ToArray()
            : catalogFrameIndexes
                .Where(index => index >= 0 && index < frameCount)
                .Distinct()
                .OrderBy(index => index)
                .ToArray();
    }

    private static string ComputeSignature(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static bool TryGetTexture(
        Texture2D? portrait,
        [NotNullWhen(true)] out Texture2D? usablePortrait)
    {
        usablePortrait = portrait;
        return portrait != null && !portrait.IsDisposed;
    }

    private static string Localize(Entry entry, string? locale)
    {
        bool useChinese = locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
        return useChinese && !string.IsNullOrWhiteSpace(entry.Chinese)
            ? entry.Chinese
            : entry.English;
    }

    private static void AddFrame(
        Dictionary<int, Dictionary<string, Entry>> entries,
        HashSet<(int Index, string Hash)> blocked,
        FrameRecord frame)
    {
        if (frame.Index < 0
            || PortraitMarkerRules.MarkerForIndex(frame.Index) is not { } expectedMarker
            || (!string.IsNullOrWhiteSpace(frame.Marker)
                && !string.Equals(frame.Marker.Trim(), expectedMarker, StringComparison.OrdinalIgnoreCase))
            || NormalizeHash(frame.Hash) is not { } normalized)
        {
            return;
        }

        var key = (frame.Index, normalized);
        string decision = (frame.Decision ?? string.Empty).Trim().ToLowerInvariant();
        if (decision == "deny")
        {
            blocked.Add(key);
            if (entries.TryGetValue(frame.Index, out Dictionary<string, Entry>? deniedIndex))
            {
                deniedIndex.Remove(normalized);
            }

            return;
        }

        bool allowed = frame.Enabled && (decision.Length == 0 || decision == "allow");
        if (!allowed || blocked.Contains(key))
        {
            return;
        }

        AddAllowed(
            entries,
            blocked,
            frame.Index,
            normalized,
            new Entry(frame.English ?? string.Empty, frame.Chinese ?? string.Empty));
    }

    private static void AddLegacyFrame(
        Dictionary<int, Dictionary<string, Entry>> entries,
        HashSet<(int Index, string Hash)> blocked,
        int frameIndex,
        string? hash,
        LegacyFrame frame)
    {
        if (!frame.Enabled || NormalizeHash(hash) is not { } normalized)
        {
            return;
        }

        AddAllowed(
            entries,
            blocked,
            frameIndex,
            normalized,
            new Entry(frame.English ?? string.Empty, frame.Chinese ?? string.Empty));
    }

    private static void AddAllowed(
        Dictionary<int, Dictionary<string, Entry>> entries,
        HashSet<(int Index, string Hash)> blocked,
        int frameIndex,
        string normalizedHash,
        Entry candidate)
    {
        var key = (frameIndex, normalizedHash);
        if (blocked.Contains(key)
            || string.IsNullOrWhiteSpace(candidate.English) && string.IsNullOrWhiteSpace(candidate.Chinese))
        {
            return;
        }

        if (!entries.TryGetValue(frameIndex, out Dictionary<string, Entry>? byHash))
        {
            byHash = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            entries[frameIndex] = byHash;
        }

        if (byHash.TryGetValue(normalizedHash, out Entry? existing) && existing != candidate)
        {
            byHash.Remove(normalizedHash);
            blocked.Add(key);
            return;
        }

        byHash[normalizedHash] = candidate;
    }

    private static string? NormalizeHash(string? hash)
    {
        string normalized = (hash ?? string.Empty).Trim().ToUpper(CultureInfo.InvariantCulture);
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : null;
    }

    private sealed class Catalog
    {
        [JsonProperty("Version")]
        public int? Version { get; set; }

        [JsonProperty("TileSize")]
        public int? TileSize { get; set; }

        public List<FrameRecord>? FrameEntries { get; set; }

        public Dictionary<string, LegacyFrame>? Frames { get; set; }

        public Dictionary<string, Profile>? Profiles { get; set; }
    }

    private sealed class FrameRecord
    {
        [JsonProperty("index")]
        public int Index { get; set; } = -1;

        [JsonProperty("marker")]
        public string? Marker { get; set; }

        [JsonProperty("hash")]
        public string? Hash { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("decision")]
        public string? Decision { get; set; }

        [JsonProperty("en")]
        public string? English { get; set; }

        [JsonProperty("zh")]
        public string? Chinese { get; set; }
    }

    private sealed class LegacyFrame
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("en")]
        public string? English { get; set; }

        [JsonProperty("zh")]
        public string? Chinese { get; set; }
    }

    private sealed class Profile
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("en")]
        public string? English { get; set; }

        [JsonProperty("zh")]
        public string? Chinese { get; set; }

        [JsonProperty("hashes")]
        public List<string>? Hashes { get; set; }
    }
}
