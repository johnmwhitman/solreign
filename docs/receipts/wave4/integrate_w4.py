#!/usr/bin/env python3
"""Integrate wave 4: ore (rigged), cash + directionalfan (procedural)."""
import io, json, os, shutil, subprocess, sys
from PIL import Image
SCR = os.path.dirname(os.path.abspath(__file__))
STAGE = f"{SCR}/w4-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
MAP = {"ore": "Objects/Materials/ore.rsi", "cash": "Objects/Economy/cash.rsi",
       "directionalfan": "Structures/Piping/Atmospherics/directionalfan.rsi"}
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
        assert "NC" not in smeta["license"], f"{name}: still NC"
        so = sorted(s["name"] for s in smeta["states"])
        oo = sorted(s["name"] for s in ometa["states"])
        assert so == oo, f"{name}: state mismatch\n new {so}\n old {oo}"
        for s in smeta["states"]:
            new = Image.open(f"{sdir}/{s['name']}.png")
            old = Image.open(io.BytesIO(show(f"{rel}/{s['name']}.png")))
            o = next(x for x in ometa["states"] if x["name"] == s["name"])
            assert s.get("directions",1) == o.get("directions",1), f"{name}/{s['name']}: dirs"
            nf = len(s["delays"][0]) if "delays" in s else 1
            of = len(o["delays"][0]) if o.get("delays") else 1
            assert nf == of, f"{name}/{s['name']}: {nf} frames vs original {of}"
            assert new.size == old.size, f"{name}/{s['name']}: {new.size} vs {old.size}"
            tiles = (new.size[0]//32)*(new.size[1]//32)
            assert tiles >= nf*s.get("directions",1), f"{name}/{s['name']}: {tiles} tiles"
        if not dry:
            shutil.rmtree(ddir); shutil.copytree(sdir, ddir)
        print(f"{name}: {'DRY-OK' if dry else 'INTEGRATED'} ({len(smeta['states'])} states, {ometa['license']} -> CC0-1.0)")
main()
