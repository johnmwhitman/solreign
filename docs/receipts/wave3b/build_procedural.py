#!/usr/bin/env python3
"""Wave3b procedural replacements: cash, artifact_fragments, toy_singularity,
directionalfan, xenoturret. Original NC art viewed for CONCEPT ONLY (shapes,
color families, size/level progression) per the reference-first rule; all
pixels drawn fresh from scratch in code. No NC pixel ever read/copied."""
import json, math, os, random, sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wave3b_common import pack_like, brightness, clamp  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "icons-proc")
COPY = "Programmatically drawn for Solreign by the sprite factory (AI-authored code, human-reviewed); replaces CC-BY-NC art"

# ---------------------------------------------------------------- font -----
# 3x5 caps + digits, compact bitmap font
FONT = {
 '0': ["111", "101", "101", "101", "111"], '1': ["010", "110", "010", "010", "111"],
 '2': ["111", "001", "111", "100", "111"], '3': ["111", "001", "111", "001", "111"],
 '4': ["101", "101", "111", "001", "001"], '5': ["111", "100", "111", "001", "111"],
 '6': ["111", "100", "111", "101", "111"], '7': ["111", "001", "010", "010", "010"],
 '8': ["111", "101", "111", "101", "111"], '9': ["111", "101", "111", "001", "111"],
 'K': ["101", "110", "100", "110", "101"], 'M': ["101", "111", "111", "101", "101"],
 'S': ["111", "100", "111", "001", "111"], '$': ["011", "110", "011", "110", "011"],
}


def draw_text(im, text, x0, y0, color, scale=1):
    x = x0
    for c in text:
        rows = FONT.get(c)
        if not rows:
            x += 4 * scale
            continue
        for ry, row in enumerate(rows):
            for rx, bit in enumerate(row):
                if bit == "1":
                    for sy in range(scale):
                        for sx in range(scale):
                            px = x + rx * scale + sx
                            py = y0 + ry * scale + sy
                            if 0 <= px < im.width and 0 <= py < im.height:
                                im.putpixel((px, py), (*color, 255))
        x += (len(rows[0]) + 1) * scale


# ---------------------------------------------------------------- cash -----
CASH = [
    # state, label, bill_color, shimmer(bool)
    ("cash", "1", (63, 145, 63), False),
    ("cash_10", (16, 130, 168), False),
    ("cash_100", (26, 30, 110), False),
    ("cash_500", (110, 40, 140), False),
    ("cash_1000", (140, 30, 30), False),
    ("cash_5000", (150, 150, 155), True),
    ("cash_10000", (60, 140, 70), True),
    ("cash_25000", (140, 90, 30), True),
    ("cash_50000", (170, 170, 175), True),
    ("cash_100000", (190, 50, 70), True),
    ("cash_1000000", (50, 130, 60), True),
]
CASH_LABEL = {
    "cash": "1", "cash_10": "10", "cash_100": "100", "cash_500": "500",
    "cash_1000": "1K", "cash_5000": "5K", "cash_10000": "10K", "cash_25000": "25K",
    "cash_50000": "50K", "cash_100000": "100K", "cash_1000000": "1M",
}
CASH_FRAMES = {  # matches meta.json declared frame counts
    "cash": 1, "cash_10": 1, "cash_100": 1, "cash_500": 1, "cash_1000": 1,
    "cash_5000": 1, "cash_10000": 14, "cash_25000": 14, "cash_50000": 15,
    "cash_100000": 19, "cash_1000000": 10,
}


def draw_bill(color, label, glint=0.0):
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([2, 10, 29, 21], radius=2, fill=(*color, 255), outline=(20, 20, 22, 255))
    edge = tuple(clamp(c * 1.35) for c in color)
    d.rectangle([4, 12, 27, 19], outline=edge)
    d.line([15, 10, 15, 21], fill=(224, 190, 90, 255), width=1)  # gold divider stripe
    draw_text(im, "S", 5, 13, (224, 190, 90))
    lx = 18 if len(label) <= 2 else 17
    draw_text(im, label, lx, 13, (224, 190, 90))
    if glint > 0:
        # diagonal glint highlight sweep
        for i in range(3):
            gx = 6 + i
            for y in range(10, 22):
                px = gx + (y - 10)
                if 2 <= px <= 29:
                    r, g, b, a = im.getpixel((px, y))
                    if a:
                        im.putpixel((px, y), (clamp(r + 60 * glint), clamp(g + 60 * glint), clamp(b + 60 * glint), a))
    return im


def build_cash():
    d = f"{OUT}/cash.rsi"
    os.makedirs(d, exist_ok=True)
    states = []
    for name, color, shimmer in [(c[0], c[1] if len(c) == 3 else c[2], c[-1]) for c in CASH]:
        label = CASH_LABEL[name]
        nframes = CASH_FRAMES[name]
        if not shimmer:
            frames = [draw_bill(color, label)]
            states.append({"name": name})
        else:
            frames = []
            for i in range(nframes):
                glint = max(0.0, 1.0 - abs((i / nframes) * 2 - 1) * 2.2)  # sweep 0->1->0
                frames.append(draw_bill(color, label, glint))
            states.append({"name": name, "delays": [[0.2] * nframes]})
        pack_like(f"Objects/Economy/cash.rsi/{name}", frames).save(f"{d}/{name}.png")
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("cash built:", [s["name"] for s in states])


# ------------------------------------------------------- artifact_fragments -
FRAG_FAMILIES = ["precursorball", "wizardball", "martianball", "eldritchball", "ancientball"]


def draw_precursor(scale):
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    r = 7 * scale
    cx, cy = 16, 16
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(200, 205, 212, 255), outline=(90, 95, 105, 255))
    d.ellipse([cx - r * 0.4, cy - r * 0.6, cx - r * 0.1, cy - r * 0.3], fill=(235, 238, 242, 255))
    d.ellipse([cx + r * 0.1, cy + r * 0.1, cx + r * 0.4, cy + r * 0.4], fill=(150, 155, 165, 255))
    return im


def draw_wizard(scale):
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    h = 9 * scale
    w = 3.5 * scale
    cx, cy = 16, 16
    d.polygon([(cx, cy - h), (cx + w, cy), (cx, cy + h), (cx - w, cy)], fill=(196, 32, 40, 255), outline=(70, 8, 10, 255))
    d.polygon([(cx, cy - h), (cx + w * 0.4, cy - h * 0.2), (cx, cy)], fill=(232, 90, 90, 255))
    return im


def draw_martian(scale):
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    rnd = random.Random(7)
    cx, cy = 16, 16
    r = 6 * scale
    for i in range(int(14 * scale) + 4):
        ang = rnd.uniform(0, 2 * math.pi)
        rr = r * rnd.uniform(0.4, 1.0)
        x, y = cx + rr * math.cos(ang), cy + rr * math.sin(ang)
        pr = max(1, round(1.6 * scale))
        d = ImageDraw.Draw(im)
        d.ellipse([x - pr, y - pr, x + pr, y + pr], fill=(168, 110, 214, 255))
    d = ImageDraw.Draw(im)
    d.ellipse([cx - r * 0.5, cy - r * 0.5, cx + r * 0.5, cy + r * 0.5], fill=(196, 150, 230, 255))
    return im


def draw_eldritch(scale):
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cx, cy = 16, 16
    r = 6 * scale
    legs = 6
    for i in range(legs):
        ang = i * (2 * math.pi / legs)
        x2, y2 = cx + r * 1.6 * math.cos(ang), cy + r * 1.6 * math.sin(ang)
        d.line([cx, cy, x2, y2], fill=(90, 20, 110, 255), width=max(1, round(scale)))
    d.ellipse([cx - r * 0.6, cy - r * 0.6, cx + r * 0.6, cy + r * 0.6], fill=(60, 10, 80, 255), outline=(20, 4, 30, 255))
    if scale > 0.5:
        d.point((cx - 1, cy), fill=(210, 60, 220, 255))
        d.point((cx + 1, cy), fill=(210, 60, 220, 255))
    return im


def draw_ancient(scale):
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cx, cy = 16, 16
    r = 4 * scale
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(20, 22, 40, 255), outline=(8, 8, 16, 255))
    spikes = 5
    for i in range(spikes):
        ang = i * (2 * math.pi / spikes) + 0.4
        x2, y2 = cx + r * 1.8 * math.cos(ang), cy + r * 1.8 * math.sin(ang)
        d.line([cx, cy, x2, y2], fill=(30, 32, 55, 255), width=1)
    return im


FRAG_DRAW = {
    "precursorball": draw_precursor, "wizardball": draw_wizard, "martianball": draw_martian,
    "eldritchball": draw_eldritch, "ancientball": draw_ancient,
}


def build_fragments():
    d = f"{OUT}/artifact_fragments.rsi"
    os.makedirs(d, exist_ok=True)
    states = []
    for fam in FRAG_FAMILIES:
        fn = FRAG_DRAW[fam]
        for lvl in range(1, 7):
            scale = 1.0 - (lvl - 1) * 0.15  # ball1 full-size -> ball6 smallest (~25%)
            name = f"{fam}{lvl}"
            im = fn(scale)
            pack_like(f"Objects/Specific/Xenoarchaeology/artifact_fragments.rsi/{name}", [im]).save(f"{d}/{name}.png")
            states.append({"name": name})
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32}, "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("artifact_fragments built:", len(states), "states")


# -------------------------------------------------------- toy_singularity --
def draw_singularity(frame, nframes, size=32):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    cx, cy = size / 2, size / 2
    rot = frame * (2 * math.pi / nframes)
    for ring in range(6, 0, -1):
        rr = ring * (size * 0.42 / 6)
        col = (
            clamp(200 - ring * 10), clamp(20 + ring * 4), clamp(30 + ring * 18),
        )
        steps = 40
        for s in range(steps):
            a = rot * (1 + ring * 0.15) + s * (2 * math.pi / steps)
            x = cx + rr * math.cos(a)
            y = cy + rr * math.sin(a) * 0.95
            if 0 <= x < size and 0 <= y < size:
                im.putpixel((int(x), int(y)), (*col, 255))
    d = ImageDraw.Draw(im)
    r0 = size * 0.14
    d.ellipse([cx - r0, cy - r0, cx + r0, cy + r0], fill=(10, 4, 8, 255))
    r1 = size * 0.06
    hx = cx + r1 * 0.6 * math.cos(rot * 2)
    hy = cy + r1 * 0.6 * math.sin(rot * 2)
    d.ellipse([hx - 2, hy - 2, hx + 2, hy + 2], fill=(230, 60, 200, 255))
    return im


def build_toy_singularity():
    d = f"{OUT}/toy_singularity.rsi"
    os.makedirs(d, exist_ok=True)
    icon_frames = [draw_singularity(i, 8) for i in range(8)]
    pack_like("Objects/Fun/toy_singularity.rsi/icon", icon_frames).save(f"{d}/icon.png")
    icon32 = icon_frames[0]

    from wave3b_common import rig_frame, DIRS
    inhand_frames_l = []
    inhand_frames_r = []
    for i in range(5):
        f = draw_singularity(i * 8 // 5, 8, size=32)  # full-size swirl, no pre-shrink
        for hand, store in (("inhand-left", inhand_frames_l), ("inhand-right", inhand_frames_r)):
            sheet4 = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
            for di, dname in enumerate(DIRS):
                # singularity is a round glowing orb w/ no facing -> same art every direction,
                # just re-centered on that direction's hand anchor with a generous overhang
                # (it's meant to read as a prominent glowing toy in-hand, not a tiny sliver)
                tile = rig_frame(f, hand, dname, rotate=0, grow=4.5)
                sheet4.paste(tile, ((di % 2) * 32, (di // 2) * 32), tile)
            store.append(sheet4)
    # inhand PNG layout: directions=4, frames=5 each -> pack as (dir outer? or frame outer?)
    # SS14 convention: outer loop = direction, inner = frame. Build per-direction frame strips then combine.
    from wave3b_common import DIRS
    for hand, frames in (("singu-inhand-left", inhand_frames_l), ("singu-inhand-right", inhand_frames_r)):
        # frames[i] is a 64x64 4-dir sheet for animation-frame i; extract each dir's 32x32 tile
        per_dir = {dname: [] for dname in DIRS}
        for sheet4 in frames:
            for di, dname in enumerate(DIRS):
                tile = sheet4.crop(((di % 2) * 32, (di // 2) * 32, (di % 2) * 32 + 32, (di // 2) * 32 + 32))
                per_dir[dname].append(tile)
        all_frames = []
        for dname in DIRS:
            all_frames.extend(per_dir[dname])
        pack_like(f"Objects/Fun/toy_singularity.rsi/{hand}", all_frames).save(f"{d}/{hand}.png")
    meta = {
        "version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
        "states": [
            {"name": "icon", "delays": [[0.1] * 8]},
            {"name": "singu-inhand-left", "directions": 4, "delays": [[0.2] * 5] * 4},
            {"name": "singu-inhand-right", "directions": 4, "delays": [[0.2] * 5] * 4},
        ],
    }
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("toy_singularity built")


# --------------------------------------------------------- directionalfan --
def draw_fan_blade(angle_deg, housing_rot=0, size=32, S=6):
    big = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    c = size * S / 2
    d.rectangle([2 * S, 2 * S, (size - 2) * S - 1, (size - 2) * S - 1], outline=(58, 62, 68, 255), width=S)
    ang0 = math.radians(angle_deg)
    for b in range(4):
        a = ang0 + b * math.pi / 2
        tip = (c + 11.5 * S * math.cos(a), c + 11.5 * S * math.sin(a))
        left = (c + 3 * S * math.cos(a + 0.55), c + 3 * S * math.sin(a + 0.55))
        right = (c + 3 * S * math.cos(a - 0.55), c + 3 * S * math.sin(a - 0.55))
        mid_l = (c + 9.5 * S * math.cos(a + 0.28), c + 9.5 * S * math.sin(a + 0.28))
        mid_r = (c + 9.5 * S * math.cos(a - 0.28), c + 9.5 * S * math.sin(a - 0.28))
        d.polygon([left, mid_l, tip, mid_r, right], fill=(40, 44, 50, 255))
    d.ellipse([c - 2.4 * S, c - 2.4 * S, c + 2.4 * S, c + 2.4 * S], fill=(70, 74, 82, 255))
    small = big.resize((size, size), Image.BOX)
    if housing_rot:
        small = small.rotate(housing_rot, resample=Image.NEAREST, expand=False)
    return small


def build_directionalfan():
    d = f"{OUT}/directionalfan.rsi"
    os.makedirs(d, exist_ok=True)
    housing_rot = {"S": 0, "N": 180, "E": 270, "W": 90}
    from wave3b_common import DIRS
    all_frames = []
    for dname in DIRS:
        for fi in range(4):
            all_frames.append(draw_fan_blade(45 + fi * 22.5, housing_rot[dname]))
    pack_like("Structures/Piping/Atmospherics/directionalfan.rsi/icon", all_frames).save(f"{d}/icon.png")
    meta = {
        "version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
        "states": [{"name": "icon", "directions": 4, "delays": [[0.01] * 4] * 4}],
    }
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("directionalfan built")


# ------------------------------------------------------------ xenoturret ---
def draw_acid_pod(pulse, size=32):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    cx, cy = size / 2, size / 2 + 3
    legr = 9
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        x2, y2 = cx + legr * math.cos(a), cy + legr * math.sin(a) * 0.6 + 3
        d.line([cx, cy, x2, y2], fill=(40, 20, 45, 255), width=2)
        d.ellipse([x2 - 1, y2 - 1, x2 + 1, y2 + 1], fill=(30, 15, 35, 255))
    bob = math.sin(pulse * 2 * math.pi) * 1.2
    bh = 8 + bob
    d.polygon([(cx, cy - bh - 4), (cx - 6, cy - 2), (cx - 3, cy + 2), (cx + 3, cy + 2), (cx + 6, cy - 2)],
              fill=(58, 22, 66, 255), outline=(24, 8, 30, 255))
    glow = clamp(140 + 100 * (0.5 + 0.5 * math.sin(pulse * 2 * math.pi)))
    d.ellipse([cx - 2.6, cy - bh - 4.6, cx + 2.6, cy - bh + 0.6], fill=(20, clamp(glow), 60, 255))
    d.ellipse([cx - 1, cy - bh - 3, cx + 1, cy - bh - 1], fill=(210, 255, 210, 255))
    for i in range(3):
        a = -math.pi / 2 + (i - 1) * 0.5
        x2, y2 = cx + 5 * math.cos(a), cy - bh - 4 + 5 * math.sin(a) - 2
        d.line([cx, cy - bh - 4, x2, y2], fill=(30, 12, 36, 255), width=1)
    return im


def build_xenoturret():
    d = f"{OUT}/xenoturret.rsi"
    os.makedirs(d, exist_ok=True)
    frames = [draw_acid_pod(i / 10) for i in range(10)]
    all_frames = frames * 4  # direction-invariant stationary pod, replicated across 4 declared directions
    pack_like("Objects/Weapons/Guns/Turrets/xenoturret.rsi/acid_turret", all_frames).save(f"{d}/acid_turret.png")
    meta = {
        "version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
        "states": [{"name": "acid_turret", "directions": 4, "delays": [[0.1] * 10] * 4}],
    }
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2)
    open(f"{d}/meta.json", "a").write("\n")
    print("xenoturret built")


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    build_cash()
    build_fragments()
    build_toy_singularity()
    build_directionalfan()
    build_xenoturret()
    print("all procedural assets built")
