#!/usr/bin/env python3
"""Build the Corporate Unicorn RSI from chibi concepts.

Pipeline per frame: key-out bg -> crop/pad -> LANCZOS downscale to 30px ->
snap every opaque pixel to a curated flat palette (kills AI-downscale mud,
restores game-fidelity flats) -> place in 32x32.

Two-layer split (matches Mobs/Animals/sheep.rsi contract so it drops into
MobSheepBase's GenericVisualizer + DamageStateVisuals unchanged):
  base  = sheep_bare  : full unicorn body incl. lavender mane (4 dir)
  wool  = sheep_wool  : the mane pixels only, recolored near-white so
                        RgbLightController (layers:[1]) tints them rainbow (4 dir)
  sheep_dead / sheep_wool_dead : 1-dir belly-up pose + its mane
  icon  : 1-dir spawn-menu icon (side profile)

Direction order (row-major 2x2, verified vs upstream): S, N, E, W. W mirrors E.
"""
import os, sys, json
from PIL import Image
sys.path.insert(0, os.path.expanduser("~/AI/Tools"))
import solreign_sprite as ss

WORK = os.path.expanduser("~/AI/solreign-trees/unicorn-sprite/assets/unicorn-work")
CONC = os.path.join(WORK, "_concepts")
RSI  = os.path.expanduser("~/AI/solreign-trees/unicorn-sprite/Resources/Textures/_Solreign/unicorn.rsi")

TRANSP = (0, 0, 0, 0)
# Curated palette. Order matters only for nearest-match ties.
OUTLINE   = (40, 32, 58)
BODY      = (255, 255, 255)
BODY_SH   = (226, 223, 238)
MANE_LT   = (222, 205, 242)
MANE_MID  = (184, 156, 216)
MANE_DK   = (140, 112, 176)
HORN      = (244, 196, 78)
HORN_DK   = (212, 146, 44)
TIE       = (126, 217, 87)
TIE_DK    = (78, 168, 50)
HOOF      = (122, 114, 136)
EYE       = (40, 32, 58)
PALETTE = [OUTLINE, BODY, BODY_SH, MANE_LT, MANE_MID, MANE_DK,
           HORN, HORN_DK, TIE, TIE_DK, HOOF, EYE]
MANE_COLORS = {MANE_LT, MANE_MID, MANE_DK}
WOOL_WHITE = (250, 250, 252)   # near-white so RgbLightController -> vivid rainbow

def snap(rgb):
    r, g, b = rgb
    best, bd = PALETTE[0], 1e9
    for c in PALETTE:
        # perceptual-ish weighting (green heavier)
        d = 2*(r-c[0])**2 + 4*(g-c[1])**2 + 3*(b-c[2])**2
        if d < bd:
            bd, best = d, c
    return best

def frame(concept, mirror=False, size=32, pad=1, tol=48):
    img = ss.key_out_background(Image.open(concept), tol)
    img = ss.crop_pad_square(img, 0.03)
    inner = size - 2*pad
    img = img.resize((inner, inner), Image.LANCZOS)
    canvas = Image.new("RGBA", (size, size), TRANSP)
    canvas.paste(img, (pad, pad))
    px = canvas.load()
    for y in range(size):
        for x in range(size):
            r, g, bl, a = px[x, y]
            if a < 128:
                px[x, y] = TRANSP
            else:
                px[x, y] = snap((r, g, bl)) + (255,)
    if mirror:
        canvas = canvas.transpose(Image.FLIP_LEFT_RIGHT)
    return canvas

def mane_layer(base):
    """Extract mane pixels -> near-white wool layer (rest transparent)."""
    size = base.size[0]
    out = Image.new("RGBA", base.size, TRANSP)
    bp, op = base.load(), out.load()
    n = 0
    for y in range(size):
        for x in range(size):
            r, g, b, a = bp[x, y]
            if a > 0 and (r, g, b) in MANE_COLORS:
                # shade: keep the darker mane strands a touch grey for depth
                if (r, g, b) == MANE_DK:
                    op[x, y] = (past(WOOL_WHITE, 0.82)) + (255,)
                elif (r, g, b) == MANE_MID:
                    op[x, y] = (past(WOOL_WHITE, 0.92)) + (255,)
                else:
                    op[x, y] = WOOL_WHITE + (255,)
                n += 1
    return out, n

def past(c, f):
    return tuple(max(0, min(255, int(round(v*f)))) for v in c)

def pack4(s, n, e, w):
    sh = Image.new("RGBA", (64, 64), TRANSP)
    for i, f in enumerate([s, n, e, w]):
        sh.paste(f, ((i % 2)*32, (i // 2)*32))
    return sh

if __name__ == "__main__":
    os.makedirs(RSI, exist_ok=True)
    S = frame(f"{CONC}/uni_s2.png")
    N = frame(f"{CONC}/uni_n2.png")
    E = frame(f"{CONC}/uni_e2.png")
    W = frame(f"{CONC}/uni_e2.png", mirror=True)
    DEAD = frame(f"{CONC}/uni_dead2.png")

    base_sheet = pack4(S, N, E, W)
    wS, nS = mane_layer(S); wN, _ = mane_layer(N)
    wE, _ = mane_layer(E);  wW, _ = mane_layer(W)
    wool_sheet = pack4(wS, wN, wE, wW)
    wDEAD, _ = mane_layer(DEAD)

    base_sheet.save(f"{RSI}/sheep_bare.png")
    wool_sheet.save(f"{RSI}/sheep_wool.png")
    DEAD.save(f"{RSI}/sheep_dead.png")
    wDEAD.save(f"{RSI}/sheep_wool_dead.png")
    # icon: side profile, single frame
    E.save(f"{RSI}/icon.png")

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Solreign (AI-generated, human-reviewed). Corporate Unicorn — "
                     "Imagen 4 (Vertex) chibi concepts, LANCZOS downscale + curated "
                     "palette-snap, mane split to wool layer for RgbLightController. "
                     "No third-party art conditioned on.",
        "size": {"x": 32, "y": 32},
        "states": [
            {"name": "sheep_bare", "directions": 4,
             "delays": [[1], [1], [1], [1]]},
            {"name": "sheep_wool", "directions": 4,
             "delays": [[1], [1], [1], [1]]},
            {"name": "sheep_dead", "delays": [[1]]},
            {"name": "sheep_wool_dead", "delays": [[1]]},
            {"name": "icon", "delays": [[1]]},
        ],
    }
    with open(f"{RSI}/meta.json", "w") as fp:
        json.dump(meta, fp, indent=2); fp.write("\n")
    print("wrote RSI to", RSI)
    print("mane px  S,N,E,W:", nS)
