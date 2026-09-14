# Receipt — Sprite-animation factory WAVE 5 (feat/sprite-idle-anims)

**Date:** 2026-07-16 · **Branch:** `feat/sprite-idle-anims` (extends 40ef3d05cd pilot + 5c43b785a3 wave 2 + 3f5e80282d wave 3 + 98bb5cf206 wave 4) · **Scope:** fix the wave-4 palette-letter cap, then animate 7 more static sprites (7 physical states), designed by **grk** (Grok). NOT merged, NOT pushed.

Before starting, the branch was updated to current `master` (2c12cad3cf, fast-forward merge, `git status` clean) per the mission's instruction — this wave's candidate survey (section 1) is therefore against the REAL current master content, not the 34-commits-behind snapshot wave 4 worked from.

## 0. Tooling fix — the palette-overflow wave

Wave 3/4's `build_palette` assigned one of 62 `LETTERS[i]` per unique opaque color, ordered by descending pixel count. Past 62 unique colors it raises `IndexError` — wave 4 dropped `lantern-on` (136 colors) for exactly this reason and left the tool as-is, flagging it as a possible wave-5 job (wave 4 receipt, "Needs John's eyeball").

New shared module `docs/receipts/wave5/palette_lib.py` (`build_letter_map()`) generalizes the scheme to **groups** of colors:
- **≤62 unique colors:** behavior is byte-identical to wave3/4's `build_palette` — each unique color still gets its own letter, in the same `Counter.most_common()` order. Verified by direct comparison against the legacy algorithm on two pre-wave-4 originals (`crystal_grey`, `telecrystal`, fetched via `git show 98bb5cf206^:...`): identical letter→color assignment, 0 mismatches.
- **>62 unique colors:** colors are clustered into ≤62 perceptual groups via deterministic median-cut quantization (recursively splitting the bucket with the widest single-channel range at the count-weighted median — no RNG, no external deps beyond PIL). Verified on `lantern-on` (136 colors): clusters into exactly 62 groups, every one of the 136 original colors is accounted for in exactly one group, every opaque pixel round-trips through `color_to_letter`.

Critically, grouping only affects **what the designer sees** (the brief's rendered pixel map, via wave-5's `dump_pixelmap.py`). The **executor** (`docs/receipts/wave5/execute_spec.py`) never reads or writes a group's representative color — `apply_ops()` (unchanged from wave3/4) always reads/writes each pixel's own ground-truth RGB straight off the source PNG; letters are used only to decide which `(x,y)` coordinates a mask covers. Two pixels sharing a grouped letter but differing slightly in exact shade each keep their own distinct color through every op — nothing in the actual asset is ever flattened or quantized.

**Spec-format versioning / replay compatibility:** wave-5's `execute_spec.py` is a strict superset of wave-3's and wave-4's path resolution and `per_direction` handling (see the module's own docstring), specifically so existing wave-3/wave-4 spec JSON files still replay correctly if pointed at it — resolution order is explicit `"path"` → `RSI_PATHS[rsi]` (carries forward every wave-4 name verbatim) → wave-3's bare `_Solreign/{rsi}.rsi` fallback. A spec may declare an informational top-level `"spec_version"` field; the executor's behavior is identical regardless, since the ≤62-color case is untouched and only the >62-color path is new. Confirmed via a real replay test: wave-4's `batch1.clean.json` (`crystal_grey`'s mask specs) resolved to identical pixel sets against the pre-wave-4 original when run through wave-5's `resolve_mask()`.

`docs/receipts/wave5/qc_masks.py` and `verify.py` are wave-5 variants of wave-4's, updated to use the grouped-palette scheme (`qc_masks.py`) and the executor's generalized path resolution (`verify.py`, so it works for both `RSI_PATHS`-style and future `"path"`-style specs).

**One bug caught and fixed during this wave, not shipped:** the first draft of `execute_spec.py` ended with a bare `main()` call at module scope (same as wave3/4's scripts). `qc_masks.py` and `verify.py` both `import execute_spec` to reuse its `resolve_png`/`RSI_PATHS` — importing it executed batch1 for real (not `--dry`) before the quality gate had run. Caught immediately via `git status` (4 RSI dirs modified pre-review), reverted with `git checkout --`, and fixed by adding `if __name__ == "__main__": main()` to wave-5's `execute_spec.py`. The pipeline was then re-run correctly: qc gate (dry, no writes) → `execute_spec.py --dry` (preview sheets only) → visual review → real execution → `verify.py`. Wave-3/4's own scripts have the same latent bare-`main()` pattern but were not touched (out of scope, spec-replay-only artifacts).

## 1. Candidate survey (against current master, post-merge)

Master gained MovementBob, Wingmates, lore-trail, Acid Storm weather, and First-Shift-Welcome content since wave 4's branch point. Checked each for new animatable assets:
- **Acid Storm's ambient mote** (`SolreignAcidMote`, `Entities/StationIdentity/spores.yml`) reuses vanilla `Effects/chempuff.rsi` — already a 5-frame animated puff. Skipped (already animated).
- **Wingmate beacon** (`wingmate_beacon.yml`) reuses `Structures/Wallmounts/noticeboard.rsi` — a plain board, no glow/energy/liquid/display feature. Skipped.
- **RC courier charging dock** (`SolreignRcCarCharger`, `terminus_props.yml`, new this wave) reuses vanilla `borg_charger.rsi`'s `borgcharger-u1` state, acid-green tinted — this DID yield a candidate (below).
- **Parallax/mood/lore additions** are backgrounds (wrong asset shape — large panoramas, not 32×32 RSI states) or text-only (Guidebook/FirstShift.yml, first_shift_assignments.yml have zero `sprite:`/`state:` references). No candidates.

Beyond that, did a fresh keyword-and-size scan of the vanilla asset tree (`glow|light|crystal|candle|energy|screen|...` in RSI paths, 32×32, single-direction, no existing `delays`) and manually triaged ~30 plausible hits. Consistent skip patterns applied (matching wave 4's own precedent):
- **Already has a dedicated animated sibling state** (same rule wave 4 used for anomaly cores): `bonfire`/`bonfire_extinguished` (→ `burning`/`legionnaire_bonfire` already animated), `arabianlamp`'s `lamp` (→ `flame` already animated), `candles.rsi`'s `candle-small/big` (→ `fire-small/big` already animated), `flare.rsi`'s `flare_base` (→ `flare_burn` already animated), `torch.rsi`'s unlit states (→ animated lit state exists), pumpkin `lantern` jack-o'-lantern (already 5-frame animated), `anomaly.rsi`'s `anom1-6` (→ `-pulse` siblings), `carp_rift.rsi` (already animated), `service_light.rsi`'s `glow` (already animated — a DIFFERENT fixture from this wave's `small_light_post.rsi`, which has no animated sibling at all).
- **Already code-driven multi-state visual, not an idle loop** (same rule wave 4 used for vending machines): `walldispenser.rsi`/`chemistry` jugs/vials' `fill-N` gauge states, `battery.rsi`'s `battery1-10`, `air_monitors.rsi`'s status-swap icons, `circuit_imprinter.rsi`'s `unlit` (a `PowerDeviceVisualLayers.Powered`-mapped layer shared across many lathe variants — same broad-reach concern as below).
- **Orphaned / zero in-game usage** (no payoff): `greenlight.rsi` (a whole unused flashlight reskin, zero prototype references), `phoron_gem`/`phoron_gem_spent` (unused material), `mmi_alive` (unused MMI state), `Shards/shard.rsi`'s inhand states.
- **Too broad-reach for a contained cosmetic wave**: the universal light system (`light_tube.rsi`, `light_small.rsi`, `strobe_light.rsi` — shared by essentially every light fixture on every map) was deliberately NOT touched, unlike the more contained `small_light_post.rsi` (a specific decorative ground-lamp prop, comparable reach to wave 4's own `crystal_grey`).
- **No legible feature, or would fabricate one**: robot-part clothing icons (`cyborg_parts.rsi`, `borgmodule.rsi`), plain statues (`statues.rsi`, `ironsand_statue*.rsi`, `showcase_1/3/4` — replica-robot statues, no inherent glow), material ingots/ore (mundane, no distinct glow color), `effects.rsi`'s `molten` (unused, and its actual pixels are flat grey, not a "molten" look).

## 2. Candidates — chosen and dropped

**Chosen (7 sprites / 7 states):**

| Sprite | RSI path | Feature | Note |
|---|---|---|---|
| lantern_on | `Objects/Tools/lantern.rsi` | lit holy lantern, amber glass glow | **wave-4's palette-dropped candidate** — 136 colors, only animatable via this wave's grouped-palette fix; fixed color, no runtime tint (verified: no `color:` override on either `Lantern`/`LanternFlash` entity's Sprite layer, only the separate `PointLight` has its own color) |
| light_post_glow | `Structures/Lighting/LightPosts/small_light_post.rsi` | ground lamp-post unshaded glow lens | fixed color, no runtime tint (verified across `LightPostSmall`/`PoweredLightPostSmall`/`PoweredLEDLightPostSmall` — `color:` only ever appears on `PointLight`, never on the Sprite) |
| warding_tower | `Structures/Specific/xeno_building.rsi` | xeno warding-tower glowing sigil overlay | fixed color, no runtime tint; single-use entity (`XenoWardingTower`, the only prototype referencing `xeno_building.rsi`) |
| borg_charger | `Structures/Power/borg_charger.rsi` | cyborg charging-dock base plate | **TINTED PER-INSTANCE, deliberately conservative**: vanilla `BorgCharger` shows this texture completely untinted; the new (this-wave, terminus_props.yml) Solreign `SolreignRcCarCharger` reuse tints it acid-green (`#9dfd39`). Treated exactly like a tinted-per-instance grayscale sprite even though one of its two uses has no tint — brightness/lerp-white ops only, so it reads correctly in BOTH contexts |
| glass_shard1/2/3 | `Objects/Materials/Shards/shard.rsi` | broken-glass shard ×3 variants | grayscale, TINTED PER-INSTANCE (verified via `shards.yml`: `ShardGlass`/other glass-type entities each apply a different `color:` override — `#bbeeff`, `#96cdef`, `#FF72E7`, `#8eff7a`, `#e0aa36` — to the same base texture); NOT the same file as wave 4's `Objects/Materials/Shards/crystal.rsi` (already animated) |

**Dropped** (see section 1 for the grouped skip reasons and full list); notable individual calls:
- `borgcharger-u1` was the only genuinely borderline pick — it's swapped for `borgcharger-u0` on a `StorageVisuals.Open` boolean (open/closed), which is the same class of "binary visibility toggle" wave 4 already accepted for `flashlight_overlay` (on/off), not the "many discrete semantically-different icons" pattern (vending machines, battery gauges) that wave 3/4 excluded. Kept, tint-safe-only.
- `circuit_imprinter.rsi`'s `unlit` was seriously considered (a lathe "screen glow" layer) but its `map: ["enum.PowerDeviceVisualLayers.Powered"]` wiring is reused, with different tints, across at least 3 different lathe variants (`circuit_imprinter`, `circuit_imprinter_hypercon`, others) — the same reach profile as the universal light system. Dropped.

## 3. Fleet design quality

**7/7 grk designs landed clean, zero redesigns needed.** Dispatched in 2 batches (4 + 3) to `~/AI/Tools/grk`, foreground, each brief fully self-contained with the new grouped-palette pixel maps (verified byte-for-byte and palette-line-for-line against the tool's own raw dump output before dispatch — a transcription slip caught and fixed pre-dispatch) plus the tint-safety rule carried forward from wave 4.

One mechanical defect, not a design fault: batch 2's raw Grok output had a stray extra `}` after the last op in 3 of the frame-3 blocks (a JSON-formatting glitch, not a content problem — every op itself was well-formed). Caught by `json.loads()` failing at a precise, reproducible character offset each time; fixed by three exact string replacements (verified the fix changed nothing except removing the single extra brace) rather than asking Grok to redesign anything.

`qc_masks.py`'s pre-execution gate found no suspiciously-small (≤1px, non-"pixels"-form) masks across all 7 sprites/24 masks. The only sub-2px masks are `warding_tower`'s 3 explicit `"pixels"`-form marker motes and the shards' explicit `"pixels"`-form spark accents — same idiom as wave 3/4's `spark` accents, correctly using the pixel-list form rather than claiming a whole-region mask for a 1-2px detail.

Tint-safety audit (grepped `Content.Server`/`Content.Shared`/`Content.Client` for `ChangeColor` touching these assets — none found beyond the known static `color:` prototype overrides already accounted for above): every op actually emitted for the 4 tinted-per-instance sprites (`borg_charger`, `glass_shard1/2/3`) is `brightness` or `lerp` toward `[255,255,255]` only — no `hue_shift`, no colored `lerp`. The 3 fixed-color sprites (`lantern_on`, `light_post_glow`, `warding_tower`) used `hue_shift` and/or off-white `lerp` targets where the concept called for it, which is fine since nothing tints them.

## 4. Verification (all programmatic — `docs/receipts/wave5/verify.py`)

- All 7 targets: PNG grid = declared frame count (32×N); frame 0 byte-identical to `git show HEAD:` original; every consecutive frame pair differs (no dead frames); license/copyright preserved verbatim; meta `delays` match the executed spec exactly.
- Sibling states in shared RSIs verified byte-for-byte/meta-for-meta unchanged: `lantern.rsi`'s other 9 states, `small_light_post.rsi`'s 4 others, `xeno_building.rsi`'s `recoverytower`/`frenzytower`/`wardingtower`, `borg_charger.rsi`'s 8 others (including the vanilla-only `borgcharger0/1/2/3`, `borgdecon1/2/3`, `borgcharger-u0`), `shard.rsi`'s `inhand-left/right`.
- `git status` confirms exactly the 4 target RSI directories (7 PNGs) + 4 meta.json files touched, nothing else in the tree — this includes recovering cleanly from the mid-wave accidental-execution bug (section 0), re-verified after the fix.
- No prototype YAML was touched this wave (pure RSI asset work) — the YAML linter gate does not apply; skipped per the mission's own conditional.
- Review montage: `docs/receipts/wave5/wave5-review-montage.png` (one row per sprite, first column = frame 0 = the original, dark background) — visually confirmed the lantern's amber breathe, the warding-tower's sequential mote flicker, and the borg-charger's subtle vent-strip pulse all read correctly at native scale.

## 5. Reproducibility

`docs/receipts/wave5/`: `palette_lib.py` (the tooling fix — shared grouped-palette module, see section 0), `dump_pixelmap.py` (wave-5 pixel-map generator using the grouped scheme), `execute_spec.py` (wave-5 executor — superset path resolution for wave-3/4/5 spec replay, `if __name__=="__main__"` guarded), `qc_masks.py`/`verify.py` (wave-5 QC/ground-truth, grouped-palette aware), `brief1-grk.txt`/`brief2-grk.txt` (the exact dispatched briefs, pixel-map-verified against the tool's own output before dispatch), `grk_batch1_raw.txt`/`grk_batch2_raw.txt` (raw fleet output, including batch 2's pre-fix stray-brace JSON), `batch1.clean.json`/`batch2.clean.json` (the 7 fleet-designed specs, post-brace-fix), `build_montage.py`, `wave5-review-montage.png`, and the 7 `*-after-sheet.png` preview strips.

Website/GIF output stays disabled (wave-4 decision, carried forward: that would write into the sibling `website-v2` worktree, out of scope for this branch).

## Needs John's eyeball

The `borg_charger` treatment — same texture untinted in vanilla `BorgCharger` and acid-green in the new Solreign RC courier dock; the tint-safe-only ops should look correct in both, but it's the first wave-5 candidate where "TINTED PER-INSTANCE" means "sometimes not tinted at all" rather than "always tinted, color varies," worth a second look. Whether the wave-3/4 scripts' own bare-`main()`-on-import hazard (section 0) is worth backporting a guard to, given they're meant to stay frozen as spec-replay artifacts. Whether `circuit_imprinter.rsi`'s `unlit` layer (dropped this wave as too broad-reach, section 2) is worth a dedicated future wave once/if the universal-light-system question gets resolved.
