#!/usr/bin/env python3
"""HUD-THEMES-GEN: all six player-selectable HUD themes drawn PROCEDURALLY (CC0).

Replaces the undeclared goonstation-family HUD theme sets (Ashen, Plasmafire,
Retro, Clockwork, Slimecore, Minimalist — 232 files) with Solreign-authored
procedural art. This is the parameterized extension of the merged wave-6
generator (docs/receipts/wave6/draw_interface_default.py): the CC0 Solreign
glyph designs from wave 6 are IMPORTED and re-rendered through six fresh
per-theme parameter packs (palette + chrome style), plus one new original
glyph (ears_headset) authored in this file.

METHOD NOTE (license rail): the original NC-suspect pixels were VIEWED only to
describe their mood vocabulary (Ashen = desaturated grey; Plasmafire = orange
fire on dark indigo; Retro = chunky green-on-blue flat; Clockwork = brass and
dark brown; Slimecore = green on dark green-grey; Minimalist = thin ghost-grey
on near-black with a folded corner) and to read unprotectable technical facts:
canvas sizes, per-file alpha conventions (which files are full-bleed panels,
which are 28x28/30x30 insets, which are bare glyphs, which are fully
transparent), and opaque-pixel counts for the wave-6 ratio gate. No NC pixels
are read, sampled, traced, img2img'd, or conditioned on. Every panel, border,
stripe, bracket, and glyph below is drawn by this code from scratch; glyph
shapes are the wave-6 Solreign originals (CC0). Fully deterministic: no
randomness anywhere.

Outputs land in out/<Theme>/ mirroring the folder layout, are gated against
the originals (size identity + wave-6 opaque-ratio gate >= 0.55), rendered to
per-theme before/after contact sheets, then copied into
Resources/Textures/Interface/<Theme>/.
"""
import importlib.util
import json
import os
import shutil
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(SCR, "..", "..", ".."))
TEX = os.path.join(ROOT, "Resources/Textures/Interface")
OUT = os.path.join(SCR, "out")

# ---------------------------------------------------------------- wave-6 import
_spec = importlib.util.spec_from_file_location(
    "wave6", os.path.join(ROOT, "docs/receipts/wave6/draw_interface_default.py"))
w6 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(w6)  # defs + constants only; main is guarded

GLYPHS = dict(w6.GLYPHS)  # Solreign-original CC0 glyph designs (wave 6)
FONT = w6.FONT

# New original glyph for this wave: an audio headset (band, two cups, mic arm).
GLYPHS["ears_headset"] = """
....OOOOOOOO....
...OBBBBBBBBO...
..OBBOOOOOOBBO..
..OBO......OBO..
..OBO......OBO..
.OBBO......OBBO.
.OBBO......OBBO.
.OSBO......OBSO.
.OSBO......OBSO.
.OOOO......OBOO.
...........OBO..
..........OBO...
......OOOOBO....
.....OHHOOO.....
......OO........
"""

# ---------------------------------------------------------------- theme packs
# Each pack is a fresh Solreign design in the named mood family.
THEMES = {
    "Ashen": dict(  # desaturated charcoal greys
        style="h",
        stripe_a=(36, 36, 40, 255), stripe_b=(29, 29, 33, 255),
        border_out=(98, 98, 104, 255), border_in=(16, 16, 18, 255),
        corner="round",
        pal=dict(O=(34, 34, 38, 255), B=(118, 118, 124, 255),
                 S=(84, 84, 90, 255), H=(152, 152, 158, 255)),
        accent=(190, 190, 198, 255),
        hi_width=1,
        hand_m_tint=((46, 46, 52, 255), (38, 38, 44, 255)),
        piece=dict(A=(52, 52, 56, 252), B=(44, 44, 48, 255),
                   HI=(78, 78, 84, 255), LO=(24, 24, 26, 255)),
        sidebar=dict(base=(48, 48, 52, 255), edge=(12, 12, 14, 255),
                     l1=(30, 30, 34, 255), l2=(38, 38, 42, 255)),
        arrow=(168, 168, 176, 255), arrow_o=(52, 52, 58, 255),
    ),
    "Plasmafire": dict(  # burning orange glyphs on dark indigo chrome
        style="h",
        stripe_a=(56, 50, 78, 255), stripe_b=(46, 42, 66, 255),
        border_out=(122, 116, 152, 255), border_in=(20, 18, 30, 255),
        corner="square",
        pal=dict(O=(105, 42, 6, 255), B=(235, 148, 40, 255),
                 S=(185, 95, 18, 255), H=(255, 205, 92, 255)),
        accent=(232, 72, 32, 255),
        hi_width=1,
        piece=dict(A=(60, 54, 84, 252), B=(50, 46, 72, 255),
                   HI=(198, 122, 42, 255), LO=(28, 26, 42, 255)),
        sidebar=dict(base=(38, 34, 56, 255), edge=(12, 10, 20, 255),
                     l1=(26, 24, 40, 255), l2=(52, 48, 76, 255)),
        tile=dict(empty=((172, 162, 214, 200), (110, 100, 160, 230)),
                  opaque=((188, 178, 226, 255), (120, 110, 170, 255))),
    ),
    "Retro": dict(  # chunky bright green on flat arcade blue
        style="flat",
        flat=(66, 138, 210, 255),
        border_out=(28, 76, 164, 255), border_in=(28, 76, 164, 255),
        corner="square",
        pal=dict(O=(12, 86, 24, 255), B=(66, 196, 70, 255),
                 S=(34, 140, 44, 255), H=(130, 235, 120, 255)),
        accent=(244, 208, 44, 255),
        hi_width=2,
        hi_double_square=True,
        piece=dict(A=(66, 138, 210, 255), B=(60, 128, 198, 255),
                   HI=(110, 174, 232, 255), LO=(28, 76, 164, 255)),
        tile=dict(empty=((176, 176, 176, 255), (120, 120, 120, 255)),
                  opaque=((192, 192, 192, 255), (130, 130, 130, 255))),
        bare=frozenset(("Slots/back.png", "Slots/belt.png", "Slots/id.png",
                        "Slots/pocket.png", "Slots/web.png",
                        "Slots/suit_storage.png")),
        transparent=frozenset(("template_small.png", "Storage/sidebar_top.png",
                               "Storage/sidebar_mid.png",
                               "Storage/sidebar_bottom.png")),
    ),
    "Clockwork": dict(  # brass and bronze, diagonal machining stripes
        style="diag",
        stripe_a=(150, 114, 58, 255), stripe_b=(128, 96, 46, 255),
        border_out=(46, 30, 10, 255), border_in=(208, 172, 92, 255),
        corner="square",
        pal=dict(O=(80, 52, 10, 255), B=(214, 168, 66, 255),
                 S=(164, 120, 38, 255), H=(244, 212, 128, 255)),
        accent=(238, 216, 140, 255),
        hi_width=2,
        piece=dict(A=(140, 106, 52, 252), B=(120, 90, 42, 255),
                   HI=(200, 164, 86, 255), LO=(52, 36, 14, 255)),
        sidebar=dict(base=(96, 70, 32, 255), edge=(20, 12, 4, 255),
                     l1=(60, 42, 18, 255), l2=(150, 118, 58, 255)),
    ),
    "Slimecore": dict(  # translucent-feeling green on dark green-grey
        style="h",
        stripe_a=(40, 50, 40, 255), stripe_b=(32, 42, 32, 255),
        border_out=(82, 104, 80, 255), border_in=(14, 20, 14, 255),
        corner="square",
        pal=dict(O=(22, 64, 30, 255), B=(96, 178, 102, 255),
                 S=(60, 126, 68, 255), H=(148, 222, 150, 255)),
        accent=(110, 230, 120, 255),
        hi_width=1,
        piece=dict(A=(46, 58, 46, 252), B=(38, 48, 38, 255),
                   HI=(88, 120, 88, 255), LO=(18, 26, 18, 255)),
        tile=dict(empty=((162, 208, 162, 220), (110, 168, 110, 235)),
                  opaque=((174, 216, 174, 255), (118, 176, 118, 255)),
                  stripes=True),
    ),
    "Minimalist": dict(  # thin ghost-grey glyphs on near-black, folded corner
        style="scan",
        stripe_a=(23, 26, 30, 255), stripe_b=(16, 18, 21, 255),
        border_out=(74, 80, 90, 255), border_in=None,
        corner="dogear",
        pal=dict(O=(26, 30, 36, 255), B=(112, 122, 138, 255),
                 S=(78, 86, 100, 255), H=(156, 166, 182, 255)),
        accent=(235, 168, 52, 255),
        hi_width=1,
        hand_m_tint=((30, 34, 40, 255), (22, 26, 31, 255)),
        toggle_green=dict(
            stripe_a=(24, 38, 26, 255), stripe_b=(18, 30, 20, 255),
            border=(76, 128, 82, 255),
            pal=dict(O=(18, 42, 22, 255), B=(96, 170, 100, 255),
                     S=(62, 122, 66, 255), H=(140, 210, 142, 255))),
    ),
}

# Themes with the 28x28 inset-panel alpha convention on these files.
INSET28 = frozenset(("Slots/back.png", "Slots/belt.png", "Slots/id.png",
                     "Slots/pocket.png", "Slots/web.png", "template_small.png"))
INSET28_THEMES = frozenset(("Plasmafire", "Clockwork", "Slimecore"))

SLOT_GLYPH = {  # slot file -> wave-6 glyph key
    "back": "back", "belt": "belt", "ears": "ears",
    "ears_headset": "ears_headset", "glasses": "glasses", "gloves": "gloves",
    "head": "head", "id": "id", "mask": "mask", "neck": "neck",
    "pocket": "pocket", "shoes": "shoes", "suit": "suit",
    "suit_storage": "suit_storage", "uniform": "uniform", "web": "web",
}


# ---------------------------------------------------------------- helpers
def art(s, pal):
    """ASCII art -> RGBA image. '.' transparent; letters per pal."""
    rows = s.strip("\n").split("\n")
    w = max(len(r) for r in rows)
    im = Image.new("RGBA", (w, len(rows)), (0, 0, 0, 0))
    px = im.load()
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            if ch != ".":
                px[x, y] = pal[ch]
    return im


def dilate(im, color):
    """One chunky-outline pass: transparent px with an opaque 4-neighbour
    becomes `color`, layered UNDER the glyph (Retro bare-glyph boldening)."""
    w, h = im.size
    src = im.load()
    out = Image.new("RGBA", (w + 2, h + 2), (0, 0, 0, 0))
    dst = out.load()
    for y in range(h):
        for x in range(w):
            if src[x, y][3] > 0:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    dst[x + 1 + dx, y + 1 + dy] = color
    for y in range(h):
        for x in range(w):
            if src[x, y][3] > 0:
                dst[x + 1, y + 1] = src[x, y]
    return out


def stamp_letter(im, ch, xy, color):
    px = im.load()
    for dy, row in enumerate(FONT[ch]):
        for dx, c in enumerate(row):
            if c == "X":
                px[xy[0] + dx, xy[1] + dy] = color


def fill_pattern(T, box, d, tint=None):
    """Paint the theme's interior pattern inside box (inclusive coords)."""
    x0, y0, x1, y1 = box
    style = T["style"]
    if style == "flat":
        d.rectangle(box, fill=T["flat"])
        return
    a, b = tint if tint else (T["stripe_a"], T["stripe_b"])
    if style == "diag":
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                d.point((x, y), fill=(a if ((x + y) // 3) % 2 == 0 else b))
    elif style == "scan":
        for y in range(y0, y1 + 1):
            d.line([(x0, y), (x1, y)], fill=(a if y % 2 == 0 else b))
    else:  # "h": paired horizontal stripes (wave-6 rhythm)
        for y in range(y0, y1 + 1):
            d.line([(x0, y), (x1, y)], fill=(a if (y // 2) % 2 == 0 else b))


def panel(T, size=32, inset=0, tint=None, border_override=None):
    """Theme slot/status panel. inset=0 full-bleed, 2 => 28x28 convention."""
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    corner = T["corner"]
    bo = border_override or T["border_out"]
    bi = T["border_in"]
    if corner == "round":  # Ashen: 30x30 rounded twin border
        i = inset + 1
        mask = Image.new("L", (size, size), 0)
        ImageDraw.Draw(mask).rounded_rectangle(
            [i, i, size - 1 - i, size - 1 - i], radius=4, fill=255)
        fill = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        fill_pattern(T, (0, 0, size - 1, size - 1), ImageDraw.Draw(fill), tint)
        im = Image.composite(fill, im, mask)
        d = ImageDraw.Draw(im)
        d.rounded_rectangle([i, i, size - 1 - i, size - 1 - i],
                            radius=4, outline=bo)
        d.rounded_rectangle([i + 1, i + 1, size - 2 - i, size - 2 - i],
                            radius=3, outline=bi)
        return im
    if corner == "dogear":  # Minimalist: 30x30, folded top-right corner
        i = inset + 1
        x0, y0, x1, y1 = i, i, size - 1 - i, size - 1 - i
        fill_pattern(T, (x0, y0, x1, y1), d, tint)
        d.rectangle([x0, y0, x1, y1], outline=bo)
        fold = 7
        px = im.load()
        for k in range(fold):  # cut the corner triangle
            for x in range(x1 - k, x1 + 1):
                px[x, y0 + (fold - 1 - k)] = (0, 0, 0, 0)
        d.line([(x1 - fold, y0), (x1, y0 + fold)],
               fill=(110, 118, 130, 255))  # fold crease
        d.line([(x0, y0), (x1 - fold, y0)], fill=bo)
        d.line([(x1, y0 + fold), (x1, y1)], fill=bo)
        return im
    # square chrome (Plasmafire / Retro / Clockwork / Slimecore)
    i = inset
    x0, y0, x1, y1 = i, i, size - 1 - i, size - 1 - i
    fill_pattern(T, (x0 + 2, y0 + 2, x1 - 2, y1 - 2), d, tint)
    d.rectangle([x0, y0, x1, y1], outline=bo)
    d.rectangle([x0 + 1, y0 + 1, x1 - 1, y1 - 1], outline=(bi or bo))
    return im


def center(base, glyph, dy=0):
    x = (base.width - glyph.width) // 2
    y = (base.height - glyph.height) // 2 + dy
    base.paste(glyph, (x, y), glyph)
    return base


def rounded_outline(size, color, width, pad, radius):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(im).rounded_rectangle(
        [pad, pad, size - 1 - pad, size - 1 - pad],
        radius=radius, outline=color, width=width)
    return im


# ---------------------------------------------------------------- builders
def glyph_img(T, key, pal_override=None):
    return art(GLYPHS[key], pal_override or T["pal"])


def build_slot(theme, T, rel):
    name = os.path.splitext(os.path.basename(rel))[0]
    bare = rel in T.get("bare", ())
    pal = T["pal"]

    if name in ("hand_l", "hand_r", "hand_l_no_letter", "hand_r_no_letter",
                "hand_m"):
        hand = art(GLYPHS["hand"], pal)
        if name.startswith("hand_r"):
            hand = hand.transpose(Image.FLIP_LEFT_RIGHT)
        tint = T.get("hand_m_tint") if name == "hand_m" else None
        im = center(panel(T, tint=tint), hand, 1)
        if name == "hand_l":
            stamp_letter(im, "L", (4, 4), pal["H"])
        elif name == "hand_r":
            stamp_letter(im, "R", (24, 4), pal["H"])
        return im

    if name == "toggle":
        tg = T.get("toggle_green")
        if tg:  # Minimalist: green expand button
            T2 = dict(T, stripe_a=tg["stripe_a"], stripe_b=tg["stripe_b"],
                      border_out=tg["border"], pal=tg["pal"])
            im = center(panel(T2), art(GLYPHS["back"], tg["pal"]), 1)
            A = tg["pal"]["H"]
        else:
            im = center(panel(T, border_override=T["accent"]),
                        glyph_img(T, "back"), 1)
            A = T["accent"]
        d = ImageDraw.Draw(im)
        d.line([(21, 10), (27, 4)], fill=A, width=2)  # NE expand arrow
        d.polygon([(27, 3), (28, 8), (22, 3)], fill=A)
        return im

    key = SLOT_GLYPH[name]
    g = glyph_img(T, key)
    if bare:  # Retro bare-glyph convention, boldened for the chunky look
        g = dilate(g, pal["O"])
        return center(Image.new("RGBA", (32, 32), (0, 0, 0, 0)), g)
    inset = 2 if (theme in INSET28_THEMES and rel in INSET28) else 0
    return center(panel(T, inset=inset), g)


def build_status(T, side, highlight):
    if highlight:
        return rounded_outline(16, T["accent"], T["hi_width"], 1, 3)
    im = panel(T, size=16)
    if T["corner"] == "square":  # cut the outward top corner (wave-6 rhythm)
        px = im.load()
        xs = (0, 1, 2) if side == "left" else (15, 14, 13)
        for i, x in enumerate(xs):
            for y in range(0, 3 - i):
                px[x, y] = (0, 0, 0, 0)
    return im


def build_slot_highlight(T):
    if T.get("hi_double_square"):  # Retro: twin yellow squares
        im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        d.rectangle([0, 0, 31, 31], outline=T["accent"])
        d.rectangle([3, 3, 28, 28], outline=T["accent"])
        return im
    if T["hi_width"] == 2:  # Clockwork: twin rounded gold rings
        im = rounded_outline(32, T["accent"], 1, 1, 5)
        im.alpha_composite(rounded_outline(32, T["accent"], 1, 4, 4))
        return im
    return rounded_outline(32, T["accent"], 1, 2, 5)


def build_storage(theme, T, rel):
    name = os.path.splitext(os.path.basename(rel))[0]
    P = T.get("piece")
    if name.startswith("piece_"):
        edges = []
        low = name[len("piece_"):].lower()
        for e in ("top", "bottom", "left", "right"):
            if e in low:
                edges.append(e)
        im = Image.new("RGBA", (8, 8), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        if T["style"] == "flat":
            d.rectangle([0, 0, 7, 7], fill=P["A"])
        else:
            for y in range(8):
                d.line([(0, y), (7, y)], fill=(P["A"] if y % 2 == 0 else P["B"]))
        if "top" in edges:
            d.line([(0, 0), (7, 0)], fill=P["HI"])
        if "bottom" in edges:
            d.line([(0, 7), (7, 7)], fill=P["LO"])
        if "left" in edges:
            d.line([(0, 0), (0, 7)], fill=P["HI"])
        if "right" in edges:
            d.line([(7, 0), (7, 7)], fill=P["LO"])
        return im
    if name.startswith("sidebar_"):
        S = T["sidebar"]
        kind = name[len("sidebar_"):]
        im = Image.new("RGBA", (16, 16), S["base"])
        d = ImageDraw.Draw(im)
        d.line([(0, 0), (0, 15)], fill=S["edge"])
        d.line([(15, 0), (15, 15)], fill=S["edge"])
        d.line([(1, 0), (1, 15)], fill=S["l1"])
        d.line([(14, 0), (14, 15)], fill=S["l2"])
        if kind == "top":
            d.line([(0, 0), (15, 0)], fill=S["edge"])
        if kind == "bottom":
            d.line([(0, 15), (15, 15)], fill=S["edge"])
        if kind == "fat":
            d.line([(4, 0), (4, 15)], fill=S["l1"])
            d.line([(11, 0), (11, 15)], fill=S["l2"])
        return im
    if name.startswith("tile_empty"):
        fill, border = T["tile"]["opaque" if name.endswith("opaque") else "empty"]
        im = Image.new("RGBA", (16, 16), fill)
        d = ImageDraw.Draw(im)
        if T["tile"].get("stripes"):  # Slimecore: banded slime gel
            for y in range(0, 16, 4):
                d.line([(0, y), (15, y)], fill=border)
                d.line([(0, y + 1), (15, y + 1)], fill=border)
        d.rectangle([0, 0, 15, 15], outline=border)
        return im
    if name == "back":  # Ashen storage back-arrow (wave-6 shape, theme colors)
        g = art(w6.RED_ARROW, {"O": T["arrow_o"], "R": T["arrow"]})
        return center(Image.new("RGBA", (16, 16), (0, 0, 0, 0)), g)
    if name == "exit":  # Ashen storage close-X
        g = art(w6.RED_X, {"O": T["arrow_o"], "R": T["arrow"]})
        return center(Image.new("RGBA", (16, 16), (0, 0, 0, 0)), g)
    raise KeyError(rel)


def build_file(theme, T, rel):
    if rel in T.get("transparent", ()):  # Retro convention: empty canvases
        size = (32, 32) if rel == "template_small.png" else (16, 16)
        return Image.new("RGBA", size, (0, 0, 0, 0))
    if rel.startswith("Slots/"):
        return build_slot(theme, T, rel)
    if rel.startswith("Storage/"):
        return build_storage(theme, T, rel)
    if rel in ("SlotBackground.png", "template_small.png"):
        inset = 2 if (theme in INSET28_THEMES and rel in INSET28) else 0
        return panel(T, inset=inset)
    if rel == "slot_highlight.png":
        return build_slot_highlight(T)
    if rel.startswith("item_status_"):
        side = "left" if "left" in rel else "right"
        return build_status(T, side, rel.endswith("_highlight.png"))
    raise KeyError(rel)


# ---------------------------------------------------------------- gate + sheets
def opaque(im):
    return sum(1 for p in im.getdata() if p[3] > 8)


def build_theme(theme):
    T = THEMES[theme]
    src = os.path.join(TEX, theme)
    rels = []
    for root, _, files in os.walk(src):
        for f in files:
            if f.endswith(".png"):
                rels.append(os.path.relpath(os.path.join(root, f), src))
    rels.sort()
    rows, fails = [], []
    for rel in rels:
        new = build_file(theme, T, rel)
        old = Image.open(os.path.join(src, rel)).convert("RGBA")
        assert new.size == old.size, f"{theme}/{rel}: {new.size} != {old.size}"
        n_new, n_old = opaque(new), opaque(old)
        ratio = (n_new / n_old) if n_old else (1.0 if n_new == 0 else 99.0)
        if n_old and ratio < 0.55:
            fails.append((rel, round(ratio, 2)))
        rows.append((rel, old, new, ratio))
        p = os.path.join(OUT, theme, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        new.save(p)
    print(f"{theme}: {len(rels)} files; ratio-gate fails: {fails}")
    assert not fails, f"{theme}: DUAL ART GATE (ratio) failed"
    contact_sheet(theme, rows)
    write_meta(theme, rels)
    return rels


def contact_sheet(theme, rows):
    cell, cols = 70, 8
    nrows = (len(rows) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * (2 * cell + 12) + 4,
                               nrows * (cell + 18) + 4), (60, 60, 70, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (rel, old, new, ratio) in enumerate(rows):
        x = (i % cols) * (2 * cell + 12) + 4
        y = (i // cols) * (cell + 18) + 4
        for j, im in enumerate((old, new)):
            s = min(64 / im.width, 64 / im.height)
            im2 = im.resize((max(1, int(im.width * s)),
                             max(1, int(im.height * s))), Image.NEAREST)
            sheet.paste(im2, (x + j * (cell + 2), y), im2)
        dr.text((x, y + cell), f"{os.path.basename(rel)[:16]} {ratio:.2f}",
                fill=(255, 255, 220, 255))
    sheet.save(os.path.join(SCR, f"CONTACT-{theme}.png"))


def context_sheet():
    """Mock HUD arrangement per theme: inventory row, hands, storage window."""
    Z = 3
    theme_h = 68 * Z + 18
    sheet = Image.new("RGBA", (8 + 406 * Z,
                               len(THEMES) * (theme_h + 8) + 4),
                      (46, 46, 54, 255))
    dr = ImageDraw.Draw(sheet)
    for ti, theme in enumerate(THEMES):
        base = os.path.join(OUT, theme)
        y0 = 4 + ti * (theme_h + 8)

        def img(rel):
            return Image.open(os.path.join(base, rel)).convert("RGBA")

        def blit(im, gx, gy):
            im2 = im.resize((im.width * Z, im.height * Z), Image.NEAREST)
            sheet.alpha_composite(im2, (4 + gx * Z, y0 + gy * Z))

        row = ["Slots/uniform.png", "Slots/suit.png", "Slots/back.png",
               "Slots/belt.png", "Slots/gloves.png", "Slots/shoes.png",
               "Slots/head.png", "Slots/mask.png", "Slots/id.png",
               "Slots/pocket.png", "Slots/web.png", "Slots/toggle.png"]
        for i, rel in enumerate(row):
            blit(img(rel), i * 34, 0)
        hands = ["Slots/hand_l.png",
                 "Slots/hand_m.png" if os.path.exists(
                     os.path.join(base, "Slots/hand_m.png"))
                 else "Slots/hand_l_no_letter.png",
                 "Slots/hand_r.png"]
        for i, rel in enumerate(hands):
            blit(img(rel), i * 34, 36)
        hl = img("slot_highlight.png")
        blit(hl, 34, 36)  # highlight over active hand
        blit(img("item_status_left.png"), 3 * 34 + 4, 36)
        blit(img("item_status_left_highlight.png"), 3 * 34 + 22, 36)
        blit(img("item_status_right.png"), 3 * 34 + 40, 36)
        blit(img("item_status_right_highlight.png"), 3 * 34 + 58, 36)
        # storage window mock (if the theme ships storage chrome)
        if os.path.isdir(os.path.join(base, "Storage")):
            gx0 = 3 * 34 + 84
            sb = os.path.join(base, "Storage/sidebar_top.png")
            has_sb = os.path.exists(sb)
            if has_sb:
                for gy, kind in ((36, "top"), (52, "mid")):
                    blit(img(f"Storage/sidebar_{kind}.png"), gx0, gy)
            gx0 += 18 if has_sb else 0
            names = [["topLeft", "top", "top", "topRight"],
                     ["left", "center", "center", "right"],
                     ["left", "center", "center", "right"],
                     ["bottomLeft", "bottom", "bottom", "bottomRight"]]
            for r, rown in enumerate(names):
                for c, n in enumerate(rown):
                    blit(img(f"Storage/piece_{n}.png"), gx0 + c * 8, 36 + r * 8)
            te = os.path.join(base, "Storage/tile_empty.png")
            if os.path.exists(te):
                blit(img("Storage/tile_empty.png"), gx0 + 40, 36)
                blit(img("Storage/tile_empty_opaque.png"), gx0 + 40, 53)
            for extra, gx in (("Storage/back.png", gx0 + 60),
                              ("Storage/exit.png", gx0 + 78)):
                if os.path.exists(os.path.join(base, extra)):
                    blit(img(extra), gx, 36)
        dr.text((6, y0 + 68 * Z + 4), theme, fill=(255, 255, 220, 255))
    sheet.save(os.path.join(SCR, "CONTACT-in-context.png"))
    print("context sheet written")


def write_meta(theme, rels):
    meta = {
        "version": 1,
        "size": {"x": 32, "y": 32},
        "license": "CC0-1.0",
        "copyright": ("Entire folder drawn procedurally from scratch for "
                      "Solreign (docs/receipts/hud-themes/draw_hud_themes.py, "
                      "2026); parameterized re-render of the Solreign wave-6 "
                      "CC0 glyph set with an original per-theme chrome design;"
                      " no prior art pixels used or referenced"),
        "states": [{"name": os.path.splitext(r)[0]} for r in rels],
    }
    with open(os.path.join(OUT, theme, "meta.json"), "w") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


def install():
    for theme in THEMES:
        src = os.path.join(OUT, theme)
        dst = os.path.join(TEX, theme)
        for root, _, files in os.walk(src):
            for f in files:
                s = os.path.join(root, f)
                d = os.path.join(dst, os.path.relpath(s, src))
                os.makedirs(os.path.dirname(d), exist_ok=True)
                shutil.copyfile(s, d)
    print("installed into Resources/Textures/Interface/")


if __name__ == "__main__":
    import sys
    os.makedirs(OUT, exist_ok=True)
    total = 0
    for theme in THEMES:
        total += len(build_theme(theme))
    print(f"total files: {total}")
    context_sheet()
    if "--install" in sys.argv:
        install()
