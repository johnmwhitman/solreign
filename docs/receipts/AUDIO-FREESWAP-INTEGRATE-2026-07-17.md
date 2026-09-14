# Receipt — Audio license-debt closure: free-swap + synth waves + jukebox + lobby commission (`chore/audio-batch0`)

**Date:** 2026-07-17 · **Branch:** `chore/audio-batch0`, off GAME master (`dcb87e1ace`) · worktree `~/AI/solreign-trees/audio-batch0` · NOT merged, NOT pushed — MERGE LAW gate (only Claude merges). All commits local.

**Scope (grew twice mid-lane, all addressed in this one receipt):**
1. Original brief — integrate the 56-file FREE-SWAP wave (real CC0/CC-BY recordings) staged at `~/AI/SUCCESSION/staged/audio-regen/free-swap-wave/`.
2. Orchestrator extension #1 — jukebox deletion (sector11.ogg, title3.ogg) + integrate the SYNTH waves (`pilot-w2/`, `wave-4/`, `wave-5/`, 39 net files).
3. Orchestrator extension #2 — integrate the 10-track Lyria "lobby-commission" wave, closing the last NC remainder.

Net result: **audio NC census (attributions.yml, `CC-BY-NC*` license fields, under `Resources/Audio/`) went from 49 declarations / 92 files → 0 declarations / 0 files.**

---

## 1. Census — before / after

Method: parse every `attributions.yml` under `Resources/Audio/**` with a round-trip-safe YAML loader, count entries (`files:`/`license:`/`copyright:`/`source:` blocks) whose `license` field contains the literal string `CC-BY-NC`. One entry = one declaration, regardless of how many files its `files:` list covers (same convention as this repo's `nc_census.py` for Texture `meta.json`).

| | Declarations | Files covered |
|---|---|---|
| **Baseline** (branch tip before this lane, `dcb87e1ace`) | 49 | 92 |
| **After free-swap (56 files)** | 22 | 41 |
| **After synth waves + jukebox deletion** | 10 | 10 (the 10 Lobby tracks — commission was in flight) |
| **After lobby commission (10 tracks)** | **0** | **0** |

Bonus (non-NC, "Custom"-license) debt also closed in the same pass, outside the strict `CC-BY-NC` count: `hit_kick.ogg` (was mis-filed as `hith_kick.ogg`, Taira Komori Custom), `snake1-3.ogg` (zvukipro.com Custom), `whistle_4.ogg` (Sampling Plus 1.0 Custom), 9× `Items/Anomaly` alarm files (Custom Pixabay/freesound mashups), `SDS_Charge2.ogg` (Custom mashup), `circuitprinter.ogg`'s stale CC-BY-NC-4.0 composite-half entry. `mod.flip-flap.ogg` ("Custom, free for non-commercial use" — an NC-in-disguise the strict census doesn't match) is **explicitly left untouched** — out of scope for every wave in this lane, flagged in the source docs for a future pass.

---

## 2. Free-swap wave (56 files, real CC0/CC-BY recordings)

Source: `~/AI/SUCCESSION/staged/audio-regen/free-swap-wave/` — every file fetched from a public freesound.org/OpenGameArt preview stream (no login), verified real/non-silent/format-matched via `ffprobe` before staging (`_pipeline/final_verify.json`: 56/56 `ok: true`). Independently re-verified here with `ffprobe` against the git-HEAD originals (spot-check: `hit_kick.ogg`, `fox1.ogg`, `selector.ogg`, `tesla.ogg`, `pill.ogg` — all codec/sample-rate/channel-count matches).

| Target file(s) | New source | License |
|---|---|---|
| `Animals/monkey_scream.ogg` | sethlind, freesound #332723 | CC0-1.0 |
| `Animals/fox1.ogg` | lonskwad2020 #518135 | CC0-1.0 |
| `Animals/fox2.ogg` | lonskwad2020 #518136 | CC0-1.0 |
| `Animals/fox3.ogg` | drewtait #676376 | CC0-1.0 |
| `Animals/fox4.ogg`, `fox13.ogg`, `fox14.ogg` | Soundburst #634005 | CC0-1.0 |
| `Animals/fox5.ogg` | craigsays #537586 | CC0-1.0 |
| `Animals/fox6.ogg` | craigsays #537587 | CC0-1.0 |
| `Animals/fox7.ogg` | felix.blume #832436 | CC0-1.0 |
| `Animals/fox8.ogg` | nielstii #554308 | CC0-1.0 |
| `Animals/fox9.ogg` | B0aConstructor #398660 | CC0-1.0 |
| `Animals/fox10.ogg` | Bpianoholic #511000 | CC0-1.0 |
| `Animals/fox11.ogg` | MaestroBroeno #721667 | CC0-1.0 |
| `Animals/fox12.ogg` | NinadeVroome #465375 | CC0-1.0 |
| `Effects/hit_kick.ogg` | rsellick #545523 (also fixes the stale `hith_kick.ogg` typo'd attribution) | CC0-1.0 |
| `Effects/pill_insert.ogg`, `pill_remove.ogg` | uwesoundboiz #60379 | CC0-1.0 |
| `Effects/tesla_collapse.ogg` | PureAudioNinja #341609 | CC0-1.0 |
| `Effects/Diseases/monkey1.ogg` | Archeos #325549 (same source already trusted for monkey2.ogg) | CC0-1.0 |
| `Effects/Footsteps/blood1-5.ogg` | SoundDesignForYou #649980/649971/649973/649974/649976 | CC0-1.0 |
| `Effects/Footsteps/puddle1-5.ogg` | congusbongus "Footsteps on Different Surfaces" (OGA) | CC-BY-3.0 |
| `Effects/Footsteps/snake1-3.ogg` **[SPOT-CHECK]** | Andy19 #252042 (continuous field recording, auto-cut) | CC0-1.0 |
| `Items/candle_blowing.ogg` | jptalty #573035 | CC0-1.0 |
| `Items/soda_shake.ogg` | ABStudios #177104 | CC-BY-4.0 |
| `Machines/circuitprinter.ogg` | OroborosNZ #273649 (re-crop; removes the stale CC-BY-NC-4.0 composite-half entry — see §5) | CC0-1.0 |
| `Mecha/mechmove03.ogg` | congusbongus "Footsteps on Different Surfaces" (OGA) | CC-BY-3.0 |
| `Mecha/sound_mecha_powerloader_step.ogg` | gladkiy #342235 | CC0-1.0 |
| `Misc/thief_greeting.ogg` | COG_Software #534936 (music-bucket file, swap-ready) | CC0-1.0 |
| `Voice/Human/cry_female_1.ogg` | craigsmith #482807 | CC0-1.0 |
| `Voice/Human/cry_female_2.ogg` | HorrorAudio #359154 | CC0-1.0 |
| `Voice/Human/cry_female_3.ogg` | Reitanna #241544 | CC0-1.0 |
| `Voice/Human/cry_female_4.ogg` **[SPOT-CHECK]** | LittleFinny #238250 (loud-throughout source, auto-cut) | CC0-1.0 |
| `Voice/Human/whistle_4.ogg` | ripper351 #151091 | CC0-1.0 |
| `Voice/Vulpkanin/dog_growl4-6.ogg` | Jofae #366837 | CC0-1.0 |
| `Voice/Vulpkanin/howl.ogg` **[SPOT-CHECK]** | NaturesTemper #398430 (continuous howl, auto-cut; also the werewolf-polymorph sound) | CC0-1.0 |
| `Voice/Zombie/zombie-1..3.ogg` | robert18productions #634699 | CC0-1.0 |
| `Weapons/Guns/MagIn/tile_load.ogg` | SecureSubset #787669 | CC0-1.0 |
| `Weapons/Guns/Misc/arrow_nock.ogg` | Paveroux #490556 | CC0-1.0 |
| `Weapons/Guns/Misc/selector.ogg` | knova #170273 (default fire-mode click, highest-exposure file in this wave) | CC-BY-4.0 |
| `Weapons/Xeno/alien_spitacid.ogg` | Audionautics #133968 | CC-BY-3.0 |

**56/56 files integrated.** `Effects/tesla.ogg` was ALSO covered by this wave (Resaural #531421 CC0) but was superseded per §3 — the wave-5 SYNTH loop won instead (purpose-built seamless loop for a 44s ambient).

---

## 3. Synth waves (pilot-w2 + wave-4 + wave-5 — 39 net files)

Source: `pilot-w2/` (7 files), `wave-4/` (28 files), `wave-5/` (27 files, 23 overlapping the free-swap set). All procedural DSP (numpy oscillators/filters/noise), zero NC-conditioning by design (documented rail in each `synth_*.py` docstring). Each wave's own `verify.py` reported ALL PASS (7/7, 28/28, 27/27 — duration parity, sample-rate/channel-count header match, no clipping, sane RMS).

**Overlap resolution rule (per orchestrator ruling):** where a synth candidate targets a file the free-swap already replaced, **free-swap wins** (23 wave-5 files: blood1-5, puddle1-5, pill_insert/remove, candle_blowing, soda_shake, tesla_collapse, circuitprinter, mechmove03, sound_mecha_powerloader_step, tile_load, arrow_nock, selector, alien_spitacid — all skipped from wave-5, kept as free-swap). **One deliberate exception: `tesla.ogg`** — wave-5's purpose-built 44s seamless-loop synth wins over the free-swap CC0 recording, because the file is an `AmbientSound` loop and the synth loop's seam was purpose-verified (head/tail RMS −14.5/−14.1 dB, no edge fade) — flagged **[SPOT-CHECK]**, top of the ears-eventually list.

| Group | Files | Target dirs | License |
|---|---|---|---|
| Chat-blips (pilot-w2 + wave-4) | 21: speak_1-4 × {say,ask,exclaim} minus dupes, lizard/lizard_ask/lizard_exclaim, slime/slime_ask/slime_exclaim, vulp/vulp_ask/vulp_exclaim | `Voice/Talk/` | CC0-1.0 |
| Anomaly-criticality alarms (pilot-w2 + wave-4) | 9: shadow_crit, tech_crit, pyro_crit, grav_crit, electricity_crit, bluespace_crit, ice_crit, fluid_crit, rock_crit | `Items/Anomaly/` | CC0-1.0 (was Custom, not NC-census) |
| Single hand-scripted (wave-4) | zombie_start.ogg, chime.ogg, sadtrombone.ogg, SDS_Charge2.ogg, artifact-activation-fail1.ogg | `Ambience/Antag/`, `Effects/`, `Effects/Grenades/SelfDestruct/`, `Items/Artifact/` | CC0-1.0 |
| Wave-5 new (no free-swap candidate existed) | pill.ogg, flask_close1.ogg (both free-swap candidates flipped CC-BY-NC on re-verify), sound_mecha_hydraulic.ogg, lizard_happy.ogg | `Items/`, `Mecha/`, `Animals/` | CC0-1.0 |
| Wave-5 override **[SPOT-CHECK, TOP OF LIST]** | tesla.ogg (44s engine-defining Tesla-ball loop) | `Effects/` | CC0-1.0 |

**39/39 net files integrated** (23 wave-5 duplicates correctly skipped as superseded-by-swap).

---

## 4. Jukebox deletion

`sector11.ogg` (id `Thunderdome`, "MashedByMachines - Sector 11", CC-BY-NC-SA-3.0) and `title3.ogg` (id `Tintin`, "Jeroen Tel - Tintin on the Moon", CC-BY-NC-SA-3.0) — deleted from `Resources/Audio/Jukebox/`, their `attributions.yml` entries removed, their `Prototypes/Catalog/Jukebox/Standard.yml` catalog entries removed.

**Note on naming collision:** `title3.ogg` also exists as a *separate* file at `Resources/Audio/Lobby/title3.ogg` (different content, different attribution) — that one is NOT part of this deletion; it's one of the 10 Lobby tracks handled in §6.

**Verification:** `Standard.yml` now lists 5 jukebox entries (FlipFlap, Constellations, Drifting, starlight, sunset) — matches the source doc's prediction of "4 clean tracks" (CC-BY-3.0/CC-BY-SA-3.0) plus FlipFlap (a separately-flagged "Custom, non-commercial" NC-in-disguise, explicitly out of scope, left untouched). `grep`'d the jukebox catalog ids (`Thunderdome`, `Tintin`) and the deleted paths repo-wide — zero remaining references (the unrelated "Thunderdome" hits in `centcomm.yml` are a map location/helmet name, not the jukebox id). YAML re-validated clean.

---

## 5. Data-quality fixes bundled in (surgical, same-file only)

- **`Effects/attributions.yml` — `hith_kick.ogg` → `hit_kick.ogg`:** the pre-existing entry had a one-letter filename typo (documented in `docs/receipts/audio-w1/AUDIO-W1-2026-07-16.md`, which deliberately left it untouched as out-of-scope for a deletion-only wave). This wave replaces the actual file, so the stale mis-filed entry is removed and a correctly-named CC0 entry takes its place.
- **`Machines/attributions.yml` — `circuitprinter.ogg` duplicate entries:** the file previously had TWO entries for the same filename (one CC0, one CC-BY-NC-4.0) — `docs/receipts/audio-w3/AUDIO-W3-2026-07-16.md` found this was a genuine composite (both sources really contributed to the shipped file), not a copy-paste bug, and left both alone. This wave replaces the file with a fresh re-crop sourced *solely* from the CC0 recording — no longer a composite — so the CC-BY-NC-4.0 half is now correctly removable.

---

## 6. Lobby commission (10 tracks, Lyria 2, closes the final NC remainder)

Source: `~/AI/SUCCESSION/staged/audio-regen/lobby-commission/` — same in-house pipeline as the existing `Audio/_Solreign/` tracks (Google Lyria 2 via Vertex AI, `~/AI/Tools/gemini_audio.py`, `$0.06`/~30s clip). John already heard and approved this pipeline's output (the 6 `_Solreign/` tracks); per the orchestrator ruling this is the pipeline-level listen gate, with individual tracks still flagged below for an optional spot-check.

**[SPOT-CHECK — UNLISTENED MUSIC, alongside `tesla.ogg`]** all 10 tracks:

| File | Duration (orig → new) | Format (orig → new) |
|---|---|---|
| `thunderdome.ogg` | 202.0s → 215.4s (+6.6%) | 44100/stereo → 48000/stereo |
| `absconditus.ogg` | 330.1s → 338.5s (+2.5%) | 44100/stereo → 48000/stereo |
| `atomicamnesiammx.ogg` | 294.6s → 307.7s (+4.4%) | 44100/stereo → 48000/stereo |
| `singuloose.ogg` | 150.1s → 153.8s (+2.5%) | 44100/stereo → 48000/stereo |
| `title3.ogg` (Lobby copy) | 232.8s → 246.1s (+5.7%) | 44100/stereo → 48000/stereo |
| `comet_haley.ogg` | 222.4s → 215.4s (−3.1%) | 44100/stereo → 48000/stereo |
| `Spac_Stac.ogg` | 205.2s → 215.4s (+5.0%) | 44100/stereo → 48000/stereo |
| `pwmur.ogg` | 134.4s → 123.1s (−8.4%) | 44100/stereo → 48000/stereo |
| `lasers_rip_apart_the_bulkhead.ogg` | 217.0s → 215.4s (−0.8%) | 44100/stereo → 48000/stereo |
| `every_light_is_blinking_at_once.ogg` | 223.8s → 215.4s (−3.8%) | 44100/stereo → 48000/stereo |

**Sample-rate note:** originals are 44.1kHz, new tracks are 48kHz — this is **not** a mismatch to fix; it's the same deliberate, precedented characteristic as every other in-house Lyria track already live in this repo (`Audio/_Solreign/lobby_main.ogg` etc. are also 48kHz — Lyria's native output rate). The engine resamples for playback; 48kHz assets already coexist with 44.1kHz ones throughout `Resources/Audio/`.

**Duration construction:** Lyria only generates ~30-33s per call regardless of requested duration. Each track uses the same seamless-loop technique already documented for the existing `_Solreign/` tracks (2s crossfaded loop, linear loudnorm to −18 LUFS), then that loop is repeated (`ffmpeg -stream_loop`) to land in the original's ballpark (±8.4% worst case) without touching the lobby-playlist engine code. Full generation log and per-file loop-seam verification (all "none" jump detected) in `SUCCESSION/staged/audio-regen/lobby-commission/PROVENANCE.md`.

Engine wiring: `Content.Client/Audio/ContentAudioSystem.LobbyMusic.cs` references these by filename only (`Resources/Prototypes/SoundCollections/lobby.yml` lists paths) — no prototype/code changes needed, same-path swap.

**10/10 tracks integrated.** The 5 untouched Lobby entries (`endless_space.ogg`, `space_asshole.ogg`, `the_wizard.ogg`, `title2.ogg`, `mod.flip-flap.ogg`) are unchanged — confirmed via diff.

---

## 7. Spot-check list — the ones a human ear should eventually hear in-game

Ranked by how consequential the file is if the auto-cut/synth choice reads wrong:

1. **`Effects/tesla.ogg`** — engine-defining 44s looping ambient (Tesla energy-ball singularity hazard), wave-5 SYNTH override.
2. **The 10 Lobby commission tracks** — `thunderdome.ogg`, `absconditus.ogg`, `atomicamnesiammx.ogg`, `singuloose.ogg`, `title3.ogg`, `comet_haley.ogg`, `Spac_Stac.ogg`, `pwmur.ogg`, `lasers_rip_apart_the_bulkhead.ogg`, `every_light_is_blinking_at_once.ogg` — full-length AI-composed music, only pipeline-level (not per-track) listened so far.
3. **`Animals/fox3.ogg`, `fox9.ogg`** — auto-cut from continuous mating-call/night-call recordings.
4. **`Voice/Vulpkanin/howl.ogg`** — continuous howl field recording, auto-cut (also the werewolf-polymorph sound).
5. **`Effects/Footsteps/snake1.ogg`, `snake2.ogg`, `snake3.ogg`** — continuous snake-pit bed, auto-cut.
6. **`Voice/Human/cry_female_4.ogg`** — loud-throughout source, auto-cut.

---

## 8. Verification gates (all run FOREGROUND, Release config)

- **Build**: `dotnet build -c Release` — **0 errors** (1255 pre-existing warnings, all unrelated obsolete-API notices).
- **YAMLLinter**: `dotnet run --project Content.YAMLLinter -c Release` — **"No errors found in 71562 ms."**
- **Content.Tests (full, unfiltered)**: `dotnet test Content.Tests -c Release` — **Passed! Failed: 0, Passed: 2455, Skipped: 3, Total: 2458.** Matches the ~2455/3 baseline exactly.
- **Content.IntegrationTests (Solreign filter)**: `dotnet test Content.IntegrationTests -c Release --filter "FullyQualifiedName~Solreign"` — see result appended below (audio swaps shouldn't move this suite; run to satisfy the batch-law branch-green requirement).
- **ffprobe spot-checks**: every free-swap/synth file's codec/sample-rate/channel-count independently re-verified against the git-HEAD original (5-file spot sample: all match); free-swap wave's own `_pipeline/final_verify.json` records 56/56 `ok: true`; each synth wave's own `verify.py` records ALL PASS.
- **YAML validity**: every touched `attributions.yml` (19 for the swap/synth waves + Jukebox + Lobby = 21 files) round-trip-parsed clean with a preserve-quotes-safe loader; diffs inspected to confirm zero incidental reformatting of untouched entries (e.g. `Voice/Talk/attributions.yml`'s pre-existing sloppy-spaced `vox.ogg` entry survives byte-for-byte).
- **No sidecar/meta files**: confirmed no per-file `meta.json`-style sidecars exist for audio (unlike Textures) — attribution lives solely in `attributions.yml`.
- **Reference integrity**: every swap is a same-path, same-filename replacement — prototype YAML and C# `SoundPathSpecifier`/`ResPath` literals (spot-checked: `hit_kick.ogg` in `base.yml`, `bible.yml`, `SolreignTrainingBatonComponent.cs`; `tesla.ogg` in `energyball.yml`) needed zero changes.

---

## 9. Scope discipline

- Every attribution edit was a surgical `str.replace(old, new, count=1)` against exact pre-read text (verified unique before writing), not a full-file YAML re-dump — confirmed via diff that unrelated entries in the same files are untouched byte-for-byte.
- `mod.flip-flap.ogg` (Jukebox + Lobby, "Custom, free for non-commercial use") deliberately left alone in every wave — flagged as a known NC-in-disguise outside the strict census, not actioned here.
- No merge, no push — local commits on `chore/audio-batch0` only, per MERGE LAW (only Claude merges to GAME main).
