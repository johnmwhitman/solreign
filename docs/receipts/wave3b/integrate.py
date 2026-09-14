#!/usr/bin/env python3
"""Integrate staged wave3b RSIs into the worktree. Usage: integrate.py [--dry]"""
import json, os, shutil, sys
from PIL import Image

SCR = os.path.dirname(os.path.abspath(__file__))
PROC = f"{SCR}/icons-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/orch-regen-wave3b")

MAP = {
    "omnitool":          "Resources/Textures/Objects/Tools/omnitool.rsi",
    "directionalfan":    "Resources/Textures/Structures/Piping/Atmospherics/directionalfan.rsi",
    "xenoturret":        "Resources/Textures/Objects/Weapons/Guns/Turrets/xenoturret.rsi",
    "toy_singularity":   "Resources/Textures/Objects/Fun/toy_singularity.rsi",
    "cash":              "Resources/Textures/Objects/Economy/cash.rsi",
    "artifact_fragments": "Resources/Textures/Objects/Specific/Xenoarchaeology/artifact_fragments.rsi",
    "ore":               "Resources/Textures/Objects/Materials/ore.rsi",
    "carp":              "Resources/Textures/Objects/Fun/Plushies/carp.rsi",
    "monitors":          "Resources/Textures/Structures/monitors.rsi",
}


def read_original_meta(rel):
    p = f"{REPO}/{rel}/meta.json"
    with open(p, "rb") as fh:
        return json.loads(fh.read().decode("utf-8-sig"))


def main():
    dry = "--dry" in sys.argv
    ok = True
    for name, rel in sorted(MAP.items()):
        sdir = f"{PROC}/{name}.rsi"
        ddir = f"{REPO}/{rel}"
        smeta = json.load(open(f"{sdir}/meta.json"))
        ometa = read_original_meta(rel)

        # gate 1: NC in, non-NC out
        if "NC" not in ometa["license"]:
            print(f"SKIP {name}: original not NC ({ometa['license']})")
            continue
        assert "NC" not in smeta["license"], f"{name}: replacement still NC"

        # gate 2: exact state-name parity
        so = sorted(s["name"] for s in smeta["states"])
        oo = sorted(s["name"] for s in ometa["states"])
        if so != oo:
            print(f"FAIL {name}: state mismatch\n  new only: {sorted(set(so)-set(oo))}\n  old only: {sorted(set(oo)-set(so))}")
            ok = False
            continue

        # gate 3: per-state geometry — size must equal original's exact pixel dims,
        # and declared frames*directions must equal the addressable tile count.
        bad = []
        for s in smeta["states"]:
            new = Image.open(f"{sdir}/{s['name']}.png")
            old = Image.open(f"{REPO}/{rel}/{s['name']}.png")
            if new.size != old.size:
                bad.append(f"{s['name']}: size {new.size} vs original {old.size}")
                continue
            ndirs = s.get("directions", 1)
            nframes = len(s["delays"][0]) if "delays" in s else 1
            tiles = (new.size[0] // 32) * (new.size[1] // 32)
            if tiles < nframes * ndirs:
                bad.append(f"{s['name']}: only {tiles} tiles but need {nframes}f x {ndirs}d")
        if bad:
            print(f"FAIL {name}: geometry issues:")
            for b in bad:
                print("   ", b)
            ok = False
            continue

        if not dry:
            shutil.rmtree(ddir)
            shutil.copytree(sdir, ddir)
        print(f"{name}: {'DRY-OK' if dry else 'INTEGRATED'} ({len(smeta['states'])} states, {ometa['license']} -> {smeta['license']})")

    if not ok:
        print("\nGATE FAILURES — nothing integrated for failing assets.")
        sys.exit(1)


if __name__ == "__main__":
    main()
