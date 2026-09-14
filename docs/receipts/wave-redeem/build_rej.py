#!/usr/bin/env python3
"""Redeem two of the three sprites rejected for bad art: lamp + capgun.
powersink is REJECTED AGAIN (3rd independent failure) — see receipt.

Dual gate applied: opaque-pixel ratio >= 0.55 AND a semantic look ("is this the
right object?"). Ratio alone is blind to wrong-object failures; eyes alone miss thin.
"""
import json, os, sys
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot"))
from process_sprite import process, sample_bg_color
sys.path.insert(0, SCR)
from process_medium import key_sweep
from inhand_rig import rig

GEN = f"{SCR}/medium-gen"
OUT = f"{SCR}/rej-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/regen-final")
COPY = ("AI-generated (MiniMax image-01) for Solreign, human-curated under the ratio+semantic "
        "dual gate; inhand and layer states derived procedurally; replaces CC-BY-NC art")

PICKS = {
    "lamp":   dict(rel="Objects/Fun/Plushies/lamp.rsi", raw=f"{GEN}/rej_lamp/icon_v2_raw.png",
                   icon="icon", rot=0, grow=2.2),
    "capgun": dict(rel="Objects/Fun/capgun.rsi", raw=f"{GEN}/rej_capgun/icon_v3_raw.png",
                   icon="icon", rot=0, grow=2.2),
}

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

def opq(im):
    im = im.convert("RGBA").crop((0, 0, 32, 32))
    return sum(1 for y in range(32) for x in range(32) if im.getpixel((x, y))[3] > 0)

def hammer_markers(gun):
    """capgun bolt-open/bolt-closed are 2-3px hammer markers. The original places them at
    the hammer (upper-left of the gun body); derive the same spot from OUR gun's bbox."""
    x0, y0, x1, y1 = gun.getbbox()
    hx = x0 + max(1, (x1-x0)//8)      # just behind the breech
    hy = y0 + max(1, (y1-y0)//6)
    ink = (33, 33, 33, 255)
    op = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for p in [(hx, hy), (hx+1, hy+1), (hx+2, hy+1)]:
        if 0 <= p[0] < 32 and 0 <= p[1] < 32: op.putpixel(p, ink)
    cl = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for p in [(hx+2, hy-1), (hx+2, hy)]:
        if 0 <= p[0] < 32 and 0 <= p[1] < 32: cl.putpixel(p, ink)
    return op, cl

def build(name):
    cfg = PICKS[name]
    ometa = json.load(open(f"{REPO}/Resources/Textures/{cfg['rel']}/meta.json", encoding="utf-8-sig"))
    want = [s["name"] for s in ometa["states"]]
    icon = to32(cfg["raw"])
    orig_icon = Image.open(f"{REPO}/Resources/Textures/{cfg['rel']}/{cfg['icon']}.png")
    ratio = opq(icon) / opq(orig_icon)
    assert ratio >= 0.55, f"{name}: ratio gate failed ({ratio:.2f})"
    files = {cfg["icon"]: icon}
    if "base" in want:
        files["base"] = icon                     # original: base == icon, byte-identical
    if "bolt-open" in want:
        op, cl = hammer_markers(icon)
        files["bolt-open"], files["bolt-closed"] = op, cl
    if "capbullet" in want:
        cb = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        cb.putpixel((16, 15), (255, 255, 255, 3))   # original is a 1px alpha-3 placeholder
        files["capbullet"] = cb
    L = f"{REPO}/Resources/Textures/{cfg['rel']}/inhand-left.png"
    R = f"{REPO}/Resources/Textures/{cfg['rel']}/inhand-right.png"
    for hand, im in rig(icon, rotate=cfg["rot"], grow=cfg["grow"]).items():
        files[hand] = im
    missing = [w for w in want if w not in files]
    assert not missing, f"{name}: missing {missing}"
    d = f"{OUT}/{name}.rsi"; os.makedirs(d, exist_ok=True)
    states = []
    for s in ometa["states"]:
        files[s["name"]].save(f"{d}/{s['name']}.png")
        e = {"name": s["name"]}
        if s.get("directions", 1) == 4: e["directions"] = 4
        states.append(e)
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY,
            "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
    print(f"{name}: {len(states)}/{len(want)} states, icon ratio {ratio:.2f} COMPLETE")
    return files

if __name__ == "__main__":
    outs = {n: build(n) for n in PICKS}
    cells = []
    for n, f in outs.items():
        for st, im in f.items():
            cols = im.width // 32
            for i in range((im.width//32)*(im.height//32)):
                cells.append((f"{n}/{st}"[:14], im.crop(((i%cols)*32, (i//cols)*32, (i%cols)*32+32, (i//cols)*32+32))))
    sheet = Image.new("RGBA", (len(cells)*76+4, 96), (45, 45, 52, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (l, c) in enumerate(cells):
        r = c.resize((68, 68), Image.NEAREST); sheet.paste(r, (4+i*76, 4), r)
        dr.text((4+i*76, 74), l, fill=(225, 225, 225, 255))
    sheet.save(f"{SCR}/rej-built.png")
    print("preview written")
