#!/usr/bin/env python3
"""Player-feedback batch 2026-07-16: procedurally rig inhand-left/right (4-dir) states
for three Solreign easter-egg items that crash or render wrong on pickup, using the
established rig idiom (docs/receipts/probe-img2img/inhand_rig.py, also used to build
Objects/Fun/Balls/beach_ball.rsi's own inhand states per that RSI's copyright note).

Targets:
  - egg20_omni_gauntlet.rsi (item 1: pickup error, Item.RsiPath falls back to this RSI
    which had zero inhand states)
  - liquid_flame_thermos.rsi (item 2: pickup error, same missing-inhand-states class,
    plus a missing icon_open state fixed separately by hand)
  - egg12_transit_orb.rsi (item 3: wrong-sprite-in-hand; once Item.sprite is repointed at
    this RSI instead of the inherited beach_ball.rsi, it needs its own inhand states)

The gauntlet's icon is a 4-frame animated strip (128x32); rig from frame 0 (a static
held pose is standard — none of the sibling reskins animate their inhand sprite).
"""
import os
from PIL import Image

ROOT = os.path.expanduser("~/AI/solreign-trees/feedback-bugs/Resources/Textures/_Solreign")

# measured medians: (hand, dir) -> (left, top, right, bottom) of the held-item bbox
# (identical anchor table to docs/receipts/probe-img2img/inhand_rig.py — same convention)
ANCHOR = {
    ("inhand-left",  "S"): (19, 16, 25, 24), ("inhand-left",  "N"): (6, 17, 11, 24),
    ("inhand-left",  "E"): (19, 16, 25, 23), ("inhand-left",  "W"): (12, 16, 19, 24),
    ("inhand-right", "S"): (6, 16, 13, 24),  ("inhand-right", "N"): (20, 17, 25, 24),
    ("inhand-right", "E"): (12, 16, 20, 24), ("inhand-right", "W"): (7, 16, 13, 23),
}
DIRS = ["S", "N", "E", "W"]


def clamp(v):
    return max(0, min(255, int(round(v))))


def stamp(item, hand, d, grow=1.9):
    l, t, r, b = ANCHOR[(hand, d)]
    bw, bh = (r - l) * grow, (b - t) * grow
    iw, ih = item.size
    s = min(bw / iw, bh / ih)
    nw, nh = max(1, round(iw * s)), max(1, round(ih * s))
    scaled = item.resize((nw, nh), Image.NEAREST)
    cx, cy = (l + r) // 2, (t + b) // 2
    tile = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    tile.paste(scaled, (cx - nw // 2, cy - nh // 2), scaled)
    return tile


def rig(icon, rotate=0, grow=1.9):
    """icon (32x32 RGBA) -> {state_name: 64x64 4-dir PNG}"""
    bb = icon.getbbox()
    item = icon.crop(bb) if bb else icon
    if rotate:
        item = item.rotate(rotate, resample=Image.NEAREST, expand=True)
        bb2 = item.getbbox()
        if bb2:
            item = item.crop(bb2)
    out = {}
    for hand in ("inhand-left", "inhand-right"):
        sheet = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        for i, d in enumerate(DIRS):
            it = item if hand == "inhand-left" else item.transpose(Image.FLIP_LEFT_RIGHT)
            sheet.paste(stamp(it, hand, d, grow), ((i % 2) * 32, (i // 2) * 32))
        out[hand] = sheet
    return out


def rig_target(rsi_name, icon_frame_box=None, grow=1.9):
    rsi_dir = f"{ROOT}/{rsi_name}"
    icon = Image.open(f"{rsi_dir}/icon.png").convert("RGBA")
    if icon_frame_box:
        icon = icon.crop(icon_frame_box)
    frames = rig(icon, grow=grow)
    for name, im in frames.items():
        im.save(f"{rsi_dir}/{name}.png")
    print(f"{rsi_name}: wrote inhand-left.png / inhand-right.png ({icon.size} source frame)")


def make_icon_open(rsi_name):
    """Item 2 (thermos): DrinkVisualsOpenable needs an icon_open state that reads
    visibly distinct from icon (lid on) at a glance -- the house convention for every
    other flask in the game (flask.rsi/barflask.rsi both ship icon + icon_open).
    Cheap procedural derivation: strip the cap-disc rows (measured off the alpha mask,
    the icon's narrowest top band) to bare the neck, then push the now-exposed neck rows
    toward the liquid's own warm amber glow -- reads as "lid off, glow escaping."
    """
    rsi_dir = f"{ROOT}/{rsi_name}"
    path = f"{rsi_dir}/icon.png"
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    out = im.copy()

    for y in range(2, 5):
        for x in range(w):
            r, g, b, a = out.getpixel((x, y))
            if a > 0:
                out.putpixel((x, y), (r, g, b, 0))

    for y in range(5, 10):
        for x in range(w):
            r, g, b, a = out.getpixel((x, y))
            if a == 0:
                continue
            nr = clamp(r * 0.6 + 255 * 0.4)
            ng = clamp(g * 0.6 + 200 * 0.4)
            nb = clamp(b * 0.5 + 60 * 0.4)
            out.putpixel((x, y), (nr, ng, nb, a))

    out.save(f"{rsi_dir}/icon_open.png")
    print(f"{rsi_name}: wrote icon_open.png")


if __name__ == "__main__":
    # gauntlet: 128x32 4-frame strip -> use frame 0 (first 32x32) as the static held pose
    rig_target("EasterEggs/egg20_omni_gauntlet.rsi", icon_frame_box=(0, 0, 32, 32))
    # thermos: plain 32x32 icon
    rig_target("EasterEggs/liquid_flame_thermos.rsi")
    make_icon_open("EasterEggs/liquid_flame_thermos.rsi")
    # transit orb: plain 32x32 icon, grow a bit more since a full sphere reads better slightly larger
    rig_target("EasterEggs/egg12_transit_orb.rsi", grow=2.1)
