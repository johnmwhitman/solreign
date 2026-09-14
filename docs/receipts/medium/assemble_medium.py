#!/usr/bin/env python3
"""Assemble curated MEDIUM-bucket generations into staged RSIs (medium-proc/)."""
import json, os
from PIL import Image

SCR = os.path.dirname(os.path.abspath(__file__))
GEN = f"{SCR}/medium-gen"
OUT = f"{SCR}/medium-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
COPY = "AI-generated (MiniMax image-01) for Solreign, human-curated; replaces CC-BY-NC art"

PICKS = {  # asset -> state -> variant file
    "possum_old": {"front": "front_v2", "back": "back_v1", "side": "side_v1", "dead": "dead_v4"},
    "raccoon":    {"front": "front_v1", "back": "back_v3", "side": "side_v1", "dead": "dead_v1"},
    "ferret":     {"front": "front_v1", "back": "back_v1", "side": "side_v3", "dead": "dead_v1"},
    "scurret":    {"front": "front_v1", "back": "back_v1", "side": "side_v1", "rip": "rip_v3", "oof": "oof_v1"},
    "guardian_info": {"hologram": "hologram_v1", "pedestal": "hologram_v2", "icon": "icon_v1"},
    "snap_pops":  {"icon": "icon_v4", "box": "box_v2"},
}
MOB_STATES = {  # rsi name -> (alive state name, dead-ish states)
    "possum_old": ("possum_old", {"possum_dead_old": "dead"}),
    "raccoon":    ("raccoon", {"raccoon_dead": "dead"}),
    "ferret":     ("ferret", {"ferret_dead": "dead"}),
    "scurret":    ("scurret", {"scurret_rip": "rip", "scurret_oof": "oof"}),
}

def load(asset, key):
    return Image.open(f"{GEN}/{asset}/{PICKS[asset][key]}_32.png").convert("RGBA")

def shrink(im, factor=0.82):
    """Scale content down inside the 32x32 tile so mobs don't tower over vanilla pets."""
    bbox = im.getbbox()
    if not bbox: return im
    content = im.crop(bbox)
    w, h = content.size
    s = min(factor * 32 / max(w, h), 1.0)
    nw, nh = max(1, round(w*s)), max(1, round(h*s))
    content = content.resize((nw, nh), Image.NEAREST)
    out = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    out.paste(content, ((32-nw)//2, 32 - nh - 2), content)  # bottom-anchored, 2px ground gap
    return out

def clamp(v): return max(0, min(255, int(round(v))))

def brightness(im, f, alpha=None):
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = im.getpixel((x, y))
            if a == 0: continue
            out.putpixel((x, y), (clamp(r*f), clamp(g*f), clamp(b*f), min(a, alpha) if alpha else a))
    return out

def grid_like(original_png, tiles):
    """Pack tiles into the same grid shape as the original PNG."""
    ow, oh = Image.open(original_png).size
    cols = ow // 32
    out = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
    for i, t in enumerate(tiles):
        out.paste(t, ((i % cols)*32, (i // cols)*32))
    return out

ORIG = {
    "possum_old": "Mobs/Animals/possum_old", "raccoon": "Mobs/Animals/raccoon",
    "ferret": "Mobs/Pets/ferret", "scurret": "Mobs/Animals/scurret/scurret",
    "guardian_info": "Objects/Misc/guardian_info", "snap_pops": "Objects/Fun/snap_pops",
}

# ---- mobs ----
for rsi, (alive, deads) in MOB_STATES.items():
    d = f"{OUT}/{rsi}.rsi"; os.makedirs(d, exist_ok=True)
    S = shrink(load(rsi, "front"))
    N = shrink(load(rsi, "back"))
    E = shrink(load(rsi, "side"))
    W = E.transpose(Image.FLIP_LEFT_RIGHT)
    orig_png = f"{REPO}/Resources/Textures/{ORIG[rsi]}.rsi/{alive}.png"
    grid_like(orig_png, [S, N, E, W]).save(f"{d}/{alive}.png")
    states = [{"name": alive, "directions": 4}]
    for st, key in deads.items():
        shrink(load(rsi, key)).save(f"{d}/{st}.png")
        states.append({"name": st})
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
    print(rsi, "assembled")

# ---- guardian_info: fairy composited over pedestal, 4-frame hologram shimmer ----
d = f"{OUT}/guardian_info.rsi"; os.makedirs(d, exist_ok=True)
fairy = load("guardian_info", "hologram")
ped = load("guardian_info", "pedestal")
pb = ped.getbbox(); pc = ped.crop(pb)
ps = min(24 / pc.width, 10 / pc.height)
pc = pc.resize((max(1, round(pc.width*ps)), max(1, round(pc.height*ps))), Image.NEAREST)
fb = fairy.getbbox(); fc = fairy.crop(fb)
fs = min(24 / fc.width, 22 / fc.height)
fc = fc.resize((max(1, round(fc.width*fs)), max(1, round(fc.height*fs))), Image.NEAREST)
base = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
base.paste(pc, ((32 - pc.width)//2, 31 - pc.height), pc)
fairy_layer = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
fairy_layer.paste(fc, ((32 - fc.width)//2, 29 - pc.height - fc.height), fc)
frames = []
for f, al in [(1.0, 210), (1.15, 175), (0.95, 210), (1.08, 190)]:
    fr = base.copy()
    fr.alpha_composite(brightness(fairy_layer, f, al))
    frames.append(fr)
grid_like(f"{REPO}/Resources/Textures/Objects/Misc/guardian_info.rsi/guardian_info.png", frames).save(f"{d}/guardian_info.png")
shrink(load("guardian_info", "icon"), 0.9).save(f"{d}/icon.png")
meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
        "states": [{"name": "guardian_info", "delays": [[0.2, 0.1, 0.2, 0.1]]}, {"name": "icon"}]}
json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
print("guardian_info assembled")

# ---- snap_pops ----
d = f"{OUT}/snap_pops.rsi"; os.makedirs(d, exist_ok=True)
shrink(load("snap_pops", "icon"), 0.9).save(f"{d}/icon.png")
shrink(load("snap_pops", "box"), 0.9).save(f"{d}/box.png")
meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
        "states": [{"name": "icon"}, {"name": "box"}]}
json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
print("snap_pops assembled")

# ---- montage of everything staged in medium-proc ----
from PIL import ImageDraw
rows = []
for rsi in sorted(os.listdir(OUT)):
    if not rsi.endswith(".rsi"): continue
    m = json.load(open(f"{OUT}/{rsi}/meta.json"))
    cells = []
    for s in m["states"]:
        im = Image.open(f"{OUT}/{rsi}/{s['name']}.png").convert("RGBA")
        cols = im.width // 32
        for i in range((im.width//32)*(im.height//32)):
            cells.append(im.crop(((i % cols)*32, (i//cols)*32, (i % cols)*32+32, (i//cols)*32+32)))
    row = Image.new("RGBA", (len(cells)*36+4, 40), (28, 28, 32, 255))
    for i, c in enumerate(cells): row.paste(c, (4+i*36, 4), c)
    rows.append((rsi[:-4], row))
W = max(r.width for _, r in rows) + 130
sheet = Image.new("RGBA", (W, len(rows)*44), (28, 28, 32, 255))
dr = ImageDraw.Draw(sheet)
for i, (n, r) in enumerate(rows):
    sheet.paste(r, (130, i*44)); dr.text((4, i*44+16), n[:16], fill=(225, 225, 225, 255))
sheet.resize((sheet.width*2, sheet.height*2), Image.NEAREST).save(f"{SCR}/medium-staged-montage.png")
print("montage written")
