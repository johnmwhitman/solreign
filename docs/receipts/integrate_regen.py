#!/usr/bin/env python3
"""Integrate curated CC-BY-NC replacement RSIs into the game tree (feat/license-regen-integration).

Drinks get procedurally synthesized fill-N liquid overlays (derived from OUR generated
icon_empty + icon — no NC pixels touched), fixing the staged batch's full-glass fills
which would double-render over the metamorphic base layer.

Usage: integrate_regen.py [--dry]   (dry = previews only, game tree untouched)
"""
import json, os, shutil, sys
from PIL import Image

REPO = os.path.expanduser("~/AI/solreign-trees/license-regen")
B1 = os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/production-batch-1/rsi-output")
PILOT = os.path.expanduser("~/AI/SUCCESSION/staged/ccbync-swap/pilot/rsi-output")
OUT = os.path.dirname(os.path.abspath(__file__))

# rsiName -> (staging dir, game-relative rsi dir, is_drink, n_fills)
ASSETS = {
    "bronx":                (B1, "Resources/Textures/Objects/Consumable/Drinks/bronx.rsi", True, 4),
    "crushdepth":           (B1, "Resources/Textures/Objects/Consumable/Drinks/crushdepth.rsi", True, 5),
    "dark&stormy":          (B1, "Resources/Textures/Objects/Consumable/Drinks/dark&stormy.rsi", True, 5),
    "electricshark":        (B1, "Resources/Textures/Objects/Consumable/Drinks/electricshark.rsi", True, 5),
    "jackrose":             (B1, "Resources/Textures/Objects/Consumable/Drinks/jackrose.rsi", True, 4),
    "junglebird":           (B1, "Resources/Textures/Objects/Consumable/Drinks/junglebird.rsi", True, 4),
    "monkeybusiness":       (B1, "Resources/Textures/Objects/Consumable/Drinks/monkeybusiness.rsi", True, 4),
    "radler":               (B1, "Resources/Textures/Objects/Consumable/Drinks/radler.rsi", True, 5),
    "tortuga":              (B1, "Resources/Textures/Objects/Consumable/Drinks/tortuga.rsi", True, 4),
    "alienbrainhemorrhage": (PILOT, "Resources/Textures/Objects/Consumable/Drinks/alienbrainhemorrhage.rsi", True, 5),
    "kalimotxo":            (PILOT, "Resources/Textures/Objects/Consumable/Drinks/kalimotxo.rsi", True, 5),
    "vampiro":              (PILOT, "Resources/Textures/Objects/Consumable/Drinks/vampiro.rsi", True, 4),
    "foam_dart":            (B1, "Resources/Textures/Objects/Fun/Foam/foam_dart.rsi", False, 0),
    "xeno_toxic":           (B1, "Resources/Textures/Objects/Weapons/Guns/Projectiles/xeno_toxic.rsi", False, 0),
    "projectiles_magnum":   (PILOT, "Resources/Textures/Objects/Weapons/Guns/Projectiles/projectiles_magnum.rsi", False, 0),
    "generic_memorial":     (PILOT, "Resources/Textures/Structures/Furniture/Memorials/generic_memorial.rsi", False, 0),
}

def clamp(v): return max(0, min(255, int(round(v))))

def bowl_geometry(empty):
    """Interior spans per row of the glass bowl, walking down from the rim until the
    bowl narrows into a stem (or 2 rows above the sprite bottom for tumblers)."""
    cols = [x for y in range(32) for x in range(32) if empty.getpixel((x, y))[3] > 0]
    cx = sum(cols) // max(1, len(cols))
    last_opaque = max(y for y in range(32) for x in range(32) if empty.getpixel((x, y))[3] > 0)
    spans, started = {}, False
    for y in range(32):
        row = [x for x in range(32) if empty.getpixel((x, y))[3] > 0]
        if not row:
            if started: break
            continue
        # run containing the centroid column (excludes detached handles/garnish)
        runs, cur = [], []
        for x in range(32):
            if empty.getpixel((x, y))[3] > 0: cur.append(x)
            elif cur: runs.append(cur); cur = []
        if cur: runs.append(cur)
        run = min(runs, key=lambda r: abs((r[0]+r[-1])//2 - cx))
        w = len(run)
        if w >= 6 and run[0] <= cx <= run[-1]:
            started = True
            spans[y] = (run[0]+1, run[-1]-1)  # 1px wall
        elif started:
            break  # narrowed into a stem — stop, never reach the foot
    # clamp every span to the modal body span — handles/spouts bulge past it
    if spans:
        import statistics
        mx0 = statistics.median(s[0] for s in spans.values())
        mx1 = statistics.median(s[1] for s in spans.values())
        spans = {y: (max(x0, int(mx0) - 1), min(x1, int(mx1) + 1)) for y, (x0, x1) in spans.items()}
        spans = {y: s for y, s in spans.items() if s[1] >= s[0]}
    # trim rim (top 2 interior rows) and base (2 rows above the bottom of the sprite)
    ys = sorted(spans)
    ys = [y for y in ys[2:] if y <= last_opaque - 2]
    return {y: spans[y] for y in ys}

def liquid_colors(icon, empty):
    """Dominant non-glass color sampled from the bowl centre of the icon; highlight = a
    lightened version of the body colour (never a garnish colour)."""
    glass = {px[:3] for y in range(32) for x in range(32)
             if (px := empty.getpixel((x, y)))[3] > 0}
    spans = bowl_geometry(icon)
    ys = sorted(spans)
    import collections
    def collect(thresh):
        cnt = collections.Counter()
        for y in ys[len(ys)//3:]:  # lower two-thirds of the bowl = liquid territory
            x0, x1 = spans[y]
            cx0, cx1 = x0 + (x1-x0)//4, x1 - (x1-x0)//4
            for x in range(cx0, cx1 + 1):
                r, g, b, a = icon.getpixel((x, y))
                if a == 0: continue
                if all((r-gr)**2 + (g-gg)**2 + (b-gb)**2 > thresh for gr, gg, gb in glass):
                    cnt[(r, g, b)] += 1
        return cnt
    cnt = collect(2500) or collect(900) or collect(200)
    assert cnt, "no liquid color found"
    body = cnt.most_common(1)[0][0]
    hi = tuple(clamp(c + (255 - c) * 0.35) for c in body)
    return body, hi

def synth_fill(empty, body, hi, level, maxn):
    spans = bowl_geometry(empty)
    ys = sorted(spans)
    assert len(ys) >= maxn, f"bowl too shallow: {len(ys)} rows"
    frac = 0.18 + 0.72 * level / maxn
    take = max(1, round(frac * len(ys)))
    liquid_rows = ys[-take:]
    ov = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for i, y in enumerate(liquid_rows):
        x0, x1 = spans[y]
        col = hi if i == 0 else body
        a = 235 if i == 0 else 245
        for x in range(x0, x1 + 1):
            ov.putpixel((x, y), (*col, a))
    return ov

def main():
    dry = "--dry" in sys.argv
    previews = []
    for name, (src, rel, is_drink, nf) in sorted(ASSETS.items()):
        sdir = f"{src}/{name}.rsi"
        ddir = f"{REPO}/{rel}"
        smeta = json.load(open(f"{sdir}/meta.json"))
        ometa = json.load(open(f"{ddir}/meta.json", encoding="utf-8-sig"))
        assert sorted(s["name"] for s in smeta["states"]) == sorted(s["name"] for s in ometa["states"]), \
            f"{name}: state mismatch"
        assert "NC" in ometa["license"] and "NC" not in smeta["license"]
        files = {}
        for st in smeta["states"]:
            im = Image.open(f"{sdir}/{st['name']}.png").convert("RGBA")
            orig = Image.open(f"{ddir}/{st['name']}.png")
            assert im.size == orig.size, f"{name}/{st['name']}: {im.size} vs original {orig.size}"
            files[st["name"]] = im
        if is_drink:
            body, hi = liquid_colors(files["icon"], files["icon_empty"])
            for lvl in range(1, nf + 1):
                files[f"fill-{lvl}"] = synth_fill(files["icon_empty"], body, hi, lvl, nf)
        # preview row: icon | empty | empty+fill-N composites (or all states for non-drinks)
        cells = [files["icon" if "icon" in files else list(files)[0]]]
        if is_drink:
            cells.append(files["icon_empty"])
            for lvl in range(1, nf + 1):
                comp = files["icon_empty"].copy()
                comp.alpha_composite(files[f"fill-{lvl}"])
                cells.append(comp)
        else:
            cells = list(files.values())
        row = Image.new("RGBA", (len(cells)*36 + 4, 36), (28, 28, 32, 255))
        for i, c in enumerate(cells):
            row.paste(c, (4 + i*36, 2), c)
        previews.append((name, row))
        if not dry:
            shutil.rmtree(ddir)
            os.makedirs(ddir)
            for st, im in files.items():
                im.save(f"{ddir}/{st}.png")
            with open(f"{ddir}/meta.json", "w") as fp:
                json.dump(smeta, fp, indent=2); fp.write("\n")
        print(f"{name}: {'DRY' if dry else 'INTEGRATED'} ({len(files)} states"
              f"{', fills synthesized' if is_drink else ''})")
    W = max(r.width for _, r in previews)
    from PIL import ImageDraw
    sheet = Image.new("RGBA", (W + 170, len(previews)*40), (28, 28, 32, 255))
    dr = ImageDraw.Draw(sheet)
    for i, (n, r) in enumerate(previews):
        sheet.paste(r, (170, i*40))
        dr.text((6, i*40 + 14), n[:22], fill=(225, 225, 225, 255))
    big = sheet.resize((sheet.width*3, sheet.height*3), Image.NEAREST)
    big.save(f"{OUT}/regen-integration-montage.png")
    print("montage written")

main()
