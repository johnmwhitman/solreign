#!/usr/bin/env python3
"""Build wave4-review-montage.png — one row per sprite, first column = frame 0 = the original."""
import json, os
from PIL import Image

TEXTURES = "/Users/johnwhitman/AI/solreign-trees/sprite-idle-anims/Resources/Textures"
HERE = os.path.dirname(os.path.abspath(__file__))
RSI_PATHS = {
    "crystal_grey": "Structures/Decoration/crystal.rsi",
    "telecrystal": "Objects/Specific/Syndicate/telecrystal.rsi",
    "crystal_shard1": "Objects/Materials/Shards/crystal.rsi",
    "crystal_shard2": "Objects/Materials/Shards/crystal.rsi",
    "crystal_shard3": "Objects/Materials/Shards/crystal.rsi",
    "oracle_screen": "Structures/Wallmounts/screen.rsi",
    "glowstick_lit": "Objects/Misc/glowstick.rsi",
    "flashlight_overlay": "Objects/Tools/flashlight.rsi",
    "carp_statue_eyes": "Structures/Specific/carp_statue.rsi",
    "glowstick_glow": "Objects/Misc/glowstick.rsi",
    "carp_statue_teeth": "Structures/Specific/carp_statue.rsi",
}

TILE = 32
SCALE = 4
MAXF = 4
rows = []
labels = []
for batch in ["batch1.clean.json", "batch2.clean.json", "companions.clean.json"]:
    spec = json.load(open(f"{HERE}/{batch}"))
    for s in spec["sprites"]:
        rsi, state = s["rsi"], s["state"]
        rel = RSI_PATHS[rsi]
        im = Image.open(f"{TEXTURES}/{rel}/{state}.png").convert("RGBA")
        n = im.width // TILE
        frames = [im.crop((i*TILE, 0, i*TILE+TILE, TILE)) for i in range(n)]
        rows.append(frames)
        labels.append(f"{rsi}/{state} ({n}f)")

cell = TILE * SCALE
cols = MAXF
img_w = cols * cell
img_h = len(rows) * cell
montage = Image.new("RGBA", (img_w, img_h), (30, 30, 34, 255))
for ri, frames in enumerate(rows):
    for fi, f in enumerate(frames):
        big = f.resize((cell, cell), Image.NEAREST)
        montage.paste(big, (fi*cell, ri*cell), big)

montage.save(f"{HERE}/wave4-review-montage.png")
print("labels (row order, first column = frame 0 = original):")
for l in labels:
    print(" ", l)
print("saved", f"{HERE}/wave4-review-montage.png", montage.size)
