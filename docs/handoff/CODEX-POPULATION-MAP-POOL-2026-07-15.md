# SOLREIGN Population-Aware Map Pool — Claude Integration Receipt

Date: 2026-07-15

Repository: GAME

Branch: `feat/codex-game-population-map-pool`

Authority: **review and integration input only; this receipt does not authorize merge, push,
deployment, restart, CVar activation, or production action**

## Integration verdict

SR-W-011 is handoff-ready on the current local GAME `master`. The branch proves the existing
population-aware selection behavior against SOLREIGN's real seven-map production pool, preserves
the existing map conditions and game-ticker checks, exercises recent-map avoidance through the
real manager, and confirms that an explicit emergency override still wins outside normal population
bands.

Claude remains the only merge operator. This branch does not change any station-map definition,
population band, rotation CVar value, deployment file, or live feature state.

## Commit and base

- Current integration base: `2e5973bf321898daea091e4e18c0e2a5f893920b`
- Initial executable/test commit: `d949916fb0` — `feat: prove population-aware Solreign map rotation`
- Independent-review test repair: `6138e04d0b` —
  `test: prove invalid map selection preserves runtime state`
- This branch was rebased cleanly onto the current base after the independent Wingmates enable-safe
  merge landed. No changed path overlaps Wingmates or Season Ledger paths.

## Exact scope

- `Content.Server/Maps/GameMapSelectionPolicy.cs` — adds a pure, content-neutral population
  predicate with a fail-closed negative-count guard.
- `Content.Server/Maps/GameMapManager.cs` — replaces only the existing inline min/max comparison
  with the policy; map conditions and `GameTicker.IsMapEligible` remain in their original order.
- `Content.IntegrationTests/Tests/_Solreign/SolreignMapPoolIntegrationTest.cs` — validates the
  production pool, every reviewed boundary, negative input, actual recent-map avoidance, and
  emergency override behavior.
- `Resources/Prototypes/_Solreign/map_pool.yml` — corrects a stale comment from six to seven maps.
- `Resources/ConfigPresets/_Solreign/solreign_rotation.toml` — corrects stale four-map comments and
  lists all seven map IDs. Runtime values are unchanged.

There are no deletions and no changes to map prototype values, station-map content, Wingmates,
Season Ledger, Director, website, database, network protocol, secrets, or public/private player data.

## Acceptance matrix

The real `SolreignMapPool` contains seven unique, resolvable prototypes. Exact eligible sets are
asserted at every band edge:

| Population | Expected eligible maps |
| --- | --- |
| 0 and 15 | Leviathan, Nocturne, Oasis, Perihelion, Verdant |
| 16 | Leviathan, Oasis, Perihelion, Verdant |
| 35 | Leviathan, Meridian, Oasis, Perihelion, Verdant |
| 36 and 69 | Leviathan, Meridian |
| 70 | Leviathan, Meridian, Terminus |
| 71 and 90 | Leviathan, Terminus |
| 91 and 100 | Terminus |

The acceptance test also proves:

- every reviewed population has at least one compatible SOLREIGN map;
- a negative population is rejected rather than cast to an unsigned value;
- two consecutive manager selections differ when alternatives exist and memory depth is one;
- `SolreignTerminus` can be selected explicitly below its normal minimum as an emergency override;
- an invalid map ID fails without replacing the last valid runtime-selected map after the emergency
  override is cleared.

## TDD evidence

- RED: the first clean compile failed because `GameMapSelectionPolicy` did not exist (`CS0103`).
- GREEN before rebase: focused map tests passed 14/14; config plus map tests passed 15/15.
- Review polish: a dedicated negative-population case was added because the new pure predicate owns
  the signed-to-unsigned boundary.
- GREEN after rebase and recompilation: config plus map tests passed 16/16.
- Independent review found that the first invalid-ID assertion was masked by the configured emergency
  map. The repaired test clears that CVar, seeds a real runtime `SolreignOasis` selection, calls the
  invalid ID, and proves Oasis remains selected; the focused map suite then passed 15/15.

## Final local verification on current base

### Focused feature and config presets, compile enabled

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-restore \
  --filter "FullyQualifiedName~ConfigPresetTests|FullyQualifiedName~SolreignMapPoolIntegrationTest" \
  --logger "console;verbosity=minimal" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `Passed 16, Failed 0, Skipped 0, Total 16` in 18 seconds.

### All SOLREIGN integration tests

```sh
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --no-build --no-restore \
  --filter "FullyQualifiedName~Content.IntegrationTests.Tests._Solreign" \
  --logger "console;verbosity=minimal" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `Passed 132, Failed 0, Skipped 1, Total 133` in 2 minutes 3 seconds. The existing
`PersistentBlockSurvivesRoundRestartAndPreventsOffer` case was the single skip.

### All SOLREIGN unit tests, compile enabled

```sh
dotnet test Content.Tests/Content.Tests.csproj --no-restore \
  --filter "FullyQualifiedName~Content.Tests._Solreign" \
  --logger "console;verbosity=minimal" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `Passed 1147, Failed 0, Skipped 2, Total 1149` in 3 seconds. The two skips are the existing
Windows-only Ledger staging ACL runtime proofs.

### Non-incremental Release server build

```sh
dotnet build Content.Server/Content.Server.csproj --no-restore --configuration Release \
  --no-incremental --verbosity minimal -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Result: `837 Warning(s), 0 Error(s)` in 54 seconds. The warnings are existing analyzer,
deprecation, and package-pruning debt; none identifies a changed feature file.

### Static and integration checks

- `git diff --check`: clean.
- branch rebase onto current local `master`: clean.
- no changed-file deletion.
- no CVar value or production activation change.
- no map-content change.

During rebase, a local post-check attempted to clone a BuildChecker dependency from GitHub and
reported sandbox DNS failure. Git completed the rebase successfully; the independent compile,
test, and Release-build receipts above ran afterward and are authoritative.

## Rollout boundary

This work is acceptance coverage and a behavior-preserving extraction, not activation. Keep
SR-W-011 out of `canary`/`proven` until the existing map-rotation configuration is intentionally
enabled through the normal operator gate and a live rotation receipt confirms map choice,
population, prior map, and rollback readiness. No such activation is authorized here.
