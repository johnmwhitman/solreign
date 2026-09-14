# Receipt — CC-BY-NC REGEN integration: SR-W-084 batches 2-9 (feat/regen-integrate)

**Date:** 2026-07-16 · **Branch:** `feat/regen-integrate`, off GAME master `c8e5a492d6` · worktree `~/AI/solreign-trees/regen-integrate` · **NOT merged, NOT pushed** — HELD at the merge gate pending the owner's curation verdict. All commits local.

**NC census: 49 → 4.** 45 assets closed, 468 states.

## Scope

Machinery: `~/AI/SUCCESSION/staged/ccbync-swap/integration-prep/{integrate_all.py, asset_index.py, grades.py}`, built by a prior lane. Its original dry-run (15:00, `dry-run-output/PLAN-REPORT.{md,json}`) found:

- **production-batch-1 (13 assets) — STALE, EXCLUDED.** Every destination already carries `CC0-1.0` on live master (integrated by 3 earlier independent waves — `LICENSE-REGEN-INTEGRATION-2026-07-15.md`, `LICENSE-REGEN-WAVE3A/3B-2026-07-15.md`). Re-applying batch-1's staged `CC-BY-NC`-era output would **regress** live art back over already-shipped `CC0` art. Not touched.
- **batches 2-9 (45 assets, 468 states) — validated CLEAN against master.**

## Step 1 — re-validation against CURRENT master (required: master moved a lot today)

Master had advanced 47 commits since the 15:00 dry-run — up to `c8e5a492d6` (16:49, "Merge fix/feedback-bugs"), including FX Language W1-W3, player-feedback bug/balance waves, HTN race fix, screen-flash fix, audio-w1/w3 relicensing, mood-rollout, parallax, delight-eggs, lore-trail, ux-simple, wingmate wiring — none of which touch batches 2-9's destination paths.

Re-ran `integrate_all.py --dry-run` against this current HEAD with a **fresh custom verdict** (batch-1 rejected/excluded; batches 2-9 approved). This surfaced one thing the original 15:00 report couldn't have seen: a brand-new `production-batch-10-worn` directory had appeared under the shared `STAGED_ROOT` (19 assets: crowbar, eggs, gs_armor/jumpsuit, hoverpack, trench, etc.) — a **different, concurrent lane's** staged work (own `manifest.py`/`gates.py`/`build.py`, zero overlap with the shared `manifest.json` used by this machinery — 0/19 matched, all `NO_MANIFEST_MATCH`). This batch is **not** part of this run's batches-2-9 scope and was explicitly excluded (rejected) in the verdict rather than left to a batch-key default, so it could never silently fall through to "approve."

**Re-validation result: 0 assets dropped.** All 45 batch-2-9 assets validated clean a second time against the moved master — same plan as the original report (45 planned / 468 states, 0 blocked, live NC census unchanged at 49 going in). No state-name/geometry drift, no asset regressed by another lane's newer art. Full report: `REVALIDATION-PLAN-REPORT.md` / `.json` (this directory).

## Step 2 — verdict file

`verdict-batches-2-9.yaml` (this directory): batch-1 and the incidental batch-10-worn both explicitly `reject` (excluded, out of scope); all 45 batch-2-9 assets `approve` (none graded FAIL). 4 are graded **BORDERLINE** and shipped per standing instruction — flagged below for the owner's merge-time review.

## Step 3 — apply

`integrate_all.py --apply verdict-batches-2-9.yaml --i-have-curation-approval --non-dry-run --worktree ~/AI/solreign-trees/regen-integrate` (flag pair authorized for this branch build only, per orchestrator instruction — not a merge/curation approval).

Composed final RSIs into the worktree: state PNGs + `meta.json` license swap `CC-BY-NC-*` → `CC-BY-SA-3.0` for all 45 assets. Batch-1's fill-retrofit patch **not applied** (`apply_batch1_fill_retrofit: false` — batch-1 excluded entirely, its retrofit is moot). Diff: 513 files touched (468 state PNGs + 45 `meta.json`), zero touches to batch-1 or batch-10-worn destinations (verified by grep against the diff).

### Assets applied (45, by batch)

| Batch | Assets | States | Grade |
|---|---|---|---|
| production-batch-2-artifacts | item_artifacts | 44 | BORDERLINE |
| production-batch-3-xeno-guardians | guardians | 12 | BORDERLINE |
| production-batch-3-xeno-guardians | xeno_artifacts | 73 | BORDERLINE |
| production-batch-4-toilets | golden_toilet | 12 | BORDERLINE |
| production-batch-4-toilets | toilet | 10 | PASS |
| production-batch-5-icononly | greyscale | 85 | PASS |
| production-batch-6-r5 | AI, arachnid, atmosian, bee, hampter, human, moth, narsie, nukie, penguin, ratvar, rouny, slime, toy_ian, toy_mouse, toy_nuke, vulp, xeno (18 plushies/toys) | 4 each (72 total) | PASS (all) |
| production-batch-7-r6 | db_shotgun_inhands_64x, enforcer_inhands_64x, glass_clear, glue-tube, improvised_shotgun_inhands_64x, lube-tube, pump_inhands_64x, sawn_inhands_64x | 4,4,19,17,4,17,4,2 (71 total) | PASS (all) |
| production-batch-8-r7 | baseball_bat, db_shotgun, energy_magnum, fireaxe, fireaxeflaming, pump, sawn | 7,4,10,7,7,4,4 (43 total) | PASS (all) |
| production-batch-9-r8 | diona, lizard, performer, snake, tennisball, vox | 5,16,5,5,10,5 (46 total) | PASS (all) |

Total: 45 assets, 468 states. (44+12+73+12+10+85+72+71+43+46 = 468.)

### Dropped on revalidation

**None.** All 45 batch-2-9 assets from the original 15:00 dry-run still validated clean against the moved master; nothing had to be dropped this run. (Batch-1's 13 assets remain excluded per original scope — that exclusion predates this run and isn't a "drop", it's the standing rule.)

### BORDERLINE — flagged for owner, may want a re-roll at merge-time review

- **item_artifacts** (production-batch-2-artifacts): 11 families — 5 PASS / 6 BORDERLINE (fine surface texture lost to MiniMax smoothing); 0 FAIL.
- **guardians** (production-batch-3-xeno-guardians): 4 creatures post-re-roll — 3 PASS / 1 BORDERLINE (miner palette fidelity only partial); 0 FAIL.
- **xeno_artifacts** (production-batch-3-xeno-guardians): 36 families — 20 PASS / 16 BORDERLINE (surface/pose detail smoothing); 0 FAIL.
- **golden_toilet** (production-batch-4-toilets): 21 PASS / 1 BORDERLINE (N-direction seat-up hook legibility nit).

All four shipped (approved) per the owner's standing instruction that the curation gate lives at merge, not here — but each is a candidate for a re-roll pass before the branch is actually merged.

## Step 4 — verify

- **Build:** `dotnet build Content.IntegrationTests -c Release` → **Build succeeded, 0 Error(s)** (warnings only, pre-existing obsolete-API notices unrelated to this change).
- **YAML linter:** `dotnet run --project Content.YAMLLinter -c Release` → **"No errors found in 33135 ms."** (exit 0)
- **Solreign unit battery:** `dotnet test Content.Tests --filter "FullyQualifiedName~_Solreign" --no-build` → **Passed! Failed: 0, Passed: 1796, Skipped: 2, Total: 1798** (matches master baseline ~1796/2skip).
- **GameMapsLoadableTest:** `dotnet test Content.IntegrationTests --filter "FullyQualifiedName~GameMapsLoadableTest" --no-build` → **Passed! Failed: 0, Passed: 246, Skipped: 0, Total: 246**, 2m01s.
- **NC census** (utf-8-sig decode, every `meta.json` under `Resources/Textures` whose `license` contains "NC"): **49 → 4.** Remaining 4 (out of this run's scope, untouched): `Interface/Default`, `Objects/Power/powersink.rsi`, `Objects/Fun/Plushies/lamp.rsi`, `Objects/Fun/capgun.rsi`.

## Deliverables

- Verdict file: `verdict-batches-2-9.yaml` (this directory)
- Revalidation plan report: `REVALIDATION-PLAN-REPORT.md` / `.json` (this directory)
- Apply receipt (raw, from `integrate_all.py --apply`): `docs/receipts/LICENSE-REGEN-INTEGRATE-ALL-RECEIPT.json`
- Before/after plan montages, one per applied batch: `montages/production-batch-{2-artifacts,3-xeno-guardians,4-toilets,5-icononly,6-r5,7-r6,8-r7,9-r8}-plan-montage.png`

## Remaining NC debt after this run

**4 assets**: `Interface/Default` (UI hotbar glyphs, needs a dedicated icon-design pass), `Objects/Power/powersink.rsi`, `Objects/Fun/Plushies/lamp.rsi`, `Objects/Fun/capgun.rsi` — none staged in this program; next targets for a future wave.

## Held at merge gate

This branch is built and tested but **not merged and not pushed**. Per the operating instruction for this lane: only the orchestrator relays the owner's curation approval, and MERGE LAW says only Claude merges — but not on this lane's say-so alone. **HELD at merge gate pending the owner's curation verdict**, in particular a decision on the 4 BORDERLINE assets flagged above.
