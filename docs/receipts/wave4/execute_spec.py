#!/usr/bin/env python3
"""Execute a fleet-authored animation spec (design-brief JSON schema) against Solreign RSIs — WAVE 4.

Wave-4 variant of docs/receipts/wave3/execute_spec.py: identical op algorithm, generalized RSI
path resolution (wave-4 candidates live outside _Solreign, all over the vanilla asset tree, since
_Solreign itself is now fully animated). Website/GIF output is DISABLED for this wave — that would
write into the sibling website-v2 worktree, which is out of scope for this branch.

Usage: execute_spec.py spec.json [--dry]  — dry mode renders sheets to scratchpad only,
leaving the .rsi files untouched.
"""
import collections, json, os, sys
from PIL import Image

TEXTURES = os.path.expanduser("~/AI/solreign-trees/sprite-idle-anims/Resources/Textures")
OUT = os.path.dirname(os.path.abspath(__file__))
LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*+="
DIR_ORDER = ["S", "N", "E", "W"]

# rsi (design-brief label) -> relative dir under Resources/Textures containing meta.json + state PNGs
RSI_PATHS = {
    "crystal_grey": "Structures/Decoration/crystal.rsi",
    "telecrystal": "Objects/Specific/Syndicate/telecrystal.rsi",
    "crystal_shard1": "Objects/Materials/Shards/crystal.rsi",
    "crystal_shard2": "Objects/Materials/Shards/crystal.rsi",
    "crystal_shard3": "Objects/Materials/Shards/crystal.rsi",
    "oracle_screen": "Structures/Wallmounts/screen.rsi",
    "glowstick_lit": "Objects/Misc/glowstick.rsi",
    "flashlight_overlay": "Objects/Tools/flashlight.rsi",
    "carp_statue_eyes": "Structures/Specific/carp_statue.rsi",
    "glowstick_glow": "Objects/Misc/glowstick.rsi",
    "carp_statue_teeth": "Structures/Specific/carp_statue.rsi",
}

def clamp(v): return max(0, min(255, int(round(v))))

def build_palette(im):
    """Same palette-letter assignment as wave3's build_palette: letters by descending count over the whole PNG."""
    cnt = collections.Counter()
    for y in range(im.height):
        for x in range(im.width):
            px = im.getpixel((x, y))
            if px[3] > 0:
                cnt[px[:3]] += 1
    return {LETTERS[i]: c for i, (c, n) in enumerate(cnt.most_common())}

def resolve_mask(tile, spec, pal):
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
        elif kind == "px_on":
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

def main():
    spec = json.load(open(sys.argv[1]))
    dry = "--dry" in sys.argv
    report = []
    for s in spec["sprites"]:
        if s.get("verdict") == "skip":
            report.append((s["rsi"], s["state"], "SKIP", s.get("concept", "")))
            continue
        rsi, state = s["rsi"], s["state"]
        rel = s.get("path") or RSI_PATHS.get(rsi)
        assert rel, f"no path mapping for rsi label {rsi!r} — add it to RSI_PATHS"
        png = f"{TEXTURES}/{rel}/{state}.png"
        assert os.path.exists(png), f"missing PNG: {png}"
        im = Image.open(png).convert("RGBA")
        pal = build_palette(im)
        n = s["frames"]; delays = s["delays"]
        assert len(delays) == n, f"{rsi}: delays len != frames"
        assert n >= 2, f"{rsi}: need >=2 frames"
        assert im.size == (32, 32), f"{rsi}: wave-4 executor only handles single 32x32 tiles, got {im.size}"

        frames = frames_for_tile(im.crop((0, 0, 32, 32)), s, s.get("masks", {}), pal)
        strip = Image.new("RGBA", (32*n, 32), (0, 0, 0, 0))
        for fi, f in enumerate(frames):
            strip.paste(f, (fi*32, 0))
        meta_delays = [list(delays)]
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
            found = False
            for st in meta["states"]:
                if st["name"] == state:
                    st["delays"] = meta_delays
                    found = True
            assert found, f"{rsi}: state {state!r} not declared in {meta_path}"
            with open(meta_path, "w") as fp:
                json.dump(meta, fp, indent=2); fp.write("\n")
            # NOTE: wave-4 deliberately does NOT touch website-v2 (sibling worktree, out of scope).
        report.append((rsi, state, f"{n} frames delays={delays}", s.get("concept", "")))
    for r in report:
        print(" | ".join(str(x) for x in r))
    print("DRY RUN — no files written" if dry else "WROTE RSI states (no website/GIF output this wave)")

main()
