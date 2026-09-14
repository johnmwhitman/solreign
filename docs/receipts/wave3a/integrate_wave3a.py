#!/usr/bin/env python3
"""Integrate the staged WAVE 3A RSIs (wave3a-proc/) into the working tree.
Usage: integrate_wave3a.py [--dry]"""
import json, os, shutil, sys
from PIL import Image

SCR = os.path.dirname(os.path.abspath(__file__))
PROC = f"{SCR}/wave3a-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/orch-regen-wave3a")

sys.path.insert(0, SCR)
from assemble_wave3a import CONFIG

def main():
    dry = "--dry" in sys.argv
    n = 0
    for rel in sorted(CONFIG):
        name = os.path.basename(rel)  # e.g. "powersink.rsi"
        sdir = f"{PROC}/{name}"
        ddir = f"{REPO}/Resources/Textures/{rel}"
        smeta = json.load(open(f"{sdir}/meta.json"))
        ometa = json.load(open(f"{ddir}/meta.json", encoding="utf-8-sig"))

        # gate 1: NC in, non-NC out
        assert "NC" in ometa["license"], f"{name}: original not NC ({ometa['license']})"
        assert "NC" not in smeta["license"], f"{name}: replacement still NC"

        # gate 2: exact state-name parity
        so = sorted(s["name"] for s in smeta["states"])
        oo = sorted(s["name"] for s in ometa["states"])
        assert so == oo, f"{name}: state mismatch\n  new: {so}\n  old: {oo}"

        # gate 3: per-state pixel-geometry parity (size + directions + frame count)
        for s in smeta["states"]:
            new = Image.open(f"{sdir}/{s['name']}.png")
            old = Image.open(f"{ddir}/{s['name']}.png")
            odirs = next(o.get("directions", 1) for o in ometa["states"] if o["name"] == s["name"])
            ndirs = s.get("directions", 1)
            assert ndirs == odirs, f"{name}/{s['name']}: directions {ndirs} vs {odirs}"
            assert new.size == old.size, f"{name}/{s['name']}: {new.size} vs original {old.size}"
            # gate 3b: enough tile capacity for the declared frame*direction count. NOT a
            # strict equality -- RobustToolbox's on-disk RSI grid may have trailing empty
            # slots (RsiLoading.cs only requires "image size is a multiple of icon size",
            # not an exact tile count; confirmed several ORIGINAL NC states in this same
            # repo pack e.g. 8 frames into a 9-slot 3x3 grid). Since our canvas dimensions
            # are read directly from the original file (new.size == old.size, just
            # asserted), tile CAPACITY parity is what actually matters here.
            nframes = len(s["delays"][0]) if "delays" in s else 1
            tiles = (new.size[0] // 32) * (new.size[1] // 32)
            assert tiles >= nframes * ndirs, \
                f"{name}/{s['name']}: {tiles} tile slots < {nframes}f x {ndirs}d declared"

        if not dry:
            shutil.rmtree(ddir)
            shutil.copytree(sdir, ddir)
        n += 1
        print(f"{name}: {'DRY-OK' if dry else 'INTEGRATED'} "
              f"({len(smeta['states'])} states, {ometa['license']} -> {smeta['license']})")
    print(f"\n{n}/{len(CONFIG)} assets {'validated (dry run)' if dry else 'integrated'}")

if __name__ == "__main__":
    main()
