#!/usr/bin/env python3
"""LICENSE BATCH B: the 9 remaining root NC floor tiles drawn PROCEDURALLY
from scratch (CC0) for Solreign.

Replaces:
  goonstation CC-BY-NC-SA-3.0 : metaldiamond, arcadeblue2, carpetclown,
                                carpetoffice, boxing, gym
  Mojave-Sun CC-BY-NC-SA-3.0  : cave, cavedrought, chromite

The NC originals were VIEWED only to describe their SUBJECTS and read
unprotectable technical parameters (canvas size, variant count from
Prototypes/Tiles/floors.yml, fully-opaque alpha, palette mood):

  metaldiamond  32x32   x1  gray industrial diamond-plate steel
  arcadeblue2   32x32   x1  dark-navy arcade carpet w/ scattered neon motifs
  carpetclown   32x32   x1  chaotic rainbow blob-patchwork carpet
  carpetoffice  32x32   x1  teal office twill carpet w/ sparse color flecks
  boxing        128x32  x4  pale-blue worn canvas mat
  gym           128x32  x4  crimson worn rubber/canvas mat
  cave          224x32  x7  dark gray-brown cracked cobble rock
  cavedrought   256x32  x8  dry brown dirt w/ pebbles + cracks
  chromite      224x32  x7  dark blue-violet cracked cobble rock

No NC pixels are read, sampled, traced, img2img'd or conditioned on — every
output pixel comes from this code: fresh in-script palettes, deterministic
integer-hash noise, torus-wrapped (mod-size) drawing so every 32x32 variant
tiles seamlessly by construction.

Gates (batch-A convention):
  - size/format gate: output must match original canvas size, RGBA, opaque
  - opaque-pixel ratio gate (>=0.55; 1.0 here)
  - EDGE-WRAP gate per 32x32 variant: wrapped seam (col31->col0, row31->row0)
    luminance step must not exceed 1.05x the strongest interior transition.

Outputs overwrite Resources/Textures/Tiles/*.png in place and write
CONTACT-tiles.png (per tile: ORIG above NEW, 4x nearest) next to this script.
"""
import os
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(SCR, "..", "..", ".."))
DST = os.path.join(ROOT, "Resources/Textures/Tiles")
TILE = 32

# ------------------------------------------------------------- deterministic
def hu(ix, iy, seed):
    """Deterministic integer hash -> [0, 1)."""
    n = (ix * 374761393 + iy * 668265263 + seed * 2246822519) & 0xFFFFFFFF
    n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
    n ^= n >> 16
    return n / 4294967296.0

def mottle(x, y, seed, cells=4):
    """Tileable low-frequency value noise on a TILE-periodic lattice."""
    step = TILE / cells
    fx, fy = x / step, y / step
    ix, iy = int(fx), int(fy)
    tx, ty = fx - ix, fy - iy
    tx = tx * tx * (3 - 2 * tx)
    ty = ty * ty * (3 - 2 * ty)
    v00 = hu(ix % cells,       iy % cells,       seed)
    v10 = hu((ix + 1) % cells, iy % cells,       seed)
    v01 = hu(ix % cells,       (iy + 1) % cells, seed)
    v11 = hu((ix + 1) % cells, (iy + 1) % cells, seed)
    a = v00 + (v10 - v00) * tx
    b = v01 + (v11 - v01) * tx
    return a + (b - a) * ty

def torus_d2(x, y, sx, sy):
    """Squared distance on the 32x32 torus."""
    dx = abs(x - sx); dx = min(dx, TILE - dx)
    dy = abs(y - sy); dy = min(dy, TILE - dy)
    return dx * dx + dy * dy

def shade(c, dv):
    return (max(0, min(255, c[0] + dv)),
            max(0, min(255, c[1] + dv)),
            max(0, min(255, c[2] + dv)), 255)

def putw(px, x, y, c):
    """Torus-wrapped putpixel."""
    px[x % TILE, y % TILE] = c

# ================================================================ generators
def gen_metaldiamond(seed):
    """Gray diamond-plate: speckled steel + diagonal lugs on an 8px lattice."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    base = [(96, 97, 100, 255), (104, 105, 108, 255),
            (112, 113, 116, 255), (120, 121, 124, 255)]
    for y in range(TILE):
        for x in range(TILE):
            v = 0.6 * mottle(x, y, seed, 4) + 0.4 * hu(x, y, seed + 7)
            px[x, y] = base[min(3, int(v * 4))]
    hi = (156, 158, 162, 255)
    lo = (58, 59, 62, 255)
    # lugs: 8px cell grid, alternating / and \ orientation, drawn wrapped
    for j in range(4):
        for i in range(4):
            cx, cy = i * 8 + 4, j * 8 + 4
            slope = 1 if (i + j) % 2 == 0 else -1
            for t in range(-2, 3):
                lx, ly = cx + t, cy + slope * t
                putw(px, lx, ly, hi)
                putw(px, lx, ly + 1, lo)
    return im

def gen_arcadeblue2(seed):
    """Dark-navy arcade carpet with fresh scattered neon motifs."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    n1 = (24, 32, 64, 255)
    n2 = (30, 40, 78, 255)
    n3 = (38, 50, 92, 255)
    for y in range(TILE):
        for x in range(TILE):
            r = hu(x, y, seed)
            c = n1 if (x + y) % 2 == 0 else n2
            if r > 0.86:
                c = n3
            px[x, y] = c
    YEL = (232, 208, 60, 255); MAG = (214, 60, 190, 255)
    CYN = (70, 210, 226, 255); GRN = (80, 200, 96, 255)
    ORN = (230, 130, 50, 255); WHT = (235, 238, 245, 255)
    def starburst(x, y):
        for d in (-1, 1):
            putw(px, x + d, y, YEL); putw(px, x, y + d, YEL)
        putw(px, x, y, WHT)
    def orb(x, y):
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if abs(dx) + abs(dy) < 2:
                    putw(px, x + dx, y + dy, GRN)
        putw(px, x - 1, y - 1, WHT)
    def comet(x, y):
        for t in range(4):
            putw(px, x + t, y - t, CYN)
        putw(px, x - 1, y + 1, MAG)
    def zag(x, y):
        pts = [(0, 0), (1, 1), (2, 0), (3, 1), (4, 0)]
        for dx, dy in pts:
            putw(px, x + dx, y + dy, MAG)
    def brackets(x, y):
        for t in range(3):
            putw(px, x + t, y, ORN); putw(px, x, y + t, ORN)
    motifs = [starburst, orb, comet, zag, brackets, starburst, comet]
    for k, fn in enumerate(motifs):
        mx = int(hu(k, 3, seed + 50) * TILE)
        my = int(hu(k, 5, seed + 60) * TILE)
        fn(mx, my)
    return im

def gen_carpetclown(seed):
    """Rainbow blob patchwork: torus voronoi cells in saturated colors."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    pal = [(206, 48, 48), (226, 122, 40), (232, 200, 48), (96, 190, 60),
           (44, 172, 128), (52, 190, 210), (58, 96, 208), (128, 62, 200),
           (198, 56, 186), (232, 110, 160)]
    seeds = []
    for k in range(13):
        sx = hu(k, 1, seed) * TILE
        sy = hu(k, 2, seed) * TILE
        col = pal[int(hu(k, 4, seed) * len(pal)) % len(pal)]
        seeds.append((sx, sy, col))
    for y in range(TILE):
        for x in range(TILE):
            best, bc = 1e9, pal[0]
            for sx, sy, col in seeds:
                d = torus_d2(x, y, sx, sy)
                if d < best:
                    best, bc = d, col
            dv = -14 if hu(x, y, seed + 9) < 0.22 else 0
            px[x, y] = shade(bc + (255,), dv)
    return im

def gen_carpetoffice(seed):
    """Teal office twill with sparse colored flecks."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    t1 = (28, 124, 116, 255)
    t2 = (24, 110, 103, 255)
    dk = (18, 90, 85, 255)
    fleck = [(198, 66, 66, 255), (64, 92, 200, 255),
             (214, 222, 220, 255), (122, 198, 188, 255)]
    for y in range(TILE):
        for x in range(TILE):
            c = t1 if ((x + y) // 2) % 2 == 0 else t2
            if (x + y) % 8 == 0:
                c = dk
            r = hu(x, y, seed)
            if r < 0.02:
                c = fleck[int(hu(x, y, seed + 3) * 4) % 4]
            px[x, y] = c
    return im

def _mat_variant(seed, base_pal, stain_dv, scuff, patch):
    """Shared engine for boxing/gym: worn mat canvas, one 32x32 variant."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    for y in range(TILE):
        for x in range(TILE):
            m = mottle(x, y, seed, 4)
            s = hu(x, y, seed + 11)
            v = 0.7 * m + 0.3 * s
            c = base_pal[min(3, int(v * 4))]
            # subtle canvas weave
            if (x % 2) ^ (y % 2):
                c = shade(c, -3)
            px[x, y] = c
    # worn lighter patch (interior, >=4px from edges)
    pcx = 6 + int(hu(1, 1, seed + 21) * (TILE - 14))
    pcy = 6 + int(hu(2, 2, seed + 22) * (TILE - 14))
    prx = 3 + int(hu(3, 3, seed + 23) * 3)
    pry = 2 + int(hu(4, 4, seed + 24) * 3)
    for y in range(max(3, pcy - pry), min(TILE - 3, pcy + pry + 1)):
        for x in range(max(3, pcx - prx), min(TILE - 3, pcx + prx + 1)):
            dx, dy = (x - pcx) / max(1, prx), (y - pcy) / max(1, pry)
            if dx * dx + dy * dy <= 1.0 and hu(x, y, seed + 25) > 0.25:
                px[x, y] = shade(px[x, y], patch)
    # scuff strokes (interior)
    for k in range(3):
        sx = 4 + int(hu(k, 31, seed + 31) * (TILE - 12))
        sy = 4 + int(hu(k, 37, seed + 32) * (TILE - 12))
        ln = 3 + int(hu(k, 41, seed + 33) * 4)
        horiz = hu(k, 43, seed + 34) > 0.5
        for t in range(ln):
            x = sx + (t if horiz else 0)
            y = sy + (0 if horiz else t)
            if 3 <= x <= TILE - 4 and 3 <= y <= TILE - 4:
                px[x, y] = shade(px[x, y], scuff)
    return im

def gen_boxing(seed):
    pal = [(96, 142, 176, 255), (104, 150, 184, 255),
           (112, 158, 192, 255), (120, 166, 200, 255)]
    return _mat_variant(seed, pal, -18, -22, +14)

def gen_gym(seed):
    pal = [(146, 50, 56, 255), (156, 58, 62, 255),
           (166, 66, 70, 255), (176, 76, 78, 255)]
    return _mat_variant(seed, pal, -16, -24, +16)

def _cobble_variant(seed, base, vary, crack, hi_dv):
    """Cracked cobble rock: torus voronoi stones + dark crack joints."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    seeds = []
    for k in range(7):
        sx = hu(k, 1, seed) * TILE
        sy = hu(k, 2, seed) * TILE
        dv = int((hu(k, 3, seed) - 0.5) * 2 * vary)
        seeds.append((sx, sy, dv))
    import math
    for y in range(TILE):
        for x in range(TILE):
            ds = sorted((math.sqrt(torus_d2(x, y, sx, sy)), dv)
                        for sx, sy, dv in seeds)
            d1, dv1 = ds[0]
            d2, _ = ds[1]
            gap = d2 - d1
            if gap < 0.8:
                c = crack                       # tight 1px joints
            else:
                c = shade(base + (255,), dv1)
                # in-stone texture: gentle mottle + speckle
                m = mottle(x, y, seed + 4, 4)
                c = shade(c, int((m - 0.5) * 12))
                if gap < 1.8:
                    c = shade(c, -6)            # soft edge shading
                elif gap > 5.5 and hu(x, y, seed + 5) > 0.85:
                    c = shade(c, hi_dv)         # sparse top glints
                s = hu(x, y, seed + 8)
                if s < 0.04:
                    c = shade(c, -10)
                elif s > 0.97:
                    c = shade(c, 7)
            px[x, y] = c
    return im

def gen_cave(seed):
    return _cobble_variant(seed, (78, 74, 68), 10, (52, 48, 44, 255), 8)

def gen_chromite(seed):
    return _cobble_variant(seed, (56, 53, 82), 12, (36, 34, 54, 255), 10)

def gen_cavedrought(seed):
    """Dry brown dirt: mottled earth + pebbles + short interior cracks."""
    im = Image.new("RGBA", (TILE, TILE))
    px = im.load()
    pal = [(70, 52, 34, 255), (79, 60, 40, 255),
           (88, 68, 46, 255), (97, 76, 52, 255)]
    for y in range(TILE):
        for x in range(TILE):
            v = 0.65 * mottle(x, y, seed, 4) + 0.35 * hu(x, y, seed + 13)
            c = pal[min(3, int(v * 4))]
            s = hu(x, y, seed + 17)
            if s < 0.04:
                c = shade(c, -18)
            elif s > 0.97:
                c = shade(c, 14)
            px[x, y] = c
    # pebbles (torus-wrapped 2x2-3x3 clusters, light top / dark bottom)
    for k in range(7):
        bx = int(hu(k, 51, seed + 51) * TILE)
        by = int(hu(k, 53, seed + 52) * TILE)
        r = 1 + int(hu(k, 57, seed + 53) * 2)
        for dy in range(-r + 1, r):
            for dx in range(-r + 1, r):
                if dx * dx + dy * dy <= r:
                    dv = 20 if dy < 0 else -18
                    putw(px, bx + dx, by + dy, shade((88, 68, 46, 255), dv))
    # short cracks, interior only
    for k in range(2):
        cx = 5 + int(hu(k, 61, seed + 61) * (TILE - 14))
        cy = 5 + int(hu(k, 63, seed + 62) * (TILE - 14))
        for t in range(4 + int(hu(k, 67, seed + 63) * 4)):
            if not (3 <= cx <= TILE - 4 and 3 <= cy <= TILE - 4):
                break
            px[cx, cy] = (48, 34, 22, 255)
            cx += 1
            cy += -1 if hu(t, k, seed + 64) < 0.3 else (
                1 if hu(t, k, seed + 65) > 0.7 else 0)
    return im

# ==================================================================== gates
def lum(p):
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]

def col_diff(px, x1, x2):
    return sum(abs(lum(px[x1, y]) - lum(px[x2, y])) for y in range(TILE)) / TILE

def row_diff(px, y1, y2):
    return sum(abs(lum(px[x, y1]) - lum(px[x, y2])) for x in range(TILE)) / TILE

def edge_wrap_ok(tile_img):
    px = tile_img.load()
    interior_cols = max(col_diff(px, x, x + 1) for x in range(TILE - 1))
    interior_rows = max(row_diff(px, y, y + 1) for y in range(TILE - 1))
    wrap_col = col_diff(px, TILE - 1, 0)
    wrap_row = row_diff(px, TILE - 1, 0)
    ok = (wrap_col <= interior_cols * 1.05 + 1.0 and
          wrap_row <= interior_rows * 1.05 + 1.0)
    return ok, (f"wrap col {wrap_col:5.2f}/{interior_cols:5.2f} "
                f"row {wrap_row:5.2f}/{interior_rows:5.2f}")

# ===================================================================== files
FILES = [
    ("metaldiamond.png", gen_metaldiamond, 1, 1100),
    ("arcadeblue2.png",  gen_arcadeblue2,  1, 2200),
    ("carpetclown.png",  gen_carpetclown,  1, 3300),
    ("carpetoffice.png", gen_carpetoffice, 1, 4400),
    ("boxing.png",       gen_boxing,       4, 5500),
    ("gym.png",          gen_gym,          4, 6600),
    ("cave.png",         gen_cave,         7, 7700),
    ("cavedrought.png",  gen_cavedrought,  8, 8800),
    ("chromite.png",     gen_chromite,     7, 9900),
]

def main():
    rows = []
    for fname, fn, variants, base_seed in FILES:
        path = os.path.join(DST, fname)
        orig = Image.open(path).convert("RGBA")
        strip = Image.new("RGBA", (TILE * variants, TILE))
        gate_log = []
        for v in range(variants):
            # deterministic retry: a voronoi crack can legitimately land on
            # the wrap column; only variants that PASS the edge gate ship.
            for attempt in range(10):
                tile = fn(base_seed + v * 97 + attempt * 13)
                ok, msg = edge_wrap_ok(tile)
                if ok:
                    gate_log.append((v, attempt, msg))
                    break
            else:
                raise AssertionError(f"{fname} variant {v}: no passing seed")
            strip.paste(tile, (v * TILE, 0))
        assert strip.size == orig.size, f"{fname}: {strip.size} != {orig.size}"
        spx = strip.load()
        n_new = sum(1 for y in range(TILE) for x in range(TILE * variants)
                    if spx[x, y][3] > 8)
        n_old = sum(1 for y in range(TILE) for x in range(TILE * variants)
                    if orig.load()[x, y][3] > 8)
        ratio = n_new / n_old
        assert ratio >= 0.55, f"ratio gate failed {fname}: {ratio}"
        assert n_new == TILE * variants * TILE, f"{fname}: not fully opaque"
        print(f"{fname} ({variants} variants, ratio {ratio:.2f}):")
        for v, attempt, msg in gate_log:
            print(f"  variant {v} (seed try {attempt}): {msg} OK")
        strip.save(path)
        rows.append((fname, orig, strip))

    # contact sheet: ORIG above NEW per file, 4x nearest
    scale, pad, label_h = 4, 8, 18
    wmax = max(o.size[0] for _, o, _ in rows) * scale
    total_h = sum(o.size[1] * 2 * scale + label_h + pad for _, o, _ in rows) + pad
    sheet = Image.new("RGBA", (wmax + pad * 2, total_h), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    y = pad
    for fname, orig, new in rows:
        dr.text((pad, y), f"{fname} — top: ORIG (NC), bottom: NEW (CC0)",
                fill=(225, 225, 225, 255))
        y += label_h
        for im in (orig, new):
            sheet.paste(im.resize((im.size[0] * scale, im.size[1] * scale),
                                  Image.NEAREST), (pad, y))
            y += im.size[1] * scale
        y += pad
    out = os.path.join(SCR, "CONTACT-tiles.png")
    sheet.save(out)
    print("written", out)

if __name__ == "__main__":
    main()
