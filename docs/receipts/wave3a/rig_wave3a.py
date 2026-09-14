#!/usr/bin/env python3
"""WAVE 3A rig — derive inhand-*/wielded-inhand-* states procedurally from a curated
CC0 icon, per PROBE-IMG2IMG-COMPLEX-2026-07-15.md Finding 2 (measured 1004-sprite anchor
table). Extends probe-img2img/inhand_rig.py with:
  - a `grow` param exposed per call (bigger for "wielded" two-handed poses)
  - multi-frame idle-wobble variants (tiny jitter + brightness pulse) for states whose
    original declares >1 frame per direction
  - packing into a canvas that matches the ORIGINAL PNG's exact pixel dimensions
    (RobustToolbox's on-disk RSI grid is whatever the source file's own width/height
    implies -- see RsiLoading.cs L216-241 -- so parity is "same total size", not a
    specific formula; grid_like() below reproduces that convention, same as wave2's
    assemble_medium.py)
"""
import os
from PIL import Image

ANCHOR = {
    ("inhand-left",  "S"): (19, 16, 25, 24), ("inhand-left",  "N"): (6, 17, 11, 24),
    ("inhand-left",  "E"): (19, 16, 25, 23), ("inhand-left",  "W"): (12, 16, 19, 24),
    ("inhand-right", "S"): (6, 16, 13, 24),  ("inhand-right", "N"): (20, 17, 25, 24),
    ("inhand-right", "E"): (12, 16, 20, 24), ("inhand-right", "W"): (7, 16, 13, 23),
}
DIRS = ["S", "N", "E", "W"]

def clamp(v): return max(0, min(255, int(round(v))))

def brightness(im, f, alpha=None):
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    px_in, px_out = im.load(), out.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px_in[x, y]
            if a == 0: continue
            px_out[x, y] = (clamp(r*f), clamp(g*f), clamp(b*f), min(a, alpha) if alpha else a)
    return out

def stamp(item, hand, d, grow=1.9, tile=32):
    """Place `item` (RGBA, tight-cropped, at tile=32 native scale) at the measured hand
    anchor for this frame. `tile` lets the same anchor table drive a 64x64 canvas by
    doubling every measurement (used nowhere in wave3a yet -- kept for future 64x waves)."""
    scale = tile / 32
    l, t, r, b = (v * scale for v in ANCHOR[(hand, d)])
    bw, bh = (r - l) * grow, (b - t) * grow
    iw, ih = item.size
    s = min(bw / iw, bh / ih)
    nw, nh = max(1, round(iw * s)), max(1, round(ih * s))
    scaled = item.resize((nw, nh), Image.NEAREST)
    cx, cy = (l + r) / 2, (t + b) / 2
    canvas = Image.new("RGBA", (tile, tile), (0, 0, 0, 0))
    canvas.paste(scaled, (round(cx - nw / 2), round(cy - nh / 2)), scaled)
    return canvas

def rig_static(icon, rotate=0, grow=1.9, tile=32):
    """icon (tile x tile RGBA CC0 art) -> {hand: [S, N, E, W] tiles}, one frame each."""
    bb = icon.getbbox()
    item = icon.crop(bb) if bb else icon
    if rotate:
        item = item.rotate(rotate, resample=Image.NEAREST, expand=True)
        item = item.crop(item.getbbox())
    out = {}
    for hand in ("inhand-left", "inhand-right"):
        frames = []
        for d in DIRS:
            it = item if hand == "inhand-left" else item.transpose(Image.FLIP_LEFT_RIGHT)
            frames.append(stamp(it, hand, d, grow, tile))
        out[hand] = frames
    return out

def rig_animated(icon, rotate=0, grow=1.9, tile=32, nframes=1, pulse=True):
    """Same as rig_static but each direction gets `nframes` near-identical frames with a
    tiny idle-wobble (brightness pulse via the guardian_info shimmer trick from wave2,
    +/-1px jitter) -- matching what the original NC frame sets actually look like
    (near-static poses with a subtle wobble, confirmed by eye on singularityhammer/
    mjollnir/chainsaw before writing this)."""
    base = rig_static(icon, rotate, grow, tile)
    if nframes <= 1:
        return {k: [[v] for v in frames] for k, frames in base.items()}
    out = {}
    for hand, frames in base.items():
        anim_dirs = []
        for d_frame in frames:
            seq = []
            for i in range(nframes):
                fac = 1.0 + 0.06 * ((-1) ** i) * (i > 0)
                fr = d_frame
                if pulse and i > 0:
                    fr = brightness(d_frame, fac)
                    dx = 1 if i % 2 else -1
                    shifted = Image.new("RGBA", fr.size, (0, 0, 0, 0))
                    shifted.paste(fr, (dx, 0), fr)
                    fr = shifted
                seq.append(fr)
            anim_dirs.append(seq)
        out[hand] = anim_dirs
    return out

def grid_like(target_w, target_h, tiles, tile=32):
    """Pack `tiles` (flat list, row-major fill order) into a canvas of exactly
    (target_w, target_h) -- the geometry-parity contract wave1/2 already established."""
    cols = target_w // tile
    out = Image.new("RGBA", (target_w, target_h), (0, 0, 0, 0))
    for i, t in enumerate(tiles):
        out.paste(t, ((i % cols) * tile, (i // cols) * tile), t)
    return out
