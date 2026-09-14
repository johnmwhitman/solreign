#!/usr/bin/env python3
"""Post-process MEDIUM mob generations -> 32x32 candidates + curation grids.
Reuses the pilot's process_sprite.py (channel-max anchor, flood-fill key, despeckle),
plus the batch-1 interior key sweep."""
import os, sys
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot"))
from process_sprite import process, sample_bg_color

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "medium-gen")

def key_sweep(img, bg, tol=52):
    """Batch-1 finding 3: enclosed key-family pixels survive border flood fill."""
    out = img.copy()
    br, bg_, bb = bg[:3]
    mx = max(br, bg_, bb) or 1
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = img.getpixel((x, y))
            if a == 0: continue
            # same hue family as key (dominant channel matches and distance small after scale)
            s = max(r, g, b) / mx if mx else 1
            if ((r - br*s)**2 + (g - bg_*s)**2 + (b - bb*s)**2) ** 0.5 < tol and \
               (r, g, b).index(max(r, g, b)) == (br, bg_, bb).index(max(br, bg_, bb)):
                out.putpixel((x, y), (0, 0, 0, 0))
    return out

grids = {}
for asset in sorted(os.listdir(BASE)):
    adir = f"{BASE}/{asset}"
    if not os.path.isdir(adir): continue
    for f in sorted(os.listdir(adir)):
        if not f.endswith("_raw.png"): continue
        raw = f"{adir}/{f}"
        outp = raw.replace("_raw.png", "_32.png")
        if not os.path.exists(outp):
            raw_img = Image.open(raw).convert("RGBA")
            bg = sample_bg_color(raw_img)
            im = process(raw, size=32, colors=20, tol=60, resample="box")
            im = key_sweep(im, bg)
            im.save(outp)
        grids.setdefault(asset, []).append((f.replace("_raw.png", ""), Image.open(outp).convert("RGBA")))

from PIL import ImageDraw
for asset, cells in grids.items():
    row = Image.new("RGBA", (len(cells)*70 + 4, 88), (28, 28, 32, 255))
    dr = ImageDraw.Draw(row)
    for i, (label, im) in enumerate(cells):
        row.paste(im.resize((64, 64), Image.NEAREST), (4 + i*70, 2))
        dr.text((4 + i*70, 70), label[:11], fill=(210, 210, 210, 255))
    row.save(f"{BASE}/_curation_{asset}.png")
    print(asset, len(cells), "candidates")
print("done")
