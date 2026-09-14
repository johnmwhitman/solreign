#!/usr/bin/env python3
"""REGEN wave 6: Interface/Default HUD icon set drawn PROCEDURALLY from scratch (CC0).

Replaces the goonstation-derived CC-BY-NC-SA-3.0 set (slot glyphs, slot chrome,
storage-grid chrome, list arrows). Originals were VIEWED only to describe their
SUBJECTS (a backpack, a belt, an ear, ...) and to read technical parameters
(canvas sizes, alpha conventions, HUD color family). Every glyph below is an
original ASCII-art design authored in this file; every chrome tile is drawn by
code. No NC pixels are read, sampled, traced, or conditioned on.

Drop-in: identical filenames (camo.png/contra.png are hardcoded in
Content.Client/Inventory/StrippableBoundUserInterface.cs; the rest are referenced
by the HUD theme system). Outputs land in iface-default/ mirroring the RSI-less
folder layout, then get copied into Resources/Textures/Interface/Default/.
"""
import os
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(SCR, "..", "..", ".."))
SRC = os.path.join(ROOT, "Resources/Textures/Interface/Default")
OUT = os.path.join(SCR, "iface-default")

# ---------------------------------------------------------------- palette
PAL = {
    "O": (30, 38, 44, 255),     # glyph outline
    "B": (96, 124, 140, 255),   # glyph base (muted steel blue, HUD family)
    "S": (66, 90, 104, 255),    # glyph shade
    "H": (128, 156, 170, 255),  # glyph highlight
    "W": (255, 255, 255, 255),  # white (marker frames)
    "T": (76, 130, 129, 255),   # HUD teal (highlight chrome family)
    "R": (210, 22, 22, 255),    # storage red
    "r": (119, 3, 3, 255),      # storage red outline
    "G": (247, 220, 27, 255),   # gold star
    "g": (172, 112, 19, 255),   # gold star outline
    "N": (200, 200, 200, 255),  # gray star
    "n": (110, 110, 110, 255),  # gray star outline
}
FRAME_BORDER = (56, 56, 62, 244)
FRAME_INNER = (22, 22, 26, 252)
STRIPE_A = (39, 39, 45, 244)
STRIPE_B = (30, 30, 35, 244)

# ---------------------------------------------------------------- helpers
def art(s, extra=None):
    """ASCII art -> RGBA image. '.' transparent; letters per PAL/extra."""
    pal = dict(PAL)
    if extra:
        pal.update(extra)
    rows = [r for r in s.strip("\n").split("\n")]
    w = max(len(r) for r in rows)
    im = Image.new("RGBA", (w, len(rows)), (0, 0, 0, 0))
    px = im.load()
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch != ".":
                px[x, y] = pal[ch]
    return im

FONT = {  # tiny 3x5, original
    "L": ["X..", "X..", "X..", "X..", "XXX"],
    "R": ["XX.", "X.X", "XX.", "X.X", "X.X"],
    "C": [".XX", "X..", "X..", "X..", ".XX"],
    "!": [".X.", ".X.", ".X.", "...", ".X."],
}

def stamp_letter(im, ch, xy, color):
    px = im.load()
    for dy, row in enumerate(FONT[ch]):
        for dx, c in enumerate(row):
            if c == "X":
                px[xy[0] + dx, xy[1] + dy] = color

def slot_frame(tint=None):
    """My own 32x32 slot panel: striped interior, double border, cut corners."""
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    a, b = (tint or (STRIPE_A, STRIPE_B))
    for y in range(2, 30):
        d.line([(2, y), (29, y)], fill=(a if (y // 2) % 2 == 0 else b))
    d.rectangle([0, 0, 31, 31], outline=FRAME_BORDER)
    d.rectangle([1, 1, 30, 30], outline=FRAME_INNER)
    px = im.load()
    for cx, cy in ((0, 0), (31, 0), (0, 31), (31, 31)):  # cut corners
        px[cx, cy] = (0, 0, 0, 0)
    return im

def center(base, glyph, dy=0):
    x = (base.width - glyph.width) // 2
    y = (base.height - glyph.height) // 2 + dy
    base.paste(glyph, (x, y), glyph)
    return base

def save(rel, im):
    p = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.save(p)
    return rel

# ---------------------------------------------------------------- glyphs (all original designs)
GLYPHS = {}

GLYPHS["back"] = """
.....OOOOOO.....
....OBBBBBBO....
...OBBHBBHBBO...
..OSBBBBBBBBSO..
..OSBBBBBBBBSO..
..OSBBBBBBBBSO..
..OSBBBBBBBBSO..
..OSBBBBBBBBSO..
..OBBOOOOOOBBO..
..OBBOBHHBOBBO..
..OBBOBBBBOBBO..
..OBBOBBBBOBBO..
..OBBOOOOOOBBO..
..OBBBBBBBBBBO..
...OBBBBBBBBO...
....OOOOOOOO....
"""

GLYPHS["belt"] = """
OOOOOOOOOOOOOOOOOO
OBBBBBBBBBBBBBBBBO
OSBBBOOOOOOOOBBBSO
OSBBBOBHHHHBOBBBSO
OSBBBOBBBBBBOBBBSO
OSBBBOOOOOOOOBBBSO
OBBBBBBBBBBBBBBBBO
OOOOOOOOOOOOOOOOOO
"""

GLYPHS["ears"] = """
...OOOOOO...
..OBBBBBBO..
.OBBBBBBBBO.
.OBBSSSBBBO.
.OBSBBBSBBO.
.OBSBBBSBBO.
.OBSBBSBBO..
.OBBSSBBBO..
.OBBBBBBO...
.OBBBBBO....
.OBBBBO.....
.OBBBBO.....
.OBBBBBO....
..OBBBBO....
...OOOO.....
"""

GLYPHS["glasses"] = """
OO................OO
.OO..............OO.
..OOOOOOOOOOOOOOOO..
.OBBBBBOO..OOBBBBBO.
.OBHBBBO....OBHBBBO.
.OBBBBBO....OBBBBBO.
..OBBBO......OBBBO..
...OOO........OOO...
"""

GLYPHS["gloves"] = """
..OOOO...OOOO..
.OBBBBO.OBBBBO.
.OBBBBO.OBBBBO.
.OBBBBO.OBBBBO.
OOBBBBO.OBBBBOO
OBOBBBBOBBBBOBO
OBBBBBBOBBBBBBO
.OBBBBBOBBBBBO.
.OSSSSBOBSSSSO.
..OOOOO.OOOOO..
"""

GLYPHS["head"] = """
......OOOO......
....OOBBBBOO....
...OBBBBBBBBO...
..OBBHBBBBBBBO..
..OBBBBBBBBBBO..
.OBBBBBBBBBBBBO.
.OBBBBBBBBBBBBO.
.OOOOOOOOOOOOOO.
.OSSSSSSSSSSSSO.
.OSSSSSSSSSSSSO.
..OOOOOOOOOOOO..
"""

GLYPHS["id"] = """
OOOOOOOOOOOOOOOO
OBBBBBBBBBBBBBBO
OBHHHHBSSSSSSBBO
OBHSSHBBBBBBBBBO
OBHSSHBSSSSBBBBO
OBHHHHBBBBBBBBBO
OBBBBBBSSSSSSBBO
OBBBBBBBBBBBBBBO
OBSSSSSSSSSBBBBO
OBBBBBBBBBBBBBBO
OOOOOOOOOOOOOOOO
"""

GLYPHS["mask"] = """
O....OOOOOO....O
OO..OBBBBBBO..OO
.OOOBBBBBBBBOOO.
..OBBSSBBSSBBO..
..OBBSHBBSHBBO..
.OBBBSSBBSSBBBO.
.OBBBBBBBBBBBBO.
.OBBBBBBBBBBBBO.
..OBBSSSSSSBBO..
..OBSBBBBBBSBO..
..OBSBBBBBBSBO..
...OBSSSSSSBO...
....OBBBBBBO....
.....OOOOOO.....
"""

GLYPHS["neck"] = """
........OOOO.
.......OBBBO.
......OBBBBO.
.....OBBBBO..
.....OBBBBO..
....OBBBBO...
....OBBBBO...
...OBBBBO....
...OBBBBO....
..OBBBBO.....
..OBBBBO.....
..OBBBBBO....
..OBSBSBO....
..OBSBSBO....
..OSBSBSO....
...OOOOO.....
"""

GLYPHS["pocket"] = """
OOOOOOOOOOOOOO
OBBBBBBBBBBBBO
OBBSO....OSBBO
OBBBSO..OSBBBO
OBBBBSOOSBBBBO
OBBBBBSSBBBBBO
OBBBBBBBBBBBBO
OSBBBBBBBBBBSO
OOOOOOOOOOOOOO
"""

GLYPHS["shoes"] = """
...OO......OO...
..OBBO....OBBO..
..OBBO....OBBO..
..OBBO....OBBO..
..OBBBO..OBBBO..
.OBBBBO..OBBBBO.
OBBBBBO..OBBBBBO
OBBBBBBOOBBBBBBO
OSSSSSSOOSSSSSSO
.OOOOOO..OOOOOO.
"""

GLYPHS["suit"] = """
.....OOOOOO.....
....OBBOOBBO....
..OOOBBOOBBOOO..
.OBBOBBBBBBOBBO.
.OBBOBBBBBBOBBO.
.OBBOBBBBBBOBBO.
.OSBOBBSSBBOBSO.
.OOOOBBBBBBOOOO.
....OBBBBBBO....
....OBBBBBBO....
....OBBSSBBO....
....OBBBBBBO....
....OOOOOOOO....
"""

GLYPHS["suit_storage"] = """
..OOOO....OOOO..
.OBBBBO..OBBBBO.
.OBBBBOOOOBBBBO.
.OBBBBBBBBBBBBO.
.OBSBBBBBBBBSBO.
OOOOOOOOOOOOOOOO
OBSSBBBBBBBBSSBO
.OBBBBBBBBBBBBO.
.OBBBBBBBBBBBBO.
..OOOOOOOOOOOO..
"""

GLYPHS["uniform"] = """
..OOO.....OOO..
.OBBBO...OBBBO.
.OBBBOOOOOBBBO.
.OBBOBBBBBOBBO.
.OBBOBBBBBOBBO.
..OOOBBSBBOOO..
....OBBSBBO....
....OBBSBBO....
....OBOOOBO....
....OBO.OBO....
....OBO.OBO....
...OBBO.OBBO...
...OSSO.OSSO...
...OOOO.OOOO...
"""

GLYPHS["web"] = """
.OO..........OO.
.OBO........OBO.
..OBO......OBO..
...OBO....OBO...
....OBO..OBO....
.....OBOOBO.....
......OBBO......
......OBBO......
.....OBOOBO.....
....OBO..OBO....
...OBO....OBO...
..OBO......OBO..
.OBO........OBO.
.OO..........OO.
"""

GLYPHS["hand"] = """
...OOOOOO..
..OBBBBBBO.
..OBBBBBBO.
..OBBBBBBO.
..OBBBBBBO.
.OOBBBBBBO.
OBOBBBBBBO.
OBBBBBBBBO.
.OBBBBBBBO.
..OSSSSSSO.
..OSSSSSSO.
...OOOOOO..
"""

STAR = """
...KK...
..KGGK..
.KGGGGK.
KGGGGGGK
.KGGGGK.
..KGGK..
...KK...
"""

RED_ARROW = """
......OO........
.....ORO........
....ORRO........
...ORRROOOOOOO..
..ORRRRRRRRRRO..
.ORRRRRRRRRRRO..
..ORRRRRRRRRRO..
...ORRROOOOOOO..
....ORRO........
.....ORO........
......OO........
"""

RED_X = """
.OO..........OO.
ORRO........ORRO
ORRRO......ORRRO
.ORRRO....ORRRO.
..ORRRO..ORRRO..
...ORRROORRRO...
....ORRRRRRO....
.....ORRRRO.....
....ORRRRRRO....
...ORRROORRRO...
..ORRRO..ORRRO..
.ORRRO....ORRRO.
ORRRO......ORRRO
ORRO........ORRO
.OO..........OO.
"""

# ---------------------------------------------------------------- builders
def build_slots():
    for name, key, dy in [
        ("back", "back", 0), ("belt", "belt", 0), ("ears", "ears", 0),
        ("glasses", "glasses", 0), ("gloves", "gloves", 0), ("head", "head", 0),
        ("id", "id", 0), ("mask", "mask", 0), ("neck", "neck", 0),
        ("pocket", "pocket", 0), ("shoes", "shoes", 0), ("suit", "suit", 0),
        ("suit_storage", "suit_storage", 0), ("uniform", "uniform", 0),
        ("web", "web", 0),
    ]:
        save(f"Slots/{name}.png", center(slot_frame(), art(GLYPHS[key]), dy))

    hand = art(GLYPHS["hand"])
    hand_r = hand.transpose(Image.FLIP_LEFT_RIGHT)
    for name, g, letter, lx in [
        ("hand_l", hand, "L", 4), ("hand_r", hand_r, "R", 24),
        ("hand_l_no_letter", hand, None, 0), ("hand_r_no_letter", hand_r, None, 0),
    ]:
        im = center(slot_frame(), g, 1)
        if letter:
            stamp_letter(im, letter, (lx, 4), PAL["H"])
        save(f"Slots/{name}.png", im)

    # hand_m: navy-tinted active-hand panel, no letter
    im = center(slot_frame(tint=((36, 40, 54, 244), (28, 31, 44, 244))), hand, 1)
    save("Slots/hand_m.png", im)

    # toggle: backpack + teal corner brackets + NE arrow (inventory expand)
    im = center(slot_frame(), art(GLYPHS["back"]), 1)
    d = ImageDraw.Draw(im)
    T = PAL["T"]
    for x0, y0, dx, dy_ in ((2, 2, 1, 1), (29, 2, -1, 1), (2, 29, 1, -1), (29, 29, -1, -1)):
        d.line([(x0, y0), (x0 + 4 * dx, y0)], fill=T)
        d.line([(x0, y0), (x0, y0 + 4 * dy_)], fill=T)
    d.line([(21, 10), (27, 4)], fill=T, width=2)
    d.polygon([(27, 3), (28, 8), (22, 3)], fill=T)
    save("Slots/toggle.png", im)

    # camo / contra: white rounded marker frames + letter
    for name, letter in (("camo", "C"), ("contra", "!")):
        im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        d.rounded_rectangle([1, 1, 30, 30], radius=4, outline=PAL["W"], width=2)
        stamp_letter(im, letter, (5, 5), PAL["W"])
        save(f"Slots/{name}.png", im)

def build_root():
    save("SlotBackground.png", slot_frame())
    save("template_small.png", slot_frame())

    # blocked: translucent crimson circle-slash
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    ring, inner = (204, 0, 51, 110), (153, 0, 51, 110)
    d.ellipse([5, 5, 26, 26], outline=ring, width=3)
    d.ellipse([7, 7, 24, 24], outline=inner, width=1)
    d.line([(9, 22), (22, 9)], fill=ring, width=3)
    save("blocked.png", im)

    # slot_highlight: teal corner brackets
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    T = (76, 130, 129, 252)
    for x0, y0, dx, dy in ((1, 1, 1, 1), (30, 1, -1, 1), (1, 30, 1, -1), (30, 30, -1, -1)):
        for k in range(2):
            d.line([(x0 + k * dx, y0 + k * dy), (x0 + (7 + k) * dx, y0 + k * dy)], fill=T)
            d.line([(x0 + k * dx, y0 + k * dy), (x0 + k * dx, y0 + (7 + k) * dy)], fill=T)
    save("slot_highlight.png", im)

    # item_status panels: 16x16 striped mini panel, mirrored corner cut
    def status(side, highlight):
        im = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        if highlight:
            d.rounded_rectangle([0, 0, 15, 15], radius=3,
                                outline=(76, 130, 129, 255), width=1)
        else:
            for y in range(1, 15):
                d.line([(1, y), (14, y)],
                       fill=(STRIPE_A if (y // 2) % 2 == 0 else STRIPE_B))
            d.rounded_rectangle([0, 0, 15, 15], radius=2, outline=FRAME_BORDER)
            # diagonal cut on the outward top corner
            px = im.load()
            xs = (0, 1, 2) if side == "left" else (15, 14, 13)
            for i, x in enumerate(xs):
                for y in range(0, 3 - i):
                    px[x, y] = (0, 0, 0, 0)
        return im
    save("item_status_left.png", status("left", False))
    save("item_status_right.png", status("right", False))
    save("item_status_left_highlight.png", status("left", True))
    save("item_status_right_highlight.png", status("right", True))

    # list arrows 24x28 (+ fresh hand-authored SVG sources)
    LAV = (123, 126, 158, 255)
    def tri(filled, left=True):
        im = Image.new("RGBA", (24, 28), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        pts = [(20, 2), (4, 14), (20, 26)] if left else [(3, 2), (19, 14), (3, 26)]
        if filled:
            d.polygon(pts, fill=LAV)
        else:
            d.polygon(pts, outline=LAV, width=3)
        return im
    save("left_arrow.svg.192dpi.png", tri(False, True))
    save("right_arrow.svg.192dpi.png", tri(False, False))
    save("filled_left_arrow.svg.192dpi.png", tri(True, True))
    save("filled_right_arrow.svg.192dpi.png", tri(True, False))
    SVG = ('<svg xmlns="http://www.w3.org/2000/svg" width="12" height="14" '
           'viewBox="0 0 12 14">{}</svg>\n')
    svgs = {
        "left_arrow.svg": '<path d="M9 2 L3 7 L9 12" stroke="#7B7E9E" stroke-width="2" fill="none"/>',
        "right_arrow.svg": '<path d="M3 2 L9 7 L3 12" stroke="#7B7E9E" stroke-width="2" fill="none"/>',
        "filled_left_arrow.svg": '<path d="M9 1 L2 7 L9 13 Z" fill="#7B7E9E"/>',
        "filled_right_arrow.svg": '<path d="M3 1 L10 7 L3 13 Z" fill="#7B7E9E"/>',
    }
    for name, body in svgs.items():
        with open(os.path.join(OUT, name), "w") as f:
            f.write(SVG.format(body))

def build_storage():
    A, Bc = (105, 105, 105, 252), (91, 91, 93, 255)
    HI, LO = (130, 130, 132, 255), (58, 58, 60, 255)

    def piece(edges):
        im = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        for y in range(8):
            d.line([(0, y), (7, y)], fill=(A if y % 2 == 0 else Bc))
        if "top" in edges: d.line([(0, 0), (7, 0)], fill=HI)
        if "bottom" in edges: d.line([(0, 7), (7, 7)], fill=LO)
        if "left" in edges: d.line([(0, 0), (0, 7)], fill=HI)
        if "right" in edges: d.line([(7, 0), (7, 7)], fill=LO)
        return im
    save("Storage/piece_center.png", piece(()))
    save("Storage/piece_top.png", piece(("top",)))
    save("Storage/piece_bottom.png", piece(("bottom",)))
    save("Storage/piece_left.png", piece(("left",)))
    save("Storage/piece_right.png", piece(("right",)))
    save("Storage/piece_topLeft.png", piece(("top", "left")))
    save("Storage/piece_topRight.png", piece(("top", "right")))
    save("Storage/piece_bottomLeft.png", piece(("bottom", "left")))
    save("Storage/piece_bottomRight.png", piece(("bottom", "right")))

    def sidebar(kind):
        im = Image.new("RGBA", (16, 16), (44, 44, 44, 255))
        d = ImageDraw.Draw(im)
        d.line([(0, 0), (0, 15)], fill=(0, 0, 0, 255))
        d.line([(15, 0), (15, 15)], fill=(0, 0, 0, 255))
        d.line([(1, 0), (1, 15)], fill=(31, 31, 31, 255))
        d.line([(14, 0), (14, 15)], fill=(37, 37, 37, 255))
        if kind == "top": d.line([(0, 0), (15, 0)], fill=(0, 0, 0, 255))
        if kind == "bottom": d.line([(0, 15), (15, 15)], fill=(0, 0, 0, 255))
        if kind == "fat":
            d.line([(4, 0), (4, 15)], fill=(31, 31, 31, 255))
            d.line([(11, 0), (11, 15)], fill=(37, 37, 37, 255))
        return im
    for k in ("top", "mid", "bottom", "fat"):
        save(f"Storage/sidebar_{k}.png", sidebar(k))

    def tile(fill, border):
        im = Image.new("RGBA", (16, 16), fill)
        ImageDraw.Draw(im).rectangle([0, 0, 15, 15], outline=border)
        return im
    save("Storage/tile_empty.png", tile((182, 182, 182, 189), (94, 94, 94, 224)))
    save("Storage/tile_empty_opaque.png", tile((201, 201, 201, 255), (114, 114, 114, 255)))
    # originals are fully transparent 16x16 canvases — mirror that
    save("Storage/tile_blocked.png", Image.new("RGBA", (16, 16), (0, 0, 0, 0)))
    save("Storage/tile_blocked_opaque.png", Image.new("RGBA", (16, 16), (0, 0, 0, 0)))

    save("Storage/back.png", center(Image.new("RGBA", (16, 16), (0, 0, 0, 0)),
                                    art(RED_ARROW, {"O": PAL["r"]})))
    save("Storage/exit.png", center(Image.new("RGBA", (16, 16), (0, 0, 0, 0)),
                                    art(RED_X, {"O": PAL["r"]})))
    save("Storage/marked_first.png",
         center(Image.new("RGBA", (8, 8), (0, 0, 0, 0)), art(STAR, {"K": PAL["g"]})))
    save("Storage/marked_second.png",
         center(Image.new("RGBA", (8, 8), (0, 0, 0, 0)),
                art(STAR.replace("G", "N"), {"K": PAL["n"]})))

# ---------------------------------------------------------------- gates + sheet
def gates_and_sheet():
    import glob
    rels = sorted(os.path.relpath(p, OUT) for p in
                  glob.glob(os.path.join(OUT, "**/*.png"), recursive=True))
    fails, rows = [], []
    for rel in rels:
        new = Image.open(os.path.join(OUT, rel)).convert("RGBA")
        old = Image.open(os.path.join(SRC, rel)).convert("RGBA")
        assert new.size == old.size, f"{rel}: size {new.size} != {old.size}"
        n_new = sum(1 for p in new.getdata() if p[3] > 8)
        n_old = sum(1 for p in old.getdata() if p[3] > 8)
        ratio = (n_new / n_old) if n_old else (1.0 if n_new == 0 else 99.0)
        rows.append((rel, old, new, ratio))
        if n_old and ratio < 0.55:
            fails.append((rel, ratio))
    print(f"{len(rels)} files; ratio gate <0.55 fails: {fails}")
    assert not fails, "DUAL ART GATE (ratio) failed"

    # before/after contact sheet
    cell, cols = 70, 8
    nrows = (len(rows) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * (2 * cell + 12) + 4, nrows * (cell + 18) + 4),
                      (60, 60, 70, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (rel, old, new, ratio) in enumerate(rows):
        x = (i % cols) * (2 * cell + 12) + 4
        y = (i // cols) * (cell + 18) + 4
        for j, im in enumerate((old, new)):
            s = min(64 / im.width, 64 / im.height)
            im2 = im.resize((max(1, int(im.width * s)), max(1, int(im.height * s))),
                            Image.NEAREST)
            sheet.paste(im2, (x + j * (cell + 2), y), im2)
        dr.text((x, y + cell), f"{os.path.basename(rel)[:16]} {ratio:.2f}",
                fill=(255, 255, 220, 255))
    sheet.save(os.path.join(SCR, "CONTACT-interface-default.png"))
    print("contact sheet written")

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    build_slots()
    build_root()
    build_storage()
    gates_and_sheet()
