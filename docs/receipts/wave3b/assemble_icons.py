#!/usr/bin/env python3
"""Assemble ore.rsi, carp.rsi, monitors.rsi from curated MiniMax generations
(icons-gen/) + procedural inhand rig / shimmer animation."""
import json, os, sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wave3b_common import pack_like, shrink, center, rig_frame, brightness, DIRS  # noqa: E402

GEN = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons-gen")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons-proc")
COPY_AI = "AI-generated (MiniMax image-01), human-curated; replaces CC-BY-NC art"

PICK = {
    "ore_bananium": "icon_v1", "ore_gold": "icon_v1", "ore_iron": "icon_v2",
    "ore_uranium": "icon_v1", "ore_plasma": "icon_v2", "ore_spacequartz": "icon_v1",
    "ore_silver": "icon_v1", "ore_coal": "icon_v3", "ore_salt": "icon_v1",
    "ore_diamond": "icon_v2",
    "carp_carpplush": "icon_v1", "carp_magicplush": "icon_v1", "carp_holoplush": "icon_v2",
    "carp_rainbowcarpplush": "icon_v3",
    "monitor_party": "icon_v2", "monitor_rad": "icon_v2", "monitor_shipalert": "icon_v2",
    "monitor_mobilevision": "front_v3", "monitor_television": "front_v2",
}


def load(asset):
    return Image.open(f"{GEN}/{asset}/{PICK[asset]}_32.png").convert("RGBA")


def load_variant(asset, key):
    return Image.open(f"{GEN}/{asset}/{key}_32.png").convert("RGBA")


# ------------------------------------------------------------------ ore ----
ORE_MATERIALS = ["bananium", "gold", "iron", "uranium", "plasma", "spacequartz",
                  "silver", "coal", "salt", "diamond"]
ORE_ROTATE = {  # per-material rotation so the rig's "upright" default reads sensibly
    "bananium": 0, "gold": 0, "iron": 0, "uranium": 0, "plasma": 0,
    "spacequartz": 0, "silver": 0, "coal": 0, "salt": 0, "diamond": 0,
}


def build_ore():
    d = f"{OUT}/ore.rsi"
    os.makedirs(d, exist_ok=True)
    states = []
    for mat in ORE_MATERIALS:
        icon = shrink(load(f"ore_{mat}"), factor=0.92, bottom_gap=1)
        pack_like(f"Objects/Materials/ore.rsi/{mat}", [icon]).save(f"{d}/{mat}.png")
        states.append({"name": mat})
        for hand in ("inhand-left", "inhand-right"):
            frames = [rig_frame(icon, hand, dname, rotate=ORE_ROTATE[mat], grow=2.1) for dname in DIRS]
            name = f"{mat}-{hand}"
            pack_like(f"Objects/Materials/ore.rsi/{name}", frames).save(f"{d}/{name}.png")
            states.append({"name": name, "directions": 4})
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY_AI, "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("ore.rsi assembled:", len(states), "states")


# ----------------------------------------------------------------- carp ----
def shimmer_frames(icon, n, base_alpha=255):
    out = []
    for i in range(n):
        f = 1.0 + 0.12 * ((i % 4) - 1.5) / 1.5
        out.append(brightness(icon, max(0.8, min(1.25, f))))
    return out


def build_carp():
    d = f"{OUT}/carp.rsi"
    os.makedirs(d, exist_ok=True)
    states = []

    # static variants: carpplush, magicplush, rainbowcarpplush (+ rig for each)
    static_map = [
        ("carpplush", "carp_carpplush", "carpplush"),
        ("magicplush", "carp_magicplush", "magicarpplush"),
        ("rainbowcarpplush", "carp_rainbowcarpplush", "rainbowcarpplush"),
    ]
    for state_name, gen_asset, rig_prefix in static_map:
        icon = shrink(load(gen_asset), factor=0.88, bottom_gap=1)
        pack_like(f"Objects/Fun/Plushies/carp.rsi/{state_name}", [icon]).save(f"{d}/{state_name}.png")
        states.append({"name": state_name})
        for hand in ("inhand-left", "inhand-right"):
            frames = [rig_frame(icon, hand, dname, rotate=0, grow=2.3) for dname in DIRS]
            name = f"{rig_prefix}-{hand}"
            pack_like(f"Objects/Fun/Plushies/carp.rsi/{name}", frames).save(f"{d}/{name}.png")
            states.append({"name": name, "directions": 4})

    # holoplush: 14-frame shimmer icon + 11-frame x4dir shimmer inhand
    holo_icon = shrink(load("carp_holoplush"), factor=0.88, bottom_gap=1)
    holo_frames = shimmer_frames(holo_icon, 14)
    pack_like("Objects/Fun/Plushies/carp.rsi/holoplush", holo_frames).save(f"{d}/holoplush.png")
    states.append({"name": "holoplush", "delays": [[0.1] * 14]})
    for hand in ("inhand-left", "inhand-right"):
        per_dir_frames = []
        for dname in DIRS:
            rigged = rig_frame(holo_icon, hand, dname, rotate=0, grow=2.3)
            per_dir_frames.append(shimmer_frames(rigged, 11))
        all_frames = [f for dframes in per_dir_frames for f in dframes]
        name = f"holocarpplush-{hand}"
        pack_like(f"Objects/Fun/Plushies/carp.rsi/{name}", all_frames).save(f"{d}/{name}.png")
        states.append({"name": name, "directions": 4, "delays": [[0.3] + [0.1] * 10] * 4})

    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY_AI, "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("carp.rsi assembled:", len(states), "states")


# -------------------------------------------------------------- monitors ---
def build_monitors():
    d = f"{OUT}/monitors.rsi"
    os.makedirs(d, exist_ok=True)
    states = []

    party = center(load("monitor_party"), factor=0.85)
    pack_like("Structures/monitors.rsi/party", [party]).save(f"{d}/party.png")
    states.append({"name": "party", "delays": [[1.0]]})

    rad_base = center(load("monitor_rad"), factor=0.85)
    pack_like("Structures/monitors.rsi/rad0", [rad_base]).save(f"{d}/rad0.png")
    states.append({"name": "rad0", "delays": [[1.0]]})
    rad_on = brightness(rad_base, 1.35)
    pack_like("Structures/monitors.rsi/rad1", [rad_base, rad_on]).save(f"{d}/rad1.png")
    states.append({"name": "rad1", "delays": [[0.1, 0.1]]})

    ship_base = center(load("monitor_shipalert"), factor=0.85)
    variants = [ship_base, brightness(ship_base, 0.7), brightness(ship_base, 1.3)]
    for i, im in enumerate(variants):
        name = f"shipalert{i}"
        pack_like(f"Structures/monitors.rsi/{name}", [im]).save(f"{d}/{name}.png")
        states.append({"name": name, "delays": [[1.0]]})

    # mobilevision / television: direction-near-invariant stationary drones ->
    # same generated view replicated across the 4 declared directions.
    for state_name, asset in [("mobilevision", "monitor_mobilevision"), ("television", "monitor_television")]:
        icon = shrink(load(asset), factor=0.85, bottom_gap=1)
        frames = [icon, icon, icon, icon]
        pack_like(f"Structures/monitors.rsi/{state_name}", frames).save(f"{d}/{state_name}.png")
        states.append({"name": state_name, "directions": 4})

    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY_AI, "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("monitors.rsi assembled:", len(states), "states")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    build_ore()
    build_carp()
    build_monitors()
    print("all icon assets assembled")
