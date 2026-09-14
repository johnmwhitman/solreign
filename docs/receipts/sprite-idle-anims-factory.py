#!/usr/bin/env python3
"""Solreign sprite-animation factory PILOT: companion_cube heart pulse + hot_potato fuse flicker."""
import colorsys, json, math, os
from PIL import Image

RSI_DIR = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims/Resources/Textures/_Solreign")
WEB_ANIM = os.path.expanduser("~/AI/solreign-trees/website-v2/website/public/assets/game/anim")
OUT = os.path.dirname(os.path.abspath(__file__))

def clamp(v): return max(0, min(255, int(round(v))))

def scale_px(px, f):
    r, g, b, a = px
    return (clamp(r * f), clamp(g * f), clamp(b * f), a)

# ---------- companion cube: 4-frame sine pulse on green-hue pixels ----------
def cube_frames():
    base = Image.open(f"{RSI_DIR}/companion_cube.rsi/icon.png").convert("RGBA")
    greens = []
    for y in range(base.height):
        for x in range(base.width):
            r, g, b, a = base.getpixel((x, y))
            if a == 0: continue
            h, s, v = colorsys.rgb_to_hsv(r/255, g/255, b/255)
            if 0.2 < h < 0.45 and s > 0.35:
                greens.append((x, y))
    assert len(greens) >= 20, f"green mask too small: {len(greens)}"
    frames = []
    AMP = 0.28
    for i in range(4):
        f = 1.0 + AMP * math.sin(2 * math.pi * i / 4)  # 1.0, 1.28, 1.0, 0.72
        im = base.copy()
        for (x, y) in greens:
            im.putpixel((x, y), scale_px(base.getpixel((x, y)), f))
        frames.append(im)
    return frames, [0.5, 0.5, 0.5, 0.5], len(greens)

# ---------- hot potato: 3-frame fuse flicker + steam toggle ----------
def potato_frames():
    base = Image.open(f"{RSI_DIR}/hot_potato.rsi/icon.png").convert("RGBA")
    flame, steam = [], []
    for y in range(base.height):
        for x in range(base.width):
            r, g, b, a = base.getpixel((x, y))
            if a == 0: continue
            h, s, v = colorsys.rgb_to_hsv(r/255, g/255, b/255)
            # flame: bright warm pixels in the fuse-tip region only (body highlights excluded)
            if 3 <= x <= 10 and 2 <= y <= 8 and v > 0.75 and s > 0.3 and h < 0.18:
                flame.append((x, y))
            # steam: cool pale wisps right of the fuse, above the potato body
            elif 14 <= x <= 25 and 5 <= y <= 11 and v > 0.6 and s < 0.25:
                steam.append((x, y))
    assert len(flame) >= 15, f"flame mask too small: {len(flame)}"
    assert len(steam) >= 8, f"steam mask too small: {len(steam)}"
    tip = min(flame, key=lambda p: (p[1], abs(p[0] - 7)))  # topmost flame pixel
    frames = []
    # (flame brightness, tip mode, steam alpha)
    for f, tipmode, salpha in [(1.0, "keep", 255), (1.22, "grow", 150), (0.82, "shrink", 210)]:
        im = base.copy()
        for (x, y) in flame:
            im.putpixel((x, y), scale_px(base.getpixel((x, y)), f))
        tx, ty = tip
        if tipmode == "grow" and ty - 1 >= 0 and im.getpixel((tx, ty - 1))[3] == 0:
            im.putpixel((tx, ty - 1), scale_px(base.getpixel((tx, ty)), f))
        elif tipmode == "shrink":
            im.putpixel((tx, ty), (0, 0, 0, 0))
        for (x, y) in steam:
            r, g, b, a = base.getpixel((x, y))
            im.putpixel((x, y), (r, g, b, min(a, salpha)))
        frames.append(im)
    return frames, [0.2, 0.15, 0.2], (len(flame), len(steam), tip)

# ---------- packers ----------
def pack_strip(frames):
    strip = Image.new("RGBA", (32 * len(frames), 32), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        strip.paste(f, (i * 32, 0))
    return strip

def write_rsi(name, frames, delays):
    strip = pack_strip(frames)
    strip.save(f"{RSI_DIR}/{name}.rsi/icon.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Solreign (AI-generated, human-reviewed)",
        "size": {"x": 32, "y": 32},
        "states": [{"name": "icon", "delays": [delays]}],
    }
    with open(f"{RSI_DIR}/{name}.rsi/meta.json", "w") as fp:
        json.dump(meta, fp, indent=2)
        fp.write("\n")

def save_gif(name, frames, delays):
    big = [f.resize((256, 256), Image.NEAREST) for f in frames]
    pal_frames = []
    for f in big:
        alpha = f.getchannel("A")
        p = f.convert("RGB").convert("P", palette=Image.ADAPTIVE, colors=255)
        mask = Image.eval(alpha, lambda a: 255 if a < 128 else 0)
        p.paste(255, mask)
        pal_frames.append(p)
    pal_frames[0].save(
        f"{WEB_ANIM}/{name}.gif", save_all=True, append_images=pal_frames[1:],
        duration=[int(d * 1000) for d in delays], loop=0, disposal=2, transparency=255,
    )

def sheet(name, frames, tag):
    s = pack_strip(frames).resize((32 * len(frames) * 8, 256), Image.NEAREST)
    s.save(f"{OUT}/{name}-{tag}-sheet.png")

# ---------- run ----------
for name, fn in [("companion_cube", cube_frames), ("hot_potato", potato_frames)]:
    before = Image.open(f"{RSI_DIR}/{name}.rsi/icon.png").convert("RGBA")
    sheet(name, [before], "before")
    frames, delays, info = fn()
    write_rsi(name, frames, delays)
    save_gif(name, frames, delays)
    sheet(name, frames, "after")
    print(name, "frames:", len(frames), "delays:", delays, "mask-info:", info)
print("OK")
