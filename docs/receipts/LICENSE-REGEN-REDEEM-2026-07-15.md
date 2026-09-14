# Receipt — redeem the art-rejected sprites + the dual-gate finding (feat/regen-final)

**Date:** 2026-07-15 · **Branch:** `feat/regen-final`, cut from **current master `055b89858a`** · NOT merged, NOT pushed.

**NC census: 49 → 47.** Two of the three sprites previously rejected for bad art are redeemed. The third is rejected a **third** time — deliberately.

## Why this branch exists

`3ca346616d` rejected `capgun`/`powersink`/`lamp` as "wispy or wrong-object art" and reverted them to NC — correctly. **A broken sprite is worse than a licensing debt.** This lane regenerates them against the *actual* originals and ships only what clears both gates.

## The dual gate (the load-bearing finding)

Wave 3a recorded a screening heuristic: **opaque-pixel ratio (new ÷ original) on the icon; < 0.55 = suspect.** It is genuinely useful — but it is **structurally blind to one of the two failure modes**, and that blindness is measurable:

| Failure mode | Example | Ratio gate | Semantic look |
|---|---|---|---|
| **Thin** — wispy, illegible | my `foam_blade` (0.44): a grey steel blade where the original is a red foam toy | **catches it** | catches it |
| **Wrong object** — right pixel mass, wrong thing | my `powersink` (0.71): a dark isometric box; the original is a coil-studded console | **MISSES it** | catches it |

A wrong-object failure keeps its pixel mass — a dark box has plenty of pixels, it's just the wrong box. So **ratio and semantics are orthogonal checks; neither subsumes the other and both must gate.** This is now asserted at integration time (`integrate_rej.py`), not merely at curation.

*Correction to `docs/receipts/wave3a/…`:* it states "Ratio < 0.55 flagged all three failures", but its own table lists `lamp` at **0.59**. The heuristic did not flag lamp — an eye did. Consistent with the above; the number can't do it alone.

## Shipped

| Asset | Ratio | Verdict |
|---|---|---|
| `lamp` | **0.79** | Bright green shade, gold arm and base — matches the original's colour identity and reads at 32×32. |
| `capgun` | **0.66** | Revolver silhouette with a visible cylinder, brown grip, blue muzzle tip. |

`capgun`'s sub-states are functional markers, derived from **our own** gun art, not copied: `base` == `icon` (as the original has it), `bolt-open`/`bolt-closed` are 3px/2px hammer marks placed from our gun's bbox, `capbullet` is a 1px alpha-3 placeholder (the original's is literally invisible). Inhands rigged as usual.

## REJECTED a third time: powersink

Three independent attempts across two lanes — the wave3a lane's, my earlier one (ratio 0.71), and this one's best of three (0.58) — **all produced the same failure: a 3/4 isometric box.** The original is a *flat, front-on, symmetric* console: three red coil towers, a dark screen, gold contacts, legs. Every prompt said "flat 2D orthographic"; the model ignored it every time for this object.

Three failures with an identical failure mode is not bad luck, it is a **capability boundary**: text-to-image defaults to isometric "game asset" renders and will not produce a dense symmetric technical faceplate.

**Recommendation — stop generating it.** `powersink` is flat, symmetric and technical: that is *geometry*, and geometry is the procedural lane's home turf (see `killsign`'s bitmap font, `cash`'s banknotes, `tinyfan`/`directionalfan`'s louvers). Draw it in code, or give it to a pixel artist. Kept NC until then.

## Verified

- State-name, direction, frame-count and pixel-geometry parity vs `master:` for both assets.
- License gate NC → CC0-1.0; **opaque-ratio gate asserted at integration** (0.79 / 0.66).
- NC census 49 → 47.
- YAML linter: exit 0, "No errors found in 31057 ms."
- GameMapsLoadableTest: **Passed! Failed: 0, Passed: 246, Skipped: 0**, 2 m 5 s.

## Process note — why this branch is cut from current master

The previous lane (`feat/license-regen-integration`) built three waves on a **stale master** and duplicated ~50 assets another lane had already merged. This repo runs parallel lanes and waves serialize: **re-fetch and re-census master before every wave and before integrating**, not once at worktree creation. That check ran first here, including a scan for dirty in-flight regen worktrees.

## Remaining: 47 NC

**31 `equipped-` worn states (519 frames)** — untouched frontier; probe before batching. Then: xeno/item artifacts (431f), greyscale decals (85f — system fully mapped: 15px tiles at 16px pitch, 7px minitiles at 8px pitch, ~20 base patterns + rotations; needs an 85-way visual diff because autotile errors are invisible to structural gates), glass_clear + lube/glue tubes (125f — reuses the wave-1 fill-overlay technique), toilets, guardians, shotgun inhands, powersink (procedural), `Jumpskirt/performer` (no substitute).
