# Probe — is img2img the answer for the CC-BY-NC COMPLEX bucket? (feasibility, 2026-07-15)

**Branch:** `feat/license-regen-integration` · **Status:** PROBE ONLY — no game asset changed by this work. Artifacts in `probe-img2img/`.

**Question:** the COMPLEX bucket (80 remaining NC assets, ~2,394 frames) was blocked because text prompting cannot hold one object's identity across many states (wave-1's `omnitool` FAIL). Does reference-conditioned img2img unblock it?

**Verdict: YES for identity — but identity turned out not to be the real blocker.** The probe found the actual one, and it is cheaper to solve than either of us assumed.

---

## Finding 0 (BLOCKING RAIL) — never condition on the NC original

The obvious img2img move — feed the NC sprite in, ask for a redraw — **is off the table and was never run.** An img2img output conditioned on CC-BY-NC-**SA** pixels is a mechanical transformation of that work: a derivative, and the SA term explicitly reaches adaptations. That would launder the NC art into the tree rather than replace it, defeating the entire regen effort.

The line held throughout waves 1–2 and here:
- ✅ **View** NC art → describe it in words → generate fresh. (Subject matter — "a raccoon" — isn't copyrightable; only the specific expression is. This is what a human artist does with references.)
- ✅ Read NC `meta.json` for **functional interface facts** (state names, delays, geometry).
- ✅ img2img conditioned on **our own CC0 art**.
- ❌ img2img conditioned on **NC pixels**. Never.

Everything below uses only our own CC0 generations as conditioning input.

## Finding 1 — img2img self-conditioning decisively solves shared identity

`omnitool` is the purest test: 5 states = 5 heads on one body; wave 1 marked it FAIL. `probe-img2img/omnitool-raws.png` shows why — 8 independent generations produced 8 unrelated tools (a crowbar, a ray gun, a pistol, a hammer).

Method: pick ONE of our own CC0 generations as the anchor (`omnitool-pulsing_v1_raw`), then `gemini_image.py edit <our-anchor> "change only the front tip to X" --vertex`.

**Result (`probe-img2img/VERDICT-omnitool-family.png`): the body held perfectly across all 5 states** — same grip, same orange stripe, same gold barrel, same green gem — with only the tip changing (screwdriver / pry wedge / cutter jaws / wrench jaw). This is the exact result independent prompting could not buy at any budget.

**Tooling note:** `gemini-3.1-flash-image` and `gemini-3-pro-image` 404 on the Vertex project (`gen-lang-client-0982267563`) — no access. **`gemini-2.5-flash-image` is the working img2img path** and is what `--model` must be set to.

## Finding 2 — the real blocker is RIG PLACEMENT, not identity (and it isn't an AI problem at all)

Inspecting what COMPLEX actually contains: **66 of 81 assets have `inhand-` states; 33 have `equipped-`.** Looking at real inhand art (`probe-img2img/ref-foam_blade.png`, `ref-corgi.png`) reframes the problem:

**Inhand states are not pictures of an object.** They are the item drawn tiny, at a specific pixel offset, aligned to the character's hand, per direction — with one direction often mostly occluded by the body. It's a rig convention. No image model knows SS14's hand anchors, so *both* text-to-image and img2img fail here — not on identity, but on placement.

**Measured the convention across 1,004 freely-licensed (non-NC) 4-dir inhand sprites** — the anchor is near-constant:

| state | dir | median item bbox | | state | dir | median item bbox |
|---|---|---|---|---|---|---|
| inhand-left | S | (19,16)–(25,24) | | inhand-right | S | (6,16)–(13,24) |
| inhand-left | N | (6,17)–(11,24) | | inhand-right | N | (20,17)–(25,24) |
| inhand-left | E | (19,16)–(25,23) | | inhand-right | E | (12,16)–(20,24) |
| inhand-left | W | (12,16)–(19,24) | | inhand-right | W | (7,16)–(13,23) |

Item height is pinned at y≈16–24 (hand height) in every case; x is a pure function of hand + facing. **So inhand states are derivable by code from a CC0 icon.**

**Demonstrated end-to-end** (`probe-img2img/VERDICT-inhand-rig.png`, `inhand_rig.py`): generated one CC0 foam-blade icon → scale + rotate + stamp at each measured anchor → **8 convention-correct inhand frames from 1 generation + 1 rotation parameter.** (The rotation is a per-item-class value — a blade stands upright, a ball doesn't care. My first attempt used the wrong sign and laid the sword flat; it's a one-number fix, visible in the file's history.)

## Revised cost model for COMPLEX

The old estimate — "~2,394 frames need an artist" — was wrong because it counted every frame as a drawing job. Actually:

| Work | Old view | Probe finding |
|---|---|---|
| ~528 `inhand-` frames (66 assets × 8) | generate each | **Procedural rig** — 1 icon + 1 param per asset |
| Variant states of one object (omnitool-class) | FAIL | **img2img self-conditioning** |
| Base `icon` states | generate each | unchanged — text-to-image, already proven |
| **~33 `equipped-` worn states** | generate each | **STILL OPEN — the genuine frontier** |

## What I did NOT solve

`equipped-` states (33 assets) — a garment draped on the character silhouette, per direction, sometimes per species. That is neither an identity problem nor a simple stamp: the art must conform to a body. **Untested.** It is the one part of COMPLEX that may genuinely want a pixel artist or a template/mask approach against the human base sprites. Worth its own probe before committing a batch.

## Recommendation

COMPLEX is now tractable enough to attempt in waves rather than declare artist-only:
1. **Wave 3a — inhand-heavy, no equipped-** (the `corgi`/`beach_ball`/`football`/`foam_blade` class, ~9 frames each): icon generation + the rig. Cheapest real progress; this is where the 66 assets live.
2. **Wave 3b — omnitool + multi-variant items**: img2img self-conditioning. Recovers wave 1's only FAIL.
3. **Wave 3c — `equipped-`**: probe first, don't batch.

Nothing here is merged or applied to any asset; this branch's game files are untouched by the probe.
