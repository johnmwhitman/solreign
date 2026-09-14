#!/usr/bin/env python3
"""Build _Solreign/holographic_cake.rsi: hologram-treated derivation of upstream cake.rsi birthday states.

Bakes the prototype's runtime tint (#c9fd8faa) + scanlines into pixels, adds a 4-frame
"budget frame rate" flicker to the world state. Inhands get the static treatment.
"""
import json, os
from PIL import Image

REPO = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims")
SRC = f"{REPO}/Resources/Textures/Objects/Consumable/Food/Baked/cake.rsi"
DST = f"{REPO}/Resources/Textures/_Solreign/holographic_cake.rsi"
OUT = os.path.dirname(os.path.abspath(__file__))
TINT = (201, 253, 143)   # #c9fd8f
ALPHA = 0.67             # aa/255

def clamp(v): return max(0, min(255, int(round(v))))

def treat(tile, scan_offset=0, gain=1.0, glitch_rows=None, alpha_gain=1.0):
    """Hologram: tint multiply + translucency + scanlines (+ optional row-shift glitch)."""
    out = Image.new("RGBA", tile.size, (0, 0, 0, 0))
    w, h = tile.size
    LO, HI = (16, 54, 22), (201, 253, 143)  # dark green -> acid green hologram ramp
    for y in range(h):
        scan = 0.78 if (y + scan_offset) % 2 else 1.0
        for x in range(w):
            r, g, b, a = tile.getpixel((x, y))
            if a == 0: continue
            lum = (0.299*r + 0.587*g + 0.114*b) / 255
            f = scan * gain
            px = tuple(clamp((LO[i] + (HI[i]-LO[i])*lum) * f) for i in range(3)) + (clamp(a*ALPHA*alpha_gain),)
            tx = x
            if glitch_rows and y in glitch_rows:
                tx = x + 1
                if tx >= w: continue
            out.putpixel((tx, y), px)
    return out

os.makedirs(DST, exist_ok=True)

# world state: 4-frame flicker
src = Image.open(f"{SRC}/birthday.png").convert("RGBA")
frames = [
    treat(src, 0),                                        # stable hold
    treat(src, 1, gain=1.06),                             # scanline roll, slight flare
    treat(src, 0),                                        # stable hold
    treat(src, 1, gain=0.85, alpha_gain=0.82, glitch_rows={12, 13}),  # dim glitch, rows 12-13 jump 1px
]
delays = [0.6, 0.1, 0.35, 0.08]
strip = Image.new("RGBA", (32*len(frames), 32), (0, 0, 0, 0))
for i, f in enumerate(frames):
    strip.paste(f, (i*32, 0))
strip.save(f"{DST}/birthday.png")

# inhands: static treatment, preserve 4-dir grid
for st in ["birthday-inhand-left", "birthday-inhand-right"]:
    im = Image.open(f"{SRC}/{st}.png").convert("RGBA")
    out = Image.new("RGBA", im.size, (0, 0, 0, 0))
    for ty in range(0, im.height, 32):
        for tx in range(0, im.width, 32):
            out.paste(treat(im.crop((tx, ty, tx+32, ty+32)), 0), (tx, ty))
    out.save(f"{DST}/{st}.png")

src_meta = json.load(open(f"{SRC}/meta.json"))
meta = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": f"Hologram derivation of the birthday states from cake.rsi ({src_meta['copyright']}); acid-green hologram recolor, scanlines and flicker animation by Solreign (AI-generated, human-reviewed)",
    "size": {"x": 32, "y": 32},
    "states": [
        {"name": "birthday", "delays": [delays]},
        {"name": "birthday-inhand-left", "directions": 4},
        {"name": "birthday-inhand-right", "directions": 4},
    ],
}
with open(f"{DST}/meta.json", "w") as fp:
    json.dump(meta, fp, indent=2); fp.write("\n")

# preview sheet + web GIF frames dumped for the caller
strip.resize((32*len(frames)*8, 256), Image.NEAREST).save(f"{OUT}/holographic_cake-birthday-after-sheet.png")
src.resize((256, 256), Image.NEAREST).save(f"{OUT}/holographic_cake-birthday-before-sheet.png")

WEB = os.path.expanduser("~/AI/solreign-trees/website-v2/website/public/assets/game/anim")
big = [f.resize((256, 256), Image.NEAREST) for f in frames]
pf = []
for f in big:
    alpha = f.getchannel("A")
    p = f.convert("RGB").convert("P", palette=Image.ADAPTIVE, colors=255)
    p.paste(255, Image.eval(alpha, lambda a: 255 if a < 128 else 0))
    pf.append(p)
pf[0].save(f"{WEB}/holographic_cake.gif", save_all=True, append_images=pf[1:],
           duration=[int(d*1000) for d in delays], loop=0, disposal=2, transparency=255)
print("holographic_cake.rsi built:", delays)
