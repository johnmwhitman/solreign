#!/usr/bin/env python3
"""Shared palette-letter scheme for the sprite-animation factory — WAVE 5.

Wave 3/4's `build_palette` assigned one LETTERS[i] per unique opaque color, ordered by
descending pixel count (`Counter.most_common()`). That crashes (IndexError) the moment a
sprite has more than len(LETTERS) == 62 distinct colors — wave 4's `lantern-on` (136 colors)
was DROPPED for exactly this reason (see docs/receipts/SPRITE-IDLE-ANIMS-WAVE4-2026-07-16.md
section 1, "Dropped").

WAVE 5 FIX: `build_letter_map()` below generalizes the scheme to GROUPS of colors instead of
a strict 1-color-per-letter mapping:

  - If a sprite has <= max_groups (62) unique opaque colors, behavior is BYTE-IDENTICAL to
    wave3/4's build_palette: each unique color gets its own letter, in the same
    Counter.most_common() descending-count order. This means replaying an existing wave3/4
    spec JSON against its (still 32x32, still single-frame) original PNG resolves every
    mask to the exact same pixel set as before — no regression, no version flag needed for
    the common case.

  - If a sprite has MORE than max_groups unique colors, colors are clustered into <=
    max_groups perceptual groups via deterministic median-cut quantization (recursively
    splitting the bucket with the widest channel range at the count-weighted median, same
    idiom classic color-quantization algorithms use — no RNG, no external deps beyond PIL).
    Each group gets ONE letter. The brief's rendered pixel map shows one symbol per group
    (letter = a representative real observed color + a total pixel count), so the DESIGNER
    only ever reasons about <= 62 regions — never exact colors, per the wave-5 brief.

    Critically, the EXECUTOR never operates on a group's representative color. Every op in
    execute_spec.py's apply_ops() reads/writes the pixel's OWN ground-truth RGB straight off
    the source PNG (see resolve_mask() below → apply_ops() in execute_spec.py) — letters are
    used ONLY to decide which (x,y) coordinates a mask covers. So two pixels sharing a letter
    but differing slightly in exact shade (e.g. two different greys quantized into the same
    perceptual bucket) each keep their own distinct color through every brightness/alpha/lerp
    op; nothing is ever flattened or quantized in the actual asset.

This module is imported by both dump_pixelmap.py (brief generation) and execute_spec.py
(spec execution) so the two always agree on letter->color(s) assignment for a given PNG.
"""
import collections
from PIL import Image

LETTERS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*+="
DIR_ORDER = ["S", "N", "E", "W"]


def collect_colors(im):
    """Counter of opaque (r,g,b) -> pixel count over the whole image (all tiles)."""
    cnt = collections.Counter()
    for y in range(im.height):
        for x in range(im.width):
            px = im.getpixel((x, y))
            if px[3] > 0:
                cnt[px[:3]] += 1
    return cnt


def _channel_range(colors):
    """Per-channel (min,max) across a list of RGB tuples."""
    rs = [c[0] for c in colors]; gs = [c[1] for c in colors]; bs = [c[2] for c in colors]
    return [(min(rs), max(rs)), (min(gs), max(gs)), (min(bs), max(bs))]


def _median_cut(items, max_groups):
    """items: list of (color, count). Returns list of buckets, each a list of colors.
    Deterministic: no RNG. Repeatedly splits the bucket with the widest single-channel
    range along that channel, at the count-weighted median, until max_groups buckets exist
    or no bucket has >=2 distinct colors left to split."""
    buckets = [[c for c, _ in items]]
    counts = dict(items)

    def bucket_widest_range(b):
        if len(b) < 2:
            return -1
        ranges = _channel_range(b)
        return max(hi - lo for lo, hi in ranges)

    while len(buckets) < max_groups:
        splittable = [b for b in buckets if len(b) >= 2]
        if not splittable:
            break
        target = max(splittable, key=bucket_widest_range)
        ranges = _channel_range(target)
        axis = max(range(3), key=lambda i: ranges[i][1] - ranges[i][0])
        srt = sorted(target, key=lambda c: (c[axis], c))
        total = sum(counts[c] for c in srt)
        acc = 0
        split_i = 1
        for idx, c in enumerate(srt):
            acc += counts[c]
            if acc >= total / 2:
                split_i = idx + 1
                break
        split_i = max(1, min(len(srt) - 1, split_i))
        left, right = srt[:split_i], srt[split_i:]
        buckets.remove(target)
        buckets.append(left)
        buckets.append(right)
    return buckets


def build_letter_map(im, max_groups=62):
    """Returns (letter_to_colors, color_to_letter, repr_color, cnt):
      letter_to_colors[l] -> list of exact original RGB tuples in that letter's group
      color_to_letter[c]  -> the letter for an exact original RGB tuple
      repr_color[l]       -> the highest-count individual color in the group (for display)
      cnt                 -> the raw Counter(color -> pixel count), for total-count display

    <= max_groups unique colors: identical letter-per-color assignment as wave3/4's
    build_palette (Counter.most_common() order) — singleton groups, zero behavior change.
    >  max_groups unique colors: median-cut clustered into <= max_groups groups, ordered by
    descending total group pixel count (most_common-equivalent ordering at the group level).
    """
    cnt = collect_colors(im)
    if len(cnt) <= max_groups:
        buckets = [[c] for c, _ in cnt.most_common()]
    else:
        buckets = _median_cut(list(cnt.items()), max_groups)
        buckets.sort(key=lambda b: -sum(cnt[c] for c in b))

    letter_to_colors, color_to_letter, repr_color = {}, {}, {}
    for i, b in enumerate(buckets):
        l = LETTERS[i]
        letter_to_colors[l] = list(b)
        repr_color[l] = max(b, key=lambda c: cnt[c])
        for c in b:
            color_to_letter[c] = l
    return letter_to_colors, color_to_letter, repr_color, cnt


def resolve_mask(tile, spec, letter_to_colors):
    """Same contract as wave3/4's resolve_mask, generalized: a letter now maps to a SET of
    exact colors (singleton set in the <=62-unique-color case, so this is a drop-in
    replacement — wave3/4 spec replay is unaffected)."""
    px = set()
    if "pixels" in spec:
        for x, y in spec["pixels"]:
            px.add((x, y))
        return px
    colors = set()
    for l in spec.get("letters", []):
        colors.update(letter_to_colors.get(l, []))
    x0, y0, x1, y1 = spec.get("region", [0, 0, 31, 31])
    for y in range(max(0, y0), min(32, y1 + 1)):
        for x in range(max(0, x0), min(32, x1 + 1)):
            p = tile.getpixel((x, y))
            if p[3] > 0 and p[:3] in colors:
                px.add((x, y))
    return px
