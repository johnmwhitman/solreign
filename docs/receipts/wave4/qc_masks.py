#!/usr/bin/env python3
"""QUALITY GATE: report resolved mask sizes for each wave-4 spec, to catch near-zero/fabricated masks
before execution (wave-3 lesson: a 1-px mask that claims to be a whole feature is a redesign signal)."""
import collections, json, sys
from PIL import Image

TEXTURES = "/Users/johnwhitman/AI/solreign-trees/sprite-idle-anims/Resources/Textures"
LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*+="
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

def build_palette(im):
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

for batch in sys.argv[1:]:
    spec = json.load(open(batch))
    for s in spec["sprites"]:
        rsi, state = s["rsi"], s["state"]
        rel = RSI_PATHS[rsi]
        png = f"{TEXTURES}/{rel}/{state}.png"
        im = Image.open(png).convert("RGBA")
        pal = build_palette(im)
        tile = im.crop((0, 0, 32, 32))
        opaque = sum(1 for y in range(32) for x in range(32) if tile.getpixel((x, y))[3] > 0)
        print(f"--- {rsi}/{state} (opaque px total: {opaque}) ---")
        for name, m in s.get("masks", {}).items():
            px = resolve_mask(tile, m, pal)
            flag = " <<< SUSPICIOUSLY SMALL" if len(px) <= 1 and "pixels" not in m else ""
            print(f"  mask {name}: {len(px)} px{flag}  spec={m}")
