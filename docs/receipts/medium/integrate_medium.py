#!/usr/bin/env python3
"""Integrate the staged MEDIUM-bucket RSIs into feat/license-regen-integration.
Usage: integrate_medium.py [--dry]"""
import json, os, shutil, subprocess, sys
from PIL import Image

SCR = os.path.dirname(os.path.abspath(__file__))
PROC = f"{SCR}/medium-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")

# staged rsi name -> game-relative rsi dir
MAP = {
    "possum_old":       "Resources/Textures/Mobs/Animals/possum_old.rsi",
    "raccoon":          "Resources/Textures/Mobs/Animals/raccoon.rsi",
    "scurret":          "Resources/Textures/Mobs/Animals/scurret/scurret.rsi",
    "ferret":           "Resources/Textures/Mobs/Pets/ferret.rsi",
    "eldritch_actions": "Resources/Textures/Objects/Magic/Eldritch/eldritch_actions.rsi",
    "buffering":        "Resources/Textures/Objects/Misc/buffering.rsi",
    "guardian_info":    "Resources/Textures/Objects/Misc/guardian_info.rsi",
    "killsign":         "Resources/Textures/Objects/Misc/killsign.rsi",
    "snap_pops":        "Resources/Textures/Objects/Fun/snap_pops.rsi",
    "tinyfan":          "Resources/Textures/Structures/Piping/Atmospherics/tinyfan.rsi",
}

def git_show(rel):
    return subprocess.run(["git", "-C", REPO, "show", f"master:{rel}"],
                          capture_output=True, check=True).stdout

def main():
    dry = "--dry" in sys.argv
    for name, rel in sorted(MAP.items()):
        sdir = f"{PROC}/{name}.rsi"
        ddir = f"{REPO}/{rel}"
        smeta = json.load(open(f"{sdir}/meta.json"))
        ometa = json.loads(git_show(f"{rel}/meta.json").decode("utf-8-sig"))
        # gate 1: NC in, non-NC out
        assert "NC" in ometa["license"], f"{name}: original not NC ({ometa['license']})"
        assert "NC" not in smeta["license"], f"{name}: replacement still NC"
        # gate 2: exact state-name parity
        so = sorted(s["name"] for s in smeta["states"])
        oo = sorted(s["name"] for s in ometa["states"])
        assert so == oo, f"{name}: state mismatch\n  new: {so}\n  old: {oo}"
        # gate 3: per-state grid parity (directions preserved; frame count may differ only
        # where we declare delays)
        for s in smeta["states"]:
            new = Image.open(f"{sdir}/{s['name']}.png")
            old = Image.open(__import__("io").BytesIO(git_show(f"{rel}/{s['name']}.png")))
            odirs = next(o.get("directions", 1) for o in ometa["states"] if o["name"] == s["name"])
            ndirs = s.get("directions", 1)
            assert ndirs == odirs, f"{name}/{s['name']}: directions {ndirs} vs {odirs}"
            nframes = len(s["delays"][0]) if "delays" in s else 1
            # every frame count is preserved from the original, so geometry must match exactly
            assert new.size == old.size, f"{name}/{s['name']}: {new.size} vs original {old.size}"
            tiles = (new.size[0] // 32) * (new.size[1] // 32)
            assert tiles == nframes * ndirs, \
                f"{name}/{s['name']}: {tiles} tiles vs {nframes}f x {ndirs}d declared"
        if not dry:
            shutil.rmtree(ddir)
            shutil.copytree(sdir, ddir)
        print(f"{name}: {'DRY-OK' if dry else 'INTEGRATED'} "
              f"({len(smeta['states'])} states, {ometa['license']} -> {smeta['license']})")

main()
