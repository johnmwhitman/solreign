# Receipt — CC-BY-NC REGEN integration wave 1 (feat/license-regen-integration)

**Date:** 2026-07-15 · **Branch:** `feat/license-regen-integration` (worktree `~/AI/solreign-trees/license-regen`, off master b74fe79d3f) · Separate lane from feat/sprite-idle-anims, independently mergeable. NOT merged, NOT pushed.

## What this does

Integrates the **staged SR-W-084 pilot + production-batch-1 license-replacement sprites** (from `~/AI/SUCCESSION/staged/ccbync-swap/`) into the game tree. These were generated 2026-07-12/15 (MiniMax image-01 + PIL post-processing), assembled as CC0-1.0 RSIs, and explicitly gated on "a human curation pass" — this lane is that curation + integration, performed by Claude (vision review of every contact/curation sheet) under John's 2026-07-15 "own it" grant. Merge remains gated on orchestrator review + John per MERGE LAW.

**NC census: 106 → 90** (utf-8-sig method, matches the license-hygiene lane's counting).

## Integrated (16 assets)

- **12 metamorphic drinks** (icon + icon_empty + fill-1..4/5): bronx, crushdepth, dark&stormy, electricshark, jackrose, junglebird, monkeybusiness, radler, tortuga (batch-1) + alienbrainhemorrhage, kalimotxo, vampiro (pilot batch-2).
- **4 items**: foam_dart, xeno_toxic, projectiles_magnum (2 bolt states), generic_memorial (32×64).

## The fill-state fix (why staged ≠ integrated bytes)

`SolutionContainerVisualsSystem.cs` renders metamorphic drinks as reagent `icon_empty` BASE layer + `fill-N` OVERLAY — the staged batches generated fill-N as *full glasses*, which would double-render (batch-1's own RUN-REPORT finding #1). Integration therefore kept the curated icon/icon_empty and **synthesized all 58 fill overlays procedurally** (`integrate_regen.py`): bowl-interior detection on OUR icon_empty (centroid-run walk, modal-span clamp so mug handles stay dry, stem cutoff so stemware liquid stays in the bowl), liquid color sampled from the bowl-centre of OUR icon (threshold fallback for glass-hued liquids like tortuga), surface highlight = lightened body color. **No NC pixels were read or reused anywhere.** Composited previews: `regen-integration-montage.png` (icon | empty | empty+fill-N per drink).

## Excluded (kept NC in tree, deliberate)

- **omnitool** — batch-1 verdict FAIL confirmed by my review (5 unrelated tool bodies). Recommend img2img/reference-conditioning or SWAP-UPSTREAM.
- **snap_pops** — icon reads as a lying capsule, box is 3D-isometric; needs the report's "targeted retouch round".

## Verified

- State-name parity with each NC original's meta.json (git show master:) — exact, all 16.
- Per-state PNG size parity with originals; all PNGs verify.
- All 16 meta.json NC-free (CC0-1.0, AI-generation copyright).
- NC census 106 → 90.
- YAML linter: exit 0, "No errors found in 34144 ms." (no YAML changed; prototypes reference the same paths/states).

## Remaining license debt (for the next waves)

- 88 REGEN assets: 10 MEDIUM (77 frames) + 81 COMPLEX (2,394 frames) minus these, plus excluded omnitool/snap_pops and Jumpskirt/performer (skipped earlier for no substitute). MEDIUM generation is the next natural batch (needs reference-first prompt authoring + a MiniMax run + this integration pipeline).

## Needs John's eyeball

In-game bar pass: pour a few of these drinks at the dev client (fill progression + metamorphic swap), check the magnum tracer in flight, and the memorial on a wall.
