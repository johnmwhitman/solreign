#!/usr/bin/env python3
"""QUALITY GATE: report resolved mask sizes for each wave-5 spec, to catch near-zero/fabricated
masks before execution (wave-3/4 lesson: a 1-px mask that claims to be a whole feature is a
redesign signal). Wave-5 variant: uses palette_lib's grouped-letter scheme so this works on
>62-color sprites (lantern-on) too, not just the <=62-color case wave4's qc_masks.py handled."""
import json, sys, os
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from palette_lib import build_letter_map, resolve_mask
from execute_spec import RSI_PATHS, resolve_png

TEXTURES = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims/Resources/Textures")

for batch in sys.argv[1:]:
    spec = json.load(open(batch))
    for s in spec["sprites"]:
        if s.get("verdict") == "skip":
            continue
        rsi, state = s["rsi"], s["state"]
        png = resolve_png(s)
        im = Image.open(png).convert("RGBA")
        tile = im.crop((0, 0, 32, 32))
        letter_to_colors, _, _, _ = build_letter_map(tile)
        opaque = sum(1 for y in range(32) for x in range(32) if tile.getpixel((x, y))[3] > 0)
        print(f"--- {rsi}/{state} (opaque px total: {opaque}) ---")
        for name, m in s.get("masks", {}).items():
            px = resolve_mask(tile, m, letter_to_colors)
            flag = " <<< SUSPICIOUSLY SMALL" if len(px) <= 1 and "pixels" not in m else ""
            print(f"  mask {name}: {len(px)} px{flag}  spec={m}")
