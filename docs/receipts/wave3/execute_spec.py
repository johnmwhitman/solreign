#!/usr/bin/env python3
"""Execute a fleet-authored animation spec (design-brief JSON schema) against Solreign RSIs.

Usage: execute_spec.py spec.json [--dry]  — dry mode renders sheets/GIF previews to scratchpad only,
leaving the .rsi files and website assets untouched.
"""
import collections, json, os, sys
from PIL import Image

RSI = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims/Resources/Textures/_Solreign")
WEB_ANIM = os.path.expanduser("~/AI/solreign-trees/website-v2/website/public/assets/game/anim")
OUT = os.path.dirname(os.path.abspath(__file__))
LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*+="
DIR_ORDER = ["S", "N", "E", "W"]

def clamp(v): return max(0, min(255, int(round(v))))

def build_palette(im):
    """Same palette-letter assignment as census.py: letters by descending count over the whole PNG."""
    cnt = collections.Counter()
    for y in range(im.height):
        for x in range(im.width):
            px = im.getpixel((x, y))
            if px[3] > 0:
                cnt[px[:3]] += 1
    return {LETTERS[i]: c for i, (c, n) in enumerate(cnt.most_common())}

def resolve_mask(tile, spec, pal):
    """Return set of (x,y) tile-local coords for a mask spec."""
    px = set()
    if "pixels" in spec:
        for x, y in spec["pixels"]:
            px.add((x, y))
        return px
    colors = {pal[l] for l in spec.get("letters", []) if l in pal}
    x0, y0, x1, y1 = spec.get("region", [0, 0, 31, 31])
    for y in range(max(0, y0), min(32, y1 + 1)):
        for x in range(max(0, x0), min(32, x1 + 1)):
            p = tile.getpixel((x, y))
            if p[3] > 0 and p[:3] in colors:
                px.add((x, y))
    return px

def apply_ops(base_tile, ops, masks, pal):
    im = base_tile.copy()
    for op in ops:
        kind = op["op"]
        if kind == "shift_down_1":
            assert all(base_tile.getpixel((x, 31))[3] == 0 for x in range(32)), "shift would clip bottom row"
            im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
            im.paste(base_tile, (0, 1))
            continue
        if kind in ("brightness", "alpha", "lerp", "hue_shift"):
            for (x, y) in resolve_mask(base_tile, masks[op["mask"]], pal):
                r, g, b, a = base_tile.getpixel((x, y))
                if a == 0: continue
                if kind == "brightness":
                    f = op["factor"]
                    im.putpixel((x, y), (clamp(r*f), clamp(g*f), clamp(b*f), a))
                elif kind == "hue_shift":
                    import colorsys
                    h, s_, v = colorsys.rgb_to_hsv(r/255, g/255, b/255)
                    nr, ng, nb = colorsys.hsv_to_rgb((h + op["degrees"]/360.0) % 1.0, s_, v)
                    im.putpixel((x, y), (clamp(nr*255), clamp(ng*255), clamp(nb*255), a))
                elif kind == "alpha":
                    im.putpixel((x, y), (r, g, b, min(a, int(op["value"]))))
                else:
                    tr, tg, tb = op["to"]; t = op["t"]
                    im.putpixel((x, y), (clamp(r+(tr-r)*t), clamp(g+(tg-g)*t), clamp(b+(tb-b)*t), a))
        elif kind == "px_off":
            x, y = op["at"]; im.putpixel((x, y), (0, 0, 0, 0))
        elif kind == "px_on":  # copy color from a source pixel, or explicit rgba
            x, y = op["at"]
            src = op.get("color") or list(base_tile.getpixel(tuple(op["from"])))
            im.putpixel((x, y), tuple(src))
        elif kind == "px_copy":
            fx, fy = op["from"]; tx, ty = op["to"]
            im.putpixel((tx, ty), base_tile.getpixel((fx, fy)))
        else:
            raise ValueError(f"unknown op {kind}")
    return im

def frames_for_tile(tile, sprite, masks, pal):
    n = sprite["frames"]
    by_frame = {fo["frame"]: fo["ops"] for fo in sprite.get("frame_ops", [])}
    out = [tile.copy()]  # frame 0 always pristine
    for i in range(1, n):
        out.append(apply_ops(tile, by_frame.get(i, []), masks, pal))
    return out

def gif_save(name, frames, delays):
    big = [f.resize((256, 256), Image.NEAREST) for f in frames]
    pf = []
    for f in big:
        alpha = f.getchannel("A")
        p = f.convert("RGB").convert("P", palette=Image.ADAPTIVE, colors=255)
        p.paste(255, Image.eval(alpha, lambda a: 255 if a < 128 else 0))
        pf.append(p)
    pf[0].save(f"{WEB_ANIM}/{name}.gif", save_all=True, append_images=pf[1:],
               duration=[int(d*1000) for d in delays], loop=0, disposal=2, transparency=255)

def main():
    spec = json.load(open(sys.argv[1]))
    dry = "--dry" in sys.argv
    report = []
    for s in spec["sprites"]:
        if s.get("verdict") == "skip":
            report.append((s["rsi"], s["state"], "SKIP", s.get("concept", "")))
            continue
        rsi, state = s["rsi"], s["state"]
        png = f"{RSI}/{rsi}.rsi/{state}.png"
        if not os.path.exists(png):
            png = f"{RSI}/EasterEggs/{rsi}.rsi/{state}.png"
        im = Image.open(png).convert("RGBA")
        pal = build_palette(im)
        n = s["frames"]; delays = s["delays"]
        assert len(delays) == n, f"{rsi}: delays len != frames"
        assert n >= 2, f"{rsi}: need >=2 frames"

        if s.get("per_direction"):
            ndirs = 4
            cols = im.width // 32
            tiles = [im.crop(((t % cols)*32, (t//cols)*32, (t % cols)*32+32, (t//cols)*32+32))
                     for t in range(ndirs)]
            all_frames = []
            for di, dname in enumerate(DIR_ORDER):
                dspec = s["per_direction"][dname]
                masks = dspec.get("masks", s.get("masks", {}))
                dsprite = dict(s); dsprite["frame_ops"] = dspec.get("frame_ops", s.get("frame_ops", []))
                all_frames.append(frames_for_tile(tiles[di], dsprite, masks, pal))
            strip = Image.new("RGBA", (32*n, 32*ndirs), (0, 0, 0, 0))
            for di in range(ndirs):
                for fi in range(n):
                    strip.paste(all_frames[di][fi], (fi*32, di*32))
            meta_delays = [list(delays) for _ in range(ndirs)]
            gif_frames = all_frames[0]  # south-facing for the web GIF
            sheet_frames = all_frames[0]
        else:
            frames = frames_for_tile(im.crop((0, 0, 32, 32)), s, s.get("masks", {}), pal)
            strip = Image.new("RGBA", (32*n, 32), (0, 0, 0, 0))
            for fi, f in enumerate(frames):
                strip.paste(f, (fi*32, 0))
            meta_delays = [list(delays)]
            gif_frames = frames
            sheet_frames = frames

        # preview sheet always
        sheet = Image.new("RGBA", (32*n, 32), (0, 0, 0, 0))
        for fi, f in enumerate(sheet_frames):
            sheet.paste(f, (fi*32, 0))
        sheet.resize((32*n*8, 256), Image.NEAREST).save(f"{OUT}/{rsi}-{state}-after-sheet.png")

        if not dry:
            strip.save(png)
            meta_path = os.path.join(os.path.dirname(png), "meta.json")
            meta = json.load(open(meta_path))
            for st in meta["states"]:
                if st["name"] == state:
                    st["delays"] = meta_delays
            with open(meta_path, "w") as fp:
                json.dump(meta, fp, indent=2); fp.write("\n")
            if not s.get("no_gif"):
                gif_save(s.get('gif_name', rsi), gif_frames, delays)
        report.append((rsi, state, f"{n} frames delays={delays}", s.get("concept", "")))
    for r in report:
        print(" | ".join(str(x) for x in r))
    print("DRY RUN — no files written to RSI/web" if dry else "WROTE RSI + GIFs")

main()
