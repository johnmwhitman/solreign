#!/usr/bin/env python3
"""NC census -- parse every Resources/Textures/**/meta.json with utf-8-sig (some carry a
BOM) and count license containing 'NC'. Same method used by every prior REGEN/hygiene
receipt in this repo."""
import glob, json, os, sys

REPO = os.path.expanduser("~/AI/solreign-trees/orch-regen-wave3a")

def census():
    nc = []
    total = 0
    for p in glob.glob(f"{REPO}/Resources/Textures/**/meta.json", recursive=True):
        total += 1
        try:
            d = json.load(open(p, encoding="utf-8-sig"))
        except Exception as e:
            print(f"PARSE FAIL {p}: {e}", file=sys.stderr)
            continue
        lic = d.get("license", "")
        if "NC" in lic:
            nc.append((p.replace(f"{REPO}/Resources/Textures/", ""), lic))
    return nc, total

if __name__ == "__main__":
    nc, total = census()
    print(f"Total meta.json: {total}")
    print(f"NC-licensed: {len(nc)}")
    if "--list" in sys.argv:
        for p, lic in sorted(nc):
            print(f"  {p}  [{lic}]")
