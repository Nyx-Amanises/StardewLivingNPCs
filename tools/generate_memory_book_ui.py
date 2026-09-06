#!/usr/bin/env python3
"""Generate the original pixel-art atlas used by the in-game memory book.

The atlas is intentionally generated from tiny pixel maps and primitive shapes so the
source artwork stays reviewable, reproducible, and independent from Stardew Valley or
other mods' distributed assets.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "LivingNPCs" / "assets" / "ui" / "memory-book.png"

TRANSPARENT = (0, 0, 0, 0)
SHADOW = (58, 30, 22, 255)
INK = (74, 37, 24, 255)
LEATHER_DARK = (91, 44, 30, 255)
LEATHER = (138, 69, 40, 255)
LEATHER_LIGHT = (192, 106, 58, 255)
GOLD = (231, 154, 69, 255)
GOLD_LIGHT = (249, 190, 91, 255)
PAPER = (243, 212, 138, 255)
PAPER_LIGHT = (248, 229, 181, 255)
PAPER_BRIGHT = (255, 241, 201, 255)
PAPER_SHADOW = (215, 168, 95, 255)
MUTED = (118, 85, 60, 255)
RELATION_RED = (157, 62, 58, 255)
MEMORY_GREEN = (49, 91, 70, 255)
MEMORY_LIGHT = (95, 145, 90, 255)
TALK_BLUE = (54, 92, 121, 255)
TALK_LIGHT = (102, 154, 181, 255)
MOMENT_ORANGE = (138, 75, 31, 255)
SKY_PAPER = (207, 226, 221, 255)
ROSE_PAPER = (238, 205, 184, 255)
WHITE = (255, 249, 223, 255)


def inset_polygon(x: int, y: int, size: int, inset: int, cut: int = 2) -> list[tuple[int, int]]:
    left = x + inset
    top = y + inset
    right = x + size - 1 - inset
    bottom = y + size - 1 - inset
    corner = max(0, cut - inset)
    return [
        (left + corner, top),
        (right - corner, top),
        (right, top + corner),
        (right, bottom - corner),
        (right - corner, bottom),
        (left + corner, bottom),
        (left, bottom - corner),
        (left, top + corner),
    ]


def draw_nine_slice(
    draw: ImageDraw.ImageDraw,
    x: int,
    y: int,
    *,
    fill: tuple[int, int, int, int],
    rim: tuple[int, int, int, int],
    bevel: tuple[int, int, int, int],
    highlight: tuple[int, int, int, int],
    shade: tuple[int, int, int, int],
) -> None:
    size = 18
    draw.polygon(inset_polygon(x, y, size, 0), fill=rim)
    draw.polygon(inset_polygon(x, y, size, 1), fill=bevel)
    draw.polygon(inset_polygon(x, y, size, 2), fill=fill)

    draw.line((x + 3, y + 2, x + 14, y + 2), fill=highlight)
    draw.line((x + 2, y + 3, x + 2, y + 14), fill=highlight)
    draw.line((x + 3, y + 15, x + 14, y + 15), fill=shade)
    draw.line((x + 15, y + 3, x + 15, y + 14), fill=shade)

    draw.point((x + 3, y + 3), fill=GOLD_LIGHT)
    draw.point((x + 14, y + 3), fill=GOLD)
    draw.point((x + 3, y + 14), fill=GOLD)
    draw.point((x + 14, y + 14), fill=INK)


def paint_map(
    image: Image.Image,
    x: int,
    y: int,
    rows: tuple[str, ...],
    colors: dict[str, tuple[int, int, int, int]],
    scale: int = 1,
) -> None:
    for row_index, row in enumerate(rows):
        for column_index, symbol in enumerate(row):
            color = colors.get(symbol)
            if color is None:
                continue
            for dy in range(scale):
                for dx in range(scale):
                    image.putpixel(
                        (x + column_index * scale + dx, y + row_index * scale + dy),
                        color,
                    )


def icon_palette(primary: tuple[int, int, int, int], accent: tuple[int, int, int, int]) -> dict[str, tuple[int, int, int, int]]:
    return {"1": INK, "2": primary, "3": accent, "4": WHITE}


GLYPHS: dict[str, tuple[str, ...]] = {
    "villagers": (
        "000000000000",
        "001110011100",
        "012221122210",
        "012221122210",
        "001110011100",
        "000100001000",
        "011110111100",
        "122221222210",
        "122221222210",
        "011110111100",
        "000000000000",
        "000000000000",
    ),
    "relationship": (
        "000000000000",
        "001100011000",
        "012210122100",
        "122221222210",
        "122222222210",
        "012222222100",
        "001222221000",
        "000122210000",
        "000012100000",
        "000001000000",
        "000000000000",
        "000000000000",
    ),
    "memories": (
        "000000000000",
        "011110111100",
        "122221222210",
        "123221223210",
        "122221222210",
        "122221222210",
        "122221222210",
        "011110111100",
        "000001000000",
        "000344300000",
        "000434000000",
        "000030000000",
    ),
    "conversations": (
        "000000000000",
        "011111110000",
        "122222221000",
        "123223221000",
        "122222221000",
        "011111110000",
        "001100000000",
        "000001111110",
        "000012222221",
        "000012232221",
        "000001111110",
        "000000001100",
    ),
    "moments": (
        "001000100000",
        "011111111100",
        "122222222210",
        "133333333310",
        "122222222210",
        "123223232210",
        "122322323210",
        "123223232210",
        "122222222210",
        "011111111100",
        "000004000000",
        "000044400000",
    ),
    "leaf": (
        "000000011000",
        "000000122100",
        "000001233100",
        "000012333100",
        "000123331000",
        "001233310000",
        "012333100000",
        "123331000000",
        "011110000000",
        "000100000000",
        "000100000000",
        "000000000000",
    ),
    "sparkle": (
        "000003000000",
        "000003000000",
        "000013100000",
        "033333333300",
        "000013100000",
        "000003000000",
        "000003000000",
        "000000000000",
        "000000300000",
        "000003330000",
        "000000300000",
        "000000000000",
    ),
    "quill": (
        "000000000110",
        "000000001221",
        "000000012321",
        "000000123310",
        "000001233100",
        "000012331000",
        "000123310000",
        "001233100000",
        "012331000000",
        "123310000000",
        "011111111100",
        "000000000000",
    ),
    "calendar": (
        "001000010000",
        "011111111100",
        "122222222210",
        "133333333310",
        "122222222210",
        "123223232210",
        "122322323210",
        "123223232210",
        "122222222210",
        "011111111100",
        "000000000000",
        "000000000000",
    ),
    "gift": (
        "000110110000",
        "001221221000",
        "000122210000",
        "011111111100",
        "122233222210",
        "122233222210",
        "111133111100",
        "122233222210",
        "122233222210",
        "011111111100",
        "000000000000",
        "000000000000",
    ),
    "empty": (
        "000000000000",
        "011110111100",
        "122221222210",
        "122221222210",
        "122221222210",
        "122221222210",
        "122221222210",
        "011110111100",
        "000001000000",
        "000000003000",
        "000000033300",
        "000000003000",
    ),
    "pin": (
        "000011100000",
        "000122210000",
        "000123210000",
        "000122210000",
        "001111111000",
        "000011100000",
        "000011000000",
        "000010000000",
        "000010000000",
        "000000000000",
        "000000000000",
        "000000000000",
    ),
    "flower": (
        "000030300000",
        "000333330000",
        "003334333000",
        "000333330000",
        "000034300000",
        "000001000000",
        "000011000000",
        "000121100000",
        "001221000000",
        "000110000000",
        "000100000000",
        "000000000000",
    ),
    "clock": (
        "000111100000",
        "001222210000",
        "012333321000",
        "123303332100",
        "123303332100",
        "123303332100",
        "123333332100",
        "012333321000",
        "001222210000",
        "000111100000",
        "000000000000",
        "000000000000",
    ),
    "up": (
        "000003000000",
        "000033300000",
        "000322230000",
        "003222223000",
        "032222222300",
        "000022200000",
        "000022200000",
        "000022200000",
        "000011100000",
        "000000000000",
        "000000000000",
        "000000000000",
    ),
    "down": (
        "000011100000",
        "000022200000",
        "000022200000",
        "000022200000",
        "032222222300",
        "003222223000",
        "000322230000",
        "000033300000",
        "000003000000",
        "000000000000",
        "000000000000",
        "000000000000",
    ),
    "promise": (
        "................",
        "................",
        "...111....111...",
        "..13321..12331..",
        "..12.121121.21..",
        "...12.1331.21...",
        "....12322321....",
        ".....123321.....",
        ".....122221.....",
        "....121..121....",
        "...1231..1231...",
        "...1241..1241...",
        "....11....11....",
        "................",
        "................",
        "................",
    ),
}


def main() -> None:
    image = Image.new("RGBA", (256, 128), TRANSPARENT)
    draw = ImageDraw.Draw(image)

    panels = [
        (0, LEATHER, SHADOW, LEATHER_DARK, LEATHER_LIGHT, INK),
        (20, PAPER, INK, PAPER_SHADOW, PAPER_LIGHT, MUTED),
        (40, PAPER_LIGHT, INK, PAPER_SHADOW, PAPER_BRIGHT, MUTED),
        (60, PAPER_BRIGHT, RELATION_RED, GOLD, WHITE, PAPER_SHADOW),
        (80, PAPER, INK, PAPER_SHADOW, PAPER_LIGHT, MUTED),
        (100, GOLD_LIGHT, INK, RELATION_RED, WHITE, LEATHER_DARK),
        (120, PAPER_LIGHT, INK, GOLD, PAPER_BRIGHT, PAPER_SHADOW),
        (140, SKY_PAPER, TALK_BLUE, TALK_LIGHT, WHITE, MUTED),
        (160, ROSE_PAPER, RELATION_RED, GOLD, WHITE, MUTED),
        (180, GOLD_LIGHT, INK, MOMENT_ORANGE, WHITE, LEATHER_DARK),
        (200, PAPER_BRIGHT, INK, PAPER_SHADOW, WHITE, MUTED),
        (220, PAPER_SHADOW, INK, MUTED, PAPER_LIGHT, SHADOW),
    ]
    for x, fill, rim, bevel, highlight, shade in panels:
        draw_nine_slice(
            draw,
            x,
            0,
            fill=fill,
            rim=rim,
            bevel=bevel,
            highlight=highlight,
            shade=shade,
        )

    # Cloth title plate with stitched edges and ribbon tails.
    title = [(0, 8), (6, 2), (14, 2), (14, 0), (81, 0), (81, 2), (89, 2), (95, 8), (89, 18), (81, 18), (81, 19), (14, 19), (14, 18), (6, 18)]
    draw.polygon([(x, y + 24) for x, y in title], fill=INK)
    inner = [(2, 8), (7, 4), (16, 4), (16, 2), (79, 2), (79, 4), (88, 4), (93, 8), (88, 16), (79, 16), (79, 17), (16, 17), (16, 16), (7, 16)]
    draw.polygon([(x, y + 24) for x, y in inner], fill=GOLD)
    draw.rectangle((16, 27, 79, 40), fill=PAPER_BRIGHT)
    draw.line((17, 27, 78, 27), fill=WHITE)
    draw.line((17, 40, 78, 40), fill=PAPER_SHADOW)
    for stitch_x in range(20, 78, 7):
        draw.point((stitch_x, 29), fill=MUTED)
        draw.point((stitch_x, 38), fill=MUTED)

    # Repeating leather spine, paper speckle tile, and small decorations.
    draw.rectangle((100, 24, 107, 39), fill=SHADOW)
    draw.rectangle((101, 24, 106, 39), fill=LEATHER_DARK)
    draw.rectangle((103, 24, 104, 39), fill=LEATHER_LIGHT)
    for stitch_y in (26, 31, 36):
        draw.point((102, stitch_y), fill=GOLD)
        draw.point((105, stitch_y), fill=GOLD)

    for px, py, alpha in ((113, 25, 54), (118, 27, 42), (115, 31, 48), (119, 30, 34)):
        image.putpixel((px, py), (*MUTED[:3], alpha))

    paint_map(image, 124, 24, GLYPHS["leaf"], icon_palette(MEMORY_GREEN, MEMORY_LIGHT), scale=2)
    paint_map(image, 152, 24, GLYPHS["flower"], icon_palette(MEMORY_GREEN, GOLD_LIGHT), scale=2)

    draw.polygon([(181, 24), (191, 24), (191, 43), (186, 39), (181, 43)], fill=INK)
    draw.polygon([(183, 25), (189, 25), (189, 39), (186, 36), (183, 39)], fill=RELATION_RED)
    draw.line((184, 27, 188, 27), fill=GOLD_LIGHT)

    draw.rectangle((196, 24, 205, 39), fill=INK)
    draw.rectangle((198, 25, 203, 38), fill=GOLD)
    draw.rectangle((199, 26, 201, 36), fill=GOLD_LIGHT)
    draw.point((202, 37), fill=LEATHER_DARK)

    draw.polygon([(215, 24), (221, 24), (225, 28), (225, 35), (221, 39), (215, 39), (211, 35), (211, 28)], fill=INK)
    draw.polygon([(216, 25), (220, 25), (224, 29), (224, 34), (220, 38), (216, 38), (212, 34), (212, 29)], fill=RELATION_RED)
    draw.line((215, 29, 221, 35), fill=WHITE)
    draw.line((221, 29, 215, 35), fill=WHITE)

    paint_map(image, 229, 23, GLYPHS["up"], icon_palette(GOLD, GOLD_LIGHT))
    paint_map(image, 241, 23, GLYPHS["down"], icon_palette(GOLD, GOLD_LIGHT))

    icon_specs = [
        ("villagers", MEMORY_GREEN, GOLD_LIGHT),
        ("relationship", RELATION_RED, GOLD_LIGHT),
        ("memories", MEMORY_GREEN, GOLD_LIGHT),
        ("conversations", TALK_BLUE, TALK_LIGHT),
        ("moments", MOMENT_ORANGE, GOLD_LIGHT),
        ("leaf", MEMORY_GREEN, MEMORY_LIGHT),
        ("sparkle", GOLD, GOLD_LIGHT),
        ("quill", TALK_BLUE, PAPER_BRIGHT),
        ("calendar", MOMENT_ORANGE, GOLD_LIGHT),
        ("gift", RELATION_RED, GOLD_LIGHT),
        ("empty", MEMORY_GREEN, GOLD_LIGHT),
        ("pin", RELATION_RED, GOLD_LIGHT),
        ("flower", MEMORY_GREEN, GOLD_LIGHT),
        ("clock", TALK_BLUE, GOLD_LIGHT),
        ("up", GOLD, GOLD_LIGHT),
        ("down", GOLD, GOLD_LIGHT),
    ]
    for index, (name, primary, accent) in enumerate(icon_specs):
        paint_map(image, index * 16 + 2, 58, GLYPHS[name], icon_palette(primary, accent))

    # A second row contains muted and highlighted tab variants for future skins.
    for index, name in enumerate(("relationship", "memories", "conversations", "moments")):
        paint_map(image, index * 16 + 2, 82, GLYPHS[name], icon_palette(PAPER_BRIGHT, GOLD_LIGHT))

    # The approved 16x16 promise knot occupies the next unused tile in the second row.
    paint_map(
        image,
        64,
        80,
        GLYPHS["promise"],
        {"1": INK, "2": RELATION_RED, "3": LEATHER_LIGHT, "4": GOLD_LIGHT},
    )

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUTPUT, format="PNG", optimize=False)
    print(f"Wrote {OUTPUT} ({image.width}x{image.height}, RGBA)")


if __name__ == "__main__":
    main()
