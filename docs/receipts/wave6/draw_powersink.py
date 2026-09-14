#!/usr/bin/env python3
"""REGEN wave 6: powersink drawn PROCEDURALLY from scratch (CC0).

The original (CC-BY-NC-SA, by Ubaser) was viewed only to DESCRIBE its subject:
a front-on symmetric dark console with a large screen, a control row, three
segmented red coil towers, gold contacts and legs. No NC pixels are read,
sampled, or conditioned on — every pixel below comes from this code.
powersink was REJECTED 3x by text-to-image across 2 lanes (always returned an
isometric box) -> capability boundary -> procedural, per the ledger.

Outputs: powersink.png (32x32 icon), inhand-left/right.png (64x64 2x2 4-dir
grids) rigged with the measured hand-anchor convention
(docs/receipts/probe-img2img/inhand_rig.py, imported directly).
"""
import os, sys
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(SCR, "..", "..", ".."))
RSI = os.path.join(ROOT, "Resources/Textures/Objects/Power/powersink.rsi")
sys.path.insert(0, os.path.join(ROOT, "docs/receipts/probe-img2img"))
from inhand_rig import rig, DIRS

# palette (SS14 light-from-top-left)
STEEL_HI  = (128, 132, 140, 255)
STEEL     = (92,  96, 104, 255)
STEEL_LO  = (58,  61,  68, 255)
STEEL_DK  = (38,  40,  46, 255)
SCREEN_BZ = (30,  31,  36, 255)
SCREEN    = (16,  12,  14, 255)
SCREEN_GL = (96,  22,  26, 255)   # faint red glow scanline
COIL_HI   = (196,  60,  60, 255)
COIL      = (148,  36,  40, 255)
COIL_LO   = (96,  22,  28, 255)
COIL_GAP  = (34,  22,  24, 255)
GOLD_HI   = (214, 178,  92, 255)
GOLD      = (168, 132,  58, 255)
LED       = (222, 196, 120, 255)

def px(d, x, y, c): d.point((x, y), fill=c)

def coil_tower(d, x, top, bot, w=3):
    """Segmented coil: alternating bright/dark red bands with dark gaps."""
    band = 0
    for y in range(top, bot + 1):
        if (y - top) % 3 == 2:
            d.line([(x, y), (x + w - 1, y)], fill=COIL_GAP)
        else:
            c = COIL_HI if band % 2 == 0 else COIL
            d.line([(x, y), (x + w - 1, y)], fill=c)
            px(d, x + w - 1, y, COIL_LO)          # right shade
            if (y - top) % 3 == 1:
                band += 1
    # rounded cap
    d.line([(x, top - 1), (x + w - 1, top - 1)], fill=STEEL_LO)

def draw_icon():
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    # --- chassis ---
    d.rectangle([7, 9, 24, 25], fill=STEEL)                    # body
    d.rectangle([7, 9, 24, 9],  fill=STEEL_HI)                 # top edge light
    d.line([(7, 10), (7, 25)],  fill=STEEL_HI)                 # left edge light
    d.line([(24, 10), (24, 25)], fill=STEEL_LO)                # right edge shade
    d.rectangle([7, 24, 24, 25], fill=STEEL_LO)                # lower shade
    # ribbed side frames
    for y in range(11, 22, 2):
        px(d, 8, y, STEEL_LO); px(d, 23, y, STEEL_DK)

    # --- screen ---
    d.rectangle([10, 11, 21, 17], fill=SCREEN_BZ)              # bezel
    d.rectangle([11, 12, 20, 16], fill=SCREEN)
    d.line([(11, 14), (20, 14)], fill=SCREEN_GL)               # glow scanline
    px(d, 12, 13, SCREEN_GL); px(d, 19, 15, SCREEN_GL)

    # --- control row ---
    for i, x in enumerate(range(10, 22, 2)):
        px(d, x, 20, LED if i in (1, 4) else STEEL_DK)
        px(d, x, 21, STEEL_DK)
    d.rectangle([10, 22, 21, 22], fill=STEEL_LO)

    # --- three coil towers ---
    coil_tower(d, 3, 8, 24)          # left flank (full height)
    coil_tower(d, 26, 8, 24)         # right flank
    coil_tower(d, 14, 2, 8, w=4)     # centre, above chassis
    # brackets tying flanks to body
    for y in (12, 19):
        px(d, 6, y, STEEL_DK); px(d, 25, y, STEEL_DK)

    # --- base skirt, gold contacts, legs ---
    d.rectangle([6, 26, 25, 27], fill=STEEL_DK)
    d.rectangle([6, 26, 25, 26], fill=STEEL_LO)
    for x in (9, 13, 17, 21):                                   # gold prongs
        px(d, x, 28, GOLD_HI); px(d, x, 29, GOLD)
    d.rectangle([6, 28, 7, 29], fill=STEEL_DK)                  # legs
    d.rectangle([24, 28, 25, 29], fill=STEEL_DK)
    return im

def main():
    icon = draw_icon()
    orig = Image.open(os.path.join(RSI, "powersink.png")).convert("RGBA")
    n_new = sum(1 for y in range(32) for x in range(32) if icon.load()[x, y][3] > 8)
    n_old = sum(1 for y in range(32) for x in range(32) if orig.load()[x, y][3] > 8)
    ratio = n_new / n_old
    print(f"opaque-pixel-ratio new/old = {n_new}/{n_old} = {ratio:.2f}")
    assert ratio >= 0.55, "DUAL ART GATE (ratio) failed"

    frames = rig(icon, rotate=0, grow=1.9)

    out = os.path.join(SCR, "powersink-proc")
    os.makedirs(out, exist_ok=True)
    icon.save(os.path.join(out, "powersink.png"))
    for name, im in frames.items():
        assert im.size == (64, 64)
        im.save(os.path.join(out, f"{name}.png"))

    # contact sheet: original left (VIEW reference), procedural right + inhands
    cells = [("ORIG (NC)", orig), ("NEW (CC0)", icon)]
    for name, im in frames.items():
        for i, dd in enumerate(DIRS):
            cells.append((f"{name.split('-')[1][:1]}-{dd}",
                          im.crop(((i % 2) * 32, (i // 2) * 32,
                                   (i % 2) * 32 + 32, (i // 2) * 32 + 32))))
    sheet = Image.new("RGBA", (len(cells) * 104 + 4, 126), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (lbl, c) in enumerate(cells):
        sheet.paste(c.resize((96, 96), Image.NEAREST), (4 + i * 104, 4))
        dr.text((4 + i * 104, 108), lbl, fill=(225, 225, 225, 255))
    sheet.save(os.path.join(SCR, "CONTACT-powersink.png"))
    print("written", out)

if __name__ == "__main__":
    main()
