#!/usr/bin/env python3
"""Wave 4: ore.rsi — 10 ore types, each icon + 8 rigged inhand frames = 90 frames
from 10 generations. State names are per-type prefixed (`gold-inhand-left`)."""
import json, os, sys
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot"))
from process_sprite import process, sample_bg_color
sys.path.insert(0, SCR)
from process_medium import key_sweep
from inhand_rig import rig

GEN = f"{SCR}/medium-gen/ore"
OUT = f"{SCR}/w4-proc/ore.rsi"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
REL = "Objects/Materials/ore.rsi"
COPY = ("AI-generated ore icons (MiniMax image-01) for Solreign, human-curated; inhand states "
        "derived procedurally from those icons; replaces CC-BY-NC art")

PICKS = {"bananium": "bananium_v2", "gold": "gold_v1", "iron": "iron_v1", "uranium": "uranium_v1",
         "plasma": "plasma_v1", "spacequartz": "spacequartz_v1", "silver": "silver_v2",
         "coal": "coal_v1", "salt": "salt_v3", "diamond": "diamond_v1"}

def strip_teal(im):
    o = im.copy()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = im.getpixel((x, y))
            if a and g > r*1.35 and b > r*1.35 and abs(g-b) < 25:
                o.putpixel((x, y), (0, 0, 0, 0))
    return o

def to32(p):
    bg = sample_bg_color(Image.open(p).convert("RGBA"))
    return strip_teal(key_sweep(process(p, size=32, colors=20, tol=60, resample="box"), bg, tol=35))

def main():
    ometa = json.load(open(f"{REPO}/Resources/Textures/{REL}/meta.json", encoding="utf-8-sig"))
    want = {s["name"]: s for s in ometa["states"]}
    os.makedirs(OUT, exist_ok=True)
    states, preview, missing = [], [], []
    for ore, pick in PICKS.items():
        raw = f"{GEN}/{pick}_raw.png"
        if not os.path.exists(raw):
            missing.append(ore); continue
        icon = to32(raw)
        icon.save(f"{OUT}/{ore}.png")
        states.append({"name": ore})
        preview.append((ore, icon))
        frames = rig(icon, rotate=0, grow=2.0)   # a rock pile has no "up" — no rotation
        for hand, im in frames.items():
            st = f"{ore}-{hand}"
            assert st in want, f"unexpected state {st}"
            im.save(f"{OUT}/{st}.png")
            states.append({"name": st, "directions": 4})
    # order states exactly as the original declares them
    order = [s["name"] for s in ometa["states"]]
    states.sort(key=lambda s: order.index(s["name"]))
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY,
            "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{OUT}/meta.json", "w"), indent=2); open(f"{OUT}/meta.json", "a").write("\n")
    have = {s["name"] for s in states}
    gap = [o for o in order if o not in have]
    print(f"ore: {len(states)}/{len(order)} states built" + (f"  MISSING={gap}" if gap else "  COMPLETE"))
    sheet = Image.new("RGBA", (len(preview)*84+4, 104), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (l, c) in enumerate(preview):
        r = c.resize((76, 76), Image.NEAREST); sheet.paste(r, (4+i*84, 4), r)
        dr.text((4+i*84, 84), l[:11], fill=(225, 225, 225, 255))
    sheet.save(f"{SCR}/w4-ore-preview.png")

main()
