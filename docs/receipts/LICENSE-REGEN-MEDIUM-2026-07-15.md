# Receipt — CC-BY-NC REGEN wave 2: the MEDIUM bucket (feat/license-regen-integration)

**Date:** 2026-07-15 · **Branch:** `feat/license-regen-integration` (extends wave 1 commit `1a821c32fa`) · NOT merged, NOT pushed — orchestrator review + MERGE LAW gate.

**NC census: 90 → 80.** The entire MEDIUM bucket is now closed, plus snap_pops recovered from wave 1's exclusion list.

## What shipped (10 assets, 77 frames)

### Procedurally drawn — no AI generation, no NC pixels read (4 assets, 45 frames)
These are geometry/typography, which text-to-image handles badly and code handles exactly. Timings and grid shapes replicated from each original's `meta.json` only (`medium/build_procedural.py`):

| Asset | Approach | Frames × delays |
|---|---|---|
| `killsign` | 4×6 bitmap font drawn from scratch (10 words × 2-frame color flash + arrow bounce) + 2 static admin-verb icons | 10 states × 2 × [0.15, 0.15]; `icon`/`icon-hidden` static |
| `buffering` | 6-frame chase: 6 lit dots orbiting an 8-position ring, head-large→tail-small | 6 × [0.2] |
| `tinyfan` | 4-blade fan on a housing, 8× supersampled then BOX-downsampled for clean pixel edges, 22.5° rotation per frame | 4 × [0.01] |
| `eldritch_actions` / `voidblink` | figure dissolving into void: deterministic hash-noise mask at 100→55→25→6→6→25→55→100→100 % density | 9 × [0.1, 0.3, …] |

### AI-generated + curated (6 assets, 32 frames)
MiniMax image-01, **76-candidate pool across 4 parallel workers, 0 failures** (`medium/jobs_*.json`, `mmx_worker.py`). Ground truth viewed at 8× before any prompt was written (`medium/medium-refs.png` — the binding reference-first gate). Per-asset chroma keys per the batch-1 playbook (green for the grey possum, magenta for the rest, cyan for snap_pops). Curation grids: `medium/_curation_*.png`.

| Asset | States | Notes |
|---|---|---|
| `possum_old` | 4-dir + dead | dead = the classic X-eyes play-dead pose |
| `raccoon` | 4-dir + dead | bandit mask + ringed tail held across all 4 angles |
| `ferret` | 4-dir + dead | long low body reads at every angle |
| `scurret` | 4-dir + rip + oof | rip = translucent ghost; oof = flattened splat |
| `guardian_info` | 4-frame hologram + icon | fairy composited over its own pedestal, then given a **shimmer animation** (brightness+alpha cycle) — the original was animated and now still is |
| `snap_pops` | icon + box | **recovered from wave 1's exclusion**: upright twist pile + flat box, the two fixes batch-1's report prescribed |

**Technique note:** west is a horizontal mirror of east (standard SS14 practice) — this guarantees left/right consistency that independent generations cannot. All mobs are bottom-anchored and scaled to 82% of tile so they don't tower over vanilla pets.

## Verified

- **State-name parity** with every NC original (`git show master:`) — exact, all 10.
- **Geometry parity** — every replacement PNG matches its original's exact pixel dimensions and grid shape (2×2 for 4-dir mobs, 3×3 for voidblink, etc.), with declared `frames × directions` equal to actual tile count.
- **License gate** — asserted NC in → CC0-1.0 out for all 10.
- **killsign C# sweep** (the manifest's flagged risk — its RSI path is hardcoded, invisible to YAML grep): every state referenced from C# (`icon`, `icon-hidden` in `AdminVerbSystem.Smites.cs`; `kill` in `KillSignComponent.cs`) exists in the replacement. Full grep of `killsign.rsi"), "<state>"` across all Content.* returns exactly those three.
- **NC census** 90 → 80 (utf-8-sig method).
- **YAML linter**: exit 0, "No errors found in 47547 ms."
- **GameMapsLoadableTest** (mobs appear on maps): **Passed! Failed: 0, Passed: 246, Skipped: 0**, 1 m 52 s.

## Remaining license debt

**80 NC assets: 81 COMPLEX minus the ones now done** — the heavy tail (worn clothing with equipped-/inhand- states, directional/animated mobs, greyscale decals, ~2,394 frames). Plus `omnitool` (wave-1 FAIL: text prompting cannot pin one mechanical body across independent generations — needs img2img/reference-conditioning or a pixel artist) and `Jumpskirt/performer` (no upstream substitute).

**The COMPLEX bucket is a different kind of problem than everything closed so far** — it needs reference-conditioned generation or human pixel work, not more prompt effort. That's a scoping decision worth John's input before burning a batch on it.

## Needs John's eyeball

Spawn the four animals at the dev client (check scale against vanilla pets and that the mirrored west direction reads right), trigger an admin killsign smite, and look at a guardian info hologram. The tinyfan at 0.01s delays is effectively a blur — matching the original's intent.
