#!/usr/bin/env python3
"""Wave 4 procedural replacements: directionalfan (louver vent) + cash (banknotes).

Both are geometry/typography — code, not text-to-image. Original NC pixels are never
copied: only functional layout facts (bbox placement, frame counts, grid shape) and an
independently-authored palette.
"""
import json, math, os, sys
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
OUT = f"{SCR}/w4-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
COPY = ("Programmatically drawn for Solreign by the sprite factory (AI-authored code, "
        "human-reviewed); replaces CC-BY-NC art")

sys.path.insert(0, SCR)
from build_procedural import FONT, draw_word   # the 4x6 bitmap font from the MEDIUM wave

# The MEDIUM-wave 4x6 font is too wide for a 19px banknote (a 2-char word is 9px and the
# note's text field is ~14px). Author a compact 3x5 set for cash instead.
TINY = {
 '0':["111","101","101","101","111"], '1':["010","110","010","010","111"],
 '2':["111","001","111","100","111"], '3':["111","001","111","001","111"],
 '4':["101","101","111","001","001"], '5':["111","100","111","001","111"],
 '6':["111","100","111","101","111"], '7':["111","001","010","010","010"],
 '8':["111","101","111","101","111"], '9':["111","101","111","001","111"],
 'K':["101","110","100","110","101"], 'M':["101","111","111","101","101"],
 'S':["111","100","111","001","111"],
}

def draw_tiny(im, word, x, y, color):
    """3x5 glyphs, 1px gap. Returns the width drawn."""
    missing = [c for c in word if c not in TINY]
    assert not missing, f"TINY font missing {missing} for {word!r}"
    cx = x
    for ch in word:
        for ry, row in enumerate(TINY[ch]):
            for rx, bit in enumerate(row):
                if bit == "1" and 0 <= cx+rx < 32 and 0 <= y+ry < 32:
                    im.putpixel((cx+rx, y+ry), (*color, 255))
        cx += 4
    return cx - x - 1

def tiny_width(word): return len(word)*4 - 1

# the MEDIUM-wave font was authored for killsign's words — it has no digits. Add them.
FONT.update({
 '0':["0110","1001","1001","1001","1001","0110"], '1':["010","110","010","010","010","111"],
 '2':["0110","1001","0001","0010","0100","1111"], '3':["1110","0001","0110","0001","0001","1110"],
 '4':["0010","0110","1010","1111","0010","0010"], '5':["1111","1000","1110","0001","1001","0110"],
 '6':["0110","1000","1110","1001","1001","0110"], '7':["1111","0001","0010","0100","0100","0100"],
 '8':["0110","1001","0110","1001","1001","0110"], '9':["0110","1001","0111","0001","0001","0110"],
 'M':["10001","11011","10101","10001","10001","10001"],
})

def clamp(v): return max(0, min(255, int(round(v))))
def shade(c, f): return tuple(clamp(x*f) for x in c)

def pack(orig_png, frames):
    ow, oh = Image.open(orig_png).size
    cols = ow // 32
    out = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        out.paste(f, ((i % cols)*32, (i // cols)*32))
    return out

# ---------------- directionalfan: a louver vent, slats rotating in place ----------------
FAN_BOX = {"S": (6, 24, 25, 28), "N": (6, 3, 25, 7), "E": (24, 6, 28, 25), "W": (3, 6, 7, 25)}
BODY, EDGE, SLAT = (74, 77, 82), (30, 31, 34), (128, 132, 138)

def fan_tile(d, fi):
    x0, y0, x1, y1 = FAN_BOX[d]
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    dr.rectangle([x0, y0, x1, y1], fill=BODY, outline=EDGE)
    horiz = d in ("S", "N")
    # slats sweep through 4 phases: diagonal -> edge-on -> opposite diagonal -> flat
    phase = fi * math.pi / 4
    openness = abs(math.cos(phase))          # 1 = face-on (slats read wide), 0 = edge-on
    step = 2 if openness > 0.6 else 3
    span = (x1 - x0) if horiz else (y1 - y0)
    for k in range(1, span, step):
        skew = int(round(2 * math.sin(phase)))
        if horiz:
            x = x0 + k
            for y in range(y0 + 1, y1):
                xx = x + (skew if y > (y0 + y1)//2 else 0)
                if x0 < xx < x1: im.putpixel((xx, y), SLAT + (255,))
        else:
            y = y0 + k
            for x in range(x0 + 1, x1):
                yy = y + (skew if x > (x0 + x1)//2 else 0)
                if y0 < yy < y1: im.putpixel((x, yy), SLAT + (255,))
    return im

def build_fan():
    rel = "Structures/Piping/Atmospherics/directionalfan.rsi"
    orig = f"{REPO}/Resources/Textures/{rel}/icon.png"
    frames = [fan_tile(d, fi) for d in ("S", "N", "E", "W") for fi in range(4)]
    d = f"{OUT}/directionalfan.rsi"; os.makedirs(d, exist_ok=True)
    pack(orig, frames).save(f"{d}/icon.png")
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
            "states": [{"name": "icon", "directions": 4, "delays": [[0.01]*4 for _ in range(4)]}]}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
    return frames

# ---------------- cash: banknotes, spinning for the high denominations ----------------
# Independently authored ramp (NOT sampled from the NC art): a plausible ascending
# denomination colour progression.
NOTES = {
  "cash":         ((38, 112, 46),  ""),
  "cash_10":      ((32, 86, 122),  "10"),
  "cash_100":     ((70, 48, 130),  "100"),
  "cash_500":     ((124, 44, 122), "500"),
  "cash_1000":    ((140, 40, 40),  "1K"),
  "cash_5000":    ((96, 96, 102),  "5K"),
  "cash_10000":   ((46, 104, 40),  "10K"),
  "cash_25000":   ((122, 74, 32),  "25K"),
  "cash_50000":   ((70, 72, 78),   "50K"),
  "cash_100000":  ((176, 48, 74),  "100K"),
  "cash_1000000": ((196, 158, 52), "1M"),
}
BAND = (198, 166, 92)
NOTE_BOX = (6, 13, 24, 23)   # functional layout: a 19x11 note, centred

def note_face(col, label, mirrored=False):
    """One face of the banknote: body, left security band, centred denomination."""
    x0, y0, x1, y1 = NOTE_BOX
    im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    dr.rectangle([x0, y0, x1, y1], fill=col, outline=shade(col, 0.55))
    dr.rectangle([x0+1, y0+1, x1-1, y1-1], outline=shade(col, 1.45))
    bx = x0 + 1
    dr.rectangle([bx, y0+1, bx, y1-1], fill=BAND)            # 1px security strip
    field_l, field_r = bx + 2, x1 - 1                        # 15px field: fits "100K"
    if label:
        w = tiny_width(label)
        tx = field_l + max(0, ((field_r - field_l + 1) - w)//2)
        assert w <= (field_r - field_l + 1), f"label {label!r} ({w}px) overflows the note field"
        draw_tiny(im, label, tx, y0 + 3, shade(BAND, 1.3))
    if mirrored:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    return im

def spin_frames(col, label, n):
    """A banknote rotating about its vertical axis: horizontal squash + face flip."""
    x0, y0, x1, y1 = NOTE_BOX
    cx = (x0 + x1)//2
    out = []
    for i in range(n):
        th = 2*math.pi*i/n
        w = math.cos(th)
        face = note_face(col, label, mirrored=(w < 0))
        k = max(1, int(round(abs(w) * (x1 - x0 + 1))))
        strip = face.crop((x0, 0, x1+1, 32)).resize((k, 32), Image.NEAREST)
        fr = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        fr.paste(strip, (cx - k//2, 0))
        if k <= 2:  # edge-on: just the band
            fr = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
            ImageDraw.Draw(fr).rectangle([cx-1, y0-1, cx+1, y1+1], fill=BAND, outline=shade(BAND, 0.6))
        out.append(fr)
    return out

def build_cash():
    rel = "Objects/Economy/cash.rsi"
    src = f"{REPO}/Resources/Textures/{rel}"
    ometa = json.load(open(f"{src}/meta.json", encoding="utf-8-sig"))
    d = f"{OUT}/cash.rsi"; os.makedirs(d, exist_ok=True)
    states, preview = [], []
    for s in ometa["states"]:
        name = s["name"]
        col, label = NOTES[name]
        if s.get("delays"):
            n = len(s["delays"][0])
            frames = spin_frames(col, label, n)
            pack(f"{src}/{name}.png", frames).save(f"{d}/{name}.png")
            states.append({"name": name, "delays": [list(s["delays"][0])]})
            preview.append((name, frames[0]))
        else:
            im = note_face(col, label)
            im.save(f"{d}/{name}.png")
            states.append({"name": name})
            preview.append((name, im))
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32},
            "states": states}
    json.dump(meta, open(f"{d}/meta.json", "w"), indent=2); open(f"{d}/meta.json", "a").write("\n")
    return preview

if __name__ == "__main__":
    fan = build_fan()
    cash = build_cash()
    cells = [(f"fan{i}", f) for i, f in enumerate(fan[:8])] + cash
    sheet = Image.new("RGBA", (len(cells)*72+4, 92), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (l, c) in enumerate(cells):
        r = c.resize((64, 64), Image.NEAREST); sheet.paste(r, (4+i*72, 4), r)
        dr.text((4+i*72, 70), l[:10], fill=(220, 220, 220, 255))
    sheet.save(f"{SCR}/w4-proc-preview.png")
    print("directionalfan + cash built")
