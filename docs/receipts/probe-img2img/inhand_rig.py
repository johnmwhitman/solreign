#!/usr/bin/env python3
"""PROBE: derive SS14 inhand-left/right (4-dir) states procedurally from a CC0 icon.

The hand anchor is a repo-wide convention, measured from 1004 freely-licensed inhand
sprites (see receipt). Per (hand, direction) the held item occupies a consistent box.
So inhand states are a RIG PLACEMENT, not a generation problem: scale + rotate the
item once, then stamp it at the measured anchor for each of the 8 frames.
"""
import os, sys
from PIL import Image

# measured medians: (hand, dir) -> (left, top, right, bottom) of the held-item bbox
ANCHOR = {
    ("inhand-left",  "S"): (19, 16, 25, 24), ("inhand-left",  "N"): (6, 17, 11, 24),
    ("inhand-left",  "E"): (19, 16, 25, 23), ("inhand-left",  "W"): (12, 16, 19, 24),
    ("inhand-right", "S"): (6, 16, 13, 24),  ("inhand-right", "N"): (20, 17, 25, 24),
    ("inhand-right", "E"): (12, 16, 20, 24), ("inhand-right", "W"): (7, 16, 13, 23),
}
DIRS = ["S", "N", "E", "W"]

def stamp(item, hand, d, grow=1.9):
    """Place `item` (RGBA, tight-cropped) at the measured hand anchor for this frame."""
    l, t, r, b = ANCHOR[(hand, d)]
    # anchor box is the median *small* item; scale the real item to the box's centre,
    # allowing it to overhang proportionally (a sword is longer than a median trinket)
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
    """icon (32x32 RGBA CC0 art) -> {state_name: 64x64 4-dir PNG}"""
    bb = icon.getbbox()
    item = icon.crop(bb)
    if rotate:
        item = item.rotate(rotate, resample=Image.NEAREST, expand=True)
        item = item.crop(item.getbbox())
    out = {}
    for hand in ("inhand-left", "inhand-right"):
        sheet = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        for i, d in enumerate(DIRS):
            it = item if hand == "inhand-left" else item.transpose(Image.FLIP_LEFT_RIGHT)
            sheet.paste(stamp(it, hand, d, grow), ((i % 2) * 32, (i // 2) * 32))
        out[hand] = sheet
    return out

if __name__ == "__main__":
    sys.path.insert(0, os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot"))
    from process_sprite import process
    SCR = os.path.dirname(os.path.abspath(__file__))
    icon = process(f"{SCR}/img2img-probe/blade_icon_raw.png", size=32, colors=20, tol=60, resample="box")
    icon.save(f"{SCR}/img2img-probe/blade_icon_32.png")
    frames = rig(icon, rotate=45)    # per-item-class param: icon is diagonal; +45 stands the blade upright in hand
    for name, im in frames.items():
        im.save(f"{SCR}/img2img-probe/blade_{name}.png")

    # montage: icon + all 8 rigged frames
    from PIL import ImageDraw
    cells = [("icon", icon)]
    for name, im in frames.items():
        for i, d in enumerate(DIRS):
            cells.append((f"{name.split('-')[1][:1]}-{d}", im.crop(((i % 2)*32, (i//2)*32, (i % 2)*32+32, (i//2)*32+32))))
    sheet = Image.new("RGBA", (len(cells)*100+4, 120), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (l, c) in enumerate(cells):
        sheet.paste(c.resize((96, 96), Image.NEAREST), (4+i*100, 4))
        dr.text((4+i*100, 102), l, fill=(225, 225, 225, 255))
    sheet.save(f"{SCR}/img2img-probe/VERDICT-inhand-rig.png")
    print("rig demo written")
