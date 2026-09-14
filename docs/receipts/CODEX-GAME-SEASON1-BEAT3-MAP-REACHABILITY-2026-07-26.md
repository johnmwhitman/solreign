# Codex GAME Season 1 Beat 3 exact-map receipt — 2026-07-26

## Verdict

`PASS-AS-LOCAL-EXACT-MAP-EVIDENCE / CLAUDE INTEGRATION HOLD`

The existing manually started `SolreignGhostCabinets` rule delivered exactly
one curated transcript folder on all seven canonical SOLREIGN rotation maps.
The fixture also censuses every station-owned storage candidate admitted by
the production rule and proves each candidate belongs to the exact loaded map.

This is delivery-mechanics evidence, not proof that players can discover the
folder, that the event is Showrunner-eligible, or that it is ready for
activation. It does not authorize a canonical merge, push, package, deploy,
restart, event activation, launcher/hub change, credential use, or production
mutation. Claude remains the sole integrator.

## Exact identity

- Branch:
  `codex/game-season1-beat3-map-reachability-v1-20260726`
- Worktree:
  `/Users/johnwhitman/AI/solreign-trees/codex-game-season1-beat3-map-reachability-v1-20260726`
- Tested code base:
  `70eb235834d1e43e2e7500e9b8dbbbeb64273df0`
- Beat 3 map-test commit:
  `8b55bf1fcb2dd7833401129380d507369cf8c26d`
- Beat 3 map-test tree:
  `3fdcaa4b4777aab60ca15751d10dc53f823a913f`
- Strict-build repair dependency:
  `79fb230c10e695f97daea7055fdc9b34703135df`
- Conflict-free tested composition tree:
  `697b79a3ba67a62ca007e6ee30a901b229c04b8b`
- Disposable composition commit:
  `c4bb6e70f8c8abce0625b46e1db87183e7d26ce2`
- RobustToolbox:
  `960edb32c4dd417496e4667177625d8c3cb14f7e`
- Test-source SHA-256:
  `961d114dbd1793f502d924f59a2ba688bce076f4e66578f7250451fcbbdab2e0`
- Beat 3 binary-diff SHA-256:
  `9fc32551b0874193bc2965e7f942b86b276f8f0dc04e6f6262415e574291eb22`

During the full battery, cached `origin/master` advanced to
`14a3c7a01b90cec8205b12504480bcb32df1e774`, adding only
`docs/receipts/BUG-LEDGER-2026-07-26.md`. A conflict-free preview of that
docs-only head plus the Beat 3 commit produced tree
`69f9706f9ca7858ef78ca9d6da3e505dfa5c7d7c`. The test results remain bound to
the exact code base and tested composition above; Claude should recompose on
the then-current head.

## What the fixture proves

For every ID in the shared `SolreignMapTestCatalog.MapIds`:

1. the real `GameMapPrototype` loads through `GameTicker.LoadGameMap`;
2. exactly one event-eligible loaded station is available to the random station
   selector;
3. every station-owned `EntityStorageComponent` accepted by the production
   `CanInsert` predicate is on the exact loaded map and one of its grids;
4. manually starting the real rule creates exactly one new folder;
5. the folder's text is one of the four curated
   `Season1GhostCabinetTranscripts` values; and
6. the exact-map run realized `Inserted` on all seven maps and proved both
   sides of each container relationship.

The fixture also contains fail-closed assertions for a realized
`DroppedAdjacent` result: it must be uncontained but remain on an exact-map,
station-owned grid. That branch is exercised separately by the pre-existing
synthetic open-storage test in the full SOLREIGN band, not by the recorded
seven-map run.

The candidate census uses a temporary real folder-prototype probe and deletes
it in a `finally`. Rule termination and map deletion are also guarded so map
cleanup is attempted even if rule termination throws.

## Map census

One normal-output run observed:

| Map | Admitted storage candidates | Open candidates | Realized result |
| --- | ---: | ---: | --- |
| Leviathan | 437 | 8 | `Inserted` |
| Meridian | 274 | 8 | `Inserted` |
| Nocturne | 44 | 2 | `Inserted` |
| Oasis | 172 | 1 | `Inserted` |
| Perihelion | 225 | 4 | `Inserted` |
| Terminus | 374 | 8 | `Inserted` |
| Verdant | 225 | 4 | `Inserted` |

Candidate counts are planning evidence from that isolated run, not a stable
content API. The existence of open candidates matters: the production storage
API can validly return `DroppedAdjacent`. The pre-existing synthetic test
exercises that outcome, while the exact-map fixture will validate its map and
station provenance if random selection realizes it in a future run.

## Verification

All final build and test work was serialized with `-m:1`,
`-nodeReuse:false`, and `-p:UseSharedCompilation=false`.

| Gate | Result |
| --- | --- |
| Strict integration-project build on exact composition | **355 warnings / 0 errors** in 22.02s |
| Focused seven-map fixture on exact composition | **7 passed / 0 failed / 0 skipped** in 43s |
| Full `Content.Tests` on exact composition | **2,935 passed / 0 failed / 3 skipped / 2,938 total** in 6s |
| Full `FullyQualifiedName~Solreign` integration band | **494 passed / 0 failed / 4 skipped / 498 total** in 13m20s |
| `git diff --check` | clean |

The three unit skips were the existing `TestAlertManager` skip and two
Windows-only ACL skips. The four integration skips were the expected
low-pop-disabled, inspection-disabled, authenticated-lifecycle, and persistent
block scenarios.

The source-first ladder caught and closed:

- one missing `Robust.Shared.Localization` import during the intermediate
  compile; and
- two nullable prototype-ID comparisons surfaced by the strict analyzer
  composition.

One redundant back-to-back build produced a local XAML/PDB file-contention
failure. A subsequent isolated serialized build passed at 355 warnings and zero
errors; the contention result is not treated as a source regression.

## Claim limits and next decision

This receipt does not prove:

- player visibility, pathfinding, access, search time, story recall, or
  discoverability;
- that every candidate will produce the same insertion postcondition;
- clue cleanup, restart behavior, persistence, Chronicle consequences, or
  cross-round history;
- automatic scheduling, Showrunner eligibility, CVar behavior, preview,
  confirmation, or operator safety;
- maps outside the canonical seven;
- full upstream-wide integration, YAML/prototype lint, Release packaging,
  Windows CI, launcher join, hub listing, live round, or deployment behavior.

The next player-value decision is not another delivery fix. It is how much of
the mystery to reveal at low population: department-scale diegetic hint,
records-hub preference, or a dedicated spectral-cabinet anchor. That choice
should be reviewed before changing maps or runtime selection.
