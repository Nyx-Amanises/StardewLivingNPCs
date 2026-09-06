#!/usr/bin/env python3
"""Render review-only promise icon drafts; never updates the game's atlas."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
INK = (74, 37, 24, 255)
MUTED = (118, 85, 60, 255)
PAPER = (243, 212, 138, 255)
PAPER_BRIGHT = (255, 241, 201, 255)
GOLD = (231, 154, 69, 255)
GOLD_LIGHT = (249, 190, 91, 255)
RED = (157, 62, 58, 255)
NEAREST = Image.Resampling.NEAREST

OPTIONS = (
    ("sealed-note", "A", "封蜡约定笺", "把说好的事，认真记下来。"),
    ("knot", "B", "红绳约定结", "用一个结，记住彼此的约定。"),
    ("handshake", "C", "握手约定", "两个人，说定一件事。"),
)


def load_icon(stem: str) -> Image.Image:
    spec = json.loads((HERE / f"{stem}.json").read_text(encoding="utf-8-sig"))
    width, height = spec["width"], spec["height"]
    if (width, height) != (16, 16):
        raise ValueError(f"{stem}: expected a 16x16 tile")
    if len(spec["rows"]) != height or any(len(row) != width for row in spec["rows"]):
        raise ValueError(f"{stem}: pixel map dimensions do not match")
    icon = Image.new("RGBA", (width, height))
    for y, row in enumerate(spec["rows"]):
        for x, symbol in enumerate(row):
            color = tuple(spec["palette"][symbol])
            if len(color) != 4 or color[3] not in (0, 255):
                raise ValueError(f"{stem}: use fully opaque or transparent RGBA pixels")
            icon.putpixel((x, y), color)
    icon.save(HERE / f"{stem}-16.png", optimize=False)
    return icon


def frame(atlas: Image.Image, source_x: int, size: tuple[int, int]) -> Image.Image:
    """Reproduce the book's 18x18 nine-slice at its usual 2x border scale."""
    source = atlas.crop((source_x, 0, source_x + 18, 18))
    width, height = size
    result = Image.new("RGBA", size)
    source_cuts = (0, 6, 12, 18)
    x_cuts, y_cuts = (0, 12, width - 12, width), (0, 12, height - 12, height)
    for row in range(3):
        for col in range(3):
            tile = source.crop((source_cuts[col], source_cuts[row], source_cuts[col + 1], source_cuts[row + 1]))
            tile = tile.resize((x_cuts[col + 1] - x_cuts[col], y_cuts[row + 1] - y_cuts[row]), NEAREST)
            result.alpha_composite(tile, (x_cuts[col], y_cuts[row]))
    return result


def render_board(icons: list[Image.Image], font_path: Path) -> None:
    atlas = Image.open(ROOT / "LivingNPCs/assets/ui/memory-book.png").convert("RGBA")
    board = Image.new("RGBA", (1200, 720), (248, 229, 181, 255))
    draw = ImageDraw.Draw(board)

    def text(position: tuple[int, int], value: str, size: int, color: tuple[int, ...] = INK, anchor: str = "lt") -> None:
        font = ImageFont.truetype(str(font_path), size)
        draw.text(position, value, font=font, fill=color, anchor=anchor)

    draw.rectangle((0, 0, 1199, 9), fill=INK)
    draw.rectangle((0, 10, 1199, 15), fill=GOLD)
    text((48, 45), "「约定」图标提案", 36)
    text((50, 101), "沿用记忆手册的像素轮廓、深棕描边与暖色调", 20, MUTED)
    text((1148, 56), "设计预览", 19, MUTED, "rt")

    card_width, gap, top, height = 352, 24, 157, 445
    for index, ((stem, letter, title, description), icon) in enumerate(zip(OPTIONS, icons, strict=True)):
        x = 48 + index * (card_width + gap)
        board.alpha_composite(frame(atlas, 120, (card_width, height)), (x, top))
        if index == 0:
            draw.rectangle((x + 3, top + 3, x + card_width - 4, top + 6), fill=RED)
        text((x + 24, top + 26), letter, 27, RED if index == 0 else MUTED)
        text((x + 58, top + 31), title, 23)
        if index == 0:
            draw.rectangle((x + 261, top + 28, x + 327, top + 57), fill=RED)
            text((x + 294, top + 34), "推荐", 16, PAPER_BRIGHT, "mt")

        # Enlarged nearest-neighbor artwork on the same gold as the header.
        icon_tile = Image.new("RGBA", (144, 144), GOLD_LIGHT)
        icon_tile.alpha_composite(icon.resize((128, 128), NEAREST), (8, 8))
        board.alpha_composite(icon_tile, (x + 104, top + 97))
        text((x + 176, top + 253), "放大细节", 16, MUTED, "mt")
        text((x + 176, top + 299), description, 18, INK, "mt")

        # Same 32px icon slot and offsets used by the memory-book section headers.
        header_x, header_y = x + 22, top + 353
        board.alpha_composite(frame(atlas, 180, (308, 42)), (header_x, header_y))
        board.alpha_composite(icon.resize((32, 32), NEAREST), (header_x + 8, header_y + 3))
        text((header_x + 48, header_y + 7), "约定", 24)
        text((x + 176, top + 411), "标题栏效果", 16, MUTED, "mt")

    draw.line((50, 636, 1150, 636), fill=(215, 168, 95, 255), width=1)
    original = atlas.crop((32, 56, 48, 72)).resize((32, 32), NEAREST)
    text((51, 665), "当前图标", 18, MUTED, "lm")
    board.alpha_composite(original, (141, 649))
    text((190, 665), "通用书本", 18, MUTED, "lm")
    text((1150, 665), "A 偏手册记录    ·    B 偏情感牵系    ·    C 偏双方承诺", 18, MUTED, "rm")
    board.convert("RGB").save(HERE / "comparison.png", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--font", type=Path, default=Path("C:/Windows/Fonts/msyhbd.ttc"), help="Chinese TrueType/OpenType font for the comparison board")
    args = parser.parse_args()
    icons = [load_icon(stem) for stem, *_ in OPTIONS]
    render_board(icons, args.font)
    print(f"Rendered {len(icons)} draft icons and {HERE / 'comparison.png'}")


if __name__ == "__main__":
    main()
