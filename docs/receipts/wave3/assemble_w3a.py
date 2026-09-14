#!/usr/bin/env python3
"""Wave 3a: assemble COMPLEX inhand-heavy RSIs from ONE curated CC0 icon each.

inhand-left/right (4-dir) are RIGGED procedurally (measured hand anchors), and
`storage` states are the icon stood upright — neither needs generation.
"""
import json, os, sys
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot"))
from process_sprite import process, sample_bg_color
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_medium import key_sweep
from inhand_rig import rig

SCR = os.path.dirname(os.path.abspath(__file__))
GEN = f"{SCR}/medium-gen"
OUT = f"{SCR}/w3a/rsi"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
COPY = ("AI-generated icon (MiniMax image-01) for Solreign, human-curated; inhand and storage "
        "states derived procedurally from that icon; replaces CC-BY-NC art")

# asset -> dict(dir=game rsi dir, pick=icon variant, rot=inhand rotation, grow=inhand scale,
#               storage_rot=rotation for the `storage` state, extra={state: variant})
ASSETS = {
  "beach_ball":     dict(dir="Objects/Fun/Balls/beach_ball.rsi",       pick="icon_v2", rot=0,   grow=2.1),
  "football":       dict(dir="Objects/Fun/Balls/football.rsi",         pick="icon_v1", rot=0,   grow=2.1),
  "corgi":          dict(dir="Objects/Fun/Balloons/corgi.rsi",         pick="icon_v2", rot=0,   grow=2.6),
  "nanotrasen":     dict(dir="Objects/Fun/Balloons/nanotrasen.rsi",    pick="icon_v2", rot=0,   grow=2.6),
  "syndicate":      dict(dir="Objects/Fun/Balloons/syndicate.rsi",     pick="icon_v2", rot=0,   grow=2.6),
  "rubber_chicken": dict(dir="Objects/Fun/rubber_chicken.rsi",         pick="icon_v2", rot=45,  grow=2.2),
  "clownrecorder":  dict(dir="Objects/Fun/clownrecorder.rsi",          pick="icon_v1", rot=0,   grow=2.0),
  "lamp":           dict(dir="Objects/Fun/Plushies/lamp.rsi",          pick="icon_v1", rot=0,   grow=2.2),
  "pondering_orb":  dict(dir="Objects/Fun/pondering_orb.rsi",          pick="icon_v1", rot=0,   grow=2.0,
                         pulse=dict(state="icon", delays=[0.2,0.2,0.2,0.2], grid=(2,2))),
  "powersink":      dict(dir="Objects/Power/powersink.rsi",            pick="icon_v1", rot=0,   grow=2.2,
                         icon_state="powersink"),
  "cutlass":        dict(dir="Objects/Weapons/Melee/cutlass.rsi",      pick="icon_v1", rot=45,  grow=2.4,
                         storage_rot=45, extra={"foam_icon": "foam_icon_v1"},
                         upright_from={"foam_storage": "foam_icon"}),
  "machete":        dict(dir="Objects/Weapons/Melee/machete.rsi",      pick="icon_v1", rot=45,  grow=2.4,
                         storage_rot=45),
  "incomplete_bat": dict(dir="Objects/Weapons/Melee/incomplete_bat.rsi", pick="icon_v1", rot=45, grow=2.4,
                         storage_rot=45),
  "handdrill":      dict(dir="Objects/Tools/handdrill.rsi",            pick="icon_v1", rot=30, grow=2.2,
                         icon_state="handdrill"),
  "foam_grenade":   dict(dir="Objects/Fun/Foam/foam_grenade.rsi",      pick="icon_v2", rot=0,  grow=2.0,
                         primed_from="icon"),
  "foam_crossbow":  dict(dir="Objects/Fun/Foam/foam_crossbow.rsi",     pick="icon_v2", rot=0,  grow=2.3,
                         extra={"foambox": "foambox_v1"}),
  "foam_blade":     dict(dir="Objects/Fun/Foam/foam_blade.rsi",        pick=None, rot=45, grow=2.4,
                         icon_src=f"{SCR}/img2img-probe/blade_icon_raw.png"),
}

def strip_teal_halo(im):
    out = im.copy()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = im.getpixel((x, y))
            if a == 0: continue
            if g > r*1.35 and b > r*1.35 and abs(g-b) < 25:
                out.putpixel((x, y), (0, 0, 0, 0))
    return out

def to32(raw):
    bg = sample_bg_color(Image.open(raw).convert("RGBA"))
    im = process(raw, size=32, colors=20, tol=60, resample="box")
    return strip_teal_halo(key_sweep(im, bg, tol=35))

def pulse(icon, n, grid):
    """A glowing item breathing — 4-frame sine brightness cycle, packed to the original's grid.
    Derived from our own icon (same technique as the _Solreign animation factory)."""
    import math
    cols, rows = grid
    out = Image.new("RGBA", (cols*32, rows*32), (0, 0, 0, 0))
    for i in range(n):
        f = 1.0 + 0.22 * math.sin(2*math.pi*i/n)
        fr = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        for y in range(32):
            for x in range(32):
                r, g, b, a = icon.getpixel((x, y))
                if a == 0: continue
                fr.putpixel((x, y), (min(255,int(r*f)), min(255,int(g*f)), min(255,int(b*f)), a))
        out.paste(fr, ((i % cols)*32, (i // cols)*32))
    return out

def heat(icon):
    """`primed` = the same item, running hot. Derived from our own icon so identity is exact."""
    out = icon.copy()
    for y in range(icon.height):
        for x in range(icon.width):
            r, g, b, a = icon.getpixel((x, y))
            if a == 0: continue
            tr, tg, tb = 255, 90, 30
            t = 0.45
            out.putpixel((x, y), (min(255, int(r+(tr-r)*t)+30), min(255, int(g+(tg-g)*t)),
                                  min(255, int(b+(tb-b)*t)), a))
    return out

def upright(icon, rot):
    """`storage` = the item stood upright in its box."""
    bb = icon.getbbox()
    it = icon.crop(bb).rotate(rot, resample=Image.NEAREST, expand=True)
    it = it.crop(it.getbbox())
    s = min(28 / it.width, 30 / it.height)
    it = it.resize((max(1, round(it.width*s)), max(1, round(it.height*s))), Image.NEAREST)
    out = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    out.paste(it, ((32-it.width)//2, (32-it.height)//2), it)
    return out

def build(name, cfg, dry=True):
    orig_meta = json.load(open(f"{REPO}/Resources/Textures/{cfg['dir']}/meta.json", encoding="utf-8-sig"))
    want = [s["name"] for s in orig_meta["states"]]
    icon_state = cfg.get("icon_state", "icon")
    raw = cfg.get("icon_src") or f"{GEN}/{name}/{cfg['pick']}_raw.png"
    if not os.path.exists(raw): return None
    icon = to32(raw)
    files = {icon_state: icon}
    for st, var in (cfg.get("extra") or {}).items():
        p = f"{GEN}/{name}/{var}_raw.png"
        if os.path.exists(p): files[st] = to32(p)
    if "storage" in want:
        files["storage"] = upright(icon, cfg.get("storage_rot", 0))
    for st, srcst in (cfg.get("upright_from") or {}).items():
        if srcst in files:
            files[st] = upright(files[srcst], cfg.get("storage_rot", 0))
    if cfg.get("pulse"):
        p = cfg["pulse"]
        files[p["state"]] = pulse(icon, len(p["delays"]), p["grid"])
    if cfg.get("primed_from") and "primed" in want:
        files["primed"] = heat(files[cfg["primed_from"]])
    for st, im in rig(icon, rotate=cfg["rot"], grow=cfg["grow"]).items():
        files[st] = im
    missing = [w for w in want if w not in files]
    states = []
    for s in orig_meta["states"]:
        if s["name"] not in files: continue
        e = {"name": s["name"]}
        if s.get("directions", 1) == 4: e["directions"] = 4
        if cfg.get("pulse") and s["name"] == cfg["pulse"]["state"]:
            e["delays"] = [cfg["pulse"]["delays"]]
        states.append(e)
    if not dry:
        d = f"{OUT}/{name}.rsi"; os.makedirs(d, exist_ok=True)
        for st, im in files.items():
            if st in want: im.save(f"{d}/{st}.png")
        meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY,
                "size": {"x": 32, "y": 32}, "states": states}
        json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
    return files, want, missing

if __name__ == "__main__":
    dry = "--dry" in sys.argv
    rows = []
    for name, cfg in ASSETS.items():
        r = build(name, cfg, dry)
        if r is None:
            print(f"{name}: SKIP (no icon generated yet)"); continue
        files, want, missing = r
        print(f"{name}: {'dry' if dry else 'built'} {len(want)} states"
              + (f"  MISSING={missing}" if missing else ""))
        cells = [(st, files[st]) for st in want if st in files]
        row = []
        for st, im in cells:
            cols = im.width // 32
            for i in range((im.width//32)*(im.height//32)):
                row.append(im.crop(((i%cols)*32, (i//cols)*32, (i%cols)*32+32, (i//cols)*32+32)))
        rows.append((name, row))
    W = max(len(r) for _, r in rows)*36 + 150
    sheet = Image.new("RGBA", (W, len(rows)*40), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (n, cs) in enumerate(rows):
        for j, c in enumerate(cs): sheet.paste(c, (150+j*36, i*40+4), c)
        dr.text((4, i*40+16), n[:18], fill=(225, 225, 225, 255))
    sheet.resize((sheet.width*2, sheet.height*2), Image.NEAREST).save(f"{SCR}/w3a/montage.png")
    print("montage written")
