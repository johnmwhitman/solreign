#!/usr/bin/env python3
"""LICENSE BATCH C: 6 NC parallax layers regenerated PROCEDURALLY (CC0).

Replaces (all confirmed USED — PlasmaStation/BagelStation/OriginStation/
AmberStation/ExoStation/TrainStation are in the main-menu background
allowlist in Content.Client/MainMenu/UI/MainMenuControl.xaml.cs, and
plasma/bagel/exo ship as loadable maps):

  planet.png          480x480  untiled disc (plasma.yml, bagel.yml: tiled false)
  gas_giant.png       480x480  untiled disc (origin.yml: tiled false)
  Asteroids.png       480x480  TILED scatter (plasma/amber/train.yml, default Tiled=true)
  debris_small.png    480x480  TILED sparse scraps (amber.yml)
  space_map3.png     1024x1024 TILED opaque deep-space field (exo.yml)
  XenoParallaxNeb.png 1024x1024 TILED nebula puffs (exo.yml)

(core_planet.png is NOT regenerated: its only user, the CoreStation
prototype, is referenced nowhere — deleted alongside core.yml.)

The NC originals were VIEWED only to describe SUBJECTS and unprotectable
technical facts: canvas sizes, alpha conventions (discs/scatter on
transparency; space_map3 fully opaque), tiled-vs-untiled per the prototype
configs (Content.Client/Parallax/Data/ParallaxLayerConfig.cs: Tiled
defaults true), palette moods (dark lava planet + small icy moon + debris
ring; purple marbled giant + dust ring + green moonlet; shaded gray
asteroid scatter; a handful of tiny dark scraps; near-black blue/purple
deep space; pale lavender cloud puffs). No NC pixels are read, sampled,
traced, img2img'd or conditioned on — every output pixel comes from this
code. Fresh compositions authored here.

Determinism: numpy default_rng with fixed seeds + periodic lattice value
noise (period == canvas), so every tiled output wraps EXACTLY by
construction; an edge-wrap gate verifies it anyway.

Gates:
  - size gate: output size == original size, RGBA
  - opacity-ratio gate: opaque(alpha>8) count >= 0.55x the original's
  - full-opacity gate for space_map3 (original fully opaque)
  - EDGE-WRAP gate (tiled files only): wrapped seam (last col/row -> first)
    alpha-weighted luminance step <= 1.05x strongest interior step + 1.0

Outputs overwrite Resources/Textures/Parallaxes/*.png in place and write
CONTACT-parallax.png (ORIG above NEW per file) next to this script.
"""
import os
import numpy as np
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(SCR, "..", "..", ".."))
DST = os.path.join(ROOT, "Resources/Textures/Parallaxes")

# ----------------------------------------------------------------- noise kit
def vnoise(size, cells, rng):
    """Periodic (torus) value noise, size x size, lattice cells x cells."""
    lat = rng.random((cells, cells))
    idx = np.arange(size) * cells / size
    i0 = np.floor(idx).astype(int) % cells
    i1 = (i0 + 1) % cells
    t = idx - np.floor(idx)
    t = t * t * (3 - 2 * t)
    # gather rows/cols
    a = lat[np.ix_(i0, i0)]
    b = lat[np.ix_(i0, i1)]
    c = lat[np.ix_(i1, i0)]
    d = lat[np.ix_(i1, i1)]
    tx = t[np.newaxis, :]
    ty = t[:, np.newaxis]
    ab = a + (b - a) * tx
    cd = c + (d - c) * tx
    return ab + (cd - ab) * ty

def fbm(size, rng, octaves):
    """octaves = [(cells, weight), ...]; normalized to [0,1]."""
    out = np.zeros((size, size))
    wsum = 0.0
    for cells, w in octaves:
        out += vnoise(size, cells, rng) * w
        wsum += w
    return out / wsum

def clamp255(a):
    return np.clip(a, 0, 255).astype(np.uint8)

def draw_disc_shading(size, cx, cy, r):
    """Return (dist, mask, nz) grids for a sphere at cx,cy radius r."""
    yy, xx = np.mgrid[0:size, 0:size].astype(float)
    dist = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)
    mask = dist < r
    nz = np.zeros((size, size))
    inside = np.clip(1 - (dist / r) ** 2, 0, 1)
    nz = np.sqrt(inside)
    return xx, yy, dist, mask, nz

# ------------------------------------------------------------------- planet
def gen_planet(size=480):
    """Fresh subject take: dark molten world w/ glowing fissures, debris
    ring, small icy moon. Untiled (single composition on transparency)."""
    rng = np.random.default_rng(51001)
    img = np.zeros((size, size, 4), dtype=float)

    cx, cy, r = 222.0, 226.0, 136.0
    xx, yy, dist, mask, nz = draw_disc_shading(size, cx, cy, r)

    # ring: tilted ellipse of rocky specks; back half first, front half last
    ring = []
    ang = np.deg2rad(-18.0)
    ca, sa = np.cos(ang), np.sin(ang)
    for k in range(950):
        t = rng.random() * 2 * np.pi
        a_r = 205 + rng.normal(0, 9)
        b_r = 58 + rng.normal(0, 5)
        ex, ey = a_r * np.cos(t), b_r * np.sin(t)
        px = cx + ex * ca - ey * sa
        py = cy + ex * sa + ey * ca
        if 0 <= px < size and 0 <= py < size:
            g = 60 + rng.integers(0, 60)
            sz = 1 + (rng.random() < 0.22)
            ring.append((int(px), int(py), int(g), int(sz), ey > 0))

    def blit_speck(px_, py_, g, sz):
        img[py_:py_ + sz, px_:px_ + sz, 0] = g
        img[py_:py_ + sz, px_:px_ + sz, 1] = g * 0.94
        img[py_:py_ + sz, px_:px_ + sz, 2] = g * 0.90
        img[py_:py_ + sz, px_:px_ + sz, 3] = 255

    for px_, py_, g, sz, front in ring:
        if not front:
            blit_speck(px_, py_, g, sz)

    # planet body: dark basalt with glowing fissures
    mot = fbm(size, rng, [(12, 1.0), (24, 0.55), (48, 0.3)])
    ridge = 1 - np.abs(2 * fbm(size, rng, [(10, 1.0), (20, 0.6), (40, 0.35)]) - 1)
    vein = np.clip((ridge - 0.86) / 0.14, 0, 1) ** 1.7

    base_r = 34 + mot * 22
    base_g = 26 + mot * 14
    base_b = 28 + mot * 14
    lava_r, lava_g, lava_b = 244, 120, 36
    body_r = base_r + (lava_r - base_r) * vein
    body_g = base_g + (lava_g - base_g) * vein
    body_b = base_b + (lava_b - base_b) * vein
    # lighting: top-left key + limb darkening
    lx = np.clip(((cx - xx) * 0.45 + (cy - yy) * 0.45) / r, -1, 1)
    light = 0.55 + 0.45 * nz + 0.18 * lx
    for ch, arr in ((0, body_r), (1, body_g), (2, body_b)):
        img[..., ch] = np.where(mask, arr * light, img[..., ch])
    img[..., 3] = np.where(mask, 255, img[..., 3])

    # warm glow halo just outside the disc
    halo_band = (dist >= r) & (dist < r + 14)
    fall = np.clip(1 - (dist - r) / 14.0, 0, 1) ** 2
    ga = fall * 120
    for ch, gc in ((0, 220), (1, 70), (2, 30)):
        img[..., ch] = np.where(halo_band & (img[..., 3] < 10),
                                gc, img[..., ch])
    img[..., 3] = np.where(halo_band & (img[..., 3] < 10),
                           ga, img[..., 3])

    for px_, py_, g, sz, front in ring:
        if front:
            blit_speck(px_, py_, g, sz)

    # icy moon lower-right
    mx, my, mr = 372.0, 338.0, 27.0
    _, _, mdist, mmask, mnz = draw_disc_shading(size, mx, my, mr)
    mm = fbm(size, rng, [(24, 1.0), (48, 0.5)])
    ml = 0.5 + 0.5 * mnz
    img[..., 0] = np.where(mmask, (188 + mm * 40) * ml, img[..., 0])
    img[..., 1] = np.where(mmask, (204 + mm * 34) * ml, img[..., 1])
    img[..., 2] = np.where(mmask, (222 + mm * 26) * ml, img[..., 2])
    img[..., 3] = np.where(mmask, 255, img[..., 3])
    return clamp255(img)

# ---------------------------------------------------------------- gas giant
def gen_gas_giant(size=480):
    """Fresh take: purple banded gas giant, sparse diagonal dust ring,
    tiny green moonlet. Untiled."""
    rng = np.random.default_rng(52002)
    img = np.zeros((size, size, 4), dtype=float)
    cx, cy, r = 244.0, 238.0, 142.0
    xx, yy, dist, mask, nz = draw_disc_shading(size, cx, cy, r)

    # ring specks (back half then front half around body)
    ring = []
    ang = np.deg2rad(24.0)
    ca, sa = np.cos(ang), np.sin(ang)
    for k in range(700):
        t = rng.random() * 2 * np.pi
        a_r = 208 + rng.normal(0, 7)
        b_r = 34 + rng.normal(0, 4)
        ex, ey = a_r * np.cos(t), b_r * np.sin(t)
        px = cx + ex * ca - ey * sa
        py = cy + ex * sa + ey * ca
        if 0 <= px < size and 0 <= py < size:
            g = 90 + rng.integers(0, 70)
            ring.append((int(px), int(py), int(g), ey > 0))

    def speck(px_, py_, g):
        img[py_, px_] = (g * 0.9, g * 0.8, g, 255)

    for px_, py_, g, front in ring:
        if not front:
            speck(px_, py_, g)

    # banded, swirled surface: distort latitude with noise
    warp = fbm(size, rng, [(6, 1.0), (12, 0.6), (24, 0.4), (48, 0.2)])
    lat = (yy - (cy - r)) / (2 * r)          # 0..1 across the disc
    band_t = lat * 9.0 + (warp - 0.5) * 2.4  # wavy band index
    bandv = 0.5 + 0.5 * np.sin(band_t * np.pi)
    detail = fbm(size, rng, [(32, 1.0), (64, 0.6)])
    bandv = np.clip(bandv * 0.75 + detail * 0.25, 0, 1)

    # purple family palette lerp (authored fresh)
    lo = np.array([52, 26, 82])
    mid = np.array([116, 58, 158])
    hi = np.array([172, 110, 208])
    col = np.zeros((size, size, 3))
    tlow = np.clip(bandv * 2, 0, 1)
    thigh = np.clip(bandv * 2 - 1, 0, 1)
    for ch in range(3):
        c1 = lo[ch] + (mid[ch] - lo[ch]) * tlow
        col[..., ch] = c1 + (hi[ch] - c1) * thigh
    light = 0.5 + 0.5 * nz
    edge_glow = np.clip((dist / r - 0.94) / 0.06, 0, 1) * np.where(mask, 1, 0)
    for ch in range(3):
        v = col[..., ch] * light + edge_glow * 40
        img[..., ch] = np.where(mask, v, img[..., ch])
    img[..., 3] = np.where(mask, 255, img[..., 3])

    # faint cool halo
    halo = (dist >= r) & (dist < r + 10)
    fall = np.clip(1 - (dist - r) / 10.0, 0, 1) ** 2
    img[..., 0] = np.where(halo & (img[..., 3] < 10), 120, img[..., 0])
    img[..., 1] = np.where(halo & (img[..., 3] < 10), 70, img[..., 1])
    img[..., 2] = np.where(halo & (img[..., 3] < 10), 160, img[..., 2])
    img[..., 3] = np.where(halo & (img[..., 3] < 10), fall * 90, img[..., 3])

    for px_, py_, g, front in ring:
        if front:
            speck(px_, py_, g)

    # tiny green-teal moonlet upper-left
    mx, my, mr = 96.0, 88.0, 11.0
    _, _, _, mmask, mnz = draw_disc_shading(size, mx, my, mr)
    ml = 0.45 + 0.55 * mnz
    img[..., 0] = np.where(mmask, 46 * ml + 20, img[..., 0])
    img[..., 1] = np.where(mmask, 150 * ml + 30, img[..., 1])
    img[..., 2] = np.where(mmask, 108 * ml + 20, img[..., 2])
    img[..., 3] = np.where(mmask, 255, img[..., 3])
    return clamp255(img)

# ---------------------------------------------------------------- asteroids
def gen_asteroids(size=480):
    """Tiled scatter of shaded rocky asteroids + specks. Torus drawing."""
    rng = np.random.default_rng(53003)
    img = np.zeros((size, size, 4), dtype=float)

    def draw_rock(acx, acy, R):
        nlobes = 2 + int(rng.integers(0, 2))
        ph = rng.random() * 2 * np.pi
        amp = 0.07 + rng.random() * 0.07
        amp2 = 0.04 * rng.random()
        ph2 = rng.random() * 2 * np.pi
        bb = int(R * 1.3) + 2
        crat = []
        for _ in range(1 + int(rng.integers(0, 3))):
            ca_ = rng.random() * 2 * np.pi
            cd = rng.random() * R * 0.5
            crat.append((cd * np.cos(ca_), cd * np.sin(ca_),
                         R * (0.16 + rng.random() * 0.2)))
        gbase = 96 + rng.integers(0, 34)
        for dy in range(-bb, bb + 1):
            for dx in range(-bb, bb + 1):
                d = np.hypot(dx, dy)
                if d < 0.001:
                    d = 0.001
                th = np.arctan2(dy, dx)
                rr = R * (1 + amp * np.sin(nlobes * th + ph)
                          + amp2 * np.sin((nlobes + 2) * th + ph2))
                if d <= rr:
                    # strong key light from top-left
                    lit = 1.0 + 0.75 * (-dx - dy) / (1.5 * R)
                    edge = d / rr
                    lit *= 1.0 - 0.35 * edge ** 2.5
                    g = gbase * max(0.25, lit)
                    for ccx, ccy, cr in crat:
                        cdst = np.hypot(dx - ccx, dy - ccy)
                        if cdst < cr:
                            g *= 0.66
                        elif cdst < cr * 1.35:
                            g *= 1.10
                    if edge > 0.93:
                        g *= 0.70          # thin dark rim
                    x_, y_ = (acx + dx) % size, (acy + dy) % size
                    img[y_, x_] = (g, g * 0.98, g * 0.94, 255)

    # non-overlapping-ish placements on the torus
    placed = []
    tries = 0
    while len(placed) < 26 and tries < 900:
        tries += 1
        R = 11 + rng.random() * 20
        ax = rng.integers(0, size)
        ay = rng.integers(0, size)
        ok = True
        for bx, by, br in placed:
            ddx = min(abs(ax - bx), size - abs(ax - bx))
            ddy = min(abs(ay - by), size - abs(ay - by))
            if np.hypot(ddx, ddy) < (R + br) * 1.12:
                ok = False
                break
        if ok:
            placed.append((int(ax), int(ay), R))
    for ax, ay, R in placed:
        draw_rock(ax, ay, R)

    # tiny specks
    for _ in range(55):
        sx = int(rng.integers(0, size))
        sy = int(rng.integers(0, size))
        g = 60 + rng.integers(0, 50)
        sz = 1 + (rng.random() < 0.3)
        for dy in range(sz):
            for dx in range(sz):
                img[(sy + dy) % size, (sx + dx) % size] = (g, g, g * 0.95, 255)
    return clamp255(img)

# ------------------------------------------------------------- debris small
def gen_debris_small(size=480):
    """Tiled: a handful of tiny dark scrap glyphs (bent struts). Torus."""
    rng = np.random.default_rng(54004)
    img = np.zeros((size, size, 4), dtype=float)

    def seg(x0, y0, ang_, ln, thick, g):
        for t in np.arange(0, ln, 0.5):
            fx = x0 + np.cos(ang_) * t
            fy = y0 + np.sin(ang_) * t
            for oy in range(thick):
                for ox in range(thick):
                    img[int(fy + oy) % size, int(fx + ox) % size] = \
                        (g, g * 1.03, g * 1.08, 255)
        return (x0 + np.cos(ang_) * ln, y0 + np.sin(ang_) * ln)

    for k in range(9):
        x = float(rng.integers(0, size))
        y = float(rng.integers(0, size))
        ang_ = rng.random() * 2 * np.pi
        g = 52 + rng.integers(0, 26)
        nseg = 3 + int(rng.integers(0, 2))
        for s in range(nseg):
            ln = 6 + rng.random() * 8
            thick = 1 + (rng.random() < 0.6)
            x, y = seg(x, y, ang_, ln, int(thick), g)
            ang_ += (rng.random() - 0.5) * 1.9
        # one highlight pixel
        img[int(y) % size, int(x) % size] = (118, 122, 130, 255)
    return clamp255(img)

# ---------------------------------------------------------------- space map
def gen_space_map3(size=1024):
    """Tiled fully-opaque deep-space field: near-black indigo base, two
    faint periodic nebula washes, sparse hashed stars."""
    rng = np.random.default_rng(55005)
    neb1 = fbm(size, rng, [(8, 1.0), (16, 0.6), (32, 0.35), (64, 0.2)])
    neb2 = fbm(size, rng, [(8, 1.0), (16, 0.6), (32, 0.35), (64, 0.2)])
    w1 = np.clip(neb1 - 0.52, 0, 1) ** 1.5 * 2.2
    w2 = np.clip(neb2 - 0.55, 0, 1) ** 1.5 * 2.2
    img = np.zeros((size, size, 4), dtype=float)
    img[..., 0] = 7 + w1 * 10 + w2 * 34
    img[..., 1] = 8 + w1 * 30 + w2 * 10
    img[..., 2] = 15 + w1 * 40 + w2 * 46
    img[..., 3] = 255

    n_stars = 1500
    sx = rng.integers(0, size, n_stars)
    sy = rng.integers(0, size, n_stars)
    for i in range(n_stars):
        b = 40 + rng.random() * 140
        tint = rng.random()
        c = (b, b, min(255, b * 1.15)) if tint < 0.6 else (
            (min(255, b * 1.1), b, b * 0.85) if tint < 0.8 else (b, b, b))
        x, y = int(sx[i]), int(sy[i])
        img[y, x, :3] = np.maximum(img[y, x, :3], c)
        if b > 150:  # bright star: faint cross halo (torus)
            h = b * 0.35
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                yy_, xx_ = (y + dy) % size, (x + dx) % size
                img[yy_, xx_, :3] = np.maximum(img[yy_, xx_, :3], (h, h, h))
    return clamp255(img)

# -------------------------------------------------------------- xeno nebula
def gen_xeno_neb(size=1024):
    """Tiled pale-lavender cloud puffs on transparency (~15-20% coverage)."""
    rng = np.random.default_rng(56006)
    clus = fbm(size, rng, [(8, 1.0), (16, 0.5)])          # scattered clusters
    puff = fbm(size, rng, [(16, 1.0), (32, 0.6), (64, 0.35), (128, 0.18)])
    field = np.clip(clus - 0.44, 0, 1) ** 1.15 * puff
    thr = 0.075
    alpha = np.clip((field - thr) / 0.10, 0, 1)
    alpha = (alpha ** 0.8) * 205
    body = np.clip((puff - 0.35) / 0.5, 0, 1)
    img = np.zeros((size, size, 4), dtype=float)
    img[..., 0] = 186 + body * 44
    img[..., 1] = 180 + body * 46
    img[..., 2] = 218 + body * 32
    img[..., 3] = alpha
    return clamp255(img)

# ======================================================================= run
def lum_a(row):
    """Alpha-weighted luminance for a row/col of RGBA float pixels."""
    a = row[..., 3] / 255.0
    return (0.299 * row[..., 0] + 0.587 * row[..., 1] + 0.114 * row[..., 2]) * a

def edge_wrap_gate(arr, name):
    f = arr.astype(float)
    size = f.shape[0]
    colsteps = [np.mean(np.abs(lum_a(f[:, x]) - lum_a(f[:, x + 1])))
                for x in range(size - 1)]
    rowsteps = [np.mean(np.abs(lum_a(f[y]) - lum_a(f[y + 1])))
                for y in range(size - 1)]
    wrap_c = np.mean(np.abs(lum_a(f[:, -1]) - lum_a(f[:, 0])))
    wrap_r = np.mean(np.abs(lum_a(f[-1]) - lum_a(f[0])))
    ic, ir = max(colsteps), max(rowsteps)
    ok = wrap_c <= ic * 1.05 + 1.0 and wrap_r <= ir * 1.05 + 1.0
    print(f"  {name}: wrap col {wrap_c:6.3f}/{ic:6.3f} "
          f"row {wrap_r:6.3f}/{ir:6.3f} {'OK' if ok else 'FAIL'}")
    assert ok, f"EDGE-WRAP gate failed for {name}"

FILES = [
    ("planet.png",          gen_planet,       False),
    ("gas_giant.png",       gen_gas_giant,    False),
    ("Asteroids.png",       gen_asteroids,    True),
    ("debris_small.png",    gen_debris_small, True),
    ("space_map3.png",      gen_space_map3,   True),
    ("XenoParallaxNeb.png", gen_xeno_neb,     True),
]

def main():
    rows = []
    for fname, fn, tiled in FILES:
        path = os.path.join(DST, fname)
        orig = Image.open(path).convert("RGBA")
        arr = fn()
        im = Image.fromarray(arr, "RGBA")
        assert im.size == orig.size, f"{fname}: {im.size} != {orig.size}"
        n_new = int(np.sum(arr[..., 3] > 8))
        oarr = np.asarray(orig)
        n_old = int(np.sum(oarr[..., 3] > 8))
        ratio = n_new / max(1, n_old)
        print(f"{fname}: opaque {n_new} vs orig {n_old} (ratio {ratio:.2f}), "
              f"tiled={tiled}")
        assert ratio >= 0.55, f"ratio gate failed {fname}: {ratio:.2f}"
        if fname == "space_map3.png":
            assert n_new == im.size[0] * im.size[1], "space_map3 must be opaque"
        if tiled:
            edge_wrap_gate(arr, fname)
        im.save(path)
        rows.append((fname, orig, im))

    # contact sheet: ORIG above NEW, 256px thumbs on dark bg
    th, pad, label_h = 256, 10, 18
    cols = 3
    rws = (len(rows) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * (th + pad) + pad,
                               rws * (2 * th + label_h + pad) + pad),
                      (16, 16, 22, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (fname, orig, new) in enumerate(rows):
        x = pad + (i % cols) * (th + pad)
        y = pad + (i // cols) * (2 * th + label_h + pad)
        dr.text((x, y), f"{fname} — top ORIG (NC) / bottom NEW (CC0)",
                fill=(225, 225, 225, 255))
        sheet.paste(orig.resize((th, th)), (x, y + label_h))
        sheet.paste(new.resize((th, th)), (x, y + label_h + th))
    out = os.path.join(SCR, "CONTACT-parallax.png")
    sheet.save(out)
    print("written", out)

if __name__ == "__main__":
    main()
