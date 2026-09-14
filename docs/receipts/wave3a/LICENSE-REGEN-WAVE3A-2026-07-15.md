# Receipt — CC-BY-NC REGEN wave 3a: the inhand-only bucket (feat/orch-regen-wave3a)

**Date:** 2026-07-15 · **Branch:** `feat/orch-regen-wave3a` (worktree `~/AI/solreign-trees/orch-regen-wave3a`, off GAME master `61e46543bd`, which already carries waves 1–2). NOT merged, NOT pushed — local commits only, per MERGE LAW (only Claude merges) and the task's scope discipline.

## What this is

Wave 3a of the COMPLEX bucket per `PROBE-IMG2IMG-COMPLEX-2026-07-15.md`'s recommended sequencing: the **inhand-heavy, no-`equipped-`** class (recon target: 33 assets from the precomputed `nc-classes.json[inhand_only]` list). Method, exactly as prescribed:

1. **Reference-first**: every generated state's PNG + `meta.json` was viewed (contact sheets `refs-tierAB.png`, `refs-extras.png`) before any prompt was written. No prompt was ever derived from an asset's file name alone.
2. **MiniMax image-01, fanned out in parallel** (`mmx_worker.py`, reused verbatim from wave 2 with only the output dir changed) for every base `icon`/other non-inhand state. 3 variants per state, 4 parallel workers.
3. **`inhand-*`/`wielded-inhand-*` states derived procedurally**, never AI-generated, via `rig_wave3a.py` (extends the probe's `inhand_rig.py` — same measured 1004-sprite hand-anchor table) — one rotation param + one grow-scale param per item class, plus a small idle-wobble (brightness pulse + 1px jitter) for states whose original declares >1 frame per direction.
4. **`.rsi` assembly** (`assemble_wave3a.py`) clones each original's exact state list, `directions`, and `delays` into the new `meta.json`; every replacement PNG's pixel dimensions are read directly from the ORIGINAL file and reproduced exactly (see "packing note" below).
5. **NC-in/CC0-out asserted** (`integrate_wave3a.py`) before any file was overwritten; only the 25 assets below were ever touched under `Resources/Textures/`.

## Packing note (a real finding, not assumed)

`RsiLoading.cs` (RobustToolbox, L216–241) repacks on-disk RSI states into an internal GPU atlas at `dimensionX = ceil(sqrt(totalFrameCount))` — **but that formula is for the runtime atlas, not the on-disk source file**. The loader reads the *source* image's own width to find its column count (`srcWidth`), so the on-disk grid can be any rectangle whose area is a multiple of the tile size (confirmed empirically: `mjollnir.rsi/icon.png` is a 3×3 grid holding 8 frames, one slot padding). Wave 1/2's integration gate asserted `tiles == frames*directions` exactly, which is too strict for assets that need padding (several of ours do — `chainsaw`, `mjollnir`, `singularityhammer`, `basketball`). Fixed the gate to assert **tile capacity** (`tiles >= frames*directions`) plus **exact byte-dimension parity with the original** (`new.size == old.size`, unconditionally satisfied here since every replacement canvas is sized directly from the original file it replaces) — the true parity contract.

## Assets shipped (25 of 33, all clean inhand-class fits)

| Asset | States | Rig rotate/grow | Notes |
|---|---:|---|---|
| `Objects/Power/powersink.rsi` | 3 | 0° / 1.9 | console + antenna spikes |
| `Objects/Tools/handdrill.rsi` | 3 | 0° / 1.9 | |
| `Objects/Tools/handdrilldiamond.rsi` | 3 | 0° / 1.9 | separate generation (cyan bit) |
| `Objects/Fun/Balls/football.rsi` | 3 | 0° / 1.6 | |
| `Objects/Fun/Balls/basketball.rsi` | 3 | 0° / 1.6 | inhand is a 5-frame/dir bounce anim — pulse-derived |
| `Objects/Fun/Balls/beach_ball.rsi` | 3 | 0° / 1.6 | 1st gen (magenta key) collided with the ball's own red panel under border flood-fill; regenerated with a green key — see lesson below |
| `Objects/Fun/Balloons/nanotrasen.rsi` | 3 | 0° / 1.9 | |
| `Objects/Fun/Balloons/corgi.rsi` | 3 | 0° / 1.9 | |
| `Objects/Fun/Balloons/syndicate.rsi` | 3 | 0° / 1.9 | |
| `Objects/Fun/rubber_chicken.rsi` | 3 | 0° / 1.9 | |
| `Objects/Fun/pondering_orb.rsi` | 3 | 0° / 1.6 | icon is a 4-frame glow-pulse anim |
| `Objects/Fun/clownrecorder.rsi` | 3 | 0° / 1.9 | |
| `Objects/Fun/Plushies/lamp.rsi` | 3 | 0° / 1.9 | |
| `Objects/Fun/capgun.rsi` | 7 | 0° / 1.9 | `base`=duplicate of `icon` (verified pixel-identical in the NC original too); `bolt-open`/`bolt-closed`/`capbullet` procedural (see below) |
| `Objects/Fun/Foam/foam_crossbow.rsi` | 4 | 0° / 1.9 | `foambox` separately generated |
| `Objects/Fun/Foam/foam_grenade.rsi` | 4 | 0° / 1.9 | `primed` = our own icon + a hand-drawn spark composite, no extra AI call |
| `Objects/Fun/Foam/foam_blade.rsi` | 3 | 45° / 1.9 | the probe's own worked example |
| `Objects/Weapons/Guns/Basic/energy_crossbow.rsi` | 3 | 0° / 1.9 | |
| `Objects/Weapons/Melee/machete.rsi` | 4 | 45° / 1.9 | `storage` = our icon rotated to vertical (same silhouette upright, confirmed the NC original does exactly this) |
| `Objects/Weapons/Melee/cutlass.rsi` | 6 | 45° / 1.9 | `foam_icon`/`foam_storage` are pixel-identical to `icon`/`storage` **in the NC original** (checked before assuming) — duplicated, no extra generation |
| `Objects/Weapons/Melee/incomplete_bat.rsi` | 4 | 45° / 1.9 | `storage` = rotated icon, same pattern as machete |
| `Objects/Weapons/Melee/chainsaw.rsi` | 5 | 45° / 1.9 (2.6 wielded) | icon is a 4-frame anim; inhand/wielded are 2-frame/dir |
| `Objects/Weapons/Melee/cult_halberd.rsi` | 5 | 45° / 1.9 (2.6 wielded) | |
| `Objects/Weapons/Melee/mjollnir.rsi` | 5 | 45° / 1.9 (2.6 wielded) | icon is an 8-frame anim (packed 3×3, 1 pad slot — the packing-note asset) |
| `Objects/Weapons/Melee/singularityhammer.rsi` | 5 | 45° / 1.9 (2.6 wielded) | icon AND inhand/wielded are all 10-frame anims (heaviest asset; inhand canvas 192×224, 6×7 grid, 2 pad slots) |

**NC census: 80 → 55** (−25, exact match to assets integrated; `nc_census.py`, `utf-8-sig` method, same as every prior wave).

## Excluded (8 of 33) — deliberate, recorded

**5 shotgun `*_inhands_64x.rsi` assets** (`improvised_shotgun`, `sawn`, `pump`, `enforcer`, `db_shotgun`) — **not a clean inhand-class fit**. Each RSI holds *only* `inhand-left/right` + `wielded-inhand-left/right` states, no base `icon` at all. The method (generate an icon, then rig inhand states FROM it) has no non-inhand state to generate — the only "reference" available is the inhand poses themselves, and deriving an icon from an inhand pose to then re-rig back into inhand states is circular, not the prescribed pipeline. This is architecturally a different problem (reference-less inhand-only rigs against a *held sprite package* convention) that deserves its own probe, not a batch commit under this wave's method. Left NC, untouched.

**3 drink-container assets** (`glass_clear.rsi` 19 states, `lube-tube.rsi`/`glue-tube.rsi` 17 states each) — **not a clean inhand-class fit**. They combine two unproven-together problems: (a) the fill-level layering system (wave 1's bespoke bowl-interior-detection synthesis), AND (b) inhand+fill combinatorial states (`inhand-left-fill-1/2/3` etc.) — rig-placement UNDER liquid-fill compositing. Nobody has proven this combination; forcing it risks shipping visually broken liquid-in-hand sprites for zero gate-visible reason (the gates check structure, not correctness of a fill-in-hand composite). Left NC, untouched. Recommend a dedicated probe before any wave 3d attempt.

Per the task's scope discipline: **partial delivery of solid assets beats a forced full sweep.** All 25 shipped assets are genuine no-caveat inhand-class fits.

## Generation stats

- 26 unique AI-generation targets (25 primary icons + `foam_crossbow/foambox`), 3 variants requested each = 78 planned generations, plus 1 chroma-key-lesson re-shoot (`beach_ball`, 3 more).
- **81 successful MiniMax generations, 25 failed attempts** — all 25 failures were `rate limit exceeded (RPM)` from the shared MiniMax key under heavy concurrent load (a sibling `orch-regen-wave3b` lane plus at least one more session were generating in parallel against the same account the whole time this ran). Zero content-policy or generation-quality failures. Recovered via three serial, backed-off retry passes (`mmx_worker_slow.py`/`_slow2.py`, 12–30s between calls, up to 8 tries) — every one of the 26 targets eventually got at least one clean success; no asset was skipped for generation-infrastructure reasons.
- **Zero NC pixels were ever read into a generation** — every prompt was written from a human (Claude) description of the viewed reference art, per Finding 0's absolute rail. `meta.json`/geometry facts (state names, directions, delays, pixel size) were the only NC-adjacent data read, matching the same convention wave 1/2 already established.

## The `beach_ball` chroma-key lesson (worth carrying into wave 3b/3c)

First generation used a magenta background key. `beach_ball`'s own art has a red/salmon panel — close enough in hue to magenta that the raw render's soft drop-shadow (grey-pink, also close to the key) let the border-seeded flood-fill leak through anti-aliased edge pixels into the panel, washing it out in all 3 variants. Regenerated with a **green** key (the one hue absent from a beach ball's red/yellow/blue/white palette) — clean result, all three panel colors intact. **Rule going forward: chroma key choice must consider the raw render's likely cast-shadow color too, not just the item's own palette** — a warm-toned item is at risk from a warm-toned shadow even under a "safe" key.

## Verified (gates, all run in this worktree)

| Gate | Result |
|---|---|
| Reference-first (every generated state's NC original PNG + meta.json viewed before prompting) | Yes — `refs-tierAB.png`, `refs-extras.png` contact sheets, plus individual zooms for ambiguous cases (`singularityhammer`, `mjollnir`, `cutlass` storage/foam duplicates, `machete`/`incomplete_bat` storage rotation) |
| NC-in → CC0-out per asset (`integrate_wave3a.py`) | 25/25 |
| State-name exact parity vs. each NC original | 25/25 |
| Per-state pixel-geometry parity (byte-exact canvas size vs. original; tile capacity ≥ declared frames×directions) | 25/25 |
| C# hardcoded-path sweep (`csharp_sweep.sh`) — the killsign lesson, YAML grep isn't enough | Only generic hits (`basketball.rsi`+`"icon"` in `AdminVerbSystem.Smites.cs`; generic `"bolt-open"`/`"primed"` state-name lookups in `GunSystem.ChamberMagazine.cs`/`TimerTriggerVisualizerComponent.cs`) — no asset-specific path outside the state list already preserved |
| `dotnet run --project Content.YAMLLinter -c DebugOpt` | **"No errors found in 44046 ms."** |
| `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~GameMapsLoadableTest" -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | **Passed! Failed: 0, Passed: 246, Skipped: 0**, 2 m 17 s |
| NC census | **80 → 55** |

## Not done / out of scope (deliberate)

- 8 assets left NC (5 shotgun inhands, 3 drinks) — see "Excluded" above.
- 55 NC assets remain overall: the 8 above + wave 3b/3c's targets (`equipped-` states, explicitly out of scope for this wave) + whatever wave 3b (a concurrent sibling lane, `feat/orch-regen-wave3b`) is independently working.
- Additive only: no prototype YAML edits, no CVar changes. All 120 changed files are under `Resources/Textures/**` for the 25 assets.
- Local commits only, nothing pushed, nothing merged (MERGE LAW: only Claude merges, per repo convention — this branch awaits that gate).

## Artifacts

- Montage: `docs/receipts/wave3a/wave3a-integrated-montage.png`
- Reference contact sheets: `docs/receipts/wave3a/refs-tierAB.png`, `refs-extras.png`
- Curation grids: `docs/receipts/wave3a/wave3a-gen/_curation_*.png`
- Pipeline scripts (reusable for wave 3b/3c): `mmx_worker.py`, `process_wave3a.py`, `rig_wave3a.py`, `assemble_wave3a.py`, `integrate_wave3a.py`, `procedural.py`, `csharp_sweep.sh`, `nc_census.py`, `montage_wave3a.py`

## Needs John's eyeball

Spawn a few of these at the dev client and check them in-hand: `chainsaw`/`mjollnir`/`singularityhammer` (the animated multi-frame rigs — the pulse-derived idle wobble is a judgment call, not measured), `beach_ball` (confirm the green-key regen reads right), and `cutlass`'s `storage`/`foam_icon` duplication (confirmed pixel-identical in the NC original, so this should be invisible, but worth a glance).

## Orchestrator curation pass (2026-07-15) — 3 assets REJECTED, reverted to NC originals

The wave's technical gates (state/geometry parity, linter, map-load) all passed for 25/25 — but
those gates cannot see whether the art is *good*. A visual curation pass against the originals
(the pilot verdict's mandatory human step) rejected three:

| Asset | orig→new opaque px | Why rejected |
|---|---|---|
| `Objects/Fun/capgun.rsi` | 185 → 97 (0.52) | Replacement is a faint wispy outline; the original is a legible revolver (black body, wood grip, blue muzzle). Does not read as a gun. |
| `Objects/Power/powersink.rsi` | 571 → 270 (0.47) | Replacement is a small dark box — a *different object*. The original is a distinctive machine: grey console + screen, red coils both sides, gold contacts, tripod legs. |
| `Objects/Fun/Plushies/lamp.rsi` | 184 → 109 (0.59) | Replacement is a dark muddy stick-lamp; the original is a bright green/yellow desk lamp. Clear legibility regression at 32x32. |

All three keep their CC-BY-NC originals — same call wave 1 made for `omnitool`/`snap_pops`. **A
broken sprite is worse than a licensing debt**; these return to the regen backlog.

**Revised wave 3a delivery: 22 assets. NC census 80 → 58** (not 55 as the pre-curation report
claimed).

**Screening heuristic worth reusing**: opaque-pixel ratio (new ÷ original) on the icon state.
Ratio < 0.55 flagged all three failures. Ratios *above* 1.0 were NOT failures — basketball (4.58)
and pondering_orb (4.05) are markedly *more* legible than the small dark originals. Thin =
suspect; heavy = usually an improvement.
