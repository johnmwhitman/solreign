#!/usr/bin/env python3
"""Montage of every assembled WAVE 3A RSI (wave3a-proc/) -- one row per asset, one cell
per tile (all directions/frames flattened), for the final receipt."""
import json, os
from PIL import Image, ImageDraw

SCR = os.path.dirname(os.path.abspath(__file__))
PROC = f"{SCR}/wave3a-proc"

rows = []
for rsi in sorted(os.listdir(PROC)):
    if not rsi.endswith(".rsi"):
        continue
    m = json.load(open(f"{PROC}/{rsi}/meta.json"))
    cells = []
    for s in m["states"]:
        im = Image.open(f"{PROC}/{rsi}/{s['name']}.png").convert("RGBA")
        cols = im.width // 32
        rows_n = im.height // 32
        for i in range(cols * rows_n):
            cell = im.crop(((i % cols) * 32, (i // cols) * 32, (i % cols) * 32 + 32, (i // cols) * 32 + 32))
            if cell.getbbox():
                cells.append(cell)
    if not cells:
        continue
    row = Image.new("RGBA", (len(cells) * 36 + 4, 40), (28, 28, 32, 255))
    for i, c in enumerate(cells):
        row.paste(c, (4 + i * 36, 4), c)
    rows.append((rsi[:-4], row))

W = max(r.width for _, r in rows) + 160
sheet = Image.new("RGBA", (W, len(rows) * 44 + 4), (28, 28, 32, 255))
dr = ImageDraw.Draw(sheet)
for i, (n, r) in enumerate(rows):
    sheet.paste(r, (160, i * 44))
    dr.text((4, i * 44 + 16), n[:22], fill=(225, 225, 225, 255))
sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST).save(f"{SCR}/wave3a-integrated-montage.png")
print("montage written:", sheet.size)
