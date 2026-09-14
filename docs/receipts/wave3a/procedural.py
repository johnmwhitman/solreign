#!/usr/bin/env python3
"""WAVE 3A hand-drawn micro-states. These are NOT AI-generated and read NO NC pixels --
they are tiny procedural details (a few px) or simple gradients, each justified inline
by what was observed in the NC original (see docs/receipts/wave3a/refs-extras.png)."""
from PIL import Image

def clamp(v): return max(0, min(255, int(round(v))))

def capgun_bolt_open():
    """NC original bbox (5,13)-(8,15): a ~3x2px dark metal fleck (cylinder-open
    indicator). Reproduced as a small dark-grey rounded fleck at the same slot."""
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = im.load()
    for x, y in [(5, 13), (6, 13), (7, 13), (5, 14), (6, 14), (7, 14)]:
        px[x, y] = (60, 58, 62, 255)
    px[6, 13] = (95, 92, 98, 255)
    return im

def capgun_bolt_closed():
    """NC original bbox (7,12)-(8,14): a ~1x2px dark metal fleck (cylinder-closed
    indicator), smaller and shifted from the open pose."""
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = im.load()
    px[7, 12] = (60, 58, 62, 255)
    px[7, 13] = (80, 78, 84, 255)
    return im

def capgun_capbullet():
    """NC original: a slim vertical bronze/gold bar (a single cap strip)."""
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    px = im.load()
    for y in range(8, 24):
        f = (y - 8) / 16
        r = clamp(196 - 40 * f); g = clamp(150 - 30 * f); b = clamp(58 - 10 * f)
        for x in (14, 15, 16, 17):
            shade = 1.0 if x in (15, 16) else 0.75
            px[x, y] = (clamp(r*shade), clamp(g*shade), clamp(b*shade), 255)
    return im

def foam_grenade_primed(base_icon):
    """NC original 'primed': same grenade body + a small lit green spark/fuse on top.
    Derived by compositing a hand-drawn spark cluster onto OUR OWN curated grenade icon
    (never the NC pixels) at the top of its bbox -- identity-preserving, no new AI call."""
    im = base_icon.copy()
    bbox = im.getbbox()
    if not bbox:
        return im
    px = im.load()
    top_x = (bbox[0] + bbox[2]) // 2
    top_y = bbox[1]
    spark = [(-1, -3, (110, 230, 90, 255)), (0, -4, (170, 255, 140, 255)),
             (1, -3, (110, 230, 90, 255)), (0, -3, (200, 255, 190, 255)),
             (0, -2, (90, 200, 70, 255)), (-1, -1, (60, 150, 50, 200)),
             (1, -1, (60, 150, 50, 200))]
    for dx, dy, col in spark:
        x, y = top_x + dx, top_y + dy
        if 0 <= x < 32 and 0 <= y < 32:
            px[x, y] = col
    return im
