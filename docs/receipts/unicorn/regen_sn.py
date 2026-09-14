#!/usr/bin/env python3
"""Targeted S/N re-frame for the Corporate Unicorn RSI.

Only the SOUTH (front) and NORTH (back) directional frames were wrong — head
closeups at a different zoom than the full-body E/W frames, so the mob appeared
to change size when it turned. This regenerates ONLY those two tiles from new
img2img concepts (front_raw.png / back_raw.png, gemini-2.5-flash-image edit
self-conditioned on our own full-body side source uni_e2.png) using the EXACT
same frame() pipeline as build_unicorn_rsi.py, then splices them into the
existing sheep_bare / sheep_wool sheets. E (tile 2), W (tile 3), sheep_dead,
sheep_wool_dead and icon are left byte-for-byte untouched.
"""
import os
from PIL import Image

# reuse the committed pipeline verbatim
import importlib.util
BUILD = os.path.expanduser(
    "~/AI/solreign-trees/unicorn-sprite/docs/receipts/unicorn/build_unicorn_rsi.py")
spec = importlib.util.spec_from_file_location("bu", BUILD)
bu = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bu)

REGEN = os.path.expanduser(
    "~/AI/solreign-trees/unicorn-sprite/assets/unicorn-work/_regen")
RSI = os.path.expanduser(
    "~/AI/solreign-trees/unicorn-sprite/Resources/Textures/_Solreign/unicorn.rsi")
TRANSP = (0, 0, 0, 0)


def splice_tile(sheet, tile_idx, frame32):
    """Replace one 32x32 tile (row-major 2x2) in a 64x64 sheet."""
    x = (tile_idx % 2) * 32
    y = (tile_idx // 2) * 32
    # clear then paste (paste with alpha would blend; we want a clean replace)
    sheet.paste(TRANSP + (0,) if False else Image.new("RGBA", (32, 32), TRANSP), (x, y))
    sheet.paste(frame32, (x, y))
    return sheet


if __name__ == "__main__":
    # new full-body S and N through the identical pipeline (key-out/crop/downscale/snap)
    S = bu.frame(f"{REGEN}/front_raw.png")
    N = bu.frame(f"{REGEN}/back_raw.png")

    # --- base layer: sheep_bare ---
    base = Image.open(f"{RSI}/sheep_bare.png").convert("RGBA")
    splice_tile(base, 0, S)   # S = top-left
    splice_tile(base, 1, N)   # N = top-right   (E=2, W=3 untouched)
    base.save(f"{RSI}/sheep_bare.png")

    # --- wool/mane layer: sheep_wool (mane pixels -> near-white for RgbLightController) ---
    wool = Image.open(f"{RSI}/sheep_wool.png").convert("RGBA")
    wS, nS = bu.mane_layer(S)
    wN, nN = bu.mane_layer(N)
    splice_tile(wool, 0, wS)
    splice_tile(wool, 1, wN)
    wool.save(f"{RSI}/sheep_wool.png")

    print(f"spliced S,N into sheep_bare + sheep_wool. mane px S={nS} N={nN}")
    print("E/W/dead/icon untouched.")
