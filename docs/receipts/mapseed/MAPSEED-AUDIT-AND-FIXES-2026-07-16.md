# Mapseed audit + minimal fixes — 2026-07-16

Branch: `feat/mapseed-fixes`, worktree `~/AI/solreign-trees/mapseed-fixes`, forked from `master` @
`126e91779e` (post `feat/mood-rollout` + `feat/solreign-parallax` merges). No push, no merge —
Claude remains the only merge operator per repo convention.

## Mission

The owner wants every Solreign capability/item properly seeded on every rotation map. Phase 1
(read-only) built a coverage matrix for Solreign content x the 7 `SolreignMapPool` rotation maps
(Oasis, Nocturne, Perihelion, Verdant, Meridian, Leviathan, Terminus), parsing the map YAML
programmatically instead of eyeballing 7 large files. Full audit doc with the matrix + gap list +
reachability cross-checks:
`/private/tmp/claude-501/-Users-johnwhitman-AI/d4343975-777a-4f1c-89c4-58e26c7f041a/scratchpad/MAPSEED-AUDIT-2026-07-16.md`
(and its two reusable parser scripts, `audit_mapseed.py` + `audit_egg_reachability.py`, in the same
directory).

## Cross-referenced against SR-W-012 (map-health scorecard, merged `993c323375`)

That scorecard asserts 6 structural checks (station grid, latejoin spawns, job spawns, emergency
shuttle, APC load, spawn-tile atmosphere) — it never touches content-seeding. This work is additive,
not overlapping.

## What the audit found (summary — see the full doc for the matrix and every gap)

- v11 front doors (Directive Terminal, Requisitions Anonymous, Liability Board) and the v13
  Wingmate Beacon: **confirmed present, exactly 1 each, on all 7 maps.** Receipts were accurate.
- `SolreignStationMood` (day/night + ambience + weather-event preference) was **missing on
  Nocturne and Verdant** at audit start — confirmed by reading each map's `stations:` block. **This
  was resolved mid-audit by a concurrent session's `feat/mood-rollout` merge** (`5dbaacca05`, landed
  while Phase 1 was in progress) — re-verified after that merge: all 7 maps now carry the component.
  No Phase-2 action needed.
- EasterEggs: of 32 distinct video-game-homage items (20 `SolreignEgg01-20` + 12 legacy), only
  `SolreignEgg01` was directly map-baked anywhere (Oasis). The other 31 are distributed via global,
  Solreign-map-agnostic vending-machine inventory packs (by design). Cross-referencing which of the
  21 relevant vending-machine *types* each of the 7 maps actually contains surfaced real
  reachability holes: `SolreignSodaCan` had zero reachability on **any** of the 7 maps
  (`VendingMachineSoda` absent everywhere); `SolreignEgg08` was missing on 5/7 (only Leviathan and
  Terminus carry `VendingMachineWallMedical`); Nocturne was the worst-served map overall (20/32
  reachable vs. 24-30/32 elsewhere).
- Season-1 "Ledger Wakes" lore trail (8 Auditor Memos + 20 Handbook pages + Vault Reward Crate):
  **Oasis has the complete trail; Meridian has only the 20 handbook pages; Leviathan has only 2 of
  8 memos; Nocturne, Perihelion, Verdant, Terminus have none of it at all** — 4 of 7 maps (57%)
  never expose this content on a random-map round. Highest player-impact gap found; **deliberately
  not attempted here** — it's a full mapping-wave-sized effort per its own source comment
  ("mapping wave places memos along the trail"), not a minimal fix.
- Cake-showcase companion sign (`SolreignSignRewardPending`) was present only on Oasis; the other
  6 maps had the filled cake showcase itself but not its signage companion.

## Concurrency check (v11 concurrent-lane law: disjoint tiles/uid ranges, keep-both on conflicts)

Checked `feat/wow-wiring` and `feat/mood-rollout` at Phase-1 start: both sat at their fork point
with zero committed diffs. **Two lanes advanced and merged into master while this audit was in
progress**:
- `feat/mood-rollout` (`5dbaacca05`) — touched 5 map **config** files
  (`Resources/Prototypes/Maps/_Solreign/solreign_{oasis,nocturne,verdant,meridian,leviathan}.yml`)
  + 2 `.ftl` files. Resolved the mood gap above; **Phase 2 does not touch these 5 files again.**
- `feat/solreign-parallax` (`7bcad13751`) — touched 4 map **data** files
  (`Resources/Maps/_Solreign/solreign_{leviathan,meridian,oasis,terminus}.yml`), each a 2-line
  `Parallax` component addition on the map-root grid entity near the top of the entities list (not
  a new top-level entity block). This worktree was created from master *after* both merges, so
  there is no branch-divergence conflict — Phase 2's edits to those same 4 data files are new
  top-level `- proto:` blocks appended near each file's existing tail (disjoint region from the
  parallax component patch, fresh uids past each file's actual current max).

`feat/wow-wiring` remained at its fork point (zero diff) through Phase 2 — no files skipped due to
that lane.

## Phase 2 fixes applied (branch `feat/mapseed-fixes`)

1. **Cake-showcase companion sign, 6 maps** (Nocturne, Perihelion, Verdant, Meridian, Leviathan,
   Terminus) — added `SolreignSignRewardPending` one tile from each map's existing, already-placed
   `SolreignCakeShowcaseFilled`/`SolreignCompanionCubeStructure`/contracts-board neighbors, inheriting
   those neighbors' already-verified floor safety (same idiom Oasis itself uses: its own sign sits
   one tile south of its own showcase). Fresh uids past each file's actual max; zero duplicate uids
   introduced (verified via `grep -o "uid: [0-9]*" | sort | uniq -d` per file, empty on all 6).
2. **EasterEgg desert rebalance, 2 items, capped per the task's "no sweeping map surgery" ceiling**:
   - `SolreignSodaCan` baked onto **Nocturne** (closes a gap that was otherwise unreachable on
     *any* of the 7 maps — the highest-value single placement available).
   - `SolreignEgg08` baked onto **Perihelion** (closes a gap that was otherwise unreachable on 5 of
     7 maps). Both placed adjacent to already-verified-safe floor tiles (companion cube / cake
     showcase / dropbox neighbors), continuing an established open-floor row rather than guessing
     an unverified tile.
3. **Doc-hygiene**: fixed the stale header comment on
   `Resources/Prototypes/_Solreign/PlayerDelight/wingmate_beacon.yml`, which still claimed the
   beacon was "deliberately absent from maps and inventories" — untrue since the 2026-07-15
   Wingmates rollout wave placed one on all 7 maps (independently corroborated by
   `SolreignWingmateBeaconMapPlacementIntegrationTest.cs`). Comment-only, zero behavior change.
4. **Scorecard extension** (new file, closes the recurrence risk the audit flagged):
   `Content.IntegrationTests/Tests/_Solreign/SolreignMapContentSeedIntegrationTest.cs`. Reuses
   `SolreignMapTestCatalog` and the same "largest station-member grid" resolution
   `SolreignMapHealthScorecardIntegrationTest`/`SolreignWingmateBeaconMapPlacementIntegrationTest`
   already use. Asserts, per map: exactly one Directive Terminal (`SolreignOracleComponent`),
   exactly one market console (`SolreignMarketConsoleComponent`), exactly one bounty board
   (`SolreignBountyBoardComponent`), exactly one filled cake showcase (prototype-id match, no
   dedicated marker component exists for it), and that the station entity carries
   `SolreignStationMoodComponent`. If any future edit drops one of these again (as Nocturne/Verdant
   briefly did for the mood component), CI now fails instead of silently drifting.

## Deferred (out of "minimal fix" scope, flagged for a dedicated follow-up wave)

- Season-1 lore trail completion on the 4 fully-barren maps + partial-trail maps (Meridian,
  Leviathan) — full mapping-wave-sized effort, not a 1-2 line patch.
- Remaining EasterEgg reachability gaps beyond the 2 fixed above (`VendingMachineSnack` absent on
  4/7 maps orphaning 3 eggs each there; `VendingMachineWinter` absent on Meridian; Nocturne's other
  6 missing vendor types) — adding a missing vending-machine department fixture is real map/power
  surgery, not a minimal content-only patch, and risks collision with `feat/wow-wiring`'s stated
  scope (asset/prop wiring).

## Verification (this worktree, `feat/mapseed-fixes`)

```
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -p:UseSharedCompilation=false
  -> 0 errors, 96 warnings (pre-existing, unrelated to this change)

dotnet run --project Content.YAMLLinter/Content.YAMLLinter.csproj -p:UseSharedCompilation=false
  -> No errors found in 51745 ms.

dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~Tests._Solreign" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
  -> Passed: 176, Failed: 0, Skipped: 1, Total: 177  (8 m 27 s)

dotnet test ... --filter "FullyQualifiedName~SolreignMapContentSeedIntegrationTest" ...
  -> Passed: 7, Failed: 0, Total: 7  (new scorecard extension, all 7 maps green)

dotnet test ... --filter "FullyQualifiedName~SolreignMapHealthScorecardIntegrationTest|
  FullyQualifiedName~SolreignMapPoolIntegrationTest|
  FullyQualifiedName~SolreignWingmateBeaconMapPlacementIntegrationTest" ...
  -> Passed: 33, Failed: 0, Total: 33  (existing scorecard/pool/beacon suites unaffected)

dotnet test ... --filter "FullyQualifiedName~PostMapInitTest.GameMapsLoadableTest|
  FullyQualifiedName~StationPowerTests" ...
  -> Passed: 38, Failed: 0, Total: 38  (matches the SR-W-012 baseline exactly)
```

All green. No production behavior changed except the additive map-content placements themselves
(new sign/egg entities, and the pre-existing wingmate comment fix, which is comment-only).

## Files changed

```
Resources/Maps/_Solreign/solreign_leviathan.yml      (+ sign, fresh uid 900613)
Resources/Maps/_Solreign/solreign_meridian.yml       (+ sign, fresh uid 900564)
Resources/Maps/_Solreign/solreign_nocturne.yml       (+ sign uid 3534, + SolreignSodaCan uid 3535)
Resources/Maps/_Solreign/solreign_perihelion.yml     (+ sign uid 900554, + SolreignEgg08 uid 900555)
Resources/Maps/_Solreign/solreign_terminus.yml       (+ sign, fresh uid 90544)
Resources/Maps/_Solreign/solreign_verdant.yml        (+ sign, fresh uid 900614)
Resources/Prototypes/_Solreign/PlayerDelight/wingmate_beacon.yml   (comment fix only)
Content.IntegrationTests/Tests/_Solreign/SolreignMapContentSeedIntegrationTest.cs   (new)
docs/receipts/mapseed/MAPSEED-AUDIT-AND-FIXES-2026-07-16.md        (this file)
```
