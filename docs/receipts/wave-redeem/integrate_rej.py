#!/usr/bin/env python3
import io, json, os, shutil, subprocess, sys
from PIL import Image
SCR = os.path.dirname(os.path.abspath(__file__))
STAGE = f"{SCR}/rej-proc"
REPO = os.path.expanduser("~/AI/solreign-trees/regen-final")
MAP = {"lamp": "Objects/Fun/Plushies/lamp.rsi", "capgun": "Objects/Fun/capgun.rsi"}
def show(rel):
    return subprocess.run(["git","-C",REPO,"show",f"master:Resources/Textures/{rel}"],
                          capture_output=True, check=True).stdout
def opq(im):
    im=im.convert("RGBA").crop((0,0,32,32))
    return sum(1 for y in range(32) for x in range(32) if im.getpixel((x,y))[3]>0)
def main():
    dry = "--dry" in sys.argv
    for name, rel in sorted(MAP.items()):
        sdir, ddir = f"{STAGE}/{name}.rsi", f"{REPO}/Resources/Textures/{rel}"
        smeta = json.load(open(f"{sdir}/meta.json"))
        ometa = json.loads(show(f"{rel}/meta.json").decode("utf-8-sig"))
        assert "NC" in ometa["license"] and "NC" not in smeta["license"], f"{name}: license gate"
        so = sorted(s["name"] for s in smeta["states"]); oo = sorted(s["name"] for s in ometa["states"])
        assert so == oo, f"{name}: state mismatch\n new {so}\n old {oo}"
        for s in smeta["states"]:
            new = Image.open(f"{sdir}/{s['name']}.png")
            old = Image.open(io.BytesIO(show(f"{rel}/{s['name']}.png")))
            o = next(x for x in ometa["states"] if x["name"]==s["name"])
            assert s.get("directions",1)==o.get("directions",1), f"{name}/{s['name']}: dirs"
            nf = len(s["delays"][0]) if "delays" in s else 1
            of = len(o["delays"][0]) if o.get("delays") else 1
            assert nf==of, f"{name}/{s['name']}: frames {nf} vs {of}"
            assert new.size==old.size, f"{name}/{s['name']}: {new.size} vs {old.size}"
        # the wave-4 lesson: ratio gate, asserted at integration not just curation
        icon = "icon"
        r = opq(Image.open(f"{sdir}/{icon}.png")) / opq(Image.open(io.BytesIO(show(f"{rel}/{icon}.png"))))
        assert r >= 0.55, f"{name}: opaque-ratio gate failed ({r:.2f})"
        if not dry:
            shutil.rmtree(ddir); shutil.copytree(sdir, ddir)
        print(f"{name}: {'DRY-OK' if dry else 'INTEGRATED'} ({len(smeta['states'])} states, ratio {r:.2f}, {ometa['license']} -> CC0-1.0)")
main()
