#!/usr/bin/env python3
"""Shared helpers for wave3b (reused from pilot/medium pipelines)."""
import os, sys
from PIL import Image

sys.path.insert(0, os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot"))
from process_sprite import process, sample_bg_color  # noqa: E402

REPO = os.path.expanduser("~/AI/solreign-trees/orch-regen-wave3b")


def orig_png(rel):
    """rel like 'Objects/Materials/ore.rsi/gold' (no .png)"""
    return f"{REPO}/Resources/Textures/{rel}.png"


def pack_like(original_rel, frames):
    """Pack a list of 32x32 RGBA frame images into a canvas matching the
    ORIGINAL file's exact pixel dimensions, filled row-major from (0,0).
    Guarantees exact geometry parity by construction."""
    ow, oh = Image.open(orig_png(original_rel)).size
    cols = ow // 32
    out = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        out.paste(f, ((i % cols) * 32, (i // cols) * 32))
    return out


def clamp(v):
    return max(0, min(255, int(round(v))))


def brightness(im, f, alpha=None):
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    px_in = im.load()
    px_out = out.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px_in[x, y]
            if a == 0:
                continue
            px_out[x, y] = (clamp(r * f), clamp(g * f), clamp(b * f), min(a, alpha) if alpha else a)
    return out


def shrink(im, factor=0.82, bottom_gap=2):
    """Scale content down inside a 32x32 tile, bottom-anchored."""
    bbox = im.getbbox()
    if not bbox:
        return im
    content = im.crop(bbox)
    w, h = content.size
    s = min(factor * 32 / max(w, h), 1.0)
    nw, nh = max(1, round(w * s)), max(1, round(h * s))
    content = content.resize((nw, nh), Image.NEAREST)
    out = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    out.paste(content, ((32 - nw) // 2, 32 - nh - bottom_gap), content)
    return out


def center(im, factor=0.82):
    """Scale content down inside a 32x32 tile, centered (not bottom-anchored)."""
    bbox = im.getbbox()
    if not bbox:
        return im
    content = im.crop(bbox)
    w, h = content.size
    s = min(factor * 32 / max(w, h), 1.0)
    nw, nh = max(1, round(w * s)), max(1, round(h * s))
    content = content.resize((nw, nh), Image.NEAREST)
    out = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    out.paste(content, ((32 - nw) // 2, (32 - nh) // 2), content)
    return out


# ---- inhand rig (from probe-img2img/inhand_rig.py) ----
ANCHOR = {
    ("inhand-left", "S"): (19, 16, 25, 24), ("inhand-left", "N"): (6, 17, 11, 24),
    ("inhand-left", "E"): (19, 16, 25, 23), ("inhand-left", "W"): (12, 16, 19, 24),
    ("inhand-right", "S"): (6, 16, 13, 24), ("inhand-right", "N"): (20, 17, 25, 24),
    ("inhand-right", "E"): (12, 16, 20, 24), ("inhand-right", "W"): (7, 16, 13, 23),
}
DIRS = ["S", "N", "E", "W"]


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


def rig_frame(icon, hand, d, rotate=0, grow=1.9):
    """icon (32x32 RGBA) -> single rigged 32x32 frame for (hand, direction)."""
    bb = icon.getbbox()
    item = icon.crop(bb) if bb else icon
    if rotate:
        item = item.rotate(rotate, resample=Image.NEAREST, expand=True)
        item = item.crop(item.getbbox())
    it = item if hand == "inhand-left" else item.transpose(Image.FLIP_LEFT_RIGHT)
    return stamp(it, hand, d, grow)


def rig_sheet(icon, hand, rotate=0, grow=1.9):
    """icon -> 64x64 4-dir sheet (2x2 grid, S,N,E,W row-major) for one hand."""
    sheet = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    for i, d in enumerate(DIRS):
        sheet.paste(rig_frame(icon, hand, d, rotate, grow), ((i % 2) * 32, (i // 2) * 32))
    return sheet
