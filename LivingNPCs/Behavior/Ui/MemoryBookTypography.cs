using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LivingNPCs.Behavior.Ui;

/// <summary>手册正文的局部字距修正，复用游戏字形，不改变全局字体或其他语言的排版。</summary>
internal sealed class MemoryBookTypography
{
    private SpriteFont? cachedSource;
    private SpriteFont? cachedBodyFont;
    private int cachedLineSpacing;
    private float cachedSpacing;
    private char? cachedDefaultCharacter;

    /// <summary>返回测量、换行和绘制必须共用的正文实例；所有纹理仍由游戏管理。</summary>
    internal SpriteFont BodyFont(SpriteFont source, bool useEvenCjkSpacing)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!useEvenCjkSpacing)
        {
            return source;
        }

        if (ReferenceEquals(this.cachedSource, source)
            && this.cachedBodyFont != null
            && this.cachedLineSpacing == source.LineSpacing
            && this.cachedSpacing == source.Spacing
            && this.cachedDefaultCharacter == source.DefaultCharacter)
        {
            return this.cachedBodyFont;
        }

        this.cachedSource = source;
        this.cachedLineSpacing = source.LineSpacing;
        this.cachedSpacing = source.Spacing;
        this.cachedDefaultCharacter = source.DefaultCharacter;
        this.cachedBodyFont = CreateEvenCjkFont(source);
        return this.cachedBodyFont;
    }

    private static SpriteFont CreateEvenCjkFont(SpriteFont source)
    {
        SpriteFont.Glyph[] glyphs = source.Glyphs;
        int widestCjkGlyph = 0;
        float largestCjkAdvance = 0f;
        foreach (SpriteFont.Glyph glyph in glyphs)
        {
            if (!IsCjkIdeograph(glyph.Character))
            {
                continue;
            }

            widestCjkGlyph = Math.Max(widestCjkGlyph, glyph.BoundsInTexture.Width);
            largestCjkAdvance = Math.Max(largestCjkAdvance, glyph.WidthIncludingBearings + source.Spacing);
        }

        if (widestCjkGlyph == 0)
        {
            return source;
        }

        // The shipped Chinese small font advances 18px, but some glyphs paint 20px.
        // Reserve at least one clear pixel; keep wider font replacements readable too.
        // Positive tracking also needs enough side bearing for MeasureString's final glyph.
        int clearance = Math.Max(1, 2 * (int)Math.Ceiling(Math.Max(0f, source.Spacing)));
        int cellAdvance = (int)Math.Ceiling(Math.Max(largestCjkAdvance, widestCjkGlyph + clearance));
        var bounds = new List<Rectangle>(glyphs.Length);
        var cropping = new List<Rectangle>(glyphs.Length);
        var characters = new List<char>(glyphs.Length);
        var kerning = new List<Vector3>(glyphs.Length);

        foreach (SpriteFont.Glyph glyph in glyphs)
        {
            bounds.Add(glyph.BoundsInTexture);
            characters.Add(glyph.Character);
            if (!IsCjkIdeograph(glyph.Character))
            {
                cropping.Add(glyph.Cropping);
                kerning.Add(new Vector3(glyph.LeftSideBearing, glyph.Width, glyph.RightSideBearing));
                continue;
            }

            int inkWidth = glyph.BoundsInTexture.Width;
            int left = (cellAdvance - inkWidth) / 2;
            float right = cellAdvance - source.Spacing - left - inkWidth;

            // DrawString clamps the first glyph's negative left bearing, then adds Cropping.X.
            // Move horizontal placement into a nonnegative integer bearing so line starts and
            // following glyphs use the same cell. Keep the original vertical baseline and size.
            cropping.Add(new Rectangle(0, glyph.Cropping.Y, glyph.Cropping.Width, glyph.Cropping.Height));
            kerning.Add(new Vector3(left, inkWidth, right));
        }

        return new SpriteFont(
            source.Texture,
            bounds,
            cropping,
            characters,
            source.LineSpacing,
            source.Spacing,
            kerning,
            source.DefaultCharacter);
    }

    private static bool IsCjkIdeograph(char character)
    {
        return character is >= '\u3400' and <= '\u4DBF'
            or >= '\u4E00' and <= '\u9FFF'
            or >= '\uF900' and <= '\uFAFF';
    }
}
