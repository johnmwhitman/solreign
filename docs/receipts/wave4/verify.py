#!/usr/bin/env python3
"""Ground-truth verification for wave-4 (the wave-3 bar, fully programmatic):
- frame 0 byte-identical to `git show HEAD:<path>` (the pre-wave4 committed original)
- PNG grid width == declared frame count * 32, height == 32
- per-frame diff > 0 vs the previous frame (no dead frames)
- license/copyright in meta.json preserved verbatim vs the pre-wave4 committed original
- meta.json delays match what the spec declared
"""
import io, json, os, subprocess, sys
from PIL import Image

REPO = "/Users/johnwhitman/AI/solreign-trees/sprite-idle-anims"
TEXTURES = f"{REPO}/Resources/Textures"
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

def git_show(rel_path):
    return subprocess.run(["git", "show", f"HEAD:{rel_path}"], cwd=REPO,
                           capture_output=True, check=True).stdout

def check(cond, msg, fails):
    if not cond:
        fails.append(msg)
    print(("PASS " if cond else "FAIL ") + msg)

def main():
    fails = []
    touched = set()
    for batch in sys.argv[1:]:
        spec = json.load(open(batch))
        for s in spec["sprites"]:
            touched.add((RSI_PATHS[s["rsi"]], s["state"]))

    for batch in sys.argv[1:]:
        spec = json.load(open(batch))
        for s in spec["sprites"]:
            rsi, state = s["rsi"], s["state"]
            rel = RSI_PATHS[rsi]
            new_png = f"{TEXTURES}/{rel}/{state}.png"
            new_meta_path = f"{TEXTURES}/{rel}/meta.json"
            orig_png_bytes = git_show(f"Resources/Textures/{rel}/{state}.png")
            orig_meta_j = json.loads(git_show(f"Resources/Textures/{rel}/meta.json"))

            n = s["frames"]; delays = s["delays"]
            im = Image.open(new_png).convert("RGBA")
            check(im.size == (32*n, 32), f"{rsi}/{state}: grid size {im.size} == ({32*n},32)", fails)

            frame0 = im.crop((0, 0, 32, 32))
            orig = Image.open(io.BytesIO(orig_png_bytes)).convert("RGBA")
            check(list(frame0.getdata()) == list(orig.getdata()),
                  f"{rsi}/{state}: frame 0 byte-identical to git HEAD original", fails)

            frames = [im.crop((i*32, 0, i*32+32, 32)) for i in range(n)]
            all_diff = True
            for i in range(1, n):
                prev, cur = list(frames[i-1].getdata()), list(frames[i].getdata())
                ndiff = sum(1 for a, b in zip(prev, cur) if a != b)
                if ndiff == 0:
                    all_diff = False
                    print(f"  frame {i-1}->{i}: {ndiff} px differ")
            check(all_diff, f"{rsi}/{state}: every consecutive frame pair differs (no dead frames)", fails)

            meta = json.load(open(new_meta_path))
            check(meta.get("license") == orig_meta_j.get("license"),
                  f"{rsi}/{state}: license preserved verbatim", fails)
            check(meta.get("copyright") == orig_meta_j.get("copyright"),
                  f"{rsi}/{state}: copyright preserved verbatim", fails)

            st = next(x for x in meta["states"] if x["name"] == state)
            check(st.get("delays") == [list(delays)],
                  f"{rsi}/{state}: meta delays == spec delays {delays}", fails)

            orig_states = {x["name"]: x for x in orig_meta_j["states"]}
            for sib in meta["states"]:
                if sib["name"] == state:
                    continue
                if (rel, sib["name"]) in touched:
                    continue  # intentionally animated elsewhere in this same wave
                check(sib == orig_states.get(sib["name"]),
                      f"{rsi}: sibling state {sib['name']!r} meta unchanged", fails)

    print()
    if fails:
        print(f"{len(fails)} FAILURE(S)")
        sys.exit(1)
    print("ALL CHECKS PASSED")

main()
