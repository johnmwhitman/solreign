#!/usr/bin/env python3
"""LICENSE BATCH A: Tiles/Planet/Concrete 6-pack drawn PROCEDURALLY from scratch (CC0).

Replaces the goonstation-derived CC-BY-NC-SA-3.0 concrete floor set
(concrete, concrete_mono, concrete_smooth, grayconcrete, grayconcrete_mono,
grayconcrete_smooth). The originals were VIEWED only to describe their
SUBJECTS and read unprotectable technical parameters:

  - canvas: 128x32 RGBA strip = 4 variants of 32x32 (floors.yml: variants: 4)
  - three geometry roles: "tile" = 2x2 grid of 16px sub-tiles with grout
    lines, "mono"/slab = one slab per 32px tile with a grout border,
    "smooth" = featureless speckled surface
  - palette mood: a warm greenish-beige concrete family and a dark cool
    gray family; fully opaque

No NC pixels are read, sampled, traced, or conditioned on — every pixel
below comes from this code: fresh palettes authored here, deterministic
integer-hash speckle + tileable lattice mottle, worn-industrial details
(pits, chips, hairline cracks kept clear of tile edges so variants tile
against each other cleanly).

Outputs overwrite Resources/Textures/Tiles/Planet/Concrete/*.png in place
and write CONTACT-concrete.png (before/after grid) next to this script.

Gates:
  - size/format gate: output strip must match original size + RGBA + opaque
  - EDGE-WRAP gate: per 32x32 variant, the wrapped seam (col31->col0,
    row31->row0) must not be stronger than the strongest transition already
    inside the texture — i.e. tiling introduces no new visible seam.
  - opaque-pixel ratio gate (wave-6 convention; trivially 1.0 here, kept
    for the receipt).
"""
import os
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(SCR, "..", "..", ".."))
DST = os.path.join(ROOT, "Resources/Textures/Tiles/Planet/Concrete")

TILE = 32          # one floor tile
VARIANTS = 4       # variants per strip
W, H = TILE * VARIANTS, TILE

# ---------------------------------------------------------------- palettes
# Authored fresh for Solreign (not sampled). Ordered dark -> light.
WARM = {
    "base":  [(134, 131, 118, 255), (142, 139, 126, 255),
              (150, 147, 133, 255), (158, 155, 141, 255)],
    "grout": (72, 70, 62, 255),
    "bevel": (170, 167, 152, 255),
    "pit":   (106, 104, 93, 255),
    "chip":  (178, 175, 160, 255),
}
GRAY = {
    "base":  [(76, 76, 79, 255), (85, 85, 88, 255),
              (94, 94, 97, 255), (103, 103, 106, 255)],
    "grout": (42, 42, 45, 255),
    "bevel": (115, 115, 118, 255),
    "pit":   (56, 56, 59, 255),
    "chip":  (126, 126, 129, 255),
}

# ------------------------------------------------------------ deterministic
def hu(ix, iy, seed):
    """Deterministic integer hash -> [0, 1). Pure function of args."""
    n = (ix * 374761393 + iy * 668265263 + seed * 2246822519) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    n ^= n >> 16
    return n / 4294967296.0

def mottle(x, y, seed, cells=4):
    """Tileable low-frequency value noise over a TILE-periodic lattice.

    Lattice indices are taken mod `cells`, so value at x==0 equals the
    continuation past x==TILE-1 -> tileable by construction.
    """
    step = TILE / cells
    fx, fy = x / step, y / step
    ix, iy = int(fx), int(fy)
    tx, ty = fx - ix, fy - iy
    tx = tx * tx * (3 - 2 * tx)   # smoothstep
    ty = ty * ty * (3 - 2 * ty)
    v00 = hu(ix % cells,       iy % cells,       seed)
    v10 = hu((ix + 1) % cells, iy % cells,       seed)
    v01 = hu(ix % cells,       (iy + 1) % cells, seed)
    v11 = hu((ix + 1) % cells, (iy + 1) % cells, seed)
    a = v00 + (v10 - v00) * tx
    b = v01 + (v11 - v01) * tx
    return a + (b - a) * ty

# ------------------------------------------------------------------ drawing
def draw_variant(pal, geometry, seed):
    """One 32x32 worn-concrete floor tile. geometry in {grid, slab, smooth}."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    for y in range(TILE):
        for x in range(TILE):
            m = mottle(x, y, seed, cells=4)             # broad stains
            s = hu(x, y, seed + 101)                     # per-pixel speckle
            v = 0.74 * m + 0.26 * s
            idx = min(3, int(v * 4))
            c = pal["base"][idx]
            # worn details: pits (dark) and chips (light), sparse
            r = hu(x, y, seed + 202)
            if r < 0.011:
                c = pal["pit"]
            elif r > 0.994:
                c = pal["chip"]
            px[x, y] = c

    d = ImageDraw.Draw(im)
    if geometry == "grid":
        # grout at local x,y == 0 and 16 (16px module -> wraps cleanly)
        for k in (0, 16):
            d.line([(k, 0), (k, TILE - 1)], fill=pal["grout"])
            d.line([(0, k), (TILE - 1, k)], fill=pal["grout"])
        # single-pixel bevel light along the inner top/left of each sub-tile
        for k in (1, 17):
            for t in range(TILE):
                if hu(t, k, seed + 303) > 0.45:
                    if px[t, k][:3] != pal["grout"][:3]:
                        px[t, k] = pal["bevel"]
                    if px[k, t][:3] != pal["grout"][:3]:
                        px[k, t] = pal["bevel"]
    elif geometry == "slab":
        # one slab per tile: grout border along top/left edge only, so
        # adjacent tiles share a single 1px joint
        d.line([(0, 0), (0, TILE - 1)], fill=pal["grout"])
        d.line([(0, 0), (TILE - 1, 0)], fill=pal["grout"])
        for t in range(1, TILE):
            if hu(t, 1, seed + 303) > 0.5:
                px[t, 1] = pal["bevel"]
            if hu(1, t, seed + 304) > 0.5:
                px[1, t] = pal["bevel"]
    # smooth: nothing extra

    # hairline cracks, kept >=3px away from every edge so tiling never
    # cuts a crack (variants land next to random neighbours in-game)
    n_cracks = int(hu(seed, 7, seed + 404) * 2)          # 0-1
    for ci in range(n_cracks):
        cx = 4 + int(hu(ci, 11, seed + 505) * (TILE - 12))
        cy = 4 + int(hu(ci, 13, seed + 506) * (TILE - 12))
        length = 4 + int(hu(ci, 17, seed + 507) * 4)
        horiz = hu(ci, 19, seed + 508) > 0.5
        for t in range(length):
            if not (3 <= cx <= TILE - 4 and 3 <= cy <= TILE - 4):
                break
            px[cx, cy] = pal["pit"]
            if horiz:
                cx += 1
                cy += -1 if hu(t, ci, seed + 509) < 0.33 else (
                    1 if hu(t, ci, seed + 510) > 0.66 else 0)
            else:
                cy += 1
                cx += -1 if hu(t, ci, seed + 509) < 0.33 else (
                    1 if hu(t, ci, seed + 510) > 0.66 else 0)
    return im

def build_strip(pal, geometry, base_seed):
    strip = Image.new("RGBA", (W, H))
    for v in range(VARIANTS):
        strip.paste(draw_variant(pal, geometry, base_seed + v * 1000), (v * TILE, 0))
    return strip

# -------------------------------------------------------------------- gates
def lum(p):
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]

def col_diff(px, x1, x2):
    return sum(abs(lum(px[x1, y]) - lum(px[x2, y])) for y in range(TILE)) / TILE

def row_diff(px, y1, y2):
    return sum(abs(lum(px[x, y1]) - lum(px[x, y2])) for x in range(TILE)) / TILE

def edge_wrap_check(tile_img, name):
    """Wrapped seam must not exceed the strongest interior transition."""
    px = tile_img.load()
    interior_cols = max(col_diff(px, x, x + 1) for x in range(TILE - 1))
    interior_rows = max(row_diff(px, y, y + 1) for y in range(TILE - 1))
    wrap_col = col_diff(px, TILE - 1, 0)
    wrap_row = row_diff(px, TILE - 1, 0)
    ok_c = wrap_col <= interior_cols * 1.05 + 1.0
    ok_r = wrap_row <= interior_rows * 1.05 + 1.0
    print(f"  {name}: wrap col {wrap_col:5.2f} (interior max {interior_cols:5.2f}) "
          f"row {wrap_row:5.2f} (interior max {interior_rows:5.2f}) "
          f"{'OK' if ok_c and ok_r else 'FAIL'}")
    assert ok_c and ok_r, f"EDGE-WRAP gate failed for {name}"

FILES = [
    ("concrete.png",             WARM, "grid",   11),
    ("concrete_mono.png",        WARM, "slab",   22),
    ("concrete_smooth.png",      WARM, "smooth", 33),
    ("grayconcrete.png",         GRAY, "grid",   44),
    ("grayconcrete_mono.png",    GRAY, "slab",   55),
    ("grayconcrete_smooth.png",  GRAY, "smooth", 66),
]

def main():
    rows = []
    for fname, pal, geometry, seed in FILES:
        orig = Image.open(os.path.join(DST, fname)).convert("RGBA")
        strip = build_strip(pal, geometry, seed)
        assert strip.size == orig.size, f"{fname}: size {strip.size} != {orig.size}"
        # opacity gates
        n_new = sum(1 for y in range(H) for x in range(W) if strip.load()[x, y][3] > 8)
        n_old = sum(1 for y in range(H) for x in range(W) if orig.load()[x, y][3] > 8)
        ratio = n_new / n_old
        assert ratio >= 0.55, f"DUAL ART GATE (ratio) failed for {fname}: {ratio}"
        assert n_new == W * H, f"{fname}: not fully opaque"
        # edge-wrap gate per variant
        print(f"{fname} (ratio {ratio:.2f}):")
        for v in range(VARIANTS):
            tile = strip.crop((v * TILE, 0, (v + 1) * TILE, TILE))
            edge_wrap_check(tile, f"variant {v}")
        strip.save(os.path.join(DST, fname))
        rows.append((fname, orig, strip))

    # contact sheet: per file, ORIG strip above NEW strip, 4x nearest
    scale = 4
    cw, ch = W * scale, H * scale
    pad, label_h = 8, 18
    sheet = Image.new("RGBA", (cw + pad * 2,
                               (ch * 2 + label_h + pad) * len(rows) + pad),
                      (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    y = pad
    for fname, orig, new in rows:
        dr.text((pad, y), f"{fname} — top: ORIG (NC), bottom: NEW (CC0)",
                fill=(225, 225, 225, 255))
        y += label_h
        sheet.paste(orig.resize((cw, ch), Image.NEAREST), (pad, y)); y += ch
        sheet.paste(new.resize((cw, ch), Image.NEAREST), (pad, y)); y += ch + pad
    sheet.save(os.path.join(SCR, "CONTACT-concrete.png"))
    print("written", os.path.join(SCR, "CONTACT-concrete.png"))

if __name__ == "__main__":
    main()
