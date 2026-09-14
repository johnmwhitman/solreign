#!/usr/bin/env python3
"""Build wave5-review-montage.png — one row per sprite, first column = frame 0 = the original."""
import json, os, sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from execute_spec import resolve_png

HERE = os.path.dirname(os.path.abspath(__file__))
TILE = 32
SCALE = 4
MAXF = 4
rows = []
labels = []
for batch in ["batch1.clean.json", "batch2.clean.json"]:
    spec = json.load(open(f"{HERE}/{batch}"))
    for s in spec["sprites"]:
        rsi, state = s["rsi"], s["state"]
        png = resolve_png(s)
        im = Image.open(png).convert("RGBA")
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

montage.save(f"{HERE}/wave5-review-montage.png")
print("labels (row order, first column = frame 0 = original):")
for l in labels:
    print(" ", l)
print("saved", f"{HERE}/wave5-review-montage.png", montage.size)
