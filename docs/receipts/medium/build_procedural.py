#!/usr/bin/env python3
"""Procedural MEDIUM-bucket replacements: killsign, buffering, tinyfan, voidblink.
Original NC art never read — geometry/timing replicated from meta.json + concept only.
Writes staged RSIs to medium-proc/<name>.rsi + preview sheets."""
import json, math, os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "medium-proc")
COPY = "Programmatically drawn for Solreign by the sprite factory (AI-authored code, human-reviewed); replaces CC-BY-NC art"


REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
ORIG_PNG = {
    "killsign": "Resources/Textures/Objects/Misc/killsign.rsi",
    "buffering": "Resources/Textures/Objects/Misc/buffering.rsi",
    "tinyfan": "Resources/Textures/Structures/Piping/Atmospherics/tinyfan.rsi",
    "eldritch_actions": "Resources/Textures/Objects/Magic/Eldritch/eldritch_actions.rsi",
}

def pack_like(asset, state, frames):
    """Pack frames into the same grid shape the original PNG uses."""
    ow, oh = Image.open(f"{REPO}/{ORIG_PNG[asset]}/{state}.png").size
    cols = ow // 32
    out = Image.new("RGBA", (ow, oh), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        out.paste(f, ((i % cols)*32, (i // cols)*32))
    return out

FONT = {  # 4x6 pixel caps
 'A':["0110","1001","1001","1111","1001","1001"], 'B':["1110","1001","1110","1001","1001","1110"],
 'C':["0111","1000","1000","1000","1000","0111"], 'D':["1110","1001","1001","1001","1001","1110"],
 'E':["1111","1000","1110","1000","1000","1111"], 'F':["1111","1000","1110","1000","1000","1000"],
 'G':["0111","1000","1000","1011","1001","0111"], 'H':["1001","1001","1111","1001","1001","1001"],
 'I':["111","010","010","010","010","111"],       'K':["1001","1010","1100","1100","1010","1001"],
 'L':["1000","1000","1000","1000","1000","1111"], 'N':["1001","1101","1101","1011","1011","1001"],
 'O':["0110","1001","1001","1001","1001","0110"], 'P':["1110","1001","1001","1110","1000","1000"],
 'R':["1110","1001","1001","1110","1010","1001"], 'S':["0111","1000","0110","0001","0001","1110"],
 'T':["111","010","010","010","010","010"],       'U':["1001","1001","1001","1001","1001","0110"],
 'Y':["101","101","010","010","010","010"],
}

def draw_word(im, word, y0, color, outline=(24, 24, 28, 255)):
    widths = [len(FONT[c][0]) for c in word]
    total = sum(widths) + len(word) - 1
    x = (32 - total) // 2
    px = []
    for c, w in zip(word, widths):
        for ry, row in enumerate(FONT[c]):
            for rx, bit in enumerate(row):
                if bit == "1":
                    px.append((x + rx, y0 + ry))
        x += w + 1
    for (X, Y) in px:  # 1px drop shadow then glyph
        if 0 <= X+1 < 32 and 0 <= Y+1 < 32:
            im.putpixel((X+1, Y+1), outline)
    for (X, Y) in px:
        im.putpixel((X, Y), (*color, 255))

def draw_arrow(im, y0, color, outline=(24, 24, 28, 255)):
    w = 11
    x0 = (32 - w) // 2
    rows = [(x0, x0 + w - 1), (x0+2, x0+w-3), (x0+4, x0+w-5), (x0+5, x0+w-6)]
    for dy, (a, b) in enumerate(rows):
        for x in range(a, b + 1):
            im.putpixel((x, y0 + dy), (*color, 255))
            if y0+dy+1 < 32: im.putpixel((x, y0+dy+1), outline) if dy == len(rows)-1 else None
    # outline pass (below tip)
    return

def killsign():
    words = {"bald":"BALD","cat":"CAT","dog":"DOG","furry":"FURRY","it":"IT","kill":"KILL",
             "nerd":"NERD","peak":"PEAK","raider":"RAIDER","stinky":"STINKY"}
    pal = {"bald":((63,169,63),(127,224,127)), "cat":((155,79,214),(224,127,224)),
           "dog":((232,232,232),(224,127,224)), "furry":((155,79,214),(224,127,224)),
           "it":((214,59,47),(232,132,47)),     "kill":((214,59,47),(232,132,47)),
           "nerd":((155,79,214),(224,127,224)), "peak":((63,200,232),(143,224,240)),
           "raider":((143,31,31),(232,132,47)), "stinky":((63,169,63),(143,224,143))}
    states, files = [], {}
    for st, word in words.items():
        c1, c2 = pal[st]
        frames = []
        for fi in range(2):
            im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
            col = c1 if fi == 0 else c2
            draw_word(im, word, 6, col)
            draw_arrow(im, 16 + fi, col)  # arrow bounces 1px on the alternate frame
            frames.append(im)
        files[st] = pack_like("killsign", st, frames)
        states.append({"name": st, "delays": [[0.15, 0.15]]})
    for st, word, col in [("icon", "KILL", (214,59,47)), ("icon-hidden", "HIDE", (214,59,47))]:
        im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        d.rounded_rectangle([3, 5, 28, 26], radius=3, fill=(238, 238, 232, 255), outline=(60, 60, 64, 255))
        draw_word(im, word, 9, col, outline=(190, 185, 180, 255))
        draw_arrow(im, 18, col, outline=(190, 185, 180, 255))
        files[st] = im
        states.append({"name": st})
    return states, files

def buffering():
    frames = []
    N = 8  # dot positions on the ring; 6 lit with trailing sizes
    for fi in range(6):
        im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        for k in range(6):
            pos = (fi + k) % N
            ang = -math.pi/2 + pos * 2*math.pi/N
            cx, cy = 15.5 + 10*math.cos(ang), 15.5 + 10*math.sin(ang)
            r = [3, 3, 2, 2, 1, 1][k]  # head big, tail small
            x, y = int(round(cx)), int(round(cy))
            for dy in range(-r, r+1):  # diamond
                for dx in range(-(r-abs(dy)), r-abs(dy)+1):
                    if 0 <= x+dx < 32 and 0 <= y+dy < 32:
                        im.putpixel((x+dx, y+dy), (30, 30, 34, 255))
        frames.append(im)
    return [{"name": "icon", "delays": [[0.2]*6]}], {"icon": pack_like("buffering", "icon", frames)}

def tinyfan():
    S = 8  # supersample
    frames = []
    for fi in range(4):
        big = Image.new("RGBA", (32*S, 32*S), (0, 0, 0, 0))
        d = ImageDraw.Draw(big)
        c = 16*S
        d.rectangle([2*S, 2*S, 30*S-1, 30*S-1], outline=(52, 54, 58, 255), width=S)
        for corner in [(2,2),(26,2),(2,26),(26,26)]:
            d.rectangle([corner[0]*S, corner[1]*S, (corner[0]+4)*S, (corner[1]+4)*S], fill=(52,54,58,255))
        ang0 = math.radians(45 + fi * 22.5)
        for b in range(4):
            a = ang0 + b * math.pi/2
            tip = (c + 11.5*S*math.cos(a), c + 11.5*S*math.sin(a))
            l = (c + 3*S*math.cos(a + 0.55), c + 3*S*math.sin(a + 0.55))
            r = (c + 3*S*math.cos(a - 0.55), c + 3*S*math.sin(a - 0.55))
            mid_l = (c + 10*S*math.cos(a + 0.28), c + 10*S*math.sin(a + 0.28))
            mid_r = (c + 10*S*math.cos(a - 0.28), c + 10*S*math.sin(a - 0.28))
            d.polygon([l, mid_l, tip, mid_r, r], fill=(38, 40, 44, 255))
        d.ellipse([c-2.5*S, c-2.5*S, c+2.5*S, c+2.5*S], fill=(64, 66, 72, 255))
        frames.append(big.resize((32, 32), Image.BOX))
    return [{"name": "icon", "delays": [[0.01]*4]}], {"icon": pack_like("tinyfan", "icon", frames)}

def voidblink():
    base = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    ink = (12, 10, 16, 255)
    d.ellipse([12, 2, 19, 9], fill=ink)                     # head
    d.polygon([(10, 10), (21, 10), (23, 20), (8, 20)], fill=ink)  # torso
    d.rectangle([8, 10, 10, 17], fill=ink); d.rectangle([21, 10, 23, 17], fill=ink)  # arms
    d.rectangle([11, 20, 14, 29], fill=ink); d.rectangle([17, 20, 20, 29], fill=ink) # legs
    body = [(x, y) for y in range(32) for x in range(32) if base.getpixel((x, y))[3] > 0]
    def keep(x, y, salt, pct):
        return (x*73856093 ^ y*19349663 ^ salt*83492791) % 100 < pct
    pcts = [100, 55, 25, 6, 6, 25, 55, 100, 100]
    salts = [0, 1, 2, 3, 4, 5, 6, 0, 0]
    frames = []
    for pct, salt in zip(pcts, salts):
        im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
        for (x, y) in body:
            if keep(x, y, salt, pct):
                im.putpixel((x, y), (12, 10, 16, 255))
        frames.append(im)
    return [{"name": "voidblink", "delays": [[0.1,0.3,0.1,0.3,0.1,0.3,0.1,0.3,0.3]]}], {"voidblink": pack_like("eldritch_actions", "voidblink", frames)}

for name, fn in [("killsign", killsign), ("buffering", buffering), ("tinyfan", tinyfan), ("eldritch_actions", voidblink)]:
    states, files = fn()
    d = f"{OUT}/{name}.rsi"
    os.makedirs(d, exist_ok=True)
    for st, im in files.items():
        im.save(f"{d}/{st}.png")
    meta = {"version": 1, "license": "CC0-1.0", "copyright": COPY, "size": {"x": 32, "y": 32}, "states": states}
    with open(f"{d}/meta.json", "w") as fp:
        json.dump(meta, fp, indent=2); fp.write("\n")
    # preview
    total = sum((im.width//32)*(im.height//32) for im in files.values())
    sheet = Image.new("RGBA", (total*36+4, 40), (28, 28, 32, 255))
    x = 4
    for st, im in files.items():
        cols = im.width//32
        for i in range((im.width//32)*(im.height//32)):
            sheet.paste(im.crop(((i%cols)*32, (i//cols)*32, (i%cols)*32+32, (i//cols)*32+32)), (x, 4)); x += 36
    sheet.resize((sheet.width*4, sheet.height*4), Image.NEAREST).save(f"{OUT}/{name}-preview.png")
    print(name, "built:", [s["name"] for s in states])
