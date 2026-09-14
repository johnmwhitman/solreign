#!/usr/bin/env python3
"""Assemble WAVE 3A curated generations + procedural derivations into staged RSIs
(wave3a-proc/). Meta-driven: reads each ORIGINAL asset's meta.json + PNG dimensions
directly (they're still NC in the working tree at this point -- functional interface
facts only, per the probe's Finding-0 rail: state names / directions / delays / pixel
geometry are read, no NC pixel is ever composited into an output)."""
import json, os
from PIL import Image

import sys
SCR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCR)
from rig_wave3a import rig_static, rig_animated, brightness, DIRS
import procedural as proc

GEN = f"{SCR}/wave3a-gen"
OUT = f"{SCR}/wave3a-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/orch-regen-wave3a")
COPY = "AI-generated (MiniMax image-01) for Solreign, human-curated; inhand states procedurally rigged; replaces CC-BY-NC-SA art"

# ---- curated variant picks: asset -> state -> "vN" (chosen by eye from
# wave3a-gen/_curation_<asset>.png contact sheets, see receipt for notes) ----
PICKS = {
    "powersink": {"powersink": "v1"},
    "handdrill": {"handdrill": "v1"},
    "handdrilldiamond": {"handdrill": "v1"},
    "football": {"icon": "v2"},
    "basketball": {"icon": "v2"},
    "beach_ball": {"icon2": "v1"},
    "nanotrasen": {"icon": "v1"},
    "corgi": {"icon": "v1"},
    "syndicate": {"icon": "v1"},
    "rubber_chicken": {"icon": "v2"},
    "pondering_orb": {"icon": "v2"},
    "clownrecorder": {"icon": "v1"},
    "lamp": {"icon": "v2"},
    "capgun": {"icon": "v1"},
    "foam_crossbow": {"icon": "v3", "foambox": "v1"},
    "foam_grenade": {"icon": "v2"},
    "foam_blade": {"icon": "v2"},
    "energy_crossbow": {"icon": "v2"},
    "machete": {"icon": "v2"},
    "singularityhammer": {"icon": "v2"},
    "chainsaw": {"icon": "v2"},
    "cult_halberd": {"icon": "v3"},
    "mjollnir": {"icon": "v2"},
    "cutlass": {"icon": "v2"},
    "incomplete_bat": {"icon": "v1"},
}

# ---- per-RSI config ----
# rsi: game-relative rsi dir under Resources/Textures
# primary: (state_name_in_meta, gen_asset_key, gen_state_key) -- the main generated icon
# rotate: degrees applied for BOTH inhand-rig placement and any "vertical" derived states
#         (diagonal-icon classes only; 0 for round/upright items)
# grow / wielded_grow: rig stamp scale factors
# extra: list of (state_name, spec) where spec is one of:
#   ("duplicate", other_state_name)
#   ("rotate_full", other_state_name)      -- rotate that state's content by `rotate`, center full-size
#   ("gen", gen_asset_key, gen_state_key)  -- separate AI generation
#   ("procedural", fn_name)                -- procedural.py function, no args or (base_icon)
CONFIG = {
    "Objects/Power/powersink.rsi": {
        "primary": ("powersink", "powersink", "powersink"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Tools/handdrill.rsi": {
        "primary": ("handdrill", "handdrill", "handdrill"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Tools/handdrilldiamond.rsi": {
        "primary": ("handdrill", "handdrilldiamond", "handdrill"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/Balls/football.rsi": {
        "primary": ("icon", "football", "icon"), "rotate": 0, "grow": 1.6, "extra": [],
    },
    "Objects/Fun/Balls/basketball.rsi": {
        "primary": ("icon", "basketball", "icon"), "rotate": 0, "grow": 1.6, "extra": [],
    },
    "Objects/Fun/Balls/beach_ball.rsi": {
        # gen_state "icon2" = the green-chroma-key retry; the first attempt used magenta,
        # which collided with the ball's own reddish panel (see receipt "beach_ball
        # chroma-key lesson") and washed it out under border-flood-fill.
        "primary": ("icon", "beach_ball", "icon2"), "rotate": 0, "grow": 1.6, "extra": [],
    },
    "Objects/Fun/Balloons/nanotrasen.rsi": {
        "primary": ("icon", "nanotrasen", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/Balloons/corgi.rsi": {
        "primary": ("icon", "corgi", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/Balloons/syndicate.rsi": {
        "primary": ("icon", "syndicate", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/rubber_chicken.rsi": {
        "primary": ("icon", "rubber_chicken", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/pondering_orb.rsi": {
        "primary": ("icon", "pondering_orb", "icon"), "rotate": 0, "grow": 1.6, "extra": [],
    },
    "Objects/Fun/clownrecorder.rsi": {
        "primary": ("icon", "clownrecorder", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/Plushies/lamp.rsi": {
        "primary": ("icon", "lamp", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Fun/capgun.rsi": {
        "primary": ("icon", "capgun", "icon"), "rotate": 0, "grow": 1.9,
        "extra": [
            ("base", ("duplicate", "icon")),
            ("bolt-open", ("procedural", "capgun_bolt_open")),
            ("bolt-closed", ("procedural", "capgun_bolt_closed")),
            ("capbullet", ("procedural", "capgun_capbullet")),
        ],
    },
    "Objects/Fun/Foam/foam_crossbow.rsi": {
        "primary": ("icon", "foam_crossbow", "icon"), "rotate": 0, "grow": 1.9,
        "extra": [("foambox", ("gen", "foam_crossbow", "foambox"))],
    },
    "Objects/Fun/Foam/foam_grenade.rsi": {
        "primary": ("icon", "foam_grenade", "icon"), "rotate": 0, "grow": 1.9,
        "extra": [("primed", ("procedural", "foam_grenade_primed"))],
    },
    "Objects/Fun/Foam/foam_blade.rsi": {
        "primary": ("icon", "foam_blade", "icon"), "rotate": 45, "grow": 1.9, "extra": [],
    },
    "Objects/Weapons/Guns/Basic/energy_crossbow.rsi": {
        "primary": ("icon", "energy_crossbow", "icon"), "rotate": 0, "grow": 1.9, "extra": [],
    },
    "Objects/Weapons/Melee/machete.rsi": {
        "primary": ("icon", "machete", "icon"), "rotate": 45, "grow": 1.9,
        "extra": [("storage", ("rotate_full", "icon"))],
    },
    "Objects/Weapons/Melee/singularityhammer.rsi": {
        "primary": ("icon", "singularityhammer", "icon"), "rotate": 45, "grow": 1.9,
        "wielded_grow": 2.6, "extra": [],
    },
    "Objects/Weapons/Melee/chainsaw.rsi": {
        "primary": ("icon", "chainsaw", "icon"), "rotate": 45, "grow": 1.9,
        "wielded_grow": 2.6, "extra": [],
    },
    "Objects/Weapons/Melee/cult_halberd.rsi": {
        "primary": ("icon", "cult_halberd", "icon"), "rotate": 45, "grow": 1.9,
        "wielded_grow": 2.6, "extra": [],
    },
    "Objects/Weapons/Melee/mjollnir.rsi": {
        "primary": ("icon", "mjollnir", "icon"), "rotate": 45, "grow": 1.9,
        "wielded_grow": 2.6, "extra": [],
    },
    "Objects/Weapons/Melee/cutlass.rsi": {
        "primary": ("icon", "cutlass", "icon"), "rotate": 45, "grow": 1.9,
        "extra": [
            ("storage", ("rotate_full", "icon")),
            ("foam_icon", ("duplicate", "icon")),
            ("foam_storage", ("duplicate", "storage")),
        ],
    },
    "Objects/Weapons/Melee/incomplete_bat.rsi": {
        "primary": ("icon", "incomplete_bat", "icon"), "rotate": 45, "grow": 1.9,
        "extra": [("storage", ("rotate_full", "icon"))],
    },
}

def orig_meta(rel):
    return json.load(open(f"{REPO}/Resources/Textures/{rel}/meta.json", encoding="utf-8-sig"))

def orig_png_size(rel, state):
    return Image.open(f"{REPO}/Resources/Textures/{rel}/{state}.png").size

def load_curated(asset, state):
    v = PICKS[asset][state]
    return Image.open(f"{GEN}/{asset}/{state}_{v}_32.png").convert("RGBA")

def rotate_full(icon, rotate):
    """Full-size centered rotation (for 'storage'-style vertical derivatives)."""
    bb = icon.getbbox()
    if not bb:
        return icon.copy()
    item = icon.crop(bb)
    if rotate:
        item = item.rotate(rotate, resample=Image.NEAREST, expand=True)
        item = item.crop(item.getbbox())
    iw, ih = item.size
    s = min(30 / iw, 30 / ih, 1.0)
    nw, nh = max(1, round(iw * s)), max(1, round(ih * s))
    item = item.resize((nw, nh), Image.NEAREST)
    canvas = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    canvas.paste(item, ((32 - nw) // 2, (32 - nh) // 2), item)
    return canvas

def place_full(icon):
    """Center the curated icon as-is on a 32x32 canvas (primary state placement)."""
    bb = icon.getbbox()
    if not bb:
        return icon.copy()
    item = icon.crop(bb)
    iw, ih = item.size
    s = min(30 / iw, 30 / ih, 1.0)
    nw, nh = max(1, round(iw * s)), max(1, round(ih * s))
    item = item.resize((nw, nh), Image.NEAREST) if s < 1.0 else item
    canvas = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    canvas.paste(item, ((32 - nw) // 2, (32 - nh) // 2), item)
    return canvas

def grid_pack(w, h, tiles, tile=32):
    cols = w // tile
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for i, t in enumerate(tiles):
        out.paste(t, ((i % cols) * tile, (i // cols) * tile), t)
    return out

def pulse_frames(base32, n):
    """n near-identical idle-pulse frames of a full-size (not rig-stamped) icon state,
    for animated non-inhand icons like pondering_orb / chainsaw / mjollnir /
    singularityhammer (all confirmed-by-eye near-static poses w/ a subtle wobble)."""
    if n <= 1:
        return [base32]
    out = []
    for i in range(n):
        if i == 0:
            out.append(base32); continue
        fac = 1.0 + 0.08 * (1 if i % 2 else -1)
        fr = brightness(base32, fac)
        dx = 1 if i % 2 else -1
        shifted = Image.new("RGBA", fr.size, (0, 0, 0, 0))
        shifted.paste(fr, (dx, 0), fr)
        out.append(shifted)
    return out

def build_asset(rel, cfg):
    meta = orig_meta(rel)
    primary_name, gen_asset, gen_state = cfg["primary"]
    icon = load_curated(gen_asset, gen_state)
    d = f"{OUT}/{os.path.basename(rel)}"
    os.makedirs(d, exist_ok=True)

    built = {}  # state_name -> PIL Image (already correct final png)

    for st in meta["states"]:
        name = st["name"]
        directions = st.get("directions", 1)
        nframes = len(st["delays"][0]) if "delays" in st else 1
        ow, oh = orig_png_size(rel, name)

        if name == primary_name:
            base = place_full(icon)
            tiles = pulse_frames(base, nframes) if directions == 1 else [base] * (directions * nframes)
            img = grid_pack(ow, oh, tiles)

        elif name in ("inhand-left", "inhand-right", "wielded-inhand-left", "wielded-inhand-right"):
            wielded = name.startswith("wielded")
            hand = "inhand-left" if "left" in name else "inhand-right"
            grow = cfg.get("wielded_grow", cfg["grow"]) if wielded else cfg["grow"]
            anim = rig_animated(icon, cfg["rotate"], grow, tile=32, nframes=nframes)
            per_dir = anim[hand]  # list of 4 dirs, each a list of nframes tiles
            tiles = []
            for d_frames in per_dir:
                tiles.extend(d_frames)
            img = grid_pack(ow, oh, tiles)

        else:
            spec = dict(cfg["extra"])[name]
            kind = spec[0]
            if kind == "duplicate":
                img = built[spec[1]].copy()
            elif kind == "rotate_full":
                img = rotate_full(icon if spec[1] == primary_name else built[spec[1]], cfg["rotate"])
            elif kind == "gen":
                extra_icon = load_curated(spec[1], spec[2])
                img = place_full(extra_icon)
            elif kind == "procedural":
                fn = getattr(proc, spec[1])
                img = fn(icon) if spec[1] == "foam_grenade_primed" else fn()
            else:
                raise ValueError(f"unknown spec kind {kind}")
            if img.size != (ow, oh):
                # single-frame procedural/derived states are always 32x32 = tile size;
                # pad/crop defensively to original dims for parity (should be equal already)
                canvas = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
                canvas.paste(img, (0, 0))
                img = canvas

        built[name] = img
        img.save(f"{d}/{name}.png")

    new_meta = {
        "version": meta.get("version", 1),
        "license": "CC0-1.0",
        "copyright": COPY,
        "size": meta["size"],
        "states": meta["states"],
    }
    json.dump(new_meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print(f"{rel}: assembled ({len(meta['states'])} states)")

def main():
    for rel, cfg in CONFIG.items():
        build_asset(rel, cfg)
    print("ALL ASSEMBLED")

if __name__ == "__main__":
    main()
