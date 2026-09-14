#!/usr/bin/env python3
"""Dump palette-indexed pixel maps (wave3 brief format) for wave-4 candidate sprites."""
import collections, os, sys
from PIL import Image

RSI = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims/Resources/Textures")
LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*+="
DIR_ORDER = ["S", "N", "E", "W"]

def build_palette(im):
    cnt = collections.Counter()
    for y in range(im.height):
        for x in range(im.width):
            px = im.getpixel((x, y))
            if px[3] > 0:
                cnt[px[:3]] += 1
    return {LETTERS[i]: c for i, (c, n) in enumerate(cnt.most_common())}, cnt

def render_tile(tile, pal_by_color):
    lines = []
    for y in range(32):
        row = []
        for x in range(32):
            px = tile.getpixel((x, y))
            if px[3] == 0:
                row.append(".")
            else:
                row.append(pal_by_color[px[:3]])
        lines.append(f"{y:2d} {''.join(row)}")
    return "\n".join(lines)

def dump(rsi_rel, state, per_direction=False, label=None):
    png = f"{RSI}/{rsi_rel}/{state}.png"
    im = Image.open(png).convert("RGBA")
    pal, cnt = build_palette(im)
    pal_by_color = {v: k for k, v in pal.items()}
    print(f"##### {label or (rsi_rel + '.rsi' if not rsi_rel.endswith('.rsi') else rsi_rel)} state '{state}'")
    print("PALETTE (letter = rgb, count):")
    for letter, color in pal.items():
        print(f"  {letter} = rgb{color} x{cnt[color]}")
    if per_direction:
        cols = im.width // 32
        rows = im.height // 32
        for d in range(4):
            tx, ty = d % cols, d // cols
            tile = im.crop((tx*32, ty*32, tx*32+32, ty*32+32))
            print(f"TILE {DIR_ORDER[d]}:")
            print(render_tile(tile, pal_by_color))
    else:
        tile = im.crop((0, 0, 32, 32))
        print("TILE 0:")
        print(render_tile(tile, pal_by_color))
    print()

if __name__ == "__main__":
    import json
    targets = json.loads(sys.argv[1])
    for t in targets:
        dump(t["rsi"], t["state"], t.get("per_direction", False), t.get("label"))
