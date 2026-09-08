#!/usr/bin/env python3
"""Generate the original pixel-art atlas used by the in-game memory book.

The atlas is intentionally generated from tiny pixel maps and primitive shapes so the
source artwork stays reviewable, reproducible, and independent from Stardew Valley or
other mods' distributed assets.
"""

from __future__ import annotations

import json
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
PETAL = (244, 195, 147, 255)
PETAL_LIGHT = (255, 224, 172, 255)
LEAF_DARK = (43, 75, 50, 255)
LEAF_MID = (65, 108, 64, 255)
LEAF_LIGHT = (107, 148, 77, 255)
LEAF_GLOW = (151, 177, 103, 255)
ROSE_COLORS = ((139, 65, 83, 255), (198, 105, 127, 255), (237, 157, 165, 255), (255, 204, 190, 255))
APRICOT_COLORS = ((169, 105, 58, 255), (225, 157, 80, 255), (250, 204, 127, 255), (255, 235, 188, 255))
LILAC_COLORS = ((121, 87, 133, 255), (168, 132, 177, 255), (210, 177, 209, 255), (243, 216, 230, 255))


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


TITLE_CHINESE_GLYPHS: dict[str, tuple[str, ...]] = {
    # Hand-drawn 16px title lettering. Keep the stroke grid rather than rasterizing
    # an installed font, so every build has the same crisp Chinese letterforms.
    "记": (
        "................",
        "..1.............",
        "...1...1111111..",
        "...1........1...",
        "............1...",
        ".111........1...",
        "...1...1111111..",
        "...1...1........",
        "...1...1........",
        "...1...1........",
        "...1...1........",
        "...1.1.1......1.",
        "...11..1......1.",
        "...1....111111..",
        "................",
        "................",
    ),
    "忆": (
        "................",
        "...1............",
        "...1..11111111..",
        "...1........1...",
        "...11......1....",
        ".1.1.1....1.....",
        ".1.1.....1......",
        ".1.1....1.......",
        "1..1....1.......",
        "...1...1........",
        "...1...1........",
        "...1..1.......1.",
        "...1..1.......1.",
        "...1...1111111..",
        "...1............",
        "................",
    ),
    "手": (
        "................",
        "..........111...",
        "..11111111......",
        ".......1........",
        ".......1........",
        "..11111111111...",
        ".......1........",
        ".......1........",
        ".......1........",
        ".11111111111111.",
        ".......1........",
        ".......1........",
        ".......1........",
        ".....1.1........",
        "......11........",
        "................",
    ),
    "册": (
        "................",
        "..11111..11111..",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "111111111111111.",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "..1...1..1...1..",
        "..1...1.1....1..",
        ".1..111.1..111..",
        "1......1........",
        "................",
    ),
}

TITLE_ENGLISH_GLYPHS: dict[str, tuple[str, ...]] = {
    "M": (
        "1.....1", "11...11", "1.1.1.1", "1..1..1", "1..1..1", "1.....1",
        "1.....1", "1.....1", "1.....1", "1.....1", "1.....1",
    ),
    "E": (
        "1111111", "1......", "1......", "1......", "1......", "111111.",
        "1......", "1......", "1......", "1......", "1111111",
    ),
    "O": (
        ".11111.", "1.....1", "1.....1", "1.....1", "1.....1", "1.....1",
        "1.....1", "1.....1", "1.....1", "1.....1", ".11111.",
    ),
    "R": (
        "111111.", "1.....1", "1.....1", "1.....1", "1.....1", "111111.",
        "1.1....", "1..1...", "1...1..", "1....1.", "1.....1",
    ),
    "Y": (
        "1.....1", "1.....1", ".1...1.", ".1...1.", "..1.1..", "...1...",
        "...1...", "...1...", "...1...", "...1...", "...1...",
    ),
    "B": (
        "111111.", "1.....1", "1.....1", "1.....1", "1.....1", "111111.",
        "1.....1", "1.....1", "1.....1", "1.....1", "111111.",
    ),
    "K": (
        "1.....1", "1....1.", "1...1..", "1..1...", "1.1....", "11.....",
        "1.1....", "1..1...", "1...1..", "1....1.", "1.....1",
    ),
}

BUTTERFLY = tuple(
    half + half[::-1]
    for half in (
        "..........",
        "......1...",
        "..111..1..",
        ".12231..1.",
        ".123341..1",
        ".123334111",
        "..12334311",
        "...1233411",
        "....113311",
        "...1223311",
        "...1234311",
        "....123111",
        ".....11..1",
        ".........1",
        "..........",
        "..........",
    )
)


def paint_reviewed_icon(image: Image.Image, x: int, y: int, name: str) -> None:
    """Reuse the approved source pixels, including their exact original palette."""
    source = ROOT / "docs" / "design" / "memory-book-promise-icon" / f"{name}.json"
    paint_reviewed_art(image, x, y, source, (16, 16))


def paint_reviewed_art(image: Image.Image, x: int, y: int, source: Path, expected_size: tuple[int, int]) -> None:
    """Import reviewable native pixel maps without fonts or image resampling."""
    spec = json.loads(source.read_text(encoding="utf-8-sig"))
    rows = tuple(spec["rows"])
    width, height = expected_size
    if (spec["width"], spec["height"]) != expected_size or len(rows) != height or any(len(row) != width for row in rows):
        raise ValueError(f"{source}: expected a {width}x{height} pixel map")
    palette = {symbol: tuple(color) for symbol, color in spec["palette"].items()}
    if any(len(color) != 4 or color[3] not in (0, 255) for color in palette.values()):
        raise ValueError(f"{source}: use opaque or transparent RGBA pixels")
    if any(symbol not in palette for row in rows for symbol in row):
        raise ValueError(f"{source}: pixel map references an unknown palette symbol")
    paint_map(image, x, y, rows, palette)


def paint_title(image: Image.Image, x: int, y: int, text: str, *, chinese: bool) -> None:
    glyphs = TITLE_CHINESE_GLYPHS if chinese else TITLE_ENGLISH_GLYPHS
    advance = 18 if chinese else 8
    for letter in text:
        if letter == " ":
            x += 4
            continue
        rows = glyphs[letter]
        expected_size = (16, 16) if chinese else (7, 11)
        if len(rows) != expected_size[1] or any(len(row) != expected_size[0] for row in rows):
            raise ValueError(f"Invalid title glyph: {letter}")
        # One native pixel of warm relief; never add a solid plate behind the text.
        paint_map(image, x + 1, y + 1, rows, {"1": LEATHER_LIGHT})
        paint_map(image, x, y, rows, {"1": INK})
        x += advance


def draw_vine_corner(image: Image.Image, x: int, y: int) -> None:
    """40x40 transparent L-shaped trim; leave the page-facing 30x30 area clear."""
    draw = ImageDraw.Draw(image)

    def path(points: list[tuple[int, int]], color: tuple[int, int, int, int]) -> None:
        draw.line([(x + px, y + py) for px, py in points], fill=color)

    # Tendrils follow the narrow leather rim, rather than crossing the paper.
    paths = [
        [(3, 38), (3, 29), (4, 28), (4, 19), (5, 18), (5, 12), (7, 10), (7, 7), (10, 5), (18, 5), (19, 4), (28, 4), (29, 3), (38, 3)],
        [(3, 31), (5, 29), (7, 29), (8, 30), (8, 32), (7, 33), (6, 33)],
        [(30, 4), (29, 6), (30, 8), (32, 8), (33, 7), (33, 6)],
    ]
    for points in paths:
        path([(px + 1, py + 1) for px, py in points], INK)
        path(points, MEMORY_GREEN)

    leaf = (
        ".111..",
        "12331.",
        ".23331",
        "..1231",
        "...11.",
    )
    leaf_image = Image.new("RGBA", (6, 5), TRANSPARENT)
    paint_map(leaf_image, 0, 0, leaf, icon_palette(MEMORY_GREEN, MEMORY_LIGHT))
    for px, py, flip in ((0, 18, False), (0, 33, False), (2, 8, False), (19, 0, True), (33, 0, True)):
        sprite = leaf_image.transpose(Image.Transpose.FLIP_LEFT_RIGHT) if flip else leaf_image
        image.alpha_composite(sprite, (x + px, y + py))
    # A pale apricot blossom and a tiny honey-coloured bud tie the green into the
    # existing leather-and-gold palette. Neither extends into the page interior.
    blossom = (
        "..11...",
        ".1231..",
        "123331.",
        ".13431.",
        ".133321",
        "..1231.",
        "...11..",
    )
    paint_map(image, x + 8, y, blossom, {"1": INK, "2": PETAL, "3": PETAL_LIGHT, "4": GOLD})
    paint_map(image, x + 1, y + 24, (".1.", "131", ".1."), {"1": INK, "3": GOLD_LIGHT})


def botanical_leaf() -> Image.Image:
    """A newly drawn 19x13 broad leaf with a stepped edge and branching veins."""
    leaf = Image.new("RGBA", (19, 13), TRANSPARENT)
    draw = ImageDraw.Draw(leaf)
    draw.polygon([(0, 12), (1, 8), (5, 3), (10, 1), (18, 0), (17, 5), (12, 11), (6, 12)], fill=LEAF_MID, outline=INK)
    draw.polygon([(2, 10), (8, 6), (16, 2), (16, 6), (11, 10), (5, 11)], fill=LEAF_DARK)
    draw.polygon([(2, 8), (6, 4), (11, 2), (15, 2), (9, 6), (4, 9)], fill=LEAF_LIGHT)
    draw.line((2, 10, 15, 2), fill=LEAF_GLOW)
    draw.line((6, 8, 7, 5), fill=LEAF_GLOW)
    draw.line((10, 5, 10, 3), fill=LEAF_GLOW)
    draw.line((8, 7, 12, 7), fill=LEAF_MID)
    return leaf


def rose_bloom(colors=ROSE_COLORS) -> Image.Image:
    """A 23x23 rose with individual folded petals and a small central spiral."""
    shade, mid, light, glow = colors
    bloom = Image.new("RGBA", (23, 23), TRANSPARENT)
    draw = ImageDraw.Draw(bloom)
    outline = [(8, 0), (14, 0), (14, 1), (18, 1), (18, 3), (21, 3), (21, 6), (22, 6), (22, 15), (20, 15), (20, 19), (17, 19), (17, 21), (13, 22), (6, 21), (6, 20), (3, 20), (3, 17), (1, 17), (0, 12), (1, 7), (2, 7), (2, 4), (5, 4), (5, 2), (8, 2)]
    draw.polygon(outline, fill=shade, outline=INK)
    draw.polygon([(7, 3), (9, 1), (14, 1), (17, 3), (17, 7), (14, 10), (8, 8), (5, 5)], fill=light)
    draw.line([(8, 3), (10, 2), (14, 2), (16, 4)], fill=glow)
    draw.polygon([(2, 8), (4, 5), (8, 6), (11, 11), (8, 16), (3, 15), (1, 12)], fill=mid)
    draw.line([(3, 8), (4, 7), (6, 7), (8, 9)], fill=light)
    draw.polygon([(15, 5), (18, 5), (21, 8), (21, 14), (17, 17), (13, 11)], fill=mid)
    draw.polygon([(17, 6), (19, 7), (20, 9), (20, 12), (17, 13), (15, 9)], fill=light)
    draw.point((19, 8), fill=glow)
    draw.polygon([(8, 12), (14, 12), (18, 17), (16, 20), (13, 21), (8, 20), (4, 17)], fill=light)
    draw.polygon([(6, 17), (10, 18), (14, 17), (17, 17), (15, 20), (10, 20)], fill=mid)
    draw.line([(8, 14), (7, 16), (9, 17)], fill=glow)
    draw.polygon([(8, 7), (13, 6), (17, 9), (16, 14), (12, 17), (7, 14), (5, 10)], fill=shade)
    draw.polygon([(9, 8), (13, 7), (15, 9), (15, 12), (12, 15), (8, 13), (7, 10)], fill=mid)
    draw.line([(8, 10), (10, 8), (13, 8), (14, 10)], fill=glow)
    draw.line([(8, 11), (9, 13), (12, 14), (14, 12)], fill=light)
    draw.polygon([(10, 10), (13, 10), (13, 12), (11, 13), (9, 11)], fill=shade)
    draw.line((10, 10, 12, 10), fill=light)
    draw.point((12, 11), fill=glow)
    return bloom


def daisy_bloom(colors=APRICOT_COLORS) -> Image.Image:
    """Eight distinct 22px petals around a textured honey-gold centre."""
    shade, mid, light, glow = colors
    bloom = Image.new("RGBA", (22, 22), TRANSPARENT)
    draw = ImageDraw.Draw(bloom)
    petals = [
        [(8, 0), (12, 0), (14, 4), (12, 9), (9, 9), (7, 4)],
        [(15, 2), (18, 2), (20, 5), (18, 9), (13, 11), (11, 8)],
        [(17, 7), (20, 8), (21, 11), (19, 14), (14, 14), (11, 11)],
        [(17, 13), (20, 16), (19, 19), (16, 20), (12, 17), (11, 12)],
        [(8, 13), (12, 12), (15, 17), (13, 21), (9, 21), (7, 18)],
        [(6, 12), (10, 14), (9, 18), (5, 20), (2, 17), (3, 14)],
        [(5, 7), (10, 10), (8, 13), (3, 14), (0, 11), (1, 8)],
        [(4, 2), (7, 2), (11, 7), (8, 11), (4, 9), (1, 6)],
    ]
    for index, points in enumerate(petals):
        draw.polygon(points, fill=light if index < 3 or index == 7 else mid, outline=INK)
    for points in (
        [(9, 2), (11, 2), (12, 5), (11, 8), (9, 6)],
        [(16, 4), (18, 4), (18, 6), (15, 8), (14, 7)],
        [(18, 9), (19, 10), (18, 12), (15, 12), (14, 10)],
        [(5, 4), (7, 4), (9, 7), (7, 8), (4, 6)],
        [(2, 9), (4, 9), (7, 11), (5, 12), (2, 11)],
    ):
        draw.polygon(points, fill=glow)
    draw.line((16, 15, 18, 17), fill=light)
    draw.line((10, 17, 11, 19), fill=light)
    draw.line((5, 16, 4, 17), fill=light)
    draw.polygon([(8, 7), (12, 7), (15, 10), (14, 13), (11, 15), (8, 14), (6, 11)], fill=shade, outline=INK)
    draw.polygon([(9, 8), (12, 8), (14, 10), (12, 13), (9, 13), (7, 11)], fill=GOLD)
    draw.rectangle((9, 9, 11, 10), fill=GOLD_LIGHT)
    draw.point((9, 9), fill=PAPER_BRIGHT)
    draw.point((12, 11), fill=LEATHER_LIGHT)
    return bloom


def little_flower(colors=LILAC_COLORS) -> Image.Image:
    shade, mid, light, glow = colors
    sprite = Image.new("RGBA", (13, 13), TRANSPARENT)
    draw = ImageDraw.Draw(sprite)
    draw.polygon([(4, 0), (7, 0), (8, 3), (11, 2), (12, 5), (10, 7), (11, 10), (8, 12), (6, 10), (3, 12), (1, 9), (3, 7), (0, 5), (1, 2), (4, 3)], fill=mid, outline=INK)
    draw.polygon([(5, 1), (6, 1), (7, 4), (5, 5), (4, 3)], fill=glow)
    draw.polygon([(9, 3), (10, 3), (11, 5), (8, 6), (7, 5)], fill=light)
    draw.polygon([(2, 3), (3, 4), (5, 5), (4, 7), (1, 5)], fill=light)
    draw.polygon([(4, 8), (6, 7), (8, 9), (7, 10), (5, 9), (3, 10)], fill=shade)
    draw.rectangle((5, 5, 7, 7), fill=GOLD)
    draw.point((5, 5), fill=PAPER_BRIGHT)
    return sprite


def rose_bud(colors=ROSE_COLORS) -> Image.Image:
    shade, mid, light, glow = colors
    sprite = Image.new("RGBA", (9, 13), TRANSPARENT)
    draw = ImageDraw.Draw(sprite)
    draw.polygon([(3, 0), (6, 0), (8, 3), (7, 7), (5, 10), (2, 9), (0, 5), (1, 2)], fill=shade, outline=INK)
    draw.polygon([(3, 1), (5, 1), (6, 4), (5, 7), (3, 8), (1, 5)], fill=mid)
    draw.line([(3, 2), (4, 2), (5, 4), (4, 6)], fill=light)
    draw.point((3, 2), fill=glow)
    draw.polygon([(1, 6), (4, 9), (7, 6), (6, 10), (4, 11), (4, 12), (3, 12), (3, 10)], fill=LEAF_MID, outline=INK)
    draw.point((4, 10), fill=LEAF_GLOW)
    return sprite


def stem_paths(sprite: Image.Image, paths: list[list[tuple[int, int]]]) -> None:
    draw = ImageDraw.Draw(sprite)
    for points in paths:
        draw.line(points, fill=INK, width=3)
        draw.line(points, fill=LEAF_DARK, width=2)
        draw.line([(x - 1, y) for x, y in points], fill=LEAF_LIGHT, width=1)


def place_leaf(sprite: Image.Image, x: int, y: int, *, flip: bool = False, turn: bool = False, small: bool = False) -> None:
    leaf = botanical_leaf()
    if small:
        leaf = leaf.resize((13, 9), Image.Resampling.NEAREST)
    if flip:
        leaf = leaf.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    if turn:
        leaf = leaf.transpose(Image.Transpose.ROTATE_90)
    sprite.alpha_composite(leaf, (x, y))


def floral_corner() -> Image.Image:
    """96px floral corner: a focal rose, secondary daisies, buds and layered leaves."""
    sprite = Image.new("RGBA", (96, 96), TRANSPARENT)
    stem_paths(sprite, [
        [(12, 94), (15, 83), (12, 70), (16, 55), (14, 44), (19, 32), (28, 23), (42, 17), (60, 17), (74, 13), (92, 11)],
        [(22, 30), (13, 20), (11, 9), (4, 5)],
        [(27, 25), (39, 31), (52, 29), (58, 25)],
        [(17, 45), (28, 48), (32, 43), (30, 39)],
        [(16, 60), (25, 63), (29, 70)],
        [(14, 78), (6, 81), (5, 87), (8, 90)],
        [(65, 16), (70, 6), (77, 4)],
        [(80, 13), (87, 21), (91, 24)],
    ])
    for x, y, flip, turn in (
        (1, 12, False, False), (19, 1, False, False), (28, 26, True, False),
        (1, 29, True, True), (18, 33, False, False), (1, 53, True, False),
        (17, 52, False, True), (0, 71, True, True), (14, 76, False, False),
        (43, 0, False, False), (50, 22, True, False), (65, 0, False, False),
        (74, 21, True, False), (77, 0, False, False),
    ):
        place_leaf(sprite, x, y, flip=flip, turn=turn)
    place_leaf(sprite, 4, 84, flip=True, small=True)
    place_leaf(sprite, 24, 69, small=True)
    sprite.alpha_composite(rose_bloom(), (12, 10))
    sprite.alpha_composite(daisy_bloom(), (35, 5))
    sprite.alpha_composite(little_flower(), (59, 10))
    sprite.alpha_composite(little_flower(APRICOT_COLORS), (80, 3))
    sprite.alpha_composite(rose_bud(), (84, 19))
    sprite.alpha_composite(rose_bud(LILAC_COLORS), (3, 1))
    sprite.alpha_composite(daisy_bloom(LILAC_COLORS), (2, 36))
    sprite.alpha_composite(little_flower(APRICOT_COLORS), (21, 61))
    sprite.alpha_composite(rose_bloom(APRICOT_COLORS).resize((17, 17), Image.Resampling.NEAREST), (1, 70))
    sprite.alpha_composite(rose_bud(), (10, 83))
    return sprite


def hanging_vine() -> Image.Image:
    sprite = Image.new("RGBA", (40, 112), TRANSPARENT)
    stem_paths(sprite, [
        [(19, 0), (21, 15), (17, 29), (22, 44), (17, 61), (23, 75), (18, 91), (21, 105), (17, 111)],
        [(19, 18), (8, 21), (4, 28)],
        [(19, 33), (31, 32), (35, 39)],
        [(20, 53), (9, 56), (4, 65)],
        [(19, 66), (31, 67), (35, 73)],
        [(20, 84), (9, 87), (7, 94), (10, 98), (13, 96)],
    ])
    for x, y, flip, turn in (
        (0, 3, True, False), (19, 10, False, False), (0, 27, True, False),
        (19, 36, False, False), (1, 50, True, False), (20, 62, False, False),
        (1, 73, True, False), (20, 86, False, False),
    ):
        place_leaf(sprite, x, y, flip=flip, turn=turn)
    place_leaf(sprite, 9, 96, flip=True, small=True)
    sprite.alpha_composite(little_flower(APRICOT_COLORS), (13, 17))
    sprite.alpha_composite(rose_bud(), (27, 37))
    sprite.alpha_composite(little_flower(), (7, 54))
    sprite.alpha_composite(rose_bud(APRICOT_COLORS), (20, 77))
    return sprite


def flower_spray() -> Image.Image:
    sprite = Image.new("RGBA", (64, 48), TRANSPARENT)
    stem_paths(sprite, [
        [(4, 43), (17, 32), (28, 25), (42, 23), (59, 9)],
        [(18, 32), (13, 18), (5, 11)],
        [(29, 25), (30, 12), (38, 3)],
        [(37, 24), (46, 36), (60, 39)],
    ])
    for x, y, flip, turn in (
        (2, 15, True, False), (10, 28, True, False), (23, 1, False, False),
        (35, 29, True, False), (43, 3, False, False), (44, 24, False, False),
    ):
        place_leaf(sprite, x, y, flip=flip, turn=turn)
    place_leaf(sprite, 2, 34, small=True)
    sprite.alpha_composite(rose_bloom(), (16, 12))
    sprite.alpha_composite(daisy_bloom(), (34, 5))
    sprite.alpha_composite(little_flower(), (5, 24))
    sprite.alpha_composite(little_flower(APRICOT_COLORS), (45, 31))
    sprite.alpha_composite(rose_bud(LILAC_COLORS), (4, 6))
    return sprite


def large_butterfly(*, warm: bool = False) -> Image.Image:
    """New 40x32 wing silhouettes with veins, scallops and contrasting gold marks."""
    if warm:
        shade, mid, light, glow = ROSE_COLORS
    else:
        shade, mid, light, glow = (38, 73, 104, 255), (66, 124, 158, 255), (123, 183, 194, 255), (190, 220, 209, 255)
    left = Image.new("RGBA", (40, 32), TRANSPARENT)
    draw = ImageDraw.Draw(left)
    draw.polygon([(18, 14), (15, 6), (9, 2), (5, 2), (2, 5), (2, 10), (5, 15), (9, 18), (16, 20)], fill=shade, outline=INK)
    draw.polygon([(17, 17), (9, 16), (5, 20), (6, 25), (10, 28), (14, 27), (18, 22)], fill=shade, outline=INK)
    draw.polygon([(4, 5), (6, 3), (10, 4), (14, 7), (16, 12), (17, 16), (11, 15), (7, 12), (4, 9)], fill=mid)
    draw.polygon([(5, 4), (8, 4), (11, 6), (13, 9), (9, 10), (6, 8)], fill=light)
    draw.line([(6, 4), (8, 4), (10, 6)], fill=glow)
    draw.polygon([(12, 9), (14, 11), (16, 16), (14, 16), (11, 13), (10, 11)], fill=GOLD)
    draw.line([(11, 11), (13, 12), (15, 15)], fill=GOLD_LIGHT)
    draw.line([(17, 17), (11, 14), (7, 8)], fill=shade)
    draw.line([(11, 14), (5, 11)], fill=shade)
    draw.polygon([(8, 18), (13, 18), (16, 20), (14, 24), (11, 26), (8, 24)], fill=mid)
    draw.polygon([(8, 19), (11, 18), (13, 20), (11, 23), (8, 23)], fill=light)
    draw.rectangle((10, 21, 12, 23), fill=GOLD_LIGHT)
    draw.point((10, 21), fill=PAPER_BRIGHT)
    for px, py in ((3, 6), (4, 10), (6, 14), (7, 23), (10, 27)):
        draw.point((px, py), fill=glow)
    sprite = left.copy()
    sprite.alpha_composite(left.transpose(Image.Transpose.FLIP_LEFT_RIGHT))
    draw = ImageDraw.Draw(sprite)
    draw.line([(18, 10), (17, 7), (15, 5), (13, 4)], fill=INK)
    draw.line([(21, 10), (22, 7), (24, 5), (26, 4)], fill=INK)
    draw.point((13, 3), fill=GOLD_LIGHT)
    draw.point((26, 3), fill=GOLD_LIGHT)
    draw.rectangle((19, 10, 20, 25), fill=INK)
    draw.rectangle((18, 9, 21, 12), fill=INK)
    draw.point((19, 10), fill=GOLD_LIGHT)
    draw.line((19, 14, 19, 21), fill=LEATHER_LIGHT)
    draw.point((19, 17), fill=GOLD)
    return sprite


def main() -> None:
    image = Image.new("RGBA", (256, 512), TRANSPARENT)
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

    # Honey-gold ribbon; the former white cloth plate and its dot stitches are gone.
    title = [(0, 8), (6, 2), (14, 2), (14, 0), (81, 0), (81, 2), (89, 2), (95, 8), (89, 18), (81, 18), (81, 19), (14, 19), (14, 18), (6, 18)]
    draw.polygon([(x, y + 24) for x, y in title], fill=INK)
    inner = [(2, 8), (7, 4), (16, 4), (16, 2), (79, 2), (79, 4), (88, 4), (93, 8), (88, 16), (79, 16), (79, 17), (16, 17), (16, 16), (7, 16)]
    draw.polygon([(x, y + 24) for x, y in inner], fill=GOLD)
    draw.rectangle((16, 27, 79, 40), fill=GOLD_LIGHT)
    draw.line((17, 27, 78, 27), fill=PAPER)
    draw.line((17, 40, 78, 40), fill=LEATHER_LIGHT)

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

    # The sealed note now identifies the relationship memories; the promise knot
    # keeps both its existing source pixels and tile coordinates.
    paint_reviewed_icon(image, 80, 80, "sealed-note")

    # New artwork occupies previously unused tiles or rows below the old atlas.
    # Preserve the old frame/icon coordinates for all callers and fallback skins.
    paint_title(image, 1, 105, "记忆手册", chinese=True)
    paint_title(image, 84, 107, "MEMORY BOOK", chinese=False)
    draw_vine_corner(image, 0, 128)
    paint_map(
        image,
        50,
        130,
        BUTTERFLY,
        {"1": INK, "2": TALK_BLUE, "3": TALK_LIGHT, "4": GOLD_LIGHT},
    )

    # The new wordmarks are separate, transparent sprites. The menu does not draw
    # the legacy ribbon behind them; its source tile stays intact for compatibility.
    title_source = ROOT / "docs" / "design" / "memory-book-floral-refresh"
    paint_reviewed_art(image, 0, 192, title_source / "title-zh.json", (104, 28))
    paint_reviewed_art(image, 0, 224, title_source / "title-en.json", (176, 28))

    # Generous floral assets use new native detail rather than enlarging the former
    # 40px corner. Each source rectangle remains independently composable.
    image.alpha_composite(floral_corner(), (0, 256))
    image.alpha_composite(hanging_vine(), (96, 256))
    image.alpha_composite(flower_spray(), (144, 256))
    image.alpha_composite(large_butterfly(), (208, 256))
    image.alpha_composite(large_butterfly(warm=True), (208, 288))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUTPUT, format="PNG", optimize=False)
    print(f"Wrote {OUTPUT} ({image.width}x{image.height}, RGBA)")


if __name__ == "__main__":
    main()
