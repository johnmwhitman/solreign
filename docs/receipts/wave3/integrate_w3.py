#!/usr/bin/env python3
"""Integrate wave 3a (inhand-heavy, rigged) + 3b (omnitool via img2img) into the game tree."""
import io, json, os, shutil, subprocess, sys
from PIL import Image

SCR = os.path.dirname(os.path.abspath(__file__))
STAGE = f"{SCR}/w3a/rsi"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
MAP = {
 "beach_ball":"Objects/Fun/Balls/beach_ball.rsi", "football":"Objects/Fun/Balls/football.rsi",
 "corgi":"Objects/Fun/Balloons/corgi.rsi", "nanotrasen":"Objects/Fun/Balloons/nanotrasen.rsi",
 "syndicate":"Objects/Fun/Balloons/syndicate.rsi", "rubber_chicken":"Objects/Fun/rubber_chicken.rsi",
 "clownrecorder":"Objects/Fun/clownrecorder.rsi", "lamp":"Objects/Fun/Plushies/lamp.rsi",
 "pondering_orb":"Objects/Fun/pondering_orb.rsi", "powersink":"Objects/Power/powersink.rsi",
 "cutlass":"Objects/Weapons/Melee/cutlass.rsi", "machete":"Objects/Weapons/Melee/machete.rsi",
 "incomplete_bat":"Objects/Weapons/Melee/incomplete_bat.rsi", "handdrill":"Objects/Tools/handdrill.rsi",
 "foam_grenade":"Objects/Fun/Foam/foam_grenade.rsi", "foam_crossbow":"Objects/Fun/Foam/foam_crossbow.rsi",
 "foam_blade":"Objects/Fun/Foam/foam_blade.rsi", "omnitool":"Objects/Tools/omnitool.rsi",
}
def show(rel):
    return subprocess.run(["git","-C",REPO,"show",f"master:Resources/Textures/{rel}"],
                          capture_output=True, check=True).stdout
def main():
    dry = "--dry" in sys.argv
    for name, rel in sorted(MAP.items()):
        sdir, ddir = f"{STAGE}/{name}.rsi", f"{REPO}/Resources/Textures/{rel}"
        smeta = json.load(open(f"{sdir}/meta.json"))
        ometa = json.loads(show(f"{rel}/meta.json").decode("utf-8-sig"))
        assert "NC" in ometa["license"], f"{name}: original not NC"
        assert "NC" not in smeta["license"], f"{name}: replacement still NC"
        so = sorted(s["name"] for s in smeta["states"])
        oo = sorted(s["name"] for s in ometa["states"])
        assert so == oo, f"{name}: state mismatch\n new {so}\n old {oo}"
        for s in smeta["states"]:
            new = Image.open(f"{sdir}/{s['name']}.png")
            old = Image.open(io.BytesIO(show(f"{rel}/{s['name']}.png")))
            odirs = next(o.get("directions",1) for o in ometa["states"] if o["name"]==s["name"])
            assert s.get("directions",1) == odirs, f"{name}/{s['name']}: dirs"
            assert new.size == old.size, f"{name}/{s['name']}: {new.size} vs {old.size}"
            tiles = (new.size[0]//32)*(new.size[1]//32)
            nf = len(s["delays"][0]) if "delays" in s else 1
            assert tiles == nf*odirs, f"{name}/{s['name']}: {tiles} tiles vs {nf}x{odirs}"
        if not dry:
            shutil.rmtree(ddir); shutil.copytree(sdir, ddir)
        print(f"{name}: {'DRY-OK' if dry else 'INTEGRATED'} ({len(smeta['states'])} states, {ometa['license']} -> CC0-1.0)")
main()
