#!/usr/bin/env python3
"""Dump palette-indexed pixel maps (wave3/4 brief format, wave-5 grouped-palette scheme) for
wave-5 candidate sprites. Unlike wave4's dump_pixelmap.py (which crashes past 62 unique
colors), this uses palette_lib.build_letter_map() so ANY sprite — including >62-color ones
like lantern-on — renders as a <=62-symbol map. Groups with more than one exact color are
flagged inline (e.g. "x37 [12 shades]") so the designer (grk) knows that letter is a
perceptual band, not a single flat color — matters for the RUNTIME TINTING note in the brief
brief text, since a "grouped" letter still needs the same tint-safety discipline as a
singleton one."""
import os, sys, json
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palette_lib import build_letter_map, DIR_ORDER

RSI = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims/Resources/Textures")


def render_tile(tile, color_to_letter):
    lines = []
    for y in range(32):
        row = []
        for x in range(32):
            px = tile.getpixel((x, y))
            if px[3] == 0:
                row.append(".")
            else:
                row.append(color_to_letter[px[:3]])
        lines.append(f"{y:2d} {''.join(row)}")
    return "\n".join(lines)


def dump(rsi_rel, state, per_direction=False, label=None):
    png = f"{RSI}/{rsi_rel}/{state}.png"
    im = Image.open(png).convert("RGBA")
    tile0 = im.crop((0, 0, 32, 32))
    letter_to_colors, color_to_letter, repr_color, cnt = build_letter_map(tile0, max_groups=62)
    print(f"##### {label or (rsi_rel + '.rsi' if not rsi_rel.endswith('.rsi') else rsi_rel)} state '{state}'")
    n_unique = len(cnt)
    n_groups = len(letter_to_colors)
    if n_groups < n_unique:
        print(f"PALETTE ({n_unique} exact colors grouped into {n_groups} perceptual bands — "
              f"letter = representative rgb, total count, [n shades] if grouped):")
    else:
        print("PALETTE (letter = rgb, count):")
    for letter, colors in letter_to_colors.items():
        total = sum(cnt[c] for c in colors)
        rep = repr_color[letter]
        tag = f" [{len(colors)} shades]" if len(colors) > 1 else ""
        print(f"  {letter} = rgb{rep} x{total}{tag}")
    if per_direction:
        cols = im.width // 32
        rows = im.height // 32
        for d in range(4):
            tx, ty = d % cols, d // cols
            tile = im.crop((tx*32, ty*32, tx*32+32, ty*32+32))
            print(f"TILE {DIR_ORDER[d]}:")
            print(render_tile(tile, color_to_letter))
    else:
        print("TILE 0:")
        print(render_tile(tile0, color_to_letter))
    print()


if __name__ == "__main__":
    targets = json.loads(sys.argv[1])
    for t in targets:
        dump(t["rsi"], t["state"], t.get("per_direction", False), t.get("label"))
