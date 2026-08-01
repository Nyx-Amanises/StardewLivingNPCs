using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace LivingNPCs.Behavior.Ui;

internal enum MemoryBookFrame
{
    Cover,
    Page,
    Roster,
    SelectedRow,
    TabIdle,
    TabActive,
    Content,
    PlayerNote,
    NpcNote,
    Header,
    Status,
    ScrollTrack
}

internal enum MemoryBookIcon
{
    Villagers,
    Relationship,
    Memories,
    Conversations,
    Moments,
    Leaf,
    Sparkle,
    Quill,
    Calendar,
    Gift,
    EmptyBook,
    Pin,
    Flower,
    Clock,
    ArrowUp,
    ArrowDown
}

/// <summary>
/// Original, mod-owned pixel artwork for the memory book. Loading is deliberately lazy and
/// failure-tolerant: a damaged/missing atlas must never prevent the menu from opening.
/// </summary>
internal sealed class MemoryBookAssets
{
    private const string AssetPath = "assets/ui/memory-book.png";

    private MemoryBookAssets(Texture2D? texture)
    {
        this.Texture = texture;
    }

    public Texture2D? Texture { get; }

    public bool HasCustomArt => this.Texture != null;

    internal static MemoryBookAssets Fallback { get; } = new(texture: null);

    public static MemoryBookAssets Load(IModHelper helper, IMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentNullException.ThrowIfNull(monitor);

        try
        {
            Texture2D texture = helper.ModContent.Load<Texture2D>(AssetPath);
            if (texture.Width != 256 || texture.Height != 128)
            {
                throw new InvalidOperationException(
                    $"Expected a 256x128 atlas, but the loaded texture is {texture.Width}x{texture.Height}.");
            }

            return new MemoryBookAssets(texture);
        }
        catch (Exception ex)
        {
            monitor.Log(
                $"Failed to load the memory-book pixel atlas '{AssetPath}'. The menu will use its vanilla-texture fallback. {ex.Message}",
                LogLevel.Warn);
            return new MemoryBookAssets(texture: null);
        }
    }

    internal static Rectangle FrameSource(MemoryBookFrame frame)
    {
        int x = frame switch
        {
            MemoryBookFrame.Cover => 0,
            MemoryBookFrame.Page => 20,
            MemoryBookFrame.Roster => 40,
            MemoryBookFrame.SelectedRow => 60,
            MemoryBookFrame.TabIdle => 80,
            MemoryBookFrame.TabActive => 100,
            MemoryBookFrame.Content => 120,
            MemoryBookFrame.PlayerNote => 140,
            MemoryBookFrame.NpcNote => 160,
            MemoryBookFrame.Header => 180,
            MemoryBookFrame.Status => 200,
            MemoryBookFrame.ScrollTrack => 220,
            _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "Unknown memory-book frame.")
        };
        return new Rectangle(x, 0, 18, 18);
    }

    internal static Rectangle IconSource(MemoryBookIcon icon, bool highlighted = false)
    {
        if (!Enum.IsDefined(typeof(MemoryBookIcon), icon))
        {
            throw new ArgumentOutOfRangeException(nameof(icon), icon, "Unknown memory-book icon.");
        }

        int index = (int)icon;
        if (highlighted && index <= (int)MemoryBookIcon.Moments && index >= (int)MemoryBookIcon.Relationship)
        {
            return new Rectangle((index - 1) * 16, 80, 16, 16);
        }

        return new Rectangle(index * 16, 56, 16, 16);
    }

    internal static Rectangle TitleBannerSource => new(0, 24, 96, 20);

    internal static Rectangle SpineSource => new(100, 24, 8, 16);

    internal static Rectangle PaperPatternSource => new(112, 24, 8, 8);

    internal static Rectangle SprigSource => new(124, 24, 24, 24);

    internal static Rectangle FlowerSource => new(152, 24, 24, 24);

    internal static Rectangle BookmarkSource => new(180, 24, 12, 20);

    internal static Rectangle ScrollThumbSource => new(196, 24, 10, 16);

    internal static Rectangle WaxSealSource => new(210, 24, 16, 16);
}

internal static class MemoryBookPalette
{
    internal static readonly Color Shadow = new(58, 30, 22);
    internal static readonly Color Ink = new(74, 37, 24);
    internal static readonly Color LeatherDark = new(91, 44, 30);
    internal static readonly Color Leather = new(138, 69, 40);
    internal static readonly Color LeatherLight = new(192, 106, 58);
    internal static readonly Color Gold = new(231, 154, 69);
    internal static readonly Color GoldLight = new(249, 190, 91);
    internal static readonly Color Paper = new(243, 212, 138);
    internal static readonly Color PaperLight = new(248, 229, 181);
    internal static readonly Color PaperBright = new(255, 241, 201);
    internal static readonly Color PaperShadow = new(215, 168, 95);
    internal static readonly Color Muted = new(118, 85, 60);
    internal static readonly Color Relationship = new(157, 62, 58);
    internal static readonly Color Memories = new(49, 91, 70);
    internal static readonly Color Conversations = new(54, 92, 121);
    internal static readonly Color Moments = new(138, 75, 31);
    internal static readonly Color PlayerNote = new(207, 226, 221);
    internal static readonly Color NpcNote = new(238, 205, 184);
}
