using LivingNPCs.Behavior.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LivingNPCs.Tests;

public sealed class MemoryBookTypographyTests
{
    [Fact]
    public void WideChineseGlyphsHaveClearSpaceAndEqualAdvances()
    {
        SpriteFont source = CreateFont();
        SpriteFont font = new MemoryBookTypography().BodyFont(source, useEvenCjkSpacing: true);
        const string text = "一起四处起起一起";

        List<(float Left, float Right)> painted = PaintedGlyphBounds(font, text);
        for (int i = 1; i < painted.Count; i++)
        {
            Assert.True(painted[i].Left - painted[i - 1].Right >= 1f);
            Assert.Equal(21f, font.MeasureString(text[..(i + 1)]).X - font.MeasureString(text[..i]).X);
        }

        // SpriteFont omits inter-character Spacing before the first glyph: 21 * 4 - (-1).
        Assert.Equal(85f, font.MeasureString("一起四处").X);
        Assert.All(painted, glyph => Assert.Equal(MathF.Floor(glyph.Left), glyph.Left));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(-0.5f)]
    [InlineData(2f)]
    public void HorizontalCroppingAndNegativeFirstBearingCannotShiftChineseCells(float spacing)
    {
        SpriteFont source = CreateFont(spacing, displacedCropping: true);
        SpriteFont font = new MemoryBookTypography().BodyFont(source, useEvenCjkSpacing: true);
        List<(float Left, float Right)> painted = PaintedGlyphBounds(font, "起起起");
        float advance = painted[1].Left - painted[0].Left;

        Assert.Equal(advance, painted[2].Left - painted[1].Left);
        Assert.Equal(MathF.Floor(advance), advance);
        Assert.True(advance >= 21f);
        Assert.All(painted, glyph => Assert.Equal(MathF.Floor(glyph.Left), glyph.Left));
        Assert.Equal(advance, font.MeasureString("起起").X - font.MeasureString("起").X);
        Assert.True(font.MeasureString("起起起").X >= painted[^1].Right);

        SpriteFont.Glyph original = source.GetGlyphs()['起'];
        SpriteFont.Glyph adjusted = font.GetGlyphs()['起'];
        Assert.Equal(original.BoundsInTexture, adjusted.BoundsInTexture);
        Assert.Equal(original.Cropping.Y, adjusted.Cropping.Y);
        Assert.Equal(original.Cropping.Height, adjusted.Cropping.Height);
        Assert.Equal(source.MeasureString("起\n起").Y, font.MeasureString("起\n起").Y);
    }

    [Fact]
    public void OriginalFontAndLatinMetricsRemainUnchanged()
    {
        SpriteFont source = CreateFont(displacedCropping: true);
        SpriteFont.Glyph[] originalGlyphs = source.Glyphs.ToArray();
        const string latin = "A17 v.\n17 A?";
        Vector2 originalLatinSize = source.MeasureString(latin);
        Vector2 originalChineseSize = source.MeasureString("一起四处");

        SpriteFont font = new MemoryBookTypography().BodyFont(source, useEvenCjkSpacing: true);

        Assert.NotSame(source, font);
        Assert.NotSame(source.Glyphs, font.Glyphs);
        Assert.Same(source.Texture, font.Texture);
        Assert.Equal(originalGlyphs, source.Glyphs);
        Assert.Equal(originalChineseSize, source.MeasureString("一起四处"));
        Assert.Equal(originalLatinSize, source.MeasureString(latin));
        Assert.Equal(originalLatinSize, font.MeasureString(latin));
        Assert.Equal(PaintedGlyphBounds(source, latin), PaintedGlyphBounds(font, latin));
        Assert.Equal(source.DefaultCharacter, font.DefaultCharacter);
        Assert.Equal(source.Spacing, font.Spacing);
        Assert.Equal(source.LineSpacing, font.LineSpacing);
    }

    [Fact]
    public void FontIsReusedUntilTheGameReloadsOrChangesItsMetrics()
    {
        var typography = new MemoryBookTypography();
        SpriteFont source = CreateFont();
        SpriteFont font = typography.BodyFont(source, useEvenCjkSpacing: true);

        Assert.Same(font, typography.BodyFont(source, useEvenCjkSpacing: true));
        Assert.Same(source, typography.BodyFont(source, useEvenCjkSpacing: false));
        Assert.Same(font, typography.BodyFont(source, useEvenCjkSpacing: true));

        SpriteFont reloaded = CreateFont();
        SpriteFont rebuilt = typography.BodyFont(reloaded, useEvenCjkSpacing: true);
        Assert.NotSame(font, rebuilt);

        reloaded.LineSpacing = 32;
        reloaded.Spacing = -0.5f;
        reloaded.DefaultCharacter = 'A';
        SpriteFont updated = typography.BodyFont(reloaded, useEvenCjkSpacing: true);
        Assert.NotSame(rebuilt, updated);
        Assert.Equal(32, updated.LineSpacing);
        Assert.Equal(-0.5f, updated.Spacing);
        Assert.Equal('A', updated.DefaultCharacter);
    }

    private static SpriteFont CreateFont(float spacing = -1f, bool displacedCropping = false)
    {
        var metrics = new Dictionary<char, (int InkWidth, Vector3 Kerning)>
        {
            [' '] = (3, new Vector3(0, 4, 0)),
            ['.'] = (3, new Vector3(1, 2, 1)),
            ['1'] = (7, new Vector3(1, 7, 1)),
            ['7'] = (8, new Vector3(0, 8, 1)),
            ['?'] = (8, new Vector3(1, 7, 1)),
            ['A'] = (11, new Vector3(-1, 11, 1)),
            ['v'] = (9, new Vector3(0, 9, -1)),
            // The widths and bearings below reproduce the shipped Chinese small font.
            ['一'] = (18, new Vector3(1, 17, 1)),
            ['起'] = (20, new Vector3(1, 17, 1)),
            ['四'] = (17, new Vector3(1, 16, 2)),
            ['处'] = (19, new Vector3(0, 18, 1))
        };
        List<char> characters = metrics.Keys.OrderBy(character => character).ToList();
        var bounds = new List<Rectangle>();
        var cropping = new List<Rectangle>();
        var kerning = new List<Vector3>();
        foreach (char character in characters)
        {
            (int inkWidth, Vector3 bearings) = metrics[character];
            bool displaced = displacedCropping && character == '起';
            bounds.Add(new Rectangle(bounds.Count * 24, 0, inkWidth, 19));
            cropping.Add(new Rectangle(displaced ? 4 : 0, 5, inkWidth, 30));
            kerning.Add(displaced ? new Vector3(-4, 20, 3) : bearings);
        }

        // SpriteFont measurement and glyph layout need no GPU or texture allocation.
        return new SpriteFont(null!, bounds, cropping, characters, 28, spacing, kerning, '?');
    }

    private static List<(float Left, float Right)> PaintedGlyphBounds(SpriteFont font, string text)
    {
        // Follow the consuming MonoGame SpriteBatch.DrawString contract without a GraphicsDevice.
        Dictionary<char, SpriteFont.Glyph> glyphs = font.GetGlyphs();
        var result = new List<(float Left, float Right)>();
        float cursor = 0f;
        bool first = true;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                cursor = 0f;
                first = true;
                continue;
            }

            SpriteFont.Glyph glyph = glyphs[character];
            cursor = first ? Math.Max(glyph.LeftSideBearing, 0f) : cursor + font.Spacing + glyph.LeftSideBearing;
            first = false;
            float left = cursor + glyph.Cropping.X;
            result.Add((left, left + glyph.BoundsInTexture.Width));
            cursor += glyph.Width + glyph.RightSideBearing;
        }

        return result;
    }
}
